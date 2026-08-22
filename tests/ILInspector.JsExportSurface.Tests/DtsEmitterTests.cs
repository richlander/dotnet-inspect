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
        ApiMember member = Assert.Single(
            record.Members,
            candidate => candidate.Name == "Value");

        Assert.Equal(
            $"type 0x{record.MetadataToken:X8} member "
                + "[JsonPropertyName]: control-character JSON property names "
                + "are not supported.",
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
        FilteredJsonPropertyNameFact fact =
            Assert.Single(record.FilteredJsonPropertyNameFacts);
        Assert.Equal(
            FilteredJsonPropertyNameKind.AutoPropertyBackingField,
            fact.Kind);
        Assert.Equal("Value", fact.AssociatedMemberName);
        Assert.Equal(
            ["backing\nbreak\r\t\u0001"],
            fact.PropertyNames);
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Equal(
            $"field 0x{fact.MetadataToken:X8} [field: JsonPropertyName]: "
                + "control-character JSON property names are not supported.",
            exception.Message);
    }

    [Fact]
    public void Emit_RefusesControlCharacterJsonPropertyNamesOnFilteredEventFields()
    {
        using FileStream stream = File.OpenRead(
            typeof(FilteredEventControlPropertyNameFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(FilteredEventControlPropertyNameFixture));
        FilteredJsonPropertyNameFact fact =
            Assert.Single(record.FilteredJsonPropertyNameFacts);
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Equal(
            $"field 0x{fact.MetadataToken:X8} [field: JsonPropertyName]: "
                + "control-character JSON property names are not supported.",
            exception.Message);
    }

    [Fact]
    public void Emit_RefusesAnyUnsafeFilteredFieldFactWhenLocationsRepeat()
    {
        var record = new ApiType
        {
            Name = "DuplicateBackingFieldDto",
            FilteredJsonPropertyNameFacts =
            [
                new(
                    FilteredJsonPropertyNameKind.AutoPropertyBackingField,
                    "Value",
                    0x04000001,
                    ["unsafe\nname"]),
                new(
                    FilteredJsonPropertyNameKind.AutoPropertyBackingField,
                    "Value",
                    0x04000002,
                    ["safe"]),
            ],
        };
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Emit_RefusesDuplicateOrMalformedJsonPropertyNameAttributes(
        bool malformed,
        bool filtered)
    {
        List<string?> propertyNames = malformed
            ? [null]
            : ["safe", "unsafe\nname"];
        var record = new ApiType
        {
            Name = "InvalidAttributeDto",
        };
        if (filtered)
        {
            record.FilteredJsonPropertyNameFacts =
            [
                new(
                    FilteredJsonPropertyNameKind.CompilerNamedField,
                    AssociatedMemberName: null,
                    0x04000001,
                    propertyNames),
            ];
        }
        else
        {
            record.Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    JsonPropertyNameAttributeValues = propertyNames,
                },
            ];
        }
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Contains(
            "duplicate or malformed JsonPropertyName attributes are not supported",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe\nname", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RefusesNestedTypeDeclarationNames()
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records =
            [
                new ApiType
                {
                    Namespace = "Example",
                    Name = "Outer.Inner",
                },
            ],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Contains(
            "TypeScript declaration names must be identifiers",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RefusesDuplicateTypeDeclarationNames()
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records =
            [
                new ApiType { Namespace = "A", Name = "Widget" },
                new ApiType { Namespace = "B", Name = "Widget" },
            ],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Contains(
            "multiple JSON types project to the same TypeScript declaration name",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("await")]
    [InlineData("interface")]
    public void Emit_RefusesReservedTypeDeclarationNames(string name)
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [new ApiType { Name = name }],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
    }

    [Theory]
    [InlineData("Größe")]
    [InlineData("℘Value")]
    [InlineData("A·B")]
    public void Emit_AcceptsUnicodeTypeScriptIdentifiers(string name)
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [new ApiType { Name = name }],
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.Contains($"export interface {name} {{", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_DoesNotEchoRejectedTypeNames()
    {
        const string unsafeName = "Bad\u001b[2J\u0007Name";
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records =
            [
                new ApiType
                {
                    Name = unsafeName,
                    MetadataToken = 0x02000001,
                },
            ],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.StartsWith("type 0x02000001:", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(unsafeName, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', exception.Message);
        Assert.DoesNotContain('\u0007', exception.Message);
    }

    [Fact]
    public void Emit_RefusesControlCharactersInResolvedMemberNames()
    {
        const string unsafeName = "Value\n\u001b[31m";
        var record = new ApiType
        {
            Name = "Dto",
            Members =
            [
                new ApiMember
                {
                    Name = unsafeName,
                    Kind = "property",
                    ReturnType = "string",
                    MetadataToken = 0x17000001,
                },
            ],
        };
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.StartsWith("member 0x17000001:", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(unsafeName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RefusesDuplicateResolvedMemberNames()
    {
        var record = new ApiType
        {
            Name = "Dto",
            Members =
            [
                new ApiMember
                {
                    Name = "Name",
                    Kind = "property",
                    ReturnType = "string",
                },
                new ApiMember
                {
                    Name = "Other",
                    Kind = "property",
                    ReturnType = "string",
                    JsonPropertyName = "Name",
                },
            ],
        };
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Contains(
            "multiple members resolve to the same JSON property name",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_EscapesRenderingHazardsInQuotedPropertyNames()
    {
        var record = new ApiType
        {
            Name = "Dto",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    ReturnType = "string",
                    JsonPropertyName = "left\u202Eright",
                },
            ],
        };
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.Contains(
            "\"left\\u202Eright\": string;",
            dts,
            StringComparison.Ordinal);
        Assert.DoesNotContain('\u202E', dts);
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

        ApiMember member = Assert.Single(
            enumType.Members,
            candidate => candidate.Name == "Value");
        Assert.Equal(
            $"type 0x{enumType.MetadataToken:X8} member [JsonPropertyName]: "
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
    public void Emit_IncludesJsonIncludedFieldsInParentInterface()
    {
        var root = new ApiType
        {
            Name = "RootDto",
            JsonPropertyNamingPolicy = JsonWireNamingPolicy.CamelCase,
            Members =
            [
                new ApiMember
                {
                    Name = "Child",
                    Kind = "field",
                    ReturnType = "NestedDto",
                    HasJsonInclude = true,
                },
                new ApiMember
                {
                    Name = "StaticChild",
                    Kind = "property",
                    ReturnType = "NestedDto",
                    IsStatic = true,
                },
            ],
        };
        var nested = new ApiType { Name = "NestedDto" };
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [root, nested],
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.Contains("  child: NestedDto;", dts, StringComparison.Ordinal);
        Assert.DoesNotContain("StaticChild", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_IncludesCompiledJsonIncludedField()
    {
        using FileStream stream = File.OpenRead(
            typeof(JsonIncludedFieldRootFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType root = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(JsonIncludedFieldRootFixture));
        ApiType nested = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(JsonIncludedFieldNestedFixture));
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [root, nested],
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.Contains(
            "  Child: JsonIncludedFieldNestedFixture;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_UsesGetterAccessibilityForCompiledProperties()
    {
        using FileStream stream = File.OpenRead(
            typeof(GetterAccessibilityFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(GetterAccessibilityFixture));
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.DoesNotContain("SetterOnlyAtWire", dts, StringComparison.Ordinal);
        Assert.DoesNotContain("NoGetter", dts, StringComparison.Ordinal);
        Assert.Contains("  IncludedPrivateGetter: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  PublicGetter: string;", dts, StringComparison.Ordinal);
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

        Assert.Contains(
            "control-character JSON property names are not supported",
            exception.Message,
            StringComparison.Ordinal);
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
