using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Security.Cryptography;

using ILInspector.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// Prescans top-level signature blobs and bounds nested TypeSpec re-entry before
/// handing attacker-controlled metadata to SRM's recursive signature decoder.
/// </summary>
internal static class GuardedProviderDecode
{
    public static MethodSignature<T> Method<T, TContext>(
        MetadataReader reader,
        MethodDefinition method,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallbackReturn)
        => TryMethod(reader, method, provider, context, out var signature)
            ? signature
            : FallbackSignature(fallbackReturn);

    public static bool TryMethod<T, TContext>(
        MetadataReader reader,
        MethodDefinition method,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        out MethodSignature<T> signature)
    {
        var result = SignatureBlobGuard.IsSafeToDecode(reader, method.Signature, SignatureBlobGuard.Kind.Method)
            ? (Safe: true, Signature: method.DecodeSignature(provider, context))
            : (Safe: false, Signature: default(MethodSignature<T>));
        signature = result.Signature;
        return result.Safe;
    }

    public static MethodSignature<T> MemberRefMethod<T, TContext>(
        MetadataReader reader,
        MemberReference member,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallbackReturn)
        => TryMemberRefMethod(reader, member, provider, context, out var signature)
            ? signature
            : FallbackSignature(fallbackReturn);

    public static bool TryMemberRefMethod<T, TContext>(
        MetadataReader reader,
        MemberReference member,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        out MethodSignature<T> signature)
    {
        var result = SignatureBlobGuard.IsSafeToDecode(reader, member.Signature, SignatureBlobGuard.Kind.Method)
            ? (Safe: true, Signature: member.DecodeMethodSignature(provider, context))
            : (Safe: false, Signature: default(MethodSignature<T>));
        signature = result.Signature;
        return result.Safe;
    }

    public static MethodSignature<T> StandaloneMethod<T, TContext>(
        MetadataReader reader,
        StandaloneSignature signature,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallbackReturn)
        => TryStandaloneMethod(reader, signature, provider, context, out var decoded)
            ? decoded
            : FallbackSignature(fallbackReturn);

    public static bool TryStandaloneMethod<T, TContext>(
        MetadataReader reader,
        StandaloneSignature signature,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        out MethodSignature<T> decoded)
    {
        var result = SignatureBlobGuard.IsSafeToDecode(reader, signature.Signature, SignatureBlobGuard.Kind.Method)
            ? (Safe: true, Signature: signature.DecodeMethodSignature(provider, context))
            : (Safe: false, Signature: default(MethodSignature<T>));
        decoded = result.Signature;
        return result.Safe;
    }

    public static T Field<T, TContext>(
        MetadataReader reader,
        FieldDefinition field,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallback)
        => TryField(reader, field, provider, context, out var decoded)
            ? decoded
            : fallback;

    public static bool TryField<T, TContext>(
        MetadataReader reader,
        FieldDefinition field,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        out T decoded)
    {
        var result = SignatureBlobGuard.IsSafeToDecode(reader, field.Signature, SignatureBlobGuard.Kind.Field)
            ? (Safe: true, Value: field.DecodeSignature(provider, context))
            : (Safe: false, Value: default(T)!);
        decoded = result.Value;
        return result.Safe;
    }

    public static T MemberRefField<T, TContext>(
        MetadataReader reader,
        MemberReference member,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallback)
        => TryMemberRefField(reader, member, provider, context, out var decoded)
            ? decoded
            : fallback;

    public static bool TryMemberRefField<T, TContext>(
        MetadataReader reader,
        MemberReference member,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        out T decoded)
    {
        var result = SignatureBlobGuard.IsSafeToDecode(reader, member.Signature, SignatureBlobGuard.Kind.Field)
            ? (Safe: true, Value: member.DecodeFieldSignature(provider, context))
            : (Safe: false, Value: default(T)!);
        decoded = result.Value;
        return result.Safe;
    }

    public static ImmutableArray<T> LocalVariables<T, TContext>(
        MetadataReader reader,
        StandaloneSignature signature,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context)
        => SignatureBlobGuard.IsSafeToDecode(reader, signature.Signature, SignatureBlobGuard.Kind.LocalVariables)
            ? signature.DecodeLocalSignature(provider, context)
            : ImmutableArray<T>.Empty;

    public static ImmutableArray<T> MethodSpec<T, TContext>(
        MetadataReader reader,
        MethodSpecification specification,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        ImmutableArray<T> fallback)
        => TryMethodSpec(reader, specification, provider, context, out var decoded)
            ? decoded
            : fallback;

    public static bool TryMethodSpec<T, TContext>(
        MetadataReader reader,
        MethodSpecification specification,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        out ImmutableArray<T> decoded)
    {
        var result = SignatureBlobGuard.IsSafeToDecode(
                reader,
                specification.Signature,
                SignatureBlobGuard.Kind.MethodSpecification)
            ? (Safe: true, Value: specification.DecodeSignature(provider, context))
            : (Safe: false, Value: ImmutableArray<T>.Empty);
        decoded = result.Value;
        return result.Safe;
    }

    public static T TypeSpec<T, TContext>(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        T fallback)
        => TryTypeSpec(reader, handle, provider, context, out var decoded)
            ? decoded
            : fallback;

    public static bool TryTypeSpec<T, TContext>(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        ISignatureTypeProvider<T, TContext> provider,
        TContext context,
        out T decoded)
    {
        if (!TypeSpecGuard.TryEnter(reader, handle, out int blobLength))
        {
            decoded = default!;
            return false;
        }

        try
        {
            decoded = reader.GetTypeSpecification(handle).DecodeSignature(provider, context);
            return true;
        }
        finally
        {
            TypeSpecGuard.Exit(blobLength);
        }
    }

    public static string RejectedIdentity(MetadataReader reader, BlobHandle signature)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(reader.GetBlobBytes(signature), hash);
        return $"<unsupported-signature:{Convert.ToHexString(hash)}>";
    }

    public static MethodSignature<T> FallbackSignature<T>(T fallbackReturn)
        => new(default, fallbackReturn, requiredParameterCount: 0, genericParameterCount: 0, ImmutableArray<T>.Empty);
}
