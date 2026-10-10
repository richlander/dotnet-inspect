using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// One compared Fast Diff key or fact: a byte string that only
/// <see cref="Builder"/> can make.
/// </summary>
/// <remarks>
/// <para>
/// Injectivity is a function of construction, as containment is for
/// <c>InertString</c>. Every part a builder writes is self-delimiting: a
/// marker is one byte, an integer is a tagged variable-length number, and a
/// name, a nested key, or a blob is tagged and length-prefixed. A key is
/// therefore a sequence of parts that decodes one way only, so two keys are
/// equal exactly when they were built from the same parts. No name is
/// scanned or escaped, whatever characters it contains.
/// </para>
/// <para>
/// A name is the metadata's own UTF-8, copied from the <c>#Strings</c> heap
/// without decoding, so two names that differ only in ill-formed bytes stay
/// distinct rather than both decoding to U+FFFD.
/// </para>
/// </remarks>
internal readonly struct SymbolKey : IEquatable<SymbolKey>, IComparable<SymbolKey>
{
    readonly byte[] _bytes;

    SymbolKey(byte[] bytes) => _bytes = bytes;

    public ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>Whether this is the default value, which no builder makes.</summary>
    public bool IsDefault => _bytes is null;

    /// <summary>The first part's tag, which names a fact's kind.</summary>
    public SymbolPart Kind => (SymbolPart)_bytes[0];

    public bool Equals(SymbolKey other) => Bytes.SequenceEqual(other.Bytes);

    public override bool Equals(object? obj) => obj is SymbolKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    public int CompareTo(SymbolKey other) => Bytes.SequenceCompareTo(other.Bytes);

    public static bool operator ==(SymbolKey left, SymbolKey right) => left.Equals(right);

    public static bool operator !=(SymbolKey left, SymbolKey right) => !left.Equals(right);

    /// <summary>A diagnostic rendering of the parts; it is never compared.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        Render(text, Bytes);
        return text.ToString();
    }

    static void Render(StringBuilder text, ReadOnlySpan<byte> bytes)
    {
        int position = 0;
        while (position < bytes.Length)
        {
            if (position > 0)
                text.Append(' ');
            var part = (SymbolPart)bytes[position++];
            switch (part)
            {
                case SymbolPart.Int:
                    ulong zigzag = ReadVarint(bytes, ref position);
                    text.Append((long)(zigzag >> 1) ^ -(long)(zigzag & 1));
                    break;
                case SymbolPart.Name or SymbolPart.Key or SymbolPart.Blob:
                    int length = (int)ReadVarint(bytes, ref position);
                    ReadOnlySpan<byte> payload = bytes.Slice(position, length);
                    position += length;
                    if (part is SymbolPart.Name)
                        text.Append('"').Append(Encoding.UTF8.GetString(payload)).Append('"');
                    else if (part is SymbolPart.Blob)
                        text.Append("0x").Append(Convert.ToHexString(payload));
                    else
                    {
                        Render(text.Append('('), payload);
                        text.Append(')');
                    }
                    break;
                default:
                    text.Append(part);
                    break;
            }
        }
    }

    static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong value = 0;
        for (int shift = 0; ; shift += 7)
        {
            byte b = bytes[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
                return value;
        }
    }

    /// <summary>
    /// Builds keys in one reused buffer. Builds nest: a key built while
    /// another is open is written above it and removed when it ends.
    /// </summary>
    internal sealed class Builder(Utf8Names names)
    {
        byte[] _buffer = new byte[256];
        int _length;

        /// <summary>The heap the builder copies names from; rebound with the reader.</summary>
        public Utf8Names Names { get; set; } = names;

        /// <summary>Opens a key and returns its mark.</summary>
        public int Begin() => _length;

        /// <summary>Closes the key opened at <paramref name="mark"/>.</summary>
        public SymbolKey End(int mark)
        {
            var key = new SymbolKey(_buffer.AsSpan(mark, _length - mark).ToArray());
            _length = mark;
            return key;
        }

        /// <summary>Discards every open key, after a build was abandoned by an exception.</summary>
        public void Reset() => _length = 0;

        public Builder Mark(SymbolPart part)
        {
            Ensure(1);
            _buffer[_length++] = (byte)part;
            return this;
        }

        public Builder Int(long value)
        {
            Mark(SymbolPart.Int);
            WriteVarint((ulong)((value << 1) ^ (value >> 63)));
            return this;
        }

        public Builder Key(SymbolKey key) => Framed(SymbolPart.Key, key.Bytes);

        /// <summary>Appends the rest of a blob without copying it out first.</summary>
        public Builder Blob(BlobReader reader)
        {
            int length = reader.RemainingBytes;
            Mark(SymbolPart.Blob);
            WriteVarint((uint)length);
            Ensure(length);
            reader.ReadBytes(length, _buffer, _length);
            _length += length;
            return this;
        }

        /// <summary>Appends a <c>#Strings</c> name as its stored UTF-8.</summary>
        public Builder Name(StringHandle handle)
        {
            int length = Names.Length(handle);
            Mark(SymbolPart.Name);
            WriteVarint((uint)length);
            Ensure(length);
            Names.Copy(handle, _buffer, _length, length);
            _length += length;
            return this;
        }

        Builder Framed(SymbolPart part, ReadOnlySpan<byte> payload)
        {
            Mark(part);
            WriteVarint((uint)payload.Length);
            Ensure(payload.Length);
            payload.CopyTo(_buffer.AsSpan(_length));
            _length += payload.Length;
            return this;
        }

        void WriteVarint(ulong value)
        {
            Ensure(10);
            while (value >= 0x80)
            {
                _buffer[_length++] = (byte)(value | 0x80);
                value >>= 7;
            }
            _buffer[_length++] = (byte)value;
        }

        void Ensure(int count)
        {
            if (_buffer.Length - _length < count)
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
        }
    }

    /// <summary>
    /// Reads <c>#Strings</c> entries as their stored bytes, from a reader
    /// that applies no Windows Runtime projection, so every name has a heap
    /// offset.
    /// </summary>
    internal sealed class Utf8Names
    {
        BlobReader _heap;

        public Utf8Names(PEReader pe, MetadataReader md)
            => _heap = pe.GetMetadata().GetReader(
                md.GetHeapMetadataOffset(HeapIndex.String),
                md.GetHeapSize(HeapIndex.String));

        public int Length(StringHandle handle)
        {
            int offset = MetadataTokens.GetHeapOffset(handle);
            if (offset < 0 || offset >= _heap.Length)
                throw new BadImageFormatException("A name lies outside the #Strings heap.");
            _heap.Offset = offset;
            int length = _heap.IndexOf(0);
            return length >= 0
                ? length
                : throw new BadImageFormatException("A #Strings entry is not terminated.");
        }

        /// <summary>Copies the name <see cref="Length"/> measured.</summary>
        public void Copy(StringHandle handle, byte[] buffer, int index, int length)
        {
            _heap.Offset = MetadataTokens.GetHeapOffset(handle);
            _heap.ReadBytes(length, buffer, index);
        }
    }
}

/// <summary>
/// The tags of <see cref="SymbolKey"/> parts. Each marker names one key or
/// fact shape, so keys of different shapes never share a spelling.
/// </summary>
internal enum SymbolPart : byte
{
    // Payload parts.
    Int = 1,
    Name,
    Key,
    Blob,

    // Shared markers.
    Absent,
    NonApi,
    Visible,
    Hidden,
    Malformed,
    Namespace,
    Vararg,
    Return,

    // Keys.
    Type,
    Method,
    Field,
    Property,
    Event,
    Parameter,
    GenericParameter,
    MethodSpecification,
    LocalSignature,
    StandaloneSignature,

    // Signature Types.
    Primitive,
    SZArray,
    Array,
    ByReference,
    Pointer,
    FunctionPointer,
    GenericInstance,
    TypeParameter,
    MethodParameter,
    RequiredModifier,
    OptionalModifier,
    Pinned,

    // Facts.
    TypeFact,
    BeforeFieldInit,
    InterfaceFact,
    NullableContext,
    Layout,
    MethodImplementation,
    FieldFact,
    MethodFact,
    ParameterFact,
    Import,
    PropertyFact,
    EventFact,
    GenericParameterFact,
    Constraint,
    Attribute,
    Constant,
}
