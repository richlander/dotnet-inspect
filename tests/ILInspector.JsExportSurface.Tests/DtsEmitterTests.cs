using System.Reflection.PortableExecutable;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.Metadata;
using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Verifies <see cref="DtsEmitter.Emit"/> end to end against
/// <see cref="ILInspector.JsExportSurface.Fixtures.FixtureExports"/>: correct camelCase naming,
/// nested-record ordering, array/nullable mapping, and Task/void rewriting all compose correctly
/// when projected together.
/// </summary>
public sealed class DtsEmitterTests
{
    private static string EmitFixtureDts()
    {
        using FileStream stream = File.OpenRead(typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);
        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);
        return DtsEmitter.Emit(surface);
    }

    [Fact]
    public void Emit_ProducesInterfacesForBothRecords()
    {
        string dts = EmitFixtureDts();

        Assert.Contains("export interface WidgetDto {", dts, StringComparison.Ordinal);
        Assert.Contains("export interface WidgetOwner {", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_UsesCamelCasePropertyNames()
    {
        string dts = EmitFixtureDts();

        Assert.Contains("  name: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  count: number;", dts, StringComparison.Ordinal);
        Assert.Contains("  displayName: string;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_MapsArrayAndNullableRecordProperties()
    {
        string dts = EmitFixtureDts();

        Assert.Contains("  tags: number[];", dts, StringComparison.Ordinal);
        Assert.Contains("  owner: WidgetOwner | null;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_DeclaresFunctionsWithCamelCaseNamesAndPromiseReturnTypes()
    {
        string dts = EmitFixtureDts();

        Assert.Contains(
            "export declare function getWidget(name: string, count: number): string;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function getWidgetAsync(name: string): Promise<string>;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function ping(): Promise<void>;",
            dts,
            StringComparison.Ordinal);
    }
}
