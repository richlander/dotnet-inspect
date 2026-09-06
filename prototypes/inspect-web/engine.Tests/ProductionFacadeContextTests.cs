using System.Reflection;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using TsJsExport;

namespace InspectWeb.Engine.Tests;

/// <summary>
/// Gates the production facade partition described in
/// <c>docs/design/inspect-web-jsexport-partitioning.md</c>: the compiled context's assembly set,
/// the export partition over those assemblies, the project graph's direction, and each facade's
/// self-contained JSON wire closure.
/// </summary>
/// <remarks>
/// Every expectation below is compared against state derived from compiled metadata or the
/// checked-in project files. None of them can pass by construction: an omitted root, a moved
/// export, a sibling project reference, or a wire type borrowed from another assembly all fail.
/// </remarks>
public sealed class ProductionFacadeContextTests
{
    const string HostAssembly = "InspectWeb.Engine";
    const string PackageAssembly = "InspectWeb.Engine.PackageExports";
    const string MetadataAssembly = "InspectWeb.Engine.MetadataExports";
    const string AnalysisAssembly = "InspectWeb.Engine.AnalysisExports";
    const string SourceAssembly = "InspectWeb.Engine.SourceExports";
    const string CallGraphAssembly = "InspectWeb.Engine.CallGraphExports";
    const string CatalogAssembly = "InspectWeb.Engine.CatalogExports";
    const string CoreAssembly = "InspectWeb.Engine.Core";

    static readonly string[] ExpectedAssemblies =
    [
        HostAssembly,
        PackageAssembly,
        MetadataAssembly,
        AnalysisAssembly,
        SourceAssembly,
        CallGraphAssembly,
        CatalogAssembly,
    ];

    /// <summary>
    /// The exhaustive target inventory from the owning design. It is stated here and compared
    /// against the exports the compiled root assemblies actually declare.
    /// </summary>
    static readonly Dictionary<string, string[]> ExpectedPartition = new(StringComparer.Ordinal)
    {
        [HostAssembly] =
        [
            "AsyncLoweringCanary",
            "BuildIdentity",
            "ConfigureHost",
        ],
        [PackageAssembly] =
        [
            "ActivateWorkspacePackageOccurrence",
            "CancelPackageQuery",
            "ClearWorkspacePackageOccurrences",
            "GetPackageDocument",
            "ListGalleryDiscoveryCatalog",
            "ListPackageQueryFacets",
            "LoadRuntimePack",
            "LoadRuntimePackAssembly",
            "MatchPackageDependencyCoordinate",
            "PackageCacheStats",
            "QueryMemberDocumentation",
            "QueryPackage",
            "QueryPackageDependencies",
            "QueryPackageVersions",
            "QueryWorkspacePackageOccurrences",
            "RequestPackageQueryMatches",
            "ResolvePackageDependencyVersion",
            "RunPackageQuery",
            "SearchTypes",
        ],
        [MetadataAssembly] =
        [
            "QueryGraphMemberSurface",
            "QueryPackageHeapEntries",
            "QueryPackageMetadata",
            "QueryPackageMetadataTable",
            "QueryPlatformHeapEntries",
            "QueryPlatformMetadata",
            "QueryPlatformMetadataTable",
            "QueryTypeProjection",
        ],
        [AnalysisAssembly] =
        [
            "QueryMemberFacts",
            "QueryPackageIntegrations",
            "QueryPackageOpportunities",
            "QueryPackagePerformance",
            "QueryPlatformIntegrations",
            "QueryPlatformOpportunities",
            "QueryPlatformPerformance",
        ],
        [SourceAssembly] =
        [
            "CancelMethodBodyComparison",
            "CancelSourceQuery",
            "CancelTypeSourceQuery",
            "QueryMethodBodyComparison",
            "QueryMethodBodyComparisonTargets",
            "QueryMemberAnnotatedSource",
            "QueryMemberFindingCensus",
            "QueryMemberSource",
            "QueryTypeMemberSource",
            "QueryTypeSource",
        ],
        [CallGraphAssembly] =
        [
            "ExpandPlatformCallGraph",
            "QueryMemberCallGraph",
        ],
        [CatalogAssembly] =
        [
            "DecodeWorkspaceShareState",
            "EncodeWorkspaceShareState",
            "ListHomeDemos",
            "ListVocabulary",
            "ResolveHomeDemo",
            "RunHomeDemo",
        ],
    };

    [Fact]
    public void ProductionFacadeContext_DeclaresExactAssemblySet()
    {
        string[] declared =
        [
            .. RootTypes().Select(root => AssemblyNameOf(root.Assembly)),
        ];

        Assert.Equal(ExpectedAssemblies.Length, declared.Length);
        Assert.Equal(
            [.. ExpectedAssemblies.Order(StringComparer.Ordinal)],
            [.. declared.Order(StringComparer.Ordinal)]);
        // A root anchors one assembly, so two roots may never name the same module.
        Assert.Equal(declared.Length, declared.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(CoreAssembly, declared);
    }

    [Fact]
    public void ProductionFacadePartition_AssignsEveryJsExportExactlyOnce()
    {
        Dictionary<string, string[]> actual = new(StringComparer.Ordinal);
        foreach (Type root in RootTypes())
        {
            Assembly assembly = root.Assembly;
            string name = AssemblyNameOf(assembly);
            Assert.False(
                actual.ContainsKey(name),
                $"Assembly '{name}' is rooted more than once.");
            actual[name] = [.. JsExports(assembly).Order(StringComparer.Ordinal)];
        }

        Assert.Equal(
            [.. ExpectedPartition.Keys.Order(StringComparer.Ordinal)],
            [.. actual.Keys.Order(StringComparer.Ordinal)]);
        foreach ((string assembly, string[] expected) in ExpectedPartition)
        {
            Assert.Equal(
                [.. expected.Order(StringComparer.Ordinal)],
                actual[assembly]);
        }

        // 55 operations, and no operation name in two modules: a move that forgot to delete its
        // origin, or a name published twice, fails here rather than in the browser.
        string[] everyExport = [.. actual.Values.SelectMany(names => names)];
        Assert.Equal(55, everyExport.Length);
        Assert.Equal(
            everyExport.Length,
            everyExport.Distinct(StringComparer.Ordinal).Count());

        // InspectWeb.Engine.Core carries no export at all.
        Assert.Empty(JsExports(typeof(BrowserPackageWorkspace).Assembly));
    }

    [Fact]
    public void ProductionFacadeProjects_HaveAcyclicOwnerReferences()
    {
        string[] capabilities =
        [
            PackageAssembly,
            MetadataAssembly,
            AnalysisAssembly,
            SourceAssembly,
            CallGraphAssembly,
            CatalogAssembly,
        ];

        string[] hostReferences = BrowserProjectReferences(HostAssembly);
        Assert.Contains(CoreAssembly, hostReferences);
        foreach (string capability in capabilities)
            Assert.Contains(capability, hostReferences);

        foreach (string capability in capabilities)
        {
            string[] references = BrowserProjectReferences(capability);
            Assert.Contains(CoreAssembly, references);
            Assert.DoesNotContain(HostAssembly, references);
            foreach (string sibling in capabilities.Where(
                other => !other.Equals(capability, StringComparison.Ordinal)))
            {
                Assert.DoesNotContain(sibling, references);
            }
        }

        Assert.Empty(BrowserProjectReferences(CoreAssembly));

        // The direction above already forbids a cycle, so a cycle here means a project moved.
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Walk(string project)
        {
            Assert.True(
                visiting.Add(project),
                $"Browser project cycle through '{project}'.");
            if (visited.Add(project))
            {
                foreach (string reference in BrowserProjectReferences(project))
                    Walk(reference);
            }
            visiting.Remove(project);
        }

        Walk(HostAssembly);
        Assert.Equal(
            [
                .. capabilities
                    .Append(CoreAssembly)
                    .Append(HostAssembly)
                    .Order(StringComparer.Ordinal),
            ],
            [.. visited.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void ProductionFacadeWireContexts_AreAssemblyLocal()
    {
        var contexts = 0;
        var assemblyLocalWireTypes = 0;
        foreach (Type root in RootTypes())
        {
            Assembly assembly = root.Assembly;
            string owner = AssemblyNameOf(assembly);
            Type[] declaredContexts =
            [
                .. assembly.GetTypes()
                    .Where(type => typeof(JsonSerializerContext).IsAssignableFrom(type)
                        && !type.IsAbstract),
            ];
            Assert.NotEmpty(declaredContexts);
            contexts += declaredContexts.Length;

            var closure = new HashSet<Type>();
            foreach (Type context in declaredContexts)
            {
                Type[] serializable = [.. SerializableRoots(context)];
                Assert.NotEmpty(serializable);
                foreach (Type declared in serializable)
                    Collect(declared, closure);
            }

            Assert.NotEmpty(closure);
            foreach (Type type in closure)
            {
                string declaring = AssemblyNameOf(type.Assembly);
                if (declaring.StartsWith("InspectWeb.", StringComparison.Ordinal))
                {
                    Assert.Equal(owner, declaring);
                    assemblyLocalWireTypes++;
                    continue;
                }

                // No raw product object reaches TypeScript: each facade transports its own record.
                Assert.False(
                    declaring.StartsWith("ILInspector.", StringComparison.Ordinal)
                    || declaring.StartsWith("DotnetInspector.", StringComparison.Ordinal)
                    || declaring.StartsWith("CSharpText", StringComparison.Ordinal),
                    $"{owner} serializes product-owned type '{type.FullName}'.");
            }
        }

        Assert.Equal(ExpectedAssemblies.Length, contexts);
        Assert.True(
            assemblyLocalWireTypes > 0,
            "No assembly-local wire type was discovered.");
    }

    static IEnumerable<Type> SerializableRoots(Type context)
    {
        foreach (CustomAttributeData attribute in context.GetCustomAttributesData())
        {
            if (attribute.AttributeType != typeof(JsonSerializableAttribute)
                || attribute.ConstructorArguments.Count == 0
                || attribute.ConstructorArguments[0].Value is not Type declared)
            {
                continue;
            }

            yield return declared;
        }
    }

    static void Collect(Type type, HashSet<Type> closure)
    {
        Type resolved = Nullable.GetUnderlyingType(type) ?? type;
        if (resolved.IsArray)
        {
            Collect(resolved.GetElementType()!, closure);
            return;
        }

        if (resolved.IsGenericType)
        {
            foreach (Type argument in resolved.GetGenericArguments())
                Collect(argument, closure);
            resolved = resolved.GetGenericTypeDefinition();
        }

        if (resolved.IsPrimitive || resolved == typeof(string) || !closure.Add(resolved))
            return;

        foreach (PropertyInfo property in resolved.GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0)
                Collect(property.PropertyType, closure);
        }
    }

    static IEnumerable<string> JsExports(Assembly assembly) =>
        assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Static
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributesData().Any(attribute =>
                attribute.AttributeType.FullName
                == "System.Runtime.InteropServices.JavaScript.JSExportAttribute"))
            .Select(method => method.Name);

    static IEnumerable<Type> RootTypes() =>
        typeof(InspectWebJsExportContext)
            .GetCustomAttributes<JsExportRootAttribute>(inherit: false)
            .Select(root => root.RootType);

    static string AssemblyNameOf(Assembly assembly) =>
        assembly.GetName().Name
        ?? throw new InvalidOperationException("An export assembly has no simple name.");

    static readonly Dictionary<string, string> BrowserProjectPaths = new(StringComparer.Ordinal)
    {
        [HostAssembly] = Path.Combine("engine", "InspectWeb.Engine.csproj"),
        [CoreAssembly] = Path.Combine("engine.Core", "InspectWeb.Engine.Core.csproj"),
        [PackageAssembly] =
            Path.Combine("engine.PackageExports", "InspectWeb.Engine.PackageExports.csproj"),
        [MetadataAssembly] =
            Path.Combine("engine.MetadataExports", "InspectWeb.Engine.MetadataExports.csproj"),
        [AnalysisAssembly] =
            Path.Combine("engine.AnalysisExports", "InspectWeb.Engine.AnalysisExports.csproj"),
        [SourceAssembly] =
            Path.Combine("engine.SourceExports", "InspectWeb.Engine.SourceExports.csproj"),
        [CallGraphAssembly] =
            Path.Combine("engine.CallGraphExports", "InspectWeb.Engine.CallGraphExports.csproj"),
        [CatalogAssembly] =
            Path.Combine("engine.CatalogExports", "InspectWeb.Engine.CatalogExports.csproj"),
    };

    /// <summary>
    /// The browser-owned projects one project references, by assembly name. Product references are
    /// deliberately out of scope: this gates the facade graph's direction, not its product needs.
    /// </summary>
    static string[] BrowserProjectReferences(string assembly)
    {
        string project = Path.Combine(InspectWebRoot(), BrowserProjectPaths[assembly]);
        Assert.True(File.Exists(project), $"Missing browser project '{project}'.");
        return
        [
            .. XDocument.Load(project)
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value
                    ?? throw new InvalidOperationException(
                        $"A ProjectReference in '{project}' has no Include."))
                .Select(include => Path.GetFileNameWithoutExtension(
                    include.Replace('\\', Path.DirectorySeparatorChar)))
                .Where(BrowserProjectPaths.ContainsKey),
        ];
    }

    static string InspectWebRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return Path.Combine(directory.FullName, "prototypes", "inspect-web");
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing dotnet-inspect.slnx.");
    }
}
