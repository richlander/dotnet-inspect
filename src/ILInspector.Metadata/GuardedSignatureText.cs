using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Top-level prescan gateway for string-producing signature decodes in Metadata.
/// Nested TypeSpec re-entry is guarded by <see cref="SignatureDecoder"/>.
/// </summary>
internal static class GuardedSignatureText
{
    const string Unresolved = "object";
    static readonly MethodSignature<string> UnresolvedMethod = new(default, Unresolved, 0, 0, []);

    public static MethodSignature<string> MethodText(
        MetadataReader reader,
        MethodDefinition method,
        GenericContext? context)
        => SignatureBlobGuard.IsSafeToDecode(reader, method.Signature, SignatureBlobGuard.Kind.Method)
            ? method.DecodeSignature(SignatureDecoder.Instance, context)
            : UnresolvedMethod;

    public static MethodSignature<string> MemberRefMethodText(
        MetadataReader reader,
        MemberReference member,
        GenericContext? context)
        => SignatureBlobGuard.IsSafeToDecode(reader, member.Signature, SignatureBlobGuard.Kind.Method)
            ? member.DecodeMethodSignature(SignatureDecoder.Instance, context)
            : UnresolvedMethod;

    public static MethodSignature<string> PropertyText(
        MetadataReader reader,
        PropertyDefinition property,
        GenericContext? context)
        => SignatureBlobGuard.IsSafeToDecode(reader, property.Signature, SignatureBlobGuard.Kind.Method)
            ? property.DecodeSignature(SignatureDecoder.Instance, context)
            : UnresolvedMethod;

    public static string FieldText(
        MetadataReader reader,
        FieldDefinition field,
        GenericContext? context)
        => SignatureBlobGuard.IsSafeToDecode(reader, field.Signature, SignatureBlobGuard.Kind.Field)
            ? field.DecodeSignature(SignatureDecoder.Instance, context)
            : Unresolved;

    public static ImmutableArray<string> MethodSpecTypeArgs(
        MetadataReader reader,
        MethodSpecification specification,
        GenericContext? context)
        => SignatureBlobGuard.IsSafeToDecode(
            reader,
            specification.Signature,
            SignatureBlobGuard.Kind.MethodSpecification)
            ? specification.DecodeSignature(SignatureDecoder.Instance, context)
            : [];

    public static string TypeSpecText(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        GenericContext? context)
    {
        var specification = reader.GetTypeSpecification(handle);
        return SignatureBlobGuard.IsSafeToDecode(
            reader,
            specification.Signature,
            SignatureBlobGuard.Kind.TypeSpecification)
            ? specification.DecodeSignature(SignatureDecoder.Instance, context)
            : Unresolved;
    }
}
