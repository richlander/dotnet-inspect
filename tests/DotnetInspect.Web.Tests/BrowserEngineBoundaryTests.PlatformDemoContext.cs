using System.Text.Json;
using DotnetInspect.Web.Interop.Catalog;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using BrowserCallGraph = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraph;
using BrowserCallGraphJsonContext = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphJsonContext;
using CallGraphExports = DotnetInspect.Web.Interop.CallGraph.CallGraphExports;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task PlatformHomeDemo_ExportRetainsExactContextAcrossReloadAndDrill()
    {
        const string framework = "net10.0";
        const string version = "10.0.12";
        const string assembly = "Microsoft.Extensions.DependencyInjection.Abstractions";
        string assetDirectory = Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "PlatformDemo");
        string[] assemblies =
        [
            assembly,
            "Microsoft.Extensions.Logging",
            "Microsoft.Extensions.Http",
        ];
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                "microsoft.aspnetcore.app.runtime.linux-x64",
                version,
                PlatformPackage(
                    framework,
                    assemblies.Select(name =>
                        ($"{name}.dll", File.ReadAllBytes(
                            Path.Combine(assetDirectory, $"{name}.dll")))).ToArray()),
                fromCache: false));

        BrowserHomeDemoRunResult initial = Assert.IsType<BrowserHomeDemoRunResult>(
            JsonSerializer.Deserialize(
                await CatalogExports.RunHomeDemo(ProductDemoIds.ExtensionsCallGraph),
                BrowserCatalogJsonContext.Default.BrowserHomeDemoRunResult));
        BrowserHomeDemoRunActivation activation = Assert.IsType<BrowserHomeDemoRunActivation>(
            initial.Activation);
        string contextId = Assert.IsType<string>(activation.PlatformContextId);
        BrowserCallGraphTarget focus = Assert.Single(
            initial.CallGraph!.Targets, target => target.Kind == "focus");
        Assert.Equal(3, initial.CallGraph.Scope.Assemblies);
        Assert.Equal(assembly, focus.Assembly);

        BrowserCallGraph ordinary = await QueryAsync(focus, null);
        Assert.Equal(1, ordinary.Scope.Assemblies);
        BrowserCallGraph reloaded = await QueryAsync(focus, contextId);
        Assert.Equal(3, reloaded.Scope.Assemblies);
        Assert.Equal(3, reloaded.Scope.CallerAssemblies);
        Assert.Equal(initial.CallGraph.Mermaid, reloaded.Mermaid);
        BrowserPackageSurface logging = Assert.Single(initial.Packages,
            surface => surface.DefaultAssemblyId == "Microsoft.Extensions.Logging");
        BrowserTypeSurface loggingType = Assert.Single(logging.Types,
            type => type.DefinitionId == "Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions");
        BrowserMemberSurface addLogging = Assert.Single(loggingType.Api,
            member => member.DocumentationId
                == "M:Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(Microsoft.Extensions.DependencyInjection.IServiceCollection)");
        BrowserAssemblySurface loggingAssembly = Assert.Single(logging.Assemblies);
        BrowserCallGraphTarget caller = focus with
        {
            Assembly = loggingAssembly.Name,
            AssemblyVersion = loggingAssembly.Version,
            AssemblyCulture = loggingAssembly.Culture,
            AssemblyPublicKeyToken = loggingAssembly.PublicKeyToken,
            TypeFullName = loggingType.DefinitionId,
            TypeDefinitionId = loggingType.DefinitionId,
            MemberName = addLogging.Name,
            SelectorKey = addLogging.GraphSelectorKey,
            MetadataToken = addLogging.MetadataToken,
        };
        BrowserCallGraph drilled = await QueryAsync(caller, contextId);
        Assert.Equal(3, drilled.Scope.Assemblies);
        Assert.Contains(drilled.Targets,
            target => target.Kind == "focus"
                && target.Assembly == "Microsoft.Extensions.Logging");
        Assert.Equal(reloaded.Mermaid, (await QueryAsync(focus, contextId)).Mermaid);

        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                "microsoft.netcore.app.runtime.linux-x64",
                version,
                PlatformPackage(
                    framework,
                    new[] { "System.Runtime", "System.Private.CoreLib" }.Select(name =>
                        ($"{name}.dll", File.ReadAllBytes(
                            Path.Combine(assetDirectory, $"{name}.dll")))).ToArray()),
                fromCache: false));
        BrowserCallGraphTarget runtimeTarget = initial.CallGraph.Targets.First(target =>
            target.TypeDefinitionId == "System.Object"
                && target.MemberName == "GetType") with
        {
            PlatformPack = "netcore.app",
        };
        Assert.Equal("System.Runtime", runtimeTarget.Assembly);
        BrowserCallGraph crossFamily = await QueryAsync(runtimeTarget, contextId);
        Assert.Equal(5, crossFamily.Scope.Assemblies);
        Assert.Equal(5, (await QueryAsync(focus, contextId)).Scope.Assemblies);
        Assert.Equal(1, (await QueryAsync(focus, null)).Scope.Assemblies);

        var plan = new WorkspacePlan(
            [],
            [
                new WorkspaceContextInput
                {
                    Framework = framework,
                    Members = [WorkspaceMemberCoordinate.Platform(
                        "aspnetcore", assembly, version, framework)],
                },
            ]);
        await using BrowserPlatformScopeResolution replacement =
            await BrowserPlatformWorkspace.OpenContextAsync(
                plan,
                plan.Contexts[0],
                "aspnetcore",
                assembly,
                TestContext.Current.CancellationToken);
        Assert.NotEqual(contextId, replacement.ContextId);
        InvalidOperationException expired = await Assert.ThrowsAsync<InvalidOperationException>(
            () => QueryAsync(focus, contextId));
        Assert.Contains("ContextUnavailable", expired.Message, StringComparison.Ordinal);
        Assert.Equal(1, (await QueryAsync(focus, replacement.ContextId)).Scope.Assemblies);
        Assert.Equal(2, (await QueryAsync(caller, replacement.ContextId)).Scope.Assemblies);
        Assert.Equal(2, (await QueryAsync(focus, replacement.ContextId)).Scope.Assemblies);
        Assert.Equal(1, (await QueryAsync(focus, null)).Scope.Assemblies);

        await replacement.DisposeAsync();
        BrowserPlatformScope retained;
        await using (BrowserPlatformScopeResolution expanded =
            await BrowserPlatformWorkspace.OpenRetainedContextAssemblyAsync(
                replacement.ContextId!,
                framework,
                version,
                $"{assembly}.dll",
                "aspnetcore.app",
                TestContext.Current.CancellationToken))
        {
            retained = expanded.Scope;
            Assert.Equal(replacement.ContextId, expanded.ContextId);
        }
        await BrowserPackageWorkspace.RemoveScopeAsync(retained);
        InvalidOperationException evicted = await Assert.ThrowsAsync<InvalidOperationException>(
            () => QueryAsync(focus, replacement.ContextId));
        Assert.Contains("ContextUnavailable", evicted.Message, StringComparison.Ordinal);

        async Task<BrowserCallGraph> QueryAsync(BrowserCallGraphTarget target, string? selectedContext) =>
            Assert.IsType<BrowserCallGraph>(JsonSerializer.Deserialize(
                await CallGraphExports.ExpandPlatformCallGraph(
                    framework,
                    version,
                    target.Assembly!,
                    target.PlatformPack!,
                    target.AssemblyVersion!,
                    target.AssemblyCulture,
                    target.AssemblyPublicKeyToken,
                    target.TypeDefinitionId!,
                    target.MemberName,
                    target.SelectorKey,
                    target.MetadataToken ?? 0,
                    selectedContext),
                BrowserCallGraphJsonContext.Default.BrowserCallGraph));
    }
}
