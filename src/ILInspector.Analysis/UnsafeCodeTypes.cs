using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>Whether a Type's Finding Unsafe Any search settled.</summary>
public enum UnsafeCodeTypeState
{
    /// <summary>The Type declares a member with a census finding.</summary>
    Unsafe,
    /// <summary>
    /// The Type has no finding, but a body attributed to one of its members
    /// could not be inspected, so it may have one.
    /// </summary>
    Unknown,
}

/// <summary>One declaring Type and its settled state.</summary>
public sealed record UnsafeCodeType(TypeRef DeclaringType, UnsafeCodeTypeState State);

/// <summary>
/// The Finding Unsafe Any answer for one library: each Type that has unsafe
/// code, or may have it, and the limitations that leave an answer unsettled.
/// </summary>
public sealed record UnsafeCodeTypes(
    ImmutableArray<UnsafeCodeType> Types,
    ImmutableArray<UnsafeMemberLimitation> Limitations)
{
    ImmutableArray<UnsafeCodeType> _types =
        ImmutableArrayValueEquality.EmptyIfDefault(Types, nameof(Types));
    ImmutableArray<UnsafeMemberLimitation> _limitations =
        ImmutableArrayValueEquality.EmptyIfDefault(Limitations, nameof(Limitations));

    public ImmutableArray<UnsafeCodeType> Types
    {
        get => _types;
        init => _types = ImmutableArrayValueEquality.EmptyIfDefault(value, nameof(Types));
    }

    public ImmutableArray<UnsafeMemberLimitation> Limitations
    {
        get => _limitations;
        init => _limitations = ImmutableArrayValueEquality.EmptyIfDefault(value, nameof(Limitations));
    }

    public bool IsComplete => Limitations.IsEmpty;

    public bool Equals(UnsafeCodeTypes? other)
        => other is not null
            && ImmutableArrayValueEquality.SequenceEqual(Types, other.Types)
            && ImmutableArrayValueEquality.SequenceEqual(Limitations, other.Limitations);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        ImmutableArrayValueEquality.AddToHash(ref hash, Types);
        ImmutableArrayValueEquality.AddToHash(ref hash, Limitations);
        return hash.ToHashCode();
    }
}
