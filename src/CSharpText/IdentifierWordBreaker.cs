namespace CSharpText;

using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

public enum IdentifierWordSpanClassification
{
    Word,
    Ordinal,
    Separator,
    Unresolved,
}

public enum IdentifierWordRuleKind
{
    ExactOracleAtom,
    ExactOracleCompound,
    OracleSegmentation,
    OrdinaryCaseWord,
    SingleUppercaseWord,
    DigitCompoundFallback,
    NumberedFamilyOrdinal,
    WhitespaceSeparator,
    PunctuationSeparator,
    SymbolSeparator,
    UnknownUppercaseRun,
    UnsupportedLetterDigitShape,
    UnsupportedUnicodeCategory,
    LeadingCombiningMark,
    ControlText,
    FormatText,
    MalformedUtf16,
}

public sealed record IdentifierWordRuleEvidence(
    IdentifierWordRuleKind Kind,
    string? OracleEntry = null,
    IdentifierWordOracleEntryKind? OracleEntryKind = null,
    UnicodeCategory? UnicodeCategory = null);

public sealed record IdentifierWordSpan(
    int Start,
    int Length,
    string Text,
    IdentifierWordSpanClassification Classification,
    IdentifierWordRuleEvidence Evidence);

public sealed record IdentifierWordBreakResult(
    string Text,
    IdentifierWordOracleReceipt OracleReceipt,
    IdentifierPopulationReceipt? NumberedFamilyReceipt,
    ImmutableArray<IdentifierWordSpan> Spans);

public enum IdentifierWordBreakRejectionReason
{
    OracleGrammarVersionMismatch,
    NumberedFamilyGrammarVersionMismatch,
}

public abstract record IdentifierWordBreakOutcome
{
    private IdentifierWordBreakOutcome()
    {
    }

    public sealed record Succeeded : IdentifierWordBreakOutcome
    {
        internal Succeeded(IdentifierWordBreakResult result) => Result = result;

        public IdentifierWordBreakResult Result { get; }
    }

    public sealed record Rejected : IdentifierWordBreakOutcome
    {
        internal Rejected(IdentifierWordBreakRejectionReason reason) => Reason = reason;

        public IdentifierWordBreakRejectionReason Reason { get; }
    }
}

public static class IdentifierWordBreaker
{
    public const string GrammarVersion = "csharp-identifier-words-v1";

    public static IdentifierWordBreakOutcome Break(
        string text,
        IdentifierWordOracle oracle,
        IdentifierNumberedFamilyContext? numberedFamilyContext = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(oracle);

        if (!string.Equals(
                oracle.Receipt.GrammarVersion,
                GrammarVersion,
                StringComparison.Ordinal))
        {
            return new IdentifierWordBreakOutcome.Rejected(
                IdentifierWordBreakRejectionReason.OracleGrammarVersionMismatch);
        }
        if (numberedFamilyContext is not null
            && !string.Equals(
                numberedFamilyContext.Receipt.GrammarVersion,
                oracle.Receipt.GrammarVersion,
                StringComparison.Ordinal))
        {
            return new IdentifierWordBreakOutcome.Rejected(
                IdentifierWordBreakRejectionReason.NumberedFamilyGrammarVersionMismatch);
        }

        var spans = new List<IdentifierWordSpan>();
        int offset = 0;
        while (offset < text.Length)
        {
            OperationStatus status = Rune.DecodeFromUtf16(
                text.AsSpan(offset),
                out Rune rune,
                out int consumed);
            if (status != OperationStatus.Done)
            {
                AddSpan(
                    spans,
                    text,
                    offset,
                    1,
                    IdentifierWordSpanClassification.Unresolved,
                    new(IdentifierWordRuleKind.MalformedUtf16));
                offset++;
                continue;
            }

            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsWhiteSpace(rune))
            {
                AddSpan(
                    spans,
                    text,
                    offset,
                    consumed,
                    IdentifierWordSpanClassification.Separator,
                    new(IdentifierWordRuleKind.WhitespaceSeparator));
                offset += consumed;
                continue;
            }
            if (IsPunctuation(category))
            {
                AddSpan(
                    spans,
                    text,
                    offset,
                    consumed,
                    IdentifierWordSpanClassification.Separator,
                    new(
                        IdentifierWordRuleKind.PunctuationSeparator,
                        UnicodeCategory: category));
                offset += consumed;
                continue;
            }
            if (IsSymbol(category))
            {
                AddSpan(
                    spans,
                    text,
                    offset,
                    consumed,
                    IdentifierWordSpanClassification.Separator,
                    new(
                        IdentifierWordRuleKind.SymbolSeparator,
                        UnicodeCategory: category));
                offset += consumed;
                continue;
            }
            if (IsAdmittedLetter(category) || IsAsciiDigit(rune))
            {
                int end = ReadRunEnd(text, offset);
                BreakRun(
                    text,
                    offset,
                    end,
                    oracle,
                    numberedFamilyContext,
                    spans);
                offset = end;
                continue;
            }
            if (IsCombiningMark(category))
            {
                AddSpan(
                    spans,
                    text,
                    offset,
                    consumed,
                    IdentifierWordSpanClassification.Unresolved,
                    new(IdentifierWordRuleKind.LeadingCombiningMark));
                offset += consumed;
                continue;
            }

            IdentifierWordRuleKind unresolved = category switch
            {
                UnicodeCategory.Control => IdentifierWordRuleKind.ControlText,
                UnicodeCategory.Format => IdentifierWordRuleKind.FormatText,
                _ => IdentifierWordRuleKind.UnsupportedUnicodeCategory,
            };
            AddSpan(
                spans,
                text,
                offset,
                consumed,
                IdentifierWordSpanClassification.Unresolved,
                new(unresolved, UnicodeCategory: category));
            offset += consumed;
        }

        ValidateCoverage(text, spans);
        return new IdentifierWordBreakOutcome.Succeeded(
            new IdentifierWordBreakResult(
                text,
                oracle.Receipt,
                numberedFamilyContext?.Receipt,
                spans.ToImmutableArray()));
    }

    static void BreakRun(
        string source,
        int start,
        int end,
        IdentifierWordOracle oracle,
        IdentifierNumberedFamilyContext? context,
        List<IdentifierWordSpan> destination)
    {
        List<RunUnit> units = CreateUnits(source, start, end);
        var exactMatches = SelectExactMatches(source, units, oracle);
        var exactCovered = new bool[units.Count];
        foreach ((int matchStart, ExactMatch match) in exactMatches)
        {
            for (int covered = matchStart; covered < match.EndUnitExclusive; covered++)
                exactCovered[covered] = true;
        }

        for (int index = 0; index < units.Count; index++)
        {
            if (units[index].Kind != RunUnitKind.Digits || exactCovered[index])
                continue;
            if (index != units.Count - 1)
            {
                AddSpan(
                    destination,
                    source,
                    start,
                    end - start,
                    IdentifierWordSpanClassification.Unresolved,
                    new(IdentifierWordRuleKind.UnsupportedLetterDigitShape));
                return;
            }
        }

        var local = new List<IdentifierWordSpan>();
        int unitIndex = 0;
        while (unitIndex < units.Count)
        {
            if (exactMatches.TryGetValue(unitIndex, out ExactMatch match))
            {
                AddSpan(
                    local,
                    source,
                    units[unitIndex].Start,
                    units[match.EndUnitExclusive - 1].End - units[unitIndex].Start,
                    IdentifierWordSpanClassification.Word,
                    ExactOracleEvidence(match.Entry));
                unitIndex = match.EndUnitExclusive;
                continue;
            }

            RunUnit unit = units[unitIndex];
            switch (unit.Kind)
            {
                case RunUnitKind.Ordinary:
                    AddSpan(
                        local,
                        source,
                        unit.Start,
                        unit.Length,
                        IdentifierWordSpanClassification.Word,
                        new(IdentifierWordRuleKind.OrdinaryCaseWord));
                    unitIndex++;
                    break;

                case RunUnitKind.SingleUppercase:
                    AddSpan(
                        local,
                        source,
                        unit.Start,
                        unit.Length,
                        IdentifierWordSpanClassification.Word,
                        new(IdentifierWordRuleKind.SingleUppercaseWord));
                    unitIndex++;
                    break;

                case RunUnitKind.OpaqueUppercase:
                    int clusterEndUnit = unitIndex + 1;
                    if (clusterEndUnit < units.Count
                        && units[clusterEndUnit].Kind == RunUnitKind.Digits
                        && !exactMatches.ContainsKey(clusterEndUnit)
                        && !IsNumberedFamilyOrdinal(
                            source,
                            start,
                            end,
                            units[clusterEndUnit],
                            context))
                    {
                        clusterEndUnit++;
                    }

                    int clusterEnd = units[clusterEndUnit - 1].End;
                    ImmutableArray<OracleSegment> segmentation =
                        FindOracleSegmentation(
                            source.AsSpan(unit.Start, clusterEnd - unit.Start),
                            oracle);
                    if (segmentation.IsDefaultOrEmpty)
                    {
                        AddSpan(
                            local,
                            source,
                            unit.Start,
                            clusterEnd - unit.Start,
                            IdentifierWordSpanClassification.Unresolved,
                            new(IdentifierWordRuleKind.UnknownUppercaseRun));
                    }
                    else
                    {
                        foreach (OracleSegment segment in segmentation)
                        {
                            AddSpan(
                                local,
                                source,
                                unit.Start + segment.Start,
                                segment.Length,
                                IdentifierWordSpanClassification.Word,
                                new(
                                    IdentifierWordRuleKind.OracleSegmentation,
                                    segment.Entry.Text,
                                    segment.Entry.Kind));
                        }
                    }
                    unitIndex = clusterEndUnit;
                    break;

                case RunUnitKind.Digits:
                    bool numberedFamily = IsNumberedFamilyOrdinal(
                        source,
                        start,
                        end,
                        unit,
                        context);
                    if (numberedFamily)
                    {
                        AddSpan(
                            local,
                            source,
                            unit.Start,
                            unit.Length,
                            IdentifierWordSpanClassification.Ordinal,
                            new(IdentifierWordRuleKind.NumberedFamilyOrdinal));
                    }
                    else if (local.Count > 0
                        && local[^1].Classification == IdentifierWordSpanClassification.Word
                        && local[^1].Start + local[^1].Length == unit.Start)
                    {
                        IdentifierWordSpan word = local[^1];
                        local[^1] = new IdentifierWordSpan(
                            word.Start,
                            unit.End - word.Start,
                            source[word.Start..unit.End],
                            IdentifierWordSpanClassification.Word,
                            new(IdentifierWordRuleKind.DigitCompoundFallback));
                    }
                    else
                    {
                        AddSpan(
                            local,
                            source,
                            unit.Start,
                            unit.Length,
                            IdentifierWordSpanClassification.Unresolved,
                            new(IdentifierWordRuleKind.UnsupportedLetterDigitShape));
                    }
                    unitIndex++;
                    break;
            }
        }

        foreach (IdentifierWordSpan span in local)
            AddSpan(destination, span);
    }

    static bool IsNumberedFamilyOrdinal(
        string source,
        int runStart,
        int runEnd,
        RunUnit unit,
        IdentifierNumberedFamilyContext? context)
        => unit.Kind == RunUnitKind.Digits
            && unit.End == runEnd
            && runStart == 0
            && runEnd == source.Length
            && context is not null
            && IdentifierNumberedFamilyContext.IsCanonicalPositiveOrdinal(
                source[unit.Start..unit.End])
            && context.ContainsPrefix(source[..unit.Start]);

    static int ReadRunEnd(string text, int start)
    {
        int offset = start;
        bool followsLetter = false;
        while (offset < text.Length)
        {
            OperationStatus status = Rune.DecodeFromUtf16(
                text.AsSpan(offset),
                out Rune rune,
                out int consumed);
            if (status != OperationStatus.Done)
                break;

            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (IsAdmittedLetter(category))
            {
                followsLetter = true;
            }
            else if (IsAsciiDigit(rune))
            {
                followsLetter = false;
            }
            else if (IsCombiningMark(category) && followsLetter)
            {
            }
            else
            {
                break;
            }
            offset += consumed;
        }
        return offset;
    }

    static List<RunUnit> CreateUnits(string source, int start, int end)
    {
        var units = new List<RunUnit>();
        int offset = start;
        while (offset < end)
        {
            Rune.DecodeFromUtf16(source.AsSpan(offset), out Rune rune, out _);
            if (IsAsciiDigit(rune))
            {
                int digitEnd = offset + 1;
                while (digitEnd < end && source[digitEnd] is >= '0' and <= '9')
                    digitEnd++;
                units.Add(new RunUnit(offset, digitEnd, RunUnitKind.Digits));
                offset = digitEnd;
                continue;
            }

            var letters = new List<LetterCluster>();
            while (offset < end)
            {
                Rune.DecodeFromUtf16(
                    source.AsSpan(offset),
                    out rune,
                    out int consumed);
                UnicodeCategory category = Rune.GetUnicodeCategory(rune);
                if (!IsAdmittedLetter(category))
                    break;

                int clusterStart = offset;
                offset += consumed;
                while (offset < end)
                {
                    Rune.DecodeFromUtf16(
                        source.AsSpan(offset),
                        out Rune mark,
                        out int markLength);
                    if (!IsCombiningMark(Rune.GetUnicodeCategory(mark)))
                        break;
                    offset += markLength;
                }
                letters.Add(new LetterCluster(
                    clusterStart,
                    offset,
                    category == UnicodeCategory.LowercaseLetter
                        ? LetterCase.Lower
                        : LetterCase.Upper));
            }

            int unitStart = 0;
            for (int i = 1; i < letters.Count; i++)
            {
                bool boundary = letters[i - 1].Case == LetterCase.Lower
                    && letters[i].Case == LetterCase.Upper;
                if (!boundary
                    && letters[i - 1].Case == LetterCase.Upper
                    && letters[i].Case == LetterCase.Upper
                    && i + 1 < letters.Count
                    && letters[i + 1].Case == LetterCase.Lower)
                {
                    boundary = true;
                }
                if (!boundary)
                    continue;

                AddLetterUnit(units, letters, unitStart, i);
                unitStart = i;
            }
            AddLetterUnit(units, letters, unitStart, letters.Count);
        }
        return units;
    }

    static void AddLetterUnit(
        List<RunUnit> units,
        List<LetterCluster> letters,
        int start,
        int end)
    {
        bool allUpper = true;
        for (int i = start; i < end; i++)
            allUpper &= letters[i].Case == LetterCase.Upper;

        RunUnitKind kind = allUpper
            ? end - start == 1
                ? RunUnitKind.SingleUppercase
                : RunUnitKind.OpaqueUppercase
            : RunUnitKind.Ordinary;
        units.Add(new RunUnit(
            letters[start].Start,
            letters[end - 1].End,
            kind));
    }

    static Dictionary<int, ExactMatch> SelectExactMatches(
        string source,
        List<RunUnit> units,
        IdentifierWordOracle oracle)
    {
        var matches = new Dictionary<int, ExactMatch>();
        int index = 0;
        while (index < units.Count)
        {
            ExactMatch? best = null;
            foreach (IdentifierWordOracleEntry entry in oracle.Entries)
            {
                int endOffset = units[index].Start + entry.Text.Length;
                if (endOffset > units[^1].End
                    || !source.AsSpan(units[index].Start, entry.Text.Length)
                        .SequenceEqual(entry.Text))
                {
                    continue;
                }

                int endUnit = index;
                while (endUnit < units.Count && units[endUnit].End < endOffset)
                    endUnit++;
                if (endUnit == units.Count || units[endUnit].End != endOffset)
                    continue;

                int endExclusive = endUnit + 1;
                if (best is null || entry.Text.Length > best.Value.Length)
                    best = new ExactMatch(endExclusive, entry.Text.Length, entry);
            }
            if (best is { } selected)
            {
                matches.Add(index, selected);
                index = selected.EndUnitExclusive;
            }
            else
            {
                index++;
            }
        }
        return matches;
    }

    static ImmutableArray<OracleSegment> FindOracleSegmentation(
        ReadOnlySpan<char> text,
        IdentifierWordOracle oracle)
    {
        SegmentationPath?[] paths = new SegmentationPath?[text.Length + 1];
        paths[text.Length] = new SegmentationPath([]);
        for (int offset = text.Length - 1; offset >= 0; offset--)
        {
            SegmentationPath? best = null;
            foreach (IdentifierWordOracleEntry entry in oracle.Entries)
            {
                if (!text[offset..].StartsWith(entry.Text, StringComparison.Ordinal))
                    continue;
                int next = offset + entry.Text.Length;
                if (next > text.Length || paths[next] is not { } tail)
                    continue;

                var segments = ImmutableArray.CreateBuilder<OracleSegment>(
                    tail.Segments.Length + 1);
                segments.Add(new OracleSegment(offset, entry.Text.Length, entry));
                segments.AddRange(tail.Segments);
                var candidate = new SegmentationPath(segments.MoveToImmutable());
                if (best is null || IsBetter(candidate, best))
                    best = candidate;
            }
            paths[offset] = best;
        }
        return paths[0]?.Segments ?? [];
    }

    static bool IsBetter(SegmentationPath candidate, SegmentationPath current)
    {
        if (candidate.Segments.Length != current.Segments.Length)
            return candidate.Segments.Length < current.Segments.Length;

        for (int i = 0; i < candidate.Segments.Length; i++)
        {
            int length = candidate.Segments[i].Length;
            int currentLength = current.Segments[i].Length;
            if (length != currentLength)
                return length > currentLength;

            int spelling = string.CompareOrdinal(
                candidate.Segments[i].Entry.Text,
                current.Segments[i].Entry.Text);
            if (spelling != 0)
                return spelling < 0;
        }
        return false;
    }

    static IdentifierWordRuleEvidence ExactOracleEvidence(IdentifierWordOracleEntry entry)
        => new(
            entry.Kind == IdentifierWordOracleEntryKind.Atom
                ? IdentifierWordRuleKind.ExactOracleAtom
                : IdentifierWordRuleKind.ExactOracleCompound,
            entry.Text,
            entry.Kind);

    static void AddSpan(
        List<IdentifierWordSpan> spans,
        string source,
        int start,
        int length,
        IdentifierWordSpanClassification classification,
        IdentifierWordRuleEvidence evidence)
        => AddSpan(
            spans,
            new IdentifierWordSpan(
                start,
                length,
                source.Substring(start, length),
                classification,
                evidence));

    static void AddSpan(
        List<IdentifierWordSpan> spans,
        IdentifierWordSpan span)
    {
        if (spans.Count > 0
            && span.Classification is IdentifierWordSpanClassification.Separator
                or IdentifierWordSpanClassification.Unresolved
            && spans[^1].Classification == span.Classification
            && spans[^1].Evidence == span.Evidence
            && spans[^1].Start + spans[^1].Length == span.Start)
        {
            IdentifierWordSpan previous = spans[^1];
            spans[^1] = previous with
            {
                Length = previous.Length + span.Length,
                Text = previous.Text + span.Text,
            };
            return;
        }
        spans.Add(span);
    }

    static void ValidateCoverage(
        string source,
        List<IdentifierWordSpan> spans)
    {
        int offset = 0;
        foreach (IdentifierWordSpan span in spans)
        {
            if (span.Start != offset
                || span.Length <= 0
                || !source.AsSpan(span.Start, span.Length).SequenceEqual(span.Text))
            {
                throw new InvalidOperationException(
                    "Identifier word spans must cover the exact source text.");
            }
            offset += span.Length;
        }
        if (offset != source.Length)
        {
            throw new InvalidOperationException(
                "Identifier word spans must cover the exact source text.");
        }
    }

    static bool IsAdmittedLetter(UnicodeCategory category)
        => category is
            UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter;

    static bool IsCombiningMark(UnicodeCategory category)
        => category is
            UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark;

    static bool IsAsciiDigit(Rune rune)
        => rune.IsAscii && Rune.IsDigit(rune);

    static bool IsPunctuation(UnicodeCategory category)
        => category is
            UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation;

    static bool IsSymbol(UnicodeCategory category)
        => category is
            UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol
            or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol;

    enum RunUnitKind
    {
        Ordinary,
        SingleUppercase,
        OpaqueUppercase,
        Digits,
    }

    enum LetterCase
    {
        Lower,
        Upper,
    }

    readonly record struct LetterCluster(int Start, int End, LetterCase Case);

    readonly record struct RunUnit(int Start, int End, RunUnitKind Kind)
    {
        public int Length => End - Start;
    }

    readonly record struct ExactMatch(
        int EndUnitExclusive,
        int Length,
        IdentifierWordOracleEntry Entry);

    readonly record struct OracleSegment(
        int Start,
        int Length,
        IdentifierWordOracleEntry Entry);

    sealed record SegmentationPath(ImmutableArray<OracleSegment> Segments);
}
