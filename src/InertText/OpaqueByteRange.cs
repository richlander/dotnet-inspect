namespace InertText;

internal sealed class OpaqueByteBacking
{
    internal OpaqueByteBacking(byte[] bytes, bool frozen)
    {
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        IsFrozen = frozen;
    }

    internal byte[] Bytes { get; }

    // Builder state. A builder and every copy of it share this one object, so they share one
    // write position, and freezing here also stops a copy taken before Freeze. Ranges may be
    // issued only after the owner stops writing.
    internal int Written { get; set; }

    internal bool IsFrozen { get; private set; }

    internal void Freeze() => IsFrozen = true;
}

/// <summary>
/// A bounded ownership snapshot over immutable bytes.
/// </summary>
/// <remarks>
/// The value exposes closed byte operations but never lends its retained storage.
/// </remarks>
public readonly struct OpaqueByteRange :
    IEquatable<OpaqueByteRange>,
    IComparable<OpaqueByteRange>
{
    private readonly OpaqueByteBacking? _backing;
    private readonly int _offset;

    internal OpaqueByteRange(
        OpaqueByteBacking backing,
        int offset,
        int length)
    {
        ArgumentNullException.ThrowIfNull(backing);
        if (!backing.IsFrozen)
            throw new InvalidOperationException(
                "A range cannot be issued over a backing that is still being written.");
        if ((uint)offset > (uint)backing.Bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if ((uint)length > (uint)(backing.Bytes.Length - offset))
            throw new ArgumentOutOfRangeException(nameof(length));

        _backing = backing;
        _offset = offset;
        Length = length;
    }

    /// <summary>Creates one immutable backing containing a copy of the source.</summary>
    public static OpaqueByteRange Copy(ReadOnlySpan<byte> source) =>
        FromOwned(source.ToArray());

    internal static OpaqueByteRange FromOwned(byte[] bytes)
    {
        var backing = new OpaqueByteBacking(bytes, frozen: true);
        return new(backing, 0, bytes.Length);
    }

    internal ReadOnlySpan<byte> Bytes =>
        _backing is null
            ? []
            : _backing.Bytes.AsSpan(_offset, Length);

    /// <summary>The number of bytes in this range.</summary>
    public int Length { get; }

    /// <summary>Whether the range contains no bytes.</summary>
    public bool IsEmpty => Length == 0;

    /// <summary>Returns a checked subrange retaining the same immutable backing.</summary>
    public OpaqueByteRange Slice(int offset, int length)
    {
        if ((uint)offset > (uint)Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if ((uint)length > (uint)(Length - offset))
            throw new ArgumentOutOfRangeException(nameof(length));

        return _backing is null
            ? default
            : new(_backing, checked(_offset + offset), length);
    }

    /// <summary>Returns the frozen subrange recorded by a builder slot.</summary>
    public OpaqueByteRange Slice(OpaqueByteRangeSlot slot) =>
        Slice(slot.Offset, slot.Length);

    /// <summary>
    /// One byte of the range, for a domain's bounded structural reader that
    /// interprets tags and lengths. A reader that needs a run of bytes slices
    /// a child range instead; this never lends the backing.
    /// </summary>
    public byte ByteAt(int offset)
    {
        if ((uint)offset >= (uint)Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return _backing!.Bytes[_offset + offset];
    }

    /// <summary>
    /// Tests byte-exact equality with a caller-supplied sequence without lending this range.
    /// </summary>
    /// <remarks>
    /// The query is input to the comparison and is never retained. The answer does not
    /// classify either sequence as text.
    /// </remarks>
    public bool SequenceEqual(ReadOnlySpan<byte> other) =>
        Bytes.SequenceEqual(other);

    /// <inheritdoc />
    public bool Equals(OpaqueByteRange other) =>
        Bytes.SequenceEqual(other.Bytes);

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is OpaqueByteRange other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Bytes);
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public int CompareTo(OpaqueByteRange other) =>
        Bytes.SequenceCompareTo(other.Bytes);

    /// <summary>Tests byte-exact equality.</summary>
    public static bool operator ==(
        OpaqueByteRange left,
        OpaqueByteRange right) =>
        left.Equals(right);

    /// <summary>Tests byte-exact inequality.</summary>
    public static bool operator !=(
        OpaqueByteRange left,
        OpaqueByteRange right) =>
        !left.Equals(right);
}
