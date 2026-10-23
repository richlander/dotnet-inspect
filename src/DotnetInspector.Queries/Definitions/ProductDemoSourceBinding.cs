namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// One exact product-demo source capture: the resolved presentation scenario
/// and its owner-issued committed complete-restoration input.
/// </summary>
public sealed record ProductDemoSourceCapture(
    ResolvedScenario Scenario,
    CompleteRestorationRequestBasis.DefinitionInput RestorationRequest);

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
    /// Constructs this source's records once, resolves its exact scenario, and
    /// derives committed complete-restoration input from that same capture.
    /// </summary>
    public ProductDemoSourceCapture Capture()
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
        return new(
            resolved,
            CreateRestorationRequest(resolved, records));
    }

    /// <summary>Constructs this source's records and resolves its scenario.</summary>
    public ResolvedScenario Resolve() => Capture().Scenario;

    private static CompleteRestorationRequestBasis.DefinitionInput
        CreateRestorationRequest(
        ResolvedScenario resolved,
        IReadOnlyList<InspectionDefinitionRecord> records)
    {
        const int schemaVersion = InspectionDefinitionSchema.Version4;
        WorkspaceDefinition sourceWorkspace = resolved.Workspace
            ?? throw new InspectionDefinitionException(
                $"Product demo '{resolved.ScenarioId}' has no Workspace definition.");
        NavigationDefinition sourceNavigation = records
            .OfType<NavigationDefinition>()
            .Single(record => string.Equals(
                record.Id,
                resolved.Scenario.Navigation,
                StringComparison.Ordinal));
        ViewDefinition sourceView = resolved.View
            ?? throw new InspectionDefinitionException(
                $"Product demo '{resolved.ScenarioId}' has no View definition.");

        var workspace = new WorkspaceDefinition(
            schemaVersion,
            sourceWorkspace.Id,
            sourceWorkspace.Contexts,
            sourceWorkspace.Title,
            sourceWorkspace.Description,
            sourceWorkspace.Groups,
            sourceWorkspace.Registrations);
        var navigation = new CommittedNavigationDefinition(
            schemaVersion,
            sourceNavigation.Id,
            sourceNavigation.Tabs,
            focus: null);
        var view = new CommittedViewDefinition(
            schemaVersion,
            sourceView.Id,
            [
                new CommittedViewStateDefinition(
                    navigation: null,
                    new PortableSubjectRequest.Workspace()),
                .. sourceNavigation.Tabs.Select(
                    static tab =>
                        new CommittedViewStateDefinition(tab.Id)),
            ]);
        var scenario = new ScenarioDefinition(
            schemaVersion,
            resolved.Scenario.Id,
            resolved.Scenario.Title,
            resolved.Scenario.Description,
            workspace: workspace.Id,
            context: resolved.Scenario.Context,
            view: view.Id,
            navigation: navigation.Id);
        return new(
            resolved.ScenarioId,
            [workspace, navigation, view, scenario]);
    }
}
