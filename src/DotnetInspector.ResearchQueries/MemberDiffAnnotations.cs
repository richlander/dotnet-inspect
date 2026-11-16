using System.Collections.Immutable;
using ILInspector.CSharp;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// Places a Public API Member diff's annotations from the declarations'
/// identified parts and the relation's classified changes
/// (docs/design/member-diff.md#document). Nothing is inferred from text: a
/// caret marks a part the model identified, and only within it does the
/// token comparison choose the differing span.
/// </summary>
internal static class MemberDiffAnnotations
{
    /// <summary>
    /// The part carets of two present sides. Parts correspond by kind, key,
    /// and occurrence. Either side unidentified places none.
    /// </summary>
    public static ImmutableArray<MemberDiffCaret> Carets(
        CSharpDeclarationParts baseline,
        CSharpDeclarationParts current)
    {
        if (baseline.Parts is not { } baselineParts || current.Parts is not { } currentParts)
            return [];

        var baselineByKey = baselineParts.ToDictionary(static part => (part.Kind, part.Key, part.Occurrence));
        var currentKeys = currentParts.Select(static part => (part.Kind, part.Key, part.Occurrence)).ToHashSet();
        var carets = new List<MemberDiffCaret>();
        var baselineLines = new LineIndex(baseline.Text);
        var currentLines = new LineIndex(current.Text);

        foreach (CSharpDeclarationPart part in currentParts)
        {
            if (!baselineByKey.TryGetValue((part.Kind, part.Key, part.Occurrence), out var counterpart))
            {
                Add(carets, currentLines, MemberDiffSideKind.Current, part, part.Start, part.Length, MemberDiffCaretKind.Added, null);
                continue;
            }

            string currentText = current.Text.Substring(part.Start, part.Length);
            string baselineText = baseline.Text.Substring(counterpart.Start, counterpart.Length);
            if (currentText == baselineText)
                continue;

            var (currentSpan, baselineSpan) = DifferingSpans(baselineText, currentText);
            if (currentSpan.Length > 0 && baselineSpan.Length > 0)
            {
                Add(
                    carets,
                    currentLines,
                    MemberDiffSideKind.Current,
                    part,
                    part.Start + currentSpan.Start,
                    currentSpan.Length,
                    MemberDiffCaretKind.Changed,
                    baselineText.Substring(baselineSpan.Start, baselineSpan.Length));
            }
            else if (currentSpan.Length > 0)
            {
                Add(carets, currentLines, MemberDiffSideKind.Current, part, part.Start + currentSpan.Start, currentSpan.Length, MemberDiffCaretKind.Added, null);
            }
            else if (baselineSpan.Length > 0)
            {
                Add(carets, baselineLines, MemberDiffSideKind.Baseline, counterpart, counterpart.Start + baselineSpan.Start, baselineSpan.Length, MemberDiffCaretKind.Removed, null);
            }
            else
            {
                // Only whitespace between tokens differs; the line comparison shows it.
            }
        }

        foreach (CSharpDeclarationPart part in baselineParts)
        {
            if (!currentKeys.Contains((part.Kind, part.Key, part.Occurrence)))
                Add(carets, baselineLines, MemberDiffSideKind.Baseline, part, part.Start, part.Length, MemberDiffCaretKind.Removed, null);
        }

        return [.. carets
            .OrderBy(static caret => caret.Side)
            .ThenBy(static caret => caret.Line)
            .ThenBy(static caret => caret.Start)];
    }

    /// <summary>
    /// The line that carries notes on a declaration: the line holding its name,
    /// or its last line when the parts are unidentified.
    /// </summary>
    public static int DeclarationLine(CSharpDeclarationParts declaration)
    {
        var lines = new LineIndex(declaration.Text);
        CSharpDeclarationPart? name = declaration.Parts?.FirstOrDefault(
            static part => part.Kind == CSharpDeclarationPartKind.Name);
        return name is null ? lines.Count - 1 : lines.Locate(name.Start).Line;
    }

    /// <summary>
    /// The relation's classified changes: every Member change whose subject
    /// is this pair's baseline or current Member, by anchor.
    /// </summary>
    public static ImmutableArray<MemberDiffChangeNote> ChangeNotes(
        ApiDiff diff,
        ApiMemberHandle? baseline,
        ApiMemberHandle? current,
        MemberDiffSideKind side,
        int line)
    {
        var notes = ImmutableArray.CreateBuilder<MemberDiffChangeNote>();
        foreach (TypeDiff typeDiff in diff.TypeDiffs)
        {
            foreach (ApiChange change in typeDiff.Changes)
            {
                if (change.Subject is not { Kind: ApiChangeSubjectKind.Member } subject)
                    continue;
                bool matches =
                    (baseline?.Anchor is { } baselineAnchor && subject.OldMember?.Anchor == baselineAnchor)
                    || (current?.Anchor is { } currentAnchor && subject.NewMember?.Anchor == currentAnchor);
                if (!matches)
                    continue;
                notes.Add(new MemberDiffChangeNote(
                    side,
                    line,
                    change.Kind,
                    change.Classification,
                    change.Category,
                    change.Message,
                    change.OldValue,
                    change.NewValue));
            }
        }
        return notes.ToImmutable();
    }

    static void Add(
        List<MemberDiffCaret> carets,
        LineIndex lines,
        MemberDiffSideKind side,
        CSharpDeclarationPart part,
        int start,
        int length,
        MemberDiffCaretKind kind,
        string? counterpart)
    {
        var (line, column) = lines.Locate(start);
        // A caret marks one line; a span reaching past the line's end stops there.
        int clipped = Math.Min(length, lines.Length(line) - column);
        if (clipped <= 0)
            return;
        carets.Add(new MemberDiffCaret(
            side,
            line,
            column,
            clipped,
            kind,
            ToPartKind(part.Kind),
            part.Key,
            part.Occurrence,
            counterpart));
    }

    static MemberDiffPartKind ToPartKind(CSharpDeclarationPartKind kind)
        => kind switch
        {
            CSharpDeclarationPartKind.Attribute => MemberDiffPartKind.Attribute,
            CSharpDeclarationPartKind.ReturnAttributes => MemberDiffPartKind.ReturnAttributes,
            CSharpDeclarationPartKind.Modifier => MemberDiffPartKind.Modifier,
            CSharpDeclarationPartKind.ReturnType => MemberDiffPartKind.ReturnType,
            CSharpDeclarationPartKind.Name => MemberDiffPartKind.Name,
            CSharpDeclarationPartKind.Parameter => MemberDiffPartKind.Parameter,
            CSharpDeclarationPartKind.Accessor => MemberDiffPartKind.Accessor,
            CSharpDeclarationPartKind.Constraint => MemberDiffPartKind.Constraint,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    /// <summary>
    /// The differing tokens of two spellings of one part: the span left after
    /// trimming the tokens they share at the start and at the end. A span runs
    /// from its first differing token's start to its last one's end.
    /// </summary>
    internal static ((int Start, int Length) Current, (int Start, int Length) Baseline) DifferingSpans(
        string baseline,
        string current)
    {
        var before = Tokenize(baseline);
        var after = Tokenize(current);
        int prefix = 0;
        while (prefix < before.Count && prefix < after.Count && before[prefix].Text == after[prefix].Text)
            prefix++;
        int suffix = 0;
        while (suffix < before.Count - prefix
            && suffix < after.Count - prefix
            && before[^(suffix + 1)].Text == after[^(suffix + 1)].Text)
        {
            suffix++;
        }
        return (Span(after, prefix, after.Count - suffix), Span(before, prefix, before.Count - suffix));

        static (int Start, int Length) Span(List<Token> tokens, int first, int end)
            => first >= end
                ? (0, 0)
                : (tokens[first].Start, tokens[end - 1].Start + tokens[end - 1].Text.Length - tokens[first].Start);
    }

    readonly record struct Token(int Start, string Text);

    /// <summary>
    /// C# tokens enough to tell a declaration's words apart: identifiers and
    /// keywords (with a leading <c>@</c>), numbers, string and character
    /// literals, and single punctuation characters.
    /// </summary>
    static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int index = 0;
        while (index < text.Length)
        {
            char character = text[index];
            int start = index;
            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }
            if (char.IsLetter(character) || character is '_' or '@')
            {
                index++;
                while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
                    index++;
            }
            else if (char.IsDigit(character))
            {
                index++;
                while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '.'))
                    index++;
            }
            else if (character is '"' or '\'')
            {
                index++;
                while (index < text.Length && text[index] != character)
                    index += text[index] == '\\' && index + 1 < text.Length ? 2 : 1;
                index = Math.Min(index + 1, text.Length);
            }
            else
            {
                index++;
            }
            tokens.Add(new Token(start, text[start..index]));
        }
        return tokens;
    }

    sealed class LineIndex
    {
        readonly string _text;
        readonly List<int> _starts = [0];

        public LineIndex(string text)
        {
            _text = text;
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] == '\n')
                    _starts.Add(index + 1);
            }
        }

        public int Count => _starts.Count;

        public (int Line, int Column) Locate(int offset)
        {
            int line = _starts.BinarySearch(offset);
            if (line < 0)
                line = ~line - 1;
            return (line, offset - _starts[line]);
        }

        public int Length(int line)
            => (line + 1 < _starts.Count ? _starts[line + 1] - 1 : _text.Length) - _starts[line];
    }
}
