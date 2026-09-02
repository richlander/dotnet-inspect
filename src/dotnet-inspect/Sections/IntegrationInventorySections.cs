using DotnetInspector.Queries;
using Markout;

namespace DotnetInspector.Sections;

/// <summary>
/// Reusable L2 declaration for the Workspace Integration Inventory.
/// </summary>
public static class IntegrationInventorySections
{
    public const string Inventory = "Integration Inventory";

    public static SectionCatalog<IntegrationInventoryProjectionResult>
        SectionCatalog { get; } =
        CreatePipeline().Compile();

    public static SectionCatalog<IntegrationInventoryProjectionResult>
        CreateCatalog() =>
        SectionCatalog;

    public static SectionPipeline<IntegrationInventoryProjectionResult>
        CreatePipeline() =>
        new SectionPipeline<IntegrationInventoryProjectionResult>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<InventoryRows>();

    public static DocumentSchema CreateSchema() =>
        new DocumentSchema().Add(
            Inventory,
            "column",
            "Concept",
            "Relationship",
            "Source",
            "Source Assembly",
            "Source Provenance",
            "Source Parent",
            "Binding Context",
            "Peer",
            "Peer Scope",
            "Terminal",
            "Terminal Assembly",
            "Terminal Provenance",
            "Terminal Parent",
            "Forwarding Hops",
            "Disposition",
            "Out Reason",
            "Producer Policies");

    public sealed class InventoryRows
        : ISectionDescriptor<IntegrationInventoryProjectionResult>
    {
        public static string Name => Inventory;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;

        public static bool CanRender(
            IntegrationInventoryProjectionResult model) =>
            model.Rows.Length != 0;
    }
}
