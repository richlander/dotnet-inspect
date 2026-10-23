using InertText;
using InertText.Encoding;

namespace ILInspector.Metadata;

/// <summary>
/// The interning table of one scope, such as one comparison
/// (<c>docs/design/symbol-key.md#interning</c>): keys of one domain mapped to
/// dense ids, equal keys to one id. A lookup reads the candidate's bytes or
/// range and allocates nothing; a first sighting copies the key into the
/// table's one storage. A key that embeds another through
/// <see cref="SymbolKeyBuilder{TDomain}.Ref"/> is bound to the table that
/// issued the id; <see cref="Expand"/> rewrites it with every reference in
/// <c>Key</c> form.
/// </summary>
public sealed class SymbolKeyInterner<TDomain>
    where TDomain : unmanaged, Enum
{
    // A reference chain is acyclic by construction, since a key can embed only
    // ids issued before it; the bound, the depth the signature guard admits a
    // spelling to nest, contains a hand-built cycle.
    const int MaxExpansionDepth = SignatureBlobGuard.DefaultMaxDepth;

    readonly Entries _entries;
    readonly HashSet<int> _ids;
    readonly HashSet<int>.AlternateLookup<ReadOnlySpan<byte>> _byBytes;
    readonly HashSet<int>.AlternateLookup<OpaqueByteRange> _byRange;
    // One scratch builder per nesting depth of an expansion: a builder holds one
    // open key, and a nested key's expansion must complete before its parent embeds it.
    List<SymbolKeyBuilder<TDomain>>? _expansion;

    public SymbolKeyInterner(int initialCapacity = 16 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        _entries = new Entries(initialCapacity);
        _ids = new HashSet<int>(_entries);
        _byBytes = _ids.GetAlternateLookup<ReadOnlySpan<byte>>();
        _byRange = _ids.GetAlternateLookup<OpaqueByteRange>();
    }

    /// <summary>The number of distinct keys interned.</summary>
    public int Count => _entries.Count;

    /// <summary>The id of a staged key, issued on its first sighting.</summary>
    public int Intern(StagedSymbolKey<TDomain> key)
    {
        if (key.IsDefault)
            throw new ArgumentException("The default staged key cannot be interned.", nameof(key));
        return Intern(key.Bytes);
    }

    /// <summary>The id of a frozen key, issued on its first sighting.</summary>
    public int Intern(SymbolKey<TDomain> key)
    {
        if (key.IsDefault)
            throw new ArgumentException("The default key cannot be interned.", nameof(key));
        if (_byRange.TryGetValue(key.Range, out int id))
            return id;
        id = _entries.Add(key.Range);
        _ids.Add(id);
        return id;
    }

    internal int Intern(ReadOnlySpan<byte> key)
    {
        if (_byBytes.TryGetValue(key, out int id))
            return id;
        id = _entries.Add(key);
        _ids.Add(id);
        return id;
    }

    /// <summary>Whether a frozen key is interned, and its id.</summary>
    public bool TryGetId(SymbolKey<TDomain> key, out int id)
    {
        if (key.IsDefault)
            throw new ArgumentException("The default key has no id.", nameof(key));
        return _byRange.TryGetValue(key.Range, out id);
    }

    /// <summary>Whether a staged key is interned, and its id.</summary>
    public bool TryGetId(StagedSymbolKey<TDomain> key, out int id)
    {
        if (key.IsDefault)
            throw new ArgumentException("The default staged key has no id.", nameof(key));
        return _byBytes.TryGetValue(key.Bytes, out id);
    }

    /// <summary>
    /// The key an id names, as its own view: the copy that expansion and
    /// diagnostics take, which the comparing paths never need.
    /// </summary>
    public SymbolKey<TDomain> this[int id] => new(OpaqueByteRange.Copy(_entries.Bytes(id)));

    /// <summary>Releases the storage slack of a table that will grow no further.</summary>
    public void TrimExcess()
    {
        _entries.TrimExcess();
        _ids.TrimExcess();
        _expansion = null;
    }

    /// <summary>
    /// Writes <paramref name="key"/> into <paramref name="into"/> with every
    /// <c>Ref</c> part, at any depth, replaced by the <c>Key</c> form of the
    /// key it names. A key without <c>Ref</c> parts expands to its own bytes.
    /// </summary>
    public StagedSymbolKey<TDomain> Expand(SymbolKey<TDomain> key, SymbolKeyBuilder<TDomain> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (key.IsDefault)
            throw new ArgumentException("The default key cannot be expanded.", nameof(key));
        int mark = into.Begin();
        try
        {
            Write(key.Parts, into, depth: 0);
        }
        catch
        {
            into.Abandon();
            throw;
        }
        return into.End(mark);
    }

    void Write(SymbolKeyPartReader<TDomain> parts, SymbolKeyBuilder<TDomain> into, int depth)
    {
        while (parts.Read())
        {
            switch (parts.Kind)
            {
                case SymbolKeyPartKind.Marker:
                    into.Mark(parts.Marker);
                    break;
                case SymbolKeyPartKind.Int:
                    into.Int(parts.Int);
                    break;
                case SymbolKeyPartKind.Name:
                    into.FramedRange(SymbolKeyTag.Name, parts.Payload);
                    break;
                case SymbolKeyPartKind.Blob:
                    into.FramedRange(SymbolKeyTag.Blob, parts.Payload);
                    break;
                case SymbolKeyPartKind.Key:
                    Nested(parts.Key, into, depth);
                    break;
                case SymbolKeyPartKind.Ref:
                    Nested(this[parts.Id], into, depth);
                    break;
            }
        }
    }

    void Nested(SymbolKey<TDomain> key, SymbolKeyBuilder<TDomain> into, int depth)
    {
        if (depth >= MaxExpansionDepth)
            throw new InvalidOperationException("A key's expansion nests deeper than the table allows.");
        _expansion ??= [];
        if (depth == _expansion.Count)
            _expansion.Add(new SymbolKeyBuilder<TDomain>(256));
        SymbolKeyBuilder<TDomain> scratch = _expansion[depth];
        scratch.Reset();
        int mark = scratch.Begin();
        Write(key.Parts, scratch, depth + 1);
        into.Key(scratch.End(mark));
        scratch.Reset();
    }

    /// <summary>
    /// The table's storage and the set's comparer in one: the set holds ids,
    /// and hashes and compares them by the bytes they name, so a candidate's
    /// bytes or range look an id up directly.
    /// </summary>
    sealed class Entries(int initialCapacity)
        : IEqualityComparer<int>,
            IAlternateEqualityComparer<ReadOnlySpan<byte>, int>,
            IAlternateEqualityComparer<OpaqueByteRange, int>
    {
        byte[] _bytes = new byte[Math.Max(16, initialCapacity)];
        // Entry i spans [_ends[i - 1], _ends[i]), with _ends[-1] read as zero.
        int[] _ends = new int[64];
        int _count;
        int _length;

        public int Count => _count;

        public ReadOnlySpan<byte> Bytes(int id)
        {
            if ((uint)id >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(id), "The id was not issued by this table.");
            int start = id == 0 ? 0 : _ends[id - 1];
            return _bytes.AsSpan(start, _ends[id] - start);
        }

        public int Add(ReadOnlySpan<byte> key)
        {
            key.CopyTo(Reserve(key.Length));
            return Commit(key.Length);
        }

        public int Add(OpaqueByteRange key)
        {
            OpaqueByteRangeEncoder.Encode(key, Reserve(key.Length));
            return Commit(key.Length);
        }

        Span<byte> Reserve(int length)
        {
            if (_bytes.Length - _length < length)
                Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, _length + length));
            if (_count == _ends.Length)
                Array.Resize(ref _ends, _ends.Length * 2);
            return _bytes.AsSpan(_length, length);
        }

        int Commit(int length)
        {
            _length += length;
            _ends[_count] = _length;
            return _count++;
        }

        public void TrimExcess()
        {
            Array.Resize(ref _bytes, Math.Max(16, _length));
            Array.Resize(ref _ends, Math.Max(1, _count));
        }

        public bool Equals(int x, int y) => x == y;

        public int GetHashCode(int id) => Hash(Bytes(id));

        public bool Equals(ReadOnlySpan<byte> alternate, int id) => Bytes(id).SequenceEqual(alternate);

        public int GetHashCode(ReadOnlySpan<byte> alternate) => Hash(alternate);

        public int Create(ReadOnlySpan<byte> alternate)
            => throw new NotSupportedException("Bytes are looked up; the table issues ids.");

        public bool Equals(OpaqueByteRange alternate, int id) => alternate.SequenceEqual(Bytes(id));

        // The range hashes its bytes as HashCode.AddBytes does, so the two lookups agree.
        public int GetHashCode(OpaqueByteRange alternate) => alternate.GetHashCode();

        public int Create(OpaqueByteRange alternate)
            => throw new NotSupportedException("A range is looked up; the table issues ids.");

        static int Hash(ReadOnlySpan<byte> bytes)
        {
            var hash = new HashCode();
            hash.AddBytes(bytes);
            return hash.ToHashCode();
        }
    }
}
