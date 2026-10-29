using System.Buffers;
using System.Globalization;
using System.Collections.Immutable;
using System.Text;
using System.Reflection.Metadata;

using InertText;
using InertText.Encoding;

namespace ILInspector.Metadata;

/// <summary>
/// One metadata Name range whose UTF-8 meaning is established by the Metadata grammar.
/// </summary>
public readonly struct MetadataUtf8Name : IEquatable<MetadataUtf8Name>
{
    private readonly OpaqueByteRange _range;

    internal MetadataUtf8Name(OpaqueByteRange range) =>
        _range = range;

    /// <summary>Tests an exact caller-supplied UTF-8 sequence without decoding this Name.</summary>
    public bool ValueTextEquals(ReadOnlySpan<byte> utf8Text) =>
        Utf8RangeClassification.Read(_range).ValueTextEquals(utf8Text);

    internal OpaqueByteRange Range => _range;

    /// <summary>Constructs the policy-contained UTF-8 presentation value.</summary>
    public InertUtf8String Contain(TextPolicy policy) =>
        new(policy, Utf8RangeClassification.Read(_range));

    /// <summary>The number of exact metadata Name bytes.</summary>
    public int Length => _range.Length;

    /// <summary>
    /// Views the exact bytes of one bounded #Strings entry without decoding them.
    /// </summary>
    internal static unsafe ReadOnlySpan<byte> ReadExactBytes(BlobReader reader) =>
        new(reader.StartPointer, reader.Length);

    /// <summary>
    /// Detaches exact Name bytes and measures their contained Field spelling, which is the
    /// UTF-16 length a producer charges to <see cref="MetadataOperationDimension.RetainedText"/>.
    /// </summary>
    internal static MetadataUtf8Name Detach(
        ReadOnlySpan<byte> bytes,
        out int containedLength)
    {
        OpaqueByteRange range = OpaqueByteRange.Copy(bytes);
        containedLength = MeasureContainedLength(range);
        return new(range);
    }

    /// <summary>
    /// Detaches one population of exact Name bytes into a single backing, measuring each
    /// Name's contained Field spelling as <see cref="Detach"/> does.
    /// </summary>
    internal static MetadataUtf8Name[] DetachShared(
        IReadOnlyList<BlobReader> names,
        Span<int> containedLengths)
    {
        if (containedLengths.Length != names.Count)
        {
            throw new ArgumentException(
                "Each detached Name requires one contained length.",
                nameof(containedLengths));
        }

        int total = 0;
        for (int i = 0; i < names.Count; i++)
            total = checked(total + names[i].Length);

        var builder = new OpaqueByteRangeBuilder(total);
        var slots = new OpaqueByteRangeSlot[names.Count];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = builder.Append(ReadExactBytes(names[i]));

        OpaqueByteRange population = builder.Freeze();
        var detached = new MetadataUtf8Name[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            OpaqueByteRange range = population.Slice(slots[i]);
            containedLengths[i] = MeasureContainedLength(range);
            detached[i] = new(range);
        }
        return detached;
    }

    /// <summary>
    /// Views this Name without its canonical generic-arity suffix when that suffix declares
    /// exactly <paramref name="arity"/> parameters.
    /// </summary>
    /// <remarks>
    /// The suffix is the ASCII text <c>`N</c> for the decimal <paramref name="arity"/>, so the
    /// match is an exact comparison of the Name's tail with that text. A matching tail is the
    /// last backtick, has no leading zero, and follows a non-empty prefix, which are the
    /// <see cref="MetadataNameArity"/> rules for a canonical suffix. The result is a subrange
    /// of the same backing.
    /// </remarks>
    public bool TryRemoveAritySuffix(
        int arity,
        out MetadataUtf8Name withoutArity)
    {
        withoutArity = default;
        if (arity <= 0 || arity > MetadataNameArity.MaxArity)
            return false;

        Span<byte> suffix = stackalloc byte[1 + 5];
        suffix[0] = (byte)'`';
        if (!arity.TryFormat(
                suffix[1..],
                out int digits,
                provider: CultureInfo.InvariantCulture))
        {
            return false;
        }

        int suffixLength = 1 + digits;
        int prefixLength = _range.Length - suffixLength;
        if (prefixLength <= 0
            || !new MetadataUtf8Name(
                    _range.Slice(prefixLength, suffixLength))
                .ValueTextEquals(suffix[..suffixLength]))
        {
            return false;
        }

        withoutArity = new(_range.Slice(0, prefixLength));
        return true;
    }

    /// <summary>
    /// Splits an exact namespace Name into dot-separated components without
    /// decoding or copying them.
    /// </summary>
    public ImmutableArray<MetadataUtf8Name> SplitNamespaceComponents()
    {
        if (_range.IsEmpty)
            return [];

        Span<byte> storage = _range.Length <= 256
            ? stackalloc byte[_range.Length]
            : new byte[_range.Length];
        OpaqueByteRangeEncoder.Encode(_range, storage);
        ReadOnlySpan<byte> bytes = storage;
        var components =
            ImmutableArray.CreateBuilder<MetadataUtf8Name>();
        int start = 0;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)'.')
                continue;
            components.Add(
                new(_range.Slice(start, index - start)));
            start = index + 1;
        }
        components.Add(
            new(_range.Slice(start, bytes.Length - start)));
        return components.ToImmutable();
    }

    /// <summary>
    /// Writes the contained composed-name spelling used by C# Type presentation,
    /// preserving ordinary UTF-8 bytes while folding line terminators and making
    /// terminal and bidi controls visible.
    /// </summary>
    public void WriteCSharpComposedTo(
        IBufferWriter<byte> destination)
        => WriteCSharpComposedTo(
            destination,
            containFieldTab: false);

    /// <summary>
    /// Writes the contained composed-name spelling used inside a text field,
    /// additionally making a tab delimiter visible.
    /// </summary>
    public void WriteCSharpComposedFieldTo(
        IBufferWriter<byte> destination)
        => WriteCSharpComposedTo(
            destination,
            containFieldTab: true);

    void WriteCSharpComposedTo(
        IBufferWriter<byte> destination,
        bool containFieldTab)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Span<byte> storage = _range.Length <= 256
            ? stackalloc byte[_range.Length]
            : new byte[_range.Length];
        OpaqueByteRangeEncoder.Encode(_range, storage);
        ReadOnlySpan<byte> bytes = storage;
        if (!IsWellFormedUtf8(bytes))
        {
            Contain(TextPolicy.Field).WriteTo(destination);
            return;
        }

        int offset = 0;
        while (offset < bytes.Length)
        {
            OperationStatus status = Rune.DecodeFromUtf8(
                bytes[offset..],
                out Rune rune,
                out int consumed);
            if (status is not OperationStatus.Done)
            {
                throw new InvalidOperationException(
                    "A prevalidated UTF-8 Name did not decode.");
            }

            if (rune.Value == '\r'
                && offset + consumed < bytes.Length
                && bytes[offset + consumed] == (byte)'\n')
            {
                WriteByte(destination, (byte)' ');
                offset += consumed + 1;
                continue;
            }
            if (IsLineTerminator(rune.Value))
            {
                WriteByte(destination, (byte)' ');
            }
            else if (containFieldTab && rune.Value == '\t')
            {
                WriteBytes(destination, "\\^I"u8);
            }
            else if (IsRenderingHazard(rune))
            {
                WriteBmpEscape(destination, rune.Value);
            }
            else
            {
                WriteBytes(
                    destination,
                    bytes.Slice(offset, consumed));
            }
            offset += consumed;
        }
    }

    static bool IsWellFormedUtf8(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(
                    bytes,
                    out _,
                    out int consumed)
                is not OperationStatus.Done)
            {
                return false;
            }
            bytes = bytes[consumed..];
        }
        return true;
    }

    static bool IsLineTerminator(int scalar) =>
        scalar
            is '\r'
                or '\n'
                or '\f'
                or 0x0085
                or 0x2028
                or 0x2029;

    static bool IsRenderingHazard(Rune rune) =>
        rune.Value != '\t'
        && (Rune.GetUnicodeCategory(rune)
                is UnicodeCategory.Control
            || rune.Value
                is 0x061C
                    or 0x200E
                    or 0x200F
                    or >= 0x202A and <= 0x202E
                    or >= 0x2066 and <= 0x2069);

    static void WriteBmpEscape(
        IBufferWriter<byte> destination,
        int scalar)
    {
        Span<byte> text = stackalloc byte[6];
        text[0] = (byte)'\\';
        text[1] = (byte)'u';
        for (int index = 5; index >= 2; index--)
        {
            int digit = scalar & 0xF;
            text[index] = (byte)(
                digit < 10
                    ? '0' + digit
                    : 'A' + digit - 10);
            scalar >>= 4;
        }
        WriteBytes(destination, text);
    }

    static void WriteByte(
        IBufferWriter<byte> destination,
        byte value)
    {
        Span<byte> target = destination.GetSpan(1);
        target[0] = value;
        destination.Advance(1);
    }

    static void WriteBytes(
        IBufferWriter<byte> destination,
        ReadOnlySpan<byte> value)
    {
        value.CopyTo(destination.GetSpan(value.Length));
        destination.Advance(value.Length);
    }

    static int MeasureContainedLength(OpaqueByteRange range) =>
        Utf8VisualEncoder.MeasureInertStringLength(
            TextPolicy.Field,
            Utf8RangeClassification.Read(range));

    /// <summary>Compares exact Name bytes.</summary>
    public bool Equals(MetadataUtf8Name other) => _range.Equals(other._range);

    public override bool Equals(object? obj) =>
        obj is MetadataUtf8Name other && Equals(other);

    public override int GetHashCode() => _range.GetHashCode();

    public static bool operator ==(MetadataUtf8Name left, MetadataUtf8Name right) =>
        left.Equals(right);

    public static bool operator !=(MetadataUtf8Name left, MetadataUtf8Name right) =>
        !left.Equals(right);
}
