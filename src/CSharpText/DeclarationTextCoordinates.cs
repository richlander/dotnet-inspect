using System.Collections.Immutable;

namespace CSharpText;

internal readonly record struct SourceTextPoint(int Line, int Column);

internal readonly record struct SourceTextRange(
    SourceTextPoint Start,
    SourceTextPoint End)
{
    public LineRange Lines => new(Start.Line + 1, End.Line + 1);
}

internal sealed record DeclarationTextCoordinates(
    SourceTextRange Declaration,
    SourceTextRange Signature,
    SourceTextRange? Body,
    SourceTextPoint TerminalEnd,
    ImmutableArray<SourceTextRange> XmlDocumentation,
    ImmutableArray<SourceTextRange> Attributes,
    bool DeclarationKnown,
    bool DocumentationKnown,
    bool IsKnown);
