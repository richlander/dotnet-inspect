namespace InertText;

/// <summary>
/// A scoped reader for bytes that a typed domain view classifies as UTF-8 text.
/// </summary>
/// <remarks>
/// A typed domain view issues this reader through
/// <see cref="Encoding.Utf8RangeClassification.Read(OpaqueByteRange)"/>. Creating it neither
/// validates the bytes nor applies a <see cref="TextPolicy"/>.
/// </remarks>
public readonly ref struct Utf8RangeReader
{
    private readonly OpaqueByteRange _source;

    internal Utf8RangeReader(OpaqueByteRange source) =>
        _source = source;

    internal ReadOnlySpan<byte> Source => _source.Bytes;

    internal OpaqueByteRange RetainSource() => _source;

    /// <summary>
    /// Tests exact equality with a caller-supplied UTF-8 sequence without decoding the source.
    /// </summary>
    public bool ValueTextEquals(ReadOnlySpan<byte> utf8Text) =>
        _source.SequenceEqual(utf8Text);

    /// <summary>The number of classified source bytes.</summary>
    public int Length => _source.Length;
}
