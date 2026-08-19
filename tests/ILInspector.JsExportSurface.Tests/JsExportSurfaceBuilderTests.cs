using System.Reflection.PortableExecutable;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Verifies <see cref="JsExportSurfaceBuilder.Build"/> against
/// <see cref="ILInspector.JsExportSurface.Fixtures.FixtureExports"/>: a small, purpose-built
/// <c>[JSExport]</c> surface covering sync/async/non-generic-<c>Task</c> exports and nested/array/
/// nullable record shapes.
/// </summary>
public sealed class JsExportSurfaceBuilderTests
{
    private static ILInspector.JsExportSurface.JsExportSurface BuildFixtureSurface()
    {
        using FileStream stream = File.OpenRead(typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);
        return JsExportSurfaceBuilder.Build(apiSurface);
    }

    [Fact]
    public void Build_DiscoversAllThreeJsExportFunctions()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        var names = surface.Functions.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(3, surface.Functions.Count);
        Assert.Contains("GetWidget", names);
        Assert.Contains("GetWidgetAsync", names);
        Assert.Contains("Ping", names);
    }

    [Fact]
    public void Build_ReportsFunctionSignaturesUnmodified()
    {
        // The OM stays C#-faithful: Task<string> is reported as-is, not unwrapped.
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        JsExportFunction getWidget = surface.Functions.Single(f => f.Name == "GetWidget");
        Assert.Equal("string", getWidget.ReturnType);
        Assert.Equal(2, getWidget.Parameters.Count);

        JsExportFunction getWidgetAsync = surface.Functions.Single(f => f.Name == "GetWidgetAsync");
        Assert.Contains("Task", getWidgetAsync.ReturnType, StringComparison.Ordinal);
        Assert.Contains("string", getWidgetAsync.ReturnType, StringComparison.Ordinal);

        JsExportFunction ping = surface.Functions.Single(f => f.Name == "Ping");
        Assert.Contains("Task", ping.ReturnType, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DiscoversJsonSerializerContextRootAndItsTransitiveNestedRecord()
    {
        // WidgetDto is the sole JsonSerializable root on FixtureJsonContext; WidgetOwner is only
        // reachable transitively, through WidgetDto's Owner property.
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        var recordNames = surface.Records.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(2, surface.Records.Count);
        Assert.Contains("WidgetDto", recordNames);
        Assert.Contains("WidgetOwner", recordNames);
    }

    [Fact]
    public void Build_DoesNotDiscoverTheJsonSerializerContextTypeItselfAsARecord()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        Assert.DoesNotContain(surface.Records, r => r.Name == "FixtureJsonContext");
    }

    [Fact]
    public void Build_WidgetDtoExposesAllFourDeclaredProperties()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        ApiType widgetDto = surface.Records.Single(r => r.Name == "WidgetDto");
        var propertyNames = widgetDto.Members
            .Where(m => m.Kind == "property")
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(4, propertyNames.Count);
        Assert.Contains("Name", propertyNames);
        Assert.Contains("Count", propertyNames);
        Assert.Contains("Tags", propertyNames);
        Assert.Contains("Owner", propertyNames);
    }
}
