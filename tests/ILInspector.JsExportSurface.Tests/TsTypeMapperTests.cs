using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;
using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Verifies <see cref="TsTypeMapper"/>: all TS-specific "personality" (Task/ValueTask unwrap to
/// Promise, array/nullable mapping, C# primitive to TS primitive, record-name passthrough) lives
/// here per the repo's architecture decision that the OM stays C#-faithful.
/// </summary>
public sealed class TsTypeMapperTests
{
    private static readonly HashSet<string> RecordNames = new(StringComparer.Ordinal)
    {
        "WidgetDto",
        "ILInspector.JsExportSurface.Fixtures.WidgetDto",
    };
    private static readonly ApiAssemblyIdentity FixtureAssembly = new(
        "ILInspector.JsExportSurface.Fixtures",
        new Version(1, 0, 0, 0),
        culture: null,
        publicKeyToken: "0011223344556677");

    [Theory]
    [InlineData("string", "string")]
    [InlineData("System.String", "string")]
    [InlineData("bool", "boolean")]
    [InlineData("System.Boolean", "boolean")]
    [InlineData("int", "number")]
    [InlineData("System.Int32", "number")]
    [InlineData("long", "number")]
    [InlineData("double", "number")]
    [InlineData("void", "void")]
    public void MapReturnType_MapsCSharpPrimitivesToTsPrimitives(string csharpType, string expected)
    {
        Assert.Equal(expected, TsTypeMapper.MapReturnType(csharpType, RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsGenericTaskToPromise()
    {
        Assert.Equal("Promise<string>", TsTypeMapper.MapReturnType("Task<string>", RecordNames));
        Assert.Equal(
            "Promise<string>",
            TsTypeMapper.MapReturnType("System.Threading.Tasks.Task<string>", RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsGenericValueTaskToPromise()
    {
        Assert.Equal("Promise<number>", TsTypeMapper.MapReturnType("ValueTask<int>", RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsNonGenericTaskToPromiseVoid()
    {
        Assert.Equal("Promise<void>", TsTypeMapper.MapReturnType("Task", RecordNames));
        Assert.Equal(
            "Promise<void>",
            TsTypeMapper.MapReturnType("System.Threading.Tasks.Task", RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsNonGenericValueTaskToPromiseVoid()
    {
        Assert.Equal("Promise<void>", TsTypeMapper.MapReturnType("ValueTask", RecordNames));
    }

    [Fact]
    public void Map_ArrayTypeMapsToTsArraySyntax()
    {
        Assert.Equal("number[]", TsTypeMapper.MapParameterType("int[]", RecordNames));
    }

    [Theory]
    [InlineData("byte[]")]
    [InlineData("System.Byte[]")]
    public void MapJsonWireType_MapsExactByteArraysToBase64Strings(string csharpType)
    {
        Assert.Equal("string", TsTypeMapper.MapJsonWireType(csharpType, RecordNames));
    }

    [Fact]
    public void MapJsonWireType_DoesNotApplyDelegateAuthorityToByteSimpleName()
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "ReadonlyArray<unknown>",
            TsTypeMapper.MapJsonWireType(
                "Byte[]",
                RecordNames,
                diagnostics,
                "Payload.Bytes"));
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location == "Payload.Bytes"
                && diagnostic.CSharpType == "Byte");
    }

    [Fact]
    public void MapJsonWireType_MapsArraysToReadonlyArrays()
    {
        Assert.Equal(
            "ReadonlyArray<number>",
            TsTypeMapper.MapJsonWireType("int[]", RecordNames));
        Assert.Equal(
            "ReadonlyArray<WidgetDto | null>",
            TsTypeMapper.MapJsonWireType("WidgetDto?[]", RecordNames));
        Assert.Equal(
            "ReadonlyArray<ReadonlyArray<number>>",
            TsTypeMapper.MapJsonWireType("int[][]", RecordNames));
    }

    [Theory]
    [InlineData("byte[]")]
    [InlineData("System.Byte[]")]
    public void MapInteropType_PreservesByteArraysAsNumericArrays(string csharpType)
    {
        Assert.Equal("number[]", TsTypeMapper.MapParameterType(csharpType, RecordNames));
        Assert.Equal("number[]", TsTypeMapper.MapReturnType(csharpType, RecordNames));
    }

    [Fact]
    public void Map_PreservesOrdinaryArrayMappingForOtherByteLikeTypes()
    {
        Assert.Equal("number[]", TsTypeMapper.MapParameterType("sbyte[]", RecordNames));
        Assert.Equal("number[]", TsTypeMapper.MapParameterType("System.SByte[]", RecordNames));
    }

    [Fact]
    public void Map_NullableTypeMapsToUnionWithNull()
    {
        Assert.Equal("WidgetDto | null", TsTypeMapper.MapParameterType("WidgetDto?", RecordNames));
    }

    [Fact]
    public void MapParameterType_MapsAuthenticatedActionWithNullablePayload()
    {
        var signature = new JsExportDelegateParameter
        {
            ParameterIndex = 0,
            Kind = JsExportDelegateKind.Action,
            ParameterTypes =
            [
                TypeRef.CoreLib("System", "String"),
            ],
        };

        Assert.Equal(
            "(arg0: string | null) => undefined",
            TsTypeMapper.MapParameterType(
                "System.Action<string?>",
                RecordNames,
                delegateParameter: signature));
    }

    [Fact]
    public void MapParameterType_MapsAuthenticatedNullableArrayPayload()
    {
        Assert.Equal(
            "(arg0: string[] | null) => undefined",
            TsTypeMapper.MapParameterType(
                "System.Action<string[]?>",
                RecordNames,
                delegateParameter: ActionParameter(
                    TypeRef.SzArray(
                        TypeRef.CoreLib("System", "String")))));
    }

    [Fact]
    public void MapParameterType_MapsAuthenticatedIntPtrAsNumber()
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "(arg0: number) => undefined",
            TsTypeMapper.MapParameterType(
                "System.Action<nint>",
                RecordNames,
                diagnostics,
                "RegisterPointer.callback",
                delegateParameter: ActionParameter(
                    TypeRef.CoreLib("System", "IntPtr"))));
        Assert.Empty(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void MapJsonWireType_DoesNotInheritIntPtrInteropMapping()
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapJsonWireType(
                "nint",
                RecordNames,
                diagnostics,
                "Payload.Pointer"));
        Assert.NotEmpty(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void MapParameterType_MapsAuthenticatedFuncInManagedOrder()
    {
        var signature = new JsExportDelegateParameter
        {
            ParameterIndex = 0,
            Kind = JsExportDelegateKind.Func,
            ParameterTypes =
            [
                TypeRef.CoreLib("System", "Int32"),
                TypeRef.CoreLib("System", "String"),
            ],
            ReturnType = TypeRef.CoreLib("System", "Int32"),
        };

        Assert.Equal(
            "(arg0: number, arg1: string) => number",
            TsTypeMapper.MapParameterType(
                "System.Func<int, string, int>",
                RecordNames,
                delegateParameter: signature));
    }

    [Fact]
    public void MapParameterType_AcceptsCorrelatedQualifiedDelegateTypes()
    {
        var signature = new JsExportDelegateParameter
        {
            ParameterIndex = 0,
            Kind = JsExportDelegateKind.Func,
            ParameterTypes =
            [
                TypeRef.SzArray(
                    TypeRef.CoreLib("System", "Int32")),
                TypeRef.GenericInstance(
                    TypeRef.Definition(
                        "System.Runtime",
                        "System.Collections.Generic",
                        "IReadOnlyDictionary`2"),
                    [
                        TypeRef.CoreLib("System", "String"),
                        TypeRef.CoreLib("System", "String"),
                    ]),
            ],
            ReturnType = TypeRef.GenericInstance(
                TypeRef.CoreLib("System", "Nullable`1"),
                [TypeRef.CoreLib("System", "Int32")]),
        };

        Assert.Equal(
            "(arg0: number[], arg1: Record<string, string | null>) "
                + "=> number | null",
            TsTypeMapper.MapParameterType(
                "global::System.Func<System.Int32[], "
                    + "System.Collections.Generic."
                    + "IReadOnlyDictionary<string, string?>, "
                    + "System.Nullable<int>>",
                RecordNames,
                delegateParameter: signature));
    }

    [Fact]
    public void MapParameterType_AcceptsCorrelatedLocalRecordIdentity()
    {
        var signature = new JsExportDelegateParameter
        {
            ParameterIndex = 0,
            Kind = JsExportDelegateKind.Action,
            ParameterTypes =
            [
                ResolvedType(
                    FixtureAssembly,
                    "ILInspector.JsExportSurface.Fixtures",
                    "WidgetDto"),
            ],
        };

        Assert.Equal(
            "(arg0: WidgetDto | null) => undefined",
            TsTypeMapper.MapParameterType(
                "System.Action<"
                    + "ILInspector.JsExportSurface.Fixtures.WidgetDto?>",
                RecordNames,
                delegateParameter: signature,
                delegateMappingContext:
                    FixtureDelegateContext(
                        TsLocalTypeKind.Reference)));
    }

    [Fact]
    public void MapParameterType_DoesNotRebindAuthenticatedFrameworkPayloadThroughAlias()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var mappedTypeNames = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["System.DateTime"] = "LocalDateTime",
        };

        Assert.Equal(
            "(arg0: unknown) => undefined",
            TsTypeMapper.MapParameterType(
                "System.Action<System.DateTime>",
                new HashSet<string>(
                    ["System.DateTime"],
                    StringComparer.Ordinal),
                diagnostics,
                "Observe.callback",
                mappedTypeNames: mappedTypeNames,
                delegateParameter: ActionParameter(
                    TypeRef.CoreLib("System", "DateTime"))));
        Assert.Single(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void MapParameterType_PreservesAllocatedLocalIntrinsicSpelling()
    {
        MetadataTypeDefinitionName definitionName =
            DefinitionName("Mine", "IntPtr");
        var context = new TsDelegateMappingContext(
            new HashSet<string>(
                ["Mine.IntPtr", "IntPtr"],
                StringComparer.Ordinal),
            new Dictionary<
                MetadataTypeDefinitionName,
                TsLocalTypeKind>
            {
                [definitionName] = TsLocalTypeKind.Reference,
            },
            FixtureAssembly,
            new Dictionary<MetadataTypeDefinitionName, string>
            {
                [definitionName] = "IntPtr",
            });

        Assert.Equal(
            "(arg0: IntPtr) => undefined",
            TsTypeMapper.MapParameterType(
                "System.Action<Mine.IntPtr>",
                context.RecordNames,
                delegateParameter: ActionParameter(
                    ResolvedType(
                        FixtureAssembly,
                        "Mine",
                        "IntPtr")),
                delegateMappingContext: context));
    }

    [Fact]
    public void MapParameterType_RejectsIncompleteLocalAssemblyIdentity()
    {
        var assembly = new ApiAssemblyIdentity(
            "Local",
            version: null,
            culture: null,
            publicKeyToken: null);
        var context = new TsDelegateMappingContext(
            new HashSet<string>(["Mine.Payload"], StringComparer.Ordinal),
            new Dictionary<
                MetadataTypeDefinitionName,
                TsLocalTypeKind>
            {
                [DefinitionName("Mine", "Payload")] =
                    TsLocalTypeKind.Reference,
            },
            assembly);

        AssertDelegateRejected(
            "System.Action<Mine.Payload>",
            ActionParameter(
                ResolvedType(
                    assembly,
                    "Mine",
                    "Payload")),
            context);
    }

    [Fact]
    public void MapParameterType_RejectsDelegateFactsBeyondSdkArity()
    {
        TypeRef intType = TypeRef.CoreLib("System", "Int32");

        AssertDelegateRejected(
            "System.Action<int, int, int, int>",
            new JsExportDelegateParameter
            {
                ParameterIndex = 0,
                Kind = JsExportDelegateKind.Action,
                ParameterTypes =
                [
                    intType,
                    intType,
                    intType,
                    intType,
                ],
            });
        AssertDelegateRejected(
            "System.Func<int, int, int, int, int>",
            new JsExportDelegateParameter
            {
                ParameterIndex = 0,
                Kind = JsExportDelegateKind.Func,
                ParameterTypes =
                [
                    intType,
                    intType,
                    intType,
                    intType,
                ],
                ReturnType = intType,
            });
    }

    [Fact]
    public void MapParameterType_RejectsFrameworkLookalikeIdentities()
    {
        AssertDelegateRejected(
            "System.Action<System.Int32>",
            ActionParameter(
                TypeRef.Definition(
                    "Other.Assembly",
                    "System",
                    "Int32")));
        AssertDelegateRejected(
            "System.Action<int>",
            ActionParameter(
                TypeRef.Definition(
                    "System.Runtime",
                    "System",
                    "Int32",
                    trustedFrameworkAssembly: false)));
        AssertDelegateRejected(
            "System.Action<"
                + "System.Collections.Generic."
                + "IReadOnlyDictionary<string, string>>",
            ActionParameter(
                TypeRef.GenericInstance(
                    TypeRef.Definition(
                        "Other.Assembly",
                        "System.Collections.Generic",
                        "IReadOnlyDictionary`2"),
                    [
                        TypeRef.CoreLib("System", "String"),
                        TypeRef.CoreLib("System", "String"),
                    ])));
    }

    [Fact]
    public void MapParameterType_RejectsSameRecordFromDifferentAssembly()
    {
        AssertDelegateRejected(
            "System.Action<"
                + "ILInspector.JsExportSurface.Fixtures.WidgetDto>",
            ActionParameter(
                ResolvedType(
                    new ApiAssemblyIdentity(
                        "Other.Assembly",
                        new Version(1, 0, 0, 0),
                        culture: null,
                        publicKeyToken: null),
                    "ILInspector.JsExportSurface.Fixtures",
                    "WidgetDto")),
            FixtureDelegateContext(
                TsLocalTypeKind.Reference));
    }

    [Fact]
    public void MapParameterType_RejectsNullableLocalValueTypeWithoutWrapper()
    {
        AssertDelegateRejected(
            "System.Action<"
                + "ILInspector.JsExportSurface.Fixtures.WidgetDto?>",
            ActionParameter(
                ResolvedType(
                    FixtureAssembly,
                    "ILInspector.JsExportSurface.Fixtures",
                    "WidgetDto")),
            FixtureDelegateContext(TsLocalTypeKind.Value));
    }

    [Fact]
    public void MapParameterType_MapsNullableLocalValueTypeWithWrapper()
    {
        TypeRef localType = ResolvedType(
            FixtureAssembly,
            "ILInspector.JsExportSurface.Fixtures",
            "WidgetDto");

        Assert.Equal(
            "(arg0: WidgetDto | null) => undefined",
            TsTypeMapper.MapParameterType(
                "System.Action<"
                    + "ILInspector.JsExportSurface.Fixtures.WidgetDto?>",
                RecordNames,
                delegateParameter: ActionParameter(
                    TypeRef.GenericInstance(
                        TypeRef.CoreLib("System", "Nullable`1"),
                        [localType])),
                delegateMappingContext:
                    FixtureDelegateContext(TsLocalTypeKind.Value)));
    }

    [Fact]
    public void MapParameterType_RejectsNullableWrapperAroundLocalReferenceType()
    {
        TypeRef localType = ResolvedType(
            FixtureAssembly,
            "ILInspector.JsExportSurface.Fixtures",
            "WidgetDto");

        AssertDelegateRejected(
            "System.Action<"
                + "ILInspector.JsExportSurface.Fixtures.WidgetDto?>",
            ActionParameter(
                TypeRef.GenericInstance(
                    TypeRef.CoreLib("System", "Nullable`1"),
                    [localType])),
            FixtureDelegateContext(TsLocalTypeKind.Reference));
    }

    [Fact]
    public void MapParameterType_RejectsExplicitNullableOfReferenceType()
    {
        AssertDelegateRejected(
            "System.Action<System.Nullable<string>>",
            ActionParameter(
                TypeRef.GenericInstance(
                    TypeRef.CoreLib("System", "Nullable`1"),
                    [TypeRef.CoreLib("System", "String")])));
    }

    [Fact]
    public void MapParameterType_RejectsNullableLocalTypeWithoutClassification()
    {
        AssertDelegateRejected(
            "System.Action<"
                + "ILInspector.JsExportSurface.Fixtures.WidgetDto?>",
            ActionParameter(
                ResolvedType(
                    FixtureAssembly,
                    "ILInspector.JsExportSurface.Fixtures",
                    "WidgetDto")),
            new TsDelegateMappingContext(
                RecordNames,
                new Dictionary<
                    MetadataTypeDefinitionName,
                    TsLocalTypeKind>(),
                FixtureAssembly));
    }

    [Fact]
    public void MapParameterType_RejectsSameSimpleAssemblyWithDifferentIdentity()
    {
        AssertDelegateRejected(
            "System.Action<"
                + "ILInspector.JsExportSurface.Fixtures.WidgetDto>",
            ActionParameter(
                ResolvedType(
                    new ApiAssemblyIdentity(
                        FixtureAssembly.Name,
                        new Version(2, 0, 0, 0),
                        FixtureAssembly.Culture,
                        "8899aabbccddeeff"),
                    "ILInspector.JsExportSurface.Fixtures",
                    "WidgetDto")),
            FixtureDelegateContext(TsLocalTypeKind.Reference));
    }

    [Fact]
    public void MapParameterType_RejectsLocalTypeWithoutResolutionOrigin()
    {
        AssertDelegateRejected(
            "System.Action<"
                + "ILInspector.JsExportSurface.Fixtures.WidgetDto>",
            ActionParameter(
                TypeRef.Definition(
                    FixtureAssembly.Name,
                    "ILInspector.JsExportSurface.Fixtures",
                    "WidgetDto")),
            FixtureDelegateContext(TsLocalTypeKind.Reference));
    }

    [Fact]
    public void MapParameterType_RejectsFlattenedLocalDefinitionCollision()
    {
        var recordNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "C",
            "A.B.C",
        };
        var context = new TsDelegateMappingContext(
            recordNames,
            new Dictionary<
                MetadataTypeDefinitionName,
                TsLocalTypeKind>
            {
                [DefinitionName("A.B", "C")] =
                    TsLocalTypeKind.Reference,
            },
            FixtureAssembly);
        TypeRef nestedType = ResolvedType(
            FixtureAssembly,
            "A",
            "B+C",
            DefinitionName("A", "B", "C"));
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType(
                "System.Action<A.B.C?>",
                recordNames,
                diagnostics,
                "Register.callback",
                delegateParameter: ActionParameter(nestedType),
                delegateMappingContext: context));
        Assert.NotEmpty(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void MapParameterType_RejectsMalformedFrameworkGenericNames()
    {
        AssertDelegateRejected(
            "System.Action<System.Nullable<int>>",
            ActionParameter(
                TypeRef.GenericInstance(
                    TypeRef.Definition(
                        "Other.Assembly",
                        "System",
                        "Nullable"),
                    [TypeRef.CoreLib("System", "Int32")])));
        AssertDelegateRejected(
            "System.Action<"
                + "System.Collections.Generic."
                + "Dictionary<string, string>>",
            ActionParameter(
                TypeRef.GenericInstance(
                    TypeRef.Definition(
                        "Other.Assembly",
                        "System.Collections.Generic",
                        "Dictionary"),
                    [
                        TypeRef.CoreLib("System", "String"),
                        TypeRef.CoreLib("System", "String"),
                    ])));
    }

    [Fact]
    public void MapParameterType_RejectsFrameworkNamesWithWrongGenericArity()
    {
        AssertDelegateRejected(
            "System.Action<System.Nullable<int, int>>",
            ActionParameter(
                TypeRef.GenericInstance(
                    TypeRef.CoreLib("System", "Nullable`2"),
                    [
                        TypeRef.CoreLib("System", "Int32"),
                        TypeRef.CoreLib("System", "Int32"),
                    ])));
        AssertDelegateRejected(
            "System.Action<System.String<int>>",
            ActionParameter(
                TypeRef.GenericInstance(
                    TypeRef.CoreLib("System", "String`1"),
                    [TypeRef.CoreLib("System", "Int32")])));
    }

    [Fact]
    public void MapParameterType_RejectsVoidDelegatePayloads()
    {
        AssertDelegateRejected(
            "System.Action<void>",
            ActionParameter(
                TypeRef.CoreLib("System", "Void")));
        AssertDelegateRejected(
            "System.Func<void>",
            new JsExportDelegateParameter
            {
                ParameterIndex = 0,
                Kind = JsExportDelegateKind.Func,
                ReturnType = TypeRef.CoreLib("System", "Void"),
            });
    }

    [Fact]
    public void MapParameterType_PreservesAuthenticatedOpaqueJsObject()
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "(arg0: unknown) => undefined",
            TsTypeMapper.MapParameterType(
                "System.Action<"
                    + "System.Runtime.InteropServices.JavaScript.JSObject>",
                RecordNames,
                diagnostics,
                "Register.callback",
                delegateParameter: ActionParameter(
                    TypeRef.Definition(
                        "System.Runtime.InteropServices.JavaScript",
                        "System.Runtime.InteropServices.JavaScript",
                        "JSObject"))));
        Assert.Empty(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void MapParameterType_FrameworkFactsOverrideLocalNameCollisions()
    {
        var recordNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "JSObject",
            "Mine.JSObject",
            "Int32",
            "Mine.Int32",
        };
        var context = new TsDelegateMappingContext(
            recordNames,
            new Dictionary<
                MetadataTypeDefinitionName,
                TsLocalTypeKind>
            {
                [DefinitionName("Mine", "JSObject")] =
                    TsLocalTypeKind.Reference,
                [DefinitionName("Mine", "Int32")] =
                    TsLocalTypeKind.Reference,
            },
            FixtureAssembly);
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "(arg0: unknown) => undefined",
            TsTypeMapper.MapParameterType(
                "System.Action<JSObject>",
                recordNames,
                diagnostics,
                "RegisterJsObject.callback",
                delegateParameter: ActionParameter(
                    TypeRef.Definition(
                        "System.Runtime.InteropServices.JavaScript",
                        "System.Runtime.InteropServices.JavaScript",
                        "JSObject")),
                delegateMappingContext: context));
        Assert.Equal(
            "(arg0: number) => undefined",
            TsTypeMapper.MapParameterType(
                "System.Action<Int32>",
                recordNames,
                diagnostics,
                "RegisterInt32.callback",
                delegateParameter: ActionParameter(
                    TypeRef.CoreLib("System", "Int32")),
                delegateMappingContext: context));
        Assert.Empty(diagnostics.UnmappedTypes);
    }

    [Fact]
    public void MapParameterType_FrameworkFilteringPreservesLocalGenericArguments()
    {
        TypeRef localType = ResolvedType(
            FixtureAssembly,
            "ILInspector.JsExportSurface.Fixtures",
            "WidgetDto");
        TypeRef dictionary = TypeRef.GenericInstance(
            TypeRef.Definition(
                "System.Collections",
                "System.Collections.Generic",
                "Dictionary`2"),
            [
                TypeRef.CoreLib("System", "String"),
                localType,
            ]);

        Assert.Equal(
            "(arg0: Record<string, WidgetDto>) => undefined",
            TsTypeMapper.MapParameterType(
                "System.Action<"
                    + "System.Collections.Generic."
                    + "Dictionary<string, "
                    + "ILInspector.JsExportSurface.Fixtures.WidgetDto>>",
                RecordNames,
                delegateParameter: ActionParameter(dictionary),
                delegateMappingContext:
                    FixtureDelegateContext(
                        TsLocalTypeKind.Reference)));
    }

    [Fact]
    public void MapParameterType_RejectsAuthenticatedIdentityMismatch()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var signature = new JsExportDelegateParameter
        {
            ParameterIndex = 0,
            Kind = JsExportDelegateKind.Func,
            ParameterTypes =
            [
                TypeRef.CoreLib("System", "Int32"),
            ],
            ReturnType = TypeRef.CoreLib("System", "Int32"),
        };

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType(
                "System.Func<string, string>",
                RecordNames,
                diagnostics,
                "TransformValue.callback",
                delegateParameter: signature));
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location == "TransformValue.callback"
                && diagnostic.CSharpType
                    == "System.Func<string, string>");
    }

    [Fact]
    public void MapParameterType_RejectsUnqualifiedRecordAliasMismatch()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var signature = new JsExportDelegateParameter
        {
            ParameterIndex = 0,
            Kind = JsExportDelegateKind.Action,
            ParameterTypes =
            [
                TypeRef.Definition(
                    "Other",
                    "Other",
                    "WidgetDto"),
            ],
        };

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType(
                "System.Action<WidgetDto>",
                RecordNames,
                diagnostics,
                "ReportWidget.callback",
                delegateParameter: signature));
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location == "ReportWidget.callback"
                && diagnostic.CSharpType
                    == "System.Action<WidgetDto>");
    }

    [Fact]
    public void MapParameterType_DoesNotTrustDelegateLookingText()
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType(
                "System.Action<int>",
                RecordNames,
                diagnostics,
                "ReportValue.callback"));
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location == "ReportValue.callback"
                && diagnostic.CSharpType == "System.Action<int>");
    }

    [Fact]
    public void MapParameterType_RejectsPromiseReturningDelegate()
    {
        var diagnostics = new TsBindGenDiagnostics();
        var signature = new JsExportDelegateParameter
        {
            ParameterIndex = 0,
            Kind = JsExportDelegateKind.Func,
            ParameterTypes =
            [
                TypeRef.CoreLib("System", "Int32"),
            ],
            ReturnType = TypeRef.GenericInstance(
                TypeRef.CoreLib(
                    "System.Threading.Tasks",
                    "Task`1"),
                [TypeRef.CoreLib("System", "Int32")]),
        };

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType(
                "System.Func<int, int>",
                RecordNames,
                diagnostics,
                "TransformAsync.callback",
                delegateParameter: signature));
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location == "TransformAsync.callback"
                && diagnostic.CSharpType
                    == "System.Func<int, int>");
    }

    [Fact]
    public void Map_KnownRecordNamePassesThroughByName()
    {
        Assert.Equal("WidgetDto", TsTypeMapper.MapParameterType("WidgetDto", RecordNames));
        Assert.Equal(
            "WidgetDto",
            TsTypeMapper.MapParameterType("ILInspector.JsExportSurface.Fixtures.WidgetDto", RecordNames));
    }

    [Fact]
    public void Map_UnknownTypeMapsToUnknownAndReportsDiagnostic()
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType("SomeUnmappedType", RecordNames, diagnostics, "WidgetDto.Property"));
        Assert.Collection(
            diagnostics.UnmappedTypes,
            d =>
            {
                Assert.Equal("WidgetDto.Property", d.Location);
                Assert.Equal("SomeUnmappedType", d.CSharpType);
            });
    }

    [Fact]
    public void Map_QualifiedExternalTypeDoesNotAliasLocalRecord()
    {
        var knownTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Result",
            "Mine.Result",
        };
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType(
                "Other.Result",
                knownTypes,
                diagnostics,
                "Holder.Value"));
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic => diagnostic.Location == "Holder.Value"
                && diagnostic.CSharpType == "Other.Result");
        Assert.Equal(
            "Result",
            TsTypeMapper.MapParameterType(
                "Mine.Result",
                knownTypes));
    }

    [Fact]
    public void Map_ArrayOfNullableRecordParenthesizesTheUnion()
    {
        // "WidgetDto | null[]" would bind as "WidgetDto | (null[])" in TS; the array of a union
        // must be parenthesized: "(WidgetDto | null)[]".
        Assert.Equal("(WidgetDto | null)[]", TsTypeMapper.MapParameterType("WidgetDto?[]", RecordNames));
    }

    [Fact]
    public void Map_NullableValueTypeUnwrapsSystemNullable()
    {
        // Nullable<T> value types (e.g. `int?`) surface in signature text as "System.Nullable<T>",
        // not the "T?" suffix form used for nullable reference types.
        Assert.Equal("number | null", TsTypeMapper.MapParameterType("System.Nullable<int>", RecordNames));
        Assert.Equal("number | null", TsTypeMapper.MapParameterType("Nullable<int>", RecordNames));
    }

    [Theory]
    [InlineData("byte", "number")]
    [InlineData("sbyte", "number")]
    [InlineData("short", "number")]
    [InlineData("ushort", "number")]
    [InlineData("uint", "number")]
    [InlineData("long", "number")]
    [InlineData("ulong", "number")]
    [InlineData("float", "number")]
    [InlineData("decimal", "number")]
    [InlineData("char", "string")]
    public void Map_MapsAllCommonCSharpPrimitives(string csharpType, string expected)
    {
        Assert.Equal(expected, TsTypeMapper.MapParameterType(csharpType, RecordNames));
    }

    [Fact]
    public void Map_DictionaryOfStringKeysMapsToRecord()
    {
        Assert.Equal(
            "Record<string, string>",
            TsTypeMapper.MapParameterType("IReadOnlyDictionary<string, string>", RecordNames));
        Assert.Equal(
            "Readonly<Record<string, ReadonlyArray<WidgetDto>>>",
            TsTypeMapper.MapJsonWireType(
                "IReadOnlyDictionary<string, WidgetDto[]>",
                RecordNames));
    }

    [Fact]
    public void Map_DictionaryWithNonStringKeyReportsUnmappedType()
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType(
                "Dictionary<int, string>",
                RecordNames,
                diagnostics,
                "WidgetCatalog.OwnersByKey"));
        Assert.Contains(
            diagnostics.UnmappedTypes,
            d => d.Location == "WidgetCatalog.OwnersByKey" && d.CSharpType == "Dictionary<int, string>");
    }

    [Theory]
    [InlineData("System.Text.Json.JsonElement")]
    [InlineData("JsonElement")]
    public void Map_JsonElementMapsToUnknownWithoutReportingAsUnmapped(string csharpType)
    {
        // JsonElement is STJ's own representation of arbitrary/untyped JSON: "unknown" is the
        // deliberately correct TS shape here, not a gap the way an unrecognized type (Guid,
        // DateTime, an unmappable Dictionary) is — so it must not be recorded as an unmapped type.
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType(csharpType, RecordNames, diagnostics, "BrowserAnnotatedSource.Document"));
        Assert.Empty(diagnostics.UnmappedTypes);
    }

    [Theory]
    [InlineData("System.Runtime.InteropServices.JavaScript.JSObject")]
    [InlineData("JSObject")]
    public void Map_JSObjectMapsToUnknownWithoutReportingAsUnmapped(string csharpType)
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType(
                csharpType,
                RecordNames,
                diagnostics,
                "InspectionEngine.RunPackageQuery.eventSink"));
        Assert.Empty(diagnostics.UnmappedTypes);
    }

    [Theory]
    [InlineData("System.Runtime.InteropServices.JavaScript.JSObject")]
    [InlineData("JSObject")]
    public void MapJsonWireType_JSObjectReportsUnmappedType(string csharpType)
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapJsonWireType(
                csharpType,
                RecordNames,
                diagnostics,
                "QueryEvent.Sink"));
        Assert.Collection(
            diagnostics.UnmappedTypes,
            diagnostic =>
            {
                Assert.Equal("QueryEvent.Sink", diagnostic.Location);
                Assert.Equal(csharpType, diagnostic.CSharpType);
            });
    }

    static JsExportDelegateParameter ActionParameter(TypeRef parameterType) =>
        new()
        {
            ParameterIndex = 0,
            Kind = JsExportDelegateKind.Action,
            ParameterTypes = [parameterType],
        };

    static void AssertDelegateRejected(
        string displayType,
        JsExportDelegateParameter signature,
        TsDelegateMappingContext? mappingContext = null)
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType(
                displayType,
                RecordNames,
                diagnostics,
                "Register.callback",
                delegateParameter: signature,
                delegateMappingContext: mappingContext));
        Assert.Contains(
            diagnostics.UnmappedTypes,
            diagnostic =>
                diagnostic.Location == "Register.callback"
                && diagnostic.CSharpType == displayType);
    }

    static TsDelegateMappingContext FixtureDelegateContext(
        TsLocalTypeKind kind) =>
        new(
            RecordNames,
            new Dictionary<
                MetadataTypeDefinitionName,
                TsLocalTypeKind>
            {
                [DefinitionName(
                    "ILInspector.JsExportSurface.Fixtures",
                    "WidgetDto")] = kind,
            },
            FixtureAssembly);

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
}
