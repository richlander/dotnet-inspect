using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Models;

/// <summary>Which owner issued one projected identity.</summary>
public enum DependencyEvidenceIdentityOwner
{
    Package,
    RestoredProject,
}

/// <summary>One package root's owner-issued coordinate.</summary>
public sealed record DependencyEvidencePackageCoordinateJson
{
    public required string PackageId { get; init; }

    public required string Version { get; init; }

    internal static DependencyEvidencePackageCoordinateJson Create(
        PackageSourceCoordinate coordinate) =>
        new()
        {
            PackageId = coordinate.PackageId,
            Version = coordinate.Version,
        };
}

/// <summary>One restored-project selection identity.</summary>
public sealed record DependencyEvidenceRestoredSelectionIdentityJson
{
    public required string TargetIdentity { get; init; }

    public required string FactsDigest { get; init; }

    internal static DependencyEvidenceRestoredSelectionIdentityJson Create(
        RestoredProjectSelectionIdentity identity) =>
        new()
        {
            TargetIdentity = identity.TargetIdentity,
            FactsDigest = identity.FactsDigest,
        };
}

/// <summary>
/// One admitted root's owner-issued identity.
/// </summary>
/// <remarks>
/// The query's identity is a closed union, so this DTO names its family explicitly and carries
/// exactly the member the family selects. The abstract query record is never serialized directly:
/// a polymorphic contract would leak the owner's type hierarchy into the CLI's JSON shape.
/// </remarks>
public sealed record DependencyEvidenceRootIdentityJson
{
    public required DependencyEvidenceIdentityOwner Owner { get; init; }

    public DependencyEvidencePackageCoordinateJson? Package { get; init; }

    public DependencyEvidenceRestoredSelectionIdentityJson? RestoredProject
        { get; init; }

    internal static DependencyEvidenceRootIdentityJson Create(
        PackageDependencyEvidenceRootIdentity identity) =>
        identity switch
        {
            PackageDependencyEvidenceRootIdentity.Package package => new()
            {
                Owner = DependencyEvidenceIdentityOwner.Package,
                Package = DependencyEvidencePackageCoordinateJson.Create(
                    package.Coordinate),
            },
            PackageDependencyEvidenceRootIdentity.RestoredProject restored => new()
            {
                Owner = DependencyEvidenceIdentityOwner.RestoredProject,
                RestoredProject =
                    DependencyEvidenceRestoredSelectionIdentityJson.Create(
                        restored.Identity.Selection),
            },
            _ => throw new InvalidOperationException(
                "Unknown package dependency evidence root identity."),
        };

    internal static DependencyEvidenceRootIdentityJson? CreateOptional(
        PackageDependencyEvidenceRootIdentity? identity) =>
        identity is null ? null : Create(identity);
}

/// <summary>One package-manifest declaration group's owner-issued identity.</summary>
public sealed record DependencyEvidencePackageGroupIdentityJson
{
    public required DependencyEvidencePackageCoordinateJson Root { get; init; }

    public required bool ImplicitManifestGroup { get; init; }

    public required int FirstSourceOccurrence { get; init; }
}

/// <summary>One restored-project declaration group's owner-issued identity.</summary>
public sealed record DependencyEvidenceRestoredGroupIdentityJson
{
    public required DependencyEvidenceRestoredSelectionIdentityJson Selection
        { get; init; }

    public required string PivotIdentity { get; init; }
}

/// <summary>One normalized logical group's owner-issued identity.</summary>
public sealed record DependencyEvidenceGroupIdentityJson
{
    public required DependencyEvidenceIdentityOwner Owner { get; init; }

    public DependencyEvidencePackageGroupIdentityJson? Package { get; init; }

    public DependencyEvidenceRestoredGroupIdentityJson? RestoredProject { get; init; }

    internal static DependencyEvidenceGroupIdentityJson Create(
        PackageDependencyEvidenceGroupIdentity identity) =>
        identity switch
        {
            PackageDependencyEvidenceGroupIdentity.Package package => new()
            {
                Owner = DependencyEvidenceIdentityOwner.Package,
                Package = new DependencyEvidencePackageGroupIdentityJson
                {
                    Root = DependencyEvidencePackageCoordinateJson.Create(
                        package.Root.Coordinate),
                    ImplicitManifestGroup = package.IsImplicitManifestGroup,
                    FirstSourceOccurrence = package.FirstSourceOccurrence,
                },
            },
            PackageDependencyEvidenceGroupIdentity.RestoredProject restored => new()
            {
                Owner = DependencyEvidenceIdentityOwner.RestoredProject,
                RestoredProject = new DependencyEvidenceRestoredGroupIdentityJson
                {
                    Selection =
                        DependencyEvidenceRestoredSelectionIdentityJson.Create(
                            restored.Identity.Selection),
                    PivotIdentity = restored.Identity.PivotIdentity,
                },
            },
            _ => throw new InvalidOperationException(
                "Unknown package dependency evidence group identity."),
        };

    internal static DependencyEvidenceGroupIdentityJson? CreateOptional(
        PackageDependencyEvidenceGroupIdentity? identity) =>
        identity is null ? null : Create(identity);
}

/// <summary>One owner-issued occurrence contributing to a logical group.</summary>
public sealed record DependencyEvidenceGroupOccurrenceJson
{
    public required DependencyEvidenceIdentityOwner Owner { get; init; }

    /// <summary>The manifest's authored group position, for a package occurrence.</summary>
    public int? SourceIndex { get; init; }

    public DependencyEvidenceRestoredGroupIdentityJson? RestoredProject { get; init; }

    internal static DependencyEvidenceGroupOccurrenceJson Create(
        PackageDependencyEvidenceGroupOccurrence occurrence) =>
        occurrence switch
        {
            PackageDependencyEvidenceGroupOccurrence.Package package => new()
            {
                Owner = DependencyEvidenceIdentityOwner.Package,
                SourceIndex = package.SourceIndex,
            },
            PackageDependencyEvidenceGroupOccurrence.RestoredProject restored =>
                new()
                {
                    Owner = DependencyEvidenceIdentityOwner.RestoredProject,
                    RestoredProject =
                        new DependencyEvidenceRestoredGroupIdentityJson
                        {
                            Selection =
                                DependencyEvidenceRestoredSelectionIdentityJson
                                    .Create(restored.Identity.Selection),
                            PivotIdentity = restored.Identity.PivotIdentity,
                        },
                },
            _ => throw new InvalidOperationException(
                "Unknown package dependency evidence group occurrence."),
        };

    internal static DependencyEvidenceGroupOccurrenceJson? CreateOptional(
        PackageDependencyEvidenceGroupOccurrence? occurrence) =>
        occurrence is null ? null : Create(occurrence);
}

/// <summary>One successful declaration's owner-issued identity.</summary>
public sealed record DependencyEvidenceDeclarationIdentityJson
{
    public required DependencyEvidenceGroupIdentityJson Group { get; init; }

    public required string CanonicalPackageId { get; init; }

    internal static DependencyEvidenceDeclarationIdentityJson Create(
        PackageDependencyEvidenceDeclarationIdentity identity) =>
        new()
        {
            Group = DependencyEvidenceGroupIdentityJson.Create(identity.Group),
            CanonicalPackageId = identity.CanonicalPackageId,
        };
}

/// <summary>One resolved package node's owner-issued identity.</summary>
public sealed record DependencyEvidencePackageNodeIdentityJson
{
    public required DependencyEvidenceRestoredSelectionIdentityJson Selection
        { get; init; }

    public required DependencyEvidencePackageCoordinateJson Coordinate { get; init; }

    internal static DependencyEvidencePackageNodeIdentityJson Create(
        RestoredProjectPackageNodeIdentity identity) =>
        new()
        {
            Selection = DependencyEvidenceRestoredSelectionIdentityJson.Create(
                identity.Selection),
            Coordinate = DependencyEvidencePackageCoordinateJson.Create(
                identity.Coordinate),
        };
}

/// <summary>One resolved project node's owner-issued identity.</summary>
public sealed record DependencyEvidenceProjectNodeIdentityJson
{
    public required DependencyEvidenceRestoredSelectionIdentityJson Selection
        { get; init; }

    public required string SourceIdentity { get; init; }
}

/// <summary>One graph edge's owner-issued parent identity.</summary>
public sealed record DependencyEvidenceGraphParentIdentityJson
{
    public required DependencyEvidenceEdgeParentKind Kind { get; init; }

    public DependencyEvidenceRestoredSelectionIdentityJson? Root { get; init; }

    public DependencyEvidencePackageNodeIdentityJson? Package { get; init; }

    public DependencyEvidenceProjectNodeIdentityJson? Project { get; init; }

    internal static DependencyEvidenceGraphParentIdentityJson Create(
        RestoredProjectGraphParentIdentity parent) =>
        parent switch
        {
            RestoredProjectGraphParentIdentity.Root root => new()
            {
                Kind = DependencyEvidenceEdgeParentKind.Root,
                Root = root.Identity.Selection is { } selection
                    ? DependencyEvidenceRestoredSelectionIdentityJson.Create(
                        selection)
                    : null,
            },
            RestoredProjectGraphParentIdentity.Package package => new()
            {
                Kind = DependencyEvidenceEdgeParentKind.Package,
                Package = DependencyEvidencePackageNodeIdentityJson.Create(
                    package.Identity),
            },
            RestoredProjectGraphParentIdentity.Project project => new()
            {
                Kind = DependencyEvidenceEdgeParentKind.Project,
                Project = new DependencyEvidenceProjectNodeIdentityJson
                {
                    Selection =
                        DependencyEvidenceRestoredSelectionIdentityJson.Create(
                            project.Identity.Selection),
                    SourceIdentity = project.Identity.SourceIdentity,
                },
            },
            _ => throw new InvalidOperationException(
                "Unknown restored project graph parent identity."),
        };
}

/// <summary>One graph edge's owner-issued identity.</summary>
public sealed record DependencyEvidenceEdgeIdentityJson
{
    public required DependencyEvidenceGraphParentIdentityJson Parent { get; init; }

    public required DependencyEvidencePackageNodeIdentityJson Dependency
        { get; init; }

    internal static DependencyEvidenceEdgeIdentityJson Create(
        RestoredProjectEdgeIdentity identity) =>
        new()
        {
            Parent = DependencyEvidenceGraphParentIdentityJson.Create(
                identity.Parent),
            Dependency = DependencyEvidencePackageNodeIdentityJson.Create(
                identity.Dependency),
        };
}

/// <summary>
/// One package source result identity.
/// </summary>
/// <remarks>
/// <see cref="PackageSourceAssociation"/> is opaque reference identity with no renderable value,
/// so it is projected as a deterministic request-local token: two results that share one
/// association share one token within a document, and the token means nothing outside it. The
/// producer key, inert producer display, and transport kind are the source facts the producer
/// actually publishes.
/// </remarks>
public sealed record DependencyEvidenceSourceIdentityJson
{
    public required int Association { get; init; }

    public required string ProducerKey { get; init; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? ProducerDisplay { get; init; }

    public required PackageSourceKind TransportKind { get; init; }
}

/// <summary>
/// Assigns the request-local association tokens one document uses.
/// </summary>
/// <remarks>
/// Tokens are assigned over the whole projection in a fixed order — the package-prefix source,
/// then admitted roots, then failure records — so one request's tokens do not move when section
/// selection or a row window changes which of those rows are rendered.
/// </remarks>
internal sealed class DependencyEvidenceSourceTokens
{
    private readonly Dictionary<PackageSourceAssociation, int> _tokens =
        new(AssociationComparer.Instance);

    private DependencyEvidenceSourceTokens()
    {
    }

    public static DependencyEvidenceSourceTokens Create(
        DependencyEvidenceProjection projection)
    {
        var tokens = new DependencyEvidenceSourceTokens();
        tokens.Reserve(projection.Summary.PackagePrefix?.Source);
        foreach (DependencyEvidenceRootRow root in projection.Roots)
            tokens.Reserve(root.Source);
        foreach (DependencyEvidenceFailureRow failure in projection.Failures)
            tokens.Reserve(failure.Source);
        return tokens;
    }

    public DependencyEvidenceSourceIdentityJson? Project(
        PackageSourceResultIdentity? source) =>
        source is null
            ? null
            : new DependencyEvidenceSourceIdentityJson
            {
                Association = Reserve(source),
                ProducerKey = source.Producer.Key,
                ProducerDisplay = source.Producer.Display,
                TransportKind = source.TransportKind,
            };

    private int Reserve(PackageSourceResultIdentity? source)
    {
        if (source is null)
            return 0;
        if (_tokens.TryGetValue(source.Association, out int token))
            return token;
        token = _tokens.Count + 1;
        _tokens[source.Association] = token;
        return token;
    }

    /// <summary>An association is reference identity; nothing about it is a comparable value.</summary>
    private sealed class AssociationComparer
        : IEqualityComparer<PackageSourceAssociation>
    {
        public static AssociationComparer Instance { get; } = new();

        public bool Equals(
            PackageSourceAssociation? x,
            PackageSourceAssociation? y) =>
            ReferenceEquals(x, y);

        public int GetHashCode(PackageSourceAssociation obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
