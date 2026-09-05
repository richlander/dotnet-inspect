using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.JsExportSurface.PublishabilityFixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

public sealed class DtsEmitterTests
{
    private static string EmitFixtureDts(
        bool includeAll = false,
        TypeScriptGenerationDiagnostics? diagnostics = null)
    {
        using FileStream stream = File.OpenRead(typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: includeAll);
        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);
        return DtsEmitter.Emit(surface, diagnostics);
    }

    private static string EmitFixtureDtsWithWireContracts()
        => DtsEmitter.Emit(BuildFixtureSurfaceWithWireContracts());

    private static ILInspector.JsExportSurface.JsExportSurface
        BuildFixtureSurfaceWithWireContracts()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        return JsExportSurfaceBuilder.Build(apiSurface, bodyIndex);
    }

    [Fact]
    public void Emit_ProducesInterfacesForBothRecords()
    {
        string dts = EmitFixtureDts();

        Assert.Contains("export interface WidgetDto {", dts, StringComparison.Ordinal);
        Assert.Contains("export interface WidgetOwner {", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_UsesReadonlyPropertiesWithContextNamingPolicy()
    {
        string dts = EmitFixtureDts();

        Assert.Contains("  readonly name: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly count: number;", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly displayName: string;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_PreservesPascalCaseForNoPolicyContextProperties()
    {
        string dts = EmitFixtureDts(includeAll: true);

        Assert.Contains("export interface InternalContextPascalWidget {", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly Name: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly Count: number;", dts, StringComparison.Ordinal);
        Assert.Contains("export interface InternalContextCamelWidget {", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly name: string;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_MapsWireCollectionsToReadonlyTypes()
    {
        string dts = EmitFixtureDts();

        Assert.Contains(
            """
            export interface WidgetDto {
              readonly name: string;
              readonly count: number;
              readonly tags: ReadonlyArray<number>;
              readonly owner: WidgetOwner | null;
            }
            """,
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface WidgetCatalog {
              readonly ownersByKey: Readonly<Record<string, WidgetOwner>>;
            }
            """,
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_MapsByteArrayPropertiesToBase64StringsInDirectAndNestedDtos()
    {
        string dts = EmitFixtureDts();

        Assert.Contains(
            """
            export interface ByteEnvelopeDto {
              readonly content: string;
              readonly payload: BytePayloadDto;
            }
            """,
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface BytePayloadDto {
              readonly content: string;
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
    public void Emit_MapsDirectByteArrayExportAsInteropArray()
    {
        string dts = EmitFixtureDts();

        Assert.Contains(
            "export declare function echoBytes(value: number[]): number[];",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_MapsAuthenticatedSynchronousDelegatesToFunctionTypes()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            "export declare function reportValue("
                + "callback: (arg0: number) => undefined): void;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function reportNullableText("
                + "callback: (arg0: string | null) => undefined): void;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function transformValue("
                + "callback: (arg0: number, arg1: string) => boolean): "
                + "boolean;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function observeValues("
                + "callback: (arg0: number, arg1: string, "
                + "arg2: boolean) => undefined): void;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_MapsDelegateRecordFromContainingAssembly()
    {
        var assembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: "0011223344556677");
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            AssemblyIdentity = assembly,
            Records =
            [
                new ApiType
                {
                    Namespace = "Mine",
                    Name = "Payload",
                    Kind = "class",
                    DefinitionName =
                        DefinitionName("Mine", "Payload"),
                },
            ],
            Functions =
            [
                DelegateRecordFunction(assembly),
            ],
        };

        Assert.Contains(
            "export declare function register("
                + "callback: (arg0: Payload | null) => undefined): void;",
            DtsEmitter.Emit(surface),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RejectsDelegateRecordFromDifferentAssembly()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        var assembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: "0011223344556677");
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            AssemblyIdentity = assembly,
            Records =
            [
                new ApiType
                {
                    Namespace = "Mine",
                    Name = "Payload",
                    Kind = "class",
                    DefinitionName =
                        DefinitionName("Mine", "Payload"),
                },
            ],
            Functions =
            [
                DelegateRecordFunction(
                    new ApiAssemblyIdentity(
                        "Other",
                        new Version(1, 0, 0, 0),
                        culture: null,
                        publicKeyToken: null)),
            ],
        };

        Assert.Contains(
            "export declare function register(callback: unknown): void;",
            DtsEmitter.Emit(surface, diagnostics),
            StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location == "Register.Callback"
                && diagnostic.CSharpType
                    == "System.Action<Mine.Payload?>");
    }

    [Fact]
    public void Emit_RejectsDelegateRecordWithIncompleteAssemblyIdentity()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        var assembly = new ApiAssemblyIdentity(
            "Local",
            version: null,
            culture: null,
            publicKeyToken: null);
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            AssemblyIdentity = assembly,
            Records =
            [
                new ApiType
                {
                    Namespace = "Mine",
                    Name = "Payload",
                    Kind = "class",
                    DefinitionName =
                        DefinitionName("Mine", "Payload"),
                },
            ],
            Functions =
            [
                DelegateRecordFunction(assembly),
            ],
        };

        Assert.Contains(
            "export declare function register(callback: unknown): void;",
            DtsEmitter.Emit(surface, diagnostics),
            StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_RejectsNullableDelegateValueTypeWithoutWrapper()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        var assembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            AssemblyIdentity = assembly,
            Enums =
            [
                new ApiType
                {
                    Namespace = "Mine",
                    Name = "Payload",
                    Kind = "enum",
                    DefinitionName =
                        DefinitionName("Mine", "Payload"),
                },
            ],
            Functions =
            [
                DelegateRecordFunction(assembly),
            ],
        };

        Assert.Contains(
            "export declare function register(callback: unknown): void;",
            DtsEmitter.Emit(surface, diagnostics),
            StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location == "Register.Callback"
                && diagnostic.CSharpType
                    == "System.Action<Mine.Payload?>");
    }

    [Fact]
    public void Emit_MapsNullableDelegateValueTypeWithWrapper()
    {
        var assembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            AssemblyIdentity = assembly,
            Enums =
            [
                new ApiType
                {
                    Namespace = "Mine",
                    Name = "Payload",
                    Kind = "enum",
                    DefinitionName =
                        DefinitionName("Mine", "Payload"),
                },
            ],
            Functions =
            [
                DelegateRecordFunction(
                    assembly,
                    nullableValueType: true),
            ],
        };

        Assert.Contains(
            "export declare function register("
                + "callback: (arg0: Payload | null) => undefined): void;",
            DtsEmitter.Emit(surface),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RejectsDelegateRecordWithDifferentFullAssemblyIdentity()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        var assembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: "0011223344556677");
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            AssemblyIdentity = assembly,
            Records =
            [
                new ApiType
                {
                    Namespace = "Mine",
                    Name = "Payload",
                    Kind = "class",
                    DefinitionName =
                        DefinitionName("Mine", "Payload"),
                },
            ],
            Functions =
            [
                DelegateRecordFunction(
                    new ApiAssemblyIdentity(
                        assembly.Name,
                        new Version(2, 0, 0, 0),
                        culture: null,
                        publicKeyToken: "8899aabbccddeeff")),
            ],
        };

        Assert.Contains(
            "export declare function register(callback: unknown): void;",
            DtsEmitter.Emit(surface, diagnostics),
            StringComparison.Ordinal);
        Assert.NotEmpty(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_RejectsFlattenedLocalDefinitionCollision()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        var assembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            AssemblyIdentity = assembly,
            Records =
            [
                new ApiType
                {
                    Namespace = "A.B",
                    Name = "C",
                    Kind = "class",
                    DefinitionName =
                        DefinitionName("A.B", "C"),
                },
            ],
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "Register",
                    ReturnType = "void",
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = "Callback",
                            Type = "System.Action<A.B.C?>",
                        },
                    ],
                    DelegateParameters =
                    [
                        new JsExportDelegateParameter
                        {
                            ParameterIndex = 0,
                            Kind = JsExportDelegateKind.Action,
                            ParameterTypes =
                            [
                                ResolvedType(
                                    assembly,
                                    "A",
                                    "B+C",
                                    DefinitionName(
                                        "A",
                                        "B",
                                        "C")),
                            ],
                        },
                    ],
                },
            ],
        };

        Assert.Contains(
            "export declare function register(callback: unknown): void;",
            DtsEmitter.Emit(surface, diagnostics),
            StringComparison.Ordinal);
        Assert.NotEmpty(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_FrameworkDelegateFactsOverrideNestedLocalNameCollisions()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        var assembly = new ApiAssemblyIdentity(
            "Local",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        var coreAssembly = new ApiAssemblyIdentity(
            "System.Private.CoreLib",
            new Version(11, 0, 0, 0),
            culture: null,
            publicKeyToken: "7cec85d7bea7798e");
        var jsAssembly = new ApiAssemblyIdentity(
            "System.Runtime.InteropServices.JavaScript",
            new Version(11, 0, 0, 0),
            culture: null,
            publicKeyToken: "b03f5f7f11d50a3a");
        TypeRef localPayload = ResolvedType(
            assembly,
            "Mine",
            "Payload");
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            AssemblyIdentity = assembly,
            Records =
            [
                LocalClass("Mine", "Payload"),
                LocalClass("System", "Int32"),
                LocalClass(
                    "System.Runtime.InteropServices.JavaScript",
                    "JSObject"),
            ],
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "Register",
                    ReturnType = "void",
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = "Callback",
                            Type = "System.Action<"
                                + "System.Collections.Generic.Dictionary<"
                                + "string, Mine.Payload>, "
                                + "Int32?[], JSObject?[]>",
                            TypeReferences =
                            [
                                new ApiTypeReferenceIdentity(
                                    coreAssembly,
                                    "System.Int32",
                                    DefinitionName(
                                        "System",
                                        "Int32")),
                                new ApiTypeReferenceIdentity(
                                    jsAssembly,
                                    "System.Runtime.InteropServices."
                                        + "JavaScript.JSObject",
                                    DefinitionName(
                                        "System.Runtime.InteropServices."
                                            + "JavaScript",
                                        "JSObject")),
                            ],
                        },
                    ],
                    DelegateParameters =
                    [
                        new JsExportDelegateParameter
                        {
                            ParameterIndex = 0,
                            Kind = JsExportDelegateKind.Action,
                            ParameterTypes =
                            [
                                TypeRef.GenericInstance(
                                    TypeRef.Definition(
                                        "System.Collections",
                                        "System.Collections.Generic",
                                        "Dictionary`2"),
                                    [
                                        TypeRef.CoreLib(
                                            "System",
                                            "String"),
                                        localPayload,
                                    ]),
                                TypeRef.SzArray(
                                    TypeRef.GenericInstance(
                                        TypeRef.CoreLib(
                                            "System",
                                            "Nullable`1"),
                                        [
                                            TypeRef.CoreLib(
                                                "System",
                                                "Int32"),
                                        ])),
                                TypeRef.SzArray(
                                    TypeRef.Definition(
                                        "System.Runtime.InteropServices."
                                            + "JavaScript",
                                        "System.Runtime.InteropServices."
                                            + "JavaScript",
                                        "JSObject")),
                            ],
                        },
                    ],
                },
            ],
        };

        Assert.Contains(
            "export declare function register(callback: "
                + "(arg0: Record<string, Payload>, "
                + "arg1: (number | null)[], "
                + "arg2: (unknown | null)[]) => undefined): void;",
            DtsEmitter.Emit(surface, diagnostics),
            StringComparison.Ordinal);
        Assert.Empty(diagnostics.UnmappedTypes);

        ApiType LocalClass(string @namespace, string name) =>
            new()
            {
                Namespace = @namespace,
                Name = name,
                Kind = "class",
                DefinitionName =
                    DefinitionName(@namespace, name),
            };
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void Emit_RejectsInvalidDelegateParameterAssociations(
        int parameterIndex,
        bool duplicate)
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        JsExportDelegateParameter fact = new()
        {
            ParameterIndex = parameterIndex,
            Kind = JsExportDelegateKind.Action,
        };
        var facts = new List<JsExportDelegateParameter> { fact };
        if (duplicate)
            facts.Add(fact);

        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "Ping",
                    ReturnType = "void",
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = "Value",
                            Type = "int",
                        },
                    ],
                    DelegateParameters = facts,
                },
            ],
        };

        Assert.Contains(
            "export declare function ping(value: unknown): void;",
            DtsEmitter.Emit(surface, diagnostics),
            StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location == "Ping delegate parameters");
    }

    [Fact]
    public void Emit_LeavesDirectInteropArraysMutable()
    {
        string dts = EmitFixtureDts();

        Assert.Contains(
            "export declare function echoBytes(value: number[]): number[];",
            dts,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "echoBytes(value: ReadonlyArray<number>)",
            dts,
            StringComparison.Ordinal);
    }

    static JsExportFunction DelegateRecordFunction(
        ApiAssemblyIdentity assembly,
        bool nullableValueType = false) =>
        new()
        {
            DeclaringType = "Exports",
            Name = "Register",
            ReturnType = "void",
            Parameters =
            [
                new ApiParameter
                {
                    Name = "Callback",
                    Type = "System.Action<Mine.Payload?>",
                },
            ],
            DelegateParameters =
            [
                new JsExportDelegateParameter
                {
                    ParameterIndex = 0,
                    Kind = JsExportDelegateKind.Action,
                    ParameterTypes =
                    [
                        nullableValueType
                            ? TypeRef.GenericInstance(
                                TypeRef.CoreLib(
                                    "System",
                                    "Nullable`1"),
                                [
                                    ResolvedType(
                                        assembly,
                                        "Mine",
                                        "Payload"),
                                ])
                            : ResolvedType(
                                assembly,
                                "Mine",
                                "Payload"),
                    ],
                },
            ],
        };

    static TypeRef ResolvedType(
        ApiAssemblyIdentity assembly,
        string @namespace,
        string name,
        MetadataTypeDefinitionName? definitionName = null)
    {
        var identity = new AssemblyReferenceIdentity(
            assembly.Name,
            assembly.Version,
            assembly.Culture,
            assembly.PublicKeyToken);
        definitionName ??= DefinitionName(@namespace, name);
        return TypeRef.Definition(
            assembly.Name,
            @namespace,
            name,
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(identity),
                definitionName));
    }

    static MetadataTypeDefinitionName DefinitionName(
        string @namespace,
        params string[] segments) =>
        ((MetadataTypeDefinitionNameResult.Valid)
            MetadataTypeDefinitionName.Create(
                @namespace,
                segments.ToImmutableArray())).Name;

    [Fact]
    public void Emit_MapsPrimitiveArrayAndClosedGenericWireRoots()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            "export declare function getRegisteredInt(): number;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function getRegisteredIntArray(): ReadonlyArray<number>;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function getRegisteredByteArray(): string;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function getRegisteredDecimal(): number;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function getRegisteredDecimalArray(): ReadonlyArray<number>;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function getClosedGenericRoot(): "
                + "Readonly<Record<string, ClosedGenericRootDto>>;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_PreservesSerializationOnlySourceGenerationDirection()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            """
            export interface ContextSerializationOnlyDto {
              readonly Name: string;
              readonly ServerNote: string;
            }
            """,
            dts,
            StringComparison.Ordinal);
        int declarationStart = dts.IndexOf(
            "export interface ContextSerializationOnlyDto {",
            StringComparison.Ordinal);
        int declarationEnd = dts.IndexOf(
            "}\n\n",
            declarationStart,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ClientSecret",
            dts[declarationStart..declarationEnd],
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("byte[]")]
    [InlineData("System.Byte[]")]
    public void Emit_RejectsUntrustedByteArrayAliasesInInteropAndJsonWire(
        string typeName)
    {
        var hostileAssembly = new ApiAssemblyIdentity(
            "Hostile",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        var hostileByte = new ApiTypeReferenceIdentity(
            hostileAssembly,
            "System.Byte");
        var diagnostics = new TypeScriptGenerationDiagnostics();
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records =
            [
                new ApiType
                {
                    Name = "Payload",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "Bytes",
                            Kind = "property",
                            SignatureModel = new ApiSignature
                            {
                                ReturnType = typeName,
                                ReturnTypeReferences = [hostileByte],
                            },
                        },
                    ],
                },
            ],
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "Echo",
                    ReturnType = typeName,
                    ReturnTypeReferences = [hostileByte],
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = "Value",
                            Type = typeName,
                            TypeReferences = [hostileByte],
                        },
                    ],
                },
            ],
        };

        string dts = DtsEmitter.Emit(surface, diagnostics);

        Assert.Contains("  readonly Bytes: unknown;", dts, StringComparison.Ordinal);
        Assert.Contains(
            "export declare function echo(value: unknown): unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic => diagnostic.CSharpType == typeName);
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
            "export declare function getWidgetArray(): ReadonlyArray<WidgetDto>;",
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Emit_RefusesMalformedOrDuplicateFlagsMetadata(bool malformed)
    {
        var enumType = new ApiType
        {
            Name = "Permission",
            Kind = "enum",
            MetadataToken = 0x02000007,
            HasJsonStringEnumConverter = true,
            IsFlagsEnum = !malformed,
            FlagsAttributeCount = malformed ? 0 : 2,
            HasMalformedFlagsAttribute = malformed,
            Members =
            [
                new ApiMember
                {
                    Name = "Read",
                    Kind = "field",
                    IsConst = true,
                },
            ],
        };

        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => DtsEmitter.Emit(
                    new ILInspector.JsExportSurface.JsExportSurface
                    {
                        Enums = [enumType],
                    }));

        Assert.Contains("type 0x02000007", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            malformed
                ? "[Flags] metadata could not be decoded"
                : "enums must not declare multiple [Flags] attributes",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_AllowsMalformedFlagsMetadataOnConverterlessEnum()
    {
        string dts = DtsEmitter.Emit(
            new ILInspector.JsExportSurface.JsExportSurface
            {
                Enums =
                [
                    new ApiType
                    {
                        Name = "Permission",
                        Kind = "enum",
                        HasMalformedFlagsAttribute = true,
                    },
                ],
            });

        Assert.Contains(
            "export type Permission = number;",
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
        var diagnostics = new TypeScriptGenerationDiagnostics();
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
        var diagnostics = new TypeScriptGenerationDiagnostics();
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
                    IndexParameterCount = 0,
                    JsonConverterAttributeCount = 1,
                },
                new ApiMember
                {
                    Name = "Ignored",
                    Kind = "property",
                    HasGetter = true,
                    IndexParameterCount = 0,
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
        var diagnostics = new TypeScriptGenerationDiagnostics();
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
        var diagnostics = new TypeScriptGenerationDiagnostics();

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
        var diagnostics = new TypeScriptGenerationDiagnostics();
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
        var diagnostics = new TypeScriptGenerationDiagnostics();
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
        var diagnostics = new TypeScriptGenerationDiagnostics();
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
        var diagnostics = new TypeScriptGenerationDiagnostics();
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
                                ReturnTypeShape =
                                    ApiTypeShape.GenericInstance(
                                        lookalikeDictionary,
                                        [
                                            ApiTypeShape.PrimitiveType(
                                                ApiPrimitiveType.String),
                                            ApiTypeShape.PrimitiveType(
                                                ApiPrimitiveType.String),
                                        ]),
                            },
                        },
                    ],
                },
            ],
        };

        string dts = DtsEmitter.Emit(surface, diagnostics);

        Assert.Contains("  readonly Values: unknown;", dts, StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_DoesNotApplyIntPtrSemanticsToLookalikeType()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "RegisterPointer",
                    ReturnType = "void",
                    Parameters =
                    [
                        new ApiParameter
                        {
                            Name = "Value",
                            Type = "nint",
                            TypeReferences =
                            [
                                new ApiTypeReferenceIdentity(
                                    new ApiAssemblyIdentity(
                                        "Lookalikes",
                                        new Version(1, 0, 0, 0),
                                        culture: null,
                                        publicKeyToken: null),
                                    "System.IntPtr",
                                    DefinitionName(
                                        "System",
                                        "IntPtr")),
                            ],
                        },
                    ],
                },
            ],
        };

        Assert.Contains(
            "export declare function registerPointer(value: unknown): void;",
            DtsEmitter.Emit(surface, diagnostics),
            StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_RejectsUnresolvedStructuredProducerWithIntrinsicSpelling()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        var externalString = new ApiTypeReferenceIdentity(
            new ApiAssemblyIdentity(
                "External",
                new Version(1, 0, 0, 0),
                culture: null,
                publicKeyToken: null),
            "string");
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
                            Name = "Value",
                            Kind = "property",
                            HasGetter = true,
                            SignatureModel = new ApiSignature
                            {
                                ReturnType = "string",
                                ReturnTypeReferences = [externalString],
                                ReturnTypeShape =
                                    ApiTypeShape.Named(externalString),
                            },
                        },
                    ],
                },
            ],
        };

        string dts = DtsEmitter.Emit(surface, diagnostics);

        Assert.Contains(
            "  readonly Value: unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Single(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_DoesNotApplyTaskSemanticsToLookalikeType()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
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
        var diagnostics = new TypeScriptGenerationDiagnostics();
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
    public void Emit_DoesNotMapExtractedNestedFrameworkByteOrTaskLookalikes()
    {
        (ApiTypeReferenceIdentity nestedByte,
            ApiTypeReferenceIdentity nestedTask) =
            ExtractNestedFrameworkLookalikes();
        Assert.Equal("System.Byte", nestedByte.FullName);
        Assert.Equal(
            ["System", "Byte"],
            nestedByte.DefinitionName?.Segments);
        Assert.Equal(
            "System.Threading.Tasks.Task`1",
            nestedTask.FullName);
        Assert.Equal(
            ["Tasks", "Task`1"],
            nestedTask.DefinitionName?.Segments);

        var diagnostics = new TypeScriptGenerationDiagnostics();
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Functions =
            [
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "GetByte",
                    ReturnType = "System.Byte",
                    ReturnTypeReferences = [nestedByte],
                },
                new JsExportFunction
                {
                    DeclaringType = "Exports",
                    Name = "GetTask",
                    ReturnType =
                        "System.Threading.Tasks.Task<string>",
                    ReturnTypeReferences = [nestedTask],
                },
            ],
        };

        string dts = DtsEmitter.Emit(surface, diagnostics);

        Assert.Contains(
            "export declare function getByte(): unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            "export declare function getTask(): unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Equal(2, diagnostics.UnmappedTypes.Count);
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

        Assert.Contains("  readonly wire_name: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly \"display-name\": string;", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly \"\": string;", dts, StringComparison.Ordinal);
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
    [InlineData("Readonly")]
    [InlineData("ReadonlyArray")]
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
                    IndexParameterCount = 0,
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
                    IndexParameterCount = 0,
                },
                new ApiMember
                {
                    Name = "Other",
                    Kind = "property",
                    ReturnType = "string",
                    IndexParameterCount = 0,
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
                    IndexParameterCount = 0,
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
            "readonly \"left\\u202Eright\": string;",
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
                    IndexParameterCount = 0,
                },
            ],
        };
        var surface = new ILInspector.JsExportSurface.JsExportSurface
        {
            Records = [record],
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.Contains($"  readonly {expectedKey}: string;", dts, StringComparison.Ordinal);
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

        Assert.Contains("  readonly Included: string;", dts, StringComparison.Ordinal);
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

        Assert.Contains("  readonly Value: string;", dts, StringComparison.Ordinal);
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

        Assert.Contains("  readonly child: NestedDto;", dts, StringComparison.Ordinal);
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
            "  readonly Child: JsonIncludedFieldNestedFixture;",
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_UsesJsonIncludeToReachNonPublicCompiledMembers()
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
            WireDirections = new Dictionary<ApiType, JsonWireDirection>
            {
                [record] = JsonWireDirection.Serialize,
            },
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.DoesNotContain("SetterOnlyAtWire", dts, StringComparison.Ordinal);
        Assert.DoesNotContain("NoGetter", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly IncludedPrivateGetter: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly IncludedInternalGetter: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly PublicGetter: string;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGeneratedJson_IncludesJsonIncludedNonPublicMembers()
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

        Assert.Contains(
            "\"IncludedPrivateGetter\":\"private-getter\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"IncludedPrivateField\":\"private-field\"",
            json,
            StringComparison.Ordinal);
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
    public void Emit_MatchesSourceGeneratedJsonIncludedNonPublicMembers()
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
            WireDirections = new Dictionary<ApiType, JsonWireDirection>
            {
                [record] = JsonWireDirection.Serialize,
            },
        };

        string dts = DtsEmitter.Emit(surface);

        Assert.Contains("  readonly IncludedPrivateGetter: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly IncludedPrivateField: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly IncludedInternalGetter: string;", dts, StringComparison.Ordinal);
        Assert.Contains("  readonly IncludedInternalField: string;", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGeneratedJson_OmitsJsonIncludedMembersWithInaccessibleValueTypes()
    {
        string json = JsonSerializer.Serialize(
            new SourceGeneratedJsonIncludeHiddenTypeFixture(),
            ControlPropertyNameFixtureJsonContext.Default
                .SourceGeneratedJsonIncludeHiddenTypeFixture);

        Assert.Contains(
            "\"Public\":\"public\"",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("hiddenProperty", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hiddenField", json, StringComparison.Ordinal);
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
        var diagnostics = new TypeScriptGenerationDiagnostics();

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
        var diagnostics = new TypeScriptGenerationDiagnostics();

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
              readonly name: string;
              readonly serverNote: DirectionalNote | null;
              readonly alwaysPresent: string;
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
              readonly name: string;
              readonly clientSecret: string;
            }
            """,
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_PropagatesOnlyMembersPresentInTheActiveDirection()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            """
            export interface DirectionalSharedInputDto {
              readonly value: number;
              readonly secret: string;
            }
            """,
            dts,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export type DirectionalSharedInputDto = unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(DirectionalInactiveInputDto),
            dts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_UsesSetterAccessibilityForDeserializeDeclarations()
    {
        string dts = EmitFixtureDtsWithWireContracts();

        Assert.Contains(
            """
            export interface DirectionalAccessorInputDto {
              readonly id: number;
              readonly privateGetter: string;
              readonly writeOnly: string;
            }
            """,
            dts,
            StringComparison.Ordinal);
        Assert.DoesNotContain("privateSetter:", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGeneratedJson_UsesSetterAccessibilityForDeserialization()
    {
        DirectionalAccessorInputDto? value = JsonSerializer.Deserialize(
            """
            {
              "id": 1,
              "privateGetter": "private-getter",
              "privateSetter": "private-setter",
              "writeOnly": "write-only"
            }
            """,
            DirectionalFixtureJsonContext.Default
                .DirectionalAccessorInputDto);

        Assert.NotNull(value);
        Assert.Equal(1, value.Id);
        Assert.Equal("private-getter", value.ReadPrivateGetter());
        Assert.Equal("", value.PrivateSetter);
        Assert.Equal("write-only", value.ReadWriteOnly());
    }

    [Fact]
    public void Emit_BlocksUnmodeledConstructorBoundDeserialization()
    {
        ConstructorBoundInput? value = JsonSerializer.Deserialize(
            """{"Value":42}""",
            ConstructorBoundJsonContext.Default
                .ConstructorBoundInput);
        Assert.NotNull(value);
        Assert.Equal(42, value.Value);

        string path = typeof(ConstructorBoundExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        apiSurface.FilteredRuntimeJsExportFacts = [];
        apiSurface.Types =
        [
            .. apiSurface.Types.Where(type =>
                type.Name is nameof(ConstructorBoundInput)
                    or nameof(ConstructorBoundJsonContext)
                    or nameof(ConstructorBoundExports)),
        ];
        var bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        var diagnostics = new TypeScriptGenerationDiagnostics();

        string dts = DtsEmitter.Emit(
            JsExportSurfaceBuilder.Build(
                apiSurface,
                bodyIndex),
            diagnostics);

        Assert.Contains(
            "export type ConstructorBoundInput = unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location
                    == "ConstructorBoundInput JSON wire shape"
                && diagnostic.CSharpType
                    == "deserialization without a participating setter requires unmodeled constructor-binding evidence");
    }

    [Fact]
    public void Emit_BlocksConstructorBindingWithPrivateSetter()
    {
        PrivateSetterConstructorBoundInput? value =
            JsonSerializer.Deserialize(
                """{"Value":42}""",
                PrivateSetterConstructorBoundJsonContext.Default
                    .PrivateSetterConstructorBoundInput);
        Assert.NotNull(value);
        Assert.Equal(42, value.Value);

        string path =
            typeof(PrivateSetterConstructorBoundExports)
                .Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        apiSurface.FilteredRuntimeJsExportFacts = [];
        apiSurface.Types =
        [
            .. apiSurface.Types.Where(type =>
                type.Name is
                    nameof(
                        PrivateSetterConstructorBoundInput)
                    or nameof(
                        PrivateSetterConstructorBoundJsonContext)
                    or nameof(
                        PrivateSetterConstructorBoundExports)),
        ];
        ApiMember valueProperty = Assert.Single(
            Assert.Single(
                apiSurface.Types,
                type => type.Name
                    == nameof(
                        PrivateSetterConstructorBoundInput))
                .Members,
            member => member.Name == "Value");
        Assert.True(valueProperty.HasSetter);
        Assert.Equal(
            "private",
            valueProperty.SetterAccessibility);
        Assert.True(
            JsonWireMemberRules
                .RequiresConstructorBindingEvidence(
                    Assert.Single(
                        apiSurface.Types,
                        type => type.Name
                            == nameof(
                                PrivateSetterConstructorBoundInput)),
                    valueProperty));
        var bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures
                    .JsonWireContractFlow);
        var diagnostics = new TypeScriptGenerationDiagnostics();

        string dts = DtsEmitter.Emit(
            JsExportSurfaceBuilder.Build(
                apiSurface,
                bodyIndex),
            diagnostics);

        Assert.Contains(
            "export type PrivateSetterConstructorBoundInput = unknown;",
            dts,
            StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location
                    == "PrivateSetterConstructorBoundInput JSON wire shape"
                && diagnostic.CSharpType
                    == "deserialization without a participating setter requires unmodeled constructor-binding evidence");
    }

    [Fact]
    public void Emit_BlocksBidirectionalTypeWithDirectionSensitiveMember()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);

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
                    == "serialization and deserialization member sets differ on a bidirectional type");
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
              readonly text: string;
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
    /// Builds nested TypeRef rows whose flattened spelling aliases framework
    /// mappings, then extracts them through the production SRM path. The
    /// structured DefinitionName is the discriminator under test.
    /// </summary>
    static (
        ApiTypeReferenceIdentity NestedByte,
        ApiTypeReferenceIdentity NestedTask)
        ExtractNestedFrameworkLookalikes()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("NestedFrameworkLookalikes.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("NestedFrameworkLookalikes"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0xb0, 0x3f, 0x5f, 0x7f,
                        0x11, 0xd5, 0x0a, 0x3a,
                    }),
                default,
                default);
        TypeReferenceHandle byteOuter = metadata.AddTypeReference(
            systemRuntime,
            default,
            metadata.GetOrAddString("System"));
        TypeReferenceHandle nestedByte = metadata.AddTypeReference(
            byteOuter,
            default,
            metadata.GetOrAddString("Byte"));
        TypeReferenceHandle taskOuter = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System.Threading"),
            metadata.GetOrAddString("Tasks"));
        TypeReferenceHandle nestedTask = metadata.AddTypeReference(
            taskOuter,
            default,
            metadata.GetOrAddString("Task`1"));
        TypeReferenceHandle systemString = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("String"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Lookalikes"),
            metadata.GetOrAddString("Exports"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var byteSignature = new BlobBuilder();
        new BlobEncoder(byteSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: false).Parameters(
            0,
            returnType => returnType.Type().Type(
                nestedByte,
                false),
            _ => { });
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("GetByte"),
            metadata.GetOrAddBlob(byteSignature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));

        var taskSignature = new BlobBuilder();
        new BlobEncoder(taskSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: false).Parameters(
            0,
            returnType => returnType.Type()
                .GenericInstantiation(
                    nestedTask,
                    genericArgumentCount: 1,
                    isValueType: false)
                .AddArgument()
                .Type(systemString, false),
            _ => { });
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("GetTask"),
            metadata.GetOrAddBlob(taskSignature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);

        using var stream = new MemoryStream(
            image.ToArray(),
            writable: false);
        using var peReader = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType exports = Assert.Single(
            surface.Types,
            type => type.Name == "Exports");
        ApiTypeReferenceIdentity byteReference = Assert.Single(
            Assert.Single(
                exports.Members,
                member => member.Name == "GetByte")
                .SignatureModel!
                .ReturnTypeReferences);
        ApiTypeReferenceIdentity taskReference = Assert.Single(
            Assert.Single(
                exports.Members,
                member => member.Name == "GetTask")
                .SignatureModel!
                .ReturnTypeReferences,
            reference => reference.FullName
                == "System.Threading.Tasks.Task`1");
        return (byteReference, taskReference);
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
