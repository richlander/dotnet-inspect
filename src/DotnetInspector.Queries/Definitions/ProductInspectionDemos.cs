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

    public const string PlatformListScenarioId = "platform-list";

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
            PlatformListScenarioId,
            ".NET Platform",
            "Inspect platform BCL types",
            CreatePlatformListRecords),
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

    private static InspectionDefinitionRecord[] CreatePlatformListRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;
        var stjPackage = Package("System.Text.Json", "10.0.0", "net10.0");
        // Unversioned host runtime: CoreLib is runtime-only (no ref-pack download),
        // so a patch pin would fail on machines without that exact shared framework
        // (CI installs the 11.0.x SDK lane). Package demos still pin NuGet versions.
        var runtimePlatform = new DefinitionMemberCoordinate.PlatformCoordinate(
            "runtime",
            Assembly: null,
            Version: null,
            Framework: null);
        return
        [
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
                type: "System.Collections.Generic.List`1",
                section: ProductDemoSections.Methods),
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

    private static DefinitionMemberCoordinate.PackageCoordinate Package(
        string id,
        string version,
        string framework) =>
        new(id, version, framework);
}
