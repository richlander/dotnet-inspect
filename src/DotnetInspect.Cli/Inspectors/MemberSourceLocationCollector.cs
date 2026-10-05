using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using Inspector.Findings;
using ILInspector.Metadata;
using NuGetFetch;

using SourceLinkDocument = ILInspector.SourceLink.SourceDocument;

namespace DotnetInspect.Cli.Inspectors;

internal static class MemberSourceLocationCollector
{
    internal sealed record Result(
        string? PdbPath,
        IReadOnlyDictionary<ApiMember, MemberSourceObservation> Mappings);

    public static async Task<Result> EnrichAsync(
        ApiType apiType,
        string assemblyPath,
        ResolvedAssemblyReference? sourceAssembly,
        ApiSourceResult source,
        MemberOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        var collected = new Dictionary<ApiMember, MemberSourceObservation>(ReferenceEqualityComparer.Instance);
        try
        {
            bool usePlatformSettlement =
                sourceAssembly?.Provenance
                    is AssemblyResolutionProvenance.PlatformAsset;
            PackageSourceLinkSession? packageSession =
                usePlatformSettlement
                    ? null
                    : await TryOpenPackageSettlementAsync(
                        apiType,
                        assemblyPath,
                        sourceAssembly,
                        source,
                        options,
                        httpClient,
                        logger);
            SourceLinkService? packageService =
                packageSession?.Service;
            using var service =
                packageService
                ?? (usePlatformSettlement
                    ? SourceLinkService.OpenEmbeddedPdbOnly(
                        sourceAssembly!,
                        logger.Log)
                    : SourceLinkService.Open(
                        assemblyPath,
                        logger.Log));
            var context = service.Context;
            if (!context.HasMetadata)
                return new(null, collected);

            if (usePlatformSettlement)
            {
                logger.Log(
                    "Settling platform Portable PDB for: "
                    + (sourceAssembly!.Path
                        ?? sourceAssembly.Identity.Name));
                var request =
                    new PortablePdbSettlementRequest(
                        context,
                        sourceAssembly!,
                        httpClient,
                        FileSystemPdbStore.CreateDefault(),
                        new SourcePolicyPackageSourceAuthorization(
                            options.SourceOptions))
                    {
                        NuGetSourceOptions = options.SourceOptions,
                        Timeout = TimeSpan.FromMinutes(5),
                        Log = logger.Log,
                    };
                PortablePdbSettlementResult settlement =
                    await PortablePdbSettlement.SettleAsync(request);
                if (settlement
                    is PortablePdbSettlementResult.Acquired acquired)
                {
                    await acquired.LoadIntoAsync(context);
                }
                else if (settlement
                    is PortablePdbSettlementResult.Failed failed)
                {
                    CommandError.WriteWarning(
                        $"Portable PDB settlement failed: {failed.Failure}");
                }
                else if (settlement
                    is PortablePdbSettlementResult.Incomplete)
                {
                    CommandError.WriteWarning(
                        "Portable PDB settlement was incomplete.");
                }
            }
            else if (packageService is null
                && context.NeedsPdb)
            {
                if (sourceAssembly is null)
                {
                    await SourceEnricher.AcquirePdbAsync(
                        context,
                        httpClient,
                        source.PackageName,
                        source.PackageVersion,
                        isPlatformAssembly:
                            !string.IsNullOrEmpty(
                                options.PlatformAssembly),
                        logger.Log,
                        sourceOptions: options.SourceOptions);
                }
                else
                {
                    await SourceEnricher.AcquirePdbAsync(
                        context,
                        sourceAssembly,
                        httpClient,
                        logger.Log,
                        sourceOptions: options.SourceOptions,
                        fallbackPackageName: source.PackageName,
                        fallbackPackageVersion:
                            source.PackageVersion);
                }
            }

            var pdbPath = context.PortablePdbPath;
            if (!service.HasPdb)
                return new(pdbPath, collected);

            ApiMember[] targetMembers =
                GetTargetMembers(apiType, options).ToArray();
            var subject = new FindingSubject(assemblyPath, Path.GetFileName(assemblyPath));
            var membersByToken = targetMembers
                .SelectMany(member =>
                {
                    ApiMember tokenSource =
                        packageSession is not null
                        && packageSession.TokenSources.TryGetValue(
                            member,
                            out ApiMember? implementationMember)
                            ? implementationMember
                            : member;
                    return SourceTokens(tokenSource)
                        .Select(entry => (
                            entry.Token,
                            Candidate: (
                                Member: member,
                                entry.Rank)));
                })
                .GroupBy(static pair => pair.Token)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Select(static pair => pair.Candidate).ToArray());
            if (membersByToken.Count == 0)
                return new(pdbPath, collected);

            // A member can offer several accessor tokens; the best-ranked one that actually
            // resolves wins, so a later accessor is consulted only when a preferred one carries
            // no sequence points. Shared across both paths below so ordering cannot regress it.
            var appliedRank = new Dictionary<ApiMember, int>(ReferenceEqualityComparer.Instance);
            var documentsByRowId =
                new Dictionary<int, SourceLinkDocument>();
            foreach (SourceLinkDocument document in service.GetTrackedFiles())
                documentsByRowId.TryAdd(document.DocumentRowId, document);

            var sourceInspection = SourceLinkFindings.InspectMemberSources(
                service,
                subject,
                new MemberSourceQuery(membersByToken.Keys.ToHashSet()));
            if (sourceInspection.Value is FindingInspection<MemberSourceObservation>.Complete complete)
            {
                ApplySourceLocations(
                    membersByToken,
                    complete,
                    documentsByRowId,
                    appliedRank,
                    collected);
                return new(pdbPath, collected);
            }

            if (sourceInspection.Value is FindingInspection<MemberSourceObservation>.Absent)
                return new(pdbPath, collected);

            // A malformed method must not suppress source locations for healthy selected
            // members. Token queries are direct lookups, so this fallback remains O(selected).
            foreach (var (token, members) in membersByToken)
            {
                var tokenInspection = SourceLinkFindings.InspectMemberSources(
                    service,
                    subject,
                    new MemberSourceQuery(new HashSet<int> { token }));
                if (tokenInspection.Value is FindingInspection<MemberSourceObservation>.Failed failed)
                {
                    logger.LogWarning(
                        $"Failed to resolve source location for {members[0].Member.Name}: "
                        + failed.Error.Reason);
                    continue;
                }

                if (tokenInspection.Value is not FindingInspection<MemberSourceObservation>.Complete tokenComplete)
                    continue;

                ApplySourceLocations(
                    new Dictionary<int, (ApiMember Member, int Rank)[]> { [token] = members },
                    tokenComplete,
                    documentsByRowId,
                    appliedRank,
                    collected);
            }

            return new(pdbPath, collected);
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Failed to resolve member source locations for {apiType.FullName}: {ex.Message}");
            return new(null, collected);
        }
    }

    private static async Task<PackageSourceLinkSession?>
        TryOpenPackageSettlementAsync(
        ApiType apiType,
        string assemblyPath,
        ResolvedAssemblyReference? sourceAssembly,
        ApiSourceResult source,
        MemberOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (sourceAssembly is null
            || source.ApiSource != SourceKind.NuGet
            || string.IsNullOrWhiteSpace(source.PackageName)
            || string.IsNullOrWhiteSpace(source.PackageVersion)
            || string.IsNullOrWhiteSpace(source.SelectedTfm)
            || string.IsNullOrWhiteSpace(source.PackageExtractPath)
            || source.PackageAuthority is null
            || string.IsNullOrWhiteSpace(
                source.PackageProducerKey))
        {
            return null;
        }

        string relativeAssemblyPath =
            Path.GetRelativePath(
                    source.PackageExtractPath,
                    assemblyPath)
                .Replace('\\', '/');
        if (relativeAssemblyPath == ".."
            || relativeAssemblyPath.StartsWith(
                "../",
                StringComparison.Ordinal)
            || Path.IsPathRooted(relativeAssemblyPath))
        {
            return null;
        }

        NuGetSourceOptions? sourceOptions =
            source.PackageReplaySourceUrls is not null
            && !source.PackageReplayUsesOriginalSources
                ? NuGetSourceResolver.RestrictToSources(
                    options.SourceOptions,
                    source.PackageReplaySourceUrls)
                : options.SourceOptions;
        using var stores =
            new DesktopPackageStoreScope(
                "inspect-cli-member-pdb");
        await using DesktopPackageSourceComposition composition =
            source.Context.CreatePackageSourceComposition();
        var packageSource =
            new DesktopPortablePdbPackageContentSource(
                composition,
                stores,
                sourceOptions,
                source.PackageProducerKey,
                logger.Log);
        PortablePdbPackageBindingResult bindingResult =
            await PortablePdbPackageComposition.PrepareAsync(
                PackageSourceCoordinate.Create(
                    source.PackageName,
                    source.PackageVersion),
                PackageHouseTargetContext.Exact(
                    source.SelectedTfm),
                packageSource);
        if (bindingResult
            is not PortablePdbPackageBindingResult.Bound bound)
        {
            logger.LogWarning(
                "PackageHouse could not prepare the exact "
                + "package Portable PDB binding; retaining "
                + "the existing source-location route.");
            return null;
        }

        PackageHouseLibraryInventoryRow row =
            bound.Value.Candidate.Row;
        if (!string.Equals(
                row.CompileEntry.Path,
                relativeAssemblyPath,
                StringComparison.Ordinal)
            || bound.Value.Assembly.Identity
                != sourceAssembly.Identity)
        {
            logger.LogWarning(
                "PackageHouse selected a different logical "
                + "Library than the member source assembly; "
                + "retaining the existing source-location route.");
            return null;
        }

        IReadOnlyDictionary<ApiMember, ApiMember> tokenSources =
            CreateTokenSourceMap(
                apiType,
                bound.Value,
                options);
        if (tokenSources.Count == 0
            && GetTargetMembers(apiType, options).Any())
        {
            logger.LogWarning(
                "PackageHouse implementation metadata could not "
                + "correspond every selected member; retaining "
                + "the existing source-location route.");
            return null;
        }

        SourceLinkService service =
            SourceLinkService.OpenEmbeddedPdbOnly(
                bound.Value.Assembly,
                logger.Log);
        try
        {
            if (!service.Context.HasMetadata)
            {
                return new PackageSourceLinkSession(
                    service,
                    tokenSources);
            }

            logger.Log(
                "Settling package Portable PDB for: "
                + (row.ImplementationEntry
                    ?? row.CompileEntry).Path);
            var request =
                new PortablePdbSettlementRequest(
                    service.Context,
                    bound.Value.Assembly,
                    httpClient,
                    FileSystemPdbStore.CreateDefault(),
                    new SourcePolicyPackageSourceAuthorization(
                        sourceOptions))
                {
                    PackageCandidate =
                        bound.Value.Candidate,
                    NuGetSourceOptions = sourceOptions,
                    Timeout = TimeSpan.FromMinutes(5),
                    Log = logger.Log,
                };
            PortablePdbSettlementResult settlement =
                await PortablePdbSettlement.SettleAsync(
                    request);
            if (settlement
                is PortablePdbSettlementResult.Acquired acquired)
            {
                await acquired.LoadIntoAsync(
                    service.Context);
            }
            else if (settlement
                is PortablePdbSettlementResult.Failed failed)
            {
                CommandError.WriteWarning(
                    "Package Portable PDB settlement failed: "
                    + failed.Failure);
            }
            else if (settlement
                is PortablePdbSettlementResult.Incomplete)
            {
                CommandError.WriteWarning(
                    "Package Portable PDB settlement was incomplete.");
            }

            return new PackageSourceLinkSession(
                service,
                tokenSources);
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    private static IReadOnlyDictionary<ApiMember, ApiMember>
        CreateTokenSourceMap(
        ApiType apiType,
        PortablePdbPackageBinding binding,
        MemberOptions options)
    {
        ApiMember[] targetMembers =
            GetTargetMembers(apiType, options).ToArray();
        PackageHouseLibraryInventoryRow row =
            binding.Candidate.Row;
        if (row.ImplementationEntry is not { } implementationEntry
            || implementationEntry.Equals(row.CompileEntry))
        {
            var identityMap =
                new Dictionary<ApiMember, ApiMember>(
                    ReferenceEqualityComparer.Instance);
            foreach (ApiMember member in targetMembers)
            {
                identityMap.Add(member, member);
            }

            return identityMap;
        }

        using Stream implementation =
            binding.Assembly.OpenRead();
        ApiSurface? surface =
            AssemblyReader.ExtractApiSurface(
                implementation,
                includeAll: options.IncludeAll);
        ApiType? implementationType =
            surface?.Types.SingleOrDefault(
                type => string.Equals(
                    type.FullName,
                    apiType.FullName,
                    StringComparison.Ordinal));
        if (implementationType is null)
        {
            return new Dictionary<ApiMember, ApiMember>(
                ReferenceEqualityComparer.Instance);
        }

        var implementationMembers =
            new Dictionary<string, ApiMember>(
                StringComparer.Ordinal);
        foreach (ApiMember member in
            GetTargetMembers(
                implementationType,
                options))
        {
            if (!ApiMemberIdentity.TryGetCanonicalSignature(
                    implementationType,
                    member,
                    out string canonical)
                || !implementationMembers.TryAdd(
                    canonical,
                    member))
            {
                return new Dictionary<ApiMember, ApiMember>(
                    ReferenceEqualityComparer.Instance);
            }
        }

        var map =
            new Dictionary<ApiMember, ApiMember>(
                ReferenceEqualityComparer.Instance);
        foreach (ApiMember member in targetMembers)
        {
            if (!ApiMemberIdentity.TryGetCanonicalSignature(
                    apiType,
                    member,
                    out string canonical)
                || !implementationMembers.TryGetValue(
                    canonical,
                    out ApiMember? implementationMember))
            {
                return new Dictionary<ApiMember, ApiMember>(
                    ReferenceEqualityComparer.Instance);
            }

            map.Add(member, implementationMember);
        }

        return map;
    }

    private sealed record PackageSourceLinkSession(
        SourceLinkService Service,
        IReadOnlyDictionary<ApiMember, ApiMember>
            TokenSources);

    private sealed class DesktopPortablePdbPackageContentSource(
        DesktopPackageSourceComposition composition,
        DesktopPackageStoreScope stores,
        NuGetSourceOptions? sourceOptions,
        string requiredProducerKey,
        Action<string>? log)
        : IPortablePdbPackageContentSource
    {
        public Task<PackageHouseSettlement> AcquireAsync(
            PackageSourceCoordinate coordinate,
            PackageHouseContentQuery query,
            CancellationToken cancellationToken = default)
            => composition.AcquireContentAsync(
                coordinate,
                query,
                stores.Get,
                sourceOptions,
                log,
                cancellationToken,
                requiredProducerKey:
                    requiredProducerKey);
    }

    private static void ApplySourceLocations(
        IReadOnlyDictionary<int, (ApiMember Member, int Rank)[]> membersByToken,
        FindingInspection<MemberSourceObservation>.Complete inspection,
        IReadOnlyDictionary<int, SourceLinkDocument> documentsByRowId,
        Dictionary<ApiMember, int> appliedRank,
        Dictionary<ApiMember, MemberSourceObservation> collected)
    {
        foreach (var mappings in inspection.Findings
            .Select(static finding => finding.Payload)
            .GroupBy(static mapping => mapping.MetadataToken))
        {
            if (!membersByToken.TryGetValue(mappings.Key, out var candidates))
                continue;

            // Preserve the legacy resolver's preference for MethodDebugInformation.Document.
            var mapping = mappings
                .OrderByDescending(static candidate => candidate.IsPrimaryDocument)
                .ThenBy(static candidate => candidate.DocumentRowId)
                .First();
            foreach (var (member, rank) in candidates)
            {
                if (appliedRank.TryGetValue(member, out var existing) && existing <= rank)
                    continue;

                appliedRank[member] = rank;
                collected[member] = mapping;
                member.SourceFilePath = mapping.OriginalPath;
                member.SourceUrl = mapping.ResolvedUrl;
                member.SourceLineNumber = mapping.StartLine;
                member.SourceEndLineNumber = mapping.EndLine;
                if (documentsByRowId.TryGetValue(
                    mapping.DocumentRowId,
                    out SourceLinkDocument? document)
                    && string.Equals(
                        document.FilePath,
                        mapping.OriginalPath,
                        StringComparison.Ordinal))
                {
                    member.SourceChecksum = document.Checksum;
                    member.SourceChecksumAlgorithm = document.ChecksumAlgorithm;
                }
            }
        }
    }

    private static IEnumerable<ApiMember> GetTargetMembers(ApiType apiType, MemberOptions options)
    {
        var members = apiType.Members
            .Where(ApiMemberSectionDescriptors.IsBodyBacked);

        if (options.MemberFilter.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter));

        if (options.KindFilter.Count > 0)
            members = members.Where(m => options.KindFilter.Contains(m.Kind));

        if (options.UnsafeOnly)
            members = members.Where(m => m.IsUnsafe);

        return members;
    }

    /// <summary>
    /// The MethodDef token(s) whose PDB sequence points can locate a member's source,
    /// paired with a preference rank (lower wins). A method-like member is its own body. A
    /// property or event (including an indexer) has no MethodDef of its own, so it is located
    /// through its accessors — the getter/adder first, then the setter/remover, matching the
    /// default accessor ordinal the body sections address (issue #3278). Every accessor is
    /// offered rather than only the first, because a preferred accessor can carry no sequence
    /// points (a <c>#line hidden</c> or compiler-supplied body) while a later one resolves.
    /// The winning location is applied to the owning member, so a property contributes one row
    /// rather than one row per accessor.
    /// </summary>
    internal static IEnumerable<(int Token, int Rank)> SourceTokens(ApiMember member)
    {
        if (ApiMemberSectionDescriptors.IsMethodLike(member))
        {
            if (member.MetadataToken is { } methodToken)
                yield return (methodToken, 0);
            yield break;
        }

        int rank = 0;
        foreach (var accessorToken in new[]
        {
            member.GetterToken, member.AdderToken, member.SetterToken, member.RemoverToken
        })
        {
            if (accessorToken is { } token)
                yield return (token, rank++);
        }
    }

}
