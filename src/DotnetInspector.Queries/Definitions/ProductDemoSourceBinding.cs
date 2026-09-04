namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// A lazy source for one product demo's peer definition records.
/// </summary>
public sealed class ProductDemoSourceBinding
{
    private readonly Func<InspectionDefinitionRecord[]> _createRecords;

    private ProductDemoSourceBinding(
        string scenarioId,
        Func<InspectionDefinitionRecord[]> createRecords)
    {
        ScenarioId = scenarioId;
        _createRecords = createRecords;
    }

    /// <summary>Gets the exact scenario ID produced by this source.</summary>
    public string ScenarioId { get; }

    /// <summary>
    /// Creates a binding from exactly one target-free static method group.
    /// </summary>
    public static ProductDemoSourceBinding Create(
        string scenarioId,
        Func<InspectionDefinitionRecord[]> createRecords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        ArgumentNullException.ThrowIfNull(createRecords);
        if (createRecords.Target is not null
            || createRecords.GetInvocationList().Length != 1)
        {
            throw new ArgumentException(
                "A product demo source must be exactly one target-free static method group.",
                nameof(createRecords));
        }

        return new ProductDemoSourceBinding(scenarioId, createRecords);
    }

    /// <summary>
    /// Constructs this source's records and resolves its exact scenario.
    /// </summary>
    public ResolvedScenario Resolve()
    {
        InspectionDefinitionRecord[] records = _createRecords()
            ?? throw new InspectionDefinitionException(
                $"Product demo '{ScenarioId}' source returned no record sequence.");
        ScenarioDefinition[] scenarios =
        [
            .. records.OfType<ScenarioDefinition>(),
        ];
        if (scenarios.Length != 1)
        {
            throw new InspectionDefinitionException(
                $"Product demo '{ScenarioId}' source must return exactly one scenario record.");
        }

        if (!string.Equals(
                scenarios[0].Id,
                ScenarioId,
                StringComparison.Ordinal))
        {
            throw new InspectionDefinitionException(
                $"Product demo '{ScenarioId}' source returned scenario '{scenarios[0].Id}'.");
        }

        var registry = new InspectionDefinitionRegistry();
        foreach (InspectionDefinitionRecord record in records)
        {
            if (record is null)
            {
                throw new InspectionDefinitionException(
                    $"Product demo '{ScenarioId}' source returned a null definition record.");
            }

            registry.Add(record);
        }

        ResolvedScenario resolved = registry.ResolveScenario(ScenarioId);
        ProductDemoSections.EnsureHomeDemoBinding(resolved);
        return resolved;
    }
}
