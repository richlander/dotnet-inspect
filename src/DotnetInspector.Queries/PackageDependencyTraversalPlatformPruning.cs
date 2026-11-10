using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;

namespace DotnetInspector.Queries;

public enum PackageDependencyTraversalPruningAvailability
{
    Disabled,
    Unavailable,
    Incomplete,
    Available,
}

/// <summary>Workspace intent and available family evidence for one traversal.</summary>
public sealed class PackageDependencyTraversalPlatformPruning
{
    public PackageDependencyTraversalPlatformPruning(
        WorkspacePlan workspacePlan,
        IEnumerable<PlatformPruneInventory> inventories,
        string? unavailableDetail = null)
    {
        ArgumentNullException.ThrowIfNull(workspacePlan);
        ArgumentNullException.ThrowIfNull(inventories);
        WorkspacePlan = workspacePlan;
        UnavailableDetail = unavailableDetail;
        bool enabled = workspacePlan.Registrations
            .OfType<WorkspaceRegistration.Ecosystem>()
            .Any(static registration =>
                registration.Declaration.Id.Value == "ecosystem.runtime");
        RequiredFamilies = GetSelectedFamilies(workspacePlan);
        string target = workspacePlan.TraversalTargetPolicy.TargetFramework;
        PlatformPruneInventory[] matching =
        [
            .. inventories.Where(inventory =>
                enabled
                && string.Equals(
                    inventory.TargetFramework, target,
                    StringComparison.OrdinalIgnoreCase)
                && inventory.Families.All(family =>
                    RequiredFamilies.Contains(
                        family.Name, StringComparer.OrdinalIgnoreCase))),
        ];
        Inventory = matching.Length == 0
            ? PlatformPruneInventory.None(target)
            : PlatformPruneInventory.Compose(matching);
        MissingFamilies =
        [
            .. RequiredFamilies.Where(name =>
                !Inventory.TryGetExactFamilyTargetVersion(name, out _)),
        ];
        Availability = !enabled
            ? PackageDependencyTraversalPruningAvailability.Disabled
            : matching.Length == 0 || RequiredFamilies.IsEmpty
                ? PackageDependencyTraversalPruningAvailability.Unavailable
                : MissingFamilies.IsEmpty
                    ? PackageDependencyTraversalPruningAvailability.Available
                    : PackageDependencyTraversalPruningAvailability.Incomplete;
    }

    public WorkspacePlan WorkspacePlan { get; }

    public PlatformPruneInventory Inventory { get; }

    public ImmutableArray<string> RequiredFamilies { get; }

    public ImmutableArray<string> MissingFamilies { get; }

    public PackageDependencyTraversalPruningAvailability Availability { get; }

    public string? UnavailableDetail { get; }

    public static ImmutableArray<string> GetSelectedFamilies(WorkspacePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return
        [
            .. plan.Registrations
                .OfType<WorkspaceRegistration.Ecosystem>()
                .SelectMany(static registration =>
                    registration.Declaration.Populations)
                .OfType<WorkspaceEcosystemPopulationDeclaration.Platform>()
                .Select(static declaration => declaration.Population.Family switch
                {
                    PlatformFamily.DotNetRuntime => "Microsoft.NETCore.App",
                    PlatformFamily.AspNetCore => "Microsoft.AspNetCore.App",
                    _ => throw new InvalidOperationException(
                        "Unknown Platform pruning family."),
                })
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }
}

/// <summary>A comparison associated with its original declaration and projection.</summary>
public sealed record PackageDependencyTraversalSupplyComparison(
    int SourceProjectionIndex,
    PackageDependencyEvidenceDeclaration Declaration,
    PlatformSupplyReceipt Receipt,
    ImmutableArray<PackageAuthorityFailure> Diagnostics);

public sealed record PackageDependencyTraversalPruningOutcome(
    PackageDependencyTraversalPlatformPruning Input,
    ImmutableArray<PackageDependencyTraversalSupplyComparison> Comparisons)
{
    public int OmittedRoutes => Comparisons.Count(
        static comparison => comparison.Receipt.Supply.DelegatesToPlatform);

    public int UnevaluatedRoutes => Comparisons.Count(comparison =>
        comparison.Receipt.Supply.Subsumption == PlatformSubsumption.NotComparable
        || !comparison.Receipt.Supply.DelegatesToPlatform
            && Input.Availability is
                PackageDependencyTraversalPruningAvailability.Incomplete
                or PackageDependencyTraversalPruningAvailability.Unavailable);
}
