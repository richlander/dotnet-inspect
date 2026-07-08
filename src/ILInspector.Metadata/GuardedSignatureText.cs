using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Guarded wrappers around the string-producing <see cref="SignatureDecoder"/> (the C# type-text
/// provider used by the declaration-query API, the compile-back / type-source composers, and the
/// metadata scanners).
///
/// These decode signatures of the inspected (untrusted) assembly, so a malformed deeply-nested or
/// huge-count signature would overflow the native stack inside SRM's <c>SignatureDecoder</c> (an
/// uncatchable <c>StackOverflowException</c>) or trigger a multi-gigabyte pre-allocation.
/// <see cref="SignatureBlobGuard"/> detects both before decoding, so these helpers fail closed to
/// an unresolved placeholder type (a parseable <c>object</c>) instead of crashing. Real signatures
/// are shallow, so the guard only trips on malformed input.
/// </summary>
public static class GuardedSignatureText
{
    // A degraded but syntactically valid C# type keeps composed source parseable; a member whose
    // signature could not be decoded is already un-reconstructable, so its exact text is moot.
    const string Unresolved = "object";

    public static MethodSignature<string> MethodText(MetadataReader reader, MethodDefinition method, GenericContext? context)
        => SignatureBlobGuard.IsSafeToDecode(reader, method.Signature, SignatureBlobGuard.Kind.Method)
            ? method.DecodeSignature(SignatureDecoder.Instance, context)
            : UnresolvedMethod;

    public static MethodSignature<string> PropertyText(MetadataReader reader, PropertyDefinition property, GenericContext? context)
        => SignatureBlobGuard.IsSafeToDecode(reader, property.Signature, SignatureBlobGuard.Kind.Method)
            ? property.DecodeSignature(SignatureDecoder.Instance, context)
            : UnresolvedMethod;

    public static string FieldText(MetadataReader reader, FieldDefinition field, GenericContext? context)
        => SignatureBlobGuard.IsSafeToDecode(reader, field.Signature, SignatureBlobGuard.Kind.Field)
            ? field.DecodeSignature(SignatureDecoder.Instance, context)
            : Unresolved;

    public static string TypeSpecText(MetadataReader reader, TypeSpecificationHandle handle, GenericContext? context)
    {
        var spec = reader.GetTypeSpecification(handle);
        return SignatureBlobGuard.IsSafeToDecode(reader, spec.Signature, SignatureBlobGuard.Kind.TypeSpecification)
            ? spec.DecodeSignature(SignatureDecoder.Instance, context)
            : Unresolved;
    }

    public static MethodSignature<string> MemberRefMethodText(MetadataReader reader, MemberReference memberRef, GenericContext? context)
        => SignatureBlobGuard.IsSafeToDecode(reader, memberRef.Signature, SignatureBlobGuard.Kind.Method)
            ? memberRef.DecodeMethodSignature(SignatureDecoder.Instance, context)
            : UnresolvedMethod;

    public static ImmutableArray<string> MethodSpecTypeArgs(MetadataReader reader, MethodSpecification spec, GenericContext? context)
        => SignatureBlobGuard.IsSafeToDecode(reader, spec.Signature, SignatureBlobGuard.Kind.MethodSpecification)
            ? spec.DecodeSignature(SignatureDecoder.Instance, context)
            : ImmutableArray<string>.Empty;

    static readonly MethodSignature<string> UnresolvedMethod = new(default, Unresolved, 0, 0, []);
}
