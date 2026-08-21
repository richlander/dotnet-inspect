using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.Metadata;
using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

public sealed class DtsEmitterTests
{
    private static string EmitFixtureDts(
        bool includeAll = false,
        TsBindGenDiagnostics? diagnostics = null)
    {
        using FileStream stream = File.OpenRead(typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: includeAll);
        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);
        return DtsEmitter.Emit(surface, diagnostics);
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
    public void Emit_ProducesInterfacesForBothRecords()
    {
        string dts = EmitFixtureDts();

        Assert.Contains("export interface WidgetDto {", dts, StringComparison.Ordinal);
        Assert.Contains("export interface WidgetOwner {", dts, StringComparison.Ordinal);
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
    public void Emit_PreservesPascalCaseForNoPolicyContextProperties()
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
    public void Emit_DeclaresWrapperFunctionsWithCamelCaseNamesAndPromiseReturnTypes()
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
        Assert.Contains(
            "export declare function queryPackage(packageId: string): string;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function initializeEngine(onStatus?: (status: string) => void): Promise<unknown>;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithoutWireContracts_ReportsErasedEnvelopeTypesRaw()
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
    public void JsEmitter_UsesTheDeclarationEmittersReturnShapeClassification()
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new()
                {
                    DeclaringType = "FixtureExports",
                    Name = "GetTaskInfo",
                    ReturnType = "TaskInfo",
                },
                new()
                {
                    DeclaringType = "FixtureExports",
                    Name = "Ping",
                    ReturnType = "void",
                    ReturnWireType = "WidgetDto",
                },
                new()
                {
                    DeclaringType = "FixtureExports",
                    Name = "QueryWidget",
                    ReturnType = "System.Threading.Tasks.Task<string>",
                    ReturnWireType = "WidgetDto",
                },
            ],
        };

        string js = JsEmitter.Emit(surface);

        Assert.Contains(
            "export function getTaskInfo()",
            js,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function ping()",
            js,
            StringComparison.Ordinal);
        Assert.Contains(
            "return pingExport();",
            js,
            StringComparison.Ordinal);
        Assert.Contains(
            "export async function queryWidget()",
            js,
            StringComparison.Ordinal);
        Assert.Contains(
            "const result = await queryWidgetExport();\n  return JSON.parse(result);",
            js,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithWireContracts_DoesNotGuessParameterAttributionWithMultipleStringParams()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            "export declare function renameWidget(widgetJson: string, newName: string): WidgetDto;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithWireContracts_SubstitutesArrayDtoForContainerShapedReturn()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            "export declare function getWidgetArray(): WidgetDto[];",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_WithWireContracts_LeavesAmbiguousReturnAsRawEnvelope()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            "export declare function getWidgetOrOwner(wantOwner: boolean): string;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ProjectsEnumAsStringLiteralUnionNotEmptyInterface()
    {
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
        string dts = EmitFixtureDts(includeAll: true);

        Assert.DoesNotContain("equalityContract", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ProjectsFlagsEnumAsStringNotClosedUnion()
    {
        string dts = EmitFixtureDts();

        Assert.Contains(
            "export type WidgetPermission = string;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ProjectsConverterlessEnumAsNumberNotStringUnion()
    {
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
        string dts = EmitFixtureDts(includeAll: true);

        Assert.Contains("lastEditedBy", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_HonorsJsonPropertyNameAndQuotesNonIdentifierNames()
    {
        string dts = EmitFixtureDts(includeAll: true);

        Assert.Contains("  wire_name: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  \"display-name\": string;", dts, StringComparison.Ordinal);
        Assert.Contains("  \"\": string;", dts, StringComparison.Ordinal);
        Assert.DoesNotContain("ignoredAtWire", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RefusesControlCharacterJsonPropertyNamesWithoutDeclarationOutput()
    {
        using FileStream stream = File.OpenRead(typeof(ControlPropertyNameFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(ControlPropertyNameFixture));
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        UnsupportedWireContractException exception = Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));

        Assert.Equal(
            "ControlPropertyNameFixture.Value [JsonPropertyName]: "
                + "control-character JSON property names are not supported.",
            exception.Message);
    }

    [Fact]
    public void Emit_RefusesControlCharacterJsonPropertyNamesOnAutoPropertyBackingFields()
    {
        using FileStream stream = File.OpenRead(
            typeof(BackingFieldControlPropertyNameFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(BackingFieldControlPropertyNameFixture));
        ApiMember property = Assert.Single(
            record.Members,
            member => member.Name == "Value");
        Assert.Null(property.JsonPropertyName);
        Assert.Equal(
            "backing\nbreak\r\t\u0001",
            property.BackingFieldJsonPropertyName);
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Equal(
            "BackingFieldControlPropertyNameFixture.Value "
                + "[field: JsonPropertyName]: "
                + "control-character JSON property names are not supported.",
            exception.Message);
    }

    [Fact]
    public void Emit_RefusesControlCharacterJsonPropertyNamesOnEnumFields()
    {
        using FileStream stream = File.OpenRead(
            typeof(ControlPropertyNameEnumFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType enumType = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(ControlPropertyNameEnumFixture));
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Enums = [enumType],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Equal(
            "ControlPropertyNameEnumFixture.Value [JsonPropertyName]: "
                + "control-character JSON property names are not supported.",
            exception.Message);
    }

    [Fact]
    public void Emit_DoesNotApplySafeBackingFieldNameToProperty()
    {
        using FileStream stream = File.OpenRead(
            typeof(SafeBackingFieldPropertyNameFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(SafeBackingFieldPropertyNameFixture));
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.Contains("  Value: string;", dts, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not_the_property_name",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RefusesControlCharacterNameReachedOnlyThroughJsonIncludedField()
    {
        var apiSurface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Name = "SurfaceJsonContext",
                    BaseType =
                        "System.Text.Json.Serialization.JsonSerializerContext",
                    JsonPropertyNamingPolicy = JsonWireNamingPolicy.CamelCase,
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "RootDto",
                            Kind = "property",
                            ReturnType =
                                "System.Text.Json.Serialization.Metadata."
                                + "JsonTypeInfo<RootDto>",
                        },
                    ],
                },
                new ApiType
                {
                    Name = "RootDto",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "Child",
                            Kind = "field",
                            ReturnType = "NestedDto",
                            HasJsonInclude = true,
                        },
                    ],
                },
                new ApiType
                {
                    Name = "NestedDto",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "Value",
                            Kind = "field",
                            ReturnType = "string",
                            HasJsonInclude = true,
                            JsonPropertyName = "nested\nbreak",
                        },
                    ],
                },
            ],
        };
        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);
        Assert.Equal(2, surface.Records.Count);
        Assert.All(
            surface.Records,
            record => Assert.Equal(
                JsonWireNamingPolicy.CamelCase,
                record.JsonPropertyNamingPolicy));

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Equal(
            "NestedDto.Value [JsonPropertyName]: "
                + "control-character JSON property names are not supported.",
            exception.Message);
    }

    [Fact]
    public void Emit_BlocksRecordWithConflictingContextNamingPolicies()
    {
        var diagnostics = new TsBindGenDiagnostics();

        string dts = EmitFixtureDts(diagnostics: diagnostics);

        Assert.Contains(
            "export type ConflictingPolicyWidget = unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location
                    == "ConflictingPolicyWidget JsonSerializerContext.PropertyNamingPolicy"
                && diagnostic.CSharpType == "unsupported JsonKnownNamingPolicy");
    }
}
