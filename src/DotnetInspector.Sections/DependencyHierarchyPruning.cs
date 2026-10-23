using System.Collections.Immutable;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public sealed record DependencyHierarchyPruningFamily(
    string Family,
    string TargetPackVersion,
    string SourcePackVersion,
    string Precision);

public sealed record DependencyHierarchyPruningComparison(
    int SourceProjectionIndex,
    string SourcePackage,
    string SourceVersion,
    string Package,
    string VersionConstraint,
    string CandidateVersion,
    string Subsumption,
    string? Family,
    string? SuppliedVersion,
    bool Omitted);

/// <summary>Detached pruning disclosure shared by the CLI and Browser.</summary>
public sealed record DependencyHierarchyPruning(
    string Availability,
    string TargetFramework,
    int OmittedRoutes,
    int UnevaluatedRoutes,
    ImmutableArray<DependencyHierarchyPruningFamily> Families,
    ImmutableArray<string> MissingFamilies,
    string? UnavailableDetail,
    ImmutableArray<DependencyHierarchyPruningComparison> Comparisons)
{
    public static DependencyHierarchyPruning FromTraversal(
        PackageDependencyTraversalOutcome traversal)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        PackageDependencyTraversalPruningOutcome? pruning =
            traversal.PlatformPruning;
        if (pruning is null)
        {
            return new(
                "Raw", traversal.TraversalTargetPolicy.TargetFramework,
                0, 0, [], [], null, []);
        }

        return new(
            pruning.Input.Availability.ToString(),
            traversal.TraversalTargetPolicy.TargetFramework,
            pruning.OmittedRoutes,
            pruning.UnevaluatedRoutes,
            [
                .. pruning.Input.Inventory.Families.Select(static family =>
                    new DependencyHierarchyPruningFamily(
                        family.Name,
                        family.TargetPackVersion.ToNormalizedString(),
                        family.SourcePackVersion.ToNormalizedString(),
                        family.Precision.ToString())),
            ],
            pruning.Input.MissingFamilies,
            pruning.Input.UnavailableDetail,
            [
                .. pruning.Comparisons.Select(comparison =>
                {
                    NuGetFetch.PackageSourceCoordinate source =
                        traversal.Nodes[
                            traversal.Projections[
                                comparison.SourceProjectionIndex].NodeIndex]
                            .Coordinate;
                    return new DependencyHierarchyPruningComparison(
                        comparison.SourceProjectionIndex,
                        source.PackageId,
                        source.Version,
                        comparison.Declaration.CanonicalPackageId,
                        comparison.Declaration.CanonicalVersionConstraint,
                        comparison.Receipt.Coordinate.Version!,
                        comparison.Receipt.Supply.Subsumption.ToString(),
                        comparison.Receipt.Supply.Family,
                        comparison.Receipt.Supply.SuppliedVersion
                            ?.ToNormalizedString(),
                        comparison.Receipt.Supply.DelegatesToPlatform);
                }),
            ]);
    }

    public bool Equals(DependencyHierarchyPruning? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && Availability == other.Availability
        && TargetFramework == other.TargetFramework
        && OmittedRoutes == other.OmittedRoutes
        && UnevaluatedRoutes == other.UnevaluatedRoutes
        && UnavailableDetail == other.UnavailableDetail
        && DependencyValueEquality.SequenceEqual(Families, other.Families)
        && DependencyValueEquality.SequenceEqual(MissingFamilies, other.MissingFamilies)
        && DependencyValueEquality.SequenceEqual(Comparisons, other.Comparisons);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Availability);
        hash.Add(TargetFramework);
        hash.Add(OmittedRoutes);
        hash.Add(UnevaluatedRoutes);
        hash.Add(UnavailableDetail);
        DependencyValueEquality.AddSequenceHashCode(ref hash, Families);
        DependencyValueEquality.AddSequenceHashCode(ref hash, MissingFamilies);
        DependencyValueEquality.AddSequenceHashCode(ref hash, Comparisons);
        return hash.ToHashCode();
    }
}
