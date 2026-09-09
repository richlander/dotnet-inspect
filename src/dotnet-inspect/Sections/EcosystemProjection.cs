using System.Collections.Immutable;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;

namespace DotnetInspector.Sections;

/// <summary>One registered ecosystem, as a projected row value.</summary>
/// <remarks>
/// Counts rather than contents: this row answers "what does this ecosystem declare" at a glance,
/// while the declared members belong to their own sections. The demo count points at the
/// <c>demo</c> command, which owns demo discovery and execution.
/// </remarks>
public sealed record EcosystemRow(
    string Id,
    string Title,
    string Summary,
    int Order,
    string? PackageSet,
    int CorePackages,
    int ToolPackages,
    int NamespaceRoots,
    bool HasScanner,
    int Demos);

/// <summary>One package identity a platform target subsumes, as a projected row value.</summary>
/// <remarks>
/// <see cref="Live"/> distinguishes an entry whose supplied version tracks the pack from one
/// frozen at a legacy version. That is the fact which explains a result rather than restating it:
/// a frozen entry is subsumed for any plausible request, while a live one turns on the comparison.
/// </remarks>
public sealed record EcosystemPruningRow(
    string PackageId,
    string Family,
    string SuppliedVersion,
    bool Live);

/// <summary>The projected ecosystem document every section renders from.</summary>
public sealed class EcosystemProjection
{
    /// <summary>Every registered ecosystem, in declared order.</summary>
    public ImmutableArray<EcosystemRow> Ecosystems { get; init; } = [];

    /// <summary>What the selected platform target subsumes, empty when none was selected.</summary>
    public ImmutableArray<EcosystemPruningRow> Pruning { get; init; } = [];

    /// <summary>The platform target the pruning rows describe, when one resolved.</summary>
    public string? PlatformTarget { get; init; }

    /// <summary>Why the platform inventory could not be read, when it could not.</summary>
    public string? PruningUnavailable { get; init; }
}

/// <summary>Host-supplied input for one ecosystem query execution.</summary>
/// <param name="FrameworkSpec">
/// The platform framework to read prune data from, such as <c>runtime</c> or
/// <c>aspnetcore@10.0.11</c>. Null leaves the platform-specific sections empty rather than
/// guessing a target.
/// </param>
public sealed record EcosystemQueryContext(string? FrameworkSpec);

/// <summary>Composes the registered ecosystem catalog into one projected document.</summary>
/// <remarks>
/// Network-free: the registry is compiled into the product, and prune data is read from reference
/// packs already installed on this machine.
/// </remarks>
public static class EcosystemQuery
{
    public static InspectionQuery<EcosystemProjection> Definition { get; } =
        new("Ecosystem catalog", InspectionCost.NetworkFree);

    public static EcosystemProjection Execute(EcosystemQueryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ImmutableArray<EcosystemRow> ecosystems =
        [
            .. EcosystemPackCatalog.Discover()
                .OrderBy(pack => pack.Order)
                .Select(pack => new EcosystemRow(
                    pack.Id.Value,
                    pack.Title,
                    pack.Summary,
                    pack.Order,
                    pack.PackageSet?.Value,
                    pack.CorePackages.Length,
                    pack.ToolPackages.Length,
                    pack.NamespaceRoots.Length,
                    pack.HasScanner,
                    pack.Demos.Length)),
        ];

        if (string.IsNullOrWhiteSpace(context.FrameworkSpec))
        {
            return new EcosystemProjection { Ecosystems = ecosystems };
        }

        InstalledPlatformPruneSource.Result result =
            InstalledPlatformPruneSource.Read(context.FrameworkSpec);
        if (result.Inventory is not { } inventory)
        {
            return new EcosystemProjection
            {
                Ecosystems = ecosystems,
                PruningUnavailable = result.Error,
            };
        }

        return new EcosystemProjection
        {
            Ecosystems = ecosystems,
            PlatformTarget = inventory.TargetFramework,
            Pruning =
            [
                .. inventory.Entries.Select(entry => new EcosystemPruningRow(
                    entry.PackageId,
                    entry.Family,
                    entry.SuppliedVersion.ToNormalizedString(),
                    entry.Precision == PlatformPrunePrecision.Exact
                        && entry.SuppliedVersion == entry.SourcePackVersion)),
            ],
        };
    }
}
