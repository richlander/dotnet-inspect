namespace InertText;

/// <summary>
/// A scoped reader for bytes that a typed domain view classifies as UTF-8 text.
/// </summary>
public readonly ref struct Utf8RangeReader
{
    private readonly ReadOnlySpan<byte> _source;
    private readonly OpaqueByteRange _retained;
    private readonly bool _hasRetainedSource;

    internal Utf8RangeReader(OpaqueByteRange source)
    {
        _source = source.Bytes;
        _retained = source;
        _hasRetainedSource = true;
    }

    internal Utf8RangeReader(ReadOnlySpan<byte> source)
    {
        _source = source;
        _retained = default;
        _hasRetainedSource = false;
    }

    internal ReadOnlySpan<byte> Source => _source;

    internal OpaqueByteRange RetainSource() =>
        _hasRetainedSource
            ? _retained
            : OpaqueByteRange.Copy(_source);

    /// <summary>
    /// Tests exact equality with a caller-supplied UTF-8 sequence without decoding the source.
    /// </summary>
    public bool ValueTextEquals(ReadOnlySpan<byte> utf8Text) =>
        _source.SequenceEqual(utf8Text);

    /// <summary>The number of classified source bytes.</summary>
    public int Length => _source.Length;
}
