using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using DotnetInspect.Cli.Sections;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Models;

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

/// <summary>One normalized logical declaration group.</summary>
public sealed record DependencyEvidenceGroupJson
{
    public required int Root { get; init; }

    public required DependencyEvidenceRootIdentityJson RootIdentity { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? RootDisplay { get; init; }

    public required PackageDependencyEvidenceInputKind Owner { get; init; }

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

    public PackageDependencyEvidenceAcquisitionForm? SourceKind { get; init; }

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

    public string? EvidenceIdentity { get; init; }

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
            EvidenceIdentity = row.EvidenceIdentity,
        };
}
