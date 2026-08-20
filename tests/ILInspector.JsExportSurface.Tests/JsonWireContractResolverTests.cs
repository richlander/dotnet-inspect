using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Verifies <see cref="JsonWireContractResolver"/> and its wiring into
/// <see cref="JsExportSurfaceBuilder.Build"/> against
/// <see cref="ILInspector.JsExportSurface.Fixtures.FixtureExports"/>: proves each
/// <c>[JSExport]</c> export's actual DTO is resolved from its own body's
/// <c>JsonSerializer.Serialize</c> call site, not inferred from the assembly's whole registered
/// shape vocabulary.
/// </summary>
public sealed class JsonWireContractResolverTests
{
    private static ILInspector.JsExportSurface.JsExportSurface BuildFixtureSurfaceWithWireContracts()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(path);
        return JsExportSurfaceBuilder.Build(apiSurface, bodyIndex);
    }

    [Fact]
    public void Build_ResolvesReturnWireTypeForSyncExport()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction getWidget = Assert.Single(
            surface.Functions,
            f => f.Name == "GetWidget");
        Assert.Equal("WidgetDto", getWidget.ReturnWireType);
        Assert.Empty(getWidget.ParameterWireTypes);
    }

    [Fact]
    public void Build_ResolvesReturnWireTypeForAsyncExport()
    {
        // GetWidgetAsync's JsonSerializer.Serialize call is physically emitted in the compiler
        // generated state machine's MoveNext body, not GetWidgetAsync's own body. This only
        // resolves correctly because DirectCall.Caller is already attributed to the declared
        // async method (see PR #4461 / issue #4459) rather than to MoveNext.
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction getWidgetAsync = Assert.Single(
            surface.Functions,
            f => f.Name == "GetWidgetAsync");
        Assert.Equal("WidgetDto", getWidgetAsync.ReturnWireType);
        Assert.Empty(getWidgetAsync.ParameterWireTypes);
    }

    [Fact]
    public void Build_LeavesWireContractUnsetForNonEnvelopeExport()
    {
        // Ping has no JSON envelope at all (returns a non-generic Task), so no
        // JsonSerializer.Serialize/Deserialize call site exists in its body.
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction ping = Assert.Single(surface.Functions, f => f.Name == "Ping");
        Assert.Null(ping.ReturnWireType);
        Assert.Empty(ping.ParameterWireTypes);
    }

    [Fact]
    public void Build_WithoutBodyIndex_LeavesWireContractFieldsUnset()
    {
        // The overload without a LibraryBodyIndex must not attempt call-site resolution.
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);

        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);

        Assert.All(surface.Functions, f => Assert.Null(f.ReturnWireType));
        Assert.All(surface.Functions, f => Assert.Empty(f.ParameterWireTypes));
    }
}
