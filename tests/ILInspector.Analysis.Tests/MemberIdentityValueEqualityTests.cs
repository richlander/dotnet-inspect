using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

[CollectionDefinition(
    AllocationMeasurementCollection.Name,
    DisableParallelization = true)]
public sealed class AllocationMeasurementCollection
{
    public const string Name = "Allocation measurement";
}

[Collection(AllocationMeasurementCollection.Name)]
public class MemberIdentityValueEqualityTests
{
    [Fact]
    public void MemberRefEquality_IgnoresInternalParameterDirectionEvidence()
    {
        var baseline = new MemberRef(
            TypeRef.Definition("Sample", "Sample", "Api"),
            "Read",
            [TypeRef.ByRef(TypeRef.CoreLib("System", "Int32"))],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            ParameterDirections = [ParameterDirection.Ref],
        };
        var memberReference = baseline with
        {
            ParameterDirections = [],
        };

        Assert.Equal(baseline, memberReference);
        Assert.Equal(
            baseline.GetHashCode(),
            memberReference.GetHashCode());
    }

    [Fact]
    public void TypeRefInstantiationTraversesRetainedCustomModifiers()
    {
        TypeRef open = TypeRef.UnsupportedModified(
            TypeRef.Definition("Sample", "Sample", "Modifier"),
            TypeRef.MethodGenericParameter(0, "T"),
            isRequired: false);

        TypeRef instantiated = open.Instantiate(
            [],
            [TypeRef.CoreLib("System", "Int32")]);

        Assert.NotSame(open, instantiated);
        Assert.Equal(
            TypeRef.CoreLib("System", "Int32"),
            instantiated.UnmodifiedType);
        Assert.Equal(
            TypeRefKind.MethodGenericParameter,
            open.UnmodifiedType!.Kind);
        Assert.Same(open.ModifierType, instantiated.ModifierType);
    }

    [Fact]
    public void TypeRefInstantiationTraversesFunctionPointerSignatures()
    {
        var signature = new MethodSignature<TypeRef>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.CDecl,
                SignatureAttributes.Instance),
            TypeRef.MethodGenericParameter(0, "T"),
            requiredParameterCount: 1,
            genericParameterCount: 0,
            [TypeRef.MethodGenericParameter(0, "T")]);
        TypeRef open =
            TypeRef.UnsupportedFunctionPointer(signature);

        TypeRef instantiated = open.Instantiate(
            [],
            [TypeRef.CoreLib("System", "Int32")]);

        Assert.NotSame(open, instantiated);
        MethodSignature<TypeRef> closed =
            instantiated.FunctionPointerSignature!.Value;
        Assert.Equal(signature.Header, closed.Header);
        Assert.Equal(
            signature.RequiredParameterCount,
            closed.RequiredParameterCount);
        Assert.Equal(
            signature.GenericParameterCount,
            closed.GenericParameterCount);
        Assert.Equal(
            TypeRef.CoreLib("System", "Int32"),
            closed.ReturnType);
        Assert.Equal(
            TypeRef.CoreLib("System", "Int32"),
            Assert.Single(closed.ParameterTypes));
        Assert.Equal(
            TypeRefKind.MethodGenericParameter,
            open.FunctionPointerSignature!.Value.ReturnType.Kind);
        Assert.Same(open, open.Instantiate([], []));
    }

    [Fact]
    public void TypeRefSharedDag_EqualityHashAndAsyncIdentityAreLinear()
    {
        TypeRef left = TypeRef.CoreLib("System", "Int32");
        TypeRef right = TypeRef.CoreLib("System", "Int32");
        TypeRef pair =
            TypeRef.Definition("Sample", "Sample", "Pair`2");
        for (int depth = 0; depth < 30; depth++)
        {
            left = TypeRef.GenericInstance(pair, [left, left]);
            right = TypeRef.GenericInstance(pair, [right, right]);
        }

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(left, right));
        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypeIdentity(left)
                .Length < 10_000);
    }

    [Fact]
    public void TypeRefShallowEqualityAndHashing_DoNotAllocate()
    {
        TypeRef leftLeaf = TypeRef.CoreLib("System", "Int32");
        TypeRef rightLeaf = TypeRef.CoreLib("System", "Int32");
        TypeRef definition =
            TypeRef.Definition("Sample", "Sample", "Pair`2");
        TypeRef leftGeneric =
            TypeRef.GenericInstance(
                definition,
                [leftLeaf, TypeRef.CoreLib("System", "String")]);
        TypeRef rightGeneric =
            TypeRef.GenericInstance(
                TypeRef.Definition("Sample", "Sample", "Pair`2"),
                [rightLeaf, TypeRef.CoreLib("System", "String")]);

        Assert.Equal(
            0,
            MeasureEqualityAllocations(leftLeaf, leftLeaf));
        Assert.Equal(
            0,
            MeasureEqualityAllocations(leftLeaf, rightLeaf));
        Assert.Equal(
            0,
            MeasureHashAllocations(leftLeaf));
        Assert.Equal(
            0,
            MeasureEqualityAllocations(leftGeneric, rightGeneric));
        Assert.Equal(
            0,
            MeasureHashAllocations(leftGeneric));
    }

    [Fact]
    public void AllocationMeasurement_RejectsAmortizedOperationAllocation()
    {
        int calls = 0;
        long allocated = MeasureSteadyStateAllocations(
            () =>
            {
                if (++calls % 2_000 == 0)
                    Consume(new object());
            },
            static () => { });

        Assert.True(allocated > 0);
    }

    [Fact]
    public void TypeRefExactAndLegacySimpleNames_AgreeWithoutDelimiterInference()
    {
        static TypeRef Exact(params string[] segments)
        {
            MetadataTypeDefinitionName name =
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "Sample",
                        [.. segments]))
                .Name;
            return TypeRef.Definition(
                "Sample",
                "Sample",
                string.Join('+', segments),
                new ResolvableTypeReference(
                    new TypeReferenceOrigin.CurrentAssembly(),
                    name));
        }

        TypeRef legacy =
            TypeRef.Definition("Sample", "Sample", "Widget");
        TypeRef exact = Exact("Widget");

        Assert.Equal(legacy, exact);
        Assert.Equal(legacy.GetHashCode(), exact.GetHashCode());
        Assert.Equal(
            GenericMemberIdentity.KeyFragment(legacy),
            GenericMemberIdentity.KeyFragment(exact));
        Assert.Equal(
            0,
            MeasureEqualityAllocations(legacy, exact));
        Assert.Equal(
            0,
            MeasureHashAllocations(exact));
        Assert.NotEqual(
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Outer+Inner"),
            Exact("Outer+Inner"));
        Assert.NotEqual(
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Outer+Inner"),
            Exact("Outer", "Inner"));

        TypeRef owner =
            TypeRef.Definition("Sample", "Sample", "Owner");
        var method = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "M",
            [exact],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            true);
        var call = new DirectCall(
            method,
            new MemberRef(
                owner,
                "M",
                [legacy],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);

        Assert.Equal(
            0x06000001,
            MethodDefinitionMap.Create([method]).Resolve(call));
    }

    [Fact]
    public void MethodDefinitionMap_ConversionFallbackUsesReturnType()
    {
        TypeRef owner =
            TypeRef.Definition("Sample", "Sample", "Value");
        TypeRef libraryAResult =
            TypeRef.Definition("LibraryA", "Shared", "Result");
        TypeRef libraryBResult =
            TypeRef.Definition("LibraryB", "Shared", "Result");
        var toLibraryA = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "op_Explicit",
            [owner],
            libraryAResult,
            0x06000001,
            true);
        var toLibraryB = toLibraryA with
        {
            ReturnType = libraryBResult,
            MetadataToken = 0x06000002,
        };
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000003,
            true);
        var libraryACall = new DirectCall(
            caller,
            new MemberRef(
                owner,
                "op_Explicit",
                [owner],
                libraryAResult,
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);
        var libraryBCall = libraryACall with
        {
            Callee = libraryACall.Callee with
            {
                ReturnType = libraryBResult,
            },
            ILOffset = 1,
            OperandToken = 0x0A000002,
            CalleeDefinitionToken = 0x0A000002,
        };
        MethodDefinitionMap map =
            MethodDefinitionMap.Create(
                [toLibraryA, toLibraryB, caller]);

        Assert.Equal(
            toLibraryA.MetadataToken,
            map.Resolve(libraryACall));
        Assert.Equal(
            toLibraryB.MetadataToken,
            map.Resolve(libraryBCall));
    }

    [Fact]
    public void MethodDefinitionMap_ExactFallbackRejectsAmbiguity()
    {
        TypeRef owner =
            TypeRef.Definition("Sample", "Sample", "Value");
        var method = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "M",
            [],
            TypeRef.CoreLib("System", "Int32"),
            0x06000001,
            true);
        var duplicate = method with
        {
            MetadataToken = 0x06000002,
        };
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000003,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                owner,
                "M",
                [],
                TypeRef.CoreLib("System", "Int32"),
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);

        Assert.Equal(
            0,
            MethodDefinitionMap.Create(
                [method, duplicate, caller])
                .Resolve(call));
    }

    [Fact]
    public void MethodDefinitionMap_ConstructedGenericFallbackPreservesReturnAssembly()
    {
        TypeRef owner =
            TypeRef.Definition("Sample", "Sample", "Box`1");
        TypeRef closedOwner =
            TypeRef.GenericInstance(
                owner,
                [TypeRef.CoreLib("System", "Int32")]);
        TypeRef libraryAResult =
            TypeRef.Definition("LibraryA", "Shared", "Result");
        TypeRef libraryBResult =
            TypeRef.Definition("LibraryB", "Shared", "Result");
        var getA = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Get",
            [],
            libraryAResult,
            0x06000001,
            true);
        var getB = getA with
        {
            ReturnType = libraryBResult,
            MetadataToken = 0x06000002,
        };
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000003,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                closedOwner,
                "Get",
                [],
                libraryBResult,
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);

        Assert.Equal(
            getB.MetadataToken,
            MethodDefinitionMap.Create([getA, getB, caller])
                .Resolve(call));
    }

    [Fact]
    public void
        MethodDefinitionMap_ConstructedGenericVarargFallbackUsesRequiredPrefix()
    {
        TypeRef owner =
            TypeRef.Definition("Sample", "Sample", "Box`1");
        TypeRef closedOwner =
            TypeRef.GenericInstance(
                owner,
                [TypeRef.CoreLib("System", "Int32")]);
        var target = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Route",
            [TypeRef.GenericParameter(0, "T")],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            true)
        {
            SignatureHeader = 0x05,
            RequiredParameterCount = 1,
        };
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000002,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                closedOwner,
                "Route",
                [
                    TypeRef.CoreLib("System", "Int32"),
                    TypeRef.CoreLib("System", "String"),
                ],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method)
            {
                SignatureHeader = 0x05,
                RequiredParameterCount = 1,
                OpenParameterTypes =
                [
                    TypeRef.GenericParameter(0, "T"),
                    TypeRef.CoreLib("System", "String"),
                ],
            },
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);

        Assert.Equal(
            target.MetadataToken,
            MethodDefinitionMap.Create([target, caller])
                .Resolve(call));
    }

    [Fact]
    public void
        FunctionPointerSignatureIdentity_PreservesConventionModifiers()
    {
        TypeRef integer =
            TypeRef.CoreLib("System", "Int32");
        TypeRef suppressGcTransition = TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "CallConvSuppressGCTransition");
        TypeRef modifiedReturn = TypeRef.UnsupportedModified(
            suppressGcTransition,
            integer,
            isRequired: false);
        TypeRef pointer = TypeRef.UnsupportedFunctionPointer(
            new MethodSignature<TypeRef>(
                new SignatureHeader(
                    SignatureKind.Method,
                    SignatureCallingConvention.CDecl,
                    SignatureAttributes.None),
                modifiedReturn,
                requiredParameterCount: 0,
                genericParameterCount: 0,
                []));

        Assert.True(
            pointer.TryGetFunctionPointerSignatureIdentity(
                out string identity));
        Assert.Equal(
            "delegate* unmanaged[Cdecl, SuppressGCTransition]"
                + "<int>",
            identity);
    }

    [Fact]
    public void
        FunctionPointerSignatureIdentity_PreservesUnsupportedModifiers()
    {
        TypeRef integer =
            TypeRef.CoreLib("System", "Int32");
        TypeRef modifier = TypeRef.Definition(
            "Sample",
            "Sample",
            "CustomModifier");
        TypeRef modifiedReturn = TypeRef.UnsupportedModified(
            modifier,
            integer,
            isRequired: true);
        TypeRef pointer = TypeRef.UnsupportedFunctionPointer(
            new MethodSignature<TypeRef>(
                new SignatureHeader(
                    SignatureKind.Method,
                    SignatureCallingConvention.CDecl,
                    SignatureAttributes.None),
                modifiedReturn,
                requiredParameterCount: 0,
                genericParameterCount: 0,
                []));

        Assert.True(
            pointer.TryGetFunctionPointerSignatureIdentity(
                out string identity));
        Assert.Equal(
            "delegate* unmanaged[Cdecl]"
                + "<modreq(Sample.CustomModifier)int>",
            identity);

        TypeRef modifiedPointer = TypeRef.UnsupportedModified(
            modifier,
            pointer,
            isRequired: false);
        Assert.True(
            modifiedPointer.TryGetFunctionPointerSignatureIdentity(
                out string modifiedIdentity));
        Assert.Equal(
            "modopt(Sample.CustomModifier)"
                + "delegate* unmanaged[Cdecl]"
                + "<modreq(Sample.CustomModifier)int>",
            modifiedIdentity);
    }

    [Fact]
    public void
        FunctionPointerSignatureIdentity_DoesNotPartiallyNormalizeModifiers()
    {
        TypeRef integer = TypeRef.CoreLib("System", "Int32");
        TypeRef suppressGcTransition = TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "CallConvSuppressGCTransition");
        TypeRef unsupported = TypeRef.Definition(
            "Sample",
            "Probe",
            "Marker");
        TypeRef modifiedReturn = TypeRef.UnsupportedModified(
            suppressGcTransition,
            TypeRef.UnsupportedModified(
                unsupported,
                integer,
                isRequired: false),
            isRequired: false);
        TypeRef pointer = TypeRef.UnsupportedFunctionPointer(
            new MethodSignature<TypeRef>(
                new SignatureHeader(
                    SignatureKind.Method,
                    SignatureCallingConvention.Unmanaged,
                    SignatureAttributes.None),
                modifiedReturn,
                requiredParameterCount: 0,
                genericParameterCount: 0,
                []));

        Assert.True(
            pointer.TryGetFunctionPointerSignatureIdentity(
                out string identity));
        Assert.Equal(
            "delegate* unmanaged"
                + "<modopt(System.Runtime.CompilerServices"
                + ".CallConvSuppressGCTransition)"
                + "modopt(Probe.Marker)int>",
            identity);
    }

    [Theory]
    [InlineData("CallConvCdecl")]
    [InlineData("CallConvSuppressGCTransition")]
    public void
        FunctionPointerSignatureIdentity_DoesNotNormalizeCoreLibraryLookalikes(
            string modifierName)
    {
        TypeRef integer = TypeRef.CoreLib("System", "Int32");
        TypeRef lookalike = TypeRef.Definition(
            "Sample",
            "System.Runtime.CompilerServices",
            modifierName);
        TypeRef modifiedReturn = TypeRef.UnsupportedModified(
            lookalike,
            integer,
            isRequired: false);
        TypeRef pointer = TypeRef.UnsupportedFunctionPointer(
            new MethodSignature<TypeRef>(
                new SignatureHeader(
                    SignatureKind.Method,
                    SignatureCallingConvention.Unmanaged,
                    SignatureAttributes.None),
                modifiedReturn,
                requiredParameterCount: 0,
                genericParameterCount: 0,
                []));

        Assert.True(
            pointer.TryGetFunctionPointerSignatureIdentity(
                out string identity));
        Assert.Equal(
            "delegate* unmanaged"
                + $"<modopt(System.Runtime.CompilerServices.{modifierName})"
                + "int>",
            identity);
    }

    [Fact]
    public void
        FunctionPointerSignatureIdentity_PreservesSignatureHeaderStructure()
    {
        TypeRef integer = TypeRef.CoreLib("System", "Int32");

        static TypeRef Pointer(
            TypeRef returnType,
            SignatureAttributes attributes,
            int requiredParameterCount,
            int genericParameterCount,
            ImmutableArray<TypeRef> parameters,
            SignatureCallingConvention callingConvention =
                SignatureCallingConvention.CDecl)
            => TypeRef.UnsupportedFunctionPointer(
                new MethodSignature<TypeRef>(
                    new SignatureHeader(
                        SignatureKind.Method,
                        callingConvention,
                        attributes),
                    returnType,
                    requiredParameterCount,
                    genericParameterCount,
                    parameters));

        TypeRef instance = Pointer(
            integer,
            SignatureAttributes.Instance,
            requiredParameterCount: 0,
            genericParameterCount: 0,
            []);
        TypeRef generic = Pointer(
            integer,
            SignatureAttributes.Generic,
            requiredParameterCount: 0,
            genericParameterCount: 2,
            []);
        TypeRef explicitInstance = Pointer(
            integer,
            SignatureAttributes.Instance
                | SignatureAttributes.ExplicitThis,
            requiredParameterCount: 0,
            genericParameterCount: 0,
            []);
        TypeRef vararg = Pointer(
            integer,
            SignatureAttributes.None,
            requiredParameterCount: 1,
            genericParameterCount: 0,
            [integer, integer],
            SignatureCallingConvention.VarArgs);

        Assert.True(
            instance.TryGetFunctionPointerSignatureIdentity(
                out string instanceIdentity));
        Assert.True(
            generic.TryGetFunctionPointerSignatureIdentity(
                out string genericIdentity));
        Assert.True(
            explicitInstance.TryGetFunctionPointerSignatureIdentity(
                out string explicitInstanceIdentity));
        Assert.True(
            vararg.TryGetFunctionPointerSignatureIdentity(
                out string varargIdentity));
        Assert.Equal(
            "delegate* unmanaged[Cdecl]{flags=0x20}<int>",
            instanceIdentity);
        Assert.Equal(
            "delegate* unmanaged[Cdecl]{flags=0x10;generic=2}<int>",
            genericIdentity);
        Assert.Equal(
            "delegate* unmanaged[Cdecl]{flags=0x60}<int>",
            explicitInstanceIdentity);
        Assert.Equal(
            "delegate* unmanaged{calling=0x05;required=1}<int,int,int>",
            varargIdentity);
    }

    [Fact]
    public void MethodDefinitionMap_VarArgFallbackMatchesRequiredPrefix()
    {
        TypeRef owner =
            TypeRef.Definition("Sample", "Sample", "Owner");
        TypeRef integer =
            TypeRef.CoreLib("System", "Int32");
        TypeRef voidType =
            TypeRef.CoreLib("System", "Void");
        var target = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Write",
            [integer],
            voidType,
            0x06000001,
            true)
        {
            SignatureHeader = 0x05,
            RequiredParameterCount = 1,
        };
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            voidType,
            0x06000002,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                owner,
                "Write",
                [integer, integer],
                voidType,
                MemberKind.Method)
            {
                SignatureHeader = 0x05,
                RequiredParameterCount = 1,
            },
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);

        Assert.Equal(
            target.MetadataToken,
            MethodDefinitionMap.Create([target, caller])
                .Resolve(call));
    }

    [Fact]
    public void
        MethodDefinitionMap_ConversionParametersUseExactFunctionPointerIdentity()
    {
        TypeRef owner =
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Owner");
        TypeRef integer =
            TypeRef.CoreLib("System", "Int32");
        TypeRef FunctionPointer(
            SignatureCallingConvention callingConvention) =>
            TypeRef.UnsupportedFunctionPointer(
                new MethodSignature<TypeRef>(
                    new SignatureHeader(
                        SignatureKind.Method,
                        callingConvention,
                        SignatureAttributes.None),
                    TypeRef.CoreLib(
                        "System",
                        "Void"),
                    requiredParameterCount: 0,
                    genericParameterCount: 0,
                    []));
        TypeRef cdecl =
            FunctionPointer(
                SignatureCallingConvention.CDecl);
        TypeRef stdcall =
            FunctionPointer(
                SignatureCallingConvention.StdCall);
        var first = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "op_Implicit",
            [cdecl],
            integer,
            0x06000001,
            true);
        var second = first with
        {
            ParameterTypes = [stdcall],
            MetadataToken = 0x06000002,
        };
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            integer,
            0x06000003,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                owner,
                "op_Implicit",
                [stdcall],
                integer,
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);

        Assert.Equal(
            second.MetadataToken,
            MethodDefinitionMap.Create(
                    [first, second, caller])
                .Resolve(call));
    }

    [Fact]
    public void MethodDefinitionMap_ExactFallbackRequiresLocalTypeScope()
    {
        AssemblyReferenceIdentity currentAssembly = new(
            "Sample",
            new Version(1, 0, 0, 0),
            null,
            null);
        MetadataTypeDefinitionName exactName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Value"]))
            .Name;
        TypeRef localOwner = TypeRef.Definition(
            "Sample",
            "Sample",
            "Value",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(
                    currentAssembly),
                exactName));
        TypeRef selfReference = TypeRef.Definition(
            "Sample",
            "Sample",
            "Value",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    currentAssembly),
                exactName));
        TypeRef externalCollision = TypeRef.Definition(
            "Sample",
            "Sample",
            "Value",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    currentAssembly with
                    {
                        Version = new Version(2, 0, 0, 0),
                        PublicKeyToken = "0123456789abcdef",
                    }),
                exactName));
        var target = new MethodIdentity(
            "Sample",
            Guid.Empty,
            localOwner,
            "Route",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            true);
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            localOwner,
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000002,
            true);
        MethodDefinitionMap map =
            MethodDefinitionMap.Create([target, caller]);

        Assert.Equal(
            target.MetadataToken,
            map.Resolve(Call(selfReference)));
        Assert.Equal(
            0,
            map.Resolve(Call(externalCollision)));

        DirectCall Call(TypeRef declaringType) =>
            new(
                caller,
                new MemberRef(
                    declaringType,
                    "Route",
                    [],
                    TypeRef.CoreLib("System", "Void"),
                    MemberKind.Method),
                0,
                0x0A000001,
                0x0A000001,
                CallKind.Call);
    }

    [Fact]
    public void
        MethodDefinitionMap_ExactFallbackRequiresExactSignatureTypeScope()
    {
        AssemblyReferenceIdentity currentAssembly = new(
            "Sample",
            new Version(1, 0, 0, 0),
            null,
            null);
        MetadataTypeDefinitionName ownerName = Name(
            "Sample",
            "Owner");
        MetadataTypeDefinitionName argumentName = Name(
            "Sample",
            "Argument");
        TypeRef owner = Definition(
            ownerName,
            new TypeReferenceOrigin.CurrentAssembly(
                currentAssembly));
        TypeRef localArgument = Definition(
            argumentName,
            new TypeReferenceOrigin.CurrentAssembly(
                currentAssembly));
        TypeRef selfArgument = Definition(
            argumentName,
            new TypeReferenceOrigin.AssemblyReference(
                currentAssembly));
        TypeRef externalArgument = Definition(
            argumentName,
            new TypeReferenceOrigin.AssemblyReference(
                currentAssembly with
                {
                    Version = new Version(2, 0, 0, 0),
                    PublicKeyToken = "0123456789abcdef",
                }));
        var target = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Route",
            [TypeRef.SzArray(localArgument)],
            localArgument,
            0x06000001,
            true);
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000002,
            true);
        var externalParameterCallee = new MemberRef(
            owner,
            "Route",
            [TypeRef.SzArray(externalArgument)],
            selfArgument,
            MemberKind.Method);
        var selfCallee = new MemberRef(
            owner,
            "Route",
            [TypeRef.SzArray(selfArgument)],
            selfArgument,
            MemberKind.Method);
        var externalReturnCallee = new MemberRef(
            owner,
            "Route",
            [TypeRef.SzArray(selfArgument)],
            externalArgument,
            MemberKind.Method);
        MethodDefinitionMap map =
            MethodDefinitionMap.Create([target, caller]);

        Assert.Equal(
            target.MetadataToken,
            map.Resolve(
                new DirectCall(
                    caller,
                    selfCallee,
                    0,
                    0x0A000001,
                    0x0A000001,
                    CallKind.Call)));
        Assert.Equal(
            0,
            map.Resolve(
                new DirectCall(
                    caller,
                    externalParameterCallee,
                    0,
                    0x0A000001,
                    0x0A000001,
                    CallKind.Call)));
        Assert.Equal(
            0,
            map.Resolve(
                new DirectCall(
                    caller,
                    externalReturnCallee,
                    0,
                    0x0A000001,
                    0x0A000001,
                    CallKind.Call)));

        static MetadataTypeDefinitionName Name(
            string ns,
            string name) =>
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    ns,
                    [name]))
            .Name;

        static TypeRef Definition(
            MetadataTypeDefinitionName name,
            TypeReferenceOrigin origin) =>
            TypeRef.Definition(
                "Sample",
                name.Namespace,
                name.Segments[0],
                new ResolvableTypeReference(
                    origin,
                    name));
    }

    [Fact]
    public void
        UnsafeLeverage_AmbiguousFallbackDoesNotSelectUnsafeSubset()
    {
        TypeRef owner =
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Owner");
        var unsafeTarget = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Route",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            true,
            CallerUnsafeMode:
                CallerUnsafeMode.Explicit);
        var safeCollision = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Route",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000002,
            true);
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000003,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                owner,
                "Route",
                [],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);
        MethodDefinitionMap map =
            MethodDefinitionMap.Create(
                [unsafeTarget, safeCollision, caller]);

        UnsafeMethodLeverage leverage = Assert.Single(
            UnsafeLeverage.Top(
                [call],
                [unsafeTarget],
                count: 1,
                methodMap: map));

        Assert.Equal(0, leverage.DirectCallerCount);
    }

    [Fact]
    public void
        MethodDefinitionMap_MalformedUnsupportedSignatureDoesNotBind()
    {
        TypeRef owner =
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Owner");
        TypeRef unsupported =
            TypeRef.Unsupported("malformed signature");
        var target = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "M",
            [unsupported],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            true);
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000002,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                owner,
                "M",
                [unsupported],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);

        Assert.Equal(
            0,
            MethodDefinitionMap.Create(
                    [target, caller])
                .Resolve(call));
    }

    [Fact]
    public void
        MethodDefinitionMap_OutOfRangeDeclaringTypeParameterDoesNotBind()
    {
        TypeRef owner =
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Owner`1");
        TypeRef malformed =
            TypeRef.GenericParameter(1);
        var target = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "M",
            [malformed],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            true);
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000002,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                owner,
                "M",
                [malformed],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);

        Assert.Equal(
            0,
            MethodDefinitionMap.Create(
                    [target, caller])
                .Resolve(call));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void
        MethodDefinitionMap_MalformedConstructedDeclaringTypeArityDoesNotBind(
            int argumentCount)
    {
        TypeRef owner =
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Owner`1");
        TypeRef constructed = TypeRef.GenericInstance(
            owner,
            [
                .. Enumerable.Range(0, argumentCount)
                    .Select(_ =>
                        TypeRef.CoreLib(
                            "System",
                            "Int32")),
            ]);
        var target = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "M",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            true);
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000002,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                constructed,
                "M",
                [],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);

        Assert.Equal(
            0,
            MethodDefinitionMap.Create(
                    [target, caller])
                .Resolve(call));
    }

    [Fact]
    public void
        MethodDefinitionMap_DeclaringTypeMethodVariableOutsideCallerScopeDoesNotBind()
    {
        TypeRef owner =
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Target`1");
        TypeRef constructed = TypeRef.GenericInstance(
            owner,
            [TypeRef.MethodGenericParameter(0)]);
        var target = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "M",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            true);
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Caller"),
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000002,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                constructed,
                "M",
                [],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);

        Assert.Equal(
            0,
            MethodDefinitionMap.Create(
                    [target, caller])
                .Resolve(call));
    }

    [Fact]
    public void
        MethodDefinitionMap_AttributedCallUsesPhysicalGenericScope()
    {
        TypeRef owner =
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Target`1");
        TypeRef constructed = TypeRef.GenericInstance(
            owner,
            [TypeRef.MethodGenericParameter(0)]);
        var target = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "M",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            true);
        var projectedCaller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Caller"),
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000002,
            true);
        MethodIdentity physicalCaller =
            projectedCaller with
            {
                Name = "<Call>g__Local|0_0",
                MetadataToken = 0x06000003,
                GenericArity = 1,
                GenericParameterNames = ["T"],
            };
        var call = new DirectCall(
            projectedCaller,
            new MemberRef(
                constructed,
                "M",
                [],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call)
        {
            EvidenceMethod = physicalCaller,
        };

        Assert.Equal(
            target.MetadataToken,
            MethodDefinitionMap.Create(
                    [
                        target,
                        projectedCaller,
                        physicalCaller,
                    ])
                .Resolve(call));
    }

    [Fact]
    public void
        MethodDefinitionMap_LiteralPlusSegmentPreservesDeclaredArity()
    {
        MetadataTypeDefinitionName exactName =
            Assert.IsType<
                MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Outer`1+Target`1"]))
                .Name;
        TypeRef owner = TypeRef.Definition(
            "Sample",
            exactName.Namespace,
            "Outer`1+Target`1",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                exactName));
        TypeRef constructed = TypeRef.GenericInstance(
            owner,
            [TypeRef.CoreLib("System", "Int32")]);
        var target = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "M",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            true);
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Caller"),
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000002,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                constructed,
                "M",
                [],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);

        Assert.Equal(
            target.MetadataToken,
            MethodDefinitionMap.Create(
                    [target, caller])
                .Resolve(call));
    }

    [Fact]
    public void
        MethodLeverage_ResolvesBeforeFilteringBodilessDeclarations()
    {
        TypeRef owner =
            TypeRef.Definition(
                "Sample",
                "Sample",
                "Target");
        var bodyTarget = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "M",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            true);
        MethodIdentity bodilessCollision =
            bodyTarget with
            {
                MetadataToken = 0x06000002,
            };
        var caller = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "Call",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000003,
            true);
        var call = new DirectCall(
            caller,
            new MemberRef(
                owner,
                "M",
                [],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            0,
            0x0A000001,
            0x0A000001,
            CallKind.Call);
        MethodDefinitionMap declarationMap =
            MethodDefinitionMap.Create(
                [bodyTarget, bodilessCollision, caller]);

        MethodLeverage leverage =
            Assert.Single(
                MethodLeverageRanking.Top(
                    [call],
                    [bodyTarget],
                    count: 1,
                    scope: null,
                    maxDepth: 64,
                    methodMap: declarationMap));

        Assert.Equal(
            0,
            leverage.DirectCallerCount);
    }

    [Fact]
    public void MemberPattern_ConversionReturnUsesExactRetainedIdentity()
    {
        TypeRef owner =
            TypeRef.Definition("Sample", "Sample", "Value");
        TypeRef integer = TypeRef.CoreLib("System", "Int32");
        TypeRef modifierA =
            TypeRef.Definition("LibraryA", "Shared", "Marker");
        TypeRef modifierB =
            TypeRef.Definition("LibraryB", "Shared", "Marker");
        TypeRef returnA =
            TypeRef.UnsupportedModified(
                modifierA,
                TypeRef.MdArray(
                    integer,
                    new ArrayShape(1, [6], [0])),
                isRequired: true);
        TypeRef differentModifier =
            TypeRef.UnsupportedModified(
                modifierB,
                TypeRef.MdArray(
                    integer,
                    new ArrayShape(1, [6], [0])),
                isRequired: true);
        TypeRef differentArrayShape =
            TypeRef.UnsupportedModified(
                modifierA,
                TypeRef.MdArray(
                    integer,
                    new ArrayShape(1, [7], [0])),
                isRequired: true);
        var conversion = new MethodIdentity(
            "Sample",
            Guid.Empty,
            owner,
            "op_Explicit",
            [owner],
            returnA,
            0x06000001,
            true);
        MemberPattern pattern = MemberPattern.Method(conversion);
        MemberRef member = new(
            owner,
            conversion.Name,
            conversion.ParameterTypes,
            returnA,
            MemberKind.Method);

        Assert.True(pattern.Matches(member));
        Assert.False(
            pattern.Matches(
                member with { ReturnType = differentModifier }));
        Assert.False(
            pattern.Matches(
                member with { ReturnType = differentArrayShape }));
    }

    [Fact]
    public void GenericMemberKey_DistinguishesLiteralArraySyntaxFromArrayShape()
    {
        static TypeRef Exact(string name)
        {
            MetadataTypeDefinitionName exactName =
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "",
                        [name]))
                .Name;
            return TypeRef.Definition(
                "Sample",
                "",
                name,
                new ResolvableTypeReference(
                    new TypeReferenceOrigin.CurrentAssembly(),
                    exactName));
        }

        Assert.NotEqual(
            GenericMemberIdentity.KeyFragment(Exact("X[]")),
            GenericMemberIdentity.KeyFragment(
                TypeRef.SzArray(Exact("X"))));
    }

    [Fact]
    public void AsyncSiblingExactIdentity_DistinguishesOriginsWithinSharedDag()
    {
        MetadataTypeDefinitionName name =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Value"]))
            .Name;
        static TypeRef ReferencedType(
            Version version,
            MetadataTypeDefinitionName name)
        {
            var assembly = new AssemblyReferenceIdentity(
                "Dependency",
                version,
                null,
                null);
            return TypeRef.Definition(
                assembly.Name,
                name.Namespace,
                name.Segments[0],
                new ResolvableTypeReference(
                    new TypeReferenceOrigin
                        .AssemblyReference(assembly),
                    name));
        }

        TypeRef versionOne =
            ReferencedType(new Version(1, 0), name);
        TypeRef versionTwo =
            ReferencedType(new Version(2, 0), name);
        TypeRef pair =
            TypeRef.Definition("Sample", "Sample", "Pair`2");
        TypeRef shared = TypeRef.GenericInstance(
            pair,
            [versionOne, versionOne]);
        TypeRef mixed = TypeRef.GenericInstance(
            pair,
            [versionOne, versionTwo]);

        Assert.Equal(versionOne, versionTwo);
        Assert.NotEqual(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypeIdentity(shared),
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypeIdentity(mixed));
    }

    [Fact]
    public void AsyncSiblingFindingDisplay_RejectsExponentialDagExpansion()
    {
        TypeRef value = TypeRef.CoreLib("System", "Int32");
        TypeRef pair =
            TypeRef.Definition("Sample", "Sample", "Pair`2");
        for (int depth = 0; depth < 30; depth++)
            value = TypeRef.GenericInstance(pair, [value, value]);

        var exception = Assert.Throws<BadImageFormatException>(
            () => LibraryBodyAsyncSiblingSignatureMatcher
                .EnsureAsyncSiblingDisplayIsBounded(value));
        Assert.Contains(
            "output limit",
            exception.Message);
    }

    [Fact]
    public void AsyncSiblingFindingDisplay_AcceptsWideFlatSignature()
    {
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        var member = new MemberRef(
            TypeRef.Definition("Sample", "Sample", "Api"),
            "Read",
            [.. Enumerable.Repeat(int32, 256)],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);

        LibraryBodyAsyncSiblingSignatureMatcher
            .EnsureAsyncSiblingDisplayIsBounded(member);
    }

    [Fact]
    public void AsyncSiblingFindingDisplay_BoundsAggregateMemberText()
    {
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        var member = new MemberRef(
            TypeRef.Definition("Sample", "Sample", "Api"),
            "Read",
            [.. Enumerable.Repeat(int32, 5_000)],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);

        Assert.Throws<BadImageFormatException>(
            () => LibraryBodyAsyncSiblingSignatureMatcher
                .EnsureAsyncSiblingDisplayIsBounded(member));
    }

    [Fact]
    public void AsyncSiblingIdentityAndMatching_DistinguishArrayShape()
    {
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        TypeRef baseline = TypeRef.MdArray(
            int32,
            new ArrayShape(
                1,
                [6],
                [0]));
        TypeRef differentSize = TypeRef.MdArray(
            int32,
            new ArrayShape(
                1,
                [7],
                [0]));
        TypeRef differentLowerBound = TypeRef.MdArray(
            int32,
            new ArrayShape(
                1,
                [6],
                [1]));

        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingTypesMatch(
                baseline,
                differentSize));
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingTypesMatch(
                baseline,
                differentLowerBound));
        Assert.NotEqual(
            LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingTypeIdentity(
                baseline),
            LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingTypeIdentity(
                differentSize));
        Assert.NotEqual(
            LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingTypeIdentity(
                baseline),
            LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingTypeIdentity(
                differentLowerBound));
    }

    [Fact]
    public void AsyncSiblingFindingDisplay_RejectsExcessiveArrayRank()
    {
        TypeRef array = TypeRef.MdArray(
            TypeRef.CoreLib("System", "Int32"),
            rank: 1_000_000);

        Assert.Throws<BadImageFormatException>(
            () => LibraryBodyAsyncSiblingSignatureMatcher
                .EnsureAsyncSiblingDisplayIsBounded(array));
    }

    [Fact]
    public void AsyncSiblingFindingDisplay_AccumulatesNestedArrayRanks()
    {
        TypeRef array = TypeRef.CoreLib("System", "Int32");
        for (int depth = 0; depth < 200; depth++)
            array = TypeRef.MdArray(array, rank: 60_000);

        Assert.Throws<BadImageFormatException>(
            () => LibraryBodyAsyncSiblingSignatureMatcher
                .EnsureAsyncSiblingDisplayIsBounded(array));
    }

    [Fact]
    public void AsyncSiblingTypeSupport_IsLinearForSharedDag()
    {
        TypeRef value = TypeRef.CoreLib("System", "Int32");
        TypeRef pair =
            TypeRef.Definition("Sample", "Sample", "Pair`2");
        for (int depth = 0; depth < 30; depth++)
            value = TypeRef.GenericInstance(pair, [value, value]);

        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .IsSupportedAsyncSiblingType(value));
    }

    [Fact]
    public void AsyncSiblingTypeMatching_DistinguishesStructuredNames()
    {
        var assembly = new AssemblyReferenceIdentity(
            "Dependency",
            new Version(1, 0),
            null,
            null);
        MetadataTypeDefinitionName literal =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Probe",
                    ["A+B"]))
            .Name;
        MetadataTypeDefinitionName nested =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Probe",
                    ["A", "B"]))
            .Name;
        static TypeRef Create(
            AssemblyReferenceIdentity assembly,
            MetadataTypeDefinitionName name) =>
            TypeRef.Definition(
                assembly.Name,
                name.Namespace,
                name.ToNestedMetadataName(),
                new ResolvableTypeReference(
                    new TypeReferenceOrigin
                        .AssemblyReference(assembly),
                    name));

        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingTypesMatch(
                Create(assembly, literal),
                Create(assembly, nested)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long MeasureEqualityAllocations(
        TypeRef left,
        TypeRef right)
    {
        bool result = false;
        return MeasureSteadyStateAllocations(
            () => result ^= left.Equals(right),
            () => Consume(result));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long MeasureHashAllocations(TypeRef type)
    {
        int result = 0;
        return MeasureSteadyStateAllocations(
            () => result ^= type.GetHashCode(),
            () => Consume(result));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long MeasureSteadyStateAllocations(
        Action operation,
        Action consume)
    {
        const int WarmupIterations = 10_000;
        const int MeasurementIterations = 10_000;
        const int NoGcRegionBudget = 4 * 1024 * 1024;
        const int MaximumAttempts = 3;

        for (int i = 0; i < WarmupIterations; i++)
            operation();

        // A GC suspension can retire the current thread's allocation context
        // and inflate its counter. Accept only a sample whose no-GC region held.
        string failure = "No attempt was made.";
        for (int attempt = 0;
            attempt < MaximumAttempts;
            attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (TryMeasureWithoutGc(
                    operation,
                    consume,
                    MeasurementIterations,
                    NoGcRegionBudget,
                    out long allocated,
                    out failure))
            {
                return allocated;
            }
        }

        throw new InvalidOperationException(
            $"Unable to establish a stable no-GC allocation "
                + $"measurement after {MaximumAttempts} attempts. "
                + $"Last failure: {failure}");
    }

    static bool TryMeasureWithoutGc(
        Action operation,
        Action consume,
        int iterations,
        long noGcRegionBudget,
        out long allocated,
        out string failure)
    {
        allocated = 0;
        failure = "";

        try
        {
            if (!GC.TryStartNoGCRegion(noGcRegionBudget))
            {
                failure = "The runtime declined the no-GC region.";
                return false;
            }
        }
        catch (InvalidOperationException ex)
        {
            failure = ex.Message;
            return false;
        }

        ExceptionDispatchInfo? operationFailure = null;
        try
        {
            long before =
                GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iterations; i++)
                operation();
            allocated =
                GC.GetAllocatedBytesForCurrentThread() - before;
            consume();
        }
        catch (Exception ex)
        {
            operationFailure =
                ExceptionDispatchInfo.Capture(ex);
        }

        bool regionHeld =
            GCSettings.LatencyMode == GCLatencyMode.NoGCRegion;
        string? endFailure = null;
        if (regionHeld)
        {
            try
            {
                GC.EndNoGCRegion();
            }
            catch (InvalidOperationException ex)
            {
                regionHeld = false;
                endFailure = ex.Message;
            }
        }

        operationFailure?.Throw();
        if (regionHeld)
            return true;

        failure = endFailure
            ?? "The no-GC region ended during the measurement.";
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Consume<T>(T value) { }

    static readonly TypeRef DeclaringType = TypeRef.Definition(
        "Example",
        "Example",
        "Container");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    [Fact]
    public void MethodIdentity_UsesOrderedSequenceEquality()
    {
        var first = Method(
            ImmutableArray.Create(Int32, Int32, String),
            ImmutableArray.Create("T", "U"));
        var equivalent = Method(
            ImmutableArray.Create(Int32, Int32, String),
            ImmutableArray.Create("T", "U"));
        var reordered = Method(
            ImmutableArray.Create(String, Int32, Int32),
            ImmutableArray.Create("T", "U"));
        var differentDuplicates = Method(
            ImmutableArray.Create(Int32, String, String),
            ImmutableArray.Create("T", "U"));
        MethodIdentity differentHeader = first with
        {
            SignatureHeader = 0x05,
        };
        MethodIdentity capturedNonVarargCount = first with
        {
            SignatureHeader = 0x10,
            RequiredParameterCount = first.ParameterTypes.Length,
        };
        MethodIdentity vararg = first with
        {
            SignatureHeader = 0x05,
            RequiredParameterCount = 1,
        };
        MethodIdentity differentRequiredCount = vararg with
        {
            RequiredParameterCount = 2,
        };
        MethodIdentity invalidGenericDeclaration = first with
        {
            HasInvalidGenericParameterDeclaration = true,
        };

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(first, capturedNonVarargCount);
        Assert.Equal(
            first.GetHashCode(),
            capturedNonVarargCount.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.NotEqual(first, differentDuplicates);
        Assert.NotEqual(first, differentHeader);
        Assert.NotEqual(first, differentRequiredCount);
        Assert.NotEqual(first, invalidGenericDeclaration);
    }

    [Fact]
    public void MethodIdentity_NormalizesOmittedGenericNamesAndRejectsInvalidParameters()
    {
        var omitted = Method([]);
        var explicitEmpty = Method([], []);

        Assert.False(omitted.GenericParameterNames.IsDefault);
        Assert.Equal(omitted, explicitEmpty);
        Assert.Throws<ArgumentException>(() => Method(default));
        Assert.Throws<ArgumentException>(() => Method([null!]));
        Assert.Throws<ArgumentException>(
            () => omitted with { GenericParameterNames = [null!] });
    }

    [Fact]
    public void MemberRef_ComposesAllOrderedCollectionProperties()
    {
        var first = Member(
            ImmutableArray.Create(Int32, String),
            ImmutableArray.Create(String),
            ImmutableArray.Create(Int32));
        var equivalent = Member(
            ImmutableArray.Create(Int32, String),
            ImmutableArray.Create(String),
            ImmutableArray.Create(Int32));
        var reordered = Member(
            ImmutableArray.Create(String, Int32),
            ImmutableArray.Create(String),
            ImmutableArray.Create(Int32));
        var differentTypeArguments = Member(
            ImmutableArray.Create(Int32, String),
            ImmutableArray.Create(Int32),
            ImmutableArray.Create(Int32));
        var differentOpenParameters = Member(
            ImmutableArray.Create(Int32, String),
            ImmutableArray.Create(String),
            ImmutableArray.Create(String));
        MemberRef capturedNonVarargCount = first with
        {
            RequiredParameterCount = first.ParameterTypes.Length,
        };
        MemberRef vararg = first with
        {
            SignatureHeader = 0x05,
            RequiredParameterCount = 1,
        };
        MemberRef differentRequiredCount = vararg with
        {
            RequiredParameterCount = 2,
        };

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(first, capturedNonVarargCount);
        Assert.Equal(
            first.GetHashCode(),
            capturedNonVarargCount.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.NotEqual(first, differentTypeArguments);
        Assert.NotEqual(first, differentOpenParameters);
        Assert.NotEqual(first, differentRequiredCount);
    }

    [Fact]
    public void MemberRef_RejectsDefaultCollectionsIncludingWithExpressions()
    {
        Assert.Throws<ArgumentException>(() => Member(default, [], []));

        var member = Member([], [], []);
        Assert.Throws<ArgumentException>(
            () => member with { ParameterTypes = default });
        Assert.Throws<ArgumentException>(
            () => member with { TypeArguments = default });
        Assert.Throws<ArgumentException>(
            () => member with { OpenParameterTypes = default });
    }

    [Fact]
    public void DirectCall_ComposesMemberIdentityValueEquality()
    {
        var first = new DirectCall(
            Method(ImmutableArray.Create(Int32, String)),
            Member(
                ImmutableArray.Create(Int32),
                ImmutableArray.Create(String),
                ImmutableArray.Create(Int32)),
            ILOffset: 3,
            OperandToken: 0x0a000001,
            CalleeDefinitionToken: 0x06000002,
            CallKind.Call)
        {
            Opcode = "call",
        };
        var equivalent = new DirectCall(
            Method(ImmutableArray.Create(Int32, String)),
            Member(
                ImmutableArray.Create(Int32),
                ImmutableArray.Create(String),
                ImmutableArray.Create(Int32)),
            ILOffset: 3,
            OperandToken: 0x0a000001,
            CalleeDefinitionToken: 0x06000002,
            CallKind.Call)
        {
            Opcode = "call",
        };

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
    }

    static MethodIdentity Method(
        ImmutableArray<TypeRef> parameterTypes,
        ImmutableArray<string> genericParameterNames = default)
        => new(
            "Example",
            Guid.Parse("252451b8-cd83-4e7b-b7e6-8f05b6e4900c"),
            DeclaringType,
            "M",
            parameterTypes,
            Void,
            MetadataToken: 0x06000001,
            IsStatic: true,
            GenericArity: genericParameterNames.IsDefault ? 0 : genericParameterNames.Length,
            GenericParameterNames: genericParameterNames);

    static MemberRef Member(
        ImmutableArray<TypeRef> parameterTypes,
        ImmutableArray<TypeRef> typeArguments,
        ImmutableArray<TypeRef> openParameterTypes)
        => new(DeclaringType, "M", parameterTypes, Void, MemberKind.Method)
        {
            TypeArguments = typeArguments,
            HasThis = true,
            SignatureHeader = 0x20,
            GenericArity = typeArguments.Length,
            OpenParameterTypes = openParameterTypes,
            OpenReturnType = String,
        };
}
