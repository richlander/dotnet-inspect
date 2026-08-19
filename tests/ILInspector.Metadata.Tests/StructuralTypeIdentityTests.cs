using System.Reflection.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class StructuralTypeIdentityTests
{
    [Fact]
    public void Encoder_DistinguishesModifierKindPinnedAndFunctionPointerHeaders()
    {
        Assert.NotEqual(
            StructuralTypeIdentity.Modified(true, "System.Runtime.CompilerServices.IsExternalInit", "System.Int32"),
            StructuralTypeIdentity.Modified(false, "System.Runtime.CompilerServices.IsExternalInit", "System.Int32"));
        Assert.NotEqual(
            "System.Int32",
            StructuralTypeIdentity.Pinned("System.Int32"));

        string cdecl = StructuralTypeIdentity.FunctionPointer(
            SignatureCallingConvention.CDecl,
            hasThis: false,
            explicitThis: false,
            genericParameterCount: 0,
            requiredParameterCount: 1,
            ["System.Int32"],
            "System.Void");
        Assert.NotEqual(
            cdecl,
            StructuralTypeIdentity.FunctionPointer(
                SignatureCallingConvention.CDecl,
                hasThis: true,
                explicitThis: false,
                genericParameterCount: 0,
                requiredParameterCount: 1,
                ["System.Int32"],
                "System.Void"));
        Assert.NotEqual(
            cdecl,
            StructuralTypeIdentity.FunctionPointer(
                SignatureCallingConvention.CDecl,
                hasThis: true,
                explicitThis: true,
                genericParameterCount: 0,
                requiredParameterCount: 1,
                ["System.Int32"],
                "System.Void"));
        Assert.NotEqual(
            cdecl,
            StructuralTypeIdentity.FunctionPointer(
                SignatureCallingConvention.CDecl,
                hasThis: false,
                explicitThis: false,
                genericParameterCount: 1,
                requiredParameterCount: 1,
                ["System.Int32"],
                "System.Void"));
        Assert.NotEqual(
            cdecl,
            StructuralTypeIdentity.FunctionPointer(
                SignatureCallingConvention.CDecl,
                hasThis: false,
                explicitThis: false,
                genericParameterCount: 0,
                requiredParameterCount: 0,
                ["System.Int32"],
                "System.Void"));
    }

    [Fact]
    public void TypeNode_PreservesErasedPayloadAndLeavesDisplayUnchanged()
    {
        var provider = TypeNodeProvider.Instance;
        TypeNode int32 = provider.GetPrimitiveType(PrimitiveTypeCode.Int32);
        TypeNode modifier = provider.GetPrimitiveType(PrimitiveTypeCode.Object);
        TypeNode required = provider.GetModifiedType(modifier, int32, isRequired: true);
        TypeNode optional = provider.GetModifiedType(modifier, int32, isRequired: false);
        TypeNode pinned = provider.GetPinnedType(int32);

        Assert.Equal("int", required.Render());
        Assert.Equal("int", pinned.Render());
        Assert.Equal(
            StructuralTypeIdentity.Modified(true, "System.Object", "System.Int32"),
            required.StructuralIdentity());
        Assert.Equal(
            StructuralTypeIdentity.Modified(false, "System.Object", "System.Int32"),
            optional.StructuralIdentity());
        Assert.Equal(
            StructuralTypeIdentity.Pinned("System.Int32"),
            pinned.StructuralIdentity());
        Assert.NotEqual(required.StructuralIdentity(), optional.StructuralIdentity());

        var signature = new MethodSignature<TypeNode>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.CDecl,
                SignatureAttributes.Instance),
            provider.GetPrimitiveType(PrimitiveTypeCode.Void),
            requiredParameterCount: 1,
            genericParameterCount: 0,
            [int32]);
        TypeNode functionPointer = provider.GetFunctionPointerType(signature);
        TypeNode methodParameter = provider.GetGenericMethodParameter(context: null, index: 0);
        TypeNode typeParameter = provider.GetGenericTypeParameter(context: null, index: 1);
        Assert.Equal("M0", methodParameter.StructuralIdentity());
        Assert.Equal("T1", typeParameter.StructuralIdentity());
        Assert.False(methodParameter.HasStructuralPayload);

        Assert.Equal("delegate* unmanaged[Cdecl]<int, void>", functionPointer.Render());
        Assert.Equal(
            StructuralTypeIdentity.FunctionPointer(
                SignatureCallingConvention.CDecl,
                hasThis: true,
                explicitThis: false,
                genericParameterCount: 0,
                requiredParameterCount: 1,
                ["System.Int32"],
                "System.Void"),
            functionPointer.StructuralIdentity());
    }

    [Fact]
    public void TypeNode_RecursesPositionalGenericsUnderWrappersAndGenericInstantiations()
    {
        var provider = TypeNodeProvider.Instance;
        TypeNode methodParameter = provider.GetGenericMethodParameter(context: null, index: 0);
        TypeNode typeParameter = provider.GetGenericTypeParameter(context: null, index: 1);
        TypeNode byRef = provider.GetByReferenceType(methodParameter);
        TypeNode required = provider.GetModifiedType(
            new NamedTypeNode("System.Runtime.InteropServices.InAttribute", isReferenceType: false),
            byRef,
            isRequired: true);
        TypeNode list = provider.GetGenericInstantiation(
            new NamedTypeNode("System.Collections.Generic.List`1", isReferenceType: true),
            [methodParameter]);
        TypeNode nested = provider.GetGenericInstantiation(
            new NamedTypeNode("Samples.Outer`1.Inner`1", isReferenceType: true),
            [
                provider.GetPrimitiveType(PrimitiveTypeCode.Int32),
                provider.GetPrimitiveType(PrimitiveTypeCode.String),
            ]);
        TypeNode flat = provider.GetGenericInstantiation(
            new NamedTypeNode("Samples.Outer`2", isReferenceType: true),
            [
                provider.GetPrimitiveType(PrimitiveTypeCode.Int32),
                provider.GetPrimitiveType(PrimitiveTypeCode.String),
            ]);
        var functionPointer = provider.GetFunctionPointerType(
            new MethodSignature<TypeNode>(
                new SignatureHeader(
                    SignatureKind.Method,
                    SignatureCallingConvention.Default,
                    SignatureAttributes.None),
                provider.GetPrimitiveType(PrimitiveTypeCode.Void),
                requiredParameterCount: 1,
                genericParameterCount: 0,
                [list]));

        Assert.Equal("M0@", byRef.StructuralIdentity());
        Assert.Equal("T1", typeParameter.StructuralIdentity());
        Assert.Equal(
            StructuralTypeIdentity.Modified(
                required: true,
                "System.Runtime.InteropServices.InAttribute",
                "M0@"),
            required.StructuralIdentity());
        Assert.Equal("System.Collections.Generic.List{M0}", list.StructuralIdentity());
        Assert.Equal(
            "Samples.Outer{System.Int32}.Inner{System.String}",
            nested.StructuralIdentity());
        Assert.Equal("Samples.Outer{System.Int32,System.String}", flat.StructuralIdentity());
        Assert.NotEqual(nested.StructuralIdentity(), flat.StructuralIdentity());
        Assert.Contains("List{M0}", functionPointer.StructuralIdentity(), StringComparison.Ordinal);
        Assert.DoesNotContain("List{TM0}", functionPointer.StructuralIdentity(), StringComparison.Ordinal);
        Assert.False(list.HasStructuralPayload);
    }
}
