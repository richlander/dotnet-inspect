using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// One complete detached Type Signature post for an exact TypeDef.
/// </summary>
public sealed record MetadataTypeSignatureEvidence(
    MetadataTypeDeclarationEvidence Declaration,
    MetadataTypeSignatureTypeEvidence? BaseType,
    ImmutableArray<MetadataTypeSignatureGenericParameterEvidence>
        GenericParameters,
    int NestedTypeCount);

/// <summary>One Type shape and the annotations consumed by Type spelling.</summary>
public sealed record MetadataTypeSignatureTypeEvidence(
    MetadataTypeSignatureType Type,
    ImmutableArray<byte> DynamicFlags);

/// <summary>One declaration parameter and its ordered Type constraints.</summary>
public sealed record MetadataTypeSignatureGenericParameterEvidence(
    MetadataTypeGenericParameterDeclarationEvidence Declaration,
    ImmutableArray<MetadataTypeSignatureConstraintEvidence>
        TypeConstraints);

/// <summary>
/// One ordered GenericParamConstraint Type and its effective nullability.
/// </summary>
public sealed record MetadataTypeSignatureConstraintEvidence(
    MetadataTypeSignatureType Type,
    byte? NullableFlag);

/// <summary>
/// Detached, byte-backed structural Type spelling evidence.
/// </summary>
/// <remarks>
/// Named Types retain metadata namespace and root-to-leaf Name components, not resolution
/// scope. The shape owns presentation spelling, not cross-assembly Type identity.
/// </remarks>
public abstract record MetadataTypeSignatureType
{
    public sealed record Primitive(PrimitiveTypeCode Code)
        : MetadataTypeSignatureType;

    public sealed record Named(
        MetadataTypeSignatureNamedType Name,
        MetadataTypeSignatureNamedKind Kind)
        : MetadataTypeSignatureType;

    public sealed record GenericInstance(
        Named Definition,
        ImmutableArray<MetadataTypeSignatureType> Arguments)
        : MetadataTypeSignatureType;

    public sealed record TypeParameter(int Index)
        : MetadataTypeSignatureType;

    public sealed record Array(
        MetadataTypeSignatureType Element,
        int Rank,
        ImmutableArray<int> Sizes,
        ImmutableArray<int> LowerBounds)
        : MetadataTypeSignatureType;

    public sealed record SzArray(MetadataTypeSignatureType Element)
        : MetadataTypeSignatureType;

    public sealed record Pointer(MetadataTypeSignatureType Element)
        : MetadataTypeSignatureType;

    public sealed record ByReference(MetadataTypeSignatureType Element)
        : MetadataTypeSignatureType;

    public sealed record FunctionPointer(
        SignatureHeader Header,
        int GenericParameterCount,
        MetadataTypeSignatureType ReturnType,
        ImmutableArray<MetadataTypeSignatureType> ParameterTypes,
        int RequiredParameterCount)
        : MetadataTypeSignatureType;

    public sealed record Modified(
        MetadataTypeSignatureType Modifier,
        MetadataTypeSignatureType UnmodifiedType,
        bool IsRequired)
        : MetadataTypeSignatureType;

    public sealed record Pinned(MetadataTypeSignatureType Element)
        : MetadataTypeSignatureType;
}

/// <summary>The class/value marker encoded by a signature, when present.</summary>
public enum MetadataTypeSignatureNamedKind
{
    Unspecified,
    Class,
    ValueType,
}

/// <summary>One byte-backed named Type spelling.</summary>
public sealed record MetadataTypeSignatureNamedType(
    MetadataUtf8Name Namespace,
    ImmutableArray<MetadataUtf8Name> Segments,
    ImmutableArray<int> IntroducedGenericParameterCounts);

public abstract record MetadataTypeSignatureResult
{
    private protected MetadataTypeSignatureResult(
        MetadataOperationCounters counters) =>
        Counters = counters;

    public MetadataOperationCounters Counters { get; }

    public sealed record Posted : MetadataTypeSignatureResult
    {
        internal Posted(
            MetadataTypeSignatureEvidence evidence,
            MetadataOperationCounters counters)
            : base(counters) =>
            Evidence = evidence;

        public MetadataTypeSignatureEvidence Evidence { get; }
    }

    public sealed record Rejected : MetadataTypeSignatureResult
    {
        internal Rejected(
            MetadataTypeDeclarationFailure failure,
            MetadataOperationCounters counters)
            : base(counters) =>
            Failure = failure;

        public MetadataTypeDeclarationFailure Failure { get; }
    }
}
