namespace InertText;

/// <summary>
/// The position of bytes appended to an <see cref="OpaqueByteRangeBuilder"/>.
/// </summary>
/// <remarks>
/// A slot records where bytes were written, not what they are. It becomes a range only through
/// <see cref="OpaqueByteRange.Slice(OpaqueByteRangeSlot)"/> on the frozen population, so no
/// retained view exists while the backing is still being written.
/// </remarks>
public readonly struct OpaqueByteRangeSlot
{
    internal OpaqueByteRangeSlot(int offset, int length)
    {
        Offset = offset;
        Length = length;
    }

    /// <summary>The offset of the slot within its population.</summary>
    public int Offset { get; }

    /// <summary>The number of bytes in the slot.</summary>
    public int Length { get; }
}

/// <summary>
/// Writes one population of exact bytes, then freezes it into one immutable backing.
/// </summary>
/// <remarks>
/// The producer declares the complete population size up front and appends exactly that many
/// bytes. <see cref="Freeze"/> refuses an incomplete population rather than retaining
/// unwritten storage. Copies of the builder share one write position and one frozen state, so
/// a copy can neither overwrite appended bytes nor append after freezing.
/// </remarks>
public readonly ref struct OpaqueByteRangeBuilder
{
    private readonly OpaqueByteBacking? _backing;

    /// <summary>Reserves a population of exactly <paramref name="length"/> bytes.</summary>
    public OpaqueByteRangeBuilder(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _backing = new OpaqueByteBacking(
            length == 0 ? [] : GC.AllocateUninitializedArray<byte>(length),
            frozen: false);
    }

    /// <summary>The number of bytes appended so far.</summary>
    public int Length => _backing?.Written ?? 0;

    /// <summary>Copies <paramref name="source"/> into the population and records its slot.</summary>
    public OpaqueByteRangeSlot Append(ReadOnlySpan<byte> source)
    {
        OpaqueByteBacking backing = Writable();
        int offset = backing.Written;
        if (source.Length > backing.Bytes.Length - offset)
        {
            throw new InvalidOperationException(
                "The appended bytes exceed the declared population length.");
        }

        source.CopyTo(backing.Bytes.AsSpan(offset));
        backing.Written = offset + source.Length;
        return new OpaqueByteRangeSlot(offset, source.Length);
    }

    /// <summary>
    /// Ends writing and returns the complete population as one range over the frozen backing.
    /// </summary>
    public OpaqueByteRange Freeze()
    {
        OpaqueByteBacking backing = Writable();
        if (backing.Written != backing.Bytes.Length)
        {
            throw new InvalidOperationException(
                "The population was frozen before its declared length was written.");
        }

        backing.Freeze();
        return new OpaqueByteRange(backing, 0, backing.Bytes.Length);
    }

    private OpaqueByteBacking Writable() =>
        _backing is null
            ? throw new InvalidOperationException(
                "The builder was not created with a declared length.")
            : _backing.IsFrozen
                ? throw new InvalidOperationException(
                    "The population is already frozen.")
                : _backing;
}
