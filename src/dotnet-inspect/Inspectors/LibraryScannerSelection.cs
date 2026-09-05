using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal static class LibraryScannerSelection
{
    internal static InspectionQuery<AssemblyContextIntegrationScanResult> Query { get; } =
        new("SelectedIntegrationScan", InspectionCost.NetworkFree);

    internal static string? Resolve(
        string? text,
        out EcosystemIntegrationScannerBinding? binding)
    {
        binding = null;
        if (text is null)
            return null;
        if (!EcosystemPackId.TryCreate(text, out var id))
            return $"--scanner requires a canonical ecosystem ID, such as ecosystem.aspire; received '{text}'.";

        switch (EcosystemPackCatalog.SelectScanner(id))
        {
            case EcosystemScannerSelectionResult.Known selected:
                binding = selected.Binding;
                return null;
            case EcosystemScannerSelectionResult.Unavailable:
                return $"Ecosystem '{id}' has no scanner.";
            case EcosystemScannerSelectionResult.Unknown:
                return $"Unknown ecosystem '{id}'.";
            default:
                throw new InvalidOperationException("Unknown scanner selection result.");
        }
    }
}
