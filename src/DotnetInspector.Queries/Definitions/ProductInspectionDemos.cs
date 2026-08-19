namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Static product registry of inspection demos. Same shape as
/// <c>VocabularyCatalog</c> / Markout <c>FormatterRegistry</c>: typed entries
/// captured at initialization, hosts resolve by id, JSON is only for portable
/// external definitions via <see cref="InspectionDefinitionJson"/>.
/// </summary>
public static class ProductInspectionDemos
{
    private static readonly InspectionDefinitionRecord[] s_records = CreateRecords();

    private static readonly InspectionDefinitionRegistry s_registry = CreateRegistryCore(s_records);

    /// <summary>
    /// Stable scenario ids for the three workbench home demos, in display order.
    /// </summary>
    public static IReadOnlyList<string> HomeScenarioIds { get; } =
    [
        StjSerializerScenarioId,
        ExtensionsCallGraphScenarioId,
        PlatformListScenarioId,
    ];

    public const string StjSerializerScenarioId = "stj-serializer";

    public const string ExtensionsCallGraphScenarioId = "extensions-callgraph";

    public const string PlatformListScenarioId = "platform-list";

    /// <summary>Every product-owned definition record backing the home demos.</summary>
    public static IReadOnlyList<InspectionDefinitionRecord> Records { get; } = s_records;

    /// <summary>
    /// Shared registry of <see cref="Records"/>. Hosts may resolve against this
    /// instance; do not mutate it.
    /// </summary>
    public static InspectionDefinitionRegistry Registry => s_registry;

    /// <summary>
    /// Builds a fresh registry containing the product demo records. Prefer
    /// <see cref="Registry"/> when mutation is not required.
    /// </summary>
    public static InspectionDefinitionRegistry CreateRegistry() => CreateRegistryCore(s_records);

    /// <summary>Resolves one product home demo scenario by id.</summary>
    public static ResolvedScenario ResolveHomeScenario(string scenarioId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        if (!HomeScenarioIds.Contains(scenarioId, StringComparer.Ordinal))
        {
            throw new InspectionDefinitionException(
                $"Unknown product home demo scenario '{scenarioId}'.");
        }

        return s_registry.ResolveScenario(scenarioId);
    }

    private static InspectionDefinitionRegistry CreateRegistryCore(
        IReadOnlyList<InspectionDefinitionRecord> records)
    {
        var registry = new InspectionDefinitionRegistry();
        foreach (var record in records)
            registry.Add(record);
        return registry;
    }

    private static InspectionDefinitionRecord[] CreateRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;

        var stjPackage = new DefinitionMemberCoordinate.PackageCoordinate(
            "System.Text.Json",
            "10.0.0",
            "net10.0");
        var runtimePlatform = new DefinitionMemberCoordinate.PlatformCoordinate(
            "runtime",
            null,
            "10.0.10",
            "net10.0");
        var diAbstractions = new DefinitionMemberCoordinate.PackageCoordinate(
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "10.0.0",
            "net10.0");
        var logging = new DefinitionMemberCoordinate.PackageCoordinate(
            "Microsoft.Extensions.Logging",
            "10.0.0",
            "net10.0");
        var http = new DefinitionMemberCoordinate.PackageCoordinate(
            "Microsoft.Extensions.Http",
            "10.0.0",
            "net10.0");

        return
        [
            // --- System.Text.Json ---
            new WorkspaceDefinition(
                v,
                "stj-serializer-tour",
                [
                    new WorkspaceContextDefinition(
                        "stj",
                        framework: "net10.0",
                        members: [stjPackage]),
                ],
                title: "System.Text.Json serializer tour",
                description: "JsonSerializer surface from the System.Text.Json package."),
            new ViewDefinition(
                v,
                "stj-serializer-view",
                type: "System.Text.Json.JsonSerializer"),
            new NavigationDefinition(
                v,
                "stj-navigation",
                [new NavigationTabDefinition("stj", coordinate: stjPackage)],
                focus: "stj"),
            new ScenarioDefinition(
                v,
                StjSerializerScenarioId,
                title: "System.Text.Json",
                description: "Browse a real package API",
                workspace: "stj-serializer-tour",
                context: "stj",
                view: "stj-serializer-view",
                navigation: "stj-navigation"),

            // --- Cross-package call graph ---
            new WorkspaceDefinition(
                v,
                "extensions-callgraph",
                [
                    new WorkspaceContextDefinition(
                        "extensions",
                        framework: "net10.0",
                        members: [diAbstractions, logging, http]),
                ],
                title: "Extensions cross-package call graph",
                description: "DI Abstractions + Logging + Http for multi-package call graph."),
            new ViewDefinition(
                v,
                "try-add-enumerable-call-graph",
                type: "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
                memberAnchor: "74b6b4b321",
                memberKey: "method:TryAddEnumerable",
                section: "call-graph"),
            new NavigationDefinition(
                v,
                "extensions-callgraph-navigation",
                [
                    new NavigationTabDefinition("di", coordinate: diAbstractions),
                    new NavigationTabDefinition("logging", coordinate: logging),
                    new NavigationTabDefinition("http", coordinate: http),
                ],
                focus: "di"),
            new ScenarioDefinition(
                v,
                ExtensionsCallGraphScenarioId,
                title: "Cross-package call graph",
                description: "Trace calls across three packages",
                workspace: "extensions-callgraph",
                context: "extensions",
                view: "try-add-enumerable-call-graph",
                navigation: "extensions-callgraph-navigation"),

            // --- .NET Platform ---
            new WorkspaceDefinition(
                v,
                "platform-list-tour",
                [
                    new WorkspaceContextDefinition(
                        "platform",
                        framework: "net10.0",
                        members: [stjPackage, runtimePlatform]),
                ],
                title: ".NET Platform List tour",
                description: "Platform BCL List`1 with System.Text.Json also in the workspace."),
            new ViewDefinition(
                v,
                "platform-list-view",
                library: "System.Private.CoreLib",
                type: "System.Collections.Generic.List`1"),
            new NavigationDefinition(
                v,
                "platform-navigation",
                [
                    new NavigationTabDefinition("stj", coordinate: stjPackage),
                    new NavigationTabDefinition("runtime", coordinate: runtimePlatform),
                ],
                focus: "runtime"),
            new ScenarioDefinition(
                v,
                PlatformListScenarioId,
                title: ".NET Platform",
                description: "Inspect platform BCL types",
                workspace: "platform-list-tour",
                context: "platform",
                view: "platform-list-view",
                navigation: "platform-navigation"),
        ];
    }
}
