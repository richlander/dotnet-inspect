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
    /// <see cref="FirstMarker"/>.
    /// </summary>
    public static void Validate<TDomain>()
        where TDomain : unmanaged, Enum
    {
        if (Validated<TDomain>.Done)
            return;
        if (Unsafe.SizeOf<TDomain>() != 1)
            throw new InvalidOperationException($"Symbol Key domain {typeof(TDomain).Name} must be a byte-backed enum.");
        foreach (TDomain value in Enum.GetValues<TDomain>())
        {
            if (Marker(value) < FirstMarker)
                throw new InvalidOperationException($"Symbol Key domain {typeof(TDomain).Name} declares {value} below the marker range.");
            // One value, one name: a shared vocabulary's value landing on a
            // domain's own marker would read back as that marker.
            if (Validated<TDomain>.Declared[Marker(value)])
                throw new InvalidOperationException($"Symbol Key domain {typeof(TDomain).Name} declares the value 0x{Marker(value):X2} twice.");
            Validated<TDomain>.Declared[Marker(value)] = true;
        }
        Validated<TDomain>.Done = true;
    }

    /// <summary>
    /// Whether <typeparamref name="TDomain"/> declares <paramref name="marker"/>:
    /// the check that lets a domain write a shared vocabulary's marker under
    /// its own declaration of that value.
    /// </summary>
    public static bool Declares<TDomain>(byte marker)
        where TDomain : unmanaged, Enum
    {
        Validate<TDomain>();
        return Validated<TDomain>.Declared[marker];
    }

    static class Validated<TDomain>
        where TDomain : unmanaged, Enum
    {
        public static bool Done;

        public static readonly bool[] Declared = new bool[256];
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
    readonly OpaqueByteRange _key;
    int _position;
    SymbolKeyPartKind _kind;
    TDomain _marker;
    long _int;
    OpaqueByteRange _payload;

    internal SymbolKeyPartReader(OpaqueByteRange key)
    {
        _key = key;
        _position = 0;
    }

    /// <summary>Whether a part remains.</summary>
    public bool HasNext => _position < _key.Length;

    public SymbolKeyPartKind Kind => _kind;

    /// <summary>The marker of a <see cref="SymbolKeyPartKind.Marker"/> part.</summary>
    public TDomain Marker => _marker;

    /// <summary>The value of an <see cref="SymbolKeyPartKind.Int"/> part.</summary>
    public long Int => _int;

    /// <summary>The typed view of a <see cref="SymbolKeyPartKind.Name"/> part.</summary>
    public SymbolKeyName Name => new(_payload);

    /// <summary>The nested key of a <see cref="SymbolKeyPartKind.Key"/> part.</summary>
    public SymbolKey<TDomain> Key => new(_payload);

    /// <summary>The opaque range of a <see cref="SymbolKeyPartKind.Blob"/> part.</summary>
    public OpaqueByteRange Blob => _payload;

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
            default:
                throw new InvalidOperationException($"The key holds an unsupported part tag 0x{tag:X2}.");
        }
    }

    OpaqueByteRange ReadPayload()
    {
        ulong length = ReadVarint();
        if (length > (ulong)(_key.Length - _position))
            throw new InvalidOperationException("A key part's length exceeds the key.");
        OpaqueByteRange payload = _key.Slice(_position, (int)length);
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
