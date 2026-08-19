using System.Reflection;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Product-resident inspection demos shipped with the queries assembly.
/// Hosts (CLI, inspect-web, workflows) activate scenarios by id from
/// <see cref="CreateRegistry"/> rather than embedding host-local demo catalogs.
/// </summary>
public static class ProductInspectionDemos
{
    private const string ResourcePrefix =
        "DotnetInspector.Queries.Definitions.demos.";

    /// <summary>
    /// Stable scenario ids for the three workbench home demos, in display order.
    /// </summary>
    public static IReadOnlyList<string> HomeScenarioIds { get; } =
    [
        "stj-serializer",
        "extensions-callgraph",
        "platform-list",
    ];

    /// <summary>
    /// Builds a registry containing every embedded product demo definition record.
    /// </summary>
    public static InspectionDefinitionRegistry CreateRegistry()
    {
        var registry = new InspectionDefinitionRegistry();
        var assembly = typeof(ProductInspectionDemos).Assembly;
        foreach (var name in assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing embedded definition resource '{name}'.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            registry.AddJson(memory.ToArray());
        }

        return registry;
    }

    /// <summary>
    /// Resolves one product home demo scenario.
    /// </summary>
    public static ResolvedScenario ResolveHomeScenario(string scenarioId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        if (!HomeScenarioIds.Contains(scenarioId, StringComparer.Ordinal))
        {
            throw new InspectionDefinitionException(
                $"Unknown product home demo scenario '{scenarioId}'.");
        }

        return CreateRegistry().ResolveScenario(scenarioId);
    }
}
