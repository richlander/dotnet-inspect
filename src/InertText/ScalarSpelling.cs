namespace InertText.Encoding;

/// <summary>
/// The one spelling table both visual encoders size and classify from.
/// </summary>
/// <remarks>
/// <see cref="VisualEncoder"/> writes UTF-16 and <see cref="Utf8VisualEncoder"/> writes UTF-8, so
/// each keeps its own writer, but every escape is ASCII: a spelling has the same length in either
/// encoding. Keeping the length and form here means the two cannot disagree about which form a
/// scalar takes. <c>SpellingParityTests</c> gates the written output across every scalar.
/// </remarks>
internal static class ScalarSpelling
{
    /// <summary>
    /// The length and form of the escape for <paramref name="value"/>, a scalar or a lone
    /// surrogate code point.
    /// </summary>
    internal static (int Length, VisualForm Form) For(int value) =>
        value switch
        {
            '\\' => (2, VisualForm.Backslash),
            <= 0x1F => (3, VisualForm.Caret),
            0x7F => (3, VisualForm.CaretDelete),
            <= 0xFFFF => (6, VisualForm.BmpHex),
            _ => (10, VisualForm.AstralHex),
        };

    /// <summary>
    /// The code point an ill-formed UTF-8 byte is spelled as.
    /// </summary>
    /// <remarks>
    /// The surrogate-escape mapping (Python's PEP 383): a byte that is not part of a well-formed
    /// sequence is at least <c>0x80</c>, and maps to the lone low surrogate
    /// <c>U+DC80</c>–<c>U+DCFF</c>. The spelling is therefore the UTF-16 spelling of the
    /// surrogate-escaped string, so a UTF-8 value and its <see cref="InertString"/> projection
    /// agree, and each byte stays distinct and recoverable.
    /// </remarks>
    internal static int SurrogateEscape(byte value) => 0xDC00 + value;
}
