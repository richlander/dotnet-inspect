using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Why a guarded string-signature decode did not produce a value.</summary>
public enum SignatureDecodeRejectionKind
{
    /// <summary>The iterative prescan found unsafe structural depth or counts.</summary>
    UnsafeStructure,

    /// <summary>The active TypeSpec closure exceeded its depth or cumulative-byte budget.</summary>
    TypeSpecificationBudget,

    /// <summary>SRM rejected malformed metadata during the bounded decode.</summary>
    MalformedMetadata,
}

/// <summary>Inspectable detail for a rejected guarded signature decode.</summary>
public sealed record SignatureDecodeRejection
{
    public SignatureDecodeRejection(
        SignatureDecodeRejectionKind kind,
        string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        Kind = kind;
        Detail = detail;
    }

    public SignatureDecodeRejectionKind Kind { get; }
    public string Detail { get; }
}

/// <summary>
/// The outcome of a guarded string-signature decode. A rejected decode never carries a
/// success-shaped placeholder.
/// </summary>
public abstract record SignatureDecodeResult<T>
    where T : notnull
{
    private protected SignatureDecodeResult()
    {
    }

    /// <summary>The signature decoded successfully.</summary>
    public sealed record Decoded : SignatureDecodeResult<T>
    {
        public Decoded(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public T Value { get; }
    }

    /// <summary>The signature was rejected before a trustworthy value could be produced.</summary>
    public sealed record Rejected : SignatureDecodeResult<T>
    {
        public Rejected(SignatureDecodeRejection rejection)
        {
            ArgumentNullException.ThrowIfNull(rejection);
            Rejection = rejection;
        }

        public SignatureDecodeRejection Rejection { get; }
    }

    /// <summary>Returns whether this outcome carries a decoded value.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        if (this is Decoded decoded)
        {
            value = decoded.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Returns the decoded value or throws <see cref="BadImageFormatException"/> at a caller-owned
    /// failure boundary.
    /// </summary>
    public T GetValueOrThrow()
        => this switch
        {
            Decoded decoded => decoded.Value,
            Rejected rejected => throw new BadImageFormatException(
                $"Signature decode rejected ({rejected.Rejection.Kind}): "
                + rejected.Rejection.Detail),
            _ => throw new InvalidOperationException(
                "Unknown signature decode result."),
        };
}

/// <summary>Shared guarded entry points for string-producing SRM signature decodes.</summary>
public static class GuardedSignatureDecoder
{
    /// <summary>Prescans and decodes a field, method, property, or method-spec signature.</summary>
    public static SignatureDecodeResult<T> Decode<T>(
        MetadataReader reader,
        BlobHandle signature,
        SignatureBlobGuard.Kind kind,
        Func<T> decode)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(decode);
        return SignatureDecoder.Decode(
            decode,
            () => SignatureBlobGuard.IsSafeToDecode(reader, signature, kind)
                ? null
                : new SignatureDecodeRejection(
                    SignatureDecodeRejectionKind.UnsafeStructure,
                    $"The {kind} signature exceeds the structural safety limit."));
    }

    /// <summary>
    /// Decodes a TypeSpec through the same cross-handle depth and cumulative-byte envelope used by
    /// nested string-provider re-entry.
    /// </summary>
    public static SignatureDecodeResult<string> DecodeTypeSpecification(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        GenericContext? context = null)
        => SignatureDecoder.Decode(() =>
        {
            if (!TypeSpecGuard.TryEnter(
                reader,
                handle,
                out var scope,
                out var rejectionKind))
            {
                SignatureDecoder.Reject(
                    new SignatureDecodeRejection(
                        rejectionKind,
                        rejectionKind == SignatureDecodeRejectionKind.UnsafeStructure
                            ? "The TypeSpec exceeds the structural safety limit."
                            : "The TypeSpec exceeds the re-entry depth or cumulative-byte budget."));
                return "object";
            }

            using (scope)
            {
                return reader.GetTypeSpecification(handle)
                    .DecodeSignature(SignatureDecoder.Instance, context);
            }
        });
}
