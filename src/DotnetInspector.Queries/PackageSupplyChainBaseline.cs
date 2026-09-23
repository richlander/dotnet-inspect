using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries;

/// <summary>
/// Packages excluded from incremental supply-chain highlighting.
/// </summary>
public enum PackageSupplyChainBaseline
{
    Nothing,
    Self,
    SelfAndRegisteredEcosystems,
}

/// <summary>
/// Whether one already-known Package is baseline context or incremental
/// supply-chain exposure.
/// </summary>
public enum PackageSupplyChainClassification
{
    Baseline,
    IncrementalExposure,
}

/// <summary>
/// Resource-free evidence for one captured package supply-chain baseline.
/// </summary>
public sealed record PackageSupplyChainBaselineEvidence(
    PackageSupplyChainBaseline Kind,
    ImmutableArray<string> RootPackageIds,
    ImmutableArray<string> FirstPartyPackagePrefixes,
    ImmutableArray<string> RegisteredEcosystems);

/// <summary>
/// Classifies already-known Package IDs against one captured Workspace
/// registration revision.
/// </summary>
public sealed class PackageSupplyChainBaselinePolicy
{
    private readonly ImmutableArray<PackagePrefixDeclaration>
        _firstPartyPackagePrefixes;
    private readonly ImmutableArray<WorkspaceEcosystemRegistrationDeclaration>
        _registeredEcosystems;

    private PackageSupplyChainBaselinePolicy(
        ImmutableArray<string> rootPackageIds,
        PackageSupplyChainBaseline baseline,
        ImmutableArray<PackagePrefixDeclaration> firstPartyPackagePrefixes,
        ImmutableArray<WorkspaceEcosystemRegistrationDeclaration>
            registeredEcosystems)
    {
        RootPackageIds = rootPackageIds;
        Kind = baseline;
        _firstPartyPackagePrefixes = firstPartyPackagePrefixes;
        _registeredEcosystems = registeredEcosystems;
        Evidence = new PackageSupplyChainBaselineEvidence(
            baseline,
            rootPackageIds,
            [
                .. firstPartyPackagePrefixes.Select(
                    static prefix => prefix.Prefix),
            ],
            [
                .. registeredEcosystems.Select(
                    static ecosystem => ecosystem.Id.Value),
            ]);
    }

    public PackageSupplyChainBaseline Kind { get; }

    public ImmutableArray<string> RootPackageIds { get; }

    public PackageSupplyChainBaselineEvidence Evidence { get; }

    /// <summary>
    /// Creates the neutral baseline without a Workspace registration revision.
    /// </summary>
    public static PackageSupplyChainBaselinePolicy CreateNothing(
        IEnumerable<string> rootPackageIds) =>
        new(
            SnapshotRootPackageIds(rootPackageIds),
            PackageSupplyChainBaseline.Nothing,
            [],
            []);

    /// <summary>
    /// Captures one exact Workspace registration revision for package baseline
    /// classification.
    /// </summary>
    public static PackageSupplyChainBaselinePolicy Create(
        IEnumerable<string> rootPackageIds,
        PackageSupplyChainBaseline baseline,
        WorkspaceRegistrationRevision registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        if (!Enum.IsDefined(baseline))
            throw new ArgumentOutOfRangeException(nameof(baseline));

        return new(
            SnapshotRootPackageIds(rootPackageIds),
            baseline,
            [
                .. registrations.Registrations
                    .OfType<WorkspaceRegistration.PackagePrefix>()
                    .Select(static registration => registration.Prefix),
            ],
            [
                .. registrations.Registrations
                    .OfType<WorkspaceRegistration.Ecosystem>()
                    .Select(static registration =>
                        registration.Declaration),
            ]);
    }

    /// <summary>
    /// Classifies one Package whose ownership is already established by its
    /// calling owner.
    /// </summary>
    public PackageSupplyChainClassification Classify(string packageId)
    {
        string canonicalPackageId = CanonicalPackageId(
            packageId,
            nameof(packageId));
        if (RootPackageIds.Contains(
                canonicalPackageId,
                StringComparer.Ordinal))
        {
            return PackageSupplyChainClassification.Baseline;
        }

        if (Kind is PackageSupplyChainBaseline.Nothing)
            return PackageSupplyChainClassification.IncrementalExposure;

        if (_firstPartyPackagePrefixes.Any(prefix =>
                prefix.MatchesPackageId(canonicalPackageId)))
        {
            return PackageSupplyChainClassification.Baseline;
        }

        if (Kind is PackageSupplyChainBaseline.Self)
            return PackageSupplyChainClassification.IncrementalExposure;

        foreach (WorkspaceEcosystemRegistrationDeclaration ecosystem
            in _registeredEcosystems)
        {
            if (ecosystem.CorePackages.Any(package =>
                    string.Equals(
                        package.PackageId,
                        canonicalPackageId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return PackageSupplyChainClassification.Baseline;
            }

            foreach (WorkspaceEcosystemPopulationDeclaration population
                in ecosystem.Populations)
            {
                switch (population)
                {
                    case WorkspaceEcosystemPopulationDeclaration
                        .PackagePrefix prefix
                        when prefix.Prefix.MatchesPackageId(
                            canonicalPackageId):
                    case WorkspaceEcosystemPopulationDeclaration
                        .ExactLibrary
                        {
                            Coordinate:
                                ExactLibrarySourceCoordinate.Package
                                    {
                                        PackageCoordinate:
                                            { PackageId: var exactPackageId },
                                    },
                        }
                        when string.Equals(
                            exactPackageId,
                            canonicalPackageId,
                            StringComparison.OrdinalIgnoreCase):
                        return PackageSupplyChainClassification.Baseline;
                }
            }
        }

        return PackageSupplyChainClassification.IncrementalExposure;
    }

    private static ImmutableArray<string> SnapshotRootPackageIds(
        IEnumerable<string> rootPackageIds)
    {
        ArgumentNullException.ThrowIfNull(rootPackageIds);
        var roots = ImmutableArray.CreateBuilder<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string packageId in rootPackageIds)
        {
            string canonicalPackageId = CanonicalPackageId(
                packageId,
                nameof(rootPackageIds));
            if (unique.Add(canonicalPackageId))
                roots.Add(canonicalPackageId);
        }

        if (roots.Count == 0)
        {
            throw new ArgumentException(
                "A package supply-chain baseline requires at least one root Package ID.",
                nameof(rootPackageIds));
        }

        return roots.ToImmutable();
    }

    private static string CanonicalPackageId(
        string packageId,
        string parameterName)
    {
        if (!PackageCoordinateResolver.IsCanonicalPackageId(packageId))
        {
            throw new ArgumentException(
                "A package supply-chain baseline requires canonical NuGet Package IDs.",
                parameterName);
        }

        return packageId.ToLowerInvariant();
    }
}
