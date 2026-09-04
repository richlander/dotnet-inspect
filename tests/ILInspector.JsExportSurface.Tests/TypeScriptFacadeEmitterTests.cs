using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.JsExportSurface.PublishabilityFixtures;
using ILInspector.Metadata;
namespace ILInspector.JsExportSurface.Tests;

public sealed class TypeScriptFacadeEmitterTests
{
    private const string RuntimeModule = "./_framework/dotnet.js";

    [Fact]
    public void Emit_ProducesOneTypedModuleWithRawWireAndPublicViews()
    {
        var dto = new ApiType
        {
            Namespace = "Fixture",
            Name = "WidgetDto",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Name",
                    Kind = "property",
                    ReturnType = "string",
                },
            ],
        };
        var function = new JsExportFunction
        {
            DeclaringType = "Fixture.Exports",
            Name = "GetWidgetAsync",
            RuntimeDispatchKey = "GetWidgetAsync.-42",
            ReturnType = "Task<string>",
            ReturnWireType = "Fixture.WidgetDto",
            Parameters =
            [
                new ApiParameter
                {
                    Name = "Name",
                    Type = "string",
                },
            ],
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = new ApiAssemblyIdentity(
                    "Fixture",
                    new Version(1, 0, 0, 0),
                    culture: null,
                    publicKeyToken: null),
                Functions = [function],
                Records = [dto],
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [dto] = JsonWireDirection.Serialize,
                    },
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            """import { dotnet } from "./_framework/dotnet.js";""",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export interface WidgetDto {",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            readonly "GetWidgetAsync.-42": (name: string) => Promise<string>;
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export async function getWidgetAsync(name: string): Promise<WidgetDto> {
              const $result = await $requireManagedExports()["Fixture"]["Exports"]["GetWidgetAsync.-42"](name);
              const $parsed: unknown = JSON.parse($result);
              return $parsed as WidgetDto;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export declare function",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "}\n\nexport async function getWidgetAsync",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ProjectsAuthenticatedSynchronousDelegateFacts()
    {
        var function = new JsExportFunction
        {
            DeclaringType = "Fixture.Exports",
            Name = "Observe",
            RuntimeDispatchKey = "Observe.-42",
            ReturnType = "void",
            Parameters =
            [
                new ApiParameter
                {
                    Name = "Callback",
                    Type = "System.Action<int>",
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
                        TypeRef.CoreLib("System", "Int32"),
                    ],
                },
            ],
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = new ApiAssemblyIdentity(
                    "Fixture",
                    new Version(1, 0, 0, 0),
                    culture: null,
                    publicKeyToken: null),
                Functions = [function],
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            """
            readonly "Observe.-42": (callback: (arg0: number) => undefined) => void;
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export function observe(callback: (arg0: number) => undefined): void {
              return $requireManagedExports()["Fixture"]["Exports"]["Observe.-42"](callback);
            }
            """,
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_DoesNotRebindAuthenticatedDelegatePayloadThroughLocalAlias()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        var assembly = new ApiAssemblyIdentity(
            "Fixture",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        var localDateTime = new ApiType
        {
            Namespace = "System",
            Name = "DateTime",
            Kind = "class",
        };
        var function = new JsExportFunction
        {
            DeclaringType = "Fixture.Exports",
            Name = "Observe",
            RuntimeDispatchKey = "Observe.-42",
            ReturnType = "void",
            Parameters =
            [
                new ApiParameter
                {
                    Name = "Callback",
                    Type = "System.Action<System.DateTime>",
                    TypeReferences =
                    [
                        new ApiTypeReferenceIdentity(
                            assembly,
                            "System.DateTime"),
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
                        TypeRef.CoreLib("System", "DateTime"),
                    ],
                },
            ],
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = assembly,
                Functions = [function],
                Records = [localDateTime],
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule,
            diagnostics);

        Assert.Contains(
            """
            readonly "Observe.-42": (callback: (arg0: unknown) => undefined) => void;
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function observe("
                + "callback: (arg0: unknown) => undefined): void",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location == "Observe.Callback");
    }

    [Fact]
    public void Emit_PreservesAllocatedLocalDelegateTypeWithIntrinsicSpelling()
    {
        var assembly = new ApiAssemblyIdentity(
            "Fixture",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        MetadataTypeDefinitionName definitionName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Mine",
                    System.Collections.Immutable.ImmutableArray.Create(
                        "IntPtr")))
                .Name;
        var identity = new AssemblyReferenceIdentity(
            assembly.Name,
            assembly.Version,
            assembly.Culture,
            assembly.PublicKeyToken);
        var localIntPtr = new ApiType
        {
            Namespace = "Mine",
            Name = "IntPtr",
            Kind = "class",
            DefinitionName = definitionName,
        };
        var function = new JsExportFunction
        {
            DeclaringType = "Fixture.Exports",
            Name = "Observe",
            RuntimeDispatchKey = "Observe.-42",
            ReturnType = "void",
            Parameters =
            [
                new ApiParameter
                {
                    Name = "Callback",
                    Type = "System.Action<Mine.IntPtr>",
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
                        TypeRef.Definition(
                            assembly.Name,
                            "Mine",
                            "IntPtr",
                            new ResolvableTypeReference(
                                new TypeReferenceOrigin.AssemblyReference(
                                    identity),
                                definitionName)),
                    ],
                },
            ],
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = assembly,
                Functions = [function],
                Records = [localIntPtr],
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            "export interface IntPtr {",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "callback: (arg0: IntPtr) => undefined",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ModelsTerminalSingleFlightInitializationAndSeparateEntryPoint()
    {
        string source = TypeScriptFacadeEmitter.Emit(
            Surface(),
            RuntimeModule);

        Assert.Contains(
            "$initializationFailure = { error };\n"
                + "        throw error;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            const $notInitializedError = new Error("The .NET runtime facade is not initialized.");
            """,
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            source.Split(
                "throw $notInitializedError;",
                StringSplitOptions.None).Length - 1);
        Assert.Contains(
            "export function initializeRuntime(\n"
                + "  runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>,\n"
                + "): Promise<void>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function createRuntime(): Promise<JsExportRuntime>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            ".then(() => runtime === undefined ? createRuntime() : runtime)\n"
                + "      .then($initializeRuntimeCore)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "return $requireRuntime().runMain(mainAssemblyName, args);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            const exports: unknown = await runtime.getAssemblyExports("Fixture");
            """,
            source,
            StringComparison.Ordinal);
        int validate = source.IndexOf(
            "$validateManagedExports(exports);",
            StringComparison.Ordinal);
        int publishRuntime = source.IndexOf(
            "$runtime = runtime;",
            StringComparison.Ordinal);
        int publishExports = source.IndexOf(
            "$managedExports = exports;",
            StringComparison.Ordinal);
        Assert.True(validate < publishRuntime);
        Assert.True(publishRuntime < publishExports);
        Assert.DoesNotContain(
            "window",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "runMain();",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ValidatesOwnDataPropertiesAndExactRuntimeKeys()
    {
        string source = TypeScriptFacadeEmitter.Emit(
            Surface(
                Function(
                    "Fixture.Exports",
                    "Identify",
                    "Identify.101",
                    "int",
                    ("Value", "int")),
                Function(
                    "Fixture.Exports",
                    "Identify",
                    "Identify.202",
                    "string",
                    ("Value", "string"))),
            RuntimeModule);

        Assert.Contains(
            "Object.getOwnPropertyDescriptor(value, key)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """$ownDataProperty(value, "Identify.101")""",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """$ownDataProperty(value, "Identify.202")""",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """["Identify.101"](value)""",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """["Identify.202"](value)""",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            source.Split("export function identify", StringSplitOptions.None)
                .Length - 1
                + source.Split(
                    "export function operation_",
                    StringSplitOptions.None)
                    .Length - 1);
    }

    [Fact]
    public void Emit_DispatchesCompiledOverloadsThroughTheirExactRuntimeKeys()
    {
        string path = typeof(OverloadedExportFixture).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface extracted =
            ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name == nameof(OverloadedExportFixture));
        fixture.Members =
        [
            .. fixture.Members.Where(
                member => member.Name
                    == nameof(OverloadedExportFixture.Identify)),
        ];
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [fixture];
        LibraryBodyIndex bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        global::ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(extracted, bodyIndex);

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Equal(2, surface.Functions.Count);
        foreach (JsExportFunction function in surface.Functions)
        {
            Assert.Contains(
                $"[\"{function.RuntimeDispatchKey}\"]",
                source,
                StringComparison.Ordinal);
        }
        Assert.Equal(
            2,
            source.Split('\n').Count(line =>
                line.StartsWith(
                    "export function identify",
                    StringComparison.Ordinal)
                || line.StartsWith(
                    "export function operation_",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void Emit_PreservesPrimitivesWhenProducerTypeUsesKeywordSpelling()
    {
        global::ILInspector.JsExportSurface.JsExportSurface surface =
            BuildSurface(
                typeof(global::ILInspector.JsExportSurface.TypeScriptFixtures
                    .TypeScriptFixtureExports).Assembly.Location);
        JsExportFunction function = surface.Functions.Single(
            function => function.Name == "GetStringDtoAsync");
        JsExportFunction mapFunction = surface.Functions.Single(
            function => function.Name == "GetKeywordMapAsync");
        Assert.Equal(
            "System.Collections.Generic.IReadOnlyDictionary`2",
            mapFunction.ReturnWireTypeShape?.Definition?.FullName);
        Assert.Contains(
            mapFunction.ReturnWireTypeReferences,
            reference =>
                reference.FullName
                    == "System.Collections.Generic.IReadOnlyDictionary`2");

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            $"readonly \"{function.RuntimeDispatchKey}\": "
                + "(value: string) => Promise<string>;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export async function getStringDtoAsync(value: string): "
                + "Promise<type_",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly value: string;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export interface KeywordHolder {",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly title: string;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly inner: type_",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly many: ReadonlyArray<type_",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly byName: Readonly<Record<string, type_",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export interface byte {",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly byteDtos: ReadonlyArray<byte>;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly maybeBlob: string | null;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly blobs: ReadonlyArray<string | null>;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly blobsByName: "
                + "Readonly<Record<string, string | null>>;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"readonly \"{mapFunction.RuntimeDispatchKey}\": "
                + "(value: string) => Promise<string>;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export async function getKeywordMapAsync(value: string): "
                + "Promise<Readonly<Record<string, type_",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ReservesModuleInteropNamesAndParsesNullableJsonEnvelope()
    {
        global::ILInspector.JsExportSurface.JsExportSurface surface =
            BuildSurface(
                typeof(global::ILInspector.JsExportSurface.TypeScriptFixtures
                    .TypeScriptFixtureExports).Assembly.Location);
        JsExportFunction undefined = surface.Functions.Single(
            function => function.Name == "Undefined");
        JsExportFunction then = surface.Functions.Single(
            function => function.Name == "Then");
        JsExportFunction jsonElement = surface.Functions.Single(
            function => function.Name == "GetJsonElement");
        JsExportFunction nullable = surface.Functions.Single(
            function => function.Name == "GetNullableWidgetAsync");

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.DoesNotContain(
            "export function undefined(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export function then(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function operation_",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"readonly \"{undefined.RuntimeDispatchKey}\": "
                + "(value: string) => string;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"readonly \"{then.RuntimeDispatchKey}\": "
                + "(value: string) => string;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"readonly \"{jsonElement.RuntimeDispatchKey}\": () => string;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function getJsonElement(): unknown {\n"
                + "  const $result = $requireManagedExports()",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"readonly \"{nullable.RuntimeDispatchKey}\": "
                + "(name: string) => Promise<string | null>;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export async function getNullableWidgetAsync(name: string): "
                + "Promise<WidgetDto>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "returned null for an authenticated JSON envelope.",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "const $parsed: unknown = JSON.parse($result);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ReportsRejectedAsyncEnvelopeWithoutThrowing()
    {
        var diagnostics = new TypeScriptGenerationDiagnostics();
        var dto = new ApiType
        {
            Namespace = "Fixture",
            Name = "WidgetDto",
            Kind = "class",
        };
        var function = new JsExportFunction
        {
            DeclaringType = "Fixture.Exports",
            Name = "GetWidgetAsync",
            RuntimeDispatchKey = "GetWidgetAsync.1",
            ReturnType = "System.Threading.Tasks.Task<string>",
            ReturnTypeReferences =
            [
                new(
                    new ApiAssemblyIdentity(
                        "System.Threading.Tasks",
                        new Version(1, 0, 0, 0),
                        culture: null,
                        publicKeyToken: "b03f5f7f11d50a3a"),
                    "System.Threading.Tasks.Task`1"),
            ],
            ReturnWireType = "Fixture.WidgetDto",
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = AssemblyIdentity(),
                Functions = [function],
                Records = [dto],
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [dto] = JsonWireDirection.Serialize,
                    },
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule,
            diagnostics);

        Assert.Contains(
            "export function getWidgetAsync(): unknown",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export async function getWidgetAsync",
            source,
            StringComparison.Ordinal);
        Assert.NotEmpty(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void Emit_MapsEveryManagedOperationToOneFacadeFunction()
    {
        JsExportFunction[] functions =
        [
            Function(
                "Fixture.Exports",
                "First",
                "First.1",
                "void"),
            Function(
                "Fixture.Exports",
                "Second",
                "Second.2",
                "string"),
            Function(
                "Fixture.Exports",
                "Second",
                "Second.3",
                "string",
                ("Value", "string")),
        ];

        string source = TypeScriptFacadeEmitter.Emit(
            Surface(functions),
            RuntimeModule);

        string[] facadeFunctions =
        [
            .. source.Split('\n')
                .Where(line =>
                    line.StartsWith("export function ", StringComparison.Ordinal)
                    || line.StartsWith(
                        "export async function ",
                        StringComparison.Ordinal))
                .Where(line =>
                    !line.StartsWith(
                        "export function createRuntime(",
                        StringComparison.Ordinal)
                    && !line.StartsWith(
                        "export function initializeRuntime(",
                        StringComparison.Ordinal)
                    && !line.StartsWith(
                        "export function runEntryPoint(",
                        StringComparison.Ordinal)),
        ];
        Assert.Equal(functions.Length, facadeFunctions.Length);
        Assert.Equal(
            functions.Length,
            facadeFunctions.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Emit_AllocatesParametersAwayFromGeneratedLocals()
    {
        string source = TypeScriptFacadeEmitter.Emit(
            Surface(
                Function(
                    "Fixture.Exports",
                    "Parse",
                    "Parse.1",
                    "string",
                    ("$result", "string"))),
            RuntimeModule);

        Assert.DoesNotContain(
            "function parse($result:",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "function parse(parameter_",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_AllocatesDistinctTypedNamesFromCompleteManagedIdentities()
    {
        var assembly = new ApiAssemblyIdentity(
            "Fixture",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        var first = new ApiType
        {
            Namespace = "A",
            Name = "Widget",
            Kind = "class",
        };
        var second = new ApiType
        {
            Namespace = "B",
            Name = "Widget",
            Kind = "class",
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = assembly,
                Records = [second, first],
                Functions =
                [
                    new JsExportFunction
                    {
                        DeclaringType = "Fixture.Exports",
                        Name = "GetWidget",
                        RuntimeDispatchKey = "GetWidget.1",
                        ReturnType = "string",
                        ReturnWireType = "B.Widget",
                        ReturnWireTypeReferences =
                        [
                            new ApiTypeReferenceIdentity(
                                assembly,
                                "B.Widget"),
                        ],
                    },
                ],
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        string[] declarations =
        [
            .. source.Split('\n')
                .Where(line => line.StartsWith(
                    "export interface ",
                    StringComparison.Ordinal))
                .Where(line => line != "export interface JsExportRuntime {"),
        ];
        Assert.Equal(2, declarations.Length);
        Assert.Equal(2, declarations.Distinct(StringComparer.Ordinal).Count());
        string secondName = declarations
            .Select(line => line["export interface ".Length..^2])
            .Single(name => source.Contains(
                $"return $parsed as {name};",
                StringComparison.Ordinal));
        Assert.StartsWith("type_", secondName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("enum", "enum")]
    [InlineData("enum", "class")]
    public void Emit_AllocatesAcrossEnumAndRecordTypeCollisions(
        string firstKind,
        string secondKind)
    {
        var first = new ApiType
        {
            Namespace = "A",
            Name = "Widget",
            Kind = firstKind,
        };
        var second = new ApiType
        {
            Namespace = "B",
            Name = "Widget",
            Kind = secondKind,
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = AssemblyIdentity(),
                Records =
                [
                    .. new[] { first, second }
                        .Where(type => type.Kind != "enum"),
                ],
                Enums =
                [
                    .. new[] { first, second }
                        .Where(type => type.Kind == "enum"),
                ],
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        string[] names = DeclaredTypeNames(source);
        Assert.Equal(2, names.Length);
        Assert.Equal(
            2,
            names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Emit_UsesExactTypeIdentityInsideNestedWireContainers()
    {
        ApiAssemblyIdentity assembly = AssemblyIdentity();
        var first = new ApiType
        {
            Namespace = "A",
            Name = "Widget",
            Kind = "class",
        };
        var second = new ApiType
        {
            Namespace = "B",
            Name = "Widget",
            Kind = "class",
        };
        string memberType =
            "IReadOnlyDictionary<string, Widget?[]>";
        var container = new ApiType
        {
            Namespace = "Fixture",
            Name = "Container",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Items",
                    Kind = "property",
                    ReturnType = memberType,
                    IndexParameterCount = 0,
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = memberType,
                        ReturnTypeReferences =
                        [
                            new ApiTypeReferenceIdentity(
                                assembly,
                                second.FullName),
                        ],
                    },
                },
            ],
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = assembly,
                Records = [container, first, second],
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [container] = JsonWireDirection.Serialize,
                        [first] = JsonWireDirection.Serialize,
                        [second] = JsonWireDirection.Serialize,
                    },
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        string secondName = DeclaredTypeNames(source)
            .Single(name => name.StartsWith(
                "type_",
                StringComparison.Ordinal));
        Assert.Contains(
            $"readonly Items: Readonly<Record<string, "
                + $"ReadonlyArray<{secondName} | null>>>;",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_AllocatesProducerBindingsWithoutRenamingInfrastructure()
    {
        var promiseType = new ApiType
        {
            Name = "Promise",
            Kind = "class",
        };
        var runtimeType = new ApiType
        {
            Name = "JsExportRuntime",
            Kind = "class",
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = new ApiAssemblyIdentity(
                    "Fixture",
                    new Version(1, 0, 0, 0),
                    culture: null,
                    publicKeyToken: null),
                Records = [promiseType, runtimeType],
                Functions =
                [
                    Function(
                        "Fixture.Exports",
                        "InitializeRuntime",
                        "InitializeRuntime.1",
                        "void"),
                    Function(
                        "Fixture.Exports",
                        "CreateRuntime",
                        "CreateRuntime.2",
                        "void"),
                ],
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Equal(
            2,
            source.Split(
                "export interface type_",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            source.Split(
                "export interface JsExportRuntime {",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            source.Split(
                "export function createRuntime(",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            source.Split(
                "export function initializeRuntime(",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            2,
            source.Split(
                "export function operation_",
                StringSplitOptions.None).Length - 1);
        Assert.Contains(
            "function $ownDataProperty(value: unknown, key: string): unknown",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_AllocatesOperationsAcrossTypesInfrastructureAndHelpers()
    {
        var type = new ApiType
        {
            Name = "widget",
            Kind = "class",
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = AssemblyIdentity(),
                Records = [type],
                Functions =
                [
                    Function(
                        "Fixture.Exports",
                        "Widget",
                        "Widget.1",
                        "void"),
                    Function(
                        "Fixture.Exports",
                        "InitializeRuntime",
                        "InitializeRuntime.2",
                        "void"),
                    Function(
                        "Fixture.Exports",
                        "$ownDataProperty",
                        "$ownDataProperty.3",
                        "void"),
                ],
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Equal(
            3,
            source.Split(
                "export function operation_",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            source.Split(
                "export function initializeRuntime(",
                StringSplitOptions.None).Length - 1);
        Assert.Contains(
            "function $ownDataProperty(value: unknown, key: string): unknown",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_AllocatesAfterEveryDigestPrefixIsReserved()
    {
        JsExportFunction function = Function(
            "Fixture.Exports",
            "Collision",
            "Collision.1",
            "void");
        const string Identity = "Fixture.Exports::Collision()";
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Identity)))
            .ToLowerInvariant();
        List<ApiType> records =
        [
            new ApiType
            {
                Name = "collision",
                Kind = "class",
            },
        ];
        for (int length = 8; length <= digest.Length; length += 4)
        {
            records.Add(new ApiType
            {
                Name = $"operation_{digest[..length]}",
                Kind = "class",
            });
        }

        string source = TypeScriptFacadeEmitter.Emit(
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = AssemblyIdentity(),
                Records = records,
                Functions = [function],
            },
            RuntimeModule);

        Assert.Contains(
            $"export function operation_{digest}_2()",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_AllocatesParametersAcrossParametersModuleAndLocals()
    {
        string source = TypeScriptFacadeEmitter.Emit(
            Surface(
                Function(
                    "Fixture.Exports",
                    "Collide",
                    "Collide.1",
                    "string",
                    ("Value", "string"),
                    ("Value", "string"),
                    ("dotnet", "string"),
                    ("$result", "string"))),
            RuntimeModule);

        string declaration = source.Split('\n').Single(
            line => line.StartsWith(
                "export function collide(",
                StringComparison.Ordinal));
        Assert.StartsWith(
            "export function collide(value: string, parameter_",
            declaration,
            StringComparison.Ordinal);
        Assert.Equal(
            3,
            declaration.Split(
                "parameter_",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Emit_RejectsMissingOrDuplicateRuntimeDispatchIdentity()
    {
        UnsupportedWireContractException missing =
            Assert.Throws<UnsupportedWireContractException>(
                () => TypeScriptFacadeEmitter.Emit(
                    Surface(
                        Function(
                            "Fixture.Exports",
                            "Missing",
                            runtimeKey: null,
                            "void")),
                    RuntimeModule));
        Assert.Contains(
            "authenticated runtime dispatch key",
            missing.Message,
            StringComparison.Ordinal);

        Assert.Throws<UnsupportedWireContractException>(
            () => TypeScriptFacadeEmitter.Emit(
                Surface(
                    Function(
                        "Fixture.Exports",
                        "First",
                        "Shared.1",
                        "void"),
                    Function(
                        "Fixture.Exports",
                        "Second",
                        "Shared.1",
                        "void")),
                RuntimeModule));
    }

    [Theory]
    [InlineData("ValueTask")]
    [InlineData("ValueTask<string>")]
    [InlineData("System.Threading.Tasks.ValueTask")]
    [InlineData("System.Threading.Tasks.ValueTask<string>")]
    public void Emit_RejectsValueTaskReturns(string returnType)
    {
        UnsupportedWireContractException exception =
            Assert.Throws<UnsupportedWireContractException>(
                () => TypeScriptFacadeEmitter.Emit(
                    Surface(
                        Function(
                            "Fixture.Exports",
                            "Unsupported",
                            "Unsupported.1",
                            returnType)),
                    RuntimeModule));

        Assert.Contains(
            "ValueTask returns are not supported",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_IsDeterministicAcrossInputOrdering()
    {
        JsExportFunction first = Function(
            "Fixture.Z",
            "Zulu",
            "Zulu.2",
            "string");
        JsExportFunction second = Function(
            "Fixture.A",
            "Alpha",
            "Alpha.1",
            "string");

        Assert.Equal(
            TypeScriptFacadeEmitter.Emit(
                Surface(first, second),
                RuntimeModule),
            TypeScriptFacadeEmitter.Emit(
                Surface(second, first),
                RuntimeModule));
    }

    [Fact]
    public void Emit_IsDeterministicAcrossTypeOrdering()
    {
        var first = new ApiType
        {
            Namespace = "A",
            Name = "Widget",
            Kind = "class",
        };
        var second = new ApiType
        {
            Namespace = "B",
            Name = "Widget",
            Kind = "class",
        };
        ApiAssemblyIdentity assembly = new(
            "Fixture",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);

        string Emit(params ApiType[] records) =>
            TypeScriptFacadeEmitter.Emit(
                new global::ILInspector.JsExportSurface.JsExportSurface
                {
                    AssemblyIdentity = assembly,
                    Records = records,
                },
                RuntimeModule);

        Assert.Equal(
            Emit(first, second),
            Emit(second, first));
    }

    [Fact]
    public void
        Emit_ProducesByteIdenticalTypeScriptAcrossCompilerAndRuntimeAsyncLowerings()
    {
        global::ILInspector.JsExportSurface.JsExportSurface compilerSurface =
            BuildSurface(typeof(FixtureExports).Assembly.Location);
        global::ILInspector.JsExportSurface.JsExportSurface runtimeSurface =
            BuildSurface(Path.Combine(
                AppContext.BaseDirectory,
                "ILInspector.JsExportSurface.RuntimeAsyncFixtures.dll"));

        Assert.Equal(
            TypeScriptFacadeEmitter.Emit(
                compilerSurface,
                RuntimeModule),
            TypeScriptFacadeEmitter.Emit(
                runtimeSurface,
                RuntimeModule));
    }

    private static global::ILInspector.JsExportSurface.JsExportSurface
        BuildSurface(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface =
            ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        LibraryBodyIndex bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        return JsExportSurfaceBuilder.Build(apiSurface, bodyIndex);
    }

    private static global::ILInspector.JsExportSurface.JsExportSurface
        Surface(params JsExportFunction[] functions) =>
        new()
        {
            AssemblyIdentity = AssemblyIdentity(),
            Functions = functions,
        };

    private static ApiAssemblyIdentity AssemblyIdentity() =>
        new(
            "Fixture",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);

    private static string[] DeclaredTypeNames(string source) =>
    [
        .. source.Split('\n')
            .Where(line =>
                line.StartsWith("export interface ", StringComparison.Ordinal)
                || line.StartsWith("export type ", StringComparison.Ordinal))
            .Where(line => line != "export interface JsExportRuntime {")
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[2]),
    ];

    private static JsExportFunction Function(
        string declaringType,
        string name,
        string? runtimeKey,
        string returnType,
        params (string Name, string Type)[] parameters) =>
        new()
        {
            DeclaringType = declaringType,
            Name = name,
            RuntimeDispatchKey = runtimeKey,
            ReturnType = returnType,
            Parameters =
            [
                .. parameters.Select(parameter =>
                    new ApiParameter
                    {
                        Name = parameter.Name,
                        Type = parameter.Type,
                    }),
            ],
        };
}
