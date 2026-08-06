using System.Xml;

namespace DotnetInspector.Services;

/// <summary>
/// Rejects a package manifest that is not well-formed XML.
/// </summary>
/// <remarks>
/// The exception carries coordinates, never the parser's original message. An
/// <see cref="XmlException"/> message can quote the token that caused the failure, which is
/// artifact text and therefore cannot be copied to the diagnostic channel.
/// </remarks>
public sealed class NuspecParseException : Exception
{
    private NuspecParseException(int lineNumber, int linePosition)
        : base(Describe(lineNumber, linePosition))
    {
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    /// <summary>The one-based line where XML parsing failed, or zero when unavailable.</summary>
    public int LineNumber { get; }

    /// <summary>The one-based position where XML parsing failed, or zero when unavailable.</summary>
    public int LinePosition { get; }

    internal static NuspecParseException From(XmlException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new NuspecParseException(exception.LineNumber, exception.LinePosition);
    }

    private static string Describe(int lineNumber, int linePosition)
        => lineNumber > 0 && linePosition > 0
            ? $"Package manifest is not well-formed XML at line {lineNumber}, position {linePosition}."
            : "Package manifest is not well-formed XML.";
}
