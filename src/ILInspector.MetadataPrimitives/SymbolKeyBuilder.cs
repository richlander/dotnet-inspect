using System.Buffers;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    /// <summary>This key's place in its population, for a consumer that holds many keys of one population.</summary>
    public SymbolKeySlot Slot => new(_offset, _length);

    /// <summary>The staged bytes, borrowed from the builder; invalid after a freeze or reset.</summary>
    internal ReadOnlySpan<byte> Bytes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _builder is null
            ? throw new SymbolKeyStateException("The default staged key has no bytes.")
            : _builder.Staged(_generation, _offset, _length);
    }

    public TDomain Kind => SymbolKeyDomain.FromMarker<TDomain>(Bytes[0]);

    public bool Equals(StagedSymbolKey<TDomain> other) => Bytes.SequenceEqual(other.Bytes);

    /// <summary>Copies the key's bytes, <see cref="Length"/> of them, into <paramref name="destination"/>.</summary>
    public void CopyTo(Span<byte> destination) => Bytes.CopyTo(destination);

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

    /// <summary>The generation of the builder's population this froze.</summary>
    public int Generation => _generation;

    public SymbolKey<TDomain> Resolve(StagedSymbolKey<TDomain> staged)
    {
        if (staged.IsDefault)
            throw new ArgumentException("The default staged key resolves to nothing.", nameof(staged));
        if (staged.Generation != _generation)
            throw new SymbolKeyStateException("The staged key belongs to another population.");
        return Resolve(staged.Offset, staged.Length);
    }

    /// <summary>The key staged at <paramref name="offset"/> before the freeze, for a consumer that recorded its slot.</summary>
    public SymbolKey<TDomain> Resolve(int offset, int length)
        => new(_range.Slice(offset, length));

    /// <summary>A slot's range without the key view's validation, for a consumer's hot equality.</summary>
    internal OpaqueByteRange RangeAt(int offset, int length) => _range.Slice(offset, length);
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
    // The domain's declared markers, held by instance so MarkShared reads no generic static.
    readonly bool[] _declared;
    byte[] _scratch;
    int _length;
    int _generation;
    int _open = NotOpen;

    public SymbolKeyBuilder(int initialCapacity = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        _declared = SymbolKeyDomain.DeclaredMarkers<TDomain>();
        _initialCapacity = Math.Max(16, initialCapacity);
        _scratch = new byte[_initialCapacity];
    }

    /// <summary>The <c>#Strings</c> heap <see cref="Name(StringHandle)"/> copies from; rebound with the image.</summary>
    public MetadataStringHeap? Names { get; set; }

    /// <summary>The bytes staged in the current population.</summary>
    public int Length => _length;

    /// <summary>The current population's generation; a freeze or reset moves it.</summary>
    public int Generation => _generation;

    /// <summary>Whether a key is open.</summary>
    public bool IsOpen => _open != NotOpen;

    /// <summary>Opens a key and returns its mark.</summary>
    public int Begin()
    {
        if (_open != NotOpen)
            throw new SymbolKeyStateException("A Symbol Key is already open; complete it before opening another.");
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

    /// <summary>
    /// Writes a marker of a shared vocabulary, which this domain must declare
    /// under its own name with the same value; a value the domain does not
    /// declare is refused, so a shared spelling never writes a part the domain
    /// cannot read back.
    /// </summary>
    public SymbolKeyBuilder<TDomain> MarkShared<TShared>(TShared marker)
        where TShared : unmanaged, Enum
    {
        if (Unsafe.SizeOf<TShared>() != 1)
            throw new SymbolKeyStateException($"Shared vocabulary {typeof(TShared).Name} must be a byte-backed enum.");
        byte value = SymbolKeyDomain.Marker(marker);
        if (!_declared[value])
            throw new SymbolKeyStateException($"Domain {typeof(TDomain).Name} does not declare the shared marker {marker} (0x{value:X2}).");
        RequireOpen();
        Ensure(1);
        _scratch[_length++] = value;
        return this;
    }

    /// <summary>Whether this builder's domain declares <paramref name="marker"/>.</summary>
    internal bool Declares(byte marker) => _declared[marker];

    /// <summary>
    /// Writes a marker byte a caller has already validated against the domain
    /// (the speller validates its whole vocabulary once at construction), so
    /// the hot path carries no per-marker check.
    /// </summary>
    internal SymbolKeyBuilder<TDomain> MarkValidated(byte marker)
    {
        RequireOpen();
        Ensure(1);
        _scratch[_length++] = marker;
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
        return Name(names.Bytes(handle));
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

    /// <summary>
    /// Embeds a key by the id a <see cref="SymbolKeyInterner{TDomain}"/> gave
    /// it, in place of its bytes. The key being built is then bound to that
    /// table: it compares only against keys that embed through the same table,
    /// and it expands through it.
    /// </summary>
    public SymbolKeyBuilder<TDomain> Ref(int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        RequireOpen();
        Ensure(6);
        _scratch[_length++] = (byte)SymbolKeyTag.Ref;
        WriteVarint((uint)id);
        return this;
    }

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

    /// <summary>Embeds a key given as its bytes, which a consumer read from a staged or frozen key it holds.</summary>
    internal SymbolKeyBuilder<TDomain> KeyBytes(ReadOnlySpan<byte> key)
    {
        RequireOpen();
        if (key.IsEmpty)
            throw new ArgumentException("An empty key cannot be embedded.", nameof(key));
        Ensure(1 + 10 + key.Length);
        _scratch[_length++] = (byte)SymbolKeyTag.Key;
        WriteVarint((uint)key.Length);
        key.CopyTo(_scratch.AsSpan(_length));
        _length += key.Length;
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
            throw new SymbolKeyStateException("The mark does not name the open key.");
        if (_length == mark)
            throw new SymbolKeyStateException("A Symbol Key holds at least its domain marker.");
        if (_scratch[mark] < SymbolKeyDomain.FirstMarker)
            throw new SymbolKeyStateException("A Symbol Key begins with a domain marker.");
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
            throw new SymbolKeyStateException("A Symbol Key is open; complete it before copying another.");
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

    /// <summary>Discards the staged population; its staged keys become invalid. Storage is kept for the next population.</summary>
    public void Reset()
    {
        _length = 0;
        _open = NotOpen;
        _generation++;
    }

    /// <summary>
    /// Discards the staged population and its storage: what a consumer calls
    /// when no population will follow, so a completed comparison retains only
    /// its frozen chunks.
    /// </summary>
    public void Release()
    {
        Reset();
        _scratch = [];
    }

    /// <summary>
    /// Transfers the staged population to one immutable backing and starts a new
    /// population. The returned population resolves this one's staged keys.
    /// </summary>
    public SymbolKeyPopulation<TDomain> Freeze()
    {
        if (_open != NotOpen)
            throw new SymbolKeyStateException("A Symbol Key is open; complete or abandon it before freezing.");
        var backing = new OpaqueByteRangeBuilder(_length);
        backing.Append(_scratch.AsSpan(0, _length));
        var population = new SymbolKeyPopulation<TDomain>(backing.Freeze(), _generation);
        _length = 0;
        _generation++;
        return population;
    }

    /// <summary>Resolves a slot of this population to its staged key.</summary>
    public StagedSymbolKey<TDomain> Resolve(SymbolKeySlot slot)
    {
        Require(slot);
        return new StagedSymbolKey<TDomain>(this, _generation, slot.Offset, slot.Length);
    }

    /// <summary>The kind of the key at a slot of this population.</summary>
    public TDomain Kind(SymbolKeySlot slot)
    {
        Require(slot);
        return SymbolKeyDomain.FromMarker<TDomain>(_scratch[slot.Offset]);
    }

    /// <summary>
    /// Sorts slots of this population by their keys' bytes, the order
    /// <see cref="StagedSymbolKey{TDomain}.CompareTo"/> defines, with the
    /// storage resolved once for the sort rather than once per comparison.
    /// </summary>
    public void Sort(List<SymbolKeySlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        Span<SymbolKeySlot> span = CollectionsMarshal.AsSpan(slots);
        foreach (ref readonly SymbolKeySlot slot in span)
            Require(slot);
        span.Sort(_order ??= CompareSlots);
    }

    /// <summary>
    /// Whether two slot sequences, of this population and another builder's,
    /// hold equal keys in order: the element-wise
    /// <see cref="StagedSymbolKey{TDomain}.Equals(StagedSymbolKey{TDomain})"/>.
    /// </summary>
    public bool SequenceEqual(ReadOnlySpan<SymbolKeySlot> slots, SymbolKeyBuilder<TDomain> other, ReadOnlySpan<SymbolKeySlot> otherSlots)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (slots.Length != otherSlots.Length)
            return false;
        for (int i = 0; i < slots.Length; i++)
        {
            SymbolKeySlot mine = slots[i];
            SymbolKeySlot theirs = otherSlots[i];
            Require(mine);
            other.Require(theirs);
            if (!_scratch.AsSpan(mine.Offset, mine.Length).SequenceEqual(other._scratch.AsSpan(theirs.Offset, theirs.Length)))
                return false;
        }
        return true;
    }

    Comparison<SymbolKeySlot>? _order;

    int CompareSlots(SymbolKeySlot x, SymbolKeySlot y)
        => _scratch.AsSpan(x.Offset, x.Length).SequenceCompareTo(_scratch.AsSpan(y.Offset, y.Length));

    void Require(SymbolKeySlot slot)
    {
        if (slot.Length <= 0 || (uint)slot.Offset > (uint)_length || (uint)slot.Length > (uint)(_length - slot.Offset))
            throw new SymbolKeyStateException("The slot lies outside this population's staged keys.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<byte> Staged(int generation, int offset, int length)
    {
        if (generation != _generation)
            ThrowStale();
        return _scratch.AsSpan(offset, length);
    }

    static void ThrowStale()
        => throw new SymbolKeyStateException("The staged key's population was frozen or reset.");

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

    /// <summary>A framed part whose payload is a range the expansion read from another key.</summary>
    internal SymbolKeyBuilder<TDomain> FramedRange(SymbolKeyTag tag, OpaqueByteRange payload)
    {
        RequireOpen();
        int length = payload.Length;
        Ensure(1 + 10 + length);
        _scratch[_length++] = (byte)tag;
        WriteVarint((uint)length);
        OpaqueByteRangeEncoder.Encode(payload, _scratch.AsSpan(_length, length));
        _length += length;
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
            throw new SymbolKeyStateException("No Symbol Key is open.");
    }

    void Ensure(int count)
    {
        if (_scratch.Length - _length < count)
            Array.Resize(ref _scratch, Math.Max(_scratch.Length * 2, _length + count));
    }
}

/// <summary>
/// A staged key's place in its population: offset and length alone, eight
/// bytes, for a consumer that holds many keys of one population and sorts,
/// compares, or resolves them through that population's builder. A slot
/// carries no builder or generation, so the consumer uses it only within the
/// generation that staged it; the builder refuses a slot outside its staged
/// keys.
/// </summary>
public readonly struct SymbolKeySlot : IEquatable<SymbolKeySlot>
{
    internal SymbolKeySlot(int offset, int length)
    {
        Offset = offset;
        Length = length;
    }

    internal int Offset { get; }

    public int Length { get; }

    public bool Equals(SymbolKeySlot other) => Offset == other.Offset && Length == other.Length;

    public override bool Equals(object? obj) => obj is SymbolKeySlot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Offset, Length);
}

/// <summary>
/// The comparer for dictionaries keyed by frozen keys that are also looked up
/// by staged keys, through the dictionary's alternate lookup; both hash and
/// compare the same bytes.
/// </summary>
public sealed class SymbolKeyComparer<TDomain>
    : IEqualityComparer<SymbolKey<TDomain>>,
        IAlternateEqualityComparer<StagedSymbolKey<TDomain>, SymbolKey<TDomain>>,
        IAlternateEqualityComparer<ReadOnlySpan<byte>, SymbolKey<TDomain>>
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

    /// <summary>A key's bytes, read by a consumer that holds a staged or frozen key, look up a frozen key.</summary>
    public bool Equals(ReadOnlySpan<byte> alternate, SymbolKey<TDomain> other) => other.Range.SequenceEqual(alternate);

    public int GetHashCode(ReadOnlySpan<byte> alternate)
    {
        var hash = new HashCode();
        hash.AddBytes(alternate);
        return hash.ToHashCode();
    }

    public SymbolKey<TDomain> Create(ReadOnlySpan<byte> alternate)
        => throw new NotSupportedException("Bytes are looked up, not inserted.");
}
