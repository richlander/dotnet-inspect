using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Top-level result gateway for string-producing signature decodes in Metadata.
/// Nested TypeSpec rejection is retained by <see cref="SignatureDecoder"/> and projected as a
/// rejected result rather than a plausible signature value.
/// </summary>
internal static class GuardedSignatureText
{
    public static SignatureDecodeResult<MethodSignature<string>> MethodText(
        MetadataReader reader,
        MethodDefinition method,
        GenericContext? context)
        => GuardedSignatureDecoder.Decode(
            reader,
            method.Signature,
            SignatureBlobGuard.Kind.Method,
            () => method.DecodeSignature(SignatureDecoder.Instance, context));

    public static SignatureDecodeResult<MethodSignature<string>> MemberRefMethodText(
        MetadataReader reader,
        MemberReference member,
        GenericContext? context)
        => GuardedSignatureDecoder.Decode(
            reader,
            member.Signature,
            SignatureBlobGuard.Kind.Method,
            () => member.DecodeMethodSignature(SignatureDecoder.Instance, context));

    public static SignatureDecodeResult<MethodSignature<string>> PropertyText(
        MetadataReader reader,
        PropertyDefinition property,
        GenericContext? context)
        => GuardedSignatureDecoder.Decode(
            reader,
            property.Signature,
            SignatureBlobGuard.Kind.Method,
            () => property.DecodeSignature(SignatureDecoder.Instance, context));

    public static SignatureDecodeResult<string> FieldText(
        MetadataReader reader,
        FieldDefinition field,
        GenericContext? context)
        => GuardedSignatureDecoder.Decode(
            reader,
            field.Signature,
            SignatureBlobGuard.Kind.Field,
            () => field.DecodeSignature(SignatureDecoder.Instance, context));

    public static SignatureDecodeResult<ImmutableArray<string>> MethodSpecTypeArgs(
        MetadataReader reader,
        MethodSpecification specification,
        GenericContext? context)
        => GuardedSignatureDecoder.Decode(
            reader,
            specification.Signature,
            SignatureBlobGuard.Kind.MethodSpecification,
            () => specification.DecodeSignature(SignatureDecoder.Instance, context));

    public static SignatureDecodeResult<string> TypeSpecText(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        GenericContext? context)
        => TypeResolver.DecodeTypeNameFromSpecification(reader, handle, context);
}
