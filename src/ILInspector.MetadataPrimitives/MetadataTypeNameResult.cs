using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>The metadata mechanism that rejected a type-name operation.</summary>
public enum MetadataTypeNameFailureMechanism
{
    Metadata,
    Relationship,
    Signature,
    TypeSpecification,
}

/// <summary>Typed evidence for a rejected metadata type-name operation.</summary>
public sealed record MetadataTypeNameFailure
{
    MetadataTypeNameFailure(
        MetadataTypeNameFailureMechanism mechanism,
        string detail,
        int? subjectToken,
        int consumedNodes,
        RelationshipTraversalRejectionKind? relationshipKind,
        SignatureDecodeRejectionKind? signatureKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        ArgumentOutOfRangeException.ThrowIfNegative(consumedNodes);
        Mechanism = mechanism;
        Detail = detail;
        SubjectToken = subjectToken;
        ConsumedNodes = consumedNodes;
        RelationshipKind = relationshipKind;
        SignatureKind = signatureKind;
    }

    public MetadataTypeNameFailureMechanism Mechanism { get; }
    public string Detail { get; }
    public int? SubjectToken { get; }
    public int ConsumedNodes { get; }
    public RelationshipTraversalRejectionKind? RelationshipKind { get; }
    public SignatureDecodeRejectionKind? SignatureKind { get; }

    public string Kind => RelationshipKind?.ToString()
        ?? SignatureKind?.ToString()
        ?? "MalformedMetadata";

    public static MetadataTypeNameFailure From(RelationshipTraversalRejection rejection)
    {
        ArgumentNullException.ThrowIfNull(rejection);
        return new MetadataTypeNameFailure(
            MetadataTypeNameFailureMechanism.Relationship,
            rejection.Detail,
            rejection.Subject.IsNil ? null : MetadataTokens.GetToken(rejection.Subject),
            rejection.ConsumedNodes,
            rejection.Kind,
            signatureKind: null);
    }

    public static MetadataTypeNameFailure From(
        SignatureDecodeRejection rejection,
        TypeSpecificationHandle subject)
    {
        ArgumentNullException.ThrowIfNull(rejection);
        return new MetadataTypeNameFailure(
            rejection.Kind == SignatureDecodeRejectionKind.TypeSpecificationBudget
                ? MetadataTypeNameFailureMechanism.TypeSpecification
                : MetadataTypeNameFailureMechanism.Signature,
            rejection.Detail,
            subject.IsNil ? null : MetadataTokens.GetToken(subject),
            consumedNodes: 0,
            relationshipKind: null,
            rejection.Kind);
    }

    public static MetadataTypeNameFailure Malformed(
        EntityHandle subject,
        string detail)
        => ForMechanism(
            MetadataTypeNameFailureMechanism.Metadata,
            subject,
            detail);

    public static MetadataTypeNameFailure ForMechanism(
        MetadataTypeNameFailureMechanism mechanism,
        EntityHandle subject,
        string detail)
        => new(
            mechanism,
            detail,
            subject.IsNil ? null : MetadataTokens.GetToken(subject),
            consumedNodes: 0,
            relationshipKind: null,
            signatureKind: null);
}

/// <summary>
/// Strict type-name resolution that distinguishes absence from malformed or
/// resource-rejected metadata.
/// </summary>
public abstract record MetadataTypeNameResult
{
    private protected MetadataTypeNameResult()
    {
    }

    public sealed record Resolved : MetadataTypeNameResult
    {
        public Resolved(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public string Value { get; }
    }

    public sealed record Absent : MetadataTypeNameResult;

    public sealed record Rejected : MetadataTypeNameResult
    {
        public Rejected(MetadataTypeNameFailure failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public MetadataTypeNameFailure Failure { get; }
    }

    public bool TryGetValue([NotNullWhen(true)] out string? value)
    {
        if (this is Resolved resolved)
        {
            value = resolved.Value;
            return true;
        }

        value = null;
        return false;
    }
}
