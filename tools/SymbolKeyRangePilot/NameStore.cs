using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

using ILInspector.Metadata;

namespace SymbolKeyRangePilot;

internal enum KeyStrategy
{
    PrivatePerKey,
    OpaqueRangePopulation,
}

internal abstract class KeyStore
{
    internal abstract int Count { get; }

    internal abstract int EncodedBytes { get; }

    internal abstract bool Matches(KeyStore other);

    internal abstract int KeyHash();

    internal abstract int ValueTextEquals(ReadOnlySpan<byte> query);

    internal abstract ReadOnlySpan<byte> Bytes(int index);

    internal bool HasSameBytes(KeyStore other)
    {
        if (other.Count != Count)
            return false;
        for (int index = 0; index < Count; index++)
        {
            if (!Bytes(index).SequenceEqual(other.Bytes(index)))
                return false;
        }
        return true;
    }

    internal static KeyStore Build(
        NameAsset asset,
        KeyStrategy strategy,
        int copies) =>
        strategy switch
        {
            KeyStrategy.PrivatePerKey =>
                PrivateKeyStore.Build(asset, copies),
            KeyStrategy.OpaqueRangePopulation =>
                RangeKeyStore.Build(asset, copies),
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy)),
        };
}

internal sealed class PrivateKeyStore(
    SymbolKey[] keys,
    int encodedBytes) : KeyStore
{
    readonly SymbolKey[] _keys = keys;

    internal override int Count => _keys.Length;

    internal override int EncodedBytes => encodedBytes;

    internal static PrivateKeyStore Build(
        NameAsset asset,
        int copies)
    {
        var keys = new SymbolKey[
            checked(asset.KeyCount * copies)];
        var builder = new SymbolKey.Builder(
            new SymbolKey.Utf8Names(
                asset.Image,
                asset.Reader));
        int index = 0;
        int encodedBytes = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            int copyStart = index;
            foreach (KeyEntry entry in asset.Entries)
            {
                int mark = builder.Begin();
                builder.Mark(KeyFormat.Marker(entry.Role));
                if (KeyFormat.IsType(entry.Role))
                {
                    if (!entry.Namespace.IsNil)
                    {
                        builder
                            .Mark(SymbolPart.Namespace)
                            .Name(entry.Namespace);
                    }
                }
                else
                {
                    builder.Key(
                        keys[copyStart + entry.ParentIndex]);
                }
                builder.Name(entry.Name);
                if (!entry.Signature.IsNil)
                {
                    builder.Blob(
                        asset.Reader.GetBlobReader(
                            entry.Signature));
                }
                SymbolKey key = builder.End(mark);
                keys[index++] = key;
                encodedBytes = checked(
                    encodedBytes + key.Bytes.Length);
            }
        }
        return new(keys, encodedBytes);
    }

    internal override bool Matches(KeyStore other)
    {
        if (other is not PrivateKeyStore right
            || right._keys.Length != _keys.Length)
        {
            return false;
        }
        for (int index = 0; index < _keys.Length; index++)
        {
            if (_keys[index] != right._keys[index])
                return false;
        }
        return true;
    }

    internal override int KeyHash()
    {
        var hash = new HashCode();
        foreach (SymbolKey key in _keys)
            hash.Add(key);
        return hash.ToHashCode();
    }

    internal override int ValueTextEquals(
        ReadOnlySpan<byte> query)
    {
        int matches = 0;
        foreach (SymbolKey key in _keys)
        {
            KeyFormat.NameBounds(
                key.Bytes,
                out int offset,
                out int length);
            if (key.Bytes.Slice(offset, length)
                .SequenceEqual(query))
            {
                matches++;
            }
        }
        return matches;
    }

    internal override ReadOnlySpan<byte> Bytes(int index)
        => _keys[index].Bytes;
}

internal sealed class RangeKeyStore(
    RangeKey[] keys,
    int encodedBytes) : KeyStore
{
    readonly RangeKey[] _keys = keys;

    internal override int Count => _keys.Length;

    internal override int EncodedBytes => encodedBytes;

    internal static RangeKeyStore Build(
        NameAsset asset,
        int copies)
    {
        int count = checked(asset.KeyCount * copies);
        int encodedBytes = KeyFormat.Measure(asset, copies);
        var slots = new RangeSlot[count];
        var builder = new PopulationBuilder(encodedBytes);
        int index = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            int copyStart = index;
            foreach (KeyEntry entry in asset.Entries)
            {
                int mark = builder.Begin();
                builder.Mark(KeyFormat.Marker(entry.Role));
                if (KeyFormat.IsType(entry.Role))
                {
                    if (!entry.Namespace.IsNil)
                    {
                        builder.Mark(SymbolPart.Namespace);
                        builder.Name(
                            asset.Names,
                            entry.Namespace);
                    }
                }
                else
                {
                    builder.Key(
                        slots[copyStart + entry.ParentIndex]);
                }
                builder.Name(asset.Names, entry.Name);
                if (!entry.Signature.IsNil)
                {
                    builder.Blob(
                        asset.Reader.GetBlobReader(
                            entry.Signature));
                }
                slots[index++] = builder.End(mark);
            }
        }

        ByteOwner owner = builder.Freeze();
        var keys = new RangeKey[slots.Length];
        for (index = 0; index < slots.Length; index++)
        {
            RangeSlot slot = slots[index];
            keys[index] = new(
                new OpaqueByteRange(
                    owner,
                    slot.Offset,
                    slot.Length));
        }
        return new(keys, encodedBytes);
    }

    internal override bool Matches(KeyStore other)
    {
        if (other is not RangeKeyStore right
            || right._keys.Length != _keys.Length)
        {
            return false;
        }
        for (int index = 0; index < _keys.Length; index++)
        {
            if (_keys[index] != right._keys[index])
                return false;
        }
        return true;
    }

    internal override int KeyHash()
    {
        var hash = new HashCode();
        foreach (RangeKey key in _keys)
            hash.Add(key);
        return hash.ToHashCode();
    }

    internal override int ValueTextEquals(
        ReadOnlySpan<byte> query)
    {
        int matches = 0;
        foreach (RangeKey key in _keys)
        {
            Utf8RangeReader reader = key.NameReader();
            if (reader.ValueTextEquals(query))
                matches++;
        }
        return matches;
    }

    internal override ReadOnlySpan<byte> Bytes(int index)
        => _keys[index].Bytes;
}

internal static class KeyFormat
{
    internal static SymbolPart Marker(KeyRole role) =>
        role switch
        {
            KeyRole.TypeDefinition
                or KeyRole.TypeReference => SymbolPart.Type,
            KeyRole.Method => SymbolPart.Method,
            KeyRole.Property => SymbolPart.Property,
            KeyRole.Event => SymbolPart.Event,
            KeyRole.Field => SymbolPart.Field,
            _ => throw new ArgumentOutOfRangeException(
                nameof(role)),
        };

    internal static bool IsType(KeyRole role)
        => role is KeyRole.TypeDefinition
            or KeyRole.TypeReference;

    internal static int Measure(
        NameAsset asset,
        int copies)
        => checked(asset.EncodedKeyBytes * copies);

    internal static void NameBounds(
        ReadOnlySpan<byte> key,
        out int offset,
        out int length)
    {
        int position = 0;
        offset = -1;
        length = 0;
        while (position < key.Length)
        {
            SymbolPart part = (SymbolPart)key[position++];
            switch (part)
            {
                case SymbolPart.Int:
                    _ = ReadVarint(key, ref position);
                    break;
                case SymbolPart.Name:
                case SymbolPart.Key:
                case SymbolPart.Blob:
                    int payloadLength = checked(
                        (int)ReadVarint(
                            key,
                            ref position));
                    if ((uint)payloadLength
                        > (uint)(key.Length - position))
                    {
                        throw new InvalidOperationException(
                            "A pilot key part exceeds its range.");
                    }
                    if (part is SymbolPart.Name)
                    {
                        offset = position;
                        length = payloadLength;
                    }
                    position += payloadLength;
                    break;
            }
        }
        if (offset < 0)
        {
            throw new InvalidOperationException(
                "The pilot key does not contain a Name.");
        }
    }

    static ulong ReadVarint(
        ReadOnlySpan<byte> bytes,
        ref int position)
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if ((uint)position >= (uint)bytes.Length)
            {
                throw new InvalidOperationException(
                    "The pilot key ends inside a length.");
            }
            byte next = bytes[position++];
            value |= (ulong)(next & 0x7F) << shift;
            if (next < 0x80)
                return value;
        }
        throw new InvalidOperationException(
            "The pilot key length is too large.");
    }
}

internal sealed class ByteOwner(byte[] bytes)
{
    internal byte[] Bytes { get; } = bytes;
}

internal readonly struct OpaqueByteRange :
    IEquatable<OpaqueByteRange>,
    IComparable<OpaqueByteRange>
{
    readonly ByteOwner? _owner;
    readonly int _offset;
    readonly int _length;

    internal OpaqueByteRange(
        ByteOwner owner,
        int offset,
        int length)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if ((uint)offset > (uint)owner.Bytes.Length
            || (uint)length
                > (uint)(owner.Bytes.Length - offset))
        {
            throw new ArgumentOutOfRangeException(
                nameof(length));
        }
        _owner = owner;
        _offset = offset;
        _length = length;
    }

    OpaqueByteRange(
        ByteOwner owner,
        RangeSlot validated)
    {
        _owner = owner;
        _offset = validated.Offset;
        _length = validated.Length;
    }

    internal ReadOnlySpan<byte> Bytes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _owner is null
            ? []
            : _owner.Bytes.AsSpan(_offset, _length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal OpaqueByteRange Slice(
        int offset,
        int length)
    {
        if (_owner is null)
        {
            if (offset == 0 && length == 0)
                return default;
            throw new ArgumentOutOfRangeException(
                nameof(length));
        }
        if ((uint)offset > (uint)_length
            || (uint)length > (uint)(_length - offset))
        {
            throw new ArgumentOutOfRangeException(
                nameof(length));
        }
        return new(
            _owner,
            new RangeSlot(
                _offset + offset,
                length));
    }

    public bool Equals(OpaqueByteRange other)
        => Bytes.SequenceEqual(other.Bytes);

    public override bool Equals(object? obj)
        => obj is OpaqueByteRange other
            && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Bytes);
        return hash.ToHashCode();
    }

    public int CompareTo(OpaqueByteRange other)
        => Bytes.SequenceCompareTo(other.Bytes);

    public static bool operator ==(
        OpaqueByteRange left,
        OpaqueByteRange right) =>
        left.Equals(right);

    public static bool operator !=(
        OpaqueByteRange left,
        OpaqueByteRange right) =>
        !left.Equals(right);
}

internal readonly struct RangeKey(
    OpaqueByteRange value) :
    IEquatable<RangeKey>
{
    readonly OpaqueByteRange _value = value;

    internal ReadOnlySpan<byte> Bytes => _value.Bytes;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Utf8RangeReader NameReader()
    {
        KeyFormat.NameBounds(
            _value.Bytes,
            out int offset,
            out int length);
        return new(_value.Slice(offset, length));
    }

    public bool Equals(RangeKey other)
        => _value.Equals(other._value);

    public override bool Equals(object? obj)
        => obj is RangeKey other && Equals(other);

    public override int GetHashCode()
        => _value.GetHashCode();

    public static bool operator ==(
        RangeKey left,
        RangeKey right) =>
        left.Equals(right);

    public static bool operator !=(
        RangeKey left,
        RangeKey right) =>
        !left.Equals(right);
}

internal readonly ref struct Utf8RangeReader(
    OpaqueByteRange value)
{
    readonly OpaqueByteRange _value = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ValueTextEquals(
        ReadOnlySpan<byte> query) =>
        _value.Bytes.SequenceEqual(query);
}

internal readonly record struct RangeSlot(
    int Offset,
    int Length);

internal ref struct PopulationBuilder
{
    readonly byte[] _buffer;
    int _length;

    internal PopulationBuilder(int capacity)
    {
        _buffer = new byte[capacity];
        _length = 0;
    }

    internal int Begin() => _length;

    internal RangeSlot End(int mark)
        => new(mark, checked(_length - mark));

    internal void Mark(SymbolPart part)
    {
        Ensure(1);
        _buffer[_length++] = (byte)part;
    }

    internal void Key(RangeSlot slot)
    {
        Mark(SymbolPart.Key);
        WriteVarint((uint)slot.Length);
        Ensure(slot.Length);
        _buffer.AsSpan(slot.Offset, slot.Length)
            .CopyTo(_buffer.AsSpan(_length));
        _length += slot.Length;
    }

    internal void Blob(BlobReader reader)
    {
        int length = reader.RemainingBytes;
        Mark(SymbolPart.Blob);
        WriteVarint((uint)length);
        Ensure(length);
        reader.ReadBytes(length, _buffer, _length);
        _length += length;
    }

    internal void Name(
        Utf8Heap names,
        StringHandle handle)
    {
        int length = names.Length(handle);
        Mark(SymbolPart.Name);
        WriteVarint((uint)length);
        Ensure(length);
        names.Copy(handle, _buffer, _length, length);
        _length += length;
    }

    internal ByteOwner Freeze()
    {
        if (_length != _buffer.Length)
        {
            throw new InvalidOperationException(
                "The measured backing length was not exact.");
        }
        return new(_buffer);
    }

    void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            Ensure(1);
            _buffer[_length++] = (byte)(value | 0x80);
            value >>= 7;
        }
        Ensure(1);
        _buffer[_length++] = (byte)value;
    }

    void Ensure(int count)
    {
        if (_buffer.Length - _length < count)
        {
            throw new InvalidOperationException(
                "The measured backing capacity was too small.");
        }
    }
}
