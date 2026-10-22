using System.Buffers;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;

using InertText;
using InertText.Encoding;

namespace ILInspector.Metadata;

/// <summary>
/// A key the builder has completed but not yet frozen: an offset and length in
/// the builder's staging storage. It compares, hashes, and orders by its bytes,
/// including against staged keys of other builders and against frozen keys, so
/// a consumer can match while staging. It is valid until its builder freezes or
/// resets the population it belongs to, and resolves to a
/// <see cref="SymbolKey{TDomain}"/> through that population.
/// </summary>
public readonly struct StagedSymbolKey<TDomain> : IEquatable<StagedSymbolKey<TDomain>>, IComparable<StagedSymbolKey<TDomain>>
    where TDomain : unmanaged, Enum
{
    readonly SymbolKeyBuilder<TDomain> _builder;
    readonly int _generation;
    readonly int _offset;
    readonly int _length;

    internal StagedSymbolKey(SymbolKeyBuilder<TDomain> builder, int generation, int offset, int length)
    {
        _builder = builder;
        _generation = generation;
        _offset = offset;
        _length = length;
    }

    public bool IsDefault => _builder is null;

    /// <summary>Whether this key is staged in <paramref name="builder"/>.</summary>
    public bool IsStagedIn(SymbolKeyBuilder<TDomain> builder) => ReferenceEquals(_builder, builder);

    internal int Generation => _generation;

    internal int Offset => _offset;

    public int Length => _length;

    /// <summary>The staged bytes, borrowed from the builder; invalid after a freeze or reset.</summary>
    internal ReadOnlySpan<byte> Bytes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _builder is null
            ? throw new InvalidOperationException("The default staged key has no bytes.")
            : _builder.Staged(_generation, _offset, _length);
    }

    public TDomain Kind => SymbolKeyDomain.FromMarker<TDomain>(Bytes[0]);

    public bool Equals(StagedSymbolKey<TDomain> other) => Bytes.SequenceEqual(other.Bytes);

    /// <summary>Whether a frozen key holds the same parts as this key.</summary>
    public bool Equals(SymbolKey<TDomain> other) => other.Equals(this);

    public override bool Equals(object? obj) => obj is StagedSymbolKey<TDomain> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Bytes);
        return hash.ToHashCode();
    }

    public int CompareTo(StagedSymbolKey<TDomain> other) => Bytes.SequenceCompareTo(other.Bytes);

    /// <summary>
    /// Retains this key alone, in its own backing. The explicit copy for a key
    /// that outlives its population without the population being frozen.
    /// </summary>
    public SymbolKey<TDomain> Detach() => new(OpaqueByteRange.Copy(Bytes));

    public static bool operator ==(StagedSymbolKey<TDomain> left, StagedSymbolKey<TDomain> right) => left.Equals(right);

    public static bool operator !=(StagedSymbolKey<TDomain> left, StagedSymbolKey<TDomain> right) => !left.Equals(right);
}

/// <summary>
/// One frozen population: the immutable backing every key staged before the
/// freeze now shares. Resolving a staged key of this population issues its
/// <see cref="SymbolKey{TDomain}"/> view over that backing.
/// </summary>
public sealed class SymbolKeyPopulation<TDomain>
    where TDomain : unmanaged, Enum
{
    readonly OpaqueByteRange _range;
    readonly int _generation;

    internal SymbolKeyPopulation(OpaqueByteRange range, int generation)
    {
        _range = range;
        _generation = generation;
    }

    /// <summary>The population's bytes in all.</summary>
    public int Length => _range.Length;

    public SymbolKey<TDomain> Resolve(StagedSymbolKey<TDomain> staged)
    {
        if (staged.IsDefault)
            throw new ArgumentException("The default staged key resolves to nothing.", nameof(staged));
        if (staged.Generation != _generation)
            throw new ArgumentException("The staged key belongs to another population.", nameof(staged));
        return new SymbolKey<TDomain>(_range.Slice(staged.Offset, staged.Length));
    }
}

/// <summary>
/// Stages one consumer-chosen population of keys
/// (<c>docs/design/symbol-key.md#builder</c>). A key is opened with
/// <see cref="Begin"/>, written as parts, and completed with
/// <see cref="End"/>, which records it as a <see cref="StagedSymbolKey{TDomain}"/>
/// in the builder's staging storage; no key view exists over that storage.
/// <see cref="Freeze"/> transfers the staged bytes to one immutable backing and
/// returns the population through which staged keys resolve to views;
/// <see cref="Reset"/> discards them instead. One key is open at a time: a key
/// embedded in another is completed first, as a staged key of this or another
/// builder, or a frozen key.
/// </summary>
public sealed class SymbolKeyBuilder<TDomain>
    where TDomain : unmanaged, Enum
{
    const int NotOpen = -1;

    readonly int _initialCapacity;
    byte[] _scratch;
    int _length;
    int _generation;
    int _open = NotOpen;

    public SymbolKeyBuilder(int initialCapacity = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        SymbolKeyDomain.Validate<TDomain>();
        _initialCapacity = Math.Max(16, initialCapacity);
        _scratch = new byte[_initialCapacity];
    }

    /// <summary>The <c>#Strings</c> heap <see cref="Name(StringHandle)"/> copies from; rebound with the image.</summary>
    public MetadataStringHeap? Names { get; set; }

    /// <summary>The bytes staged in the current population.</summary>
    public int Length => _length;

    /// <summary>Whether a key is open.</summary>
    public bool IsOpen => _open != NotOpen;

    /// <summary>Opens a key and returns its mark.</summary>
    public int Begin()
    {
        if (_open != NotOpen)
            throw new InvalidOperationException("A Symbol Key is already open; complete it before opening another.");
        _open = _length;
        return _length;
    }

    public SymbolKeyBuilder<TDomain> Mark(TDomain marker)
    {
        RequireOpen();
        Ensure(1);
        _scratch[_length++] = SymbolKeyDomain.Marker(marker);
        return this;
    }

    public SymbolKeyBuilder<TDomain> Int(long value)
    {
        RequireOpen();
        Ensure(11);
        _scratch[_length++] = (byte)SymbolKeyTag.Int;
        WriteVarint((ulong)((value << 1) ^ (value >> 63)));
        return this;
    }

    /// <summary>A name copied from the <c>#Strings</c> heap as stored.</summary>
    public SymbolKeyBuilder<TDomain> Name(StringHandle handle)
    {
        MetadataStringHeap names = Names
            ?? throw new InvalidOperationException("The builder has no #Strings heap to copy names from.");
        int length = names.Length(handle);
        return Name(names.Bytes(handle, length));
    }

    /// <summary>A name written as the given UTF-8 bytes.</summary>
    public SymbolKeyBuilder<TDomain> Name(ReadOnlySpan<byte> utf8) => Framed(SymbolKeyTag.Name, utf8);

    /// <summary>
    /// A name encoded once from well-formed UTF-16 text, so a query compares
    /// with stored names without decoding storage; text with a lone surrogate
    /// has no UTF-8 spelling and is refused.
    /// </summary>
    public SymbolKeyBuilder<TDomain> Name(ReadOnlySpan<char> text)
    {
        RequireOpen();
        int maximum = Encoding.UTF8.GetMaxByteCount(text.Length);
        Ensure(1 + 10 + maximum);
        _scratch[_length++] = (byte)SymbolKeyTag.Name;
        Span<byte> destination = _scratch.AsSpan(_length + 10, maximum);
        if (System.Text.Unicode.Utf8.FromUtf16(text, destination, out _, out int written, replaceInvalidSequences: false)
            != OperationStatus.Done)
        {
            throw new ArgumentException("A name with a lone surrogate has no UTF-8 spelling.", nameof(text));
        }
        // The length prefix is shortest-form, so the payload moves down to it.
        int prefixStart = _length;
        WriteVarint((uint)written);
        _scratch.AsSpan(prefixStart + 10, written).CopyTo(_scratch.AsSpan(_length));
        _length += written;
        return this;
    }

    /// <summary>The rest of a blob, copied without an intermediate array.</summary>
    public unsafe SymbolKeyBuilder<TDomain> Blob(BlobReader reader)
        => Blob(new ReadOnlySpan<byte>(reader.CurrentPointer, reader.RemainingBytes));

    public SymbolKeyBuilder<TDomain> Blob(ReadOnlySpan<byte> bytes) => Framed(SymbolKeyTag.Blob, bytes);

    /// <summary>Embeds a staged key, of this or another builder, as a nested Key part.</summary>
    public SymbolKeyBuilder<TDomain> Key(StagedSymbolKey<TDomain> key)
    {
        RequireOpen();
        if (key.IsDefault)
            throw new ArgumentException("The default staged key cannot be embedded.", nameof(key));
        int length = key.Length;
        Ensure(1 + 10 + length);
        // Ensure first: the source may be this builder's own storage, which a resize replaces.
        _scratch[_length++] = (byte)SymbolKeyTag.Key;
        WriteVarint((uint)length);
        key.Bytes.CopyTo(_scratch.AsSpan(_length));
        _length += length;
        return this;
    }

    /// <summary>Embeds a frozen key as a nested Key part.</summary>
    public SymbolKeyBuilder<TDomain> Key(SymbolKey<TDomain> key)
    {
        RequireOpen();
        if (key.IsDefault)
            throw new ArgumentException("The default key cannot be embedded.", nameof(key));
        int length = key.Length;
        Ensure(1 + 10 + length);
        _scratch[_length++] = (byte)SymbolKeyTag.Key;
        WriteVarint((uint)length);
        OpaqueByteRangeEncoder.Encode(key.Range, _scratch.AsSpan(_length, length));
        _length += length;
        return this;
    }

    /// <summary>
    /// Completes the key opened at <paramref name="mark"/>. The key begins with
    /// a domain marker, so its kind is total.
    /// </summary>
    public StagedSymbolKey<TDomain> End(int mark)
    {
        if (_open != mark)
            throw new InvalidOperationException("The mark does not name the open key.");
        if (_length == mark)
            throw new InvalidOperationException("A Symbol Key holds at least its domain marker.");
        if (_scratch[mark] < SymbolKeyDomain.FirstMarker)
            throw new InvalidOperationException("A Symbol Key begins with a domain marker.");
        _open = NotOpen;
        return new StagedSymbolKey<TDomain>(this, _generation, mark, _length - mark);
    }

    /// <summary>
    /// Copies a staged key of another builder into this population as a staged
    /// key of its own, byte for byte: how a key built in scratch storage is
    /// retained.
    /// </summary>
    public StagedSymbolKey<TDomain> Copy(StagedSymbolKey<TDomain> key)
    {
        if (_open != NotOpen)
            throw new InvalidOperationException("A Symbol Key is open; complete it before copying another.");
        if (key.IsDefault)
            throw new ArgumentException("The default staged key cannot be copied.", nameof(key));
        int length = key.Length;
        Ensure(length);
        int offset = _length;
        key.Bytes.CopyTo(_scratch.AsSpan(offset));
        _length += length;
        return new StagedSymbolKey<TDomain>(this, _generation, offset, length);
    }

    /// <summary>Discards an open key after a build was abandoned by an exception; staged keys stay valid.</summary>
    public void Abandon()
    {
        if (_open != NotOpen)
        {
            _length = _open;
            _open = NotOpen;
        }
    }

    /// <summary>Discards the staged population; its staged keys become invalid.</summary>
    public void Reset()
    {
        _length = 0;
        _open = NotOpen;
        _generation++;
        Trim();
    }

    // Staging storage that grew for one unusually large population shrinks back,
    // so a long-lived builder does not retain that population's size; ordinary
    // growth is kept, since reallocating per population would cost more than
    // it saves.
    void Trim()
    {
        if (_scratch.Length > TrimThreshold && _scratch.Length > 4 * _initialCapacity)
            _scratch = new byte[_initialCapacity];
    }

    const int TrimThreshold = 1 << 20;

    /// <summary>
    /// Transfers the staged population to one immutable backing and starts a new
    /// population. The returned population resolves this one's staged keys.
    /// </summary>
    public SymbolKeyPopulation<TDomain> Freeze()
    {
        if (_open != NotOpen)
            throw new InvalidOperationException("A Symbol Key is open; complete or abandon it before freezing.");
        var backing = new OpaqueByteRangeBuilder(_length);
        backing.Append(_scratch.AsSpan(0, _length));
        var population = new SymbolKeyPopulation<TDomain>(backing.Freeze(), _generation);
        _length = 0;
        _generation++;
        Trim();
        return population;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<byte> Staged(int generation, int offset, int length)
    {
        if (generation != _generation)
            ThrowStale();
        return _scratch.AsSpan(offset, length);
    }

    static void ThrowStale()
        => throw new InvalidOperationException("The staged key's population was frozen or reset.");

    SymbolKeyBuilder<TDomain> Framed(SymbolKeyTag tag, ReadOnlySpan<byte> payload)
    {
        RequireOpen();
        Ensure(1 + 10 + payload.Length);
        _scratch[_length++] = (byte)tag;
        WriteVarint((uint)payload.Length);
        payload.CopyTo(_scratch.AsSpan(_length));
        _length += payload.Length;
        return this;
    }

    void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _scratch[_length++] = (byte)(value | 0x80);
            value >>= 7;
        }
        _scratch[_length++] = (byte)value;
    }

    void RequireOpen()
    {
        if (_open == NotOpen)
            throw new InvalidOperationException("No Symbol Key is open.");
    }

    void Ensure(int count)
    {
        if (_scratch.Length - _length < count)
            Array.Resize(ref _scratch, Math.Max(_scratch.Length * 2, _length + count));
    }
}

/// <summary>
/// The comparer for dictionaries keyed by frozen keys that are also looked up
/// by staged keys, through the dictionary's alternate lookup; both hash and
/// compare the same bytes.
/// </summary>
public sealed class SymbolKeyComparer<TDomain>
    : IEqualityComparer<SymbolKey<TDomain>>, IAlternateEqualityComparer<StagedSymbolKey<TDomain>, SymbolKey<TDomain>>
    where TDomain : unmanaged, Enum
{
    public static SymbolKeyComparer<TDomain> Instance { get; } = new();

    public bool Equals(SymbolKey<TDomain> x, SymbolKey<TDomain> y) => x.Equals(y);

    public int GetHashCode(SymbolKey<TDomain> obj) => obj.GetHashCode();

    public bool Equals(StagedSymbolKey<TDomain> alternate, SymbolKey<TDomain> other) => other.Equals(alternate);

    public int GetHashCode(StagedSymbolKey<TDomain> alternate) => alternate.GetHashCode();

    /// <summary>A staged key is inserted only after its population is frozen; the alternate lookup reads.</summary>
    public SymbolKey<TDomain> Create(StagedSymbolKey<TDomain> alternate)
        => throw new NotSupportedException("A staged key is inserted after its population is frozen.");
}
