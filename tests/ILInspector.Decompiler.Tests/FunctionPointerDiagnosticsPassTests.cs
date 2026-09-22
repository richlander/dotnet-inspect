using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class FunctionPointerDiagnosticsPassTests
{
    private static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "Owner");
    private static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    private static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");

    [Theory]
    [InlineData(nameof(FunctionPointerModifierFixture.SuppressField), "delegate* unmanaged[SuppressGCTransition]<int, int>")]
    [InlineData(nameof(FunctionPointerModifierFixture.InField), "delegate*<in int, void>")]
    [InlineData(nameof(FunctionPointerModifierFixture.OutField), "delegate*<out int, void>")]
    public void TypeRefDecoder_PreservesFunctionPointerSignatureModifiers(string fieldName, string expected)
    {
        var type = DecodeFixtureField(fieldName);

        Assert.Equal(expected, type.ToDisplayString());
    }

    [Theory]
    [InlineData(
        0x20,
        0,
        0,
        0,
        "delegate* unmanaged[Cdecl]{flags=0x20}<System.Object>")]
    [InlineData(
        0x60,
        0,
        0,
        0,
        "delegate* unmanaged[Cdecl]{flags=0x60}<System.Object>")]
    [InlineData(
        0x10,
        2,
        0,
        0,
        "delegate* unmanaged[Cdecl]{flags=0x10;generic=2}<System.Object>")]
    [InlineData(
        0x05,
        0,
        1,
        2,
        "delegate* unmanaged{calling=0x05;required=1}"
            + "<System.Object,System.Object,System.Object>")]
    public void CanonicalFunctionPointerIdentity_PreservesSignatureStructure(
        int signatureDiscriminator,
        int genericParameterCount,
        int requiredParameterCount,
        int parameterCount,
        string expected)
    {
        TypeRef pointer = TypeRef.FunctionPointer(
            Object,
            Enumerable.Repeat(Object, parameterCount).ToImmutableArray(),
            signatureDiscriminator == 0x05
                ? "unmanaged"
                : "unmanaged[Cdecl]",
            callingConventionIsExact: false,
            signatureDiscriminator: (byte)signatureDiscriminator,
            genericParameterCount,
            requiredParameterCount);

        Assert.Equal(expected, CSharpBodyDiff.CanonicalTypeName(pointer));
        Assert.NotEqual(
            TypeRef.FunctionPointer(
                Object,
                Enumerable.Repeat(Object, parameterCount).ToImmutableArray(),
                "unmanaged[Cdecl]"),
            pointer);
    }

    [Fact]
    public void
        TypeRefDecoder_DistinguishesLosslessAndDuplicateConventionModifiers()
    {
        byte[] image =
            FunctionPointerConventionReturnOverloadFixture.Build(
                returnOne: false,
                identityCase:
                    FunctionPointerConventionReturnOverloadFixture.IdentityCase
                        .DuplicateConventionModifier);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        TypeRef[] pointers =
        [
            .. reader.MethodDefinitions
                .Select(handle => reader.GetMethodDefinition(handle))
                .Where(method => reader.GetString(method.Name) == "Changed")
                .Select(method => method.DecodeSignature(
                    TypeRefDecoder.Instance,
                    GenericScope.Empty).ReturnType),
        ];

        Assert.Equal(2, pointers.Length);
        Assert.NotEqual(pointers[0], pointers[1]);
        Assert.Equal(2, pointers.ToHashSet().Count);
        Assert.Equal(
            2,
            pointers.Select(CSharpBodyDiff.CanonicalTypeName)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Theory]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .CoreLibraryLookalikeModifier,
        "delegate* unmanaged[Cdecl]<System.Int32>",
        "delegate* unmanaged"
            + "<modopt(System.Runtime.CompilerServices.CallConvCdecl)"
            + "System.Int32>")]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .CoreLibrarySuppressGcTransitionLookalikeModifier,
        "delegate* unmanaged[SuppressGCTransition]<System.Int32>",
        "delegate* unmanaged"
            + "<modopt(System.Runtime.CompilerServices"
            + ".CallConvSuppressGCTransition)System.Int32>")]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .MixedSuppressGcTransitionModifier,
        "delegate* unmanaged[SuppressGCTransition]<System.Int32>",
        "delegate* unmanaged"
            + "<modopt(System.Runtime.CompilerServices"
            + ".CallConvSuppressGCTransition)"
            + "modopt(Probe.Marker)System.Int32>")]
    public void TypeRefDecoder_PreservesConventionModifierIdentity(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            identityCase,
        string expectedCoreLibraryIdentity,
        string expectedLookalikeIdentity)
    {
        byte[] image =
            FunctionPointerConventionReturnOverloadFixture.Build(
                returnOne: false,
                identityCase);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        string[] identities =
        [
            .. reader.MethodDefinitions
                .Select(handle => reader.GetMethodDefinition(handle))
                .Where(method => reader.GetString(method.Name) == "Changed")
                .Select(method => method.DecodeSignature(
                    TypeRefDecoder.Instance,
                    GenericScope.Empty).ReturnType)
                .Select(CSharpBodyDiff.CanonicalTypeName),
        ];

        Assert.Equal(
            [expectedCoreLibraryIdentity, expectedLookalikeIdentity],
            identities);
    }

    [Fact]
    public void
        TypeRefInstantiation_DoesNotNormalizeSuppressGcTransitionLookalike()
    {
        TypeRef lookalike = TypeRef.Definition(
            "Sample",
            "System.Runtime.CompilerServices",
            "CallConvSuppressGCTransition");
        TypeRef genericReturn = TypeRef.GenericParameter(0)
            .WithCustomModifier(lookalike, isRequired: false);
        TypeRef pointer = TypeRef.FunctionPointer(
            genericReturn,
            [],
            "unmanaged");

        TypeRef instantiated = pointer.Instantiate(
            [TypeRef.CoreLib("System", "Int32")],
            []);

        Assert.Equal(
            "delegate* unmanaged"
                + "<modopt(System.Runtime.CompilerServices"
                + ".CallConvSuppressGCTransition)System.Int32>",
            CSharpBodyDiff.CanonicalTypeName(instantiated));
    }

    [Fact]
    public void CallIndirect_PreservesInOutFunctionPointerArgumentKeywords()
    {
        string output = PrintRaised(nameof(FunctionPointerModifierFixture.InvokeInOut));

        Assert.Contains("input(in value);", output);
        Assert.Contains("output(out", output);
        Assert.DoesNotContain("input(ref value);", output);
        Assert.DoesNotContain("output(ref", output);
    }

    [Fact]
    public void Run_DiagnosesVirtualFunctionPointerAsLdvirtftn()
    {
        var method = new MethodRef(Owner, "VirtualTarget", Void, [], HasThis: true);
        var function = FunctionWith(new LoadFunctionPointer(method, isVirtual: true, new LoadArgument(0, "this", Owner)));

        new FunctionPointerDiagnosticsPass().Run(function, PassContext.None);

        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Equal(DiagnosticIds.UnsupportedFunctionPointer, diagnostic.Id);
        Assert.Contains("ldvirtftn", diagnostic.Message);
        Assert.Contains("requires a receiver", diagnostic.Message);
        Assert.Contains("VirtualTarget", diagnostic.Message);
        Assert.DoesNotContain("ldftn:", diagnostic.Message);
    }

    [Fact]
    public void Run_DiagnosesStaticFunctionPointerAsLdftn()
    {
        var method = new MethodRef(Owner, "StaticTarget", Void, [], HasThis: false);
        var function = FunctionWith(new LoadFunctionPointer(method, isVirtual: false, instance: null));

        new FunctionPointerDiagnosticsPass().Run(function, PassContext.None);

        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Equal(DiagnosticIds.UnsupportedFunctionPointer, diagnostic.Id);
        Assert.Contains("ldftn", diagnostic.Message);
        Assert.Contains("not a delegate construction", diagnostic.Message);
        Assert.Contains("StaticTarget", diagnostic.Message);
        Assert.DoesNotContain("ldvirtftn", diagnostic.Message);
    }

    [Fact]
    public void MethodAddress_PInvokeTarget_StaysDiagnosticResidual()
    {
        var method = new MethodRef(Owner, "NativeTarget", Void, [], HasThis: false)
        {
            IsPInvoke = MetadataFactState.Yes
        };
        var function = FunctionWith(new LoadFunctionPointer(method, isVirtual: false, instance: null));

        new MethodAddressPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<LoadFunctionPointer>());
        Assert.Empty(function.Descendants.OfType<AddressOfMethod>());

        new FunctionPointerDiagnosticsPass().Run(function, PassContext.None);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Equal(DiagnosticIds.UnsupportedFunctionPointer, diagnostic.Id);
        Assert.Contains("P/Invoke target", diagnostic.Message);
        Assert.Contains("NativeTarget", diagnostic.Message);
    }

    [Fact]
    public void MethodAddress_RuntimeAsyncTarget_StaysDiagnosticResidual()
    {
        var method = new MethodRef(Owner, "RuntimeAsyncTarget", Void, [], HasThis: false)
        {
            IsRuntimeAsync = MetadataFactState.Yes
        };
        var function = FunctionWith(new LoadFunctionPointer(method, isVirtual: false, instance: null));

        new MethodAddressPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<LoadFunctionPointer>());
        Assert.Empty(function.Descendants.OfType<AddressOfMethod>());

        new FunctionPointerDiagnosticsPass().Run(function, PassContext.None);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Equal(DiagnosticIds.UnsupportedFunctionPointer, diagnostic.Id);
        Assert.Contains("runtime-async", diagnostic.Message);
        Assert.Contains("RuntimeAsyncTarget", diagnostic.Message);
    }

    [Fact]
    public void MethodAddress_InstanceTarget_StaysDiagnosticResidual()
    {
        var method = new MethodRef(Owner, "InstanceTarget", Void, [], HasThis: true);
        var function = FunctionWith(new LoadFunctionPointer(method, isVirtual: false, instance: null));

        new MethodAddressPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<LoadFunctionPointer>());
        Assert.Empty(function.Descendants.OfType<AddressOfMethod>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);

        new FunctionPointerDiagnosticsPass().Run(function, PassContext.None);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Equal(DiagnosticIds.UnsupportedFunctionPointer, diagnostic.Id);
        Assert.Contains("instance method", diagnostic.Message);
        Assert.Contains("InstanceTarget", diagnostic.Message);
    }

    private static IrFunction FunctionWith(IrExpression expression)
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new ExpressionStatement(expression));
        body.Add(block);

        var signature = new MethodSignature(
            Void,
            ImmutableArray.Create(new ILInspector.Decompiler.Pipeline.Parameter("this", Owner)),
            HasThis: true,
            GenericParameterCount: 0);

        return new IrFunction("M", Owner, signature, [], body);
    }

    static TypeRef DecodeFixtureField(string fieldName)
    {
        using var stream = File.OpenRead(typeof(FunctionPointerModifierFixture).Assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var type = FixtureType(reader);
        foreach (var handle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(handle);
            if (reader.GetString(field.Name) == fieldName)
                return field.DecodeSignature(TypeRefDecoder.Instance, GenericScope.Empty);
        }
        throw new InvalidOperationException($"Field {fieldName} not found.");
    }

    static TypeDefinition FixtureType(MetadataReader reader)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (reader.GetString(type.Name) == nameof(FunctionPointerModifierFixture))
                return type;
        }
        throw new InvalidOperationException("Fixture type not found.");
    }

    static string PrintRaised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(FunctionPointerModifierFixture).Assembly.Location);
        var function = IrImporter.Import(source, typeof(FunctionPointerModifierFixture).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }
}

public unsafe class FunctionPointerModifierFixture
{
    public delegate* unmanaged[SuppressGCTransition]<int, int> SuppressField;
    public delegate*<in int, void> InField;
    public delegate*<out int, void> OutField;

    public static void InvokeInOut(delegate*<in int, void> input, delegate*<out int, void> output, int value)
    {
        input(in value);
        output(out int result);
        _ = result;
    }
}
