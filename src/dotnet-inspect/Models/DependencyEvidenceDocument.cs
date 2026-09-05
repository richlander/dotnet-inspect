using System.Text.Json.Serialization;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Models;

/// <summary>
/// The source-generated typed JSON document for <c>dependency-evidence</c>.
/// </summary>
/// <remarks>
/// Built from the same <see cref="DependencyEvidenceProjection"/> the Markout view consumes, so
/// no sink reopens an artifact. Machine identity stays typed (numbers, booleans, enums, nested
/// identities), and artifact-authored text stays an <see cref="InertString"/> until the
/// serializer boundary unwraps it.
/// </remarks>
public sealed record DependencyEvidenceDocument
{
    public required DependencyEvidenceSummaryJson Summary { get; init; }

    public List<DependencyEvidenceDependencyJson>? Dependencies { get; init; }

    public List<DependencyEvidenceRootJson>? Roots { get; init; }

    public List<DependencyEvidenceRestoredEdgeJson>? RestoredEdges { get; init; }

    public List<DependencyEvidenceFailureJson>? Failures { get; init; }

    public List<DependencyEvidenceGroupJson>? DependencyGroups { get; init; }

    public List<DependencyEvidenceRestoredPackageJson>? RestoredPackages
        { get; init; }

    /// <summary>
    /// Projects one already-selected and already-windowed view of the typed projection.
    /// </summary>
    public static DependencyEvidenceDocument Create(
        DependencyEvidenceProjection projection,
        IReadOnlySet<string> sections,
        RowWindow? rows)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(sections);

        DependencyEvidenceSourceTokens tokens =
            DependencyEvidenceSourceTokens.Create(projection);
        return new DependencyEvidenceDocument
        {
            Summary = DependencyEvidenceSummaryJson.Create(
                projection.Summary,
                tokens),
            Dependencies = Project(
                sections,
                DependencyEvidenceSections.Dependencies,
                projection.Dependencies,
                rows,
                DependencyEvidenceDependencyJson.Create),
            Roots = Project(
                sections,
                DependencyEvidenceSections.Roots,
                projection.Roots,
                rows,
                row => DependencyEvidenceRootJson.Create(row, tokens)),
            RestoredEdges = Project(
                sections,
                DependencyEvidenceSections.RestoredEdges,
                projection.RestoredEdges,
                rows,
                DependencyEvidenceRestoredEdgeJson.Create),
            Failures = Project(
                sections,
                DependencyEvidenceSections.Failures,
                projection.Failures,
                rows,
                row => DependencyEvidenceFailureJson.Create(row, tokens)),
            DependencyGroups = Project(
                sections,
                DependencyEvidenceSections.DependencyGroups,
                projection.DependencyGroups,
                rows,
                DependencyEvidenceGroupJson.Create),
            RestoredPackages = Project(
                sections,
                DependencyEvidenceSections.RestoredPackages,
                projection.RestoredPackages,
                rows,
                DependencyEvidenceRestoredPackageJson.Create),
        };
    }

    private static List<TJson>? Project<TRow, TJson>(
        IReadOnlySet<string> sections,
        string section,
        IReadOnlyList<TRow> rows,
        RowWindow? window,
        Func<TRow, TJson> select)
    {
        if (!sections.Contains(section))
            return null;

        IReadOnlyList<TRow> selected = window is { IsUnlimited: false } bounded
            ? bounded.Apply(rows)
            : rows;
        return [.. selected.Select(select)];
    }
}

/// <summary>Root-set and aggregate phase completion for one evidence document.</summary>
public sealed record DependencyEvidenceSummaryJson
{
    public required PackageDependencyEvidenceRootSetCompletion RootSetCompletion
        { get; init; }

    public required int AdmittedRoots { get; init; }

    public required int RejectedRoots { get; init; }

    public required int FailedRoots { get; init; }

    public required bool Truncated { get; init; }

    public required int CompleteDeclarations { get; init; }

    public required int IncompleteDeclarations { get; init; }

    public required int UnavailableDeclarations { get; init; }

    public required int FailedDeclarations { get; init; }

    public required int NotApplicableGraphs { get; init; }

    public required int CompleteGraphs { get; init; }

    public required int IncompleteGraphs { get; init; }

    public required int UnavailableGraphs { get; init; }

    public required int FailedGraphs { get; init; }

    public DependencyEvidencePrefixJson? PackagePrefix { get; init; }

    internal static DependencyEvidenceSummaryJson Create(
        DependencyEvidenceSummary summary,
        DependencyEvidenceSourceTokens tokens) =>
        new()
        {
            RootSetCompletion = summary.RootSetCompletion,
            AdmittedRoots = summary.AdmittedRootCount,
            RejectedRoots = summary.RejectedRootCount,
            FailedRoots = summary.FailedRootCount,
            Truncated = summary.IsTruncated,
            CompleteDeclarations = summary.Phases.CompleteDeclarations,
            IncompleteDeclarations = summary.Phases.IncompleteDeclarations,
            UnavailableDeclarations = summary.Phases.UnavailableDeclarations,
            FailedDeclarations = summary.Phases.FailedDeclarations,
            NotApplicableGraphs = summary.Phases.NotApplicableGraphs,
            CompleteGraphs = summary.Phases.CompleteGraphs,
            IncompleteGraphs = summary.Phases.IncompleteGraphs,
            UnavailableGraphs = summary.Phases.UnavailableGraphs,
            FailedGraphs = summary.Phases.FailedGraphs,
            PackagePrefix = summary.PackagePrefix is { } prefix
                ? DependencyEvidencePrefixJson.Create(prefix, tokens)
                : null,
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

/// <summary>One normalized direct package declaration.</summary>
public sealed record DependencyEvidenceDependencyJson
{
    public required int Root { get; init; }

    public required DependencyEvidenceRootIdentityJson RootIdentity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? RootDisplay { get; init; }

    public required PackageDependencyEvidenceRootOwner Owner { get; init; }

    public required PackageDependencyEvidenceSourceKind SourceKind { get; init; }

    /// <summary>The document-stable group occurrence this declaration belongs to.</summary>
    public required int Group { get; init; }

    public required DependencyEvidenceGroupIdentityJson GroupIdentity { get; init; }

    public required string GroupOrderKey { get; init; }

    public required DependencyEvidenceDeclarationIdentityJson DeclarationIdentity
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

    internal static DependencyEvidenceDependencyJson Create(
        DependencyEvidenceDependencyRow row) =>
        new()
        {
            Root = row.RootIndex,
            RootIdentity = DependencyEvidenceRootIdentityJson.Create(
                row.RootIdentity),
            RootDisplay = row.RootDisplay,
            Owner = row.Owner,
            SourceKind = row.SourceKind,
            Group = row.GroupIndex,
            GroupIdentity = DependencyEvidenceGroupIdentityJson.Create(
                row.GroupIdentity),
            GroupOrderKey = row.GroupOrderKey,
            DeclarationIdentity =
                DependencyEvidenceDeclarationIdentityJson.Create(
                    row.DeclarationIdentity),
            FrameworkScope = row.FrameworkScopeKind,
            CanonicalFramework = row.CanonicalFramework,
            FrameworkSpelling = row.FrameworkSpelling,
            PackageId = row.PackageId,
            VersionConstraint = row.VersionConstraint,
            PackageIdSpelling = row.SourcePackageIdSpelling,
            VersionConstraintSpelling = row.SourceVersionConstraintSpelling,
            SourceOccurrences = row.SourceOccurrences,
            SelectedGroup = row.IsSelectedGroup,
        };
}

/// <summary>One admitted root occurrence.</summary>
public sealed record DependencyEvidenceRootJson
{
    public required int Root { get; init; }

    public required DependencyEvidenceRootIdentityJson Identity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Display { get; init; }

    public required PackageDependencyEvidenceRootOwner Owner { get; init; }

    public required PackageDependencyEvidenceSourceKind SourceKind { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? SourceLabel { get; init; }

    public string? PackageId { get; init; }

    public string? PackageVersion { get; init; }

    public PackageManifestIdentityProvenance? IdentityProvenance { get; init; }

    public DependencyEvidenceSourceIdentityJson? Source { get; init; }

    public string? ContentDigest { get; init; }

    public DependencyEvidenceRestoredSelectionIdentityJson? RestoredSelection
        { get; init; }

    public required DependencyEvidenceDeclarationState Declaration { get; init; }

    public PackageDependencyEvidencePhaseCompletion? DeclarationCompletion
        { get; init; }

    public required int DeclarationGroups { get; init; }

    public required int Declarations { get; init; }

    public required PackageDependencyEvidenceSelectionStatus Selection
        { get; init; }

    public DependencyEvidenceGroupIdentityJson? SelectedGroup { get; init; }

    /// <summary>The document-stable occurrence index of the selected group, when one was selected.</summary>
    public int? SelectedGroupOccurrence { get; init; }

    public DependencyEvidenceGroupOccurrenceJson? SelectedSourceOccurrence
        { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? RequestedFramework { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? SelectedFramework { get; init; }

    public required DependencyEvidenceGraphState Graph { get; init; }

    public PackageDependencyEvidencePhaseCompletion? GraphCompletion { get; init; }

    public required int RestoredPackages { get; init; }

    public required int RestoredEdges { get; init; }

    public string? TargetFrameworkIdentity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? TargetFrameworkSpelling { get; init; }

    public string? TargetRuntimeIdentifier { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? TargetRuntimeIdentifierSpelling { get; init; }

    public RestoredProjectTargetSelectionProvenance? TargetSelection { get; init; }

    internal static DependencyEvidenceRootJson Create(
        DependencyEvidenceRootRow row,
        DependencyEvidenceSourceTokens tokens) =>
        new()
        {
            Root = row.RootIndex,
            Identity = DependencyEvidenceRootIdentityJson.Create(row.Identity),
            Display = row.Display,
            Owner = row.Owner,
            SourceKind = row.SourceKind,
            SourceLabel = row.SourceLabel,
            PackageId = row.PackageId,
            PackageVersion = row.PackageVersion,
            IdentityProvenance = row.IdentityProvenance,
            Source = tokens.Project(row.Source),
            ContentDigest = row.ContentDigest,
            RestoredSelection = row.RestoredSelection is { } restoredSelection
                ? DependencyEvidenceRestoredSelectionIdentityJson.Create(
                    restoredSelection)
                : null,
            Declaration = row.DeclarationState,
            DeclarationCompletion = row.DeclarationCompletion,
            DeclarationGroups = row.DeclarationGroupCount,
            Declarations = row.DeclarationCount,
            Selection = row.SelectionStatus,
            SelectedGroup = DependencyEvidenceGroupIdentityJson.CreateOptional(
                row.SelectedGroup),
            SelectedGroupOccurrence = row.SelectedGroupIndex,
            SelectedSourceOccurrence =
                DependencyEvidenceGroupOccurrenceJson.CreateOptional(
                    row.SelectedSourceOccurrence),
            RequestedFramework = row.RequestedFramework,
            SelectedFramework = row.SelectedFramework,
            Graph = row.GraphState,
            GraphCompletion = row.GraphCompletion,
            RestoredPackages = row.RestoredPackageCount,
            RestoredEdges = row.RestoredEdgeCount,
            TargetFrameworkIdentity = row.RestoredTargetFrameworkIdentity,
            TargetFrameworkSpelling = row.RestoredTargetFrameworkSpelling,
            TargetRuntimeIdentifier = row.RestoredRuntimeIdentifier,
            TargetRuntimeIdentifierSpelling =
                row.RestoredRuntimeIdentifierSpelling,
            TargetSelection = row.RestoredTargetProvenance,
        };
}

/// <summary>One normalized logical declaration group.</summary>
public sealed record DependencyEvidenceGroupJson
{
    public required int Root { get; init; }

    public required DependencyEvidenceRootIdentityJson RootIdentity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? RootDisplay { get; init; }

    public required PackageDependencyEvidenceRootOwner Owner { get; init; }

    /// <summary>The document-stable group occurrence, matching the Dependencies rows.</summary>
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

/// <summary>One typed failure record and its occurrence count.</summary>
public sealed record DependencyEvidenceFailureJson
{
    public required DependencyEvidenceFailurePhase Phase { get; init; }

    public required string Reason { get; init; }

    public PackageDependencyEvidenceSourceKind? SourceKind { get; init; }

    public int? Root { get; init; }

    public DependencyEvidenceRootIdentityJson? RootIdentity { get; init; }

    public DependencyEvidenceGroupIdentityJson? GroupIdentity { get; init; }

    /// <summary>The document-stable group occurrence, when the failure names one.</summary>
    public int? Group { get; init; }

    public DependencyEvidenceSourceIdentityJson? Source { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Subject { get; init; }

    public string? PackageId { get; init; }

    public string? PackageVersion { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? SourceLabel { get; init; }

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
            Root = row.RootIndex,
            RootIdentity = DependencyEvidenceRootIdentityJson.CreateOptional(
                row.RootIdentity),
            GroupIdentity = DependencyEvidenceGroupIdentityJson.CreateOptional(
                row.Group),
            Group = row.GroupIndex,
            Source = tokens.Project(row.Source),
            Subject = row.Subject,
            PackageId = row.PackageId,
            PackageVersion = row.PackageVersion,
            SourceLabel = row.SourceLabel,
            Message = row.Message,
            Occurrences = row.Occurrences,
        };
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DependencyEvidenceDocument))]
internal partial class DependencyEvidenceJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DependencyEvidenceDocument))]
internal partial class DependencyEvidenceCompactJsonContext : JsonSerializerContext
{
}
