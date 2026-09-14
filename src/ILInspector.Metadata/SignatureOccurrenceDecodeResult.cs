using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>Why a single member signature could not be decoded.</summary>
public enum SignatureOccurrenceRejectionReason
{
    NoMetadata,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    UnsafeSignature,
    TypeSpecificationBudget,
    TypeNameBudget,
    InvalidTypeName,
    InvalidTypeScope,
    RelationshipTraversal,
    NodeBudget,
    OccurrenceCopyBudget,
    WorkBudget,
}

/// <summary>
/// A source TypeDef token paired with the image it belongs to. The image is an
/// identity here, not an owned resource; decoding does not extend its lifetime.
/// </summary>
public sealed record SignatureTypeDefinitionOrigin(
    PEReader Image,
    TypeDefinitionHandle Handle);

/// <summary>
/// One named occurrence, without binding or declaration-spelling conclusions.
/// Names inside optional modifier contents are retained but do not participate.
/// </summary>
public readonly record struct SignatureNamedTypeOccurrence(
    MetadataNamedTypeReference Reference,
    bool Participates,
    SignatureTypeDefinitionOrigin? DefinitionOrigin = null);

/// <summary>The closed outcome of decoding one method, field, or property signature.</summary>
public abstract record SignatureOccurrenceDecodeResult
{
    private protected SignatureOccurrenceDecodeResult() { }

    public sealed record Decoded : SignatureOccurrenceDecodeResult
    {
        internal Decoded(ImmutableArray<SignatureNamedTypeOccurrence> occurrences) =>
            Occurrences = occurrences;

        public ImmutableArray<SignatureNamedTypeOccurrence> Occurrences { get; }
    }

    public sealed record Rejected : SignatureOccurrenceDecodeResult
    {
        internal Rejected(SignatureOccurrenceRejectionReason reason) => Reason = reason;

        public SignatureOccurrenceRejectionReason Reason { get; }
    }
}
