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
        Assert.Equal(4, surface.Functions.Count);
        Assert.Contains("GetWidget", names);
        Assert.Contains("GetWidgetAsync", names);
        Assert.Contains("Ping", names);
        Assert.Contains("RenameWidget", names);
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
        // WidgetDto and WidgetCatalog are the JsonSerializable roots on FixtureJsonContext;
        // WidgetOwner is only reachable transitively (through WidgetDto's Owner property and
        // WidgetCatalog's OwnersByKey dictionary value type).
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        var recordNames = surface.Records.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(3, surface.Records.Count);
        Assert.Contains("WidgetDto", recordNames);
        Assert.Contains("WidgetOwner", recordNames);
        Assert.Contains("WidgetCatalog", recordNames);
    }

    [Fact]
    public void Build_DiscoversRecordNestedInsideANonFirstGenericArgument()
    {
        // WidgetCatalog.OwnersByKey is a Dictionary<string, WidgetOwner>: WidgetOwner is the
        // second, not first, generic type argument. Verifies ExtractCandidateTypeNames walks
        // every top-level comma-separated generic argument, not just the leading one.
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        Assert.Contains(surface.Records, r => r.Name == "WidgetOwner");
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

    [Fact]
    public void Build_DoesNotThrow_WhenTwoDistinctTypesShareASimpleName()
    {
        // Two types in different namespaces sharing a simple name ("Result") are ambiguous under
        // the simple-name lookup this builder uses (see remarks on JsExportSurfaceBuilder). Build
        // must not throw over an unrelated collision elsewhere in the assembly; the ambiguous name
        // simply fails to resolve as a known record.
        var jsonContextType = new ApiType
        {
            Name = "SurfaceJsonContext",
            BaseType = "System.Text.Json.Serialization.JsonSerializerContext",
            Members =
            [
                new ApiMember
                {
                    Name = "Widget",
                    Kind = "property",
                    ReturnType = "System.Text.Json.Serialization.Metadata.JsonTypeInfo<Widget>",
                },
            ],
        };

        var widgetType = new ApiType
        {
            Name = "Widget",
            Members =
            [
                new ApiMember { Name = "Value", Kind = "property", ReturnType = "Result" },
            ],
        };

        var resultInNamespaceA = new ApiType { Namespace = "A", Name = "Result" };
        var resultInNamespaceB = new ApiType { Namespace = "B", Name = "Result" };

        var apiSurface = new ApiSurface
        {
            Types = [jsonContextType, widgetType, resultInNamespaceA, resultInNamespaceB],
        };

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        Assert.Contains(surface.Records, r => r.Name == "Widget");
        Assert.DoesNotContain(surface.Records, r => r.Name == "Result");
    }
}
