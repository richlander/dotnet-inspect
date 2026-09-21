using System.Text.Json.Serialization;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Models;

internal sealed record DependsAssetDocument
{
    public required DependsAssetSummaryJson Summary { get; init; }

    public DependencyHierarchyJsonDocument? DependencyHierarchy { get; init; }

    public List<DependsAssetRootJson>? Roots { get; init; }

    public List<DependsDependencyJson>? Dependencies { get; init; }

    public List<DependsLicenseJson>? Licenses { get; init; }

    public List<DependsPruningJson>? Pruning { get; init; }

    public List<DependencyEvidenceRestoredEdgeJson>? RestoredEdges
    { get; init; }

    public List<DependsFailureJson>? Failures { get; init; }

    public List<DependencyEvidenceGroupJson>? DependencyGroups { get; init; }

    public List<DependencyEvidenceRestoredPackageJson>? RestoredPackages
    { get; init; }

    internal static DependsAssetDocument Create(
        DependsAssetProjection projection,
        IReadOnlySet<string> sections,
        RowWindow? rows,
        IReadOnlyList<DependencyHierarchyOccurrenceRow>?
            hierarchyRows = null)
    {
        DependencyEvidenceSourceTokens tokens =
            CreateTokens(projection);
        IReadOnlyList<DependencyHierarchyOccurrenceRow>
            selectedHierarchyRows =
                hierarchyRows
                ?? (rows is { IsUnlimited: false } hierarchyWindow
                    ? hierarchyWindow.Apply(projection.HierarchyRows)
                    : projection.HierarchyRows);
        IReadOnlyList<DependsRootRow> selectedRoots =
            rows is { IsUnlimited: false } rootWindow
                ? rootWindow.Apply(projection.Roots)
                : projection.Roots;

        return new DependsAssetDocument
        {
            Summary = DependsAssetSummaryJson.Create(
                projection.Summary,
                tokens),
            DependencyHierarchy =
                sections.Contains(DependsAssetSections.DependencyHierarchy)
                    ? DependencyHierarchyOutputAdapter.CreateJsonDocument(
                        projection.Hierarchy,
                        selectedHierarchyRows,
                        tokens,
                        includePackageSelectionEvidence:
                            projection.Summary.TraversalCompletion
                                != DependencyInspectionTraversalCompletion.NotRequested
                            || projection.Summary.DeclarationCompletion
                                != DependencyInspectionEvidencePhaseCompletion.NotRequested,
                        includePackageDeclarationEvidence:
                            projection.Summary.DeclarationCompletion
                                != DependencyInspectionEvidencePhaseCompletion.NotRequested)
                    : null,
            Roots = sections.Contains(DependsAssetSections.Roots)
                ? [.. selectedRoots.Select(row =>
                    DependsAssetRootJson.Create(row, tokens))]
                : null,
            Dependencies = Project(
                sections,
                DependsAssetSections.Dependencies,
                projection.Dependencies,
                rows,
                DependsDependencyJson.Create),
            Licenses = Project(
                sections,
                DependsAssetSections.Licenses,
                projection.Licenses,
                rows,
                DependsLicenseJson.Create),
            Pruning = Project(
                sections,
                DependsAssetSections.Pruning,
                projection.Pruning,
                rows,
                row => DependsPruningJson.Create(row, tokens)),
            RestoredEdges = Project(
                sections,
                DependsAssetSections.RestoredEdges,
                projection.RestoredEdges,
                rows,
                DependencyEvidenceRestoredEdgeJson.Create),
            Failures = Project(
                sections,
                DependsAssetSections.Failures,
                projection.Failures,
                rows,
                row => DependsFailureJson.Create(row, tokens)),
            DependencyGroups = Project(
                sections,
                DependsAssetSections.DependencyGroups,
                projection.DependencyGroups,
                rows,
                DependencyEvidenceGroupJson.Create),
            RestoredPackages = Project(
                sections,
                DependsAssetSections.RestoredPackages,
                projection.RestoredPackages,
                rows,
                DependencyEvidenceRestoredPackageJson.Create),
        };
    }

    private static List<TJson>? Project<TRow, TJson>(
        IReadOnlySet<string> sections,
        string section,
        IReadOnlyList<TRow> values,
        RowWindow? rows,
        Func<TRow, TJson> select)
    {
        if (!sections.Contains(section))
            return null;
        IReadOnlyList<TRow> selected =
            rows is { IsUnlimited: false } window
            && DependsAssetSections.AppliesRowWindow(sections, section)
                ? window.Apply(values)
                : values;
        return [.. selected.Select(select)];
    }

    private static DependencyEvidenceSourceTokens CreateTokens(
        DependsAssetProjection projection)
    {
        DependencyEvidenceSourceTokens tokens =
            DependencyEvidenceSourceTokens.Create();
        tokens.Reserve(projection.Summary.PackagePrefix?.Source);
        foreach (DependsRootRow root in projection.Roots)
            tokens.Reserve(root.Evidence?.Source);
        foreach (DependencyInspectionPruning pruning in projection.Pruning)
        {
            foreach (PackageSourceResultIdentity? source in
                     DependsPackageEvidenceJson.Sources(
                         pruning.RuntimeCandidateOutcome))
            {
                tokens.Reserve(source);
            }
        }
        foreach (DependencyInspectionFailure failure in projection.Failures)
        {
            switch (failure)
            {
                case DependencyInspectionFailure.Evidence evidence:
                    tokens.Reserve(evidence.Value.Source);
                    break;
                case DependencyInspectionFailure.Traversal traversal:
                    foreach (PackageSourceResultIdentity? source in
                             DependsPackageEvidenceJson.Sources(
                                 traversal.Value))
                    {
                        tokens.Reserve(source);
                    }
                    break;
            }
        }
        foreach (DependencyGraphPackageProjection packageProjection in
                 projection.Graph.PackageProjections)
        {
            tokens.Reserve(PackageSource(packageProjection.Evidence));
            if (packageProjection.RuntimeCandidate is { } candidate)
            {
                foreach (PackageAcquisitionAuthorityEvidence authority in
                         candidate.Authorities)
                {
                    tokens.Reserve(authority.Observation?.Source);
                }
            }
            foreach (PackageAuthorityFailure diagnostic in
                     packageProjection.RuntimeDiagnostics)
            {
                tokens.Reserve(
                    diagnostic.ResultSource
                        ?? diagnostic.SourceFailure?.Source);
            }
        }
        foreach (DependencyGraphEdge edge in projection.Graph.Edges)
        {
            foreach (PackageAuthorityFailure diagnostic in
                     edge.RuntimePackageDiagnostics)
            {
                tokens.Reserve(
                    diagnostic.ResultSource
                        ?? diagnostic.SourceFailure?.Source);
            }
        }
        foreach (DependencyGraphPackageProjection packageProjection in
                 projection.Graph.PackageProjections)
        {
            if (packageProjection.RuntimeCandidate is not { } candidate)
                continue;
            tokens.ProjectCorrespondence(candidate.Correspondence);
            foreach (PackageAcquisitionAuthorityEvidence authority in
                     candidate.Authorities)
            {
                tokens.ProjectAssociation(authority.Authority.Association);
            }
        }
        foreach (DependencyInspectionPruning pruning in projection.Pruning)
        {
            if (pruning.RuntimeCandidateOutcome
                is not PackageDependencyCandidateResult.Resolved resolved)
            {
                continue;
            }

            tokens.ProjectCorrespondence(
                resolved.Candidate.Correspondence);
            foreach (PackageAcquisitionAuthorityEvidence authority in
                     resolved.Candidate.Authorities)
            {
                tokens.ProjectAssociation(authority.Authority.Association);
            }
        }
        return tokens;
    }

    internal static PackageDependencyEvidenceSourceIdentity? PackageSource(
        PackageDependencyEvidenceRoot? root) =>
        (root?.Provenance
            as PackageDependencyEvidenceRootProvenance.Package)?.Source;
}

internal sealed record DependsAssetSummaryJson
{
    public required DependencyInspectionRootSetCompletion RootSetCompletion { get; init; }

    public required int RequestedRoots { get; init; }

    public required int AdmittedRoots { get; init; }

    public required int FailedRoots { get; init; }

    public required DependencyInspectionTraversalCompletion TraversalCompletion
    { get; init; }

    public required DependencyInspectionEvidencePhaseCompletion DeclarationCompletion
    { get; init; }

    public required DependencyInspectionEvidencePhaseCompletion
        RestoredRelationshipCompletion
    { get; init; }

    public required DependsPruningSummaryJson Pruning { get; init; }

    public required DependencyInspectionLicenseSummary Licenses { get; init; }

    public int? RequestedDepth { get; init; }

    public required int HierarchyOccurrences { get; init; }

    public required int CanonicalNodes { get; init; }

    public required int Relationships { get; init; }

    public DependencyEvidencePrefixJson? PackagePrefix { get; init; }

    internal static DependsAssetSummaryJson Create(
        DependencyInspectionSummary summary,
        DependencyEvidenceSourceTokens tokens) =>
        new()
        {
            RootSetCompletion = summary.RootSetCompletion,
            RequestedRoots = summary.RequestedRoots,
            AdmittedRoots = summary.AdmittedRoots,
            FailedRoots = summary.FailedRoots,
            TraversalCompletion = summary.TraversalCompletion,
            DeclarationCompletion = summary.DeclarationCompletion,
            RestoredRelationshipCompletion =
                summary.RestoredRelationshipCompletion,
            Pruning = DependsPruningSummaryJson.Create(summary.Pruning),
            Licenses = summary.Licenses,
            RequestedDepth = summary.RequestedDepth,
            HierarchyOccurrences = summary.HierarchyOccurrences,
            CanonicalNodes = summary.CanonicalNodes,
            Relationships = summary.Relationships,
            PackagePrefix = summary.PackagePrefix is { } prefix
                ? DependencyEvidencePrefixJson.Create(prefix, tokens)
                : null,
        };
}

internal sealed record DependsLicenseJson
{
    public required string Package { get; init; }

    public required string Version { get; init; }

    public required DependencyInspectionLicenseState State { get; init; }

    [JsonConverter(
        typeof(DotnetInspector.Sections.InertStringJsonConverter))]
    public required InertString License { get; init; }

    public DependencyInspectionLicenseIdentityKind? IdentityKind { get; init; }

    public DependencyInspectionLicenseDeclarationKind? DeclarationKind
    { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? DeclarationValue { get; init; }

    public PackageLicenseInventoryFailureReason? FailureReason { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? FailureMessage { get; init; }

    internal static DependsLicenseJson Create(
        DependencyInspectionLicense row) =>
        new()
        {
            Package = row.PackageId,
            Version = row.PackageVersion,
            State = row.State,
            License = row.License,
            IdentityKind = row.IdentityKind,
            DeclarationKind = row.DeclarationKind,
            DeclarationValue = row.DeclarationValue,
            FailureReason = row.FailureReason,
            FailureMessage = row.FailureMessage,
        };
}

internal sealed record DependsPruningSummaryJson
{
    public required DependencyInspectionPruningCompletion Completion { get; init; }

    public required int Roots { get; init; }

    public required int Declarations { get; init; }

    public required int Evaluated { get; init; }

    public required int Delegated { get; init; }

    public required int Retained { get; init; }

    public required int NotEvaluated { get; init; }

    public required int SourceBounded { get; init; }

    public required int Failed { get; init; }

    internal static DependsPruningSummaryJson Create(
        DependencyInspectionPruningSummary summary) =>
        new()
        {
            Completion = summary.Completion,
            Roots = summary.Roots,
            Declarations = summary.Declarations,
            Evaluated = summary.Evaluated,
            Delegated = summary.Delegated,
            Retained = summary.Retained,
            NotEvaluated = summary.NotEvaluated,
            SourceBounded = summary.SourceBounded,
            Failed = summary.Failed,
        };
}

internal sealed record DependsAssetRootJson
{
    public required int Root { get; init; }

    public required DependencyInspectionRootKind Kind { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public required InertString? Input { get; init; }

    public required string Source { get; init; }

    public required DependencyInspectionRootState State { get; init; }

    public DependencyGraphJsonNodeIdentity? Identity { get; init; }

    public required DependencyInspectionTraversalCompletion Traversal { get; init; }

    public required DependencyInspectionEvidenceAvailability Declaration { get; init; }

    public required DependencyInspectionEvidencePhaseCompletion DeclarationCompletion
    { get; init; }

    public required DependencyInspectionEvidenceAvailability RestoredRelationships
    { get; init; }

    public required DependencyInspectionEvidencePhaseCompletion
        RestoredRelationshipCompletion
    { get; init; }

    public DependencyEvidenceRootIdentityJson? EvidenceIdentity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Display { get; init; }

    public PackageDependencyEvidenceInputKind? Owner { get; init; }

    public PackageDependencyEvidenceAcquisitionForm? SourceKind { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? SourceLabel { get; init; }

    public string? PackageId { get; init; }

    public string? PackageVersion { get; init; }

    public PackageManifestIdentityProvenance? IdentityProvenance { get; init; }

    public DependencyEvidenceSourceIdentityJson? PackageSource { get; init; }

    public string? ContentDigest { get; init; }

    public DependencyEvidenceRestoredSelectionIdentityJson? RestoredSelection
    { get; init; }

    public required int DeclarationGroups { get; init; }

    public required int Declarations { get; init; }

    public required DependencyInspectionSelectionStatus Selection { get; init; }

    public DependencyEvidenceGroupIdentityJson? SelectedGroup { get; init; }

    public int? SelectedGroupOccurrence { get; init; }

    public DependencyEvidenceGroupOccurrenceJson? SelectedSourceOccurrence
    { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? RequestedFramework { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? SelectedFramework { get; init; }

    public required int RestoredPackages { get; init; }

    public required int RestoredEdges { get; init; }

    public string? TargetFrameworkIdentity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? TargetFrameworkSpelling { get; init; }

    public string? TargetRuntimeIdentifier { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? TargetRuntimeIdentifierSpelling { get; init; }

    public RestoredProjectTargetSelectionProvenance? TargetSelection
    { get; init; }

    internal static DependsAssetRootJson Create(
        DependsRootRow row,
        DependencyEvidenceSourceTokens tokens)
    {
        DependencyEvidenceRootRow? evidence = row.Evidence;
        return new DependsAssetRootJson
        {
            Root = row.Occurrence,
            Kind = row.Kind,
            Input = row.Input,
            Source = row.Source,
            State = row.State,
            Identity = row.DependencyIdentity is { } identity
                ? DependencyGraphOutputAdapter.JsonIdentity(identity)
                : null,
            Traversal = row.Traversal,
            Declaration = row.DeclarationState,
            DeclarationCompletion = row.DeclarationCompletion,
            RestoredRelationships = row.RestoredRelationshipState,
            RestoredRelationshipCompletion =
                row.RestoredRelationshipCompletion,
            EvidenceIdentity = evidence is null
                ? null
                : DependencyEvidenceRootIdentityJson.Create(
                    evidence.Identity),
            Display = evidence?.Display,
            Owner = evidence?.Owner,
            SourceKind = evidence?.SourceKind,
            SourceLabel = evidence?.SourceLabel,
            PackageId = evidence?.PackageId,
            PackageVersion = evidence?.PackageVersion,
            IdentityProvenance = evidence?.IdentityProvenance,
            PackageSource = tokens.Project(evidence?.Source),
            ContentDigest = evidence?.ContentDigest,
            RestoredSelection =
                evidence?.RestoredSelection is { } restoredSelection
                    ? DependencyEvidenceRestoredSelectionIdentityJson.Create(
                        restoredSelection)
                    : null,
            DeclarationGroups =
                row.DeclarationState == DependencyInspectionEvidenceAvailability.NotRequested
                    ? 0
                    : evidence?.DeclarationGroupCount ?? 0,
            Declarations =
                row.DeclarationState == DependencyInspectionEvidenceAvailability.NotRequested
                    ? 0
                    : evidence?.DeclarationCount ?? 0,
            Selection = row.Selection,
            SelectedGroup =
                row.Selection == DependencyInspectionSelectionStatus.NotRequested
                    ? null
                    : DependencyEvidenceGroupIdentityJson.CreateOptional(
                        row.SelectedGroup),
            SelectedGroupOccurrence =
                row.Selection == DependencyInspectionSelectionStatus.NotRequested
                    ? null
                    : row.SelectedGroupIndex,
            SelectedSourceOccurrence =
                row.Selection == DependencyInspectionSelectionStatus.NotRequested
                    ? null
                    : DependencyEvidenceGroupOccurrenceJson.CreateOptional(
                        row.SelectedSourceOccurrence),
            RequestedFramework =
                row.Selection == DependencyInspectionSelectionStatus.NotRequested
                    ? null
                    : row.RequestedFramework,
            SelectedFramework =
                row.Selection == DependencyInspectionSelectionStatus.NotRequested
                    ? null
                    : row.SelectedFramework,
            RestoredPackages = row.RestoredRelationshipState
                == DependencyInspectionEvidenceAvailability.NotRequested
                    ? 0
                    : evidence?.RestoredPackageCount ?? 0,
            RestoredEdges = row.RestoredRelationshipState
                == DependencyInspectionEvidenceAvailability.NotRequested
                    ? 0
                    : evidence?.RestoredEdgeCount ?? 0,
            TargetFrameworkIdentity =
                evidence?.RestoredTargetFrameworkIdentity,
            TargetFrameworkSpelling =
                evidence?.RestoredTargetFrameworkSpelling,
            TargetRuntimeIdentifier = evidence?.RestoredRuntimeIdentifier,
            TargetRuntimeIdentifierSpelling =
                evidence?.RestoredRuntimeIdentifierSpelling,
            TargetSelection = evidence?.RestoredTargetProvenance,
        };
    }
}

internal sealed record DependsDependencyJson
{
    public required int Root { get; init; }

    public required DependencyEvidenceRootIdentityJson RootIdentity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? RootDisplay { get; init; }

    public required PackageDependencyEvidenceInputKind Owner { get; init; }

    public required PackageDependencyEvidenceAcquisitionForm SourceKind
    { get; init; }

    public required int Group { get; init; }

    public required DependencyEvidenceGroupIdentityJson GroupIdentity
    { get; init; }

    public required string GroupOrderKey { get; init; }

    public required DependencyEvidenceDeclarationIdentityJson
        DeclarationIdentity
    { get; init; }

    public required PackageDependencyFrameworkScopeKind FrameworkScope
    { get; init; }

    public string? CanonicalFramework { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? FrameworkSpelling { get; init; }

    public required string PackageId { get; init; }

    public required string VersionConstraint { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? PackageIdSpelling { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? VersionConstraintSpelling { get; init; }

    public required int SourceOccurrences { get; init; }

    public required bool SelectedGroup { get; init; }

    public string? Resolved { get; init; }

    public DependencyEvidencePackageNodeIdentityJson? ResolvedPackageIdentity
    { get; init; }

    public DependencyEvidenceEdgeIdentityJson? ResolvedRelationshipIdentity
    { get; init; }

    internal static DependsDependencyJson Create(DependencyInspectionDependency row)
    {
        DependencyEvidenceDependencyRow declaration = row.Declaration;
        return new DependsDependencyJson
        {
            Root = declaration.RootIndex,
            RootIdentity = DependencyEvidenceRootIdentityJson.Create(
                declaration.RootIdentity),
            RootDisplay = declaration.RootDisplay,
            Owner = declaration.Owner,
            SourceKind = declaration.SourceKind,
            Group = declaration.GroupIndex,
            GroupIdentity = DependencyEvidenceGroupIdentityJson.Create(
                declaration.GroupIdentity),
            GroupOrderKey = declaration.GroupOrderKey,
            DeclarationIdentity =
                DependencyEvidenceDeclarationIdentityJson.Create(
                    declaration.DeclarationIdentity),
            FrameworkScope = declaration.FrameworkScopeKind,
            CanonicalFramework = declaration.CanonicalFramework,
            FrameworkSpelling = declaration.FrameworkSpelling,
            PackageId = declaration.PackageId,
            VersionConstraint = declaration.VersionConstraint,
            PackageIdSpelling = declaration.SourcePackageIdSpelling,
            VersionConstraintSpelling =
                declaration.SourceVersionConstraintSpelling,
            SourceOccurrences = declaration.SourceOccurrences,
            SelectedGroup = declaration.IsSelectedGroup,
            Resolved = row.ResolvedVersion,
            ResolvedPackageIdentity =
                row.ResolvedPackageIdentity is { } packageIdentity
                    ? DependencyEvidencePackageNodeIdentityJson.Create(
                        packageIdentity)
                    : null,
            ResolvedRelationshipIdentity =
                row.ResolvedRelationshipIdentity is { } relationshipIdentity
                    ? DependencyEvidenceEdgeIdentityJson.Create(
                        relationshipIdentity)
                    : null,
        };
    }
}

internal sealed record DependsPruningJson
{
    public required int Root { get; init; }

    public required DependencyEvidenceRootIdentityJson RootIdentity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public required InertString? RootDisplay { get; init; }

    public required DependencyEvidenceDeclarationIdentityJson
        DeclarationIdentity
    { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public required InertString? RequestedFramework { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public required InertString? SelectedFramework { get; init; }

    public required string PackageId { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public required InertString? PackageIdSpelling { get; init; }

    public required string VersionConstraint { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public required InertString? VersionConstraintSpelling { get; init; }

    public string? CandidateVersion { get; init; }

    public string? PlatformFamily { get; init; }

    public string? PlatformTargetFramework { get; init; }

    public string? PlatformVersion { get; init; }

    public string? PlatformProvidedVersion { get; init; }

    public required DependencyInspectionPruningDisposition Disposition { get; init; }

    public required string Reason { get; init; }

    public required PackageHouseDependencyPruningApplicabilityState
        Applicability
    { get; init; }

    public PackageHouseDependencyPruningTargetUnavailableReason?
        TargetUnavailableReason
    { get; init; }

    public string? CandidateState { get; init; }

    public string? CandidateDetail { get; init; }

    public DependsPackageCandidateOutcomeJson? Candidate { get; init; }

    public DependencyGraphJsonPackageCandidate? ResolvedCandidate
    { get; init; }

    public PlatformSubsumption? Subsumption { get; init; }

    public bool? DelegatesToPlatform { get; init; }

    internal static DependsPruningJson Create(
        DependencyInspectionPruning row,
        DependencyEvidenceSourceTokens tokens)
    {
        PackageHouseDependencyPruningResult.Evaluated? evaluated =
            row.RuntimeResult as PackageHouseDependencyPruningResult.Evaluated;
        return new DependsPruningJson
        {
            Root = row.RootOccurrence,
            RootIdentity = DependencyEvidenceRootIdentityJson.Create(
                row.RootIdentity),
            RootDisplay = row.RootDisplay,
            DeclarationIdentity =
                DependencyEvidenceDeclarationIdentityJson.Create(
                    row.DeclarationIdentity),
            RequestedFramework = row.RequestedFramework,
            SelectedFramework = row.SelectedFramework,
            PackageId = row.PackageId,
            PackageIdSpelling = row.PackageIdSpelling,
            VersionConstraint = row.VersionConstraint,
            VersionConstraintSpelling = row.VersionConstraintSpelling,
            CandidateVersion = row.CandidateVersion,
            PlatformFamily = row.PlatformFamily,
            PlatformTargetFramework = row.PlatformTargetFramework,
            PlatformVersion = row.PlatformVersion,
            PlatformProvidedVersion = row.PlatformProvidedVersion,
            Disposition = row.Disposition,
            Reason = row.Reason,
            Applicability = row.Applicability.State,
            TargetUnavailableReason =
                row.Applicability.TargetUnavailableReason,
            CandidateState = row.RuntimeCandidateOutcome?.GetType().Name,
            CandidateDetail = row.RuntimeCandidateOutcome switch
            {
                PackageDependencyCandidateResult.Failed failed =>
                    failed.Failure.GetType().Name,
                PackageDependencyCandidateResult.Incomplete incomplete =>
                    incomplete.Evidence.GetType().Name,
                PackageDependencyCandidateResult.Resolved => null,
                null => null,
                _ => throw new InvalidOperationException(
                    "Unknown package candidate result."),
            },
            Candidate = DependsPackageCandidateOutcomeJson.CreateOptional(
                row.RuntimeCandidateOutcome,
                tokens),
            ResolvedCandidate =
                row.RuntimeCandidateOutcome
                    is PackageDependencyCandidateResult.Resolved resolved
                    ? DependencyGraphOutputAdapter.JsonCandidate(
                        resolved.Candidate,
                        tokens)
                    : null,
            Subsumption = evaluated?.Pruning.Supply.Subsumption,
            DelegatesToPlatform =
                evaluated?.Pruning.Supply.DelegatesToPlatform,
        };
    }
}

internal sealed record DependsFailureJson
{
    public required DependencyEvidenceFailurePhase Phase { get; init; }

    public required string Reason { get; init; }

    public DependencyEvidenceFailureJson? Evidence { get; init; }

    public DependsTraversalFailureJson? Traversal { get; init; }

    public DependsPruningFailureJson? Pruning { get; init; }

    internal static DependsFailureJson Create(
        DependencyInspectionFailure row,
        DependencyEvidenceSourceTokens tokens) =>
        row switch
        {
            DependencyInspectionFailure.Evidence evidence => new DependsFailureJson
            {
                Phase = evidence.Value.Phase,
                Reason = evidence.Value.Reason,
                Evidence = DependencyEvidenceFailureJson.Create(
                    evidence.Value,
                    tokens),
            },
            DependencyInspectionFailure.Traversal traversal => new DependsFailureJson
            {
                Phase = DependencyEvidenceFailurePhase.Traversal,
                Reason = traversal.Value.Reason,
                Traversal = DependsTraversalFailureJson.Create(
                    traversal.Value,
                    tokens),
            },
            DependencyInspectionFailure.Pruning pruning => new DependsFailureJson
            {
                Phase = DependencyEvidenceFailurePhase.Pruning,
                Reason = pruning.Value.GetType().Name,
                Pruning = DependsPruningFailureJson.Create(
                    pruning.Value,
                    tokens),
            },
            _ => throw new InvalidOperationException(
                "Unknown depends failure row."),
        };
}

internal sealed record DependsPruningFailureJson
{
    public required string Kind { get; init; }

    public string? PlatformFamily { get; init; }

    public string? TargetFramework { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Message { get; init; }

    public int? Root { get; init; }

    public DependencyEvidenceRootIdentityJson? RootIdentity { get; init; }

    public DependencyEvidenceDeclarationIdentityJson? DeclarationIdentity
    { get; init; }

    public DependencyEvidenceDeclarationState? DeclarationState { get; init; }

    public string? PackageId { get; init; }

    public string? VersionConstraint { get; init; }

    public string? CandidateState { get; init; }

    public string? CandidateDetail { get; init; }

    public DependsPackageCandidateOutcomeJson? Candidate { get; init; }

    public required List<int> AffectedRoots { get; init; }

    public required int AffectedDeclarations { get; init; }

    internal static DependsPruningFailureJson Create(
        DependencyInspectionPruningFailure failure,
        DependencyEvidenceSourceTokens tokens) =>
        failure switch
        {
            DependencyInspectionPruningFailure.Inventory inventory => new()
            {
                Kind = nameof(DependencyInspectionPruningFailure.Inventory),
                PlatformFamily = inventory.PlatformFamily,
                TargetFramework = inventory.TargetFramework,
                Message = inventory.Message,
                AffectedRoots = [.. inventory.AffectedRootOccurrences],
                AffectedDeclarations = inventory.AffectedDeclarations,
            },
            DependencyInspectionPruningFailure.Prerequisite prerequisite => new()
            {
                Kind = nameof(DependencyInspectionPruningFailure.Prerequisite),
                Message = prerequisite.Message,
                Root = prerequisite.RootOccurrence,
                RootIdentity = DependencyEvidenceRootIdentityJson.Create(
                    prerequisite.RootIdentity),
                DeclarationState = prerequisite.DeclarationState,
                AffectedRoots = [prerequisite.RootOccurrence],
                AffectedDeclarations = 0,
            },
            DependencyInspectionPruningFailure.Candidate candidate => new()
            {
                Kind = nameof(DependencyInspectionPruningFailure.Candidate),
                Root = candidate.RootOccurrence,
                RootIdentity = DependencyEvidenceRootIdentityJson.Create(
                    candidate.RootIdentity),
                DeclarationIdentity =
                    DependencyEvidenceDeclarationIdentityJson.Create(
                        candidate.DeclarationIdentity),
                PackageId = candidate.PackageId,
                VersionConstraint = candidate.VersionConstraint,
                CandidateState = candidate.RuntimeOutcome?.GetType().Name,
                CandidateDetail = candidate.RuntimeOutcome switch
                {
                    PackageDependencyCandidateResult.Failed failed =>
                        failed.Failure.GetType().Name,
                    PackageDependencyCandidateResult.Incomplete incomplete =>
                        incomplete.Evidence.GetType().Name,
                    _ => throw new InvalidOperationException(
                        "A resolved pruning candidate is not a failure."),
                },
                Candidate = DependsPackageCandidateOutcomeJson.CreateOptional(
                    candidate.RuntimeOutcome,
                    tokens),
                AffectedRoots = [candidate.RootOccurrence],
                AffectedDeclarations = 1,
            },
            _ => throw new InvalidOperationException(
                "Unknown pruning failure."),
        };
}

internal sealed record DependsAssetJsonLine
{
    public required string Kind { get; init; }

    public DependencyHierarchyJsonOccurrence? DependencyHierarchy
    { get; init; }

    public DependsFailureJson? Failure { get; init; }
}

internal sealed record DependsTraversalFailureJson
{
    public int? SourceProjection { get; init; }

    public int? Node { get; init; }

    public int? Projection { get; init; }

    public DependencyEvidenceDeclarationIdentityJson? DeclarationIdentity
    { get; init; }

    public string? PackageId { get; init; }

    public string? VersionConstraint { get; init; }

    public DependsPackageCandidateOutcomeJson? Candidate { get; init; }

    public DependsPackageManifestFailureJson? Manifest { get; init; }

    public PackageDependencyTraversalWorkBudgetKind? BudgetKind { get; init; }

    public int? BudgetLimit { get; init; }

    public DependsRestoredTraversalFailureJson? Restored { get; init; }

    public DependsAssemblyBindingFailureJson? AssemblyBinding { get; init; }

    public required int[] AffectedRoots { get; init; }

    internal static DependsTraversalFailureJson Create(
        DependencyInspectionTraversalFailure row,
        DependencyEvidenceSourceTokens tokens) =>
        new()
        {
            SourceProjection = row.SourceProjectionIndex,
            Node = row.NodeIndex,
            Projection = row.ProjectionIndex,
            DeclarationIdentity =
                row.DeclarationIdentity is { } declaration
                    ? DependencyEvidenceDeclarationIdentityJson.Create(
                        declaration)
                    : null,
            PackageId = row.PackageId,
            VersionConstraint = row.VersionConstraint,
            Candidate =
                DependsPackageCandidateOutcomeJson.CreateOptional(
                    row.RuntimeCandidateOutcome,
                    tokens),
            Manifest = DependsPackageManifestFailureJson.CreateOptional(
                row.RuntimeManifestFailure,
                tokens),
            BudgetKind = row.BudgetKind,
            BudgetLimit = row.BudgetLimit,
            Restored = DependsRestoredTraversalFailureJson.CreateOptional(
                row.RestoredFailure),
            AssemblyBinding =
                DependsAssemblyBindingFailureJson.CreateOptional(
                    row.AssemblyBindingFailure),
            AffectedRoots = [.. row.AffectedRootOccurrences],
        };
}

internal sealed record DependsAssemblyBindingFailureJson
{
    public required string Kind { get; init; }

    public required AssemblyBindingMissDisposition Disposition { get; init; }

    public required DependencyGraphJsonLibraryIdentity RequestedAssembly
    { get; init; }

    internal static DependsAssemblyBindingFailureJson? CreateOptional(
        DependencyInspectionAssemblyBindingFailure? failure) =>
        failure is null
            ? null
            : new DependsAssemblyBindingFailureJson
            {
                Kind = "Missing",
                Disposition = failure.Disposition,
                RequestedAssembly =
                    DependencyGraphOutputAdapter.JsonLibraryIdentity(
                        new ManagedMetadataIdentity.Assembly(
                            failure.RequestedAssembly)),
            };
}

internal sealed record DependsRestoredTraversalFailureJson
{
    public required string Kind { get; init; }

    public RestoredProjectDependencyFailureReason? DocumentReason { get; init; }

    public RestoredProjectGraphFailureReason? GraphReason { get; init; }

    public int? Occurrences { get; init; }

    internal static DependsRestoredTraversalFailureJson? CreateOptional(
        DependencyInspectionRestoredTraversalFailure? failure) =>
        failure switch
        {
            null => null,
            DependencyInspectionRestoredTraversalFailure.Outcome
            {
                Value:
                    DependencyInspectionRestoredTraversalOutcomeFailure.Document
                        document,
            } =>
                new()
                {
                    Kind = nameof(
                        RestoredProjectDependencyTraversalFailure.Document),
                    DocumentReason = document.Failure.Reason,
                },
            DependencyInspectionRestoredTraversalFailure.Outcome
            {
                Value:
                    DependencyInspectionRestoredTraversalOutcomeFailure.Graph
                        graph,
            } => CreateGraph(graph.Failure),
            DependencyInspectionRestoredTraversalFailure.Graph graph =>
                CreateGraph(graph.Value),
            _ => throw new InvalidOperationException(
                "Unknown restored traversal failure."),
        };

    private static DependsRestoredTraversalFailureJson CreateGraph(
        RestoredProjectGraphFailure failure) =>
        new()
        {
            Kind = nameof(
                RestoredProjectDependencyTraversalFailure.Graph),
            GraphReason = failure.Reason,
            Occurrences = failure.Count,
        };
}

internal sealed record DependsPackageCandidateOutcomeJson
{
    public required string Kind { get; init; }

    public PackageVersionDiscoveryState? DiscoveryState { get; init; }

    public PackageVersionDiscoveryContract? DiscoveryContract { get; init; }

    public int? CandidateObservations { get; init; }

    public required DependsPackageAuthorityFailureJson[] SourceFailures
    { get; init; }

    internal static DependsPackageCandidateOutcomeJson? CreateOptional(
        PackageDependencyTraversalCandidateResult? result,
        DependencyEvidenceSourceTokens tokens) =>
        result switch
        {
            null => null,
            PackageDependencyTraversalCandidateResult.Failed
            {
                Failure:
                    PackageDependencyTraversalCandidateFailure
                        .AuthorizationDenied denied,
            } => Create(
                nameof(PackageDependencyTraversalCandidateFailure
                    .AuthorizationDenied),
                denied.Failures,
                tokens),
            PackageDependencyTraversalCandidateResult.Failed
            {
                Failure:
                    PackageDependencyTraversalCandidateFailure
                        .NoMatchingVersion,
            } => Create(
                nameof(PackageDependencyTraversalCandidateFailure
                    .NoMatchingVersion),
                [],
                tokens),
            PackageDependencyTraversalCandidateResult.Incomplete
            {
                Evidence:
                    PackageDependencyTraversalCandidateIncomplete
                        .PinnedAuthorization pinned,
            } => Create(
                nameof(PackageDependencyTraversalCandidateIncomplete
                    .PinnedAuthorization),
                pinned.Failures,
                tokens),
            PackageDependencyTraversalCandidateResult.Incomplete
            {
                Evidence:
                    PackageDependencyTraversalCandidateIncomplete
                        .VersionDiscovery discovery,
            } => Create(
                nameof(PackageDependencyTraversalCandidateIncomplete
                    .VersionDiscovery),
                discovery.Failures,
                tokens) with
            {
                DiscoveryState = discovery.State,
                DiscoveryContract = discovery.Contract,
                CandidateObservations = discovery.CandidateObservationCount,
            },
            PackageDependencyTraversalCandidateResult.Resolved resolved =>
                Create(
                    nameof(PackageDependencyTraversalCandidateResult.Resolved),
                    resolved.Diagnostics,
                    tokens),
            _ => throw new InvalidOperationException(
                "Unknown package candidate outcome."),
        };

    internal static DependsPackageCandidateOutcomeJson? CreateOptional(
        PackageDependencyCandidateResult? result,
        DependencyEvidenceSourceTokens tokens) =>
        result switch
        {
            null => null,
            PackageDependencyCandidateResult.Failed
            {
                Failure:
                    PackageDependencyCandidateFailure
                        .AuthorizationDenied denied,
            } => Create(
                nameof(PackageDependencyCandidateFailure
                    .AuthorizationDenied),
                denied.Failures,
                tokens),
            PackageDependencyCandidateResult.Failed
            {
                Failure:
                    PackageDependencyCandidateFailure.NoMatchingVersion,
            } => Create(
                nameof(PackageDependencyCandidateFailure.NoMatchingVersion),
                [],
                tokens),
            PackageDependencyCandidateResult.Failed
            {
                Failure:
                    PackageDependencyCandidateFailure
                        .ResolvedCoordinateMismatch,
            } => Create(
                nameof(PackageDependencyCandidateFailure
                    .ResolvedCoordinateMismatch),
                [],
                tokens),
            PackageDependencyCandidateResult.Incomplete
            {
                Evidence:
                    PackageDependencyCandidateIncomplete
                        .PinnedAuthorization pinned,
            } => Create(
                nameof(PackageDependencyCandidateIncomplete
                    .PinnedAuthorization),
                pinned.Failures,
                tokens),
            PackageDependencyCandidateResult.Incomplete
            {
                Evidence:
                    PackageDependencyCandidateIncomplete
                        .VersionDiscovery discovery,
            } => Create(
                nameof(PackageDependencyCandidateIncomplete
                    .VersionDiscovery),
                discovery.Failures,
                tokens) with
            {
                DiscoveryState = discovery.State,
                DiscoveryContract = discovery.Contract,
                CandidateObservations = discovery.CandidateObservationCount,
            },
            PackageDependencyCandidateResult.Resolved resolved =>
                Create(
                    nameof(PackageDependencyCandidateResult.Resolved),
                    resolved.Diagnostics,
                    tokens),
            _ => throw new InvalidOperationException(
                "Unknown package candidate outcome."),
        };

    private static DependsPackageCandidateOutcomeJson Create(
        string kind,
        IEnumerable<PackageAuthorityFailure> failures,
        DependencyEvidenceSourceTokens tokens) =>
        new()
        {
            Kind = kind,
            SourceFailures =
            [
                .. failures.Select(failure =>
                    DependsPackageAuthorityFailureJson.Create(
                        failure,
                        tokens)),
            ],
        };
}

internal sealed record DependsPackageManifestFailureJson
{
    public required string Kind { get; init; }

    public PackageManifestFailureReason? ManifestReason { get; init; }

    public int? LineNumber { get; init; }

    public int? LinePosition { get; init; }

    public DependsPackageDeclarationFailureJson? Declaration { get; init; }

    public int? BudgetLimit { get; init; }

    public required DependsPackageAuthorityFailureJson[] SourceFailures
    { get; init; }

    internal static DependsPackageManifestFailureJson? CreateOptional(
        PackageDependencyTraversalManifestFailureDetail? detail,
        DependencyEvidenceSourceTokens tokens) =>
        detail switch
        {
            null => null,
            PackageDependencyTraversalManifestFailureDetail.Acquisition
                acquisition => Create(
                    nameof(PackageDependencyTraversalManifestFailureDetail
                        .Acquisition),
                    acquisition.Failures,
                    tokens),
            PackageDependencyTraversalManifestFailureDetail
                .IncompleteAcquisition incomplete => Create(
                    nameof(PackageDependencyTraversalManifestFailureDetail
                        .IncompleteAcquisition),
                    incomplete.Failures,
                    tokens),
            PackageDependencyTraversalManifestFailureDetail.Identity identity =>
                Create(
                    nameof(PackageDependencyTraversalManifestFailureDetail
                        .Identity),
                    [],
                    tokens) with
                {
                    ManifestReason = identity.Failure.Reason,
                    LineNumber = identity.Failure.LineNumber,
                    LinePosition = identity.Failure.LinePosition,
                },
            PackageDependencyTraversalManifestFailureDetail.Declaration
                declaration => Create(
                    nameof(PackageDependencyTraversalManifestFailureDetail
                        .Declaration),
                    [],
                    tokens) with
                {
                    Declaration =
                        DependsPackageDeclarationFailureJson.Create(
                            declaration.Failure),
                },
            PackageDependencyTraversalManifestFailureDetail
                .ManifestProjectionBudgetExhausted budget => Create(
                    nameof(PackageDependencyTraversalManifestFailureDetail
                        .ManifestProjectionBudgetExhausted),
                    [],
                    tokens) with
                {
                    BudgetLimit = budget.Limit,
                },
            _ => throw new InvalidOperationException(
                "Unknown package manifest failure."),
        };

    private static DependsPackageManifestFailureJson Create(
        string kind,
        IEnumerable<PackageAuthorityFailure> failures,
        DependencyEvidenceSourceTokens tokens) =>
        new()
        {
            Kind = kind,
            SourceFailures =
            [
                .. failures.Select(failure =>
                    DependsPackageAuthorityFailureJson.Create(
                        failure,
                        tokens)),
            ],
        };
}

internal sealed record DependsPackageDeclarationFailureJson
{
    public required string Kind { get; init; }

    public DependencyEvidenceGroupIdentityJson? Group { get; init; }

    public string? PackageId { get; init; }

    public int? Occurrences { get; init; }

    public RestoredProjectDeclarationFailureReason? RestoredReason
    { get; init; }

    public AuthoredProjectDependencyLimitationReason? AuthoredReason
    { get; init; }

    public AuthoredProjectUnresolvedDependencySyntaxKind? SyntaxKind
    { get; init; }

    public string? SyntaxIdentity { get; init; }

    internal static DependsPackageDeclarationFailureJson Create(
        PackageDependencyEvidenceDeclarationFailure failure) =>
        failure switch
        {
            PackageDependencyEvidenceDeclarationFailure
                .ConflictingPackageDeclaration conflicting => new()
                {
                    Kind = nameof(PackageDependencyEvidenceDeclarationFailure
                        .ConflictingPackageDeclaration),
                    Group = DependencyEvidenceGroupIdentityJson.Create(
                        conflicting.Group),
                    PackageId = conflicting.CanonicalPackageId,
                    Occurrences = conflicting.SourceOccurrenceCount,
                },
            PackageDependencyEvidenceDeclarationFailure
                .InvalidPackageDeclaration invalid => new()
                {
                    Kind = nameof(PackageDependencyEvidenceDeclarationFailure
                        .InvalidPackageDeclaration),
                    Group = DependencyEvidenceGroupIdentityJson.Create(
                        invalid.Group),
                    Occurrences = invalid.SourceOccurrenceCount,
                },
            PackageDependencyEvidenceDeclarationFailure.RestoredProject
                restored => new()
                {
                    Kind = nameof(PackageDependencyEvidenceDeclarationFailure
                        .RestoredProject),
                    RestoredReason = restored.Failure.Reason,
                    Occurrences = restored.Failure.Count,
                },
            PackageDependencyEvidenceDeclarationFailure.AuthoredProject
                authored => new()
                {
                    Kind = nameof(PackageDependencyEvidenceDeclarationFailure
                        .AuthoredProject),
                    AuthoredReason = authored.Limitation.Reason,
                    Occurrences = authored.Limitation.Count,
                },
            PackageDependencyEvidenceDeclarationFailure
                .AuthoredProjectUnresolvedSyntax unresolved => new()
                {
                    Kind = nameof(PackageDependencyEvidenceDeclarationFailure
                        .AuthoredProjectUnresolvedSyntax),
                    SyntaxKind = unresolved.Syntax.Kind,
                    SyntaxIdentity = unresolved.Syntax.OpaqueIdentity,
                    Occurrences = 1,
                },
            _ => throw new InvalidOperationException(
                "Unknown package declaration failure."),
        };
}

internal sealed record DependsPackageAuthorityFailureJson
{
    public required string Authority { get; init; }

    public required PackageAuthorityFailureKind Kind { get; init; }

    public required string Message { get; init; }

    public DependencyEvidenceSourceIdentityJson? Source { get; init; }

    public PackageSourceCapabilities? Capability { get; init; }

    public DependencyGraphJsonPackageIdentity? Coordinate { get; init; }

    public PackageSourceFailureKind? SourceFailureKind { get; init; }

    public PackageSourceTimeoutKind? TimeoutKind { get; init; }

    public double? TimeoutSeconds { get; init; }

    public required bool RequiredProducerUnavailable { get; init; }

    internal static DependsPackageAuthorityFailureJson Create(
        PackageAuthorityFailure failure,
        DependencyEvidenceSourceTokens tokens)
    {
        PackageSourceFailure? sourceFailure = failure.SourceFailure;
        PackageSourceTimeout? timeout = failure.Timeout;
        return new DependsPackageAuthorityFailureJson
        {
            Authority = failure.Authority.ToString(),
            Kind = failure.Kind,
            Message = new InertString(
                TextPolicy.Prose,
                failure.Message).ToString(),
            Source = tokens.Project(
                failure.ResultSource ?? sourceFailure?.Source),
            Capability = sourceFailure?.Capability,
            Coordinate = sourceFailure?.Coordinate is { } coordinate
                ? new DependencyGraphJsonPackageIdentity(
                    coordinate.PackageId,
                    coordinate.Version)
                : null,
            SourceFailureKind = sourceFailure?.Kind,
            TimeoutKind = timeout?.Kind,
            TimeoutSeconds = timeout?.Duration.TotalSeconds,
            RequiredProducerUnavailable =
                failure.IsRequiredProducerUnavailable,
        };
    }
}

internal static class DependsPackageEvidenceJson
{
    internal static IEnumerable<PackageSourceResultIdentity?> Sources(
        DependencyInspectionTraversalFailure row)
    {
        foreach (PackageAuthorityFailure failure in CandidateFailures(
                     row.RuntimeCandidateOutcome))
        {
            yield return failure.ResultSource
                ?? failure.SourceFailure?.Source;
        }
        foreach (PackageAuthorityFailure failure in ManifestFailures(
                     row.RuntimeManifestFailure))
        {
            yield return failure.ResultSource
                ?? failure.SourceFailure?.Source;
        }
    }

    internal static IEnumerable<PackageSourceResultIdentity?> Sources(
        PackageDependencyCandidateResult? outcome)
    {
        foreach (PackageAuthorityFailure failure in CandidateFailures(
                     outcome))
        {
            yield return failure.ResultSource
                ?? failure.SourceFailure?.Source;
        }

        if (outcome is not PackageDependencyCandidateResult.Resolved resolved)
            yield break;

        foreach (PackageAcquisitionAuthorityEvidence authority in
                 resolved.Candidate.Authorities)
        {
            yield return authority.Observation?.Source;
        }
    }

    private static IEnumerable<PackageAuthorityFailure> CandidateFailures(
        PackageDependencyTraversalCandidateResult? outcome) =>
        outcome switch
        {
            PackageDependencyTraversalCandidateResult.Failed
            {
                Failure:
                    PackageDependencyTraversalCandidateFailure
                        .AuthorizationDenied denied,
            } => denied.Failures,
            PackageDependencyTraversalCandidateResult.Incomplete
            {
                Evidence:
                    PackageDependencyTraversalCandidateIncomplete
                        .PinnedAuthorization pinned,
            } => pinned.Failures,
            PackageDependencyTraversalCandidateResult.Incomplete
            {
                Evidence:
                    PackageDependencyTraversalCandidateIncomplete
                        .VersionDiscovery discovery,
            } => discovery.Failures,
            PackageDependencyTraversalCandidateResult.Resolved resolved =>
                resolved.Diagnostics,
            _ => [],
        };

    private static IEnumerable<PackageAuthorityFailure> CandidateFailures(
        PackageDependencyCandidateResult? outcome) =>
        outcome switch
        {
            PackageDependencyCandidateResult.Failed
            {
                Failure:
                    PackageDependencyCandidateFailure
                        .AuthorizationDenied denied,
            } => denied.Failures,
            PackageDependencyCandidateResult.Incomplete
            {
                Evidence:
                    PackageDependencyCandidateIncomplete
                        .PinnedAuthorization pinned,
            } => pinned.Failures,
            PackageDependencyCandidateResult.Incomplete
            {
                Evidence:
                    PackageDependencyCandidateIncomplete
                        .VersionDiscovery discovery,
            } => discovery.Failures,
            PackageDependencyCandidateResult.Resolved resolved =>
                resolved.Diagnostics,
            _ => [],
        };

    private static IEnumerable<PackageAuthorityFailure> ManifestFailures(
        PackageDependencyTraversalManifestFailureDetail? detail) =>
        detail switch
        {
            PackageDependencyTraversalManifestFailureDetail.Acquisition
                acquisition => acquisition.Failures,
            PackageDependencyTraversalManifestFailureDetail
                .IncompleteAcquisition incomplete => incomplete.Failures,
            _ => [],
        };
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DependsAssetDocument))]
[JsonSerializable(typeof(DependsFailureJson))]
[JsonSerializable(typeof(DependsAssetJsonLine))]
internal partial class DependsAssetJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DependsAssetDocument))]
[JsonSerializable(typeof(DependsFailureJson))]
[JsonSerializable(typeof(DependsAssetJsonLine))]
internal partial class DependsAssetCompactJsonContext : JsonSerializerContext
{
}
