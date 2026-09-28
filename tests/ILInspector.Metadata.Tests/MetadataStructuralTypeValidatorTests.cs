using System.Reflection.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataStructuralTypeValidatorTests
{
    static readonly TypeNodeProvider Provider = TypeNodeProvider.Instance;

    [Fact]
    public void PropertySignature_UsesRetTypeAndParamPositions()
    {
        TypeNode int32 = Provider.GetPrimitiveType(PrimitiveTypeCode.Int32);
        var signature = new MethodSignature<TypeNode>(
            new SignatureHeader(0x28),
            Provider.GetPrimitiveType(PrimitiveTypeCode.Void),
            requiredParameterCount: 2,
            genericParameterCount: 0,
            [
                Provider.GetByReferenceType(int32),
                Provider.GetPrimitiveType(PrimitiveTypeCode.TypedReference),
            ]);

        Assert.Null(
            MetadataStructuralTypeValidator.ValidatePropertySignature(
                signature,
                typeParameterCount: 0,
                "The property"));
    }

    [Fact]
    public void PropertySignature_RejectsTypeOnlyFormsInParamPosition()
    {
        var signature = new MethodSignature<TypeNode>(
            new SignatureHeader(0x08),
            Provider.GetPrimitiveType(PrimitiveTypeCode.Int32),
            requiredParameterCount: 1,
            genericParameterCount: 0,
            [Provider.GetPrimitiveType(PrimitiveTypeCode.Void)]);

        Assert.NotNull(
            MetadataStructuralTypeValidator.ValidatePropertySignature(
                signature,
                typeParameterCount: 0,
                "The property"));
    }

    [Fact]
    public void MethodSignature_AcceptsExplicitThisGenericMethodDef()
    {
        TypeNode methodParameter =
            Provider.GetGenericMethodParameter(context: null, index: 0);
        var signature = new MethodSignature<TypeNode>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.Default,
                SignatureAttributes.Instance
                    | SignatureAttributes.ExplicitThis
                    | SignatureAttributes.Generic),
            methodParameter,
            requiredParameterCount: 1,
            genericParameterCount: 1,
            [methodParameter]);

        Assert.Null(
            MetadataStructuralTypeValidator.ValidateMethodSignature(
                signature,
                typeParameterCount: 0,
                methodParameterCount: 1,
                "The method"));
    }

    [Fact]
    public void MethodSignature_AcceptsVarArgsOptionalParameters()
    {
        TypeNode int32 = Provider.GetPrimitiveType(PrimitiveTypeCode.Int32);
        var signature = new MethodSignature<TypeNode>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.VarArgs,
                SignatureAttributes.None),
            Provider.GetPrimitiveType(PrimitiveTypeCode.Void),
            requiredParameterCount: 1,
            genericParameterCount: 0,
            [int32, int32]);

        Assert.Null(
            MetadataStructuralTypeValidator.ValidateMethodSignature(
                signature,
                typeParameterCount: 0,
                methodParameterCount: 0,
                "The method"));
    }

    [Fact]
    public void MethodSignature_RejectsGenericArityMismatch()
    {
        var signature = new MethodSignature<TypeNode>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.Default,
                SignatureAttributes.Generic),
            Provider.GetPrimitiveType(PrimitiveTypeCode.Void),
            requiredParameterCount: 0,
            genericParameterCount: 1,
            []);

        Assert.NotNull(
            MetadataStructuralTypeValidator.ValidateMethodSignature(
                signature,
                typeParameterCount: 0,
                methodParameterCount: 0,
                "The method"));
    }

    [Fact]
    public void MethodSignature_UsesIndependentTypeAndMethodContexts()
    {
        TypeNode typeParameter =
            Provider.GetGenericTypeParameter(context: null, index: 1);
        TypeNode methodParameter =
            Provider.GetGenericMethodParameter(context: null, index: 0);
        var signature = new MethodSignature<TypeNode>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.Default,
                SignatureAttributes.Generic),
            typeParameter,
            requiredParameterCount: 1,
            genericParameterCount: 1,
            [methodParameter]);

        Assert.Null(
            MetadataStructuralTypeValidator.ValidateMethodSignature(
                signature,
                typeParameterCount: 2,
                methodParameterCount: 1,
                "The method"));
        Assert.NotNull(
            MetadataStructuralTypeValidator.ValidateMethodSignature(
                signature,
                typeParameterCount: 1,
                methodParameterCount: 1,
                "The method"));
    }
}
