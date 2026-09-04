using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Ecosystems;

internal static class ProductEcosystemPacks
{
    internal static EcosystemPackRegistry Registry { get; } = new(
    [
        new(
            EcosystemPackIds.Platform,
            "Platform",
            "Core .NET product demos.",
            100,
            PackageSet: null,
            [
                Demo(ProductDemoIds.StjSerializer, "System.Text.Json", "Browse a real package API", 100, CreateStjSerializerRecords),
                Demo(ProductDemoIds.StjSerializeCallGraph, "Serialize call graph", "Dense package-local STJ graph", 300, CreateStjSerializeCallGraphRecords),
                Demo(ProductDemoIds.StjGetDecimalCallGraph, "JsonElement.GetDecimal", "STJ number parse path", 800, CreateStjGetDecimalCallGraphRecords),
            ]),
        new(
            EcosystemPackIds.MicrosoftExtensions,
            "Microsoft.Extensions",
            "Microsoft.Extensions package and demo content.",
            200,
            PackageSetIds.MicrosoftExtensions,
            [
                Demo(ProductDemoIds.ExtensionsCallGraph, "Cross-package call graph", "Trace calls across three packages", 200, CreateExtensionsCallGraphRecords),
                Demo(ProductDemoIds.ConfigBindCallGraph, "Configuration Bind", "Recursive binder call graph", 400, CreateConfigBindCallGraphRecords),
                Demo(ProductDemoIds.OptionsAddCallGraph, "Options hub", "Inbound fan-in at AddOptions", 500, CreateOptionsAddCallGraphRecords),
                Demo(ProductDemoIds.DiTryAddCallGraph, "DI TryAdd hub", "Keyed/scoped Try* fan-in", 600, CreateDiTryAddCallGraphRecords),
                Demo(ProductDemoIds.HttpAddHttpClientCallGraph, "AddHttpClient", "HttpClient factory registration", 700, CreateHttpAddHttpClientCallGraphRecords),
            ]),
        new(
            EcosystemPackIds.AspNetCore,
            "ASP.NET Core",
            "ASP.NET Core package content.",
            300,
            PackageSetIds.AspNetCore,
            []),
        new(
            EcosystemPackIds.Aspire,
            "Aspire",
            "Aspire package and demo content.",
            400,
            PackageSetIds.Aspire,
            [
                Demo(ProductDemoIds.AspirePostgresCallGraph, "Aspire AddPostgres", "PostgreSQL resource registration graph", 900, CreateAspirePostgresCallGraphRecords),
                Demo(ProductDemoIds.AspireRedisCallGraph, "Aspire AddRedis", "Redis resource registration graph", 1000, CreateAspireRedisCallGraphRecords),
            ]),
    ]);

    private static EcosystemDemoRegistration Demo(
        string scenarioId,
        string title,
        string summary,
        int order,
        Func<InspectionDefinitionRecord[]> createRecords) =>
        new(
            title,
            summary,
            order,
            ProductDemoSourceBinding.Create(scenarioId, createRecords));

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
                ProductDemoIds.StjSerializer,
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
                ProductDemoIds.ExtensionsCallGraph,
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
                ProductDemoIds.StjSerializeCallGraph,
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
                ProductDemoIds.ConfigBindCallGraph,
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
                ProductDemoIds.OptionsAddCallGraph,
                title: "Options hub",
                description: "Inbound fan-in at AddOptions",
                workspace: "options-add-callgraph-workspace",
                context: "options",
                view: "options-add-call-graph",
                navigation: "options-add-callgraph-navigation"),
        ];
    }

    /// <summary>
    /// Package-local inbound hub: <c>TryAdd(IServiceCollection, ServiceDescriptor)</c>.
    /// Keyed/scoped/singleton/transient Try* overloads fan into the hub (high fan-in).
    /// </summary>
    private static InspectionDefinitionRecord[] CreateDiTryAddCallGraphRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;
        var di = Package(
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "10.0.0",
            "net10.0");
        return
        [
            new WorkspaceDefinition(
                v,
                "di-tryadd-callgraph-workspace",
                [
                    new WorkspaceContextDefinition(
                        "di",
                        framework: "net10.0",
                        members: [di]),
                ],
                title: "DI TryAdd call graph",
                description: "Inbound fan-in Call Graph at ServiceCollectionDescriptorExtensions.TryAdd."),
            new ViewDefinition(
                v,
                "di-tryadd-call-graph",
                type: "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
                memberAnchor: "6ce164c602",
                memberKey: "method:TryAdd",
                section: ProductDemoSections.CallGraph),
            new NavigationDefinition(
                v,
                "di-tryadd-callgraph-navigation",
                [new NavigationTabDefinition("di", coordinate: di)],
                focus: "di"),
            new ScenarioDefinition(
                v,
                ProductDemoIds.DiTryAddCallGraph,
                title: "DI TryAdd hub",
                description: "Keyed/scoped Try* fan-in",
                workspace: "di-tryadd-callgraph-workspace",
                context: "di",
                view: "di-tryadd-call-graph",
                navigation: "di-tryadd-callgraph-navigation"),
        ];
    }

    /// <summary>
    /// HttpClient factory registration: <c>AddHttpClient(IServiceCollection)</c>.
    /// Overload family funnels into one body that wires Logging, Options, Metrics,
    /// and TryAddEnumerable externally.
    /// </summary>
    private static InspectionDefinitionRecord[] CreateHttpAddHttpClientCallGraphRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;
        var http = Package("Microsoft.Extensions.Http", "10.0.0", "net10.0");
        return
        [
            new WorkspaceDefinition(
                v,
                "http-addhttpclient-callgraph-workspace",
                [
                    new WorkspaceContextDefinition(
                        "http",
                        framework: "net10.0",
                        members: [http]),
                ],
                title: "AddHttpClient call graph",
                description: "HttpClient factory registration Call Graph."),
            new ViewDefinition(
                v,
                "http-addhttpclient-call-graph",
                type: "Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions",
                memberAnchor: "5c44566d15",
                memberKey: "method:AddHttpClient",
                section: ProductDemoSections.CallGraph),
            new NavigationDefinition(
                v,
                "http-addhttpclient-callgraph-navigation",
                [new NavigationTabDefinition("http", coordinate: http)],
                focus: "http"),
            new ScenarioDefinition(
                v,
                ProductDemoIds.HttpAddHttpClientCallGraph,
                title: "AddHttpClient",
                description: "HttpClient factory registration",
                workspace: "http-addhttpclient-callgraph-workspace",
                context: "http",
                view: "http-addhttpclient-call-graph",
                navigation: "http-addhttpclient-callgraph-navigation"),
        ];
    }

    /// <summary>
    /// STJ number parse path: <c>JsonElement.GetDecimal()</c>.
    /// Complements Serialize with a document/Utf8Parser-oriented outbound graph.
    /// </summary>
    private static InspectionDefinitionRecord[] CreateStjGetDecimalCallGraphRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;
        var stj = Package("System.Text.Json", "10.0.0", "net10.0");
        return
        [
            new WorkspaceDefinition(
                v,
                "stj-getdecimal-callgraph-workspace",
                [
                    new WorkspaceContextDefinition(
                        "stj",
                        framework: "net10.0",
                        members: [stj]),
                ],
                title: "JsonElement.GetDecimal call graph",
                description: "STJ number parse Call Graph for JsonElement.GetDecimal."),
            new ViewDefinition(
                v,
                "stj-getdecimal-call-graph",
                type: "System.Text.Json.JsonElement",
                memberAnchor: "cfd9980a6c",
                memberKey: "method:GetDecimal",
                section: ProductDemoSections.CallGraph),
            new NavigationDefinition(
                v,
                "stj-getdecimal-callgraph-navigation",
                [new NavigationTabDefinition("stj", coordinate: stj)],
                focus: "stj"),
            new ScenarioDefinition(
                v,
                ProductDemoIds.StjGetDecimalCallGraph,
                title: "JsonElement.GetDecimal",
                description: "STJ number parse path",
                workspace: "stj-getdecimal-callgraph-workspace",
                context: "stj",
                view: "stj-getdecimal-call-graph",
                navigation: "stj-getdecimal-callgraph-navigation"),
        ];
    }

    private static InspectionDefinitionRecord[] CreateAspirePostgresCallGraphRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;
        var postgres = Package("Aspire.Hosting.PostgreSQL", "13.5.3", "net8.0");
        return
        [
            new WorkspaceDefinition(
                v,
                "aspire-postgres-callgraph-workspace",
                [
                    new WorkspaceContextDefinition(
                        "postgres",
                        framework: "net8.0",
                        members: [postgres]),
                ],
                title: "Aspire PostgreSQL call graph",
                description: "Resource registration Call Graph for Aspire AddPostgres."),
            new ViewDefinition(
                v,
                "aspire-postgres-call-graph",
                type: "Aspire.Hosting.PostgresBuilderExtensions",
                memberAnchor: "e5a66a2bd9",
                memberKey: "method:AddPostgres",
                section: ProductDemoSections.CallGraph),
            new NavigationDefinition(
                v,
                "aspire-postgres-callgraph-navigation",
                [new NavigationTabDefinition("postgres", coordinate: postgres)],
                focus: "postgres"),
            new ScenarioDefinition(
                v,
                ProductDemoIds.AspirePostgresCallGraph,
                title: "Aspire PostgreSQL resource registration",
                description: "Portable scenario for AddPostgres inspection.",
                workspace: "aspire-postgres-callgraph-workspace",
                context: "postgres",
                view: "aspire-postgres-call-graph",
                navigation: "aspire-postgres-callgraph-navigation"),
        ];
    }

    private static InspectionDefinitionRecord[] CreateAspireRedisCallGraphRecords()
    {
        const int v = InspectionDefinitionJson.CurrentSchemaVersion;
        var redis = Package("Aspire.Hosting.Redis", "13.5.3", "net8.0");
        return
        [
            new WorkspaceDefinition(
                v,
                "aspire-redis-callgraph-workspace",
                [
                    new WorkspaceContextDefinition(
                        "redis",
                        framework: "net8.0",
                        members: [redis]),
                ],
                title: "Aspire Redis call graph",
                description: "Resource registration Call Graph for Aspire AddRedis."),
            new ViewDefinition(
                v,
                "aspire-redis-call-graph",
                type: "Aspire.Hosting.RedisBuilderExtensions",
                memberAnchor: "7618364a03",
                memberKey: "method:AddRedis",
                section: ProductDemoSections.CallGraph),
            new NavigationDefinition(
                v,
                "aspire-redis-callgraph-navigation",
                [new NavigationTabDefinition("redis", coordinate: redis)],
                focus: "redis"),
            new ScenarioDefinition(
                v,
                ProductDemoIds.AspireRedisCallGraph,
                title: "Aspire Redis resource registration",
                description: "Portable scenario for AddRedis inspection.",
                workspace: "aspire-redis-callgraph-workspace",
                context: "redis",
                view: "aspire-redis-call-graph",
                navigation: "aspire-redis-callgraph-navigation"),
        ];
    }

    private static DefinitionMemberCoordinate.PackageCoordinate Package(
        string id,
        string version,
        string framework) =>
        new(id, version, framework);
}
