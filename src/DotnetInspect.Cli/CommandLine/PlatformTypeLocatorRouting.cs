using System.Collections.Immutable;
using DotnetInspect.Cli.Commands;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.CommandLine;

internal enum CliPlatformTypeLocatorFailureKind
{
    HouseUnavailable,
    HouseAmbiguous,
    HouseRejected,
    HouseIncomplete,
    HouseFailed,
    LibraryAdmissionRejected,
    DeclarationAdmissionRejected,
    LocatorRejected,
    LocatorIncomplete,
    CleanupFailed,
}

internal abstract class CliPlatformTypeLocatorOutcome
{
    private protected CliPlatformTypeLocatorOutcome()
    {
    }

    internal sealed class Completed : CliPlatformTypeLocatorOutcome
    {
        internal Completed(
            PlatformTypeCatalogRouteTarget target,
            ImmutableArray<PlatformTypeCatalogRouteRequest> typeRequests,
            ImmutableArray<string> compatibilityTypePatterns,
            InspectionEnvelope<TypeDeclarationLocatorSectionResult> envelope,
            PlatformHouseReceipt houseReceipt)
        {
            Target = target;
            TypeRequests = typeRequests;
            CompatibilityTypePatterns = compatibilityTypePatterns;
            Envelope = envelope;
            HouseReceipt = houseReceipt;
        }

        internal PlatformTypeCatalogRouteTarget Target { get; }
        internal ImmutableArray<PlatformTypeCatalogRouteRequest> TypeRequests
        { get; }
        internal ImmutableArray<string> CompatibilityTypePatterns { get; }
        internal int NamespaceAnswerIndex => TypeRequests.Length;
        internal InspectionEnvelope<TypeDeclarationLocatorSectionResult>
            Envelope
        { get; }
        internal PlatformHouseReceipt HouseReceipt { get; }
    }

    internal sealed class NotCompleted : CliPlatformTypeLocatorOutcome
    {
        internal NotCompleted(
            CliPlatformTypeLocatorFailureKind kind,
            PlatformHouseReceipt? houseReceipt = null,
            WorkspaceLibraryAdmissionOutcome? libraryAdmission = null,
            WorkspacePlatformPopulationDeclarationAdmissionOutcome?
                declarationAdmission = null,
            InspectionEnvelope<TypeDeclarationLocatorSectionResult>?
                envelope = null,
            InspectionWorkspaceCloseReport? closeReport = null)
        {
            Kind = kind;
            HouseReceipt = houseReceipt;
            LibraryAdmission = libraryAdmission;
            DeclarationAdmission = declarationAdmission;
            Envelope = envelope;
            CloseReport = closeReport;
        }

        internal CliPlatformTypeLocatorFailureKind Kind { get; }
        internal PlatformHouseReceipt? HouseReceipt { get; }
        internal WorkspaceLibraryAdmissionOutcome? LibraryAdmission { get; }
        internal WorkspacePlatformPopulationDeclarationAdmissionOutcome?
            DeclarationAdmission
        { get; }
        internal InspectionEnvelope<TypeDeclarationLocatorSectionResult>?
            Envelope
        { get; }
        internal InspectionWorkspaceCloseReport? CloseReport { get; }
    }
}

internal static class PlatformTypeLocatorRouting
{
    private const int MaxTypeMemberBoundaryProbes = 64;
    private static readonly LibraryTypeDeclarationInventoryInspectionBounds
        InventoryBounds = new(
            maximumAssemblyBytes: 512 * 1024 * 1024,
            maximumRetainedDeclarations: 500_000,
            maximumMetadataRows: int.MaxValue,
            maximumRetainedTextCharacters: int.MaxValue);

    internal static ValueTask<CliPlatformTypeLocatorOutcome> LocateAsync(
        CommandContext context,
        NuGetSourceOptions sourceOptions,
        string target,
        CancellationToken cancellationToken) =>
        LocateAsync(
            PlatformTypeCatalogRouting.FindActiveDotnetRoot(),
            context,
            sourceOptions,
            target,
            cancellationToken);

    internal static async ValueTask<CliPlatformTypeLocatorOutcome> LocateAsync(
        string? dotnetRoot,
        CommandContext context,
        NuGetSourceOptions sourceOptions,
        string target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sourceOptions);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        PlatformPopulationArtifactMaterializationOutcome realization =
            await PlatformTypeCatalogRouting.RealizePopulationAsync(
                    dotnetRoot,
                    context,
                    sourceOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        if (realization
            is PlatformPopulationArtifactMaterializationOutcome.Terminal
                terminal)
        {
            return HouseFailure(terminal);
        }

        var completed =
            (PlatformPopulationArtifactMaterializationOutcome.Completed)
                realization;
        var workspace = new InspectionWorkspace();
        bool resourcesSettled = false;
        bool workspaceClosed = false;
        try
        {
            WorkspaceRegistrationRevision registrations =
                AssertRegistrationSnapshot(workspace);
            WorkspaceLibraryAdmissionOutcome libraryAdmission =
                await workspace.AdmitLibraryBatchAsync(
                        registrations,
                        completed.Artifacts,
                        completed.Population.Owners)
                    .ConfigureAwait(false);
            resourcesSettled = true;
            if (libraryAdmission
                is not WorkspaceLibraryAdmissionOutcome.Accepted accepted)
            {
                InspectionWorkspaceCloseReport closeReport =
                    await workspace.CloseAsync().ConfigureAwait(false);
                workspaceClosed = true;
                return new CliPlatformTypeLocatorOutcome.NotCompleted(
                    libraryAdmission
                        is WorkspaceLibraryAdmissionOutcome.Failed
                        || !closeReport.Succeeded
                            ? CliPlatformTypeLocatorFailureKind.CleanupFailed
                            : CliPlatformTypeLocatorFailureKind
                                .LibraryAdmissionRejected,
                    completed.Population.Receipt.HouseReceipt,
                    libraryAdmission,
                    closeReport: closeReport);
            }

            WorkspacePlatformPopulationDeclarationAdmissionOutcome
                declarationAdmission =
                    WorkspacePlatformPopulationDeclarationAdmission.Admit(
                        workspace,
                        accepted.Receipt,
                        completed.Population.Value,
                        completed.Population.Receipt,
                        InventoryBounds);
            if (declarationAdmission
                is not WorkspacePlatformPopulationDeclarationAdmissionOutcome
                    .Admitted)
            {
                InspectionWorkspaceCloseReport closeReport =
                    await workspace.CloseAsync().ConfigureAwait(false);
                workspaceClosed = true;
                return new CliPlatformTypeLocatorOutcome.NotCompleted(
                    closeReport.Succeeded
                        ? CliPlatformTypeLocatorFailureKind
                            .DeclarationAdmissionRejected
                        : CliPlatformTypeLocatorFailureKind.CleanupFailed,
                    completed.Population.Receipt.HouseReceipt,
                    libraryAdmission,
                    declarationAdmission,
                    closeReport: closeReport);
            }

            PlatformFamilyTarget populationTarget =
                completed.Population.Value.Members[0].Target;
            var routeTarget = new PlatformTypeCatalogRouteTarget(
                populationTarget.Family,
                populationTarget.TargetFramework.ToString(),
                populationTarget.Version.Value);
            (
                ImmutableArray<PlatformTypeCatalogRouteRequest> typeRequests,
                ImmutableArray<string> compatibilityTypePatterns,
                ImmutableArray<TypeDeclarationLocatorRequest> requests) =
                    BuildRequests(
                        target,
                        [
                            .. completed.Population.Value.Members
                                .Select(
                                    static member =>
                                        member.Library.ApiAssembly
                                            .AssemblyIdentity)
                                .OfType<
                                    ManagedMetadataIdentity.Assembly>()
                                .Select(
                                    static identity =>
                                        identity.Identity.Name)
                                .Distinct(
                                    StringComparer.OrdinalIgnoreCase),
                        ],
                        routeTarget,
                        cancellationToken);
            InspectionEnvelope<TypeDeclarationLocatorSectionResult> envelope =
                await TypeDeclarationLocatorInspection.ExecuteAsync(
                        workspace,
                        requests,
                        TypeDeclarationLocatorSectionPlan.All,
                        cancellationToken)
                    .ConfigureAwait(false);
            InspectionWorkspaceCloseReport report =
                await workspace.CloseAsync().ConfigureAwait(false);
            workspaceClosed = true;
            if (!report.Succeeded)
            {
                return new CliPlatformTypeLocatorOutcome.NotCompleted(
                    CliPlatformTypeLocatorFailureKind.CleanupFailed,
                    completed.Population.Receipt.HouseReceipt,
                    libraryAdmission,
                    declarationAdmission,
                    envelope,
                    report);
            }
            if (envelope.Content
                is TypeDeclarationLocatorSectionResult.Rejected)
            {
                return new CliPlatformTypeLocatorOutcome.NotCompleted(
                    CliPlatformTypeLocatorFailureKind.LocatorRejected,
                    completed.Population.Receipt.HouseReceipt,
                    libraryAdmission,
                    declarationAdmission,
                    envelope,
                    report);
            }

            var evaluated =
                (TypeDeclarationLocatorSectionResult.Evaluated)
                    envelope.Content;
            if (!evaluated.IsSuccess
                || evaluated.Contexts.Any(
                    static context =>
                        !context.IsRealized
                        || !context.Failures.IsDefaultOrEmpty)
                || evaluated.Members.Any(
                    static member => !member.IsComplete))
            {
                return new CliPlatformTypeLocatorOutcome.NotCompleted(
                    CliPlatformTypeLocatorFailureKind.LocatorIncomplete,
                    completed.Population.Receipt.HouseReceipt,
                    libraryAdmission,
                    declarationAdmission,
                    envelope,
                    report);
            }

            return new CliPlatformTypeLocatorOutcome.Completed(
                routeTarget,
                typeRequests,
                compatibilityTypePatterns,
                envelope,
                completed.Population.Receipt.HouseReceipt);
        }
        finally
        {
            if (!resourcesSettled)
            {
                await PlatformTypeCatalogRouting.RetireAsync(completed)
                    .ConfigureAwait(false);
            }
            else if (!workspaceClosed)
            {
                await workspace.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    internal static InspectionEnvelope<PlatformTypeCatalogRouteOutcome>
        ResolveType(
            CliPlatformTypeLocatorOutcome.Completed completed,
            CancellationToken cancellationToken)
    {
        TypeDeclarationLocatorSectionResult.Evaluated evaluated =
            AssertEvaluated(completed.Envelope);
        for (int index = 0; index < completed.TypeRequests.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlatformTypeCatalogRouteRequest request =
                completed.TypeRequests[index];
            if (TryGetRejection(
                    request.TypePattern,
                    out PlatformTypeCatalogQueryRejectionKind rejection))
            {
                return RouteEnvelope(
                    new PlatformTypeCatalogRouteOutcome.Rejected(
                        request,
                        completed.Target,
                        rejection));
            }
            ImmutableArray<TypeDeclarationLocatorSectionCandidate> preferred =
                PreferCandidates(
                    evaluated.Answers[index].Candidates,
                    request.TypePattern,
                    cancellationToken);
            if (preferred.IsDefaultOrEmpty)
                continue;

            PlatformTypeCatalogRouteOutcome outcome = preferred.Length == 1
                ? new PlatformTypeCatalogRouteOutcome.Resolved(
                    request,
                    completed.Target,
                    Snapshot(preferred[0]))
                : new PlatformTypeCatalogRouteOutcome.Ambiguous(
                    request,
                    completed.Target,
                    [.. preferred.Select(Snapshot)]);
            return RouteEnvelope(outcome);
        }

        return RouteEnvelope(
            new PlatformTypeCatalogRouteOutcome.Missing(
                completed.TypeRequests[0],
                completed.Target));
    }

    internal static InspectionEnvelope<PlatformNamespaceDiscoveryOutcome>
        ResolveNamespace(
            CliPlatformTypeLocatorOutcome.Completed completed,
            string @namespace,
            CancellationToken cancellationToken)
    {
        string normalizedNamespace = @namespace.Trim();
        TypeDeclarationLocatorSectionResult.Evaluated evaluated =
            AssertEvaluated(completed.Envelope);
        TypeDeclarationLocatorSectionAnswer answer =
            evaluated.Answers[completed.NamespaceAnswerIndex];
        ImmutableArray<string> namesakeLibraries =
            LibraryNamespaceDiscovery.NamesakeLibraryCandidates(
                normalizedNamespace);
        var ranks = new Dictionary<string, int>(
            namesakeLibraries.Length,
            StringComparer.OrdinalIgnoreCase);
        var rankedHits =
            new List<NamespaceHitBuilder>?[
                namesakeLibraries.Length];
        for (int index = 0; index < namesakeLibraries.Length; index++)
            ranks.Add(namesakeLibraries[index], index);

        var hitsByMember =
            new Dictionary<(int Context, int Member), NamespaceHitBuilder>();
        foreach (TypeDeclarationLocatorSectionCandidate candidate
            in answer.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!candidate.IsPublicSurface
                || !ranks.TryGetValue(
                    candidate.Observation.AssemblyIdentity.Name,
                    out int rank)
                || candidate.Observation.Realization
                    is not TypeDeclarationLocatorRealization
                        .PlatformRealization { Role: { } role })
            {
                continue;
            }

            var key = (
                candidate.Observation.ContextOrder,
                candidate.Observation.MemberOrder);
            if (!hitsByMember.TryGetValue(
                    key,
                    out NamespaceHitBuilder? builder))
            {
                builder = new(
                    candidate.Observation.AssemblyIdentity.Name,
                    normalizedNamespace,
                    completed.Target,
                    role switch
                    {
                        WorkspacePlatformPopulationMemberRole.Focus =>
                            PlatformPopulationMemberRole.Focus,
                        WorkspacePlatformPopulationMemberRole.BindingSupport =>
                            PlatformPopulationMemberRole.BindingSupport,
                        _ => throw new InvalidOperationException(
                            "Unknown Workspace Platform population member role."),
                    },
                    candidate.Name,
                    candidate.Observation.ContextOrder,
                    candidate.Observation.MemberOrder);
                hitsByMember.Add(key, builder);
                (rankedHits[rank] ??= []).Add(builder);
            }
            builder.Declarations.Add(
                new(
                    candidate.Name,
                    candidate.DeclarationKind,
                    candidate.DefinitionKind));
        }

        var hits =
            ImmutableArray.CreateBuilder<PlatformNamespaceDiscoveryHit>();
        foreach (List<NamespaceHitBuilder>? tier in rankedHits)
        {
            if (tier is not null)
            {
                hits.AddRange(
                    tier.OrderBy(static builder => builder.ContextOrder)
                        .ThenBy(static builder => builder.MemberOrder)
                        .Select(static builder => builder.Build()));
            }
        }

        var request =
            new PlatformNamespaceDiscoveryRequest(normalizedNamespace);
        PlatformNamespaceDiscoveryOutcome outcome = hits.Count == 0
            ? new PlatformNamespaceDiscoveryOutcome.Missing(
                request,
                completed.Target)
            : new PlatformNamespaceDiscoveryOutcome.Found(
                request,
                hits.ToImmutable());
        return new(
            outcome,
            new InspectionShare.NonProjectable(
                "platform-namespace-locator-route/share",
                "Platform namespace locator routes do not yet have a "
                    + "canonical Workspace Share projection."));
    }

    internal static PlatformTypeCatalogRouteOutcome?
        ResolveCompatibilityType(
            CliPlatformTypeLocatorOutcome.Completed completed,
            string pattern,
            CancellationToken cancellationToken)
    {
        int patternIndex = -1;
        for (int index = 0;
            index < completed.CompatibilityTypePatterns.Length;
            index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(
                    completed.CompatibilityTypePatterns[index],
                    pattern,
                    StringComparison.OrdinalIgnoreCase))
            {
                patternIndex = index;
                break;
            }
        }
        if (patternIndex < 0)
            return null;

        TypeDeclarationLocatorSectionResult.Evaluated evaluated =
            AssertEvaluated(completed.Envelope);
        int answerIndex =
            completed.NamespaceAnswerIndex + 1 + patternIndex;
        ImmutableArray<TypeDeclarationLocatorSectionCandidate> preferred =
            PreferCandidates(
                evaluated.Answers[answerIndex].Candidates,
                pattern,
                cancellationToken);
        var request = new PlatformTypeCatalogRouteRequest(
            pattern,
            pattern,
            memberSelector: null);
        if (preferred.IsDefaultOrEmpty)
        {
            return new PlatformTypeCatalogRouteOutcome.Missing(
                request,
                completed.Target);
        }

        return preferred.Length == 1
            ? new PlatformTypeCatalogRouteOutcome.Resolved(
                request,
                completed.Target,
                Snapshot(preferred[0]))
            : new PlatformTypeCatalogRouteOutcome.Ambiguous(
                request,
                completed.Target,
                [.. preferred.Select(Snapshot)]);
    }

    private static (
        ImmutableArray<PlatformTypeCatalogRouteRequest> TypeRequests,
        ImmutableArray<string> CompatibilityTypePatterns,
        ImmutableArray<TypeDeclarationLocatorRequest> Requests)
        BuildRequests(
            string target,
            ImmutableArray<string> platformAssemblyNames,
            PlatformTypeCatalogRouteTarget routeTarget,
            CancellationToken cancellationToken)
    {
        string discoveryTarget = target.Trim();
        var typeRequests =
            ImmutableArray.CreateBuilder<
                PlatformTypeCatalogRouteRequest>();
        var requests =
            ImmutableArray.CreateBuilder<TypeDeclarationLocatorRequest>();

        AddTypeRequest(discoveryTarget, memberSelector: null);
        int probes = 0;
        int genericDepth = 0;
        for (int index = discoveryTarget.Length - 1;
            index > 0 && probes < MaxTypeMemberBoundaryProbes;
            index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (discoveryTarget[index])
            {
                case '>':
                    genericDepth++;
                    continue;
                case '<':
                    genericDepth--;
                    continue;
                case '.' when genericDepth == 0
                    && index < discoveryTarget.Length - 1:
                    break;
                default:
                    continue;
            }

            probes++;
            AddTypeRequest(
                discoveryTarget[..index],
                discoveryTarget[(index + 1)..]);
        }
        requests.Add(
            new TypeDeclarationLocatorRequest.Namespace(
                discoveryTarget,
                MetadataNamespaceMatch.Exact));
        var compatibilityTypePatterns =
            ImmutableArray.CreateBuilder<string>();
        var seenCompatibilityPatterns =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PlatformTypeCatalogRouteRequest request
            in typeRequests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PlatformResolver.TryParseQualifiedTypeName(
                    request.TypePattern,
                    out string assemblyName,
                    out string pattern)
                || !IsAssemblyOwnedByTarget(
                    assemblyName,
                    platformAssemblyNames,
                    routeTarget))
            {
                continue;
            }
            if (seenCompatibilityPatterns.Add(pattern))
            {
                compatibilityTypePatterns.Add(pattern);
                requests.Add(
                    new TypeDeclarationLocatorRequest.Pattern(pattern));
            }
        }
        return (
            typeRequests.ToImmutable(),
            compatibilityTypePatterns.ToImmutable(),
            requests.ToImmutable());

        void AddTypeRequest(
            string typePattern,
            string? memberSelector)
        {
            typeRequests.Add(
                new(
                    target,
                    typePattern,
                    memberSelector));
            requests.Add(
                new TypeDeclarationLocatorRequest.Pattern(
                    typePattern));
        }
    }

    private static bool IsAssemblyOwnedByTarget(
        string assemblyName,
        ImmutableArray<string> platformAssemblyNames,
        PlatformTypeCatalogRouteTarget target)
    {
        if (platformAssemblyNames.Contains(
                assemblyName,
                StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        string framework = target.Family switch
        {
            PlatformFamily.DotNetRuntime => "runtime",
            PlatformFamily.AspNetCore => "aspnetcore",
            _ => throw new InvalidOperationException(
                "Unknown Platform family."),
        };
        return PlatformResolver.ResolveAssembly(
                assemblyName,
                $"{framework}@{target.Version}")
            .AssemblyPath is not null;
    }

    private static ImmutableArray<TypeDeclarationLocatorSectionCandidate>
        PreferCandidates(
            ImmutableArray<TypeDeclarationLocatorSectionCandidate>
                candidates,
            string pattern,
            CancellationToken cancellationToken)
    {
        string normalizedPattern =
            FqnParser.NormalizeTypeName(pattern.Trim()).Replace('+', '.');
        bool explicitGeneric =
            TypeMatcher.HasExplicitGenericNotation(pattern);
        ImmutableArray<TypeDeclarationLocatorSectionCandidate> preferred =
        [
            .. candidates.Where(
                candidate =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return TypeMatcher.MatchesNormalized(
                        Normalize(candidate.Name),
                        normalizedPattern);
                }),
        ];
        ImmutableArray<TypeDeclarationLocatorSectionCandidate> exact =
        [
            .. preferred.Where(
                candidate =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string normalizedCandidate = Normalize(candidate.Name);
                    return normalizedCandidate.Equals(
                            normalizedPattern,
                            StringComparison.OrdinalIgnoreCase)
                        || normalizedCandidate.EndsWith(
                            $".{normalizedPattern}",
                            StringComparison.OrdinalIgnoreCase);
                }),
        ];
        if (!exact.IsDefaultOrEmpty)
            preferred = exact;
        else if (explicitGeneric)
            return [];

        ImmutableArray<TypeDeclarationLocatorSectionCandidate> topLevel =
        [
            .. preferred.Where(
                candidate =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return candidate.Name.Segments.Length == 1;
                }),
        ];
        if (!topLevel.IsDefaultOrEmpty)
            preferred = topLevel;

        ImmutableArray<TypeDeclarationLocatorSectionCandidate> definitions =
        [
            .. preferred.Where(
                candidate =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return candidate.DeclarationKind
                        == AssemblyTypeDeclarationKind.Definition;
                }),
        ];
        return definitions.IsDefaultOrEmpty
            ? preferred
            : definitions;
    }

    private static bool TryGetRejection(
        string pattern,
        out PlatformTypeCatalogQueryRejectionKind rejection)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            rejection = PlatformTypeCatalogQueryRejectionKind.EmptyPattern;
            return true;
        }
        if (pattern.Length > MetadataSafetyPolicy.MaxTypeNameCharacters)
        {
            rejection =
                PlatformTypeCatalogQueryRejectionKind.PatternTooLong;
            return true;
        }

        rejection = default;
        return false;
    }

    private static string Normalize(MetadataTypeDefinitionName name)
    {
        string typeName = string.Join('.', name.Segments);
        return name.Namespace.Length == 0
            ? typeName
            : $"{name.Namespace}.{typeName}";
    }

    private static PlatformTypeCatalogRouteCandidate Snapshot(
        TypeDeclarationLocatorSectionCandidate candidate) =>
        new(
            candidate.Name,
            new ExactTypeAssemblyIdentity(
                candidate.Observation.AssemblyIdentity,
                candidate.ModuleVersionId),
            candidate.DeclarationKind);

    private static TypeDeclarationLocatorSectionResult.Evaluated
        AssertEvaluated(
            InspectionEnvelope<TypeDeclarationLocatorSectionResult>
                envelope) =>
        envelope.Content
            as TypeDeclarationLocatorSectionResult.Evaluated
            ?? throw new InvalidOperationException(
                "Completed Platform locator routing requires an evaluated locator envelope.");

    private static WorkspaceRegistrationRevision AssertRegistrationSnapshot(
        InspectionWorkspace workspace) =>
        workspace.GetRegistrationSnapshot() switch
        {
            WorkspaceRegistrationReadResult.Available available =>
                available.Revision,
            _ => throw new InvalidOperationException(
                "A new Workspace must expose its initial registration revision."),
        };

    private static InspectionEnvelope<PlatformTypeCatalogRouteOutcome>
        RouteEnvelope(PlatformTypeCatalogRouteOutcome content) =>
        new(
            content,
            new InspectionShare.NonProjectable(
                "platform-type-locator-route/share",
                "Platform Type locator routes do not yet have a canonical "
                    + "Workspace Share projection."));

    private static CliPlatformTypeLocatorOutcome.NotCompleted HouseFailure(
        PlatformPopulationArtifactMaterializationOutcome.Terminal terminal)
    {
        CliPlatformTypeCatalogOutcome.NotCompleted failure =
            PlatformTypeCatalogRouting.HouseFailure(terminal);
        CliPlatformTypeLocatorFailureKind kind = failure.Kind switch
        {
            CliPlatformTypeCatalogFailureKind.HouseUnavailable =>
                CliPlatformTypeLocatorFailureKind.HouseUnavailable,
            CliPlatformTypeCatalogFailureKind.HouseAmbiguous =>
                CliPlatformTypeLocatorFailureKind.HouseAmbiguous,
            CliPlatformTypeCatalogFailureKind.HouseRejected =>
                CliPlatformTypeLocatorFailureKind.HouseRejected,
            CliPlatformTypeCatalogFailureKind.HouseIncomplete =>
                CliPlatformTypeLocatorFailureKind.HouseIncomplete,
            CliPlatformTypeCatalogFailureKind.HouseFailed =>
                CliPlatformTypeLocatorFailureKind.HouseFailed,
            _ => throw new InvalidOperationException(
                "Unknown PlatformHouse locator failure."),
        };
        return new(kind, failure.HouseReceipt);
    }

    private sealed record NamespaceHitBuilder(
        string Library,
        string Namespace,
        PlatformTypeCatalogRouteTarget Target,
        PlatformPopulationMemberRole Role,
        MetadataTypeDefinitionName Witness,
        int ContextOrder,
        int MemberOrder)
    {
        internal ImmutableArray<PlatformNamespaceDiscoveryDeclaration>.Builder
            Declarations { get; } =
                ImmutableArray.CreateBuilder<
                    PlatformNamespaceDiscoveryDeclaration>();

        internal PlatformNamespaceDiscoveryHit Build() =>
            new(
                Library,
                Namespace,
                Target,
                Role,
                Witness,
                Declarations.ToImmutable());
    }
}
