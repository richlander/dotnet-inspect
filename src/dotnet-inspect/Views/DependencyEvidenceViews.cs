using DotnetInspector.Sections;
using InertText;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// The Markout document for <c>dependency-evidence</c>. Every string property unwraps a value the
/// query already retained as an <see cref="InertString"/>; composed labels are composed inertly
/// rather than reconstructed after unwrapping.
/// </summary>
[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public sealed class DependencyEvidenceView
{
    [MarkoutIgnore]
    public string Title => "Dependency evidence";

    [MarkoutIgnore]
    [MarkoutSkipNull]
    public string? Description
    {
        get => field;
        init => field = DependencyEvidenceViewText.Optional(value)?.ToString();
    }

    [MarkoutPropertyName("Roots")]
    public int RootCount { get; init; }

    [MarkoutPropertyName("Root Set")]
    public string RootSet
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    } = "";

    [MarkoutPropertyName("Rejected Roots")]
    [MarkoutSkipDefault]
    public int RejectedRootCount { get; init; }

    [MarkoutPropertyName("Failed Roots")]
    [MarkoutSkipDefault]
    public int FailedRootCount { get; init; }

    [MarkoutSkipDefault]
    public bool Truncated { get; init; }

    [MarkoutPropertyName("Complete Declarations")]
    public int CompleteDeclarations { get; init; }

    [MarkoutPropertyName("Incomplete Declarations")]
    [MarkoutSkipDefault]
    public int IncompleteDeclarations { get; init; }

    [MarkoutPropertyName("Unavailable Declarations")]
    [MarkoutSkipDefault]
    public int UnavailableDeclarations { get; init; }

    [MarkoutPropertyName("Failed Declarations")]
    [MarkoutSkipDefault]
    public int FailedDeclarations { get; init; }

    [MarkoutPropertyName("Not Applicable Graphs")]
    [MarkoutSkipDefault]
    public int NotApplicableGraphs { get; init; }

    [MarkoutPropertyName("Complete Graphs")]
    [MarkoutSkipDefault]
    public int CompleteGraphs { get; init; }

    [MarkoutPropertyName("Incomplete Graphs")]
    [MarkoutSkipDefault]
    public int IncompleteGraphs { get; init; }

    [MarkoutPropertyName("Unavailable Graphs")]
    [MarkoutSkipDefault]
    public int UnavailableGraphs { get; init; }

    [MarkoutPropertyName("Failed Graphs")]
    [MarkoutSkipDefault]
    public int FailedGraphs { get; init; }

    [MarkoutIgnore]
    public InertString? PrefixText { get; init; }

    [MarkoutPropertyName("Prefix")]
    [MarkoutSkipNull]
    public string? Prefix => PrefixText?.ToString();

    [MarkoutIgnore]
    public InertString? PrefixSourceText { get; init; }

    [MarkoutPropertyName("Prefix Source")]
    [MarkoutSkipNull]
    public string? PrefixSource => PrefixSourceText?.ToString();

    [MarkoutPropertyName("Prefix Candidates")]
    [MarkoutSkipNull]
    public int? PrefixCandidates { get; init; }

    [MarkoutPropertyName("Prefix Matches")]
    [MarkoutSkipNull]
    public int? PrefixMatches { get; init; }

    [MarkoutPropertyName("Prefix Failures")]
    [MarkoutSkipNull]
    public int? PrefixFailures { get; init; }

    [MarkoutPropertyName("Truncation")]
    [MarkoutSkipNull]
    public string? TruncationReason
    {
        get => field;
        init => field = DependencyEvidenceViewText.Optional(value)?.ToString();
    }

    [MarkoutSection(Name = DependencyEvidenceSections.Dependencies)]
    public List<DependencyEvidenceDependencyView>? Dependencies { get; init; }

    [MarkoutSection(Name = DependencyEvidenceSections.Roots)]
    public List<DependencyEvidenceRootView>? Roots { get; init; }

    [MarkoutSection(Name = DependencyEvidenceSections.RestoredEdges)]
    public List<DependencyEvidenceRestoredEdgeView>? RestoredEdges { get; init; }

    [MarkoutSection(Name = DependencyEvidenceSections.Failures)]
    public List<DependencyEvidenceFailureView>? Failures { get; init; }

    [MarkoutSection(Name = DependencyEvidenceSections.DependencyGroups)]
    public List<DependencyEvidenceGroupView>? DependencyGroups { get; init; }

    [MarkoutSection(Name = DependencyEvidenceSections.RestoredPackages)]
    public List<DependencyEvidenceRestoredPackageView>? RestoredPackages
        { get; init; }
}

/// <summary>
/// The table-only projection of the same row views.
/// </summary>
/// <remarks>
/// <c>--table</c>, <c>--tsv</c>, and <c>--jsonl</c> carry exactly one row schema (see
/// <c>docs/design/output-shapes.md</c>), so this wrapper exposes the sections without the
/// document summary fields. Those fields are a second, differently shaped record; emitting them
/// into a parsed row stream would make the stream two schemas. They stay on Markdown, typed JSON,
/// and lowered JSON, and the partial-evidence warnings still reach stderr.
/// </remarks>
[MarkoutSerializable]
public sealed class DependencyEvidenceTableView
{
    [MarkoutSection(Name = DependencyEvidenceSections.Dependencies)]
    public List<DependencyEvidenceDependencyView>? Dependencies { get; init; }

    [MarkoutSection(Name = DependencyEvidenceSections.Roots)]
    public List<DependencyEvidenceRootView>? Roots { get; init; }

    [MarkoutSection(Name = DependencyEvidenceSections.RestoredEdges)]
    public List<DependencyEvidenceRestoredEdgeView>? RestoredEdges { get; init; }

    [MarkoutSection(Name = DependencyEvidenceSections.Failures)]
    public List<DependencyEvidenceFailureView>? Failures { get; init; }

    [MarkoutSection(Name = DependencyEvidenceSections.DependencyGroups)]
    public List<DependencyEvidenceGroupView>? DependencyGroups { get; init; }

    [MarkoutSection(Name = DependencyEvidenceSections.RestoredPackages)]
    public List<DependencyEvidenceRestoredPackageView>? RestoredPackages
        { get; init; }
}

/// <summary>Containment for values these views compose rather than read.</summary>
/// <remarks>
/// Artifact-authored text arrives already retained as an <see cref="InertString"/>. Canonical
/// identities and owner enum names are composed by the tool; containing them keeps one idiom for
/// every column instead of two rules a reader has to tell apart.
/// </remarks>
internal static class DependencyEvidenceViewText
{
    public static InertString Field(string? value) =>
        new(TextPolicy.Field, value ?? "");

    public static InertString? Optional(string? value) =>
        value is null ? null : Field(value);
}

[MarkoutSerializable]
public sealed class DependencyEvidenceDependencyView
{
    public DependencyEvidenceDependencyView(
        InertString root,
        int group,
        InertString framework,
        InertString package,
        InertString version,
        InertString canonicalPackage,
        InertString canonicalVersion,
        InertString scope,
        InertString source,
        int sourceOccurrences,
        bool selected)
    {
        RootText = root;
        Group = group;
        FrameworkText = framework;
        PackageText = package;
        VersionText = version;
        CanonicalPackageText = canonicalPackage;
        CanonicalVersionText = canonicalVersion;
        ScopeText = scope;
        SourceText = source;
        SourceOccurrences = sourceOccurrences;
        Selected = selected;
    }

    internal static DependencyEvidenceDependencyView From(
        DependencyEvidenceDependencyRow row) =>
        new(
            row.RootDisplay,
            row.GroupIndex + 1,
            row.FrameworkSpelling,
            row.SourcePackageIdSpelling,
            row.SourceVersionConstraintSpelling,
            DependencyEvidenceViewText.Field(row.PackageId),
            DependencyEvidenceViewText.Field(row.VersionConstraint),
            DependencyEvidenceViewText.Field(row.FrameworkScopeKind.ToString()),
            DependencyEvidenceViewText.Field(row.SourceKind.ToString()),
            row.SourceOccurrences,
            row.IsSelectedGroup);

    [MarkoutIgnore] public InertString RootText { get; }
    [MarkoutIgnore] public InertString FrameworkText { get; }
    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString VersionText { get; }
    [MarkoutIgnore] public InertString CanonicalPackageText { get; }
    [MarkoutIgnore] public InertString CanonicalVersionText { get; }
    [MarkoutIgnore] public InertString ScopeText { get; }
    [MarkoutIgnore] public InertString SourceText { get; }

    public string Root => RootText.ToString();

    /// <summary>
    /// The 1-based document-stable group occurrence. Two explicit groups may name the same
    /// framework, so the spelling alone does not tell them apart in a rendered table.
    /// </summary>
    public int Group { get; }

    [MarkoutPropertyName("TFM")]
    public string Framework => FrameworkText.ToString();

    public string Package => PackageText.ToString();

    public string Version => VersionText.ToString();

    [MarkoutPropertyName("Canonical Package")]
    public string CanonicalPackage => CanonicalPackageText.ToString();

    [MarkoutPropertyName("Canonical Version")]
    public string CanonicalVersion => CanonicalVersionText.ToString();

    public string Scope => ScopeText.ToString();

    public string Source => SourceText.ToString();

    [MarkoutPropertyName("Occurrences")]
    public int SourceOccurrences { get; }

    public bool Selected { get; }
}

[MarkoutSerializable]
public sealed class DependencyEvidenceRootView
{
    public DependencyEvidenceRootView(
        InertString root,
        InertString owner,
        InertString source,
        InertString? sourceLabel,
        InertString? package,
        InertString? version,
        InertString? identity,
        InertString? producer,
        InertString? content,
        InertString declaration,
        InertString? declarationCompletion,
        int groups,
        int declarations,
        InertString selection,
        InertString? requestedFramework,
        InertString? selectedFramework,
        InertString graph,
        InertString? graphCompletion,
        int restoredPackages,
        int restoredEdges,
        InertString? restoredTarget,
        InertString? restoredRuntime,
        InertString? targetSelection)
    {
        RootText = root;
        OwnerText = owner;
        SourceText = source;
        SourceLabelText = sourceLabel;
        PackageText = package;
        VersionText = version;
        IdentityText = identity;
        ProducerText = producer;
        ContentText = content;
        DeclarationText = declaration;
        DeclarationCompletionText = declarationCompletion;
        Groups = groups;
        Declarations = declarations;
        SelectionText = selection;
        RequestedFrameworkText = requestedFramework;
        SelectedFrameworkText = selectedFramework;
        GraphText = graph;
        GraphCompletionText = graphCompletion;
        RestoredPackages = restoredPackages;
        RestoredEdges = restoredEdges;
        RestoredTargetText = restoredTarget;
        RestoredRuntimeText = restoredRuntime;
        TargetSelectionText = targetSelection;
    }

    internal static DependencyEvidenceRootView From(
        DependencyEvidenceRootRow row) =>
        new(
            row.Display,
            DependencyEvidenceViewText.Field(row.Owner.ToString()),
            DependencyEvidenceViewText.Field(row.SourceKind.ToString()),
            row.SourceLabel,
            DependencyEvidenceViewText.Optional(row.PackageId),
            DependencyEvidenceViewText.Optional(row.PackageVersion),
            DependencyEvidenceViewText.Optional(
                row.IdentityProvenance?.ToString()),
            row.Source?.Producer.Display,
            DependencyEvidenceViewText.Optional(row.ContentDigest),
            DependencyEvidenceViewText.Field(row.DeclarationState.ToString()),
            DependencyEvidenceViewText.Optional(
                row.DeclarationCompletion?.ToString()),
            row.DeclarationGroupCount,
            row.DeclarationCount,
            DependencyEvidenceViewText.Field(row.SelectionStatus.ToString()),
            row.RequestedFramework,
            row.SelectedFramework,
            DependencyEvidenceViewText.Field(row.GraphState.ToString()),
            DependencyEvidenceViewText.Optional(row.GraphCompletion?.ToString()),
            row.RestoredPackageCount,
            row.RestoredEdgeCount,
            row.RestoredTargetFrameworkSpelling,
            row.RestoredRuntimeIdentifierSpelling,
            DependencyEvidenceViewText.Optional(
                row.RestoredTargetProvenance?.ToString()));

    [MarkoutIgnore] public InertString RootText { get; }
    [MarkoutIgnore] public InertString OwnerText { get; }
    [MarkoutIgnore] public InertString SourceText { get; }
    [MarkoutIgnore] public InertString? SourceLabelText { get; }
    [MarkoutIgnore] public InertString? PackageText { get; }
    [MarkoutIgnore] public InertString? VersionText { get; }
    [MarkoutIgnore] public InertString? IdentityText { get; }
    [MarkoutIgnore] public InertString? ProducerText { get; }
    [MarkoutIgnore] public InertString? ContentText { get; }
    [MarkoutIgnore] public InertString DeclarationText { get; }
    [MarkoutIgnore] public InertString? DeclarationCompletionText { get; }
    [MarkoutIgnore] public InertString SelectionText { get; }
    [MarkoutIgnore] public InertString? RequestedFrameworkText { get; }
    [MarkoutIgnore] public InertString? SelectedFrameworkText { get; }
    [MarkoutIgnore] public InertString GraphText { get; }
    [MarkoutIgnore] public InertString? GraphCompletionText { get; }
    [MarkoutIgnore] public InertString? RestoredTargetText { get; }
    [MarkoutIgnore] public InertString? RestoredRuntimeText { get; }
    [MarkoutIgnore] public InertString? TargetSelectionText { get; }

    public string Root => RootText.ToString();

    public string Owner => OwnerText.ToString();

    public string Source => SourceText.ToString();

    [MarkoutPropertyName("Source Label")]
    public string? SourceLabel => SourceLabelText?.ToString();

    public string? Package => PackageText?.ToString();

    public string? Version => VersionText?.ToString();

    [MarkoutPropertyName("Identity Trust")]
    public string? Identity => IdentityText?.ToString();

    public string? Producer => ProducerText?.ToString();

    [MarkoutPropertyName("Content Digest")]
    public string? Content => ContentText?.ToString();

    public string Declaration => DeclarationText.ToString();

    [MarkoutPropertyName("Declaration Completion")]
    public string? DeclarationCompletion => DeclarationCompletionText?.ToString();

    public int Groups { get; }

    public int Declarations { get; }

    public string Selection => SelectionText.ToString();

    [MarkoutPropertyName("Requested TFM")]
    public string? RequestedFramework => RequestedFrameworkText?.ToString();

    [MarkoutPropertyName("Selected TFM")]
    public string? SelectedFramework => SelectedFrameworkText?.ToString();

    public string Graph => GraphText.ToString();

    [MarkoutPropertyName("Graph Completion")]
    public string? GraphCompletion => GraphCompletionText?.ToString();

    [MarkoutPropertyName("Restored Packages")]
    public int RestoredPackages { get; }

    [MarkoutPropertyName("Restored Edges")]
    public int RestoredEdges { get; }

    [MarkoutPropertyName("Target TFM")]
    public string? RestoredTarget => RestoredTargetText?.ToString();

    [MarkoutPropertyName("Target RID")]
    public string? RestoredRuntime => RestoredRuntimeText?.ToString();

    [MarkoutPropertyName("Target Selection")]
    public string? TargetSelection => TargetSelectionText?.ToString();
}

[MarkoutSerializable]
public sealed class DependencyEvidenceGroupView
{
    public DependencyEvidenceGroupView(
        InertString root,
        int group,
        InertString owner,
        InertString framework,
        InertString scope,
        bool isImplicit,
        int declarations,
        int occurrences,
        bool selected)
    {
        RootText = root;
        Group = group;
        OwnerText = owner;
        FrameworkText = framework;
        ScopeText = scope;
        Implicit = isImplicit;
        Declarations = declarations;
        Occurrences = occurrences;
        Selected = selected;
    }

    internal static DependencyEvidenceGroupView From(
        DependencyEvidenceGroupRow row) =>
        new(
            row.RootDisplay,
            row.GroupIndex + 1,
            DependencyEvidenceViewText.Field(row.Owner.ToString()),
            row.FrameworkSpelling,
            DependencyEvidenceViewText.Field(row.FrameworkScopeKind.ToString()),
            row.IsImplicitManifestGroup,
            row.DeclarationCount,
            row.SourceOccurrenceCount,
            row.IsSelected);

    [MarkoutIgnore] public InertString RootText { get; }
    [MarkoutIgnore] public InertString OwnerText { get; }
    [MarkoutIgnore] public InertString FrameworkText { get; }
    [MarkoutIgnore] public InertString ScopeText { get; }

    public string Root => RootText.ToString();

    /// <summary>The 1-based document-stable group occurrence, matching the Dependencies table.</summary>
    public int Group { get; }

    public string Owner => OwnerText.ToString();

    [MarkoutPropertyName("TFM")]
    public string Framework => FrameworkText.ToString();

    public string Scope => ScopeText.ToString();

    public bool Implicit { get; }

    public int Declarations { get; }

    public int Occurrences { get; }

    public bool Selected { get; }
}

[MarkoutSerializable]
public sealed class DependencyEvidenceRestoredEdgeView
{
    public DependencyEvidenceRestoredEdgeView(
        InertString root,
        InertString parentKind,
        InertString? parent,
        InertString package,
        InertString resolved,
        InertString version,
        InertString canonicalVersion,
        InertString role)
    {
        RootText = root;
        ParentKindText = parentKind;
        ParentText = parent;
        PackageText = package;
        ResolvedText = resolved;
        VersionText = version;
        CanonicalVersionText = canonicalVersion;
        RoleText = role;
    }

    internal static DependencyEvidenceRestoredEdgeView From(
        DependencyEvidenceRestoredEdgeRow row) =>
        new(
            row.RootDisplay,
            DependencyEvidenceViewText.Field(row.ParentKind.ToString()),
            row.ParentPackageId is { } parentId
                ? InertString.Format(
                    TextPolicy.Field,
                    $"{parentId} {row.ParentPackageVersion}")
                : DependencyEvidenceViewText.Optional(row.ParentProjectIdentity),
            DependencyEvidenceViewText.Field(row.PackageId),
            DependencyEvidenceViewText.Field(row.PackageVersion),
            row.SourceVersionConstraintSpelling,
            DependencyEvidenceViewText.Field(row.VersionConstraint),
            DependencyEvidenceViewText.Field(row.Role.ToString()));

    [MarkoutIgnore] public InertString RootText { get; }
    [MarkoutIgnore] public InertString ParentKindText { get; }
    [MarkoutIgnore] public InertString? ParentText { get; }
    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString ResolvedText { get; }
    [MarkoutIgnore] public InertString VersionText { get; }
    [MarkoutIgnore] public InertString CanonicalVersionText { get; }
    [MarkoutIgnore] public InertString RoleText { get; }

    public string Root => RootText.ToString();

    [MarkoutPropertyName("Parent Kind")]
    public string ParentKind => ParentKindText.ToString();

    public string? Parent => ParentText?.ToString();

    public string Package => PackageText.ToString();

    [MarkoutPropertyName("Resolved Version")]
    public string Resolved => ResolvedText.ToString();

    [MarkoutPropertyName("Constraint")]
    public string Version => VersionText.ToString();

    [MarkoutPropertyName("Canonical Constraint")]
    public string CanonicalVersion => CanonicalVersionText.ToString();

    public string Role => RoleText.ToString();
}

[MarkoutSerializable]
public sealed class DependencyEvidenceRestoredPackageView
{
    public DependencyEvidenceRestoredPackageView(
        InertString root,
        InertString package,
        InertString resolved,
        InertString role)
    {
        RootText = root;
        PackageText = package;
        ResolvedText = resolved;
        RoleText = role;
    }

    internal static DependencyEvidenceRestoredPackageView From(
        DependencyEvidenceRestoredPackageRow row) =>
        new(
            row.RootDisplay,
            DependencyEvidenceViewText.Field(row.PackageId),
            DependencyEvidenceViewText.Field(row.PackageVersion),
            DependencyEvidenceViewText.Field(row.Role.ToString()));

    [MarkoutIgnore] public InertString RootText { get; }
    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString ResolvedText { get; }
    [MarkoutIgnore] public InertString RoleText { get; }

    public string Root => RootText.ToString();

    public string Package => PackageText.ToString();

    public string Resolved => ResolvedText.ToString();

    public string Role => RoleText.ToString();
}

[MarkoutSerializable]
public sealed class DependencyEvidenceFailureView
{
    public DependencyEvidenceFailureView(
        InertString phase,
        InertString reason,
        InertString? source,
        InertString? subject,
        int? group,
        InertString? package,
        InertString? version,
        InertString? sourceLabel,
        InertString message,
        int occurrences)
    {
        PhaseText = phase;
        ReasonText = reason;
        SourceText = source;
        SubjectText = subject;
        Group = group;
        PackageText = package;
        VersionText = version;
        SourceLabelText = sourceLabel;
        MessageText = message;
        Occurrences = occurrences;
    }

    internal static DependencyEvidenceFailureView From(
        DependencyEvidenceFailureRow row) =>
        new(
            DependencyEvidenceViewText.Field(row.Phase.ToString()),
            DependencyEvidenceViewText.Field(row.Reason),
            DependencyEvidenceViewText.Optional(row.SourceKind?.ToString()),
            row.Subject,
            row.GroupIndex is { } groupIndex ? groupIndex + 1 : null,
            DependencyEvidenceViewText.Optional(row.PackageId),
            DependencyEvidenceViewText.Optional(row.PackageVersion),
            row.SourceLabel,
            row.Message,
            row.Occurrences);

    [MarkoutIgnore] public InertString PhaseText { get; }
    [MarkoutIgnore] public InertString ReasonText { get; }
    [MarkoutIgnore] public InertString? SourceText { get; }
    [MarkoutIgnore] public InertString? SubjectText { get; }
    [MarkoutIgnore] public InertString? PackageText { get; }
    [MarkoutIgnore] public InertString? VersionText { get; }
    [MarkoutIgnore] public InertString? SourceLabelText { get; }
    [MarkoutIgnore] public InertString MessageText { get; }

    public string Phase => PhaseText.ToString();

    public string Reason => ReasonText.ToString();

    public string? Source => SourceText?.ToString();

    public string? Subject => SubjectText?.ToString();

    /// <summary>
    /// The 1-based document-stable group occurrence a group-scoped failure names, matching the
    /// Dependencies and Dependency Groups tables, or null for a failure no group scopes.
    /// </summary>
    public int? Group { get; }

    public string? Package => PackageText?.ToString();

    public string? Version => VersionText?.ToString();

    [MarkoutPropertyName("Source Label")]
    public string? SourceLabel => SourceLabelText?.ToString();

    public string Message => MessageText.ToString();

    public int Occurrences { get; }
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(DependencyEvidenceView))]
[MarkoutContext(typeof(DependencyEvidenceTableView))]
[MarkoutContext(typeof(DependencyEvidenceDependencyView))]
[MarkoutContext(typeof(DependencyEvidenceRootView))]
[MarkoutContext(typeof(DependencyEvidenceGroupView))]
[MarkoutContext(typeof(DependencyEvidenceRestoredEdgeView))]
[MarkoutContext(typeof(DependencyEvidenceRestoredPackageView))]
[MarkoutContext(typeof(DependencyEvidenceFailureView))]
public partial class DependencyEvidenceViewContext : MarkoutSerializerContext
{
}
