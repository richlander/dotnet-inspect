using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Product home-demo registry in the smooth-markdown-table
/// <c>RendererRegistry</c> style: a static table of id → factory. Listing is
/// metadata-only; peer definition records for a demo are allocated only when
/// that demo is resolved. Portable external definitions still use
/// <see cref="InspectionDefinitionJson"/>.
/// </summary>
public static class ProductInspectionDemos
{
    /// <summary>One home-demo catalog entry: stable id, labels, and a factory.</summary>
    public readonly record struct Entry(
        string Id,
        string Title,
        string Summary,
        Func<InspectionDefinitionRecord[]> CreateRecords);

    public const string StjSerializerScenarioId = "stj-serializer";

    public const string ExtensionsCallGraphScenarioId = "extensions-callgraph";

    public const string StjSerializeCallGraphScenarioId = "stj-serialize-callgraph";

    public const string ConfigBindCallGraphScenarioId = "config-bind-callgraph";

    public const string OptionsAddCallGraphScenarioId = "options-add-callgraph";

    private static readonly Entry[] s_entries =
    [
        new(
            StjSerializerScenarioId,
            "System.Text.Json",
            "Browse a real package API",
            CreateStjSerializerRecords),
        new(
            ExtensionsCallGraphScenarioId,
            "Cross-package call graph",
            "Trace calls across three packages",
            CreateExtensionsCallGraphRecords),
        new(
            StjSerializeCallGraphScenarioId,
            "Serialize call graph",
            "Dense package-local STJ graph",
            CreateStjSerializeCallGraphRecords),
        new(
            ConfigBindCallGraphScenarioId,
            "Configuration Bind",
            "Recursive binder call graph",
            CreateConfigBindCallGraphRecords),
        new(
            OptionsAddCallGraphScenarioId,
            "Options hub",
            "Inbound fan-in at AddOptions",
            CreateOptionsAddCallGraphRecords),
    ];

    /// <summary>
    /// Home demo ids in display order. Enumerating this does not build demo graphs.
    /// </summary>
    public static IReadOnlyList<string> HomeScenarioIds { get; } =
        Array.AsReadOnly(Array.ConvertAll(s_entries, static e => e.Id));

    /// <summary>
    /// Catalog metadata for hosts (menus, help). Does not allocate demo records.
    /// Exposed as a read-only wrapper over the static table.
    /// </summary>
    public static IReadOnlyList<Entry> Entries { get; } = Array.AsReadOnly(s_entries);

    /// <summary>Returns whether <paramref name="scenarioId"/> is a product home demo.</summary>
    public static bool HasScenario(string scenarioId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        return TryGetEntry(scenarioId, out _);
    }

    /// <summary>
    /// Resolves one home demo. Allocates only that demo's peer definition records.
    /// Enforces the closed section-preset binding
    /// (<see cref="ProductDemoSections.EnsureHomeDemoBinding"/>).
    /// </summary>
    public static ResolvedScenario ResolveHomeScenario(string scenarioId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        if (!TryGetEntry(scenarioId, out var entry))
        {
            throw new InspectionDefinitionException(
                $"Unknown product home demo scenario '{scenarioId}'.");
        }

        var resolved = CreateRegistry(entry.CreateRecords()).ResolveScenario(entry.Id);
        ProductDemoSections.EnsureHomeDemoBinding(resolved);
        return resolved;
    }

    /// <summary>
    /// Tries to resolve one home demo without throwing on unknown ids.
    /// Returns false when the id is unknown. Throws when the demo is known but
    /// fails the section-preset binding (misconfigured product data).
    /// </summary>
    public static bool TryResolveHomeScenario(
        string scenarioId,
        [NotNullWhen(true)] out ResolvedScenario? resolved)
    {
        resolved = null;
        if (string.IsNullOrWhiteSpace(scenarioId) || !TryGetEntry(scenarioId, out var entry))
            return false;

        resolved = CreateRegistry(entry.CreateRecords()).ResolveScenario(entry.Id);
        ProductDemoSections.EnsureHomeDemoBinding(resolved);
        return true;
    }

    /// <summary>
    /// Builds a registry containing every home demo. Allocates all demos — use
    /// <see cref="ResolveHomeScenario"/> when only one is needed.
    /// </summary>
    public static InspectionDefinitionRegistry CreateRegistry()
    {
        var records = new List<InspectionDefinitionRecord>();
        foreach (var entry in s_entries)
            records.AddRange(entry.CreateRecords());
        return CreateRegistry(records);
    }

    private static bool TryGetEntry(string scenarioId, out Entry entry)
    {
        foreach (ref readonly var candidate in s_entries.AsSpan())
        {
            if (string.Equals(candidate.Id, scenarioId, StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }

    private static InspectionDefinitionRegistry CreateRegistry(
        IReadOnlyList<InspectionDefinitionRecord> records)
    {
        var registry = new InspectionDefinitionRegistry();
        foreach (var record in records)
            registry.Add(record);
        return registry;
    }

    private static InspectionDefinitionRecord[] CreateStjSerializerRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;
        var stjPackage = Package("System.Text.Json", "10.0.0", "net10.0");
        return
        [
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
                type: "System.Text.Json.JsonSerializer",
                section: ProductDemoSections.Methods),
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
        ];
    }

    private static InspectionDefinitionRecord[] CreateExtensionsCallGraphRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;
        var diAbstractions = Package(
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "10.0.0",
            "net10.0");
        var logging = Package("Microsoft.Extensions.Logging", "10.0.0", "net10.0");
        var http = Package("Microsoft.Extensions.Http", "10.0.0", "net10.0");
        return
        [
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
                section: ProductDemoSections.CallGraph),
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
        ];
    }

    /// <summary>
    /// Single-package outbound graph: <c>JsonSerializer.Serialize&lt;T&gt;(T, options)</c>.
    /// Complements the Methods STJ tour with a dense package-local Call Graph.
    /// </summary>
    private static InspectionDefinitionRecord[] CreateStjSerializeCallGraphRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;
        var stjPackage = Package("System.Text.Json", "10.0.0", "net10.0");
        return
        [
            new WorkspaceDefinition(
                v,
                "stj-serialize-callgraph-workspace",
                [
                    new WorkspaceContextDefinition(
                        "stj",
                        framework: "net10.0",
                        members: [stjPackage]),
                ],
                title: "System.Text.Json Serialize call graph",
                description: "Package-local Call Graph for JsonSerializer.Serialize."),
            new ViewDefinition(
                v,
                "stj-serialize-call-graph",
                type: "System.Text.Json.JsonSerializer",
                memberAnchor: "1dc14dd1fb",
                memberKey: "method:Serialize",
                section: ProductDemoSections.CallGraph),
            new NavigationDefinition(
                v,
                "stj-serialize-callgraph-navigation",
                [new NavigationTabDefinition("stj", coordinate: stjPackage)],
                focus: "stj"),
            new ScenarioDefinition(
                v,
                StjSerializeCallGraphScenarioId,
                title: "Serialize call graph",
                description: "Dense package-local STJ graph",
                workspace: "stj-serialize-callgraph-workspace",
                context: "stj",
                view: "stj-serialize-call-graph",
                navigation: "stj-serialize-callgraph-navigation"),
        ];
    }

    /// <summary>
    /// Single-package dense recursive graph: <c>ConfigurationBinder.Bind</c>.
    /// High fan-out into binder internals (arrays, conversion, BindingPoint).
    /// </summary>
    private static InspectionDefinitionRecord[] CreateConfigBindCallGraphRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;
        var binder = Package("Microsoft.Extensions.Configuration.Binder", "10.0.0", "net10.0");
        return
        [
            new WorkspaceDefinition(
                v,
                "config-bind-callgraph-workspace",
                [
                    new WorkspaceContextDefinition(
                        "binder",
                        framework: "net10.0",
                        members: [binder]),
                ],
                title: "Configuration Binder call graph",
                description: "Recursive ConfigurationBinder.Bind Call Graph."),
            new ViewDefinition(
                v,
                "config-bind-call-graph",
                type: "Microsoft.Extensions.Configuration.ConfigurationBinder",
                memberAnchor: "a6a6257f65",
                memberKey: "method:Bind",
                section: ProductDemoSections.CallGraph),
            new NavigationDefinition(
                v,
                "config-bind-callgraph-navigation",
                [new NavigationTabDefinition("binder", coordinate: binder)],
                focus: "binder"),
            new ScenarioDefinition(
                v,
                ConfigBindCallGraphScenarioId,
                title: "Configuration Bind",
                description: "Recursive binder call graph",
                workspace: "config-bind-callgraph-workspace",
                context: "binder",
                view: "config-bind-call-graph",
                navigation: "config-bind-callgraph-navigation"),
        ];
    }

    /// <summary>
    /// Single-package inbound hub: <c>AddOptions(IServiceCollection)</c>.
    /// Sibling Configure/PostConfigure/ValidateOnStart methods fan into the hub.
    /// </summary>
    private static InspectionDefinitionRecord[] CreateOptionsAddCallGraphRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;
        var options = Package("Microsoft.Extensions.Options", "10.0.0", "net10.0");
        return
        [
            new WorkspaceDefinition(
                v,
                "options-add-callgraph-workspace",
                [
                    new WorkspaceContextDefinition(
                        "options",
                        framework: "net10.0",
                        members: [options]),
                ],
                title: "Options AddOptions call graph",
                description: "Inbound fan-in Call Graph at Options.AddOptions."),
            new ViewDefinition(
                v,
                "options-add-call-graph",
                type: "Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions",
                memberAnchor: "1e6bfaf2ae",
                memberKey: "method:AddOptions",
                section: ProductDemoSections.CallGraph),
            new NavigationDefinition(
                v,
                "options-add-callgraph-navigation",
                [new NavigationTabDefinition("options", coordinate: options)],
                focus: "options"),
            new ScenarioDefinition(
                v,
                OptionsAddCallGraphScenarioId,
                title: "Options hub",
                description: "Inbound fan-in at AddOptions",
                workspace: "options-add-callgraph-workspace",
                context: "options",
                view: "options-add-call-graph",
                navigation: "options-add-callgraph-navigation"),
        ];
    }

    private static DefinitionMemberCoordinate.PackageCoordinate Package(
        string id,
        string version,
        string framework) =>
        new(id, version, framework);
}
