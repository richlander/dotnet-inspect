using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
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
    public void Emit_MapsByteArrayPropertiesToBase64StringsInDirectAndNestedDtos()
    {
        string dts = EmitFixtureDts();

        Assert.Contains(
            """
            export interface ByteEnvelopeDto {
              content: string;
              payload: BytePayloadDto;
            }
            """,
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface BytePayloadDto {
              content: string;
            }
            """,
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGeneratedJson_UsesBase64StringsForByteArrayProperties()
    {
        string json = JsonSerializer.Serialize(
            new ByteEnvelopeDto([0, 1], new BytePayloadDto([2, 3])),
            FixtureJsonContext.Default.ByteEnvelopeDto);

        Assert.Equal(
            """{"content":"AAE=","payload":{"content":"AgM="}}""",
            json);
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
    public void Emit_ProjectsStringConvertedEnumAsStringLiteralAndNumberUnion()
    {
        string dts = EmitFixtureDts();

        Assert.Contains(
            "export type WidgetStatus = \"Draft\" | \"Published\" | \"Archived\" | number;",
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
    public void Emit_ProjectsStringConvertedFlagsEnumAsStringAndNumber()
    {
        string dts = EmitFixtureDts();

        Assert.Contains(
            "export type WidgetPermission = string | number;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGeneratedJson_StringEnumConverterAllowsUndefinedNumericValues()
    {
        string enumJson = JsonSerializer.Serialize(
            new WidgetSummary("widget", (WidgetStatus)123),
            FixtureJsonContext.Default.WidgetSummary);
        string flagsJson = JsonSerializer.Serialize(
            new WidgetPermissionSummary("widget", (WidgetPermission)8),
            FixtureJsonContext.Default.WidgetPermissionSummary);

        Assert.Equal("""{"name":"widget","status":123}""", enumJson);
        Assert.Equal("""{"name":"widget","permissions":8}""", flagsJson);
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
    public void Emit_BlocksEnumWithUnsupportedContextOptions()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var enumType = new ApiType
        {
            Name = "Status",
            Kind = "enum",
            JsonPropertyNamingPolicy = JsonWireNamingPolicy.Unsupported,
        };
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Enums = [enumType],
        };

        string dts = DtsEmitter.Emit(surface, diagnostics);

        Assert.Contains(
            "export type Status = unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location
                    == "Status JsonSerializerContext options"
                && diagnostic.CSharpType
                    == "unsupported wire-shaping options");
    }

    [Fact]
    public void Emit_RefusesEmptyStringConvertedEnumBeforeOutput()
    {
        var enumType = new ApiType
        {
            Name = "Empty",
            Kind = "enum",
            HasJsonStringEnumConverter = true,
        };
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Enums = [enumType],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
    }

    [Fact]
    public void Emit_UsesEscapedDeduplicatedEnumWireNames()
    {
        using FileStream stream = File.OpenRead(
            typeof(NamedEnumFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType enumType = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(NamedEnumFixture));

        string dts = DtsEmitter.Emit(
            new ILInspector.JsExportSurface.JsExportSurface
            {
                Enums = [enumType],
            });

        Assert.Contains(
            "\"wire \\\"value\\\"\\n\\u2028\" | \"duplicate\"",
            dts,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            dts.Split("\"duplicate\"", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Emit_RefusesMalformedOrDuplicateEnumWireNames(
        bool malformed)
    {
        var enumType = new ApiType
        {
            Name = "Status",
            Kind = "enum",
            HasJsonStringEnumConverter = true,
            Members =
            [
                new ApiMember
                {
                    Name = "Ready",
                    Kind = "field",
                    IsConst = true,
                    JsonStringEnumMemberNameAttributeValues =
                        malformed ? [null] : ["ready", "also-ready"],
                },
            ],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(
                new ILInspector.JsExportSurface.JsExportSurface
                {
                    Enums = [enumType],
                }));
    }

    [Fact]
    public void Emit_BlocksUnsupportedTypeAndMemberConverters()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var blocked = new ApiType
        {
            Name = "Blocked",
            JsonConverterAttributeCount = 1,
        };
        var memberConverted = new ApiType
        {
            Name = "MemberConverted",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    HasGetter = true,
                    ReturnType = "int",
                    JsonConverterAttributeCount = 1,
                },
                new ApiMember
                {
                    Name = "Ignored",
                    Kind = "property",
                    HasGetter = true,
                    JsonIgnoreConditions =
                        [JsonWireIgnoreCondition.Always],
                    ReturnType = "int",
                    JsonConverterAttributeCount = 1,
                },
            ],
        };

        string dts = DtsEmitter.Emit(
            new ILInspector.JsExportSurface.JsExportSurface
            {
                Records = [blocked, memberConverted],
            },
            diagnostics);

        Assert.Contains(
            "export type Blocked = unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains("Value: unknown;", dts, StringComparison.Ordinal);
        Assert.DoesNotContain("Ignored", dts, StringComparison.Ordinal);
        Assert.Equal(2, diagnostics.UnmappedTypes.Count);
    }

    [Fact]
    public void Emit_AllowsExactlyOneSupportedStringEnumConverter()
    {
        var enumType = new ApiType
        {
            Name = "Status",
            Kind = "enum",
            HasJsonStringEnumConverter = true,
            JsonConverterAttributeCount = 1,
            Members =
            [
                new ApiMember
                {
                    Name = "Ready",
                    Kind = "field",
                    IsConst = true,
                },
            ],
        };

        string dts = DtsEmitter.Emit(
            new ILInspector.JsExportSurface.JsExportSurface
            {
                Enums = [enumType],
            });

        Assert.Contains(
            "export type Status = \"Ready\" | number;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_BlocksDuplicateStringEnumConverters()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var enumType = new ApiType
        {
            Name = "Status",
            Kind = "enum",
            HasJsonStringEnumConverter = true,
            JsonConverterAttributeCount = 2,
            Members =
            [
                new ApiMember
                {
                    Name = "Ready",
                    Kind = "field",
                    IsConst = true,
                },
            ],
        };

        string dts = DtsEmitter.Emit(
            new ILInspector.JsExportSurface.JsExportSurface
            {
                Enums = [enumType],
            },
            diagnostics);

        Assert.Contains(
            "export type Status = unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_BlocksMismatchedStringEnumConverter()
    {
        using FileStream stream = File.OpenRead(
            typeof(MismatchedStringEnumConverterFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType enumType = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(MismatchedStringEnumConverterFixture));
        var diagnostics = new TsBindGenDiagnostics();

        string dts = DtsEmitter.Emit(
            new ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = apiSurface.AssemblyIdentity,
                Enums = [enumType],
            },
            diagnostics);

        Assert.Contains(
            "export type MismatchedStringEnumConverterFixture = unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_ConverterControlledTypeIgnoresResolvedMemberNames()
    {
        var blocked = new ApiType
        {
            Name = "Blocked",
            JsonConverterAttributeCount = 1,
            Members =
            [
                new ApiMember
                {
                    Name = "First",
                    Kind = "property",
                    HasGetter = true,
                    JsonPropertyName = "same\nname",
                    JsonPropertyNameAttributeValues = ["same\nname"],
                },
                new ApiMember
                {
                    Name = "Second",
                    Kind = "property",
                    HasGetter = true,
                    JsonPropertyName = "same\nname",
                    JsonPropertyNameAttributeValues = ["same\nname"],
                },
            ],
        };

        string dts = DtsEmitter.Emit(
            new ILInspector.JsExportSurface.JsExportSurface
            {
                Records = [blocked],
            });

        Assert.Contains(
            "export type Blocked = unknown;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ConverterControlledTypeStillRejectsMalformedNameRows()
    {
        var blocked = new ApiType
        {
            Name = "Blocked",
            JsonConverterAttributeCount = 1,
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    HasGetter = true,
                    JsonPropertyNameAttributeValues = [null],
                },
            ],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(
                new ILInspector.JsExportSurface.JsExportSurface
                {
                    Records = [blocked],
                }));
    }

    [Fact]
    public void Emit_ExternalEnvelopeCannotAliasLocalQualifiedType()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var localAssembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: "0011223344556677");
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            AssemblyIdentity = localAssembly,
            Records =
            [
                new ApiType
                {
                    Namespace = "Mine",
                    Name = "Result",
                },
            ],
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "GetResult",
                    ReturnType = "string",
                    ReturnWireType = "Mine.Result",
                    ReturnWireTypeReferences =
                    [
                        new(
                            new ApiAssemblyIdentity(
                                "Local",
                                new Version(1, 0, 0, 0),
                                culture: null,
                                publicKeyToken:
                                    "8899aabbccddeeff"),
                            "Mine.Result"),
                    ],
                },
            ],
        };

        string dts = DtsEmitter.Emit(surface, diagnostics);

        Assert.Contains(
            "export declare function getResult(): unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_ExternalSignatureTypesCannotAliasLocalQualifiedType()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var localAssembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: "0011223344556677");
        var externalAssembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: "8899aabbccddeeff");
        var externalResult = new ApiTypeReferenceIdentity(
            externalAssembly,
            "Mine.Result");
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            AssemblyIdentity = localAssembly,
            Records =
            [
                new ApiType
                {
                    Namespace = "Mine",
                    Name = "Result",
                },
            ],
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "Transform",
                    ReturnType = "Mine.Result",
                    ReturnTypeReferences = [externalResult],
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = "Value",
                            Type = "Mine.Result",
                            TypeReferences = [externalResult],
                        },
                    ],
                },
            ],
        };

        string dts = DtsEmitter.Emit(surface, diagnostics);

        Assert.Contains(
            "export declare function transform(value: unknown): unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Equal(2, diagnostics.UnmappedTypes.Count);
    }

    [Fact]
    public void Emit_NestedIdentityCannotAliasNamespaceQualifiedType()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var assembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: "0011223344556677");
        var topLevel = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "N.A",
                ImmutableArray.Create("B"))).Name;
        var nested = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "N",
                ImmutableArray.Create("A", "B"))).Name;
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            AssemblyIdentity = assembly,
            Records =
            [
                new ApiType
                {
                    Namespace = "N.A",
                    Name = "B",
                    DefinitionName = topLevel,
                },
            ],
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "GetB",
                    ReturnType = "N.A.B",
                    ReturnTypeReferences =
                    [
                        new ApiTypeReferenceIdentity(
                            assembly,
                            "N.A.B",
                            nested),
                    ],
                },
            ],
        };

        string dts = DtsEmitter.Emit(surface, diagnostics);

        Assert.Contains(
            "export declare function getB(): unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_DoesNotApplyDictionarySemanticsToLookalikeType()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var lookalikeDictionary = new ApiTypeReferenceIdentity(
            new ApiAssemblyIdentity(
                "System.Collections",
                new Version(1, 0, 0, 0),
                culture: null,
                publicKeyToken: null),
            "System.Collections.Generic.Dictionary`2");
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records =
            [
                new ApiType
                {
                    Name = "Root",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "Values",
                            Kind = "property",
                            HasGetter = true,
                            ReturnType =
                                "System.Collections.Generic.Dictionary<string, string>",
                            SignatureModel = new ApiSignature
                            {
                                ReturnType =
                                    "System.Collections.Generic.Dictionary<string, string>",
                                ReturnTypeReferences =
                                    [lookalikeDictionary],
                            },
                        },
                    ],
                },
            ],
        };

        string dts = DtsEmitter.Emit(surface, diagnostics);

        Assert.Contains("  Values: unknown;", dts, StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_DoesNotApplyTaskSemanticsToLookalikeType()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "GetValue",
                    ReturnType =
                        "System.Threading.Tasks.Task<string>",
                    ReturnTypeReferences =
                    [
                        new(
                            new ApiAssemblyIdentity(
                                "System.Private.CoreLib",
                                new Version(1, 0, 0, 0),
                                culture: null,
                                publicKeyToken: null),
                            "System.Threading.Tasks.Task`1"),
                    ],
                },
            ],
        };

        string dts = DtsEmitter.Emit(surface, diagnostics);

        Assert.Contains(
            "export declare function getValue(): unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_DoesNotTrustClaimedPlatformTokenFromWrongAssembly()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "GetValue",
                    ReturnType =
                        "System.Threading.Tasks.Task<string>",
                    ReturnTypeReferences =
                    [
                        new(
                            new ApiAssemblyIdentity(
                                "Lookalikes",
                                new Version(1, 0, 0, 0),
                                culture: null,
                                publicKeyToken:
                                    "b03f5f7f11d50a3a"),
                            "System.Threading.Tasks.Task`1"),
                    ],
                },
            ],
        };

        string dts = DtsEmitter.Emit(surface, diagnostics);

        Assert.Contains(
            "export declare function getValue(): unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
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
            $"member 0x{member.DeclarationMetadataToken:X8} "
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
    [InlineData("unknown")]
    [InlineData("string")]
    [InlineData("Promise")]
    [InlineData("Record")]
    public void Emit_RefusesForbiddenTypeDeclarationNames(string name)
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
    [InlineData("A・B")]
    [InlineData("A･B")]
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
    public void Emit_RefusesUnicodePatternSyntaxAsIdentifierStart()
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [new ApiType { Name = "ⸯValue" }],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
    }

    [Theory]
    [InlineData("\u1C89Value")]
    [InlineData("A\u0897Value")]
    public void Emit_RefusesIdentifiersNewerThanPinnedTypeScriptUnicode(
        string name)
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [new ApiType { Name = name }],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
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
                    DeclarationMetadataToken = 0x17000001,
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

    [Theory]
    [InlineData("Package", "package")]
    [InlineData("Static", "static")]
    public void Emit_DoesNotQuoteReservedWordsUsedAsPropertyKeys(
        string memberName,
        string expectedKey)
    {
        var record = new ApiType
        {
            Name = "Dto",
            JsonPropertyNamingPolicy = JsonWireNamingPolicy.CamelCase,
            Members =
            [
                new ApiMember
                {
                    Name = memberName,
                    Kind = "property",
                    ReturnType = "string",
                },
            ],
        };
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.Contains($"  {expectedKey}: string;", dts, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"{expectedKey}\"", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_IncludesPropertyWithJsonIgnoreNever()
    {
        using FileStream stream = File.OpenRead(
            typeof(JsonIgnoreNeverFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name == nameof(JsonIgnoreNeverFixture));
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.Contains("  Included: string;", dts, StringComparison.Ordinal);
        Assert.DoesNotContain("Excluded", dts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Class", "Value", "Ns.Exports")]
    [InlineData("Go", "Class", "Ns.Exports")]
    [InlineData("Go", "Value", "Ns.Bad\nExports")]
    [InlineData("Go\u001b[31m", "Value", "Ns.Exports")]
    public void Emit_RefusesInvalidJsExportIdentifiers(
        string functionName,
        string parameterName,
        string declaringType)
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = declaringType,
                    Name = functionName,
                    ReturnType = "void",
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = parameterName,
                            Type = "string",
                        },
                    ],
                },
            ],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.DoesNotContain(functionName, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(parameterName, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(declaringType, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RefusesJsExportNameCollisions()
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Ns.Exports",
                    Name = "Get",
                    ReturnType = "void",
                },
                new JsExportFunction
                {
                    DeclaringType = "Ns.Exports",
                    Name = "get",
                    ReturnType = "void",
                },
            ],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
    }

    [Fact]
    public void Emit_RefusesJsExportParameterNameCollisions()
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Ns.Exports",
                    Name = "Get",
                    ReturnType = "void",
                    Parameters =
                    [
                        new ApiParameter { Name = "Value", Type = "string" },
                        new ApiParameter { Name = "value", Type = "string" },
                    ],
                },
            ],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
    }

    [Theory]
    [InlineData("Eval", "Value")]
    [InlineData("Go", "Arguments")]
    public void Emit_RefusesStrictModeJsBindings(
        string functionName,
        string parameterName)
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Ns.Exports",
                    Name = functionName,
                    ReturnType = "void",
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = parameterName,
                            Type = "string",
                        },
                    ],
                },
            ],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
    }

    [Theory]
    [InlineData("Dotnet", "Other")]
    [InlineData("Foo", "FooExport")]
    public void Emit_RefusesGeneratedModuleBindingCollisions(
        string firstName,
        string secondName)
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Ns.Exports",
                    Name = firstName,
                    ReturnType = "void",
                },
                new JsExportFunction
                {
                    DeclaringType = "Ns.Exports",
                    Name = secondName,
                    ReturnType = "void",
                },
            ],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
    }

    [Fact]
    public void Emit_RefusesJsonResultLocalParameterCollision()
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Ns.Exports",
                    Name = "Get",
                    ReturnType = "string",
                    ReturnWireType = "Widget",
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = "Result",
                            Type = "string",
                        },
                    ],
                },
            ],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
    }

    [Fact]
    public void Emit_RefusesParameterThatShadowsItsExportSlot()
    {
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Ns.Exports",
                    Name = "Foo",
                    ReturnType = "string",
                    ReturnWireType = "Widget",
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = "FooExport",
                            Type = "string",
                        },
                    ],
                },
            ],
        };

        Assert.Throws<UnsupportedWireContractException>(
            () => DtsEmitter.Emit(surface));
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
            $"member 0x{member.DeclarationMetadataToken:X8} [JsonPropertyName]: "
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
        Assert.DoesNotContain("IncludedPrivateGetter", dts, StringComparison.Ordinal);
        Assert.Contains("  IncludedInternalGetter: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  PublicGetter: string;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGeneratedJson_OmitsInaccessibleJsonIncludedMembers()
    {
        var value = new SourceGeneratedJsonIncludeAccessibilityFixture
        {
            IncludedPrivateGetter = "private-getter",
            IncludedInternalGetter = "internal-getter",
        };

        string json = JsonSerializer.Serialize(
            value,
            ControlPropertyNameFixtureJsonContext.Default
                .SourceGeneratedJsonIncludeAccessibilityFixture);

        Assert.DoesNotContain("IncludedPrivateGetter", json, StringComparison.Ordinal);
        Assert.DoesNotContain("IncludedPrivateField", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"IncludedInternalGetter\":\"internal-getter\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"IncludedInternalField\":\"internal-field\"",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_MatchesSourceGeneratedJsonIncludeAccessibility()
    {
        using FileStream stream = File.OpenRead(
            typeof(SourceGeneratedJsonIncludeAccessibilityFixture)
                .Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(SourceGeneratedJsonIncludeAccessibilityFixture));
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.DoesNotContain("IncludedPrivateGetter", dts, StringComparison.Ordinal);
        Assert.DoesNotContain("IncludedPrivateField", dts, StringComparison.Ordinal);
        Assert.Contains("  IncludedInternalGetter: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  IncludedInternalField: string;", dts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(InheritedWireDerivedFixture))]
    [InlineData(nameof(NumberHandlingWireFixture))]
    [InlineData(nameof(TypeNumberHandlingWireFixture))]
    [InlineData(nameof(PolymorphicWireFixture))]
    [InlineData(nameof(ExtensionDataWireFixture))]
    public void Emit_BlocksUnsupportedWireShapingContracts(
        string typeName)
    {
        using FileStream stream = File.OpenRead(
            typeof(InheritedWireDerivedFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType record = Assert.Single(
            apiSurface.Types,
            type => type.Name == typeName);
        var diagnostics = new TsBindGenDiagnostics();

        string dts = DtsEmitter.Emit(
            new ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = apiSurface.AssemblyIdentity,
                Records = [record],
            },
            diagnostics);

        Assert.Contains(
            $"export type {typeName} = unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
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
                == "ConflictingPolicyWidget JsonSerializerContext options"
            && diagnostic.CSharpType
                == "unsupported wire-shaping options");
    }

    [Fact]
    public void Emit_PreservesWhenReadingMemberInSerializeOnlyDeclaration()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            """
            export interface DirectionalOutputDto {
              name: string;
              serverNote: DirectionalNote | null;
              alwaysPresent: string;
            }
            """,
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_PreservesWhenWritingMemberInDeserializeOnlyDeclaration()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            """
            export interface DirectionalInputDto {
              name: string;
              clientSecret: string;
            }
            """,
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_BlocksBidirectionalTypeWithDirectionSensitiveMember()
    {
        var diagnostics = new TsBindGenDiagnostics();
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(path);

        string dts = DtsEmitter.Emit(
            JsExportSurfaceBuilder.Build(apiSurface, bodyIndex),
            diagnostics);

        Assert.Contains(
            "export type DirectionalRoundTripDto = unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location
                    == "DirectionalRoundTripDto JSON wire shape"
                && diagnostic.CSharpType
                    == "direction-sensitive [JsonIgnore] on a bidirectional type");
    }

    /// <summary>
    /// Without body evidence no direction can be attributed, so every type is
    /// conservatively treated as bidirectional and a direction-sensitive shape
    /// is blocked rather than guessed.
    /// </summary>
    [Fact]
    public void Emit_BlocksDirectionSensitiveTypeWithoutBodyEvidence()
    {
        string dts = EmitFixtureDts();

        Assert.Contains(
            "export type DirectionalOutputDto = unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export type DirectionalInputDto = unknown;",
            dts,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Discovery walks the direction-independent member union, so a type
    /// reachable only through a <c>WhenReading</c>-ignored member is still
    /// declared and the directional declaration that references it resolves.
    /// </summary>
    [Fact]
    public void Emit_DoesNotOrphanTypesReachedOnlyThroughDirectionalMembers()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            """
            export interface DirectionalNote {
              text: string;
            }
            """,
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "serverNote: DirectionalNote | null;",
            dts,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Emit_RefusesMalformedOrDuplicateJsonIgnoreRows(
        bool malformed)
    {
        var record = new ApiType
        {
            Name = "Widget",
            MetadataToken = 0x02000004,
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    HasGetter = true,
                    ReturnType = "int",
                    MetadataToken = 0x17000005,
                    JsonIgnoreConditions = malformed
                        ? [null]
                        : [
                            JsonWireIgnoreCondition.Never,
                            JsonWireIgnoreCondition.Never,
                        ],
                },
            ],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(
                    new ILInspector.JsExportSurface.JsExportSurface
                    {
                        Records = [record],
                    }));

        Assert.Contains(
            "member 0x17000005",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            malformed
                ? "[JsonIgnore] metadata could not be decoded"
                : "multiple [JsonIgnore] attributes",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RefusesMalformedJsonIncludeRows()
    {
        var record = new ApiType
        {
            Name = "Widget",
            MetadataToken = 0x02000004,
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    HasGetter = true,
                    ReturnType = "int",
                    MetadataToken = 0x17000006,
                    HasMalformedJsonInclude = true,
                },
            ],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(
                    new ILInspector.JsExportSurface.JsExportSurface
                    {
                        Records = [record],
                    }));

        Assert.Contains(
            "member 0x17000006",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "[JsonInclude] metadata could not be decoded",
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Malformed authentic <c>[JsonIgnore]</c> metadata in a real compiled
    /// assembly stops generation instead of producing a success-shaped
    /// declaration, and reports only a metadata token.
    /// </summary>
    [Fact]
    public void Emit_StopsGenerationForPatchedMalformedJsonIgnoreAttribute()
    {
        byte[] image = PatchJsonIgnoreCondition(
            nameof(DirectionalOutputDto),
            nameof(DirectionalOutputDto.DefaultHidden),
            outOfRangeValue: 9);

        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: false);
        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface);

        ApiMember patched = Assert.Single(
            Assert.Single(
                surface.Records,
                record => record.Name == nameof(DirectionalOutputDto))
                .Members,
            member => member.Name
                == nameof(DirectionalOutputDto.DefaultHidden));
        Assert.Equal([null], patched.JsonIgnoreConditions);

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(surface));

        Assert.Contains(
            "[JsonIgnore] metadata could not be decoded",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "member 0x",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(DirectionalOutputDto.DefaultHidden),
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Rewrites the <c>Condition</c> value of one compiled
    /// <c>[JsonIgnore]</c> attribute in place. The blob is located through
    /// metadata and required to occur exactly once in the image, so a shared or
    /// relocated blob fails loudly instead of patching the wrong row.
    /// </summary>
    static byte[] PatchJsonIgnoreCondition(
        string typeName,
        string propertyName,
        int outOfRangeValue)
    {
        byte[] image = File.ReadAllBytes(
            typeof(FixtureExports).Assembly.Location);
        byte[] blob;
        using (var stream = new MemoryStream(image, writable: false))
        using (var peReader = new PEReader(stream))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            TypeDefinition type = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(candidate =>
                    reader.GetString(candidate.Name) == typeName);
            PropertyDefinition property = type.GetProperties()
                .Select(reader.GetPropertyDefinition)
                .Single(candidate =>
                    reader.GetString(candidate.Name) == propertyName);
            CustomAttribute attribute = property.GetCustomAttributes()
                .Select(reader.GetCustomAttribute)
                .Single(candidate =>
                    candidate.Constructor.Kind == HandleKind.MemberReference
                    && reader.GetTypeReference(
                        (TypeReferenceHandle)reader.GetMemberReference(
                            (MemberReferenceHandle)candidate.Constructor)
                            .Parent) is var typeReference
                    && reader.GetString(typeReference.Name)
                        == "JsonIgnoreAttribute");
            blob = reader.GetBlobBytes(attribute.Value);
        }

        int offset = image.AsSpan().IndexOf(blob);
        Assert.NotEqual(-1, offset);
        Assert.Equal(
            -1,
            image.AsSpan(offset + 1).IndexOf(blob));

        BitConverter.GetBytes(outOfRangeValue)
            .CopyTo(image.AsSpan(offset + blob.Length - 4));
        return image;
    }
}
