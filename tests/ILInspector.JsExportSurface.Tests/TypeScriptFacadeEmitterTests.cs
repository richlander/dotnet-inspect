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
        Assert.DoesNotContain(
            "export type JsonText<",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "}\n\nexport async function getWidgetAsync",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_PreservesDeferredJsonOutputAsBrandedText()
    {
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
            ReturnType = "Task<string>",
            ReturnWireType = "Fixture.WidgetDto",
            ReturnWireMode = JsExportJsonOutputMode.JsonText,
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
            """
            declare const jsonTextBrand: unique symbol;

            export type JsonText<T> = string & {
              readonly [jsonTextBrand]: T;
            };
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export async function getWidgetAsync(): Promise<JsonText<WidgetDto>> {
              const $result = await $requireManagedExports()["Fixture"]["Exports"]["GetWidgetAsync.1"]();
              return $result as JsonText<WidgetDto>;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "JSON.parse($result)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_PropagatesDirectionalDeclarationsThroughTypeShapes()
    {
        global::ILInspector.JsExportSurface.JsExportSurface surface =
            BuildSurface(
                typeof(global::ILInspector.JsExportSurface.TypeScriptFixtures
                    .TypeScriptFixtureExports).Assembly.Location);
        ApiType envelope = Assert.Single(
            surface.Records,
            type => type.Name == "DirectionalEnvelopeDto");
        Assert.Equal(
            JsonWireDirection.Both,
            surface.WireDirections[envelope]);
        DtsEmitter.WireDeclarationPlan plan =
            DtsEmitter.CreateWireDeclarationPlan(surface);
        Assert.Equal(
            2,
            plan.Declarations.Count(declaration =>
                ReferenceEquals(declaration.Type, envelope)
                && declaration.IsSplit));

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            """
            export interface DirectionalRoundTripDtoInput {
              readonly name: string;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface DirectionalRoundTripDtoOutput {
              readonly name: string;
              readonly serverNote: string;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface DirectionalEnvelopeDtoInput {
              readonly direct: DirectionalRoundTripDtoInput;
              readonly items: ReadonlyArray<DirectionalRoundTripDtoInput>;
              readonly lookup: Readonly<Record<string, DirectionalRoundTripDtoInput>>;
              readonly box: DirectionalBox<DirectionalRoundTripDtoInput>;
              readonly next: DirectionalEnvelopeDtoInput | null;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface DirectionalEnvelopeDtoOutput {
              readonly direct: DirectionalRoundTripDtoOutput;
              readonly items: ReadonlyArray<DirectionalRoundTripDtoOutput>;
              readonly lookup: Readonly<Record<string, DirectionalRoundTripDtoOutput>>;
              readonly box: DirectionalBox<DirectionalRoundTripDtoOutput>;
              readonly next: DirectionalEnvelopeDtoOutput | null;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface DirectionalOuterDtoInput {
              readonly envelope: DirectionalEnvelopeDtoInput;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface DirectionalOuterDtoOutput {
              readonly envelope: DirectionalEnvelopeDtoOutput;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export type DirectionalChoice = "
                + "DirectionalRoundTripDtoOutput | string | null;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function roundTripDirectional("
                + "payloadJson: DirectionalRoundTripDtoInput): "
                + "DirectionalRoundTripDtoOutput",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function roundTripDirectionalEnvelope("
                + "payloadJson: DirectionalEnvelopeDtoInput): "
                + "DirectionalEnvelopeDtoOutput",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function roundTripDirectionalOuter("
                + "payloadJson: DirectionalOuterDtoInput): "
                + "DirectionalOuterDtoOutput",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            source.Split(
                "export interface DirectionalBox<",
                StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(
            "WidgetDtoInput",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "WidgetDtoOutput",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_UsesOutputDeclarationInsideDeferredJsonText()
    {
        var dto = new ApiType
        {
            Namespace = "Fixture",
            Name = "Directional",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "ServerNote",
                    Kind = "property",
                    ReturnType = "string",
                    HasGetter = true,
                    IndexParameterCount = 0,
                    JsonIgnoreConditions =
                        [JsonWireIgnoreCondition.WhenReading],
                },
            ],
        };
        var output = new JsExportFunction
        {
            DeclaringType = "Fixture.Exports",
            Name = "GetDirectional",
            RuntimeDispatchKey = "GetDirectional.1",
            ReturnType = "Task<string>",
            ReturnWireType = "Fixture.Directional",
            ReturnWireMode = JsExportJsonOutputMode.JsonText,
        };
        var input = new JsExportFunction
        {
            DeclaringType = "Fixture.Exports",
            Name = "SetDirectional",
            RuntimeDispatchKey = "SetDirectional.1",
            ReturnType = "void",
            Parameters =
            [
                new ApiParameter
                {
                    Name = "Payload",
                    Type = "string",
                },
            ],
            ParameterWireBindings =
            [
                new JsExportParameterWireBinding
                {
                    ParameterIndex = 0,
                    WireType = "Fixture.Directional",
                },
            ],
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = AssemblyIdentity(),
                Records = [dto],
                Functions = [output, input],
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [dto] = JsonWireDirection.Both,
                    },
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            "export async function getDirectional(): "
                + "Promise<JsonText<DirectionalOutput>>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function setDirectional("
                + "payload: DirectionalInput): void",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_PropagatesDirectionalSplitThroughUnionCases()
    {
        ApiAssemblyIdentity assembly = AssemblyIdentity();
        MetadataTypeDefinitionName directionalName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    System.Collections.Immutable.ImmutableArray.Create(
                        "Directional")))
                .Name;
        MetadataTypeDefinitionName choiceName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    System.Collections.Immutable.ImmutableArray.Create(
                        "Choice")))
                .Name;
        var directional = new ApiType
        {
            Namespace = "Fixture",
            Name = "Directional",
            Kind = "class",
            DefinitionName = directionalName,
            Members =
            [
                new ApiMember
                {
                    Name = "ServerNote",
                    Kind = "property",
                    ReturnType = "string",
                    HasGetter = true,
                    IndexParameterCount = 0,
                    JsonIgnoreConditions =
                        [JsonWireIgnoreCondition.WhenReading],
                },
            ],
        };
        var choice = new ApiType
        {
            Namespace = "Fixture",
            Name = "Choice",
            Kind = "class",
            DefinitionName = choiceName,
        };
        var assemblyReference = new AssemblyReferenceIdentity(
            assembly.Name,
            assembly.Version,
            assembly.Culture,
            assembly.PublicKeyToken);
        TypeRef directionalReference = TypeRef.Definition(
            assembly.Name,
            "Fixture",
            "Directional",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    assemblyReference),
                directionalName));
        var directionalIdentity = new ApiTypeReferenceIdentity(
            assembly,
            directional.FullName,
            directionalName);
        var choiceIdentity = new ApiTypeReferenceIdentity(
            assembly,
            choice.FullName,
            choiceName);
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = assembly,
                Records = [directional],
                Unions =
                [
                    new JsExportUnion
                    {
                        Definition = choice,
                        CaseTypes = [directionalReference],
                        IncludesNull = false,
                        DeserializationUnsupportedReason = null!,
                    },
                ],
                ReferencedTypeDefinitions =
                    new Dictionary<ApiTypeReferenceIdentity, ApiType>
                    {
                        [directionalIdentity] = directional,
                        [choiceIdentity] = choice,
                    },
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [directional] = JsonWireDirection.Both,
                        [choice] = JsonWireDirection.Both,
                    },
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            "export type ChoiceInput = DirectionalInput;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export type ChoiceOutput = DirectionalOutput;",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_SerializesAuthenticatedJsonInputsWithoutChangingRawAbi()
    {
        global::ILInspector.JsExportSurface.JsExportSurface surface =
            BuildSurface(typeof(FixtureExports).Assembly.Location);
        JsExportFunction asyncRename = surface.Functions.Single(
            function => function.Name == "RenameWidgetAsync");
        JsExportFunction transformedAsync = surface.Functions.Single(
            function => function.Name == "RenameNormalizedWidgetAsync");
        JsExportFunction rename = surface.Functions.Single(
            function => function.Name == "RenameWidgetForOwner");
        JsExportFunction compare = surface.Functions.Single(
            function => function.Name == "WidgetMatchesAudit");
        JsExportFunction transformed = surface.Functions.Single(
            function => function.Name == "RenameNormalizedWidget");
        JsExportFunction conflicted = surface.Functions.Single(
            function => function.Name == "ReadWidgetOrAudit");

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            $"readonly \"{asyncRename.RuntimeDispatchKey}\": "
                + "(widgetJson: string, newName: string) => Promise<string>;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export async function renameWidgetAsync("
                + "widgetJson: WidgetDto, newName: string): "
                + "Promise<WidgetDto>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"[\"{asyncRename.RuntimeDispatchKey}\"]("
                + "$serializeJsonInput(widgetJson, "
                + $"\"{asyncRename.DeclaringType}."
                + $"{asyncRename.RuntimeDispatchKey}\", "
                + "\"widgetJson\"), newName);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export async function renameNormalizedWidgetAsync("
                + "widgetJson: string, newName: string): "
                + "Promise<WidgetDto>",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"[\"{transformedAsync.RuntimeDispatchKey}\"]("
                + "$serializeJsonInput(widgetJson,",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"readonly \"{rename.RuntimeDispatchKey}\": "
                + "(owner: string, widgetJson: string, "
                + "newName: string) => string;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function renameWidgetForOwner("
                + "owner: string, widgetJson: WidgetDto, "
                + "newName: string): WidgetDto",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"[\"{rename.RuntimeDispatchKey}\"]("
                + "owner, $serializeJsonInput(widgetJson, "
                + $"\"{rename.DeclaringType}.{rename.RuntimeDispatchKey}\", "
                + "\"widgetJson\"), newName);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"[\"{compare.RuntimeDispatchKey}\"]("
                + "$serializeJsonInput(widgetJson, "
                + $"\"{compare.DeclaringType}.{compare.RuntimeDispatchKey}\", "
                + "\"widgetJson\"), $serializeJsonInput(auditJson, "
                + $"\"{compare.DeclaringType}.{compare.RuntimeDispatchKey}\", "
                + "\"auditJson\"));",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function renameNormalizedWidget("
                + "widgetJson: string, newName: string): WidgetDto",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function readWidgetOrAudit("
                + "payload: string, summaryJson: string, "
                + "readAudit: boolean): string",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "const json = JSON.stringify(value);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (json === undefined)",
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
    public void Emit_PreservesInertStringProvenanceAsOpaqueBrand()
    {
        global::ILInspector.JsExportSurface.JsExportSurface surface =
            BuildSurface(
                typeof(global::ILInspector.JsExportSurface.TypeScriptFixtures
                    .TypeScriptFixtureExports).Assembly.Location);

        string source = TypeScriptFacadeEmitter.Emit(surface, RuntimeModule);

        Assert.Contains(
            """
            declare const inertStringBrand: unique symbol;

            export type InertString = string & {
              readonly [inertStringBrand]: "InertString";
            };
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface InertWidgetDto {
              readonly name: string;
              readonly display: InertString;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export async function getInertWidgetAsync(name: string): "
                + "Promise<InertWidgetDto>",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "decodeInertString",
            source,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "function inertString(",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_DoesNotBrandAnUnrelatedInertStringSimpleName()
    {
        var applicationAssembly = new ApiAssemblyIdentity(
            "Application",
            new Version(1, 0, 0, 0),
            culture: null,
            publicKeyToken: null);
        var applicationInertString = new ApiTypeReferenceIdentity(
            applicationAssembly,
            "Application.InertString");

        Assert.Equal(
            "ApplicationInertString",
            TsTypeMapper.MapJsonWireType(
                "InertString",
                new HashSet<string>(StringComparer.Ordinal),
                mappedTypeNames: new Dictionary<string, string>
                {
                    ["InertString"] = "ApplicationInertString",
                },
                typeShape: ApiTypeShape.Named(applicationInertString),
                identityNames:
                    new Dictionary<ApiTypeReferenceIdentity, string>
                    {
                        [applicationInertString] =
                            "ApplicationInertString",
                    }));
    }

    [Fact]
    public void Emit_AllocatesBrandBindingsBeforeUnrelatedTypes()
    {
        ApiAssemblyIdentity assembly = AssemblyIdentity();
        var inertStringIdentity = new ApiTypeReferenceIdentity(
            new ApiAssemblyIdentity(
                "InertText",
                new Version(1, 0, 0, 0),
                culture: null,
                publicKeyToken: null),
            "InertText.InertString");
        var container = new ApiType
        {
            Namespace = "Fixture",
            Name = "Container",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Display",
                    Kind = "property",
                    HasGetter = true,
                    JsonConverterAttributeCount = 1,
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "InertText.InertString",
                        ReturnTypeReferences = [inertStringIdentity],
                        ReturnTypeShape =
                            ApiTypeShape.Named(inertStringIdentity),
                    },
                },
            ],
        };
        var unrelated = new ApiType
        {
            Namespace = "Application",
            Name = "InertString",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    HasGetter = true,
                    ReturnType = "string",
                },
            ],
        };
        var unrelatedBrand = new ApiType
        {
            Namespace = "Application",
            Name = "inertStringBrand",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    HasGetter = true,
                    ReturnType = "string",
                },
            ],
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = assembly,
                Records = [container, unrelated, unrelatedBrand],
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [container] = JsonWireDirection.Serialize,
                        [unrelated] = JsonWireDirection.Serialize,
                        [unrelatedBrand] = JsonWireDirection.Serialize,
                    },
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            """
            declare const inertStringBrand: unique symbol;

            export type InertString = string & {
              readonly [inertStringBrand]: "InertString";
            };
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly Display: InertString;",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export interface InertString {",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export interface inertStringBrand {",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            source.Split(
                "export interface type_",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Emit_AllocatesDateTimeOffsetBrandBeforeUnrelatedTypes()
    {
        ApiAssemblyIdentity assembly = AssemblyIdentity();
        var timestampSelection = new ApiType
        {
            Namespace = "Fixture",
            Name = "TimestampSelection",
            Kind = "class",
        };
        var unrelated = new ApiType
        {
            Namespace = "Application",
            Name = "DateTimeOffsetString",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    HasGetter = true,
                    ReturnType = "string",
                },
            ],
        };
        var unrelatedBrand = new ApiType
        {
            Namespace = "Application",
            Name = "dateTimeOffsetStringBrand",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    HasGetter = true,
                    ReturnType = "string",
                },
            ],
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = assembly,
                Records = [unrelated, unrelatedBrand],
                Unions =
                [
                    new JsExportUnion
                    {
                        Definition = timestampSelection,
                        CaseTypes =
                        [
                            TypeRef.CoreLib(
                                "System",
                                "DateTimeOffset"),
                            TypeRef.CoreLib("System", "Boolean"),
                        ],
                        IncludesNull = true,
                    },
                ],
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [timestampSelection] =
                            JsonWireDirection.Serialize,
                        [unrelated] = JsonWireDirection.Serialize,
                        [unrelatedBrand] = JsonWireDirection.Serialize,
                    },
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            """
            declare const dateTimeOffsetStringBrand: unique symbol;

            export type DateTimeOffsetString = string & {
              readonly [dateTimeOffsetStringBrand]: "DateTimeOffsetString";
            };
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export type TimestampSelection = "
                + "DateTimeOffsetString | boolean | null;",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export interface DateTimeOffsetString {",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export interface dateTimeOffsetStringBrand {",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            source.Split(
                "export interface type_",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("browser")]
    public async Task InertStringFixture_SerializesEncodedTextAsScalarString()
    {
        string json = await global::ILInspector.JsExportSurface
            .TypeScriptFixtures.TypeScriptFixtureExports
            .GetInertWidgetAsync("widget");

        Assert.Equal(
            """{"name":"widget","display":"line\\u202Egpj"}""",
            json);
    }

    [Fact]
    public void Emit_PreservesDateTimeOffsetAsOpaqueJsonString()
    {
        global::ILInspector.JsExportSurface.JsExportSurface surface =
            BuildSurface(
                typeof(global::ILInspector.JsExportSurface.TypeScriptFixtures
                    .TypeScriptFixtureExports).Assembly.Location);

        string source = TypeScriptFacadeEmitter.Emit(surface, RuntimeModule);

        Assert.Contains(
            """
            declare const dateTimeOffsetStringBrand: unique symbol;

            export type DateTimeOffsetString = string & {
              readonly [dateTimeOffsetStringBrand]: "DateTimeOffsetString";
            };
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface TimestampDto {
              readonly observedAt: DateTimeOffsetString;
              readonly completedAt: DateTimeOffsetString | null;
              readonly selection: TimestampSelection;
              readonly nullableSelection: NullableTimestampSelection;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export type NullableTimestampSelection = "
                + "DateTimeOffsetString | boolean | null;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export type TimestampSelection = DateTimeOffsetString "
                + "| ReadonlyArray<DateTimeOffsetString | null> "
                + "| Readonly<Record<string, DateTimeOffsetString>> "
                + "| null;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export async function getTimestampAsync(): "
                + "Promise<TimestampDto>",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "parseDateTimeOffset",
            source,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "function dateTimeOffsetString(",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("browser")]
    public async Task DateTimeOffsetFixture_PreservesSerializerTimestampText()
    {
        string json = await global::ILInspector.JsExportSurface
            .TypeScriptFixtures.TypeScriptFixtureExports
            .GetTimestampAsync();

        Assert.Equal(
            """{"observedAt":"2026-09-21T10:30:45.1234567-07:00","completedAt":null,"selection":"2026-09-21T10:30:45.1234567-07:00","nullableSelection":null}""",
            json);
    }

    [Fact]
    public void Emit_ProjectsGenericRecordDeclarationsAndClosedJsonRoots()
    {
        string path = typeof(global::ILInspector.JsExportSurface.TypeScriptFixtures
            .TypeScriptFixtureExports).Assembly.Location;
        global::ILInspector.JsExportSurface.JsExportSurface surface =
            BuildSurface(path);

        string source = TypeScriptFacadeEmitter.Emit(surface, RuntimeModule);

        Assert.Contains(
            """
            export interface GenericNested<T0> {
              readonly value: T0;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface GenericRecord<T0> {
              readonly content: T0;
              readonly nested: GenericNested<T0>;
              readonly items: ReadonlyArray<GenericNested<T0>>;
              readonly lookup: Readonly<Record<string, T0>>;
              readonly choice: Boxed<T0>;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export async function getGenericRecordIntAsync(): "
                + "Promise<GenericRecord<number>>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export async function getGenericRecordWidgetAsync(name: string): "
                + "Promise<GenericRecord<WidgetDto | null>>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function getNullableGenericNested(): "
                + "GenericNested<string | null>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface GenericNestedEnvelope {
              readonly item: GenericNested<string | null>;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function getGenericNestedEnvelope(): "
                + "GenericNestedEnvelope",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface WrappedGenericNestedEnvelope {
              readonly item: Wrapped<GenericNested<string | null>>;
              readonly items: Wrapped<ReadonlyArray<GenericNested<string | null> | null>>;
              readonly lookup: Wrapped<Readonly<Record<string, GenericNested<string | null> | null>>>;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function getWrappedGenericNestedEnvelope(): "
                + "WrappedGenericNestedEnvelope",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface GenericNestedValue<T0> {
              readonly value: T0;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface NullableWrappedGenericNestedEnvelope {
              readonly item: Wrapped<GenericNestedValue<string | null> | null>;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function getNullableWrappedGenericNestedEnvelope(): "
                + "NullableWrappedGenericNestedEnvelope",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface NullablePair<T0, T1> {
              readonly first: T0;
              readonly second: T1;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface MixedNullableValueEnvelope {
              readonly item: NullablePair<number | null, string | null>;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function getMixedNullableValueEnvelope(): "
                + "MixedNullableValueEnvelope",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export type GenericNestedChoice = "
                + "GenericNested<string | null> | number | null;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function getGenericNestedChoice(): "
                + "GenericNestedChoice",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_ReportsUnregisteredGenericRecordConstruction()
    {
        var record = new ApiType
        {
            Namespace = "Fixture",
            Name = "GenericRecord",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "TValue" }],
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    HasGetter = true,
                    IndexParameterCount = 0,
                    ReturnType = "Missing<TValue>",
                },
            ],
        };
        var diagnostics = new TypeScriptGenerationDiagnostics();
        string source = DtsEmitter.Emit(
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                Records = [record],
            },
            diagnostics);

        Assert.Contains("readonly Value: unknown;", source, StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic => diagnostic.CSharpType == "Missing<TValue>");
    }

    [Fact]
    public void Emit_ReportsOpenGenericRecordUseWithoutWireShape()
    {
        var record = new ApiType
        {
            Namespace = "Fixture",
            Name = "GenericRecord",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "TValue" }],
        };
        var diagnostics = new TypeScriptGenerationDiagnostics();
        _ = DtsEmitter.Emit(
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = AssemblyIdentity(),
                Records = [record],
                Functions =
                [
                    new JsExportFunction
                    {
                        DeclaringType = "Fixture.Exports",
                        Name = "Get",
                        RuntimeDispatchKey = "Get.1",
                        ReturnType = "string",
                        ReturnWireType = "Fixture.GenericRecord<TValue>",
                    },
                ],
            },
            diagnostics);

        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic => diagnostic.CSharpType == "TValue");
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
            """
            export type JsonValue =
              | null
              | boolean
              | number
              | string
              | readonly JsonValue[]
              | { readonly [key: string]: JsonValue };
            """,
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
    public void Emit_AllocatesAcrossDirectionalPreferredNameCollisions()
    {
        var split = new ApiType
        {
            Namespace = "A",
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Value",
                    Kind = "property",
                    ReturnType = "string",
                    HasGetter = true,
                    IndexParameterCount = 0,
                    JsonIgnoreConditions =
                        [JsonWireIgnoreCondition.WhenReading],
                },
            ],
        };
        var collision = new ApiType
        {
            Namespace = "B",
            Name = "WidgetInput",
            Kind = "class",
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = AssemblyIdentity(),
                Records = [collision, split],
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [split] = JsonWireDirection.Both,
                        [collision] = JsonWireDirection.Serialize,
                    },
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            """
            export interface WidgetInput {
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface WidgetOutput {
              readonly Value: string;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            source.Split('\n'),
            line => line.StartsWith(
                "export interface type_",
                StringComparison.Ordinal));
        Assert.Equal(
            3,
            DeclaredTypeNames(source).Distinct(
                StringComparer.Ordinal).Count());
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
        var runtimeApiType = new ApiType
        {
            Name = "RuntimeAPI",
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
                Records = [promiseType, runtimeType, runtimeApiType],
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
            3,
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
    public void Emit_DeclaresJsonValueForSplitConditionalJsonElement()
    {
        MetadataTypeDefinitionName jsonElementDefinition =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.ParseSerialized(
                    "System.Text.Json.JsonElement"))
            .Name;
        var jsonElementIdentity = new ApiTypeReferenceIdentity(
            new ApiAssemblyIdentity(
                "System.Text.Json",
                new Version(11, 0, 0, 0),
                culture: null,
                publicKeyToken: "cc7b13ffcd2ddd51"),
            "System.Text.Json.JsonElement",
            jsonElementDefinition);
        var record = new ApiType
        {
            Namespace = "Fixture",
            Name = "PayloadDto",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Payload",
                    Kind = "property",
                    HasGetter = true,
                    HasSetter = true,
                    ReturnType = "System.Text.Json.JsonElement",
                    IndexParameterCount = 0,
                    JsonIgnoreConditions =
                    [
                        JsonWireIgnoreCondition.WhenWritingDefault,
                    ],
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "System.Text.Json.JsonElement",
                        ReturnTypeReferences = [jsonElementIdentity],
                        ReturnTypeShape =
                            ApiTypeShape.Named(
                                jsonElementIdentity,
                                isValueType: true),
                    },
                },
            ],
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = AssemblyIdentity(),
                Records = [record],
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [record] = JsonWireDirection.Both,
                    },
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            "export type JsonValue =",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface PayloadDtoInput {
              readonly Payload: unknown;
            }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface PayloadDtoOutput {
              readonly Payload?: JsonValue;
            }
            """,
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_DoesNotReserveUnusedJsonValueAlias()
    {
        var jsonElementIdentity = new ApiTypeReferenceIdentity(
            new ApiAssemblyIdentity(
                "System.Text.Json",
                new Version(11, 0, 0, 0),
                culture: null,
                publicKeyToken: "cc7b13ffcd2ddd51"),
            "System.Text.Json.JsonElement");
        var jsonValue = new ApiType
        {
            Namespace = "Fixture",
            Name = "JsonValue",
            Kind = "class",
        };
        var converted = new ApiType
        {
            Namespace = "Fixture",
            Name = "Converted",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Payload",
                    Kind = "property",
                    HasGetter = true,
                    ReturnType = "System.Text.Json.JsonElement",
                    IndexParameterCount = 0,
                    JsonConverterAttributeCount = 1,
                    JsonIgnoreConditions =
                    [
                        JsonWireIgnoreCondition.WhenWritingDefault,
                    ],
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "System.Text.Json.JsonElement",
                        ReturnTypeReferences = [jsonElementIdentity],
                        ReturnTypeShape =
                            ApiTypeShape.Named(
                                jsonElementIdentity,
                                isValueType: true),
                    },
                },
            ],
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = AssemblyIdentity(),
                Records = [jsonValue, converted],
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [jsonValue] = JsonWireDirection.Serialize,
                        [converted] = JsonWireDirection.Serialize,
                    },
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            "export interface JsonValue {",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export type JsonValue =",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly Payload?: unknown;",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_DoesNotReserveJsonValueForNestedJsonElement()
    {
        var jsonElementIdentity = new ApiTypeReferenceIdentity(
            new ApiAssemblyIdentity(
                "System.Text.Json",
                new Version(11, 0, 0, 0),
                culture: null,
                publicKeyToken: "cc7b13ffcd2ddd51"),
            "System.Text.Json.JsonElement");
        var jsonValue = new ApiType
        {
            Namespace = "Fixture",
            Name = "JsonValue",
            Kind = "class",
        };
        var nested = new ApiType
        {
            Namespace = "Fixture",
            Name = "Nested",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Payload",
                    Kind = "property",
                    HasGetter = true,
                    ReturnType = "System.Text.Json.JsonElement[]?",
                    IndexParameterCount = 0,
                    JsonIgnoreConditions =
                    [
                        JsonWireIgnoreCondition.WhenWritingNull,
                    ],
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "System.Text.Json.JsonElement[]?",
                        ReturnTypeReferences = [jsonElementIdentity],
                        ReturnTypeShape = ApiTypeShape.SzArray(
                            ApiTypeShape.Named(
                                jsonElementIdentity,
                                isValueType: true)),
                    },
                },
            ],
        };
        var surface =
            new global::ILInspector.JsExportSurface.JsExportSurface
            {
                AssemblyIdentity = AssemblyIdentity(),
                Records = [jsonValue, nested],
                WireDirections =
                    new Dictionary<ApiType, JsonWireDirection>
                    {
                        [jsonValue] = JsonWireDirection.Serialize,
                        [nested] = JsonWireDirection.Serialize,
                    },
            };

        string source = TypeScriptFacadeEmitter.Emit(
            surface,
            RuntimeModule);

        Assert.Contains(
            "export interface JsonValue {",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "export type JsonValue =",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "readonly Payload?: ReadonlyArray<unknown>;",
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
