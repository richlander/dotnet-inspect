using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
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

    private static string EmitFixtureDtsWithWireContracts()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(path);
        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface, bodyIndex);
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

    [Fact]
    public void Emit_WithoutWireContracts_ReportsErasedEnvelopeTypesRaw()
    {
        // Without a LibraryBodyIndex, GetWidget/GetWidgetAsync's declared string/Task<string>
        // envelope is all the emitter can see, so it reports it as-is: correct, but not the DTO
        // callers actually observe. This is the exact gap JsonWireContractResolver closes below.
        string dts = EmitFixtureDts();

        Assert.Contains(
            "export declare function getWidget(name: string, count: number): string;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function getWidgetAsync(name: string): Promise<string>;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithWireContracts_SubstitutesResolvedDtoForSyncStringReturn()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            "export declare function getWidget(name: string, count: number): WidgetDto;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithWireContracts_SubstitutesResolvedDtoInsidePromiseForAsyncReturn()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            "export declare function getWidgetAsync(name: string): Promise<WidgetDto>;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithWireContracts_LeavesNonEnvelopeReturnUnchanged()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            "export declare function ping(): Promise<void>;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithWireContracts_DoesNotGuessParameterAttributionWithMultipleStringParams()
    {
        // RenameWidget(widgetJson, newName) has two string parameters but only one resolved
        // Deserialize DTO with no positional attribution (documented residual gap in
        // JsonWireContractResolver). The emitter must not guess which parameter it applies to;
        // both stay mapped from their raw signature text.
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            "export declare function renameWidget(widgetJson: string, newName: string): WidgetDto;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithWireContracts_SubstitutesArrayDtoForContainerShapedReturn()
    {
        // GetWidgetArray's resolved wire type is "WidgetDto[]" (a container shape, not a plain
        // record name) — proves the resolver's ToDisplayString() fix reaches all the way through
        // to the emitted .d.ts, not just the resolver's own unit-level assertion.
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            "export declare function getWidgetArray(): WidgetDto[];",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithWireContracts_LeavesAmbiguousReturnAsRawEnvelope()
    {
        // GetWidgetOrOwner serializes two distinct DTOs on different branches, so
        // JsonWireContractResolver leaves ReturnWireType unset; the emitter must fall back to the
        // raw (erased) signature type rather than guessing one of the two DTOs.
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            "export declare function getWidgetOrOwner(wantOwner: boolean): string;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ProjectsEnumAsStringLiteralUnionNotEmptyInterface()
    {
        // WidgetStatus is backed by JsonStringEnumConverter (serialized as its member name, a
        // string) — must render as a TS string-literal union, not export interface WidgetStatus {}
        // (which is what treating it as a zero-property record would produce).
        string dts = EmitFixtureDts();

        Assert.Contains(
            "export type WidgetStatus = \"Draft\" | \"Published\" | \"Archived\";",
            dts,
            StringComparison.Ordinal);
        Assert.DoesNotContain("export interface WidgetStatus", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithIncludeAllExcludesCompilerSynthesizedNonPublicRecordMembers()
    {
        // A positional record's compiler-synthesized EqualityContract getter is never public;
        // includeAll: true surfaces it in ApiType.Members alongside the record's real, public
        // data properties, but it must never reach .d.ts output as a fake "equalityContract"
        // property on every record.
        using FileStream stream = File.OpenRead(typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);
        string dts = DtsEmitter.Emit(surface);

        Assert.DoesNotContain("equalityContract", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ProjectsFlagsEnumAsStringNotClosedUnion()
    {
        // WidgetPermission is [Flags] and backed by JsonStringEnumConverter; STJ serializes a
        // combination as a comma-joined string of names (e.g. "Read, Write"), which a closed
        // single-member union cannot represent — so the emitter must fall back to `string`.
        string dts = EmitFixtureDts();

        Assert.Contains(
            "export type WidgetPermission = string;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ProjectsConverterlessEnumAsNumberNotStringUnion()
    {
        // WidgetPriority carries no JsonConverter, so STJ serializes it by its numeric underlying
        // value, not by declared name — the emitter must not assume every enum is a string union.
        string dts = EmitFixtureDts();

        Assert.Contains(
            "export type WidgetPriority = number;",
            dts,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"Low\" | \"Medium\" | \"High\"", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithIncludeAllKeepsJsonIncludeNonPublicProperty()
    {
        // WidgetAudit.LastEditedBy is internal but carries [JsonInclude], explicitly opting it
        // into the wire contract — it must not be excluded by the same filter that drops a
        // record's compiler-synthesized EqualityContract getter, even though both are non-public.
        using FileStream stream = File.OpenRead(typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);
        string dts = DtsEmitter.Emit(surface);

        Assert.Contains("lastEditedBy", dts, StringComparison.Ordinal);
    }
}
