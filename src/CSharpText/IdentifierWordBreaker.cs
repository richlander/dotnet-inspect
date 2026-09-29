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
        ImmutableArray<OracleSegment> selections =
            SelectOracleMatches(source, units, oracle, out bool[] unitBoundaries);
        var local = new List<IdentifierWordSpan>();
        int unitIndex = 0;
        int selectionIndex = 0;
        while (unitIndex < units.Count)
        {
            if (selectionIndex < selections.Length
                && selections[selectionIndex].Start == units[unitIndex].Start)
            {
                int chainEnd = selections[selectionIndex].Start;
                do
                {
                    OracleSegment selection = selections[selectionIndex];
                    int selectionEnd = selection.Start + selection.Length;
                    IdentifierWordRuleEvidence evidence =
                        unitBoundaries[selection.Start]
                        && unitBoundaries[selectionEnd]
                            ? ExactOracleEvidence(selection.Entry)
                            : new(
                                IdentifierWordRuleKind.OracleSegmentation,
                                selection.Entry.Text,
                                selection.Entry.Kind);
                    AddSpan(
                        local,
                        source,
                        selection.Start,
                        selection.Length,
                        IdentifierWordSpanClassification.Word,
                        evidence);
                    chainEnd = selectionEnd;
                    selectionIndex++;
                }
                while (selectionIndex < selections.Length
                    && selections[selectionIndex].Start == chainEnd);

                while (unitIndex < units.Count && units[unitIndex].End <= chainEnd)
                    unitIndex++;
                if (unitIndex > 0 && units[unitIndex - 1].End != chainEnd)
                {
                    throw new InvalidOperationException(
                        "Oracle selection must end on a word-unit boundary.");
                }
                continue;
            }

            int nextSelectionStart = selectionIndex < selections.Length
                ? selections[selectionIndex].Start
                : end;
            int unmatchedEnd = unitIndex;
            while (unmatchedEnd < units.Count
                && units[unmatchedEnd].Start < nextSelectionStart)
            {
                unmatchedEnd++;
            }
            ClassifyUnmatchedUnits(
                source,
                start,
                end,
                units,
                unitIndex,
                unmatchedEnd,
                context,
                local);
            unitIndex = unmatchedEnd;
        }

        foreach (IdentifierWordSpan span in local)
            AddSpan(destination, span);
    }

    static void ClassifyUnmatchedUnits(
        string source,
        int runStart,
        int runEnd,
        List<RunUnit> units,
        int startUnit,
        int endUnit,
        IdentifierNumberedFamilyContext? context,
        List<IdentifierWordSpan> destination)
    {
        if (startUnit == endUnit)
            return;

        for (int index = startUnit; index < endUnit; index++)
        {
            if (units[index].Kind == RunUnitKind.Digits
                && index != units.Count - 1)
            {
                AddSpan(
                    destination,
                    source,
                    units[startUnit].Start,
                    units[endUnit - 1].End - units[startUnit].Start,
                    IdentifierWordSpanClassification.Unresolved,
                    new(IdentifierWordRuleKind.UnsupportedLetterDigitShape));
                return;
            }
        }

        int unitIndex = startUnit;
        while (unitIndex < endUnit)
        {
            RunUnit unit = units[unitIndex];
            switch (unit.Kind)
            {
                case RunUnitKind.Ordinary:
                    AddSpan(
                        destination,
                        source,
                        unit.Start,
                        unit.Length,
                        IdentifierWordSpanClassification.Word,
                        new(IdentifierWordRuleKind.OrdinaryCaseWord));
                    unitIndex++;
                    break;

                case RunUnitKind.SingleUppercase:
                    AddSpan(
                        destination,
                        source,
                        unit.Start,
                        unit.Length,
                        IdentifierWordSpanClassification.Word,
                        new(IdentifierWordRuleKind.SingleUppercaseWord));
                    unitIndex++;
                    break;

                case RunUnitKind.OpaqueUppercase:
                    if (unitIndex + 1 < endUnit
                        && unitIndex + 1 == units.Count - 1
                        && units[unitIndex + 1].Kind == RunUnitKind.Digits
                        && !IsNumberedFamilyOrdinal(
                            source,
                            runStart,
                            runEnd,
                            units[unitIndex + 1],
                            context))
                    {
                        RunUnit digits = units[unitIndex + 1];
                        AddSpan(
                            destination,
                            source,
                            unit.Start,
                            digits.End - unit.Start,
                            IdentifierWordSpanClassification.Unresolved,
                            new(IdentifierWordRuleKind.UnknownUppercaseRun));
                        unitIndex += 2;
                    }
                    else
                    {
                        AddSpan(
                            destination,
                            source,
                            unit.Start,
                            unit.Length,
                            IdentifierWordSpanClassification.Unresolved,
                            new(IdentifierWordRuleKind.UnknownUppercaseRun));
                        unitIndex++;
                    }
                    break;

                case RunUnitKind.Digits:
                    bool numberedFamily = IsNumberedFamilyOrdinal(
                        source,
                        runStart,
                        runEnd,
                        unit,
                        context);
                    if (numberedFamily)
                    {
                        AddSpan(
                            destination,
                            source,
                            unit.Start,
                            unit.Length,
                            IdentifierWordSpanClassification.Ordinal,
                            new(IdentifierWordRuleKind.NumberedFamilyOrdinal));
                    }
                    else if (destination.Count > 0
                        && destination[^1].Classification
                            == IdentifierWordSpanClassification.Word
                        && destination[^1].Start + destination[^1].Length == unit.Start)
                    {
                        IdentifierWordSpan word = destination[^1];
                        destination[^1] = new IdentifierWordSpan(
                            word.Start,
                            unit.End - word.Start,
                            source[word.Start..unit.End],
                            IdentifierWordSpanClassification.Word,
                            new(IdentifierWordRuleKind.DigitCompoundFallback));
                    }
                    else
                    {
                        AddSpan(
                            destination,
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

    static ImmutableArray<OracleSegment> SelectOracleMatches(
        string source,
        List<RunUnit> units,
        IdentifierWordOracle oracle,
        out bool[] unitBoundaries)
    {
        bool[] allowedBoundaries = CreateOracleBoundaries(
            source,
            units,
            out unitBoundaries);
        var paths = new OracleSelectionPath?[units.Count + 1];
        paths[units.Count] = new OracleSelectionPath(0, []);
        for (int index = units.Count - 1; index >= 0; index--)
        {
            OracleSelectionPath best = paths[index + 1]!;
            int selectionStart = units[index].Start;
            SegmentationPath?[] segmentations = FindOracleSegmentations(
                source,
                selectionStart,
                units[^1].End,
                allowedBoundaries,
                oracle);
            for (int endUnit = index + 1; endUnit <= units.Count; endUnit++)
            {
                int selectionEnd = units[endUnit - 1].End;
                if (segmentations[selectionEnd - selectionStart]
                    is not { } segmentation)
                    continue;

                OracleSelectionPath tail = paths[endUnit]!;
                var selections = ImmutableArray.CreateBuilder<OracleSegment>(
                    segmentation.Segments.Length + tail.Selections.Length);
                selections.AddRange(segmentation.Segments);
                selections.AddRange(tail.Selections);
                var candidate = new OracleSelectionPath(
                    selectionEnd - selectionStart + tail.CoveredLength,
                    selections.MoveToImmutable());
                if (IsBetter(candidate, best))
                    best = candidate;
            }
            paths[index] = best;
        }

        return paths[0]!.Selections;
    }

    static bool[] CreateOracleBoundaries(
        string source,
        List<RunUnit> units,
        out bool[] unitBoundaries)
    {
        var allowed = new bool[source.Length + 1];
        unitBoundaries = new bool[source.Length + 1];
        foreach (RunUnit unit in units)
        {
            allowed[unit.Start] = true;
            allowed[unit.End] = true;
            unitBoundaries[unit.Start] = true;
            unitBoundaries[unit.End] = true;
            if (unit.Kind != RunUnitKind.OpaqueUppercase)
                continue;

            int offset = unit.Start;
            while (offset < unit.End)
            {
                allowed[offset] = true;
                Rune.DecodeFromUtf16(
                    source.AsSpan(offset),
                    out _,
                    out int consumed);
                offset += consumed;
                while (offset < unit.End)
                {
                    Rune.DecodeFromUtf16(
                        source.AsSpan(offset),
                        out Rune mark,
                        out int markLength);
                    if (!IsCombiningMark(Rune.GetUnicodeCategory(mark)))
                        break;
                    offset += markLength;
                }
                allowed[offset] = true;
            }
        }
        return allowed;
    }

    static SegmentationPath?[] FindOracleSegmentations(
        string source,
        int start,
        int end,
        bool[] allowedBoundaries,
        IdentifierWordOracle oracle)
    {
        var paths = new SegmentationPath?[end - start + 1];
        paths[0] = new SegmentationPath([]);
        for (int offset = start; offset < end; offset++)
        {
            if (paths[offset - start] is not { } current)
                continue;

            foreach (IdentifierWordOracleEntry entry in oracle.Entries)
            {
                int next = offset + entry.Text.Length;
                if (next > end
                    || !allowedBoundaries[next]
                    || !source.AsSpan(offset, entry.Text.Length)
                        .SequenceEqual(entry.Text))
                {
                    continue;
                }

                var segments = ImmutableArray.CreateBuilder<OracleSegment>(
                    current.Segments.Length + 1);
                segments.AddRange(current.Segments);
                segments.Add(new OracleSegment(offset, entry.Text.Length, entry));
                var candidate = new SegmentationPath(segments.MoveToImmutable());
                ref SegmentationPath? best = ref paths[next - start];
                if (best is null || IsBetter(candidate, best))
                    best = candidate;
            }
        }
        return paths;
    }

    static bool IsBetter(OracleSelectionPath candidate, OracleSelectionPath current)
    {
        if (candidate.CoveredLength != current.CoveredLength)
            return candidate.CoveredLength > current.CoveredLength;
        if (candidate.Selections.Length != current.Selections.Length)
            return candidate.Selections.Length < current.Selections.Length;

        for (int i = 0; i < candidate.Selections.Length; i++)
        {
            OracleSegment selection = candidate.Selections[i];
            OracleSegment currentSelection = current.Selections[i];
            if (selection.Start != currentSelection.Start)
                return selection.Start < currentSelection.Start;
            if (selection.Length != currentSelection.Length)
                return selection.Length > currentSelection.Length;

            int spelling = string.CompareOrdinal(
                selection.Entry.Text,
                currentSelection.Entry.Text);
            if (spelling != 0)
                return spelling < 0;
        }
        return false;
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
            && spans[^1].Evidence.Kind == span.Evidence.Kind
            && spans[^1].Start + spans[^1].Length == span.Start)
        {
            IdentifierWordSpan previous = spans[^1];
            spans[^1] = previous with
            {
                Length = previous.Length + span.Length,
                Text = previous.Text + span.Text,
                Evidence = previous.Evidence with
                {
                    UnicodeCategory = previous.Evidence.UnicodeCategory
                        == span.Evidence.UnicodeCategory
                            ? previous.Evidence.UnicodeCategory
                            : null,
                },
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

    sealed record OracleSelectionPath(
        int CoveredLength,
        ImmutableArray<OracleSegment> Selections);

    readonly record struct OracleSegment(
        int Start,
        int Length,
        IdentifierWordOracleEntry Entry);

    sealed record SegmentationPath(ImmutableArray<OracleSegment> Segments);
}
