using System.Collections.Immutable;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Decompiler;

/// <summary>Side of a structural comparison.</summary>
public enum CSharpStructuralSide
{
    /// <summary>The before document.</summary>
    Before,

    /// <summary>The after document.</summary>
    After,
}

/// <summary>Presentation-ready rich structural-diff row.</summary>
/// <param name="Change">Explicit structural outcome or outcomes.</param>
/// <param name="Structure">Stable-kind display transition.</param>
/// <param name="Region">Enclosing-region transition.</param>
/// <param name="Detail">
/// One-line before/after text transition, empty when no single-span inline
/// text is available or the selected text did not change.
/// </param>
/// <param name="BeforeSpans">Before absolute UTF-16 spans.</param>
/// <param name="AfterSpans">After absolute UTF-16 spans.</param>
/// <param name="Fidelity">Independent compile-back transition and optional note.</param>
public sealed record CSharpStructuralDiffDisplayRow(
    string Change,
    string Structure,
    string Region,
    string Detail,
    string BeforeSpans,
    string AfterSpans,
    string Fidelity);

/// <summary>
/// Producer-owned display projection for structural C# body comparison.
/// </summary>
public static class CSharpStructuralDiffPrinter
{
    const int MaximumInlineTransitionLength = 120;

    /// <summary>Projects typed structural rows without recomputing correspondence.</summary>
    public static ImmutableArray<CSharpStructuralDiffDisplayRow> ToDisplayRows(
        CSharpStructuralComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return
        [
            .. comparison.Rows.Select(row => new CSharpStructuralDiffDisplayRow(
                FormatChange(row.Change),
                FormatTransition(Contain(row.BeforeLabel), Contain(row.AfterLabel)),
                FormatTransition(row.BeforeRegion?.ToString(), row.AfterRegion?.ToString()),
                FormatDetail(comparison, row),
                FormatSpans(row.BeforeSpans),
                FormatSpans(row.AfterSpans),
                FormatFidelity(comparison.Fidelity)))
        ];
    }

    /// <summary>
    /// Renders one complete C# document with structural caret comments inserted
    /// directly below changed spans. Source lines remain unchanged and in order.
    /// </summary>
    public static string RenderAnnotatedBody(
        CSharpStructuralComparison comparison,
        CSharpStructuralSide side)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        if (!Enum.IsDefined(side))
            throw new ArgumentException($"Unknown structural comparison side: {side}.", nameof(side));

        var document = side == CSharpStructuralSide.Before
            ? comparison.Before
            : comparison.After;
        var lines = SplitLines(document.Text);
        EnsureDisplaySafe(lines, side);
        var annotationsByLine = new Dictionary<int, List<(IAnnotation Fact, AnnotationAnchor.CaretExtent Extent)>>();

        foreach (var row in comparison.Rows)
        {
            var spans = side == CSharpStructuralSide.Before ? row.BeforeSpans : row.AfterSpans;
            string? label = side == CSharpStructuralSide.Before ? row.BeforeLabel : row.AfterLabel;
            var region = side == CSharpStructuralSide.Before ? row.BeforeRegion : row.AfterRegion;
            if (label is null)
                continue;

            string annotationText =
                $"raise: {Contain(label)}{RegionSuffix(region)}"
                + TextTransitionSuffix(comparison, row, side);
            foreach (var span in spans)
            {
                for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    var line = lines[lineIndex];
                    int start = Math.Max(span.Start, line.Start);
                    int end = Math.Min(span.Start + span.Length, line.Start + line.Text.Length);
                    if (end <= start)
                        continue;

                    int column = start - line.Start;
                    int length = end - start;
                    ReadOnlySpan<char> coveredText = line.Text.AsSpan(column, length);
                    int visibleOffset = 0;
                    while (visibleOffset < coveredText.Length
                           && char.IsWhiteSpace(coveredText[visibleOffset]))
                    {
                        visibleOffset++;
                    }
                    if (visibleOffset < coveredText.Length)
                    {
                        column += visibleOffset;
                        length -= visibleOffset;
                    }

                    var fact = new StructuralAnnotation(annotationText);
                    if (!annotationsByLine.TryGetValue(lineIndex, out var lineAnnotations))
                        annotationsByLine[lineIndex] = lineAnnotations = [];
                    lineAnnotations.Add((
                        fact,
                        new AnnotationAnchor.CaretExtent(column, length)));
                }
            }
        }

        string memberIndent = AnnotationCaret.MemberIndent([.. lines.Select(static line => line.Text)]);
        var output = new List<string>(lines.Count + annotationsByLine.Count);
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            output.Add(line.Text);
            if (!annotationsByLine.TryGetValue(lineIndex, out var entries))
                continue;

            entries.Sort(static (left, right) =>
            {
                int result = left.Extent.Column.CompareTo(right.Extent.Column);
                return result != 0 ? result : right.Extent.Length.CompareTo(left.Extent.Length);
            });
            if (memberIndent.Contains('\t')
                || HasTabBeforeExtent(line.Text, entries)
                || !CanRenderInCommentGutter(entries, memberIndent.Length))
            {
                output.AddRange(RenderExactFallback(line.Text, entries));
                continue;
            }

            var facts = entries.Select(static entry => entry.Fact).ToArray();
            var extents = entries.ToDictionary(static entry => entry.Fact, static entry => entry.Extent);
            var rendered = AnnotationCaret.Render(
                line.Text,
                memberIndent,
                facts,
                extents: extents,
                alignDetailWithCaret: true);
            output.AddRange(rendered.Count > 0 ? rendered : RenderExactFallback(line.Text, entries));
        }

        return string.Join('\n', output);
    }

    static string FormatChange(CSharpStructuralChangeKind change)
    {
        var values = new List<string>(2);
        if (change.HasFlag(CSharpStructuralChangeKind.Added)) values.Add(nameof(CSharpStructuralChangeKind.Added));
        if (change.HasFlag(CSharpStructuralChangeKind.Removed)) values.Add(nameof(CSharpStructuralChangeKind.Removed));
        if (change.HasFlag(CSharpStructuralChangeKind.Changed)) values.Add(nameof(CSharpStructuralChangeKind.Changed));
        if (change.HasFlag(CSharpStructuralChangeKind.Moved)) values.Add(nameof(CSharpStructuralChangeKind.Moved));
        return string.Join(", ", values);
    }

    static string FormatTransition(string? before, string? after)
        => (before, after) switch
        {
            (null, { } added) => $"+ {added}",
            ({ } removed, null) => $"- {removed}",
            ({ } oldValue, { } newValue) when oldValue == newValue => oldValue,
            ({ } oldValue, { } newValue) => $"{oldValue} -> {newValue}",
            _ => "",
        };

    static string FormatSpans(ImmutableArray<AnnotatedSourceSpan> spans)
        => string.Join(", ", spans.Select(static span => $"[{span.Start}..{span.Start + span.Length})"));

    static string FormatFidelity(CSharpStructuralFidelityEvidence? fidelity)
    {
        if (fidelity is null)
            return "";
        string transition = $"{fidelity.Before} -> {fidelity.After}";
        return fidelity.Note is { Length: > 0 } note
            ? $"{transition}; {Contain(note)}"
            : transition;
    }

    static string RegionSuffix(PrintedRegionRole? region)
        => region switch
        {
            PrintedRegionRole.Case => " case body",
            PrintedRegionRole.Body => " body",
            PrintedRegionRole.Else => " else clause",
            PrintedRegionRole.Catch => " catch clause",
            PrintedRegionRole.Finally => " finally clause",
            PrintedRegionRole.Header => " header",
            PrintedRegionRole.Construct => " construct",
            _ => "",
        };

    /// <summary>
    /// One-line before/after text transition for the <c>Detail</c> column,
    /// reusing the same single-span, length, and well-formedness guards as
    /// the inline "changed to/from" caret suffix. Empty when the selected
    /// text is not eligible for inline display or did not change. When the
    /// row matches the item-3 qualifier/argument role transition (see
    /// <see cref="TryDescribeQualifierArgumentRoleTransition"/>), the semantic
    /// summary replaces the literal before/after text dump.
    /// </summary>
    static string FormatDetail(CSharpStructuralComparison comparison, CSharpStructuralDiffRow row)
    {
        bool textChanged = row.Change.HasFlag(CSharpStructuralChangeKind.Changed)
            && !CSharpBodyDiff.SelectedTextEqual(
                comparison.Before,
                row.BeforeSpans,
                comparison.After,
                row.AfterSpans);
        bool added = row.Change.HasFlag(CSharpStructuralChangeKind.Added);
        bool removed = row.Change.HasFlag(CSharpStructuralChangeKind.Removed);
        if (!textChanged && !added && !removed)
            return "";

        string? beforeText = InlineText(comparison.Before, row.BeforeSpans);
        string? afterText = InlineText(comparison.After, row.AfterSpans);
        if (textChanged
            && IsInvocationRoleCandidate(row)
            && beforeText is not null
            && afterText is not null)
        {
            if (TryDescribeQualifierArgumentRoleTransition(beforeText, afterText, out var qualifierTransition))
                return qualifierTransition.DetailSummary;
            if (TryDescribeCalleeRenamedRoleTransition(comparison, beforeText, afterText, out var calleeTransition))
                return calleeTransition.DetailSummary;
        }

        return beforeText is null && afterText is null
            ? ""
            : FormatTransition(beforeText, afterText);
    }

    static string? InlineText(AnnotatedSourceDocument document, ImmutableArray<AnnotatedSourceSpan> spans)
    {
        if (spans.Length != 1 || spans[0].Length > MaximumInlineTransitionLength)
            return null;

        string text = SelectText(document, spans[0]);
        return CanRenderExactInline(text) ? Contain(text) : null;
    }

    static string TextTransitionSuffix(
        CSharpStructuralComparison comparison,
        CSharpStructuralDiffRow row,
        CSharpStructuralSide side)
    {
        if (!row.Change.HasFlag(CSharpStructuralChangeKind.Changed)
            || CSharpBodyDiff.SelectedTextEqual(
                comparison.Before,
                row.BeforeSpans,
                comparison.After,
                row.AfterSpans))
            return "";

        if (row.BeforeSpans.Length != 1 || row.AfterSpans.Length != 1)
            return "; text changed";

        var beforeSpan = row.BeforeSpans[0];
        var afterSpan = row.AfterSpans[0];
        if (beforeSpan.Length > MaximumInlineTransitionLength
            || afterSpan.Length > MaximumInlineTransitionLength)
        {
            return "; text changed";
        }

        string beforeText = SelectText(comparison.Before, beforeSpan);
        string afterText = SelectText(comparison.After, afterSpan);
        if (!CanRenderExactInline(beforeText) || !CanRenderExactInline(afterText))
            return "; text changed";

        if (IsInvocationRoleCandidate(row))
        {
            if (TryDescribeQualifierArgumentRoleTransition(
                    Contain(beforeText)!,
                    Contain(afterText)!,
                    out var qualifierTransition))
            {
                return side == CSharpStructuralSide.Before
                    ? $"; {qualifierTransition.BeforeDescription}"
                    : $"; {qualifierTransition.AfterDescription}";
            }

            if (TryDescribeCalleeRenamedRoleTransition(
                    comparison,
                    Contain(beforeText)!,
                    Contain(afterText)!,
                    out var calleeTransition))
            {
                return side == CSharpStructuralSide.Before
                    ? $"; {calleeTransition.BeforeDescription}"
                    : $"; {calleeTransition.AfterDescription}";
            }
        }

        string counterpart = side == CSharpStructuralSide.Before
            ? afterText
            : beforeText;

        return side == CSharpStructuralSide.Before
            ? $"; changed to {Contain(counterpart)}"
            : $"; changed from {Contain(counterpart)}";
    }

    static bool IsInvocationRoleCandidate(CSharpStructuralDiffRow row)
        => string.Equals(row.BeforeKind, "InvocationExpression", StringComparison.Ordinal)
            && string.Equals(row.AfterKind, "InvocationExpression", StringComparison.Ordinal);

    /// <summary>
    /// Item 3 (issue #5022): a side-local role description for the narrow,
    /// well-evidenced "receiver becomes an argument" call-rewrite shape --
    /// <c>q.Callee(args)</c> (extension/instance-style) rewritten to
    /// <c>Callee(q, args)</c> (static-style) with the same callee name, or the
    /// reverse -- derived purely from each side's own selected text. This is a
    /// textual role classification, not a semantic-model lookup: it never
    /// asserts anything the two sides' literal text does not already show,
    /// and it recognizes nothing outside this one shape.
    /// </summary>
    readonly record struct QualifierArgumentRoleTransition(
        string BeforeDescription,
        string AfterDescription,
        string DetailSummary);

    static bool TryDescribeQualifierArgumentRoleTransition(
        string beforeText,
        string afterText,
        out QualifierArgumentRoleTransition transition)
    {
        transition = default;
        if (!TryParseQualifiedCall(beforeText, out string? beforeQualifier, out string beforeCallee, out var beforeArgs)
            || !TryParseQualifiedCall(afterText, out string? afterQualifier, out string afterCallee, out var afterArgs))
        {
            return false;
        }

        if (beforeCallee.Length == 0
            || !string.Equals(beforeCallee, afterCallee, StringComparison.Ordinal))
        {
            return false;
        }

        if (beforeQualifier is { } qualifier && afterQualifier is null)
        {
            if (!TryFindInsertedArgumentIndex(beforeArgs, afterArgs, qualifier, out int argIndex))
                return false;

            transition = new QualifierArgumentRoleTransition(
                $"{qualifier}: used as extension-call qualifier",
                $"{qualifier}: moved to argument {argIndex + 1} (static call)",
                $"{qualifier}: qualifier -> argument {argIndex + 1} (extension -> static call)");
            return true;
        }

        if (afterQualifier is { } movedQualifier && beforeQualifier is null)
        {
            if (!TryFindInsertedArgumentIndex(afterArgs, beforeArgs, movedQualifier, out int argIndex))
                return false;

            transition = new QualifierArgumentRoleTransition(
                $"{movedQualifier}: argument {argIndex + 1} (static call)",
                $"{movedQualifier}: moved to extension-call qualifier",
                $"{movedQualifier}: argument {argIndex + 1} -> qualifier (static -> extension call)");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether <paramref name="larger"/> is exactly
    /// <paramref name="smaller"/> with one occurrence of <paramref name="value"/>
    /// inserted, and returns that occurrence's index in
    /// <paramref name="larger"/>. This is required, not optional: without it,
    /// merely finding <paramref name="value"/> somewhere in
    /// <paramref name="larger"/>'s arguments would let an unrelated argument
    /// change (or a coincidental duplicate value) hide behind a false
    /// "qualifier moved to argument N" claim. Returns <see langword="false"/>
    /// -- forcing the caller to fall back to the literal text transition --
    /// when no removal position reproduces <paramref name="smaller"/>
    /// exactly, or when more than one position does (an ambiguous match is
    /// not an honest one).
    /// </summary>
    static bool TryFindInsertedArgumentIndex(
        ImmutableArray<string> smaller,
        ImmutableArray<string> larger,
        string value,
        out int index)
    {
        index = -1;
        if (larger.Length != smaller.Length + 1)
            return false;

        int found = -1;
        for (int candidate = 0; candidate < larger.Length; candidate++)
        {
            if (!string.Equals(larger[candidate], value, StringComparison.Ordinal))
                continue;

            bool matches = true;
            int smallerIndex = 0;
            for (int largerIndex = 0; largerIndex < larger.Length; largerIndex++)
            {
                if (largerIndex == candidate)
                    continue;
                if (!string.Equals(smaller[smallerIndex], larger[largerIndex], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
                smallerIndex++;
            }

            if (!matches)
                continue;

            if (found >= 0)
            {
                // More than one removal position reproduces `smaller` exactly
                // (possible with duplicate argument text): the position is
                // ambiguous, so no specific claim is honest.
                index = -1;
                return false;
            }
            found = candidate;
        }

        if (found < 0)
            return false;

        index = found;
        return true;
    }

    /// <summary>
    /// Item 9 (issue #5022): a side-local role description for a call-site
    /// rewrite whose callee identifier changed, when the new (or old) callee
    /// names a local-function declaration this same comparison already
    /// reports as <see cref="CSharpStructuralChangeKind.Added"/> or
    /// <see cref="CSharpStructuralChangeKind.Removed"/> -- the exact
    /// #3902/#4116 shape item 5 already licenses (a synthesized call rewritten
    /// to call a declared local function, or the reverse). This reuses
    /// <see cref="CSharpBodyDiff.TryGetInvocationCalleeText(ReadOnlySpan{char}, out ReadOnlySpan{char})"/>,
    /// the same hardened callee-extraction logic the correspondence layer
    /// uses to license that declaration's own Added/Removed row, so an
    /// argument-only edit (the call's target unchanged) never qualifies here
    /// either. Checking for a sibling declaration row keeps this scoped to
    /// that one paired shape: an unrelated callee rename with no paired
    /// declaration change anywhere in the comparison must not get this
    /// "renamed to a local function" caption, since nothing here shows the
    /// new callee is actually a local function.
    /// </summary>
    readonly record struct CalleeRenamedRoleTransition(
        string BeforeDescription,
        string AfterDescription,
        string DetailSummary);

    static bool TryDescribeCalleeRenamedRoleTransition(
        CSharpStructuralComparison comparison,
        string beforeText,
        string afterText,
        out CalleeRenamedRoleTransition transition)
    {
        transition = default;
        if (!CSharpBodyDiff.TryGetInvocationCalleeText(beforeText, out var beforeCalleeSpan)
            || !CSharpBodyDiff.TryGetInvocationCalleeText(afterText, out var afterCalleeSpan))
        {
            return false;
        }

        string beforeCallee = beforeCalleeSpan.ToString();
        string afterCallee = afterCalleeSpan.ToString();
        if (beforeCallee.Length == 0
            || afterCallee.Length == 0
            || string.Equals(beforeCallee, afterCallee, StringComparison.Ordinal))
        {
            return false;
        }

        if (DeclaresLocalFunctionNamed(comparison, CSharpStructuralChangeKind.Added, afterCallee))
        {
            transition = new CalleeRenamedRoleTransition(
                $"call target: {beforeCallee}",
                $"call target: local function `{afterCallee}`",
                $"call target: {beforeCallee} -> local function `{afterCallee}`");
            return true;
        }

        if (DeclaresLocalFunctionNamed(comparison, CSharpStructuralChangeKind.Removed, beforeCallee))
        {
            transition = new CalleeRenamedRoleTransition(
                $"call target: local function `{beforeCallee}`",
                $"call target: {afterCallee}",
                $"call target: local function `{beforeCallee}` -> {afterCallee}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether this comparison already reports a <see cref="CSharpStructuralChangeKind.Added"/>
    /// or <see cref="CSharpStructuralChangeKind.Removed"/> <c>LocalFunctionStatement</c>
    /// row whose own selected source text names <paramref name="name"/> as a
    /// whole identifier -- not merely a substring, which would otherwise let
    /// an unrelated declaration like <c>OwnValue</c> falsely match a callee
    /// named <c>Own</c>. The row's display label is only a generic per-kind
    /// caption ("Local function"), never the declaration's own text, so this
    /// re-selects the row's actual spans instead.
    /// </summary>
    static bool DeclaresLocalFunctionNamed(
        CSharpStructuralComparison comparison,
        CSharpStructuralChangeKind change,
        string name)
    {
        var document = change == CSharpStructuralChangeKind.Added ? comparison.After : comparison.Before;
        foreach (var candidate in comparison.Rows)
        {
            if (!candidate.Change.HasFlag(change))
                continue;

            string? kind = change == CSharpStructuralChangeKind.Added ? candidate.AfterKind : candidate.BeforeKind;
            if (!string.Equals(kind, "LocalFunctionStatement", StringComparison.Ordinal))
                continue;

            var spans = change == CSharpStructuralChangeKind.Added ? candidate.AfterSpans : candidate.BeforeSpans;
            foreach (var span in spans)
            {
                if (HasWholeIdentifierMatch(SelectText(document, span), name))
                    return true;
            }
        }

        return false;
    }

    static bool HasWholeIdentifierMatch(string text, string identifier)
    {
        int searchStart = 0;
        while (true)
        {
            int index = text.IndexOf(identifier, searchStart, StringComparison.Ordinal);
            if (index < 0)
                return false;

            bool leftBoundary = index == 0 || !IsIdentifierChar(text[index - 1]);
            int end = index + identifier.Length;
            bool rightBoundary = end == text.Length || !IsIdentifierChar(text[end]);
            if (leftBoundary && rightBoundary)
                return true;

            searchStart = index + 1;
        }
    }

    static bool IsIdentifierChar(char value) => char.IsLetterOrDigit(value) || value == '_';

    /// <summary>
    /// Splits <paramref name="text"/> as <c>[qualifier.]callee(arguments)</c>
    /// when it is exactly one call expression (no trailing text after the
    /// closing paren beyond an optional statement-terminating <c>;</c>).
    /// <paramref name="qualifier"/> is <see langword="null"/> for a static
    /// (unqualified) call. Returns <see langword="false"/> for any shape this
    /// narrow heuristic does not recognize -- callers must treat that as "no
    /// opinion", not as evidence the shape does not exist.
    /// </summary>
    static bool TryParseQualifiedCall(
        string text,
        out string? qualifier,
        out string callee,
        out ImmutableArray<string> arguments)
    {
        qualifier = null;
        callee = "";
        arguments = [];

        int openParen = FindUnquotedIndexOf(text, '(', 0);
        if (openParen < 0)
            return false;

        int closeParen = FindMatchingClose(text, openParen);
        if (closeParen < 0)
            return false;

        if (text.AsSpan(closeParen + 1).TrimEnd(';').TrimEnd().Length > 0)
            return false;

        int dotIndex = -1;
        for (int index = openParen - 1; index >= 0; index--)
        {
            char character = text[index];
            if (character == '.')
            {
                dotIndex = index;
                break;
            }
            if (!char.IsLetterOrDigit(character) && character != '_')
                break;
        }

        callee = text[(dotIndex + 1)..openParen];
        if (callee.Length == 0)
            return false;

        if (dotIndex > 0)
        {
            string candidateQualifier = text[..dotIndex];
            if (!IsSimpleIdentifierPath(candidateQualifier))
                return false;
            qualifier = candidateQualifier;
        }

        arguments = SplitTopLevelArguments(text[(openParen + 1)..closeParen]);
        return true;
    }

    static bool IsSimpleIdentifierPath(string text)
    {
        if (text.Length == 0 || (!char.IsLetter(text[0]) && text[0] != '_'))
            return false;

        foreach (char character in text)
        {
            if (!char.IsLetterOrDigit(character) && character != '_' && character != '.')
                return false;
        }
        return true;
    }

    static int FindUnquotedIndexOf(string text, char target, int start)
    {
        bool inString = false;
        bool inChar = false;
        for (int index = start; index < text.Length; index++)
        {
            char character = text[index];
            if (inString)
            {
                if (character == '\\') { index++; continue; }
                if (character == '"') inString = false;
                continue;
            }
            if (inChar)
            {
                if (character == '\\') { index++; continue; }
                if (character == '\'') inChar = false;
                continue;
            }
            if (character == '"') { inString = true; continue; }
            if (character == '\'') { inChar = true; continue; }
            if (character == target)
                return index;
        }
        return -1;
    }

    static int FindMatchingClose(string text, int openIndex)
    {
        int depth = 0;
        bool inString = false;
        bool inChar = false;
        for (int index = openIndex; index < text.Length; index++)
        {
            char character = text[index];
            if (inString)
            {
                if (character == '\\') { index++; continue; }
                if (character == '"') inString = false;
                continue;
            }
            if (inChar)
            {
                if (character == '\\') { index++; continue; }
                if (character == '\'') inChar = false;
                continue;
            }
            if (character == '"') { inString = true; continue; }
            if (character == '\'') { inChar = true; continue; }
            if (character == '(') depth++;
            else if (character == ')')
            {
                depth--;
                if (depth == 0)
                    return index;
            }
        }
        return -1;
    }

    static ImmutableArray<string> SplitTopLevelArguments(string argsText)
    {
        if (argsText.Trim().Length == 0)
            return [];

        var results = ImmutableArray.CreateBuilder<string>();
        int depth = 0;
        bool inString = false;
        bool inChar = false;
        int start = 0;
        for (int index = 0; index < argsText.Length; index++)
        {
            char character = argsText[index];
            if (inString)
            {
                if (character == '\\') { index++; continue; }
                if (character == '"') inString = false;
                continue;
            }
            if (inChar)
            {
                if (character == '\\') { index++; continue; }
                if (character == '\'') inChar = false;
                continue;
            }
            if (character == '"') { inString = true; continue; }
            if (character == '\'') { inChar = true; continue; }
            if (character is '(' or '[' or '{') depth++;
            else if (character is ')' or ']' or '}') depth--;
            else if (character == ',' && depth == 0)
            {
                results.Add(argsText[start..index].Trim());
                start = index + 1;
            }
        }
        results.Add(argsText[start..].Trim());
        return results.ToImmutable();
    }

    static bool CanRenderExactInline(string text)
        => AnnotatedSourceText.IsWellFormedUtf16(text)
            && string.Equals(text, Contain(text), StringComparison.Ordinal)
            && !text.StartsWith(' ')
            && !text.EndsWith(' ')
            && !text.Contains("  ", StringComparison.Ordinal);

    static string SelectText(
        AnnotatedSourceDocument document,
        AnnotatedSourceSpan span)
        => document.Text.Substring(span.Start, span.Length);

    static IReadOnlyList<SourceTextLine> SplitLines(string text)
    {
        var lines = new List<SourceTextLine>();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\r' && text[index] != '\n')
                continue;

            int length = index - start;
            lines.Add(new SourceTextLine(start, text.Substring(start, length)));
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                index++;
            start = index + 1;
        }

        lines.Add(new SourceTextLine(start, text[start..]));
        return lines;
    }

    static bool CanRenderInCommentGutter(
        IReadOnlyList<(IAnnotation Fact, AnnotationAnchor.CaretExtent Extent)> entries,
        int commentColumn)
    {
        var extents = entries
            .Select(static entry => entry.Extent)
            .Distinct()
            .ToArray();
        if (extents.Length == 1)
            return extents[0].Column >= commentColumn + 3;

        for (int index = 0; index < extents.Length; index++)
        {
            int labelLength = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).Length + 1;
            if (extents[index].Column - labelLength < commentColumn + 2)
                return false;
        }
        return true;
    }

    static bool HasTabBeforeExtent(
        string sourceLine,
        IReadOnlyList<(IAnnotation Fact, AnnotationAnchor.CaretExtent Extent)> entries)
        => entries.Any(entry => sourceLine.AsSpan(0, entry.Extent.Column).Contains('\t'));

    static IReadOnlyList<string> RenderExactFallback(
        string sourceLine,
        IReadOnlyList<(IAnnotation Fact, AnnotationAnchor.CaretExtent Extent)> entries)
    {
        var lines = new List<string>();
        foreach (var group in entries.GroupBy(static entry => entry.Extent))
        {
            string padding = RenderFallbackPadding(sourceLine, group.Key.Column);
            lines.Add(padding + new string('^', group.Key.Length));
            foreach (var entry in group)
                lines.Add(padding + AnnotationText.Format(entry.Fact));
        }
        return lines;
    }

    static string RenderFallbackPadding(string sourceLine, int length)
    {
        var padding = new char[length];
        for (int index = 0; index < padding.Length; index++)
            padding[index] = sourceLine[index] == '\t' ? '\t' : ' ';
        return new string(padding);
    }

    static void EnsureDisplaySafe(
        IReadOnlyList<SourceTextLine> lines,
        CSharpStructuralSide side)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index].Text;
            if (!string.Equals(line, Contain(line), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"{side} document line {index + 1} contains terminal or invisible control text.");
            }
        }
    }

    static string? Contain(string? value)
        => value is null ? null : CSharpText.CSharpIdentifier.ContainRenderedText(value);

    private readonly record struct SourceTextLine(int Start, string Text);

    private sealed class StructuralAnnotation(string text) : IAnnotation
    {
        public AnnotationDescriptor Descriptor { get; } = new(
            text,
            AnnotationCategory.Semantics,
            "structural change");

        public int SourceOffset => -1;

        public AnnotationConditionality Conditionality => AnnotationConditionality.Always;

        public string? Detail => null;
    }
}
