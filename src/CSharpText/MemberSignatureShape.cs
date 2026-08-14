using System.Collections;
using System.Collections.Immutable;

namespace CSharpText;

/// <summary>
/// Identifies the source declaration form presented to
/// <see cref="SourceMemberSignatureShape.Create"/>.
/// </summary>
public enum SourceMemberSignatureKind
{
    Method,
    Constructor,
    Operator,
    ConversionOperator,
    Property,
    Indexer,
}

/// <summary>Whether a parameter is passed by value or by managed reference.</summary>
public enum ParameterPassingKind
{
    Value,
    ByReference,
}

/// <summary>Whether a generic parameter belongs to a containing type or to the member.</summary>
public enum SignatureGenericParameterKind
{
    Type,
    Method,
}

/// <summary>
/// A structurally equatable immutable sequence used by signature-shape records.
/// </summary>
public readonly struct SignatureShapeList<T> :
    IReadOnlyList<T>,
    IEquatable<SignatureShapeList<T>>
{
    readonly ImmutableArray<T> _items;

    public SignatureShapeList(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    public static SignatureShapeList<T> Empty => new([]);

    public int Count => _items.IsDefault ? 0 : _items.Length;

    public T this[int index] => _items[index];

    public bool Equals(SignatureShapeList<T> other)
    {
        if (Count != other.Count)
            return false;

        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < Count; i++)
        {
            if (!comparer.Equals(this[i], other[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj)
        => obj is SignatureShapeList<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (T item in this)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator()
        => ((_items.IsDefault ? ImmutableArray<T>.Empty : _items) as IEnumerable<T>).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(SignatureShapeList<T> left, SignatureShapeList<T> right)
        => left.Equals(right);

    public static bool operator !=(SignatureShapeList<T> left, SignatureShapeList<T> right)
        => !left.Equals(right);
}

/// <summary>A type as represented by both source syntax and an ECMA-335 signature.</summary>
public abstract record TypeSignatureShape;

public sealed record PrimitiveTypeSignatureShape(string ClrName) : TypeSignatureShape;

public sealed record GenericParameterTypeSignatureShape(
    SignatureGenericParameterKind Kind,
    int Position) : TypeSignatureShape;

public sealed record NamedTypeSegment(
    string Name,
    int Arity,
    SignatureShapeList<TypeSignatureShape> TypeArguments);

public sealed record NamedTypeSignatureShape(
    string Namespace,
    SignatureShapeList<NamedTypeSegment> Segments) : TypeSignatureShape;

/// <summary>
/// A named type retained only while normalizing a legacy lossy signature string.
/// It is never eligible for unique correspondence.
/// </summary>
public sealed record UnresolvedNamedTypeSignatureShape(
    string Name,
    SignatureShapeList<TypeSignatureShape> TypeArguments) : TypeSignatureShape;

public sealed record ArrayTypeSignatureShape(
    TypeSignatureShape ElementType,
    int Rank,
    bool IsSzArray) : TypeSignatureShape;

public sealed record PointerTypeSignatureShape(TypeSignatureShape ElementType) : TypeSignatureShape;

public sealed record ByReferenceTypeSignatureShape(TypeSignatureShape ElementType) : TypeSignatureShape;

public sealed record NullableTypeSignatureShape(TypeSignatureShape UnderlyingType) : TypeSignatureShape;

public sealed record TupleTypeSignatureShape(
    SignatureShapeList<TypeSignatureShape> ElementTypes) : TypeSignatureShape;

public sealed record FunctionPointerTypeSignatureShape(
    string CallingConvention,
    TypeSignatureShape ReturnType,
    SignatureShapeList<TypeSignatureShape> ParameterTypes) : TypeSignatureShape;

public sealed record MemberParameterSignatureShape(
    ParameterPassingKind Passing,
    TypeSignatureShape Type);

/// <summary>
/// Non-authoritative declaration evidence used to discriminate same-named member candidates.
/// It is not a metadata identity and must not be used as one.
/// </summary>
public sealed record MemberSignatureShape(
    int GenericArity,
    SignatureShapeList<MemberParameterSignatureShape> Parameters,
    TypeSignatureShape? ConversionReturnType = null);

/// <summary>An available shape or an explicit reason that one could not be produced.</summary>
public sealed record MemberSignatureShapeResult
{
    MemberSignatureShapeResult(MemberSignatureShape? shape, string? unavailableReason)
    {
        Shape = shape;
        UnavailableReason = unavailableReason;
    }

    public MemberSignatureShape? Shape { get; }

    public string? UnavailableReason { get; }

    public bool IsAvailable => Shape is not null;

    public static MemberSignatureShapeResult Available(MemberSignatureShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return new(shape, null);
    }

    public static MemberSignatureShapeResult Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(null, reason);
    }
}

public enum MemberSignatureCorrespondenceKind
{
    Unique,
    Ambiguous,
    Unavailable,
}

/// <summary>A typed candidate-discrimination result; ambiguity and refusal are not collapsed.</summary>
public sealed record MemberSignatureCorrespondence<T>
{
    MemberSignatureCorrespondence(
        MemberSignatureCorrespondenceKind kind,
        T? match,
        SignatureShapeList<T> candidates,
        string? unavailableReason)
    {
        Kind = kind;
        Match = match;
        Candidates = candidates;
        UnavailableReason = unavailableReason;
    }

    public MemberSignatureCorrespondenceKind Kind { get; }

    public T? Match { get; }

    public SignatureShapeList<T> Candidates { get; }

    public string? UnavailableReason { get; }

    public static MemberSignatureCorrespondence<T> Unique(T match)
        => new(
            MemberSignatureCorrespondenceKind.Unique,
            match,
            SignatureShapeList<T>.Empty,
            null);

    public static MemberSignatureCorrespondence<T> Ambiguous(IEnumerable<T> candidates)
        => new(
            MemberSignatureCorrespondenceKind.Ambiguous,
            default,
            new(candidates),
            null);

    public static MemberSignatureCorrespondence<T> Unavailable(string reason)
        => new(
            MemberSignatureCorrespondenceKind.Unavailable,
            default,
            SignatureShapeList<T>.Empty,
            reason);
}

/// <summary>Matches a target shape against a complete relevant candidate set without inventing a fallback.</summary>
/// <remarks>
/// Callers must include every same-name declaration under consideration. Omitting a sibling
/// can turn lexical namespace/nested-type uncertainty into a false unique result.
/// </remarks>
public static class MemberSignatureShapeMatcher
{
    public static MemberSignatureCorrespondence<T> Match<T>(
        MemberSignatureShapeResult target,
        IReadOnlyList<(T Candidate, MemberSignatureShapeResult Shape)> candidates)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(candidates);

        if (target.Shape is null)
            return MemberSignatureCorrespondence<T>.Unavailable(
                target.UnavailableReason ?? "The target signature shape is unavailable.");
        if (!IsCorrespondable(target.Shape))
        {
            return MemberSignatureCorrespondence<T>.Unavailable(
                "The target signature shape contains a semantically unresolved named type.");
        }

        // A difficult sibling is evidence against uniqueness, not a candidate to skip.
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Shape.Shape is null)
            {
                return MemberSignatureCorrespondence<T>.Unavailable(
                    candidates[i].Shape.UnavailableReason
                    ?? "A candidate signature shape is unavailable.");
            }
            if (!IsCorrespondable(candidates[i].Shape.Shape!))
            {
                return MemberSignatureCorrespondence<T>.Unavailable(
                    "A candidate signature shape contains a semantically unresolved named type.");
            }
        }

        var matches = candidates
            .Where(candidate => candidate.Shape.Shape == target.Shape)
            .Select(candidate => candidate.Candidate)
            .ToArray();

        return matches.Length switch
        {
            1 => MemberSignatureCorrespondence<T>.Unique(matches[0]),
            > 1 => MemberSignatureCorrespondence<T>.Ambiguous(matches),
            _ => MemberSignatureCorrespondence<T>.Unavailable(
                "No candidate has the requested signature shape."),
        };
    }

    static bool IsCorrespondable(MemberSignatureShape shape)
        => shape.Parameters.All(parameter => IsCorrespondable(parameter.Type))
            && (shape.ConversionReturnType is null
                || IsCorrespondable(shape.ConversionReturnType));

    static bool IsCorrespondable(TypeSignatureShape shape)
        => shape switch
        {
            UnresolvedNamedTypeSignatureShape => false,
            NamedTypeSignatureShape named => named.Segments.All(
                segment => segment.TypeArguments.All(IsCorrespondable)),
            ArrayTypeSignatureShape array => IsCorrespondable(array.ElementType),
            PointerTypeSignatureShape pointer => IsCorrespondable(pointer.ElementType),
            ByReferenceTypeSignatureShape byReference => IsCorrespondable(byReference.ElementType),
            NullableTypeSignatureShape nullable => IsCorrespondable(nullable.UnderlyingType),
            TupleTypeSignatureShape tuple => tuple.ElementTypes.All(IsCorrespondable),
            FunctionPointerTypeSignatureShape pointer =>
                IsCorrespondable(pointer.ReturnType)
                && pointer.ParameterTypes.All(IsCorrespondable),
            _ => true,
        };
}
