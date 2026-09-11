using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems;

/// <summary>Immutable discovery metadata for one product ecosystem pack.</summary>
public sealed class EcosystemPackDescriptor
{
    internal EcosystemPackDescriptor(
        EcosystemPackId id,
        string title,
        string summary,
        int order,
        PackageSetId? packageSet,
        IEnumerable<EcosystemDemoDescriptor> demos,
        bool hasScanner,
        ImmutableArray<string> namespaceRoots,
        ImmutableArray<PackageCoordinate> corePackages,
        ImmutableArray<PackageCoordinate> toolPackages)
    {
        Id = id;
        Title = title;
        Summary = summary;
        Order = order;
        PackageSet = packageSet;
        Demos = [.. demos];
        HasScanner = hasScanner;
        NamespaceRoots = namespaceRoots;
        CorePackages = corePackages;
        ToolPackages = toolPackages;
    }

    public EcosystemPackId Id { get; }

    public string Title { get; }

    public string Summary { get; }

    public int Order { get; }

    public PackageSetId? PackageSet { get; }

    public ImmutableArray<EcosystemDemoDescriptor> Demos { get; }

    public bool HasScanner { get; }

    /// <summary>Literal namespace-subtree hints in authored order, not an exhaustive inventory.</summary>
    public ImmutableArray<string> NamespaceRoots { get; }

    /// <summary>Unversioned starting points in pack-local preference order, independent of curated membership.</summary>
    public ImmutableArray<PackageCoordinate> CorePackages { get; }

    /// <summary>Explicit unversioned .NET tool references in authored order, not installation requests.</summary>
    public ImmutableArray<PackageCoordinate> ToolPackages { get; }
}

/// <summary>Immutable product metadata for one ecosystem demo.</summary>
public sealed class EcosystemDemoDescriptor
{
    internal EcosystemDemoDescriptor(
        EcosystemPackId ecosystem,
        string scenarioId,
        string title,
        string summary,
        int order)
    {
        Ecosystem = ecosystem;
        ScenarioId = scenarioId;
        Title = title;
        Summary = summary;
        Order = order;
    }

    public EcosystemPackId Ecosystem { get; }

    public string ScenarioId { get; }

    public string Title { get; }

    public string Summary { get; }

    public int Order { get; }
}

/// <summary>A selected catalog descriptor and its resolved scenario.</summary>
public sealed record EcosystemDemoSelection(
    EcosystemDemoDescriptor Descriptor,
    ResolvedScenario Scenario);

/// <summary>The result of exact ecosystem-pack lookup.</summary>
public abstract record EcosystemPackLookupResult
{
    private protected EcosystemPackLookupResult()
    {
    }

    public sealed record Known : EcosystemPackLookupResult
    {
        internal Known(EcosystemPackDescriptor descriptor) =>
            Descriptor = descriptor;

        public EcosystemPackDescriptor Descriptor { get; }
    }

    public sealed record Unknown : EcosystemPackLookupResult
    {
        internal Unknown(EcosystemPackId id) => Id = id;

        public EcosystemPackId Id { get; }
    }
}

/// <summary>The result of exact product-demo selection.</summary>
public abstract record EcosystemDemoSelectionResult
{
    private protected EcosystemDemoSelectionResult()
    {
    }

    public sealed record Known : EcosystemDemoSelectionResult
    {
        internal Known(EcosystemDemoSelection selection) =>
            Selection = selection;

        public EcosystemDemoSelection Selection { get; }
    }

    public sealed record Unknown : EcosystemDemoSelectionResult
    {
        internal Unknown(string scenarioId) => ScenarioId = scenarioId;

        public string ScenarioId { get; }
    }
}

/// <summary>The result of exact scanner selection, without invoking a scanner.</summary>
public abstract record EcosystemScannerSelectionResult
{
    private protected EcosystemScannerSelectionResult()
    {
    }

    public sealed record Known : EcosystemScannerSelectionResult
    {
        internal Known(EcosystemIntegrationScannerBinding binding) =>
            Binding = binding;

        public EcosystemIntegrationScannerBinding Binding { get; }
    }

    /// <summary>The pack is registered but does not contribute a scanner.</summary>
    public sealed record Unavailable : EcosystemScannerSelectionResult
    {
        internal Unavailable(EcosystemPackId id) => Id = id;

        public EcosystemPackId Id { get; }
    }

    public sealed record Unknown : EcosystemScannerSelectionResult
    {
        internal Unknown(EcosystemPackId id) => Id = id;

        public EcosystemPackId Id { get; }
    }
}
