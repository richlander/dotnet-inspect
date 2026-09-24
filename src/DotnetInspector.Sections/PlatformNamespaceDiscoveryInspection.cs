using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>One exact namespace requested from a Platform type catalog.</summary>
public sealed record PlatformNamespaceDiscoveryRequest
{
    public PlatformNamespaceDiscoveryRequest(string @namespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        Namespace = @namespace;
    }

    public string Namespace { get; }
}

/// <summary>One namesake Platform Library containing the exact namespace.</summary>
public sealed record PlatformNamespaceDiscoveryHit(
    string Library,
    string Namespace,
    PlatformTypeCatalogRouteTarget Target,
    PlatformPopulationMemberRole Role,
    MetadataTypeDefinitionName Witness,
    ImmutableArray<PlatformNamespaceDiscoveryDeclaration> Declarations);

/// <summary>
/// One detached public declaration observed in an exact Platform namespace.
/// </summary>
public sealed record PlatformNamespaceDiscoveryDeclaration(
    MetadataTypeDefinitionName Type,
    AssemblyTypeDeclarationKind DeclarationKind,
    AssemblyTypeDefinitionKind? DefinitionKind);

/// <summary>Typed terminal content for exact Platform namespace discovery.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Found), "found")]
[JsonDerivedType(typeof(Missing), "missing")]
public abstract record PlatformNamespaceDiscoveryOutcome
{
    private protected PlatformNamespaceDiscoveryOutcome()
    {
    }

    public sealed record Found : PlatformNamespaceDiscoveryOutcome
    {
        public Found(
            PlatformNamespaceDiscoveryRequest request,
            ImmutableArray<PlatformNamespaceDiscoveryHit> hits)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (hits.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "A found Platform namespace requires at least one hit.",
                    nameof(hits));
            }

            Request = request;
            Hits = hits;
        }

        public PlatformNamespaceDiscoveryRequest Request { get; }
        public ImmutableArray<PlatformNamespaceDiscoveryHit> Hits { get; }
    }

    public sealed record Missing(
        PlatformNamespaceDiscoveryRequest Request,
        PlatformTypeCatalogRouteTarget Target)
        : PlatformNamespaceDiscoveryOutcome;
}

/// <summary>
/// Finds public declarations in one exact namespace of namesake Libraries from
/// an already completed Platform catalog.
/// </summary>
public static class PlatformNamespaceDiscoveryInspection
{
    public static InspectionEnvelope<PlatformNamespaceDiscoveryOutcome>
        Execute(
            PlatformTypeCatalog catalog,
            PlatformNamespaceDiscoveryRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(request);

        ImmutableArray<string> namesakeLibraries =
            LibraryNamespaceDiscovery.NamesakeLibraryCandidates(
                request.Namespace);
        var namesakeRanks = new Dictionary<string, int>(
            namesakeLibraries.Length,
            StringComparer.OrdinalIgnoreCase);
        var rankedHits =
            new List<HitBuilder>?[
                namesakeLibraries.Length];
        for (int index = 0; index < namesakeLibraries.Length; index++)
            namesakeRanks.Add(namesakeLibraries[index], index);

        var buildersByMember =
            new Dictionary<PlatformPopulationMember, HitBuilder>(
                ReferenceEqualityComparer.Instance);

        foreach (PlatformTypeCatalogEntry entry in catalog.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.Declaration.IsPublicSurface
                || !string.Equals(
                    entry.Name.Namespace,
                    request.Namespace,
                    StringComparison.Ordinal))
            {
                continue;
            }

            ManagedMetadataIdentity.Assembly assembly =
                entry.ApiContent.AssemblyIdentity
                ?? throw new InvalidOperationException(
                    "A Platform namespace candidate requires a managed assembly identity.");
            if (!namesakeRanks.TryGetValue(
                    assembly.Identity.Name,
                    out int rank))
            {
                continue;
            }

            if (!buildersByMember.TryGetValue(
                    entry.Member,
                    out HitBuilder? builder))
            {
                builder = new(
                    assembly.Identity.Name,
                    request.Namespace,
                    Snapshot(entry.Member.Target),
                    entry.Member.Role,
                    entry.Name);
                buildersByMember.Add(entry.Member, builder);
                (rankedHits[rank] ??= []).Add(builder);
            }
            builder.Declarations.Add(
                new(
                    entry.Name,
                    entry.Kind,
                    entry.Declaration.DefinitionKind));
        }

        var hits =
            ImmutableArray.CreateBuilder<PlatformNamespaceDiscoveryHit>();
        foreach (List<HitBuilder>? tier in rankedHits)
        {
            if (tier is not null)
            {
                hits.AddRange(
                    tier.Select(
                        static builder =>
                            new PlatformNamespaceDiscoveryHit(
                                builder.Library,
                                builder.Namespace,
                                builder.Target,
                                builder.Role,
                                builder.Witness,
                                builder.Declarations.ToImmutable())));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        PlatformNamespaceDiscoveryOutcome content =
            hits.Count == 0
                ? new PlatformNamespaceDiscoveryOutcome.Missing(
                    request,
                    Snapshot(catalog.Target))
                : new PlatformNamespaceDiscoveryOutcome.Found(
                    request,
                    hits.ToImmutable());
        return new(
            content,
            new InspectionShare.NonProjectable(
                "platform-namespace-discovery/share",
                "Platform namespace discovery does not yet have a canonical Workspace Share projection."));
    }

    private static PlatformTypeCatalogRouteTarget Snapshot(
        PlatformFamilyTarget target) =>
        new(
            target.Family,
            target.TargetFramework.ToString(),
            target.Version.Value);

    private sealed record HitBuilder(
        string Library,
        string Namespace,
        PlatformTypeCatalogRouteTarget Target,
        PlatformPopulationMemberRole Role,
        MetadataTypeDefinitionName Witness)
    {
        internal ImmutableArray<PlatformNamespaceDiscoveryDeclaration>.Builder
            Declarations { get; } =
                ImmutableArray.CreateBuilder<
                    PlatformNamespaceDiscoveryDeclaration>();
    }
}
