using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal static class MemberSourceLocationCollector
{
    public static async Task<string?> EnrichAsync(
        ApiType apiType,
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        MemberOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        try
        {
            using var service = SourceLinkService.Open(assemblyPath, logger.Log);
            var context = service.Context;
            if (!context.HasMetadata)
                return null;

            if (context.NeedsPdb)
            {
                await SourceEnricher.AcquirePdbAsync(context, httpClient,
                    packageName, packageVersion,
                    isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log);
            }

            var pdbPath = context.PortablePdbPath;
            if (!service.HasPdb || !service.HasSourceLink)
                return pdbPath;

            var targetMembers = GetTargetMembers(apiType, options).ToArray();
            var subject = new FindingSubject(assemblyPath, Path.GetFileName(assemblyPath));
            var membersByToken = targetMembers
                .SelectMany(static member => SourceTokens(member)
                    .Select(entry => (entry.Token, Candidate: (Member: member, entry.Rank))))
                .GroupBy(static pair => pair.Token)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Select(static pair => pair.Candidate).ToArray());
            if (membersByToken.Count == 0)
                return pdbPath;

            // A member can offer several accessor tokens; the best-ranked one that actually
            // resolves wins, so a later accessor is consulted only when a preferred one carries
            // no sequence points. Shared across both paths below so ordering cannot regress it.
            var appliedRank = new Dictionary<ApiMember, int>(ReferenceEqualityComparer.Instance);

            var sourceInspection = SourceLinkFindings.InspectMemberSources(
                service,
                subject,
                new MemberSourceQuery(membersByToken.Keys.ToHashSet()));
            if (sourceInspection.Value is FindingInspection<MemberSourceObservation>.Complete complete)
            {
                ApplySourceLocations(membersByToken, complete, appliedRank);
                return pdbPath;
            }

            if (sourceInspection.Value is FindingInspection<MemberSourceObservation>.Absent)
                return pdbPath;

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
                    appliedRank);
            }

            return pdbPath;
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Failed to resolve member source locations for {apiType.FullName}: {ex.Message}");
            return null;
        }
    }

    private static void ApplySourceLocations(
        IReadOnlyDictionary<int, (ApiMember Member, int Rank)[]> membersByToken,
        FindingInspection<MemberSourceObservation>.Complete inspection,
        Dictionary<ApiMember, int> appliedRank)
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
                member.SourceFilePath = mapping.OriginalPath;
                member.SourceUrl = mapping.ResolvedUrl;
                member.SourceLineNumber = mapping.StartLine;
                member.SourceEndLineNumber = mapping.EndLine;
            }
        }
    }

    private static IEnumerable<ApiMember> GetTargetMembers(ApiType apiType, MemberOptions options)
    {
        var members = apiType.Members
            .Where(ApiMemberSectionDescriptors.IsBodyBacked)
            .Where(m => !MemberFilters.IsCompilerGenerated(m.Name));

        if (options.MemberFilter.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter));

        if (options.KindFilter.Count > 0)
            members = members.Where(m => options.KindFilter.Contains(m.Kind));

        if (options.UnsafeOnly)
            members = members.Where(m => m.IsUnsafe);

        return members;
    }

    /// <summary>
    /// The MethodDef token(s) whose PDB sequence points can locate a member's authored source,
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
