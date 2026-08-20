using System.Reflection.PortableExecutable;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

public sealed class JsExportSurfaceBuilderTests
{
    private static ILInspector.JsExportSurface.JsExportSurface BuildFixtureSurface(bool includeAll = false)
    {
        using FileStream stream = File.OpenRead(typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: includeAll);
        return JsExportSurfaceBuilder.Build(apiSurface);
    }

    [Fact]
    public void Build_DiscoversAllJsExportFunctions()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();
        var names = surface.Functions.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("QueryPackage", names);
        Assert.Contains("GetInternalContextCamelWidget", names);
        Assert.Contains("GetInternalContextWidget", names);
    }

    [Fact]
    public void Build_AssignsNamingPolicyPerContextRoot()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface(includeAll: true);
        Assert.Null(Assert.Single(surface.Records, r => r.Name == "InternalContextPascalWidget").JsonPropertyNamingPolicy);
        Assert.Equal(
            JsonWireNamingPolicy.CamelCase,
            Assert.Single(surface.Records, r => r.Name == "InternalContextCamelWidget").JsonPropertyNamingPolicy);
    }

    [Fact]
    public void Build_CapturesJsonPropertyNameAndJsonIgnoreFacts()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface(includeAll: true);
        ApiType audit = Assert.Single(surface.Records, r => r.Name == "WidgetAudit");
        Assert.Equal("wire_name", Assert.Single(audit.Members, m => m.Name == "DisplayName").JsonPropertyName);
        Assert.Equal(string.Empty, Assert.Single(audit.Members, m => m.Name == "EmptyWireName").JsonPropertyName);
        Assert.Equal("display-name", Assert.Single(audit.Members, m => m.Name == "DisplayNameWithDash").JsonPropertyName);
        Assert.True(Assert.Single(audit.Members, m => m.Name == "IgnoredAtWire").HasJsonIgnore);
    }
}
