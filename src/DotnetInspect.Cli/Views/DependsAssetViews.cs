using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public sealed class DependsAssetView
{
    [MarkoutIgnore]
    public string Title => "Dependencies";

    [MarkoutIgnore]
    [MarkoutSkipNull]
    public string? Description
    {
        get => field;
        init => field = DependencyEvidenceViewText.Optional(value)?.ToString();
    }

    [MarkoutPropertyName("Root Set")]
    public required string RootSet
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }

    [MarkoutPropertyName("Requested Roots")]
    public required int RequestedRoots { get; init; }

    [MarkoutPropertyName("Admitted Roots")]
    public required int AdmittedRoots { get; init; }

    [MarkoutPropertyName("Failed Roots")]
    [MarkoutSkipDefault]
    public int FailedRoots { get; init; }

    public required string Traversal
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }

    public required string Declarations
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }

    [MarkoutPropertyName("Restored Relationships")]
    public required string RestoredRelationships
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }

    [MarkoutPropertyName("Requested Depth")]
    [MarkoutSkipNull]
    public int? RequestedDepth { get; init; }

    [MarkoutPropertyName("Graph Nodes")]
    public required int GraphNodes { get; init; }

    [MarkoutPropertyName("Graph Edges")]
    public required int GraphEdges { get; init; }

    [MarkoutIgnore]
    public InertString? PrefixText { get; init; }

    [MarkoutPropertyName("Prefix")]
    [MarkoutSkipNull]
    public string? Prefix => PrefixText?.ToString();

    [MarkoutPropertyName("Prefix Candidates")]
    [MarkoutSkipNull]
    public int? PrefixCandidates { get; init; }

    [MarkoutPropertyName("Prefix Matches")]
    [MarkoutSkipNull]
    public int? PrefixMatches { get; init; }

    [MarkoutPropertyName("Prefix Failures")]
    [MarkoutSkipNull]
    public int? PrefixFailures { get; init; }

    [MarkoutPropertyName("Prefix Truncation")]
    [MarkoutSkipNull]
    public string? PrefixTruncation
    {
        get => field;
        init => field = DependencyEvidenceViewText.Optional(value)?.ToString();
    }

    [MarkoutSection(
        Name = DependsAssetSections.DependencyGraph,
        EmptyText = "No dependency relationships.")]
    public Markout.Graph? DependencyGraph { get; init; }

    [MarkoutSection(Name = DependsAssetSections.Roots)]
    public List<DependsRootView>? Roots { get; init; }

    [MarkoutSection(Name = DependsAssetSections.Dependencies)]
    public List<DependsDependencyView>? Dependencies { get; init; }

    [MarkoutSection(Name = DependsAssetSections.RestoredEdges)]
    public List<DependsRestoredEdgeView>? RestoredEdges { get; init; }

    [MarkoutSection(Name = DependsAssetSections.Failures)]
    public List<DependsFailureView>? Failures { get; init; }

    [MarkoutSection(Name = DependsAssetSections.DependencyGroups)]
    public List<DependsDependencyGroupView>? DependencyGroups { get; init; }

    [MarkoutSection(Name = DependsAssetSections.RestoredPackages)]
    public List<DependsRestoredPackageView>? RestoredPackages
        { get; init; }
}

[MarkoutSerializable]
public sealed class DependsAssetTableView
{
    [MarkoutSection(Name = DependsAssetSections.DependencyGraph)]
    public List<DependsGraphEdgeView>? DependencyGraph { get; init; }

    [MarkoutSection(Name = DependsAssetSections.Roots)]
    public List<DependsRootView>? Roots { get; init; }

    [MarkoutSection(Name = DependsAssetSections.Dependencies)]
    public List<DependsDependencyView>? Dependencies { get; init; }

    [MarkoutSection(Name = DependsAssetSections.RestoredEdges)]
    public List<DependsRestoredEdgeView>? RestoredEdges { get; init; }

    [MarkoutSection(Name = DependsAssetSections.Failures)]
    public List<DependsFailureView>? Failures { get; init; }

    [MarkoutSection(Name = DependsAssetSections.DependencyGroups)]
    public List<DependsDependencyGroupView>? DependencyGroups { get; init; }

    [MarkoutSection(Name = DependsAssetSections.RestoredPackages)]
    public List<DependsRestoredPackageView>? RestoredPackages
        { get; init; }
}

[MarkoutSerializable]
public sealed class DependsGraphTableView
{
    [MarkoutSection(Name = DependsAssetSections.DependencyGraph)]
    public List<DependsGraphEdgeView>? DependencyGraph { get; init; }
}

[MarkoutSerializable]
public sealed class DependsRootView
{
    internal static DependsRootView From(
        DependsRootRow row,
        DependencyEvidenceSourceTokens tokens)
    {
        DependencyEvidenceRootRow? evidence = row.Evidence;
        return new DependsRootView
        {
            Root = row.Occurrence,
            KindText = DependencyEvidenceViewText.Field(row.Kind.ToString()),
            InputText = row.Input,
            SourceText = DependencyEvidenceViewText.Field(row.Source),
            StateText = DependencyEvidenceViewText.Field(row.State.ToString()),
            IdentityKindText =
                DependencyEvidenceViewText.Optional(row.IdentityKind),
            IdentityText = row.Identity,
            TraversalText =
                DependencyEvidenceViewText.Field(row.Traversal.ToString()),
            OwnerText = evidence is null
                ? null
                : DependencyEvidenceViewText.Field(
                    evidence.Owner.ToString()),
            SourceLabelText = evidence?.SourceLabel,
            PackageText =
                DependencyEvidenceViewText.Optional(evidence?.PackageId),
            VersionText =
                DependencyEvidenceViewText.Optional(evidence?.PackageVersion),
            IdentityTrustText = DependencyEvidenceViewText.Optional(
                evidence?.IdentityProvenance?.ToString()),
            ProducerText = evidence?.Source?.Producer.Display,
            SourceAssociation = evidence?.Source is { } source
                ? tokens.Project(source)?.Association
                : null,
            ContentDigestText =
                DependencyEvidenceViewText.Optional(evidence?.ContentDigest),
            DeclarationText = DependencyEvidenceViewText.Field(
                row.DeclarationState.ToString()),
            DeclarationCompletionText = DependencyEvidenceViewText.Field(
                row.DeclarationCompletion.ToString()),
            Groups =
                row.DeclarationState == DependsEvidenceAvailability.NotRequested
                    ? 0
                    : evidence?.DeclarationGroupCount,
            DeclarationCount =
                row.DeclarationState == DependsEvidenceAvailability.NotRequested
                    ? 0
                    : evidence?.DeclarationCount,
            SelectionText = DependencyEvidenceViewText.Field(
                row.Selection.ToString()),
            SelectedGroup =
                row.Selection != DependsSelectionStatus.NotRequested
                && row.SelectedGroupIndex is { } group
                ? group + 1
                : null,
            SelectedSourceOccurrenceText =
                row.Selection == DependsSelectionStatus.NotRequested
                    ? null
                    : FormatSelectedSourceOccurrence(
                        row.SelectedSourceOccurrence),
            RequestedFrameworkText =
                row.Selection == DependsSelectionStatus.NotRequested
                    ? null
                    : row.RequestedFramework,
            SelectedFrameworkText =
                row.Selection == DependsSelectionStatus.NotRequested
                    ? null
                    : row.SelectedFramework,
            RestoredRelationshipText = DependencyEvidenceViewText.Field(
                row.RestoredRelationshipState.ToString()),
            RestoredRelationshipCompletionText =
                DependencyEvidenceViewText.Field(
                    row.RestoredRelationshipCompletion.ToString()),
            RestoredPackages = row.RestoredRelationshipState
                == DependsEvidenceAvailability.NotRequested
                    ? 0
                    : evidence?.RestoredPackageCount,
            RestoredEdges = row.RestoredRelationshipState
                == DependsEvidenceAvailability.NotRequested
                    ? 0
                    : evidence?.RestoredEdgeCount,
            TargetFrameworkText =
                evidence?.RestoredTargetFrameworkSpelling,
            TargetRuntimeText =
                evidence?.RestoredRuntimeIdentifierSpelling,
            TargetSelectionText = DependencyEvidenceViewText.Optional(
                evidence?.RestoredTargetProvenance?.ToString()),
        };
    }

    private static InertString? FormatSelectedSourceOccurrence(
        PackageDependencyEvidenceGroupOccurrence? occurrence) =>
        occurrence switch
        {
            null => null,
            PackageDependencyEvidenceGroupOccurrence.Package package =>
                DependencyEvidenceViewText.Field(
                    $"package:{package.SourceIndex}"),
            PackageDependencyEvidenceGroupOccurrence.RestoredProject
                restored => DependencyEvidenceViewText.Field(
                    $"restored-project:"
                    + $"{restored.Identity.Selection.TargetIdentity}:"
                    + $"{restored.Identity.Selection.FactsDigest}:"
                    + restored.Identity.PivotIdentity),
            PackageDependencyEvidenceGroupOccurrence.AuthoredProjectTarget
                authored => InertString.Format(
                    TextPolicy.Field,
                    $"authored-target:"
                    + $"{DependencyEvidenceViewText.Field(authored.Identity.Kind.ToString())}:"
                    + $"{DependencyEvidenceViewText.Field(authored.Identity.CanonicalFramework ?? "")}:"
                    + $"{DependencyEvidenceViewText.Field(authored.SyntaxContextIdentity)}:"
                    + $"{authored.SourceSpelling}"),
            PackageDependencyEvidenceGroupOccurrence.AuthoredProjectDeclaration
                authored => DependencyEvidenceViewText.Field(
                    $"authored-declaration:"
                    + $"{authored.Identity.FactsDigest}:"
                    + authored.SourceOccurrenceCount),
            _ => throw new InvalidOperationException(
                "Unknown selected source occurrence."),
        };

    public int Root { get; init; }

    [MarkoutIgnore]
    public InertString KindText { get; init; }

    [MarkoutIgnore]
    public InertString InputText { get; init; }

    [MarkoutIgnore]
    public InertString SourceText { get; init; }

    [MarkoutIgnore]
    public InertString StateText { get; init; }

    [MarkoutIgnore]
    public InertString? IdentityKindText { get; init; }

    [MarkoutIgnore]
    public InertString? IdentityText { get; init; }

    [MarkoutIgnore]
    public InertString TraversalText { get; init; }

    [MarkoutIgnore] public InertString? OwnerText { get; init; }
    [MarkoutIgnore] public InertString? SourceLabelText { get; init; }
    [MarkoutIgnore] public InertString? PackageText { get; init; }
    [MarkoutIgnore] public InertString? VersionText { get; init; }
    [MarkoutIgnore] public InertString? IdentityTrustText { get; init; }
    [MarkoutIgnore] public InertString? ProducerText { get; init; }
    [MarkoutIgnore] public InertString? ContentDigestText { get; init; }
    [MarkoutIgnore] public InertString DeclarationText { get; init; }
    [MarkoutIgnore]
    public InertString DeclarationCompletionText { get; init; }
    [MarkoutIgnore] public InertString? SelectionText { get; init; }
    [MarkoutIgnore]
    public InertString? SelectedSourceOccurrenceText { get; init; }
    [MarkoutIgnore] public InertString? RequestedFrameworkText { get; init; }
    [MarkoutIgnore] public InertString? SelectedFrameworkText { get; init; }
    [MarkoutIgnore]
    public InertString RestoredRelationshipText { get; init; }
    [MarkoutIgnore]
    public InertString RestoredRelationshipCompletionText { get; init; }
    [MarkoutIgnore] public InertString? TargetFrameworkText { get; init; }
    [MarkoutIgnore] public InertString? TargetRuntimeText { get; init; }
    [MarkoutIgnore] public InertString? TargetSelectionText { get; init; }

    public string Kind => KindText.ToString();

    public string Input => InputText.ToString();

    public string Source => SourceText.ToString();

    public string State => StateText.ToString();

    [MarkoutPropertyName("Identity Kind")]
    public string? IdentityKind => IdentityKindText?.ToString();

    public string? Identity => IdentityText?.ToString();

    public string Traversal => TraversalText.ToString();

    public string? Owner => OwnerText?.ToString();

    [MarkoutPropertyName("Source Label")]
    public string? SourceLabel => SourceLabelText?.ToString();

    public string? Package => PackageText?.ToString();

    public string? Version => VersionText?.ToString();

    [MarkoutPropertyName("Identity Trust")]
    public string? IdentityTrust => IdentityTrustText?.ToString();

    public string? Producer => ProducerText?.ToString();

    [MarkoutPropertyName("Source Association")]
    public int? SourceAssociation { get; init; }

    [MarkoutPropertyName("Content Digest")]
    public string? ContentDigest => ContentDigestText?.ToString();

    public string Declaration => DeclarationText.ToString();

    [MarkoutPropertyName("Declaration Completion")]
    public string DeclarationCompletion =>
        DeclarationCompletionText.ToString();

    public int? Groups { get; init; }

    [MarkoutPropertyName("Declarations")]
    public int? DeclarationCount { get; init; }

    public string? Selection => SelectionText?.ToString();

    [MarkoutPropertyName("Selected Group")]
    public int? SelectedGroup { get; init; }

    [MarkoutPropertyName("Selected Source Occurrence")]
    public string? SelectedSourceOccurrence =>
        SelectedSourceOccurrenceText?.ToString();

    [MarkoutPropertyName("Requested TFM")]
    public string? RequestedFramework => RequestedFrameworkText?.ToString();

    [MarkoutPropertyName("Selected TFM")]
    public string? SelectedFramework => SelectedFrameworkText?.ToString();

    [MarkoutPropertyName("Restored Relationships")]
    public string RestoredRelationships =>
        RestoredRelationshipText.ToString();

    [MarkoutPropertyName("Restored Relationship Completion")]
    public string RestoredRelationshipCompletion =>
        RestoredRelationshipCompletionText.ToString();

    [MarkoutPropertyName("Restored Packages")]
    public int? RestoredPackages { get; init; }

    [MarkoutPropertyName("Restored Edges")]
    public int? RestoredEdges { get; init; }

    [MarkoutPropertyName("Target TFM")]
    public string? TargetFramework => TargetFrameworkText?.ToString();

    [MarkoutPropertyName("Target RID")]
    public string? TargetRuntime => TargetRuntimeText?.ToString();

    [MarkoutPropertyName("Target Selection")]
    public string? TargetSelection => TargetSelectionText?.ToString();
}

[MarkoutSerializable]
public sealed class DependsGraphEdgeView
{
    internal static DependsGraphEdgeView From(DependencyGraphEdgeRow row) =>
        new()
        {
            Roots = string.Join(",", row.RootOccurrences),
            SourceKind = row.SourceKind,
            SourceIdentityText = row.SourceIdentityText,
            SourceText = row.SourceText,
            Relationship = row.Relationship,
            TargetKind = row.TargetKind,
            TargetIdentityText = row.TargetIdentityText,
            TargetText = row.TargetText,
            MinimumDepth = row.MinimumDepth,
            Resolution = row.Resolution,
            EvidenceKind = row.EvidenceKind,
            EvidenceIdentityText = row.EvidenceIdentityText,
        };

    public required string Roots
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }

    [MarkoutPropertyName("Source Kind")]
    public required string SourceKind
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }

    [MarkoutPropertyName("Source Identity")]
    public string SourceIdentity => SourceIdentityText.ToString();

    public string Source => SourceText.ToString();

    public required string Relationship
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }

    [MarkoutPropertyName("Target Kind")]
    public required string TargetKind
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }

    [MarkoutPropertyName("Target Identity")]
    public string TargetIdentity => TargetIdentityText.ToString();

    public string Target => TargetText.ToString();

    [MarkoutPropertyName("Minimum Depth")]
    public required int MinimumDepth { get; init; }

    public required string Resolution
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }

    [MarkoutPropertyName("Evidence Kind")]
    public string? EvidenceKind
    {
        get => field;
        init => field = DependencyEvidenceViewText.Optional(value)?.ToString();
    }

    [MarkoutPropertyName("Evidence Identity")]
    public string? EvidenceIdentity => EvidenceIdentityText?.ToString();

    [MarkoutIgnore] public InertString SourceIdentityText { get; init; }
    [MarkoutIgnore] public InertString SourceText { get; init; }
    [MarkoutIgnore] public InertString TargetIdentityText { get; init; }
    [MarkoutIgnore] public InertString TargetText { get; init; }
    [MarkoutIgnore] public InertString? EvidenceIdentityText { get; init; }
}

[MarkoutSerializable]
public sealed class DependsDependencyView
{
    internal static DependsDependencyView From(
        DependsDependencyRow row) =>
        new()
        {
            RootText = row.Declaration.RootDisplay,
            Group = row.Declaration.GroupIndex + 1,
            FrameworkText = row.Declaration.FrameworkSpelling,
            PackageText = row.Declaration.SourcePackageIdSpelling,
            VersionText = row.Declaration.SourceVersionConstraintSpelling,
            CanonicalPackage = row.Declaration.PackageId,
            CanonicalVersion = row.Declaration.VersionConstraint,
            Resolved = row.ResolvedVersion,
            Scope = row.Declaration.FrameworkScopeKind.ToString(),
            Source = row.Declaration.SourceKind.ToString(),
            Occurrences = row.Declaration.SourceOccurrences,
            Selected = row.Declaration.IsSelectedGroup,
        };

    [MarkoutIgnore] public InertString RootText { get; init; }
    [MarkoutIgnore] public InertString FrameworkText { get; init; }
    [MarkoutIgnore] public InertString PackageText { get; init; }
    [MarkoutIgnore] public InertString VersionText { get; init; }

    public string Root => RootText.ToString();
    public required int Group { get; init; }
    [MarkoutPropertyName("TFM")]
    public string Framework => FrameworkText.ToString();
    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string? Resolved
    {
        get => field;
        init => field = DependencyEvidenceViewText.Optional(value)?.ToString();
    }
    [MarkoutPropertyName("Canonical Package")]
    public required string CanonicalPackage
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    [MarkoutPropertyName("Canonical Version")]
    public required string CanonicalVersion
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    public required string Scope
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    public required string Source
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    public required int Occurrences { get; init; }
    public required bool Selected { get; init; }
}

[MarkoutSerializable]
public sealed class DependsDependencyGroupView
{
    internal static DependsDependencyGroupView From(
        DependencyEvidenceGroupRow row) =>
        new()
        {
            RootText = row.RootDisplay,
            Group = row.GroupIndex + 1,
            Owner = row.Owner.ToString(),
            FrameworkText = row.FrameworkSpelling,
            Scope = row.FrameworkScopeKind.ToString(),
            Implicit = row.IsImplicitManifestGroup,
            Declarations = row.DeclarationCount,
            Occurrences = row.SourceOccurrenceCount,
            Selected = row.IsSelected,
        };

    [MarkoutIgnore] public InertString RootText { get; init; }
    [MarkoutIgnore] public InertString FrameworkText { get; init; }

    public string Root => RootText.ToString();
    public required int Group { get; init; }
    public required string Owner
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    [MarkoutPropertyName("TFM")]
    public string Framework => FrameworkText.ToString();
    public required string Scope
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    public required bool Implicit { get; init; }
    public required int Declarations { get; init; }
    public required int Occurrences { get; init; }
    public required bool Selected { get; init; }
}

[MarkoutSerializable]
public sealed class DependsRestoredEdgeView
{
    internal static DependsRestoredEdgeView From(
        DependencyEvidenceRestoredEdgeRow row) =>
        new()
        {
            RootText = row.RootDisplay,
            ParentKind = row.ParentKind.ToString(),
            Parent = row.ParentPackageId is { } package
                ? $"{package} {row.ParentPackageVersion}"
                : row.ParentProjectIdentity,
            Package = row.PackageId,
            Resolved = row.PackageVersion,
            VersionText = row.SourceVersionConstraintSpelling,
            CanonicalVersion = row.VersionConstraint,
            Role = row.Role.ToString(),
        };

    [MarkoutIgnore] public InertString RootText { get; init; }
    [MarkoutIgnore] public InertString VersionText { get; init; }

    public string Root => RootText.ToString();
    [MarkoutPropertyName("Parent Kind")]
    public required string ParentKind
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    public string? Parent
    {
        get => field;
        init => field = DependencyEvidenceViewText.Optional(value)?.ToString();
    }
    public required string Package
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    [MarkoutPropertyName("Resolved Version")]
    public required string Resolved
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    [MarkoutPropertyName("Constraint")]
    public string Version => VersionText.ToString();
    [MarkoutPropertyName("Canonical Constraint")]
    public required string CanonicalVersion
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    public required string Role
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
}

[MarkoutSerializable]
public sealed class DependsRestoredPackageView
{
    internal static DependsRestoredPackageView From(
        DependencyEvidenceRestoredPackageRow row) =>
        new()
        {
            RootText = row.RootDisplay,
            Package = row.PackageId,
            Resolved = row.PackageVersion,
            Role = row.Role.ToString(),
        };

    [MarkoutIgnore] public InertString RootText { get; init; }

    public string Root => RootText.ToString();
    public required string Package
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    public required string Resolved
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    public required string Role
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
}

[MarkoutSerializable]
public sealed class DependsFailureView
{
    internal static DependsFailureView From(
        DependsFailureRow row) =>
        row switch
        {
            DependsFailureRow.Evidence evidence => FromEvidence(
                evidence.Value),
            DependsFailureRow.Traversal traversal => FromTraversal(
                traversal.Value),
            _ => throw new InvalidOperationException(
                "Unknown depends failure row."),
        };

    private static DependsFailureView FromEvidence(
        DependencyEvidenceFailureRow row) =>
        new()
        {
            Phase = row.Phase.ToString(),
            Reason = row.Reason,
            Source = row.SourceKind?.ToString(),
            SubjectText = row.Subject,
            Group = row.GroupIndex is { } group ? group + 1 : null,
            Package = row.PackageId,
            Version = row.PackageVersion,
            SourceLabelText = row.SourceLabel,
            MessageText = row.Message,
            AffectedRoots = row.RootIndex?.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            Occurrences = row.Occurrences,
        };

    private static DependsFailureView FromTraversal(
        DependsTraversalFailureRow row) =>
        new()
        {
            Phase = DependencyEvidenceFailurePhase.Traversal.ToString(),
            Reason = row.Reason,
            Source = null,
            SubjectText = row.AssemblyBindingFailure is { } binding
                ? DependencyEvidenceViewText.Field(
                    AssemblyIdentityFormatter.Format(
                        binding.RequestedAssembly))
                : row.PackageId is { } packageId
                    ? DependencyEvidenceViewText.Field(packageId)
                    : null,
            Group = null,
            Package = row.PackageId,
            Version = row.VersionConstraint,
            SourceLabelText = null,
            MessageText = DependencyEvidenceViewText.Field(
                DescribeTraversalFailure(row)),
            AffectedRoots = string.Join(",", row.AffectedRootOccurrences),
            Occurrences = row.AffectedRootOccurrences.Length,
        };

    private static string DescribeTraversalFailure(
        DependsTraversalFailureRow row) =>
        row.CandidateOutcome switch
        {
            PackageDependencyTraversalCandidateResult.Failed =>
                "No exact package candidate was issued for this declaration.",
            PackageDependencyTraversalCandidateResult.Incomplete =>
                "Package candidate resolution did not complete.",
            _ when row.BudgetLimit is { } limit =>
                $"The package traversal work budget of {limit} was exhausted.",
            _ when row.ManifestFailure is not null =>
                "A transitive package manifest could not be acquired or projected.",
            _ when row.RestoredFailure
                is DependsRestoredTraversalFailure.Outcome
                {
                    Value:
                        RestoredProjectDependencyTraversalFailure.Document
                            document,
                } => document.Failure.Message,
            _ when row.RestoredFailure
                is DependsRestoredTraversalFailure.Outcome
                {
                    Value:
                        RestoredProjectDependencyTraversalFailure.Graph graph,
                } => graph.Failure.Message,
            _ when row.RestoredFailure
                is DependsRestoredTraversalFailure.Graph graph =>
                    graph.Value.Message,
            _ when row.AssemblyBindingFailure is not null =>
                "No assembly binding candidate matched the requested identity.",
            _ => "Package traversal did not complete.",
        };

    public required string Phase
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    public required string Reason
    {
        get => field;
        init => field = DependencyEvidenceViewText.Field(value).ToString();
    }
    public string? Source
    {
        get => field;
        init => field = DependencyEvidenceViewText.Optional(value)?.ToString();
    }
    [MarkoutIgnore] public InertString? SubjectText { get; init; }
    [MarkoutIgnore] public InertString? SourceLabelText { get; init; }
    [MarkoutIgnore] public InertString MessageText { get; init; }

    public string? Subject => SubjectText?.ToString();
    public int? Group { get; init; }
    public string? Package
    {
        get => field;
        init => field = DependencyEvidenceViewText.Optional(value)?.ToString();
    }
    public string? Version
    {
        get => field;
        init => field = DependencyEvidenceViewText.Optional(value)?.ToString();
    }
    [MarkoutPropertyName("Source Label")]
    public string? SourceLabel => SourceLabelText?.ToString();
    public string Message => MessageText.ToString();
    [MarkoutPropertyName("Affected Roots")]
    public string? AffectedRoots
    {
        get => field;
        init => field = DependencyEvidenceViewText.Optional(value)?.ToString();
    }
    public required int Occurrences { get; init; }
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(DependsAssetView))]
[MarkoutContext(typeof(DependsAssetTableView))]
[MarkoutContext(typeof(DependsGraphTableView))]
[MarkoutContext(typeof(DependsRootView))]
[MarkoutContext(typeof(DependsGraphEdgeView))]
[MarkoutContext(typeof(DependsDependencyView))]
[MarkoutContext(typeof(DependsDependencyGroupView))]
[MarkoutContext(typeof(DependsRestoredEdgeView))]
[MarkoutContext(typeof(DependsRestoredPackageView))]
[MarkoutContext(typeof(DependsFailureView))]
public partial class DependsAssetViewContext : MarkoutSerializerContext
{
}
