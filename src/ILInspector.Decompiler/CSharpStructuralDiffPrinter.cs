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
        bool textChanged = RowTextChanged(comparison, row);
        bool added = row.Change.HasFlag(CSharpStructuralChangeKind.Added);
        bool removed = row.Change.HasFlag(CSharpStructuralChangeKind.Removed);
        if (!textChanged && !added && !removed)
            return "";

        string? beforeText = InlineText(comparison.Before, row.BeforeSpans);
        string? afterText = InlineText(comparison.After, row.AfterSpans);
        if (textChanged && IsInvocationRoleCandidate(row))
        {
            // Use the full matched node's text, not the (possibly narrowed,
            // issue #5486) caret spans -- these captions need to see the
            // whole call shape to recognize it.
            string? beforeCallText = FullNodeText(comparison.Before, row.BeforeNodeId) ?? beforeText;
            string? afterCallText = FullNodeText(comparison.After, row.AfterNodeId) ?? afterText;
            if (beforeCallText is not null && afterCallText is not null)
            {
                if (TryDescribeQualifierArgumentRoleTransition(beforeCallText, afterCallText, out var qualifierTransition))
                    return qualifierTransition.DetailSummary;
                if (TryDescribeCalleeRenamedRoleTransition(comparison, beforeCallText, afterCallText, out var calleeTransition))
                    return calleeTransition.DetailSummary;
            }
        }

        if (textChanged
            && IsUsingDeclarationRoleCandidate(row)
            && beforeText is not null
            && afterText is not null
            && TryDescribeUsingDeclarationRoleTransition(beforeText, afterText, out var usingTransition))
        {
            return usingTransition.DetailSummary;
        }

        return beforeText is null && afterText is null
            ? ""
            : FormatTransition(beforeText, afterText);
    }

    /// <summary>
    /// Whether a row's text meaningfully changed. For most rows this is the
    /// (possibly narrowed) caret spans' own text equality -- narrowing
    /// preserves the property that the narrowed span still differs whenever
    /// the row does (items 2/7/10 only narrow when the surrounding text is
    /// identical, so the difference necessarily survives inside the narrowed
    /// span). The one shape that does not preserve this property is issue
    /// #5486's qualifier/argument narrowing: `receiver` narrows to the exact
    /// same identifier text on both sides, even though its role (qualifier
    /// vs. argument) genuinely changed. For that shape, compare the full
    /// matched nodes' text instead of the narrowed caret spans.
    /// </summary>
    static bool RowTextChanged(CSharpStructuralComparison comparison, CSharpStructuralDiffRow row)
    {
        if (!row.Change.HasFlag(CSharpStructuralChangeKind.Changed))
            return false;

        if (IsInvocationRoleCandidate(row)
            && row.BeforeNodeId is int beforeId
            && row.AfterNodeId is int afterId)
        {
            var beforeNode = comparison.Before.Nodes[beforeId];
            var afterNode = comparison.After.Nodes[afterId];
            return !CSharpBodyDiff.SelectedTextEqual(
                comparison.Before,
                beforeNode.Spans,
                comparison.After,
                afterNode.Spans);
        }

        return !CSharpBodyDiff.SelectedTextEqual(
            comparison.Before,
            row.BeforeSpans,
            comparison.After,
            row.AfterSpans);
    }

    static string? InlineText(AnnotatedSourceDocument document, ImmutableArray<AnnotatedSourceSpan> spans)
    {
        if (spans.Length != 1 || spans[0].Length > MaximumInlineTransitionLength)
            return null;

        string text = SelectText(document, spans[0]);
        return CanRenderExactInline(text) ? Contain(text) : null;
    }

    /// <summary>
    /// The full text of a matched node's own span, independent of whatever
    /// (possibly narrowed) caret spans its row carries. Invocation-role
    /// captions (item 3's qualifier/argument transition, item 9's
    /// callee-rename) need to see the whole call shape to recognize it, even
    /// when the row's caret has been narrowed to just the moved sub-token
    /// (issue #5486) -- the caret position and the caption derivation are
    /// separate concerns.
    ///
    /// Also used (as <c>internal</c>) by <c>RefineInvocationQualifierArgumentRows</c>
    /// to gate narrowing itself: a row must only be narrowed when this same
    /// method would later be able to recognize and render its full call
    /// shape, otherwise the caption/detail logic falls back to the (now
    /// narrowed, textually-identical-on-both-sides) caret spans and produces
    /// a misleading self-transition such as "changed to receiver".
    /// </summary>
    internal static string? FullNodeText(AnnotatedSourceDocument document, int? nodeId)
    {
        if (nodeId is not int id)
            return null;

        var node = document.Nodes[id];
        if (node.Spans.Count != 1 || node.Spans[0].Length > MaximumInlineTransitionLength)
            return null;

        string text = SelectText(document, node.Spans[0]);
        return CanRenderExactInline(text) ? Contain(text) : null;
    }

    static string TextTransitionSuffix(
        CSharpStructuralComparison comparison,
        CSharpStructuralDiffRow row,
        CSharpStructuralSide side)
    {
        if (!RowTextChanged(comparison, row))
            return "";

        // Invocation-role captions need the whole call shape, not the
        // (possibly narrowed, issue #5486) caret spans -- try this first,
        // independent of the caret's own span count/length, and fall back to
        // the caret-derived text below when the shape isn't recognized.
        if (IsInvocationRoleCandidate(row))
        {
            string? beforeCallText = FullNodeText(comparison.Before, row.BeforeNodeId);
            string? afterCallText = FullNodeText(comparison.After, row.AfterNodeId);
            if (beforeCallText is not null && afterCallText is not null)
            {
                if (TryDescribeQualifierArgumentRoleTransition(
                        beforeCallText, afterCallText, out var qualifierTransition))
                {
                    return side == CSharpStructuralSide.Before
                        ? $"; {qualifierTransition.BeforeDescription}"
                        : $"; {qualifierTransition.AfterDescription}";
                }

                if (TryDescribeCalleeRenamedRoleTransition(
                        comparison, beforeCallText, afterCallText, out var calleeTransition))
                {
                    return side == CSharpStructuralSide.Before
                        ? $"; {calleeTransition.BeforeDescription}"
                        : $"; {calleeTransition.AfterDescription}";
                }
            }
        }

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

        if (IsUsingDeclarationRoleCandidate(row)
            && TryDescribeUsingDeclarationRoleTransition(
                Contain(beforeText)!,
                Contain(afterText)!,
                out var usingTransition))
        {
            return side == CSharpStructuralSide.Before
                ? $"; {usingTransition.BeforeDescription}"
                : $"; {usingTransition.AfterDescription}";
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

    static bool IsUsingDeclarationRoleCandidate(CSharpStructuralDiffRow row)
        => string.Equals(row.BeforeKind, "UsingStatement", StringComparison.Ordinal)
            && string.Equals(row.AfterKind, "UsingStatement", StringComparison.Ordinal);

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
    internal static bool TryFindInsertedArgumentIndex(
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
    /// Item 10 (issue #5022): a side-local role description for the
    /// #4113 "using-resource declaration dropped/added" shape licensed by
    /// <c>NarrowUsingResourceDeclaration</c> in <c>CSharpStructuralComparison.cs</c>
    /// -- once that narrowing has already confirmed the declared identifier
    /// is never read elsewhere in the statement's own body, this purely
    /// re-derives which side is the declaring one from each side's own
    /// already-narrowed text, so a "declares variable" / "variable-less
    /// resource" caption is only ever produced for that one verified shape.
    /// </summary>
    readonly record struct UsingDeclarationRoleTransition(
        string BeforeDescription,
        string AfterDescription,
        string DetailSummary);

    static bool TryDescribeUsingDeclarationRoleTransition(
        string beforeText,
        string afterText,
        out UsingDeclarationRoleTransition transition)
    {
        transition = default;

        if (TryParseTrailingDeclarationEquals(beforeText, out string droppedName)
            && !TryParseTrailingDeclarationEquals(afterText, out _))
        {
            transition = new UsingDeclarationRoleTransition(
                $"declares variable `{droppedName}` (never read)",
                "variable-less resource (declaration dropped; never read)",
                $"header: variable declaration dropped (`{droppedName}` never read)");
            return true;
        }

        if (TryParseTrailingDeclarationEquals(afterText, out string addedName)
            && !TryParseTrailingDeclarationEquals(beforeText, out _))
        {
            transition = new UsingDeclarationRoleTransition(
                "variable-less resource (declaration added; never read)",
                $"declares variable `{addedName}` (never read)",
                $"header: variable declaration added (`{addedName}` never read)");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Recognizes exactly the `Type identifier =` shape (the C# grammar for
    /// a using-resource declarator, which allows no modifiers) that
    /// <c>TryParseDeclarationPrefix</c> in <c>CSharpStructuralComparison.cs</c>
    /// narrows a row's span to. This is an independent textual re-check, not
    /// a shared call, matching this file's existing pattern (e.g.
    /// <see cref="TryParseQualifiedCall"/>) of recognizing nothing beyond
    /// what each side's own selected text shows.
    /// </summary>
    static bool TryParseTrailingDeclarationEquals(string text, out string identifier)
    {
        identifier = "";
        string trimmed = text.TrimEnd();
        if (trimmed.Length < 2 || trimmed[^1] != '=' || IsCompoundEqualsPrefixChar(trimmed[^2]))
            return false;

        string beforeEquals = trimmed[..^1].TrimEnd();
        string[] tokens = beforeEquals.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2 || !IsSimpleIdentifier(tokens[1]))
            return false;

        identifier = tokens[1];
        return true;
    }

    static bool IsCompoundEqualsPrefixChar(char character)
        => character is '=' or '!' or '<' or '>' or '+' or '-' or '*' or '/' or '%' or '&' or '|' or '^';

    static bool IsSimpleIdentifier(string text)
    {
        if (text.Length == 0)
            return false;
        char first = text[0];
        if (!char.IsLetter(first) && first != '_' && first != '@')
            return false;
        for (int index = 1; index < text.Length; index++)
        {
            char character = text[index];
            if (!char.IsLetterOrDigit(character) && character != '_')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Whether this comparison already reports a <see cref="CSharpStructuralChangeKind.Added"/>
    /// or <see cref="CSharpStructuralChangeKind.Removed"/> <c>LocalFunctionStatement</c>
    /// row whose own <em>declared name</em> -- not merely any identifier
    /// occurring anywhere in its full statement text -- equals
    /// <paramref name="name"/>. The row's display label is only a generic
    /// per-kind caption ("Local function"), never the declaration's own
    /// text, so this re-selects the row's actual spans instead. Scanning the
    /// declaration's full span text for any occurrence of the identifier
    /// (round-1 review, reviewers A and B) would falsely match a parameter,
    /// body reference, comment, or string literal that merely shares the
    /// callee's spelling -- e.g. a local function <c>Other(int New)</c> must
    /// not license the caption for an unrelated call renamed to <c>New</c>.
    /// </summary>
    /// <remarks>
    /// Round 6 review (reviewers A and B) additionally found that a
    /// same-named local function declared in a narrower nested block (e.g.
    /// inside an <c>if</c>) could license this caption for an unrelated call
    /// outside that block -- C#'s own local-function scoping rule would
    /// never let that call resolve there. Round 7 review (reviewers A and
    /// B) found the fix attempted for that (a lexical-scope check keyed off
    /// a <c>Block</c>-kind <see cref="AnnotatedSourceNode"/>) was worse than
    /// the bug: this printer never records a range for a <c>Block</c> or
    /// <c>BlockContainer</c> node (see <c>PrintedRangeMap</c>'s own remark,
    /// "a <c>Block</c> records no range of its own", and
    /// <c>CSharpPrinter.AppendContainer</c>, which recurses into a block's
    /// statements without ever recording the block itself), so every real
    /// document lacks the node the check needed and it silently suppressed
    /// this caption for the ordinary, common case too -- not just the
    /// narrow nested-scope shape it targeted. The scope check has been
    /// reverted for that reason. The narrow nested-scope false positive it
    /// was meant to prevent remains a known, accepted limitation: this
    /// comparison has no reliable lexical-scope data to check against
    /// today, and inventing one via further text scanning would repeat the
    /// same brittleness this heuristic has spent six rounds hardening
    /// against. Fixing it properly needs the decompiler pipeline to publish
    /// real block/scope spans, which is out of this PR's scope.
    /// </remarks>
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
                if (TryGetLocalFunctionDeclaredName(SelectText(document, span), out string declaredName)
                    && string.Equals(declaredName, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Modifiers a local-function declaration's header may carry before its
    /// return type and name. Needed only to skip a tuple return type's own
    /// parenthesized group (e.g. <c>static (int, string) F(int x)</c>), whose
    /// preceding token would otherwise be mistaken for the declared name.
    /// <c>ref</c> and <c>readonly</c> are included for the same reason: a
    /// ref (readonly) tuple-returning declaration (<c>ref (int, int) F()</c>,
    /// <c>ref readonly (int, int) F()</c>) has one of these as the token
    /// immediately preceding the tuple-return group, which round-6 review
    /// (reviewer A) found was otherwise returned as the declared name instead
    /// of continuing on to the real parameter list.
    /// </summary>
    static readonly string[] LocalFunctionModifiers = ["static", "async", "unsafe", "extern", "ref", "readonly"];

    /// <summary>
    /// Extracts a <c>LocalFunctionStatement</c>'s own declared name: the
    /// identifier immediately preceding the first top-level (depth-zero)
    /// parenthesized group whose preceding token is not itself a known
    /// modifier keyword. Depth tracking means an identifier merely appearing
    /// inside that group -- a parameter name, a default-value expression, or
    /// anything in the body after the parameter list closes -- is never
    /// examined; only the header token immediately before the parameter
    /// list's own opening paren is a candidate. Returns
    /// <see langword="false"/> for any shape this narrow heuristic does not
    /// recognize, matching every other textual classifier in this file:
    /// "no opinion", not a guess. Round-3 review (reviewers A and B):
    /// scanning must stop -- not merely skip a group and keep looking -- the
    /// moment a group's preceding token is neither empty-at-the-very-start
    /// (the one shape the tuple-return-type skip legitimately expects) nor a
    /// recognized modifier; otherwise an unrecognized header shape such as a
    /// type-parameter list (<c>Other&lt;T&gt;()</c>) falls through into the
    /// body and can misattribute an unrelated call found there (e.g.
    /// <c>New()</c>) as this declaration's own name. A leading attribute
    /// list (<c>[My(1)] static void Other() { }</c>) is skipped up front for
    /// the same reason: its own argument list is otherwise indistinguishable
    /// from the real parameter list. Bails on any <c>/</c> encountered in
    /// this main scan too, not only in <see cref="SkipLeadingAttributeLists"/>
    /// -- a comment anywhere later in the header (e.g.
    /// <c>static /* New() */ void Other() { }</c>) could otherwise supply a
    /// parenthesized group whose preceding token is misread as the declared
    /// name (round-6 review, reviewers A and B). Tracks a separate
    /// <c>angleDepth</c> alongside the paren <c>depth</c>, active only while
    /// <c>depth == 0</c>: a return type's own generic argument list can
    /// nest a tuple type (<c>Task&lt;(int, int)&gt; F()</c>), and without
    /// this, that tuple's parenthesized group -- opened while still nested
    /// inside the unclosed <c>&lt;...&gt;</c> -- was wrongly read as the
    /// first top-level group, whose preceding <c>&lt;</c> is not an
    /// identifier and made this bail before ever reaching the real
    /// parameter list and its own preceding name (round-7 review, reviewer
    /// A). Gating the angle tracking to <c>depth == 0</c> keeps it from
    /// touching an ordinary comparison operator that might appear inside a
    /// default parameter value once the real parameter list is already
    /// open (paren depth alone still finds that group's own close
    /// correctly, as it always has).
    /// </summary>
    static bool TryGetLocalFunctionDeclaredName(string text, out string name)
    {
        name = "";
        int start = SkipLeadingAttributeLists(text);
        if (start < 0)
            return false;

        int depth = 0;
        int angleDepth = 0;
        int groupStart = -1;
        for (int index = start; index < text.Length; index++)
        {
            char current = text[index];
            if (current == '/')
                return false;

            // The printer never emits preprocessor directives inside a
            // declaration header or parameter list (confirmed: no
            // `#line`/`#region`/`#pragma` emission anywhere in
            // CSharpPrinter or LocalFunctionRaisingPass), so this is
            // defense in depth, not a reachable product scenario -- the
            // same posture as the `/` comment bail above. Round-9 review
            // (reviewer B) added `#` as a *body-start proof* instead of a
            // bail, reasoning a directive could legally separate the
            // parameter list from the body. But round-10 review (reviewer
            // A and reviewer B, independently) showed that same `#` can
            // just as legally separate a return type from the name (e.g. a
            // `#line` directive between a tuple return type and the
            // function name), where treating it as body-start proof
            // wrongly stops the scan early and misattributes the
            // preceding modifier-spelled token as the declared name. Since
            // neither shape is reachable from this printer, bailing
            // unconditionally is strictly safer than trying to reason
            // about which side of the header a directive fell on.
            if (current == '#')
                return false;

            if (depth == 0)
            {
                if (current == '<')
                {
                    angleDepth++;
                    continue;
                }

                if (current == '>' && angleDepth > 0)
                {
                    angleDepth--;
                    continue;
                }
            }

            if (current == '(')
            {
                if (depth == 0 && angleDepth == 0)
                    groupStart = index;
                depth++;
            }
            else if (current == ')')
            {
                depth--;
                if (depth == 0 && groupStart >= 0)
                {
                    int tokenEnd = groupStart;
                    // Bounded by `start`, not 0, so a leading attribute
                    // list's own trailing whitespace never leaks into the
                    // token search -- otherwise an attributed unmodified
                    // tuple return type (`[My] (int, string) F(x)`) would
                    // wrongly see a non-empty gap here and bail instead of
                    // taking the tuple-return continuation below (round-4
                    // review, reviewer A).
                    while (tokenEnd > start && char.IsWhiteSpace(text[tokenEnd - 1]))
                        tokenEnd--;
                    int tokenStart = tokenEnd;
                    while (tokenStart > start && TryStepBackOneIdentifierRune(text, start, tokenStart, out int stepped))
                        tokenStart = stepped;

                    // Include a verbatim identifier's leading '@' (e.g. the
                    // escaped local function `@return`) so the declared name
                    // matches the callee text, which likewise keeps the '@'
                    // (round-2 review: an escaped name was silently dropped
                    // here, permanently mismatching a correctly-escaped
                    // callee and losing the caption).
                    if (tokenStart > start && text[tokenStart - 1] == '@')
                        tokenStart--;

                    if (tokenStart == tokenEnd)
                    {
                        if (tokenEnd == start)
                        {
                            // Nothing precedes this group at all: consistent
                            // only with an unmodified tuple return type
                            // opening the declaration (`(int, string) F(x)`).
                            // Keep looking for the real parameter list that
                            // follows it.
                            groupStart = -1;
                            continue;
                        }

                        // Some non-identifier token precedes this group in
                        // the middle of the text (e.g. the '>' closing a
                        // type-parameter list). This shape isn't recognized;
                        // bail rather than risk continuing into a later,
                        // unrelated group such as a body call.
                        return false;
                    }

                    if (tokenStart > start && !char.IsWhiteSpace(text[tokenStart - 1]))
                    {
                        // Whatever precedes this identifier -- a modifier
                        // keyword, a return type, or nothing at all -- must
                        // be separated from it by whitespace (or '@', which
                        // is already folded in above). Any other adjoining
                        // character means the backward identifier scan
                        // stopped at a non-identifier character in the
                        // *middle* of a longer token, not at a real
                        // boundary. The decompiler deliberately preserves
                        // compiler-unspellable names containing such
                        // characters (e.g. `bad-name`), and the valid-
                        // looking suffix captured here (`name`) is not the
                        // declaration's real name -- it could coincidentally
                        // match an unrelated call's own rename (round-9
                        // review, reviewer A). This shape isn't recognized;
                        // bail rather than risk that coincidence.
                        return false;
                    }

                    // Known residual gap (round-10 review, reviewer B): the
                    // whitespace boundary above assumes whitespace always
                    // separates two distinct tokens, but the decompiler
                    // also preserves compiler-unspellable names that
                    // themselves *contain* whitespace (e.g. `bad name`).
                    // For such a declaration, this scan still stops at the
                    // last whitespace run and captures only the trailing
                    // word (`name`), which can coincidentally match an
                    // unrelated call's own rename. Distinguishing that case
                    // from an ordinary `ReturnType Name(...)` declaration
                    // -- where the preceding word is a genuine, unrelated
                    // return type and the captured word is correctly the
                    // whole name -- is not decidable from local lexical
                    // context alone: both shapes are, textually, two
                    // whitespace-separated words before an opening paren.
                    // Requiring proof here (the way the modifier-keyword
                    // branch below does) would reject the ordinary,
                    // overwhelmingly common `ReturnType Name(...)` shape
                    // for every real declaration, which is not an
                    // acceptable trade against an edge case that needs both
                    // an embedded-whitespace unspellable name *and* a
                    // coincidental exact-text collision with an unrelated
                    // rename target. This is accepted as a known,
                    // documented limitation rather than fixed.

                    if (char.IsDigit(text[tokenStart]))
                        return false;

                    if (Array.IndexOf(LocalFunctionModifiers, text[tokenStart..tokenEnd]) >= 0)
                    {
                        // Only a genuine modifier prefixing a return type or
                        // tuple-return type is followed by more header text
                        // -- another identifier or parenthesized group --
                        // before the body opens. A local function whose own
                        // declared name merely spells a modifier keyword
                        // (`void async() { New(); }`; `async` is a
                        // contextual keyword, legal unescaped as an
                        // ordinary identifier here) has its own real
                        // parameter list mistaken for this modifier-prefixed
                        // group instead, and the body opens immediately
                        // after -- round-8 review, reviewer A. Peeking past
                        // the closing paren for an immediate body start (a
                        // block's `{` or an expression body's `=>`)
                        // distinguishes the two. No valid C# declaration
                        // puts a real modifier or tuple-return-type prefix
                        // immediately before a body: it always still needs
                        // a name and that name's own parameter list first.
                        // So finding a body right here proves, rather than
                        // guesses, that this token is the declaration's own
                        // name despite its spelling -- round-9 review,
                        // reviewer B found the original round-8 fix threw
                        // away a legitimate caption by bailing here instead
                        // of drawing this conclusion. (Round-9 also treated
                        // `#` as a third body-start marker, reasoning a
                        // preprocessor directive could legally separate the
                        // parameter list from the body; round-10 review
                        // showed that same directive can just as legally
                        // separate a return type from the name, where `#`
                        // then proves nothing. `#` is unconditionally
                        // bailed above instead, since the printer never
                        // emits directives either way.)
                        int probe = index + 1;
                        while (probe < text.Length && char.IsWhiteSpace(text[probe]))
                            probe++;
                        bool bodyStartsHere = probe < text.Length
                            && (text[probe] == '{'
                                || (text[probe] == '=' && probe + 1 < text.Length && text[probe + 1] == '>'));

                        if (!bodyStartsHere)
                        {
                            groupStart = -1;
                            continue;
                        }
                    }

                    name = text[tokenStart..tokenEnd];
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Advances past a leading, balanced <c>[...]</c> attribute list (and any
    /// further leading whitespace), so <see cref="TryGetLocalFunctionDeclaredName"/>
    /// never mistakes an attribute's own argument list -- e.g. the <c>(1)</c>
    /// in <c>[My(1)] static void Other() { }</c> -- for the declaration's
    /// parameter list. Returns the unchanged index (0-based scan position) if
    /// the remaining text is empty or has no leading <c>[</c>. Returns -1 --
    /// signaling the caller to bail entirely rather than trust this scan --
    /// if the brackets never balance, if a quote/apostrophe appears anywhere
    /// inside the attribute list (an attribute argument string can itself
    /// contain <c>[</c>/<c>]</c> characters, e.g.
    /// <c>[Description("[deprecated]")]</c>, which would silently corrupt
    /// this bracket count -- round-4 review, reviewers A and B), or if a
    /// <c>/</c> is encountered anywhere this scan looks, including between
    /// attribute sections and immediately after the last one (a comment --
    /// this codebase's own printer never emits one in a declaration header,
    /// so this is defense in depth, not a reachable product scenario -- could
    /// likewise hide a stray bracket or an identifier that looks like the
    /// real declaration; round-5 review, reviewers A and B).
    /// </summary>
    static int SkipLeadingAttributeLists(string text)
    {
        int index = 0;
        while (true)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;

            if (index < text.Length && text[index] == '/')
                return -1;

            if (index >= text.Length || text[index] != '[')
                return index;

            int depth = 0;
            int scan = index;
            for (; scan < text.Length; scan++)
            {
                char current = text[scan];
                if (current is '"' or '\'' or '/')
                    return -1;

                if (current == '[')
                    depth++;
                else if (current == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        scan++;
                        break;
                    }
                }
            }

            if (depth != 0)
                return -1;

            index = scan;
        }
    }

    /// <summary>
    /// Steps one identifier-part Unicode scalar value backward from
    /// <paramref name="position"/> (not bounded below <paramref name="start"/>),
    /// reporting the new position in <paramref name="newPosition"/> when the
    /// preceding scalar value qualifies. Operates on
    /// <see cref="System.Text.Rune"/> rather than <see cref="char"/> so a
    /// surrogate pair spanning two UTF-16 code units is treated as the one
    /// scalar value it is, and returns <see langword="false"/> -- ending the
    /// backward scan -- on an unpaired surrogate, which is never valid
    /// identifier text. Round-6 review (reviewers A and B):
    /// <see cref="char"/>-based classification examined each surrogate half
    /// independently (always false, since a surrogate's own Unicode category
    /// is never an identifier-part category), silently truncating any
    /// declared name spelled with a supplementary-plane letter, and also
    /// omitted <see cref="System.Globalization.UnicodeCategory.LetterNumber"/>
    /// (e.g. Roman numeral letters such as U+2160), truncating a name
    /// beginning with one of those. This matches
    /// <c>CSharpText.CSharpIdentifierCore.IsIdentifierPartRune</c>, this
    /// repository's own canonical Unicode identifier-part rule, which
    /// <see cref="ILInspector.Decompiler"/> cannot reference directly (it is
    /// <see langword="internal"/> to <c>CSharpText</c>).
    /// </summary>
    static bool TryStepBackOneIdentifierRune(string text, int start, int position, out int newPosition)
    {
        newPosition = position;
        if (position <= start)
            return false;

        char last = text[position - 1];
        System.Text.Rune rune;
        int width;
        if (char.IsLowSurrogate(last))
        {
            if (position - 1 <= start || !char.IsHighSurrogate(text[position - 2]))
                return false;

            rune = new System.Text.Rune(text[position - 2], last);
            width = 2;
        }
        else if (char.IsHighSurrogate(last))
        {
            // A high surrogate can never end a backward scan on its own; it
            // must be immediately followed by its low surrogate, which would
            // already have been consumed by the branch above on the prior
            // step. Reaching this branch means an unpaired surrogate.
            return false;
        }
        else
        {
            rune = new System.Text.Rune(last);
            width = 1;
        }

        if (!IsIdentifierPartRune(rune))
            return false;

        newPosition = position - width;
        return true;
    }

    /// <summary>
    /// Matches C#'s identifier-part character rule (ECMA-334 §6.4.3) over a
    /// full Unicode scalar value rather than a UTF-16 code unit: letter,
    /// digit, and letter-number categories, plus underscore, connector
    /// punctuation, combining marks, and format characters.
    /// </summary>
    static bool IsIdentifierPartRune(System.Text.Rune rune)
        => rune.Value == '_'
            || System.Text.Rune.IsLetterOrDigit(rune)
            || System.Text.Rune.GetUnicodeCategory(rune)
                is System.Globalization.UnicodeCategory.LetterNumber
                or System.Globalization.UnicodeCategory.NonSpacingMark
                or System.Globalization.UnicodeCategory.SpacingCombiningMark
                or System.Globalization.UnicodeCategory.ConnectorPunctuation
                or System.Globalization.UnicodeCategory.Format;


    /// <summary>
    /// Splits <paramref name="text"/> as <c>[qualifier.]callee(arguments)</c>
    /// when it is exactly one call expression (no trailing text after the
    /// closing paren beyond an optional statement-terminating <c>;</c>).
    /// <paramref name="qualifier"/> is <see langword="null"/> for a static
    /// (unqualified) call. Returns <see langword="false"/> for any shape this
    /// narrow heuristic does not recognize -- callers must treat that as "no
    /// opinion", not as evidence the shape does not exist.
    /// </summary>
    internal static bool TryParseQualifiedCall(
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

    /// <summary>
    /// If <paramref name="text"/>[<paramref name="lessThanIndex"/>] opens
    /// what looks like a generic type-argument list (e.g. <c>Bar&lt;A, B&gt;</c>
    /// or <c>Dictionary&lt;string, List&lt;int&gt;&gt;</c>), returns the index
    /// of its matching closing <c>&gt;</c>. Used by
    /// <see cref="SplitTopLevelArguments"/> and
    /// <see cref="TryFindArgumentSpan"/> so a comma nested inside a generic
    /// type-argument list (itself nested inside a call argument, e.g.
    /// <c>Foo(Bar&lt;A,B&gt;())</c>) is never mistaken for a top-level
    /// argument separator (issue #5494).
    ///
    /// This is a best-effort textual heuristic, not a parser: it accepts
    /// only characters that can appear in a type-argument list (identifier
    /// characters -- including supplementary-plane Unicode identifier
    /// characters encoded as UTF-16 surrogate pairs -- the <c>@</c>
    /// verbatim-identifier escape prefix (e.g. <c>@event</c>), <c>.</c>,
    /// <c>,</c>, whitespace, nested <c>&lt;&gt;</c>, and array-suffix
    /// <c>[]</c>) and bails (returns <c>false</c>) the
    /// moment it sees anything else -- a parenthesis, brace, quote,
    /// semicolon, or an arithmetic/comparison/logical operator -- treating
    /// the opening <c>&lt;</c> as an ordinary character (e.g. a
    /// less-than/greater-than comparison such as <c>a &lt; b</c>), exactly
    /// as before this method existed. A comparison chain that happens to
    /// contain a second, unrelated <c>&gt;</c> before any disqualifying
    /// character (e.g. <c>a &lt; b, c &gt; d</c>) can still be misread as a
    /// generic argument list; full disambiguation requires a real C# parser
    /// and is out of scope here (see #5494).
    /// </summary>
    static bool TryFindGenericArgumentListEnd(string text, int lessThanIndex, out int closeIndex)
    {
        closeIndex = -1;
        int depth = 1;
        for (int index = lessThanIndex + 1; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '<') { depth++; continue; }
            if (character == '>')
            {
                depth--;
                if (depth == 0)
                {
                    closeIndex = index;
                    return true;
                }
                continue;
            }
            if (character is '_' or '.' or ',' or '[' or ']' or '@'
                || char.IsWhiteSpace(character))
            {
                continue;
            }
            if (char.IsHighSurrogate(character)
                && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1]))
            {
                // A supplementary-plane identifier character (e.g. a
                // mathematical alphanumeric symbol) is a single Unicode
                // scalar value encoded as a UTF-16 surrogate pair; scan it
                // as one Rune rather than rejecting on the lone high
                // surrogate `char`.
                var rune = new System.Text.Rune(character, text[index + 1]);
                if (IsIdentifierPartRune(rune))
                {
                    index++;
                    continue;
                }
                return false;
            }
            if (!char.IsSurrogate(character) && IsIdentifierPartRune(new System.Text.Rune(character)))
                continue;
            return false;
        }
        return false;
    }

    internal static ImmutableArray<string> SplitTopLevelArguments(string argsText)
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
            if (character == '<')
            {
                // `<<` is always the shift-left operator, never two adjacent
                // generic-argument-list opens (a real nested open always has
                // an identifier between successive `<` characters, e.g.
                // `Foo<Bar<Baz>>`). Skip both characters so the second `<`
                // is never independently reconsidered as its own opener.
                if (index + 1 < argsText.Length && argsText[index + 1] == '<')
                {
                    index++;
                    continue;
                }
                if (TryFindGenericArgumentListEnd(argsText, index, out int genericClose))
                {
                    index = genericClose;
                    continue;
                }
            }
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

    /// <summary>
    /// Locates the trimmed span of the <paramref name="argumentIndex"/>-th
    /// top-level argument in <paramref name="text"/>'s own call-expression
    /// argument list (the same shape <see cref="TryParseQualifiedCall"/> and
    /// <see cref="SplitTopLevelArguments"/> recognize), as an offset and
    /// length relative to <paramref name="text"/> itself. Used to narrow a
    /// structural-diff caret to the exact argument a qualifier moved into or
    /// out of, rather than the call's entire span (issue #5486).
    /// </summary>
    internal static bool TryFindArgumentSpan(string text, int argumentIndex, out int start, out int length)
    {
        start = 0;
        length = 0;
        if (argumentIndex < 0)
            return false;

        int openParen = FindUnquotedIndexOf(text, '(', 0);
        if (openParen < 0)
            return false;
        int closeParen = FindMatchingClose(text, openParen);
        if (closeParen < 0)
            return false;

        int argsStart = openParen + 1;
        string argsText = text[argsStart..closeParen];
        if (argsText.Trim().Length == 0)
            return false;

        int depth = 0;
        bool inString = false;
        bool inChar = false;
        int segmentStart = 0;
        int currentIndex = 0;
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
            if (character == '<')
            {
                // See the matching comment in SplitTopLevelArguments: `<<`
                // is always the shift-left operator, never two adjacent
                // generic-argument-list opens.
                if (index + 1 < argsText.Length && argsText[index + 1] == '<')
                {
                    index++;
                    continue;
                }
                if (TryFindGenericArgumentListEnd(argsText, index, out int genericClose))
                {
                    index = genericClose;
                    continue;
                }
            }
            if (character is '(' or '[' or '{') { depth++; continue; }
            if (character is ')' or ']' or '}') { depth--; continue; }
            if (character != ',' || depth != 0)
                continue;

            if (currentIndex == argumentIndex)
                return TryTrimArgumentSegment(argsText, argsStart, segmentStart, index, out start, out length);

            currentIndex++;
            segmentStart = index + 1;
        }

        return currentIndex == argumentIndex
            && TryTrimArgumentSegment(argsText, argsStart, segmentStart, argsText.Length, out start, out length);
    }

    static bool TryTrimArgumentSegment(
        string argsText,
        int argsStart,
        int segmentStart,
        int segmentEnd,
        out int start,
        out int length)
    {
        start = 0;
        length = 0;
        string raw = argsText[segmentStart..segmentEnd];
        string leftTrimmed = raw.TrimStart();
        int leadingWhitespace = raw.Length - leftTrimmed.Length;
        string trimmed = leftTrimmed.TrimEnd();
        if (trimmed.Length == 0)
            return false;

        start = argsStart + segmentStart + leadingWhitespace;
        length = trimmed.Length;
        return true;
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
