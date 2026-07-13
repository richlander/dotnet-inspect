using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Top-level entry gateway for the <see cref="ISignatureTypeProvider{TType,TContext}"/>
/// implementations in this assembly. SRM's <c>DecodeSignature</c> recurses on the
/// native stack for every nested element <em>before</em> it invokes the first
/// provider callback, so a single over-deep blob overflows the stack in a way no
/// managed <c>try/catch</c> can contain. Each helper prescans the blob with
/// <see cref="SignatureBlobGuard"/> and, when the blob is too deep or malformed,
/// returns a fail-closed fallback instead of entering the recursive decode.
///
/// Valid signatures pass the prescan unchanged, so guarded decodes are
/// byte-identical to direct decodes for well-formed assemblies. The nested
/// cross-handle TypeSpec vector is bounded separately by each provider's own
/// <c>GetTypeFromSpecification</c> via <see cref="TypeSpecGuard"/>.
/// </summary>
internal static class GuardedProviderDecode
{
    public static MethodSignature<T> Method<T, TContext>(
        MetadataReader reader,
        MethodDefinition method,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallbackReturn)
        => SignatureBlobGuard.IsSafeToDecode(reader, method.Signature, SignatureBlobGuard.Kind.Method)
            ? method.DecodeSignature(provider, context)
            : FallbackSignature(fallbackReturn);

    public static MethodSignature<T> MemberRefMethod<T, TContext>(
        MetadataReader reader,
        MemberReference member,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallbackReturn)
        => SignatureBlobGuard.IsSafeToDecode(reader, member.Signature, SignatureBlobGuard.Kind.Method)
            ? member.DecodeMethodSignature(provider, context)
            : FallbackSignature(fallbackReturn);

    public static MethodSignature<T> Property<T, TContext>(
        MetadataReader reader,
        PropertyDefinition property,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallbackReturn)
        => SignatureBlobGuard.IsSafeToDecode(reader, property.Signature, SignatureBlobGuard.Kind.Method)
            ? property.DecodeSignature(provider, context)
            : FallbackSignature(fallbackReturn);

    public static T Field<T, TContext>(
        MetadataReader reader,
        FieldDefinition field,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallback)
        => SignatureBlobGuard.IsSafeToDecode(reader, field.Signature, SignatureBlobGuard.Kind.Field)
            ? field.DecodeSignature(provider, context)
            : fallback;

    public static T MemberRefField<T, TContext>(
        MetadataReader reader,
        MemberReference member,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallback)
        => SignatureBlobGuard.IsSafeToDecode(reader, member.Signature, SignatureBlobGuard.Kind.Field)
            ? member.DecodeFieldSignature(provider, context)
            : fallback;

    public static ImmutableArray<T> MethodSpec<T, TContext>(
        MetadataReader reader,
        MethodSpecification spec,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context)
        => SignatureBlobGuard.IsSafeToDecode(reader, spec.Signature, SignatureBlobGuard.Kind.MethodSpecification)
            ? spec.DecodeSignature(provider, context)
            : ImmutableArray<T>.Empty;

    public static T TypeSpec<T, TContext>(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallback)
    {
        var spec = reader.GetTypeSpecification(handle);
        return SignatureBlobGuard.IsSafeToDecode(reader, spec.Signature, SignatureBlobGuard.Kind.TypeSpecification)
            ? spec.DecodeSignature(provider, context)
            : fallback;
    }

    static MethodSignature<T> FallbackSignature<T>(T fallbackReturn)
        => new(default, fallbackReturn, requiredParameterCount: 0, genericParameterCount: 0, ImmutableArray<T>.Empty);
}
