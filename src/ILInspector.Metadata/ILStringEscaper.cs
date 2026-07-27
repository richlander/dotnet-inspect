namespace ILInspector.Metadata;

/// <summary>
/// Escapes IL <c>ldstr</c> user-string operands into quoted string literals.
/// Both methods consume the same input (a raw <c>#US</c>-heap string that may
/// contain arbitrary control characters); they differ only in their output
/// contract:
/// <list type="bullet">
/// <item><see cref="ForDisplay"/> — human-readable rendering; escapes the common
/// backslash sequences plus every Unicode line terminator, and leaves other
/// control characters as-is.</item>
/// <item><see cref="ForIlasm"/> — round-trippable ilasm QSTRING literal; also
/// octal-escapes remaining control characters so ilasm can reassemble it.</item>
/// </list>
/// Used respectively by <see cref="ILTokenResolver"/> and <see cref="CanonicalIL"/>.
/// </summary>
public static class ILStringEscaper
{
    /// <summary>
    /// Escapes a user string for human-readable display: standard backslash
    /// escapes (<c>\ " \n \r \t \f</c>) plus <c>\uXXXX</c> for the remaining
    /// Unicode line terminators (NEL, LS, PS).
    /// </summary>
    /// <remarks>
    /// Every character C# and Markdown treat as a line terminator is escaped, not
    /// just the common three. Display output is line-oriented — the <c>IL</c>
    /// section is one instruction per line, and <c>Annotated Source</c> renders
    /// each instruction inside a <c>//</c> comment — so a raw terminator here
    /// splits the line that frames it. Escaping rather than folding keeps the
    /// operand lossless: the reader still sees exactly which terminator the
    /// string holds.
    /// Gate: <c>ILStringEscaperTests</c> and
    /// <c>UntrustedIlPresentationTests</c>.
    /// </remarks>
    public static string ForDisplay(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\f': sb.Append("\\f"); break;
                case '\u0085' or '\u2028' or '\u2029':
                    sb.Append("\\u").Append(((int)c).ToString("X4"));
                    break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Escapes a user string for a round-trippable ilasm QSTRING literal: standard
    /// backslash escapes plus octal escapes for remaining control characters.
    /// </summary>
    public static string ForIlasm(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case < ' ' or '\x7f': sb.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0')); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
