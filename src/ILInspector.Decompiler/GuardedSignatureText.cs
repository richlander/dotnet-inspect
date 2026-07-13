using System.Reflection.Metadata;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler;

/// <summary>
/// Guarded wrappers around the string-producing <see cref="SignatureDecoder"/> used by the
/// compile-back / type-source composers.
///
/// These composers decode signatures of the inspected (untrusted) assembly, so a malformed
/// deeply-nested or huge-count signature would overflow the native stack inside SRM's
/// <c>SignatureDecoder</c> (an uncatchable <c>StackOverflowException</c>) or trigger a
/// multi-gigabyte pre-allocation. <see cref="SignatureBlobGuard"/> detects both before decoding,
/// so these helpers reject the member instead of fabricating a parseable <c>object</c> signature.
/// Real signatures are shallow, so the guard only trips on malformed input.
/// </summary>
internal static class GuardedSignatureText
{
    public static MethodSignature<string> MethodText(MetadataReader reader, MethodDefinition method, GenericContext? context)
        => GuardedSignatureDecoder.Decode(
            reader,
            method.Signature,
            SignatureBlobGuard.Kind.Method,
            () => method.DecodeSignature(SignatureDecoder.Instance, context))
            .GetValueOrThrow();

    public static MethodSignature<string> PropertyText(MetadataReader reader, PropertyDefinition property, GenericContext? context)
        => GuardedSignatureDecoder.Decode(
            reader,
            property.Signature,
            SignatureBlobGuard.Kind.Method,
            () => property.DecodeSignature(SignatureDecoder.Instance, context))
            .GetValueOrThrow();

    public static string FieldText(MetadataReader reader, FieldDefinition field, GenericContext? context)
        => GuardedSignatureDecoder.Decode(
            reader,
            field.Signature,
            SignatureBlobGuard.Kind.Field,
            () => field.DecodeSignature(SignatureDecoder.Instance, context))
            .GetValueOrThrow();

    public static string TypeSpecText(MetadataReader reader, TypeSpecificationHandle handle, GenericContext? context)
        => TypeResolver.GetTypeNameFromSpecification(reader, handle, context)
            .GetValueOrThrow();
}
