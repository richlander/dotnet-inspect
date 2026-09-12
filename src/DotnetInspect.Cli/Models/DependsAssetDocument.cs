using System.Text.Json.Serialization;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Models;

internal sealed record DependsAssetDocument
{
    public required DependsAssetSummaryJson Summary { get; init; }

    public DependencyGraphJsonDocument? DependencyGraph { get; init; }

    public List<DependsAssetRootJson>? Roots { get; init; }

    public List<DependsDependencyJson>? Dependencies { get; init; }

    public List<DependencyEvidenceRestoredEdgeJson>? RestoredEdges
        { get; init; }

    public List<DependsFailureJson>? Failures { get; init; }

    public List<DependencyEvidenceGroupJson>? DependencyGroups { get; init; }

    public List<DependencyEvidenceRestoredPackageJson>? RestoredPackages
        { get; init; }

    internal static DependsAssetDocument Create(
        DependsAssetProjection projection,
        IReadOnlySet<string> sections,
        RowWindow? rows)
    {
        DependencyEvidenceSourceTokens tokens =
            CreateTokens(projection);
        IReadOnlyList<DependencyGraphEdgeRow> selectedGraphRows =
            rows is { IsUnlimited: false } graphWindow
                ? graphWindow.Apply(projection.GraphRows)
                : projection.GraphRows;
        IReadOnlyList<DependsRootRow> selectedRoots =
            rows is { IsUnlimited: false } rootWindow
                ? rootWindow.Apply(projection.Roots)
                : projection.Roots;

        return new DependsAssetDocument
        {
            Summary = DependsAssetSummaryJson.Create(
                projection.Summary,
                tokens),
            DependencyGraph =
                sections.Contains(DependsAssetSections.DependencyGraph)
                    ? DependencyGraphOutputAdapter.CreateJsonDocument(
                        projection.Graph,
                        selectedGraphRows,
                        tokens,
                        includePackageSelectionEvidence:
                            projection.Summary.TraversalCompletion
                                != DependsTraversalCompletion.NotRequested
                            || projection.Summary.DeclarationCompletion
                                != DependsEvidencePhaseCompletion.NotRequested,
                        includePackageDeclarationEvidence:
                            projection.Summary.DeclarationCompletion
                                != DependsEvidencePhaseCompletion.NotRequested)
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
                ? window.Apply(values)
                : values;
        return [.. selected.Select(select)];
    }

    private static IEnumerable<PackageSourceResultIdentity?> EnumerateSources(
        DependsAssetProjection projection)
    {
        yield return projection.Summary.PackagePrefix?.Source;
        foreach (DependsRootRow root in projection.Roots)
            yield return root.Evidence?.Source;
        foreach (DependsFailureRow failure in projection.Failures)
        {
            switch (failure)
            {
                case DependsFailureRow.Evidence evidence:
                    yield return evidence.Value.Source;
                    break;
                case DependsFailureRow.Traversal traversal:
                    foreach (PackageSourceResultIdentity? source in
                             DependsPackageEvidenceJson.Sources(
                                 traversal.Value))
                    {
                        yield return source;
                    }
                    break;
            }
        }

        foreach (DependencyGraphPackageProjection packageProjection in
                 projection.Graph.PackageProjections)
        {
            yield return PackageSource(packageProjection.Evidence);
            if (packageProjection.Candidate is { } candidate)
            {
                foreach (PackageAcquisitionAuthorityEvidence authority in
                         candidate.Authorities)
                {
                    yield return authority.Observation?.Source;
                }
            }
            foreach (PackageAuthorityFailure diagnostic in
                     packageProjection.Diagnostics)
            {
                yield return diagnostic.ResultSource
                    ?? diagnostic.SourceFailure?.Source;
            }
        }
        foreach (DependencyGraphEdge edge in projection.Graph.Edges)
        {
            foreach (PackageAuthorityFailure diagnostic in
                     edge.PackageDiagnostics.IsDefault
                        ? []
                        : edge.PackageDiagnostics)
            {
                yield return diagnostic.ResultSource
                    ?? diagnostic.SourceFailure?.Source;
            }
        }
    }

    private static DependencyEvidenceSourceTokens CreateTokens(
        DependsAssetProjection projection)
    {
        DependencyEvidenceSourceTokens tokens =
            DependencyEvidenceSourceTokens.Create(
                EnumerateSources(projection));
        foreach (DependencyGraphPackageProjection packageProjection in
                 projection.Graph.PackageProjections)
        {
            if (packageProjection.Candidate is not { } candidate)
                continue;
            tokens.ProjectCorrespondence(candidate.Correspondence);
            foreach (PackageAcquisitionAuthorityEvidence authority in
                     candidate.Authorities)
            {
                tokens.ProjectAssociation(authority.Authority.Association);
            }
        }
        return tokens;
    }

    internal static PackageSourceResultIdentity? PackageSource(
        PackageDependencyEvidenceRoot? root) =>
        (root?.Provenance
            as PackageDependencyEvidenceRootProvenance.Package)?.Source;
}

/// <summary>One normalized logical declaration group.</summary>
public sealed record DependencyEvidenceGroupJson
{
    public required int Root { get; init; }

    public required DependencyEvidenceRootIdentityJson RootIdentity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? RootDisplay { get; init; }

    public required PackageDependencyEvidenceInputKind Owner { get; init; }

    public required int Group { get; init; }

    public required DependencyEvidenceGroupIdentityJson Identity { get; init; }

    public required string OrderKey { get; init; }

    public required List<DependencyEvidenceGroupOccurrenceJson> Occurrences
        { get; init; }

    public required PackageDependencyFrameworkScopeKind FrameworkScope
        { get; init; }

    public string? CanonicalFramework { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? FrameworkSpelling { get; init; }

    public required bool ImplicitManifestGroup { get; init; }

    public required int Declarations { get; init; }

    public required int SourceOccurrences { get; init; }

    public required bool Selected { get; init; }

    internal static DependencyEvidenceGroupJson Create(
        DependencyEvidenceGroupRow row) =>
        new()
        {
            Root = row.RootIndex,
            RootIdentity = DependencyEvidenceRootIdentityJson.Create(
                row.RootIdentity),
            RootDisplay = row.RootDisplay,
            Owner = row.Owner,
            Group = row.GroupIndex,
            Identity = DependencyEvidenceGroupIdentityJson.Create(row.Identity),
            OrderKey = row.OrderKey,
            Occurrences =
            [
                .. row.SourceOccurrences.Select(
                    DependencyEvidenceGroupOccurrenceJson.Create),
            ],
            FrameworkScope = row.FrameworkScopeKind,
            CanonicalFramework = row.CanonicalFramework,
            FrameworkSpelling = row.FrameworkSpelling,
            ImplicitManifestGroup = row.IsImplicitManifestGroup,
            Declarations = row.DeclarationCount,
            SourceOccurrences = row.SourceOccurrenceCount,
            Selected = row.IsSelected,
        };
}

/// <summary>One owner-issued restored package graph edge.</summary>
public sealed record DependencyEvidenceRestoredEdgeJson
{
    public required int Root { get; init; }

    public required DependencyEvidenceRootIdentityJson RootIdentity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? RootDisplay { get; init; }

    public required DependencyEvidenceEdgeIdentityJson Identity { get; init; }

    public required DependencyEvidenceEdgeParentKind ParentKind { get; init; }

    public string? ParentPackageId { get; init; }

    public string? ParentPackageVersion { get; init; }

    public string? ParentProjectIdentity { get; init; }

    public required string PackageId { get; init; }

    public required string PackageVersion { get; init; }

    public required string VersionConstraint { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? VersionConstraintSpelling { get; init; }

    public required RestoredProjectDependencyRole Role { get; init; }

    internal static DependencyEvidenceRestoredEdgeJson Create(
        DependencyEvidenceRestoredEdgeRow row) =>
        new()
        {
            Root = row.RootIndex,
            RootIdentity = DependencyEvidenceRootIdentityJson.Create(
                row.RootIdentity),
            RootDisplay = row.RootDisplay,
            Identity = DependencyEvidenceEdgeIdentityJson.Create(row.Identity),
            ParentKind = row.ParentKind,
            ParentPackageId = row.ParentPackageId,
            ParentPackageVersion = row.ParentPackageVersion,
            ParentProjectIdentity = row.ParentProjectIdentity,
            PackageId = row.PackageId,
            PackageVersion = row.PackageVersion,
            VersionConstraint = row.VersionConstraint,
            VersionConstraintSpelling = row.SourceVersionConstraintSpelling,
            Role = row.Role,
        };
}

/// <summary>One owner-issued resolved package node.</summary>
public sealed record DependencyEvidenceRestoredPackageJson
{
    public required int Root { get; init; }

    public required DependencyEvidenceRootIdentityJson RootIdentity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? RootDisplay { get; init; }

    public required DependencyEvidencePackageNodeIdentityJson Identity
        { get; init; }

    public required string PackageId { get; init; }

    public required string PackageVersion { get; init; }

    public required RestoredProjectDependencyRole Role { get; init; }

    internal static DependencyEvidenceRestoredPackageJson Create(
        DependencyEvidenceRestoredPackageRow row) =>
        new()
        {
            Root = row.RootIndex,
            RootIdentity = DependencyEvidenceRootIdentityJson.Create(
                row.RootIdentity),
            RootDisplay = row.RootDisplay,
            Identity = DependencyEvidencePackageNodeIdentityJson.Create(
                row.Identity),
            PackageId = row.PackageId,
            PackageVersion = row.PackageVersion,
            Role = row.Role,
        };
}

/// <summary>Terminal package-prefix accounting retained from the profile producer.</summary>
public sealed record DependencyEvidencePrefixJson
{
    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Prefix { get; init; }

    public DependencyEvidenceSourceIdentityJson? Source { get; init; }

    public required int Candidates { get; init; }

    public required int Matches { get; init; }

    public required int Failures { get; init; }

    public required PackageSearchTruncationReason TruncationReason { get; init; }

    internal static DependencyEvidencePrefixJson Create(
        PackageDependencyEvidencePackagePrefixCompletion completion,
        DependencyEvidenceSourceTokens tokens) =>
        new()
        {
            Prefix = completion.Prefix,
            Source = tokens.Project(completion.Source),
            Candidates = completion.Candidates,
            Matches = completion.Matches,
            Failures = completion.Failures,
            TruncationReason = completion.TruncationReason,
        };
}

/// <summary>One typed evidence failure and its occurrence count.</summary>
public sealed record DependencyEvidenceFailureJson
{
    public required DependencyEvidenceFailurePhase Phase { get; init; }

    public required string Reason { get; init; }

    public PackageDependencyEvidenceAcquisitionForm? SourceKind { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Subject { get; init; }

    public int? Root { get; init; }

    public DependencyEvidenceRootIdentityJson? RootIdentity { get; init; }

    public DependencyEvidenceGroupIdentityJson? GroupIdentity { get; init; }

    public int? Group { get; init; }

    public string? PackageId { get; init; }

    public string? PackageVersion { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? SourceLabel { get; init; }

    public DependencyEvidenceSourceIdentityJson? Source { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Message { get; init; }

    public required int Occurrences { get; init; }

    internal static DependencyEvidenceFailureJson Create(
        DependencyEvidenceFailureRow row,
        DependencyEvidenceSourceTokens tokens) =>
        new()
        {
            Phase = row.Phase,
            Reason = row.Reason,
            SourceKind = row.SourceKind,
            Subject = row.Subject,
            Root = row.RootIndex,
            RootIdentity = DependencyEvidenceRootIdentityJson.CreateOptional(
                row.RootIdentity),
            GroupIdentity = DependencyEvidenceGroupIdentityJson.CreateOptional(
                row.Group),
            Group = row.GroupIndex,
            PackageId = row.PackageId,
            PackageVersion = row.PackageVersion,
            SourceLabel = row.SourceLabel,
            Source = tokens.Project(row.Source),
            Message = row.Message,
            Occurrences = row.Occurrences,
        };
}

internal sealed record DependsAssetSummaryJson
{
    public required DependsRootSetCompletion RootSetCompletion { get; init; }

    public required int RequestedRoots { get; init; }

    public required int AdmittedRoots { get; init; }

    public required int FailedRoots { get; init; }

    public required DependsTraversalCompletion TraversalCompletion
        { get; init; }

    public required DependsEvidencePhaseCompletion DeclarationCompletion
        { get; init; }

    public required DependsEvidencePhaseCompletion
        RestoredRelationshipCompletion { get; init; }

    public int? RequestedDepth { get; init; }

    public required int GraphNodes { get; init; }

    public required int GraphEdges { get; init; }

    public DependencyEvidencePrefixJson? PackagePrefix { get; init; }

    internal static DependsAssetSummaryJson Create(
        DependsAssetSummary summary,
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
            RequestedDepth = summary.RequestedDepth,
            GraphNodes = summary.GraphNodes,
            GraphEdges = summary.GraphEdges,
            PackagePrefix = summary.PackagePrefix is { } prefix
                ? DependencyEvidencePrefixJson.Create(prefix, tokens)
                : null,
        };
}

internal sealed record DependsAssetRootJson
{
    public required int Root { get; init; }

    public required DependsAssetRootKind Kind { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public required InertString? Input { get; init; }

    public required string Source { get; init; }

    public required DependsRootState State { get; init; }

    public DependencyGraphJsonNodeIdentity? Identity { get; init; }

    public required DependsTraversalCompletion Traversal { get; init; }

    public required DependsEvidenceAvailability Declaration { get; init; }

    public required DependsEvidencePhaseCompletion DeclarationCompletion
        { get; init; }

    public required DependsEvidenceAvailability RestoredRelationships
        { get; init; }

    public required DependsEvidencePhaseCompletion
        RestoredRelationshipCompletion { get; init; }

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

    public required DependsSelectionStatus Selection { get; init; }

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
            Identity = row.GraphIdentity is { } identity
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
                row.DeclarationState == DependsEvidenceAvailability.NotRequested
                    ? 0
                    : evidence?.DeclarationGroupCount ?? 0,
            Declarations =
                row.DeclarationState == DependsEvidenceAvailability.NotRequested
                    ? 0
                    : evidence?.DeclarationCount ?? 0,
            Selection = row.Selection,
            SelectedGroup =
                row.Selection == DependsSelectionStatus.NotRequested
                    ? null
                    : DependencyEvidenceGroupIdentityJson.CreateOptional(
                        row.SelectedGroup),
            SelectedGroupOccurrence =
                row.Selection == DependsSelectionStatus.NotRequested
                    ? null
                    : row.SelectedGroupIndex,
            SelectedSourceOccurrence =
                row.Selection == DependsSelectionStatus.NotRequested
                    ? null
                    : DependencyEvidenceGroupOccurrenceJson.CreateOptional(
                        row.SelectedSourceOccurrence),
            RequestedFramework =
                row.Selection == DependsSelectionStatus.NotRequested
                    ? null
                    : row.RequestedFramework,
            SelectedFramework =
                row.Selection == DependsSelectionStatus.NotRequested
                    ? null
                    : row.SelectedFramework,
            RestoredPackages = row.RestoredRelationshipState
                == DependsEvidenceAvailability.NotRequested
                    ? 0
                    : evidence?.RestoredPackageCount ?? 0,
            RestoredEdges = row.RestoredRelationshipState
                == DependsEvidenceAvailability.NotRequested
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
        DeclarationIdentity { get; init; }

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

    internal static DependsDependencyJson Create(DependsDependencyRow row)
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

internal sealed record DependsFailureJson
{
    public required DependencyEvidenceFailurePhase Phase { get; init; }

    public required string Reason { get; init; }

    public DependencyEvidenceFailureJson? Evidence { get; init; }

    public DependsTraversalFailureJson? Traversal { get; init; }

    internal static DependsFailureJson Create(
        DependsFailureRow row,
        DependencyEvidenceSourceTokens tokens) =>
        row switch
        {
            DependsFailureRow.Evidence evidence => new DependsFailureJson
            {
                Phase = evidence.Value.Phase,
                Reason = evidence.Value.Reason,
                Evidence = DependencyEvidenceFailureJson.Create(
                    evidence.Value,
                    tokens),
            },
            DependsFailureRow.Traversal traversal => new DependsFailureJson
            {
                Phase = DependencyEvidenceFailurePhase.Traversal,
                Reason = traversal.Value.Reason,
                Traversal = DependsTraversalFailureJson.Create(
                    traversal.Value,
                    tokens),
            },
            _ => throw new InvalidOperationException(
                "Unknown depends failure row."),
        };
}

internal sealed record DependsAssetJsonLine
{
    public required string Kind { get; init; }

    public DependencyGraphJsonEdge? DependencyGraph { get; init; }

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
        DependsTraversalFailureRow row,
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
                    row.CandidateOutcome,
                    tokens),
            Manifest = DependsPackageManifestFailureJson.CreateOptional(
                row.ManifestFailure,
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
        DependsAssemblyBindingFailure? failure) =>
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
        DependsRestoredTraversalFailure? failure) =>
        failure switch
        {
            null => null,
            DependsRestoredTraversalFailure.Outcome
            {
                Value:
                    RestoredProjectDependencyTraversalFailure.Document
                        document,
            } =>
                new()
                {
                    Kind = nameof(
                        RestoredProjectDependencyTraversalFailure.Document),
                    DocumentReason = document.Failure.Reason,
                },
            DependsRestoredTraversalFailure.Outcome
            {
                Value:
                    RestoredProjectDependencyTraversalFailure.Graph graph,
            } => CreateGraph(graph.Failure),
            DependsRestoredTraversalFailure.Graph graph =>
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
        DependsTraversalFailureRow row)
    {
        foreach (PackageAuthorityFailure failure in CandidateFailures(
                     row.CandidateOutcome))
        {
            yield return failure.ResultSource
                ?? failure.SourceFailure?.Source;
        }
        foreach (PackageAuthorityFailure failure in ManifestFailures(
                     row.ManifestFailure))
        {
            yield return failure.ResultSource
                ?? failure.SourceFailure?.Source;
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
