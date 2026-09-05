using System.Text.Json.Serialization;
using DotnetInspector.Queries;

namespace DotnetInspector.Models;

public sealed record LibraryIntegrationScan(
    string Scanner,
    [property: JsonIgnore] AssemblyIntegrationsEntry Entry)
{
    public string Status => Entry switch
    {
        AssemblyIntegrationsEntry.Selected => "complete",
        AssemblyIntegrationsEntry.Rejected => "rejected",
        AssemblyIntegrationsEntry.Failed => "failed",
        _ => throw new InvalidOperationException("Expected a selected Integration outcome."),
    };

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error => Entry switch
    {
        AssemblyIntegrationsEntry.Selected => null,
        AssemblyIntegrationsEntry.Rejected rejected =>
            $"{rejected.Failure.Kind}: {rejected.Failure.Detail}",
        AssemblyIntegrationsEntry.Failed failed => failed.Error.Message,
        _ => throw new InvalidOperationException("Expected a selected Integration outcome."),
    };

    public LibraryIntegrationScanSignal[] Signals =>
        Entry is AssemblyIntegrationsEntry.Selected selected
            ? [.. selected.EcosystemSignals.Select(signal =>
                new LibraryIntegrationScanSignal(
                    Scanner, signal.Integration, signal.Kind, signal.Name, signal.Shape))]
            : [];
}

public sealed record LibraryIntegrationScanSignal(
    string Scanner,
    string Integration,
    string Kind,
    string Name,
    string Shape);
