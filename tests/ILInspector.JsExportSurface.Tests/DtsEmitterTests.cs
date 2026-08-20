using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.Metadata;
using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

public sealed class DtsEmitterTests
{
    private static string EmitFixtureDts(bool includeAll = false)
    {
        using FileStream stream = File.OpenRead(typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: includeAll);
        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);
        return DtsEmitter.Emit(surface);
    }

    private static string EmitFixtureDtsWithWireContracts()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(path);
        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface, bodyIndex);
        return DtsEmitter.Emit(surface);
    }

    [Fact]
    public void Emit_UsesContextNamingPolicyForFixtureProperties()
    {
        string dts = EmitFixtureDts();
        Assert.Contains("  name: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  count: number;", dts, StringComparison.Ordinal);
        Assert.Contains("  displayName: string;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_PreservesPascalCaseForNoPolicyContextEvenWhenAnotherContextUsesCamelCase()
    {
        string dts = EmitFixtureDts(includeAll: true);
        Assert.Contains("export interface InternalContextPascalWidget {", dts, StringComparison.Ordinal);
        Assert.Contains("  Name: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  Count: number;", dts, StringComparison.Ordinal);
        Assert.Contains("export interface InternalContextCamelWidget {", dts, StringComparison.Ordinal);
        Assert.Contains("  name: string;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_MapsArrayAndNullableRecordProperties()
    {
        string dts = EmitFixtureDts();
        Assert.Contains("  tags: number[];", dts, StringComparison.Ordinal);
        Assert.Contains("  owner: WidgetOwner | null;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_UsesVerbatimJsExportFunctionNames()
    {
        string dts = EmitFixtureDts();
        Assert.Contains("export declare function QueryPackage(packageId: string): string;", dts, StringComparison.Ordinal);
        Assert.DoesNotContain("export declare function queryPackage", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithWireContracts_SubstitutesResolvedDtoInsidePromiseForAsyncReturn()
    {
        string dts = EmitFixtureDtsWithWireContracts();
        Assert.Contains("export declare function GetWidgetAsync(name: string): Promise<WidgetDto>;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_HonorsJsonPropertyNameAndQuotesNonIdentifiers()
    {
        string dts = EmitFixtureDts(includeAll: true);
        Assert.Contains("  wire_name: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  \"display-name\": string;", dts, StringComparison.Ordinal);
        Assert.Contains("  \"\": string;", dts, StringComparison.Ordinal);
        Assert.DoesNotContain("ignoredAtWire", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithIncludeAllKeepsJsonIncludeNonPublicProperty()
    {
        string dts = EmitFixtureDts(includeAll: true);
        Assert.Contains("lastEditedBy", dts, StringComparison.Ordinal);
    }
}
