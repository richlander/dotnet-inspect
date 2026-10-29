using System.Runtime.CompilerServices;

using InertText;

namespace ILInspector.Metadata;

/// <summary>
/// The payload part tags of the Symbol Key grammar
/// (<c>docs/design/symbol-key.md#part-grammar</c>). Every value below
/// <see cref="SymbolKeyDomain.FirstMarker"/> belongs to this owner; a domain's
/// markers start there.
/// </summary>
internal enum SymbolKeyTag : byte
{
    Reserved = 0x00,
    Int = 0x01,
    Name = 0x02,
    Key = 0x03,
    Blob = 0x04,
    Ref = 0x05,
    Header = 0x06,
}

/// <summary>Validation of a key domain: a byte-backed enum whose values lie in the marker range.</summary>
public static class SymbolKeyDomain
{
    /// <summary>The first tag a domain marker may use; lower tags are the grammar's payload parts.</summary>
    public const byte FirstMarker = 0x10;

    internal static byte Marker<TDomain>(TDomain value)
        where TDomain : unmanaged, Enum
        => Unsafe.As<TDomain, byte>(ref value);

    internal static TDomain FromMarker<TDomain>(byte marker)
        where TDomain : unmanaged, Enum
        => Unsafe.As<byte, TDomain>(ref marker);

    /// <summary>
    /// Validates <typeparamref name="TDomain"/> once, at its first use: it must be
    /// byte-backed, and every declared value must be at or above
    /// <see cref="FirstMarker"/>. Two threads validating a domain at once each
    /// build the table privately and publish one; neither sees the other's
    /// writes.
    /// </summary>
    public static void Validate<TDomain>()
        where TDomain : unmanaged, Enum
        => _ = DeclaredMarkers<TDomain>();

    /// <summary>
    /// Whether <typeparamref name="TDomain"/> declares <paramref name="marker"/>:
    /// the check that lets a domain write a shared vocabulary's marker under
    /// its own declaration of that value.
    /// </summary>
    public static bool Declares<TDomain>(byte marker)
        where TDomain : unmanaged, Enum
        => DeclaredMarkers<TDomain>()[marker];

    /// <summary>
    /// The domain's declared markers as a 256-entry table, for a builder to
    /// hold by instance: a generic static read on every shared marker would be
    /// a lazy lookup per write under NativeAOT.
    /// </summary>
    internal static bool[] DeclaredMarkers<TDomain>()
        where TDomain : unmanaged, Enum
    {
        if (Validated<TDomain>.Declared is { } declared)
            return declared;
        bool[] built = Build<TDomain>();
        return Interlocked.CompareExchange(ref Validated<TDomain>.Declared, built, null) ?? built;
    }

    static bool[] Build<TDomain>()
        where TDomain : unmanaged, Enum
    {
        if (Unsafe.SizeOf<TDomain>() != 1)
            throw new InvalidOperationException($"Symbol Key domain {typeof(TDomain).Name} must be a byte-backed enum.");
        var declared = new bool[256];
        foreach (TDomain value in Enum.GetValues<TDomain>())
        {
            if (Marker(value) < FirstMarker)
                throw new InvalidOperationException($"Symbol Key domain {typeof(TDomain).Name} declares {value} below the marker range.");
            // One value, one name: a shared vocabulary's value landing on a
            // domain's own marker would read back as that marker.
            if (declared[Marker(value)])
                throw new InvalidOperationException($"Symbol Key domain {typeof(TDomain).Name} declares the value 0x{Marker(value):X2} twice.");
            declared[Marker(value)] = true;
        }
        return declared;
    }

    static class Validated<TDomain>
        where TDomain : unmanaged, Enum
    {
        public static bool[]? Declared;
    }
}

/// <summary>
/// A Symbol Key (<c>docs/design/symbol-key.md</c>): a domain-validated readonly
/// view over an <see cref="OpaqueByteRange"/> holding a sequence of
/// self-delimiting parts whose first part is a declared marker of
/// <typeparamref name="TDomain"/>. Two keys of one domain are equal exactly when
/// they hold the same parts, and they order by their bytes. A key is issued only
/// by a <see cref="SymbolKeyPopulation{TDomain}"/>, over a backing frozen before
/// the key existed.
/// </summary>
public readonly struct SymbolKey<TDomain> : IEquatable<SymbolKey<TDomain>>, IComparable<SymbolKey<TDomain>>
    where TDomain : unmanaged, Enum
{
    readonly OpaqueByteRange _range;

    internal SymbolKey(OpaqueByteRange range)
    {
        if (range.IsEmpty)
            throw new ArgumentException("A Symbol Key holds at least its domain marker.", nameof(range));
        if (range.ByteAt(0) < SymbolKeyDomain.FirstMarker)
            throw new ArgumentException("A Symbol Key begins with a domain marker.", nameof(range));
        _range = range;
    }

    /// <summary>The range the key retains; the key never exposes its bytes.</summary>
    internal OpaqueByteRange Range => _range;

    /// <summary>True for the default value, which is no key.</summary>
    public bool IsDefault => _range.IsEmpty;

    /// <summary>The key's shape: its first part, a domain marker.</summary>
    public TDomain Kind
        => IsDefault
            ? throw new InvalidOperationException("The default Symbol Key has no kind.")
            : SymbolKeyDomain.FromMarker<TDomain>(_range.ByteAt(0));

    public int Length => _range.Length;

    /// <summary>A scoped reader over the key's parts, for matchers and diagnostics.</summary>
    public SymbolKeyPartReader<TDomain> Parts => new(_range);

    public bool Equals(SymbolKey<TDomain> other) => _range.Equals(other._range);

    /// <summary>Whether a staged key holds the same parts as this key.</summary>
    public bool Equals(StagedSymbolKey<TDomain> other) => _range.SequenceEqual(other.Bytes);

    public override bool Equals(object? obj) => obj is SymbolKey<TDomain> other && Equals(other);

    public override int GetHashCode() => _range.GetHashCode();

    public int CompareTo(SymbolKey<TDomain> other) => _range.CompareTo(other._range);

    public static bool operator ==(SymbolKey<TDomain> left, SymbolKey<TDomain> right) => left.Equals(right);

    public static bool operator !=(SymbolKey<TDomain> left, SymbolKey<TDomain> right) => !left.Equals(right);
}

/// <summary>The part a <see cref="SymbolKeyPartReader{TDomain}"/> is positioned on.</summary>
public enum SymbolKeyPartKind
{
    Marker,
    Int,
    Name,
    Key,
    Blob,
    Ref,
}

/// <summary>
/// A bounded structural reader over one key's parts. Each advance consumes one
/// tag and, for a payload part, its shortest-form LEB128 length or value, and
/// exposes the part as a semantic value, a typed <see cref="SymbolKeyName"/>, a
/// nested key, or an opaque blob range. It never exposes the key's bytes.
/// </summary>
public ref struct SymbolKeyPartReader<TDomain>
    where TDomain : unmanaged, Enum
{
    readonly SymbolKeyPartBytes _key;
    int _position;
    SymbolKeyPartKind _kind;
    TDomain _marker;
    long _int;
    SymbolKeyPartBytes _payload;

    internal SymbolKeyPartReader(OpaqueByteRange key)
    {
        _key = new SymbolKeyPartBytes(key);
        _position = 0;
    }

    internal SymbolKeyPartReader(ReadOnlySpan<byte> staged)
    {
        _key = new SymbolKeyPartBytes(staged);
        _position = 0;
    }

    internal SymbolKeyPartReader<TDomain> NestedParts => new(_payload);

    SymbolKeyPartReader(SymbolKeyPartBytes bytes)
    {
        _key = bytes;
        _position = 0;
    }

    internal int ComparePrefix(SymbolKeyPartReader<TDomain> other)
        => _key.Slice(0, _position).CompareTo(other._key.Slice(0, other._position));

    internal bool SamePart(SymbolKeyPartReader<TDomain> other)
        => _kind == other._kind && _kind switch
        {
            SymbolKeyPartKind.Marker => EqualityComparer<TDomain>.Default.Equals(_marker, other._marker),
            SymbolKeyPartKind.Int or SymbolKeyPartKind.Ref => _int == other._int,
            _ => _payload.CompareTo(other._payload) == 0,
        };

    /// <summary>Copies the payload of a Name, Key, or Blob part, staged or frozen.</summary>
    internal byte[] CopyPayload()
    {
        byte[] bytes = new byte[_payload.Length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = _payload.ByteAt(i);
        return bytes;
    }

    internal bool PayloadStartsWith(byte value)
        => _payload.Length != 0 && _payload.ByteAt(0) == value;

    /// <summary>Whether a part remains.</summary>
    public bool HasNext => _position < _key.Length;

    public SymbolKeyPartKind Kind => _kind;

    /// <summary>The marker of a <see cref="SymbolKeyPartKind.Marker"/> part.</summary>
    public TDomain Marker => _marker;

    /// <summary>The value of an <see cref="SymbolKeyPartKind.Int"/> part.</summary>
    public long Int => _int;

    /// <summary>The typed view of a <see cref="SymbolKeyPartKind.Name"/> part.</summary>
    public SymbolKeyName Name => new(_payload.Frozen);

    /// <summary>The nested key of a <see cref="SymbolKeyPartKind.Key"/> part.</summary>
    public SymbolKey<TDomain> Key => new(_payload.Frozen);

    /// <summary>The opaque range of a <see cref="SymbolKeyPartKind.Blob"/> part.</summary>
    public OpaqueByteRange Blob => _payload.Frozen;

    /// <summary>
    /// The id of a <see cref="SymbolKeyPartKind.Ref"/> part, meaningful only
    /// in the <see cref="SymbolKeyInterner{TDomain}"/> that issued it.
    /// </summary>
    public int Id => (int)_int;

    /// <summary>The payload range of a Name, Key, or Blob part, for the expansion that copies it.</summary>
    internal OpaqueByteRange Payload => _payload.Frozen;

    /// <summary>Advances to the next part; false after the last.</summary>
    public bool Read()
    {
        if (_position >= _key.Length)
            return false;
        byte tag = _key.ByteAt(_position++);
        if (tag >= SymbolKeyDomain.FirstMarker)
        {
            _kind = SymbolKeyPartKind.Marker;
            _marker = SymbolKeyDomain.FromMarker<TDomain>(tag);
            return true;
        }

        switch ((SymbolKeyTag)tag)
        {
            case SymbolKeyTag.Int:
                ulong zigzag = ReadVarint();
                _int = (long)(zigzag >> 1) ^ -(long)(zigzag & 1);
                _kind = SymbolKeyPartKind.Int;
                return true;
            case SymbolKeyTag.Name:
                _payload = ReadPayload();
                _kind = SymbolKeyPartKind.Name;
                return true;
            case SymbolKeyTag.Key:
                _payload = ReadPayload();
                _kind = SymbolKeyPartKind.Key;
                return true;
            case SymbolKeyTag.Blob:
                _payload = ReadPayload();
                _kind = SymbolKeyPartKind.Blob;
                return true;
            case SymbolKeyTag.Ref:
                ulong id = ReadVarint();
                if (id > int.MaxValue)
                    throw new InvalidOperationException("A key holds a Ref part whose id exceeds the table range.");
                _int = (long)id;
                _kind = SymbolKeyPartKind.Ref;
                return true;
            default:
                throw new InvalidOperationException($"The key holds an unsupported part tag 0x{tag:X2}.");
        }
    }

    SymbolKeyPartBytes ReadPayload()
    {
        ulong length = ReadVarint();
        if (length > (ulong)(_key.Length - _position))
            throw new InvalidOperationException("A key part's length exceeds the key.");
        SymbolKeyPartBytes payload = _key.Slice(_position, (int)length);
        _position += (int)length;
        return payload;
    }

    ulong ReadVarint()
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (_position >= _key.Length)
                throw new InvalidOperationException("A key ends inside an integer part.");
            byte b = _key.ByteAt(_position++);
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
                return value;
        }
        throw new InvalidOperationException("A key holds an integer part wider than 64 bits.");
    }
}

// One grammar reader over either immutable key storage or a borrowed staging
// span. A staged part can be compared or descended into without copying it;
// only an immutable part can issue a public frozen key/name/blob view.
internal readonly ref struct SymbolKeyPartBytes
{
    readonly OpaqueByteRange _frozen;
    readonly ReadOnlySpan<byte> _staged;

    internal SymbolKeyPartBytes(OpaqueByteRange frozen) => _frozen = frozen;
    internal SymbolKeyPartBytes(ReadOnlySpan<byte> staged) => _staged = staged;
    internal int Length => _staged.IsEmpty ? _frozen.Length : _staged.Length;
    internal byte ByteAt(int offset) => _staged.IsEmpty ? _frozen.ByteAt(offset) : _staged[offset];
    internal OpaqueByteRange Frozen => _staged.IsEmpty
        ? _frozen
        : throw new SymbolKeyStateException("A staged part has no frozen view.");
    internal SymbolKeyPartBytes Slice(int offset, int length)
        => _staged.IsEmpty ? new(_frozen.Slice(offset, length)) : new(_staged.Slice(offset, length));
    internal int CompareTo(SymbolKeyPartBytes other)
    {
        if (!_staged.IsEmpty && !other._staged.IsEmpty)
            return _staged.SequenceCompareTo(other._staged);
        if (_staged.IsEmpty && other._staged.IsEmpty)
            return _frozen.CompareTo(other._frozen);
        int common = Math.Min(Length, other.Length);
        for (int i = 0; i < common; i++)
        {
            int order = ByteAt(i).CompareTo(other.ByteAt(i));
            if (order != 0)
                return order;
        }
        return Length.CompareTo(other.Length);
    }
}
