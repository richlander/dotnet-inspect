using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Why an artifact-derived metadata relationship walk did not complete.</summary>
public enum RelationshipTraversalRejectionKind
{
    /// <summary>A metadata handle repeated in the active relationship chain.</summary>
    Cycle,

    /// <summary>The relationship chain exceeded the fixed node ceiling.</summary>
    NodeBudget,

    /// <summary>SRM rejected a row or relationship handle.</summary>
    MalformedMetadata,
}

/// <summary>Inspectable evidence for a rejected metadata relationship walk.</summary>
public sealed record RelationshipTraversalRejection
{
    public RelationshipTraversalRejection(
        RelationshipTraversalRejectionKind kind,
        string detail,
        EntityHandle subject,
        int consumedNodes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        ArgumentOutOfRangeException.ThrowIfNegative(consumedNodes);
        Kind = kind;
        Detail = detail;
        Subject = subject;
        ConsumedNodes = consumedNodes;
    }

    public RelationshipTraversalRejectionKind Kind { get; }
    public string Detail { get; }
    public EntityHandle Subject { get; }
    public int ConsumedNodes { get; }
}

/// <summary>
/// A completed metadata relationship chain, ordered from the outermost handle
/// to the requested leaf handle.
/// </summary>
public sealed record RelationshipChain<THandle>
    where THandle : struct
{
    public RelationshipChain(
        ImmutableArray<THandle> handles,
        EntityHandle terminal)
    {
        if (handles.IsDefaultOrEmpty)
            throw new ArgumentException("A relationship chain must contain at least one handle.", nameof(handles));

        Handles = handles;
        Terminal = terminal;
    }

    public ImmutableArray<THandle> Handles { get; }
    public EntityHandle Terminal { get; }
}

/// <summary>
/// The outcome of a bounded metadata relationship operation. A rejected
/// operation never carries a partial success-shaped value.
/// </summary>
public abstract record RelationshipTraversalResult<T>
    where T : notnull
{
    private protected RelationshipTraversalResult()
    {
    }

    /// <summary>The relationship operation completed.</summary>
    public sealed record Completed : RelationshipTraversalResult<T>
    {
        public Completed(T value, int consumedNodes)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentOutOfRangeException.ThrowIfNegative(consumedNodes);
            Value = value;
            ConsumedNodes = consumedNodes;
        }

        public T Value { get; }
        public int ConsumedNodes { get; }
    }

    /// <summary>The relationship operation was rejected before producing a trustworthy value.</summary>
    public sealed record Rejected : RelationshipTraversalResult<T>
    {
        public Rejected(RelationshipTraversalRejection rejection)
        {
            ArgumentNullException.ThrowIfNull(rejection);
            Rejection = rejection;
        }

        public RelationshipTraversalRejection Rejection { get; }
    }

    /// <summary>Returns whether this outcome carries a completed value.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        if (this is Completed completed)
        {
            value = completed.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Returns the completed value or throws <see cref="BadImageFormatException"/>
    /// at a caller-owned whole-operation failure boundary.
    /// </summary>
    public T GetValueOrThrow()
        => this switch
        {
            Completed completed => completed.Value,
            Rejected rejected => throw new BadImageFormatException(
                $"Metadata relationship traversal rejected ({rejected.Rejection.Kind}): "
                + rejected.Rejection.Detail),
            _ => throw new InvalidOperationException(
                "Unknown metadata relationship traversal result."),
        };
}
