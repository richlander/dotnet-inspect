using System.Globalization;
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
    internal bool TryRemoveAritySuffix(
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
