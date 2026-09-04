using System.Globalization;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;

namespace DotnetInspector.CommandLine;

[Flags]
internal enum CliRowSelectionCapabilities
{
    None = 0,
    HeadTail = 1,
    Window = 2,
    Top = 4,
    OrderBy = 8,
    Lines = 16,
    All = HeadTail | Window | Top | OrderBy | Lines
}

internal enum CliLineSelectionDirection
{
    Head,
    Tail
}

internal sealed class CliLineSelectionIntent
{
    public CliLineSelectionIntent(
        int count,
        CliLineSelectionDirection direction)
    {
        Count = count;
        Direction = direction;
    }

    public int Count { get; }

    public CliLineSelectionDirection Direction { get; }
}

internal enum CliRowSelectionFailureReason
{
    MalformedValue,
    NonPositiveValue,
    OverflowValue,
    InvalidWindowForm,
    ReversedWindow,
    RepeatedGesture,
    ConflictingDirection,
    ModifierRequiresCount,
    UnsupportedCapability
}

internal sealed class CliRowSelectionFailure
{
    public CliRowSelectionFailure(
        CliRowSelectionFailureReason reason,
        CliRowSelectionOccurrenceKind occurrenceKind,
        int position,
        CliRowSelectionCapabilities missingCapabilities =
            CliRowSelectionCapabilities.None)
    {
        Reason = reason;
        OccurrenceKind = occurrenceKind;
        Position = position;
        MissingCapabilities = missingCapabilities;
    }

    public CliRowSelectionFailureReason Reason { get; }

    public CliRowSelectionOccurrenceKind OccurrenceKind { get; }

    public int Position { get; }

    public CliRowSelectionCapabilities MissingCapabilities { get; }
}

internal sealed class CliRowSelectionLowering<TOrderOperand>
    where TOrderOperand : notnull
{
    private readonly bool _hasBaselineOrderOperand;
    private readonly TOrderOperand _baselineOrderOperand;

    public CliRowSelectionLowering(
        RowSelectionIntent<TOrderOperand> semanticIntent,
        CliLineSelectionIntent? lineIntent,
        bool hasBaselineOrderOperand,
        TOrderOperand baselineOrderOperand)
    {
        SemanticIntent = semanticIntent;
        LineIntent = lineIntent;
        _hasBaselineOrderOperand = hasBaselineOrderOperand;
        _baselineOrderOperand = baselineOrderOperand;
    }

    public RowSelectionIntent<TOrderOperand> SemanticIntent { get; }

    public CliLineSelectionIntent? LineIntent { get; }

    public bool HasBaselineOrderOperand =>
        _hasBaselineOrderOperand;

    public TOrderOperand BaselineOrderOperand =>
        _hasBaselineOrderOperand
            ? _baselineOrderOperand
            : throw new InvalidOperationException(
                "The CLI row-selection request has no baseline-order operand.");
}

internal sealed class CliRowSelectionLoweringResult<TOrderOperand>
    where TOrderOperand : notnull
{
    private CliRowSelectionLoweringResult(
        CliRowSelectionLowering<TOrderOperand>? value,
        CliRowSelectionFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public bool IsSuccess => Value is not null;

    public CliRowSelectionLowering<TOrderOperand>? Value { get; }

    public CliRowSelectionFailure? Failure { get; }

    public static CliRowSelectionLoweringResult<TOrderOperand> Success(
        CliRowSelectionLowering<TOrderOperand> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value, null);
    }

    public static CliRowSelectionLoweringResult<TOrderOperand> Failed(
        CliRowSelectionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(null, failure);
    }
}

internal static class CliRowSelectionLowerer
{
    public static CliRowSelectionLoweringResult<TOrderOperand> Lower<
        TOrderOperand>(
        IReadOnlyList<CliRowSelectionOccurrence<TOrderOperand>>
            occurrences,
        CliRowSelectionCapabilities capabilities)
        where TOrderOperand : notnull
    {
        ArgumentNullException.ThrowIfNull(occurrences);

        ParsedOccurrence<TOrderOperand>[] ordered =
            ParseAndOrder(
                occurrences,
                out CliRowSelectionFailure? valueFailure);
        if (valueFailure is not null)
        {
            return CliRowSelectionLoweringResult<TOrderOperand>.Failed(
                valueFailure);
        }

        CliRowSelectionFailure? conflict =
            FindConflict(ordered);
        if (conflict is not null)
        {
            return CliRowSelectionLoweringResult<TOrderOperand>.Failed(
                conflict);
        }

        CliRowSelectionFailure? capabilityFailure =
            FindCapabilityFailure(
                ordered,
                capabilities);
        if (capabilityFailure is not null)
        {
            return CliRowSelectionLoweringResult<TOrderOperand>.Failed(
                capabilityFailure);
        }

        return CliRowSelectionLoweringResult<TOrderOperand>.Success(
            Build(ordered));
    }

    private static ParsedOccurrence<TOrderOperand>[] ParseAndOrder<
        TOrderOperand>(
        IReadOnlyList<CliRowSelectionOccurrence<TOrderOperand>>
            occurrences,
        out CliRowSelectionFailure? failure)
        where TOrderOperand : notnull
    {
        var indexed =
            new (
                CliRowSelectionOccurrence<TOrderOperand> Occurrence,
                int Index)[occurrences.Count];
        for (int index = 0; index < occurrences.Count; index++)
        {
            indexed[index] =
                (
                    occurrences[index]
                        ?? throw new ArgumentNullException(
                            nameof(occurrences),
                            $"Occurrence {index + 1} is null."),
                    index
                );
        }

        Array.Sort(
            indexed,
            static (left, right) =>
            {
                int position =
                    left.Occurrence.Position.CompareTo(
                        right.Occurrence.Position);
                return position != 0
                    ? position
                    : left.Index.CompareTo(right.Index);
            });

        var parsed =
            new ParsedOccurrence<TOrderOperand>[indexed.Length];
        for (int index = 0; index < indexed.Length; index++)
        {
            CliRowSelectionOccurrence<TOrderOperand> occurrence =
                indexed[index].Occurrence;
            if (!TryParse(
                    occurrence,
                    out parsed[index],
                    out CliRowSelectionFailureReason reason))
            {
                failure =
                    new(
                        reason,
                        occurrence.Kind,
                        occurrence.Position);
                return parsed;
            }
        }

        failure = null;
        return parsed;
    }

    private static bool TryParse<TOrderOperand>(
        CliRowSelectionOccurrence<TOrderOperand> occurrence,
        out ParsedOccurrence<TOrderOperand> parsed,
        out CliRowSelectionFailureReason failure)
        where TOrderOperand : notnull
    {
        switch (occurrence.Kind)
        {
            case CliRowSelectionOccurrenceKind.Limit:
            case CliRowSelectionOccurrenceKind.Top:
                if (!TryParsePositiveInteger(
                        occurrence.Value,
                        out int count,
                        out failure))
                {
                    parsed = default;
                    return false;
                }

                parsed =
                    new(
                        occurrence,
                        count,
                        null,
                        null);
                return true;

            case CliRowSelectionOccurrenceKind.Rows:
                if (!TryParseWindow(
                        occurrence.Value,
                        out int? start,
                        out int? end,
                        out failure))
                {
                    parsed = default;
                    return false;
                }

                parsed =
                    new(
                        occurrence,
                        0,
                        start,
                        end);
                return true;

            default:
                parsed =
                    new(
                        occurrence,
                        0,
                        null,
                        null);
                failure = default;
                return true;
        }
    }

    private static bool TryParsePositiveInteger(
        string value,
        out int parsed,
        out CliRowSelectionFailureReason failure)
    {
        parsed = 0;
        if (value.Length == 0)
        {
            failure = CliRowSelectionFailureReason.MalformedValue;
            return false;
        }

        for (int index = 0; index < value.Length; index++)
        {
            if (!char.IsAsciiDigit(value[index]))
            {
                failure =
                    CliRowSelectionFailureReason.MalformedValue;
                return false;
            }
        }

        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out parsed))
        {
            failure = CliRowSelectionFailureReason.OverflowValue;
            return false;
        }

        if (parsed == 0)
        {
            failure =
                CliRowSelectionFailureReason.NonPositiveValue;
            return false;
        }

        failure = default;
        return true;
    }

    private static bool TryParseWindow(
        string value,
        out int? start,
        out int? end,
        out CliRowSelectionFailureReason failure)
    {
        start = null;
        end = null;

        int separator =
            value.IndexOf(
                "..",
                StringComparison.Ordinal);
        if (separator < 0
            || value.IndexOf(
                "..",
                separator + 2,
                StringComparison.Ordinal) >= 0)
        {
            failure =
                CliRowSelectionFailureReason.InvalidWindowForm;
            return false;
        }

        string startText = value[..separator];
        string endText = value[(separator + 2)..];
        if (startText.Length == 0
            && endText.Length == 0)
        {
            failure =
                CliRowSelectionFailureReason.InvalidWindowForm;
            return false;
        }

        if (startText.Length > 0)
        {
            if (!TryParsePositiveInteger(
                    startText,
                    out int parsedStart,
                    out failure))
            {
                return false;
            }

            start = parsedStart;
        }

        if (endText.Length > 0)
        {
            if (!TryParsePositiveInteger(
                    endText,
                    out int parsedEnd,
                    out failure))
            {
                return false;
            }

            end = parsedEnd;
        }

        if (start is not null
            && end is not null
            && end < start)
        {
            failure =
                CliRowSelectionFailureReason.ReversedWindow;
            return false;
        }

        failure = default;
        return true;
    }

    private static CliRowSelectionFailure? FindConflict<
        TOrderOperand>(
        IReadOnlyList<ParsedOccurrence<TOrderOperand>> ordered)
        where TOrderOperand : notnull
    {
        bool countSeen = false;
        bool rowsSeen = false;
        bool topSeen = false;
        bool orderSeen = false;
        bool headSeen = false;
        bool tailSeen = false;
        bool tailLinesSeen = false;
        ParsedOccurrence<TOrderOperand>? firstModifier = null;

        for (int index = 0; index < ordered.Count; index++)
        {
            ParsedOccurrence<TOrderOperand> parsed =
                ordered[index];
            CliRowSelectionOccurrence<TOrderOperand> occurrence =
                parsed.Occurrence;

            switch (occurrence.Kind)
            {
                case CliRowSelectionOccurrenceKind.Limit:
                    if (countSeen)
                        return Repeated(occurrence);
                    countSeen = true;
                    break;

                case CliRowSelectionOccurrenceKind.Rows:
                    if (rowsSeen)
                        return Repeated(occurrence);
                    rowsSeen = true;
                    break;

                case CliRowSelectionOccurrenceKind.Top:
                    if (topSeen)
                        return Repeated(occurrence);
                    topSeen = true;
                    break;

                case CliRowSelectionOccurrenceKind.OrderBy:
                    if (orderSeen)
                        return Repeated(occurrence);
                    orderSeen = true;
                    break;

                case CliRowSelectionOccurrenceKind.Head:
                    firstModifier ??= parsed;
                    if (!headSeen && (tailSeen || tailLinesSeen))
                        return DirectionConflict(occurrence);
                    headSeen = true;
                    break;

                case CliRowSelectionOccurrenceKind.Tail:
                    firstModifier ??= parsed;
                    if (!tailSeen && headSeen)
                        return DirectionConflict(occurrence);
                    tailSeen = true;
                    break;

                case CliRowSelectionOccurrenceKind.Lines:
                    firstModifier ??= parsed;
                    break;

                case CliRowSelectionOccurrenceKind.TailLines:
                    firstModifier ??= parsed;
                    if (!tailLinesSeen && headSeen)
                        return DirectionConflict(occurrence);
                    tailLinesSeen = true;
                    break;
            }
        }

        if (!countSeen && firstModifier is not null)
        {
            CliRowSelectionOccurrence<TOrderOperand> occurrence =
                firstModifier.Value.Occurrence;
            return new(
                CliRowSelectionFailureReason.ModifierRequiresCount,
                occurrence.Kind,
                occurrence.Position);
        }

        return null;
    }

    private static CliRowSelectionFailure Repeated<TOrderOperand>(
        CliRowSelectionOccurrence<TOrderOperand> occurrence)
        where TOrderOperand : notnull =>
        new(
            CliRowSelectionFailureReason.RepeatedGesture,
            occurrence.Kind,
            occurrence.Position);

    private static CliRowSelectionFailure DirectionConflict<
        TOrderOperand>(
        CliRowSelectionOccurrence<TOrderOperand> occurrence)
        where TOrderOperand : notnull =>
        new(
            CliRowSelectionFailureReason.ConflictingDirection,
            occurrence.Kind,
            occurrence.Position);

    private static CliRowSelectionFailure? FindCapabilityFailure<
        TOrderOperand>(
        IReadOnlyList<ParsedOccurrence<TOrderOperand>> ordered,
        CliRowSelectionCapabilities capabilities)
        where TOrderOperand : notnull
    {
        bool lineSelection = false;
        for (int index = 0; index < ordered.Count; index++)
        {
            CliRowSelectionOccurrenceKind kind =
                ordered[index].Occurrence.Kind;
            if (kind is CliRowSelectionOccurrenceKind.Lines
                or CliRowSelectionOccurrenceKind.TailLines)
            {
                lineSelection = true;
                break;
            }
        }

        for (int index = 0; index < ordered.Count; index++)
        {
            CliRowSelectionOccurrence<TOrderOperand> occurrence =
                ordered[index].Occurrence;
            CliRowSelectionCapabilities required =
                RequiredCapabilities(
                    occurrence.Kind,
                    lineSelection);
            CliRowSelectionCapabilities missing =
                required & ~capabilities;
            if (missing != CliRowSelectionCapabilities.None)
            {
                return new(
                    CliRowSelectionFailureReason.UnsupportedCapability,
                    occurrence.Kind,
                    occurrence.Position,
                    missing);
            }
        }

        return null;
    }

    internal static CliRowSelectionCapabilities RequiredCapabilities(
        CliRowSelectionOccurrenceKind kind,
        bool lineSelection) =>
        kind switch
        {
            CliRowSelectionOccurrenceKind.Limit
                or CliRowSelectionOccurrenceKind.Head
                or CliRowSelectionOccurrenceKind.Tail =>
                lineSelection
                    ? CliRowSelectionCapabilities.Lines
                    : CliRowSelectionCapabilities.HeadTail,
            CliRowSelectionOccurrenceKind.Rows =>
                CliRowSelectionCapabilities.Window,
            CliRowSelectionOccurrenceKind.Top =>
                CliRowSelectionCapabilities.Top,
            CliRowSelectionOccurrenceKind.OrderBy =>
                CliRowSelectionCapabilities.OrderBy,
            CliRowSelectionOccurrenceKind.Lines =>
                CliRowSelectionCapabilities.Lines,
            CliRowSelectionOccurrenceKind.TailLines =>
                CliRowSelectionCapabilities.Lines,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                null)
        };

    private static CliRowSelectionLowering<TOrderOperand> Build<
        TOrderOperand>(
        IReadOnlyList<ParsedOccurrence<TOrderOperand>> ordered)
        where TOrderOperand : notnull
    {
        ParsedOccurrence<TOrderOperand>? count = null;
        ParsedOccurrence<TOrderOperand>? top = null;
        ParsedOccurrence<TOrderOperand>? order = null;
        bool tail = false;
        bool lines = false;

        for (int index = 0; index < ordered.Count; index++)
        {
            ParsedOccurrence<TOrderOperand> parsed =
                ordered[index];
            switch (parsed.Occurrence.Kind)
            {
                case CliRowSelectionOccurrenceKind.Limit:
                    count = parsed;
                    break;
                case CliRowSelectionOccurrenceKind.Top:
                    top = parsed;
                    break;
                case CliRowSelectionOccurrenceKind.OrderBy:
                    order = parsed;
                    break;
                case CliRowSelectionOccurrenceKind.Tail:
                    tail = true;
                    break;
                case CliRowSelectionOccurrenceKind.Lines:
                    lines = true;
                    break;
                case CliRowSelectionOccurrenceKind.TailLines:
                    tail = true;
                    lines = true;
                    break;
            }
        }

        var operations =
            new List<RowSelectionIntentOperation<TOrderOperand>>();
        for (int index = 0; index < ordered.Count; index++)
        {
            ParsedOccurrence<TOrderOperand> parsed =
                ordered[index];
            switch (parsed.Occurrence.Kind)
            {
                case CliRowSelectionOccurrenceKind.Limit
                    when !lines:
                    operations.Add(
                        tail
                            ? RowSelectionIntentOperation<TOrderOperand>
                                .Tail(parsed.Count)
                            : RowSelectionIntentOperation<TOrderOperand>
                                .Head(parsed.Count));
                    break;

                case CliRowSelectionOccurrenceKind.Rows:
                    operations.Add(
                        RowSelectionIntentOperation<TOrderOperand>
                            .Window(
                                parsed.Start,
                                parsed.End));
                    break;

                case CliRowSelectionOccurrenceKind.Top:
                    operations.Add(
                        order is not null
                            ? RowSelectionIntentOperation<TOrderOperand>
                                .Top(
                                    parsed.Count,
                                    order.Value.Occurrence.OrderOperand)
                            : RowSelectionIntentOperation<TOrderOperand>
                                .Top(parsed.Count));
                    break;
            }
        }

        CliLineSelectionIntent? lineIntent =
            lines
                ? new(
                    count!.Value.Count,
                    tail
                        ? CliLineSelectionDirection.Tail
                        : CliLineSelectionDirection.Head)
                : null;
        bool hasBaselineOrder =
            order is not null && top is null;

        return new(
            RowSelectionIntent<TOrderOperand>.Create(operations),
            lineIntent,
            hasBaselineOrder,
            hasBaselineOrder
                ? order!.Value.Occurrence.OrderOperand
                : default!);
    }

    private readonly record struct ParsedOccurrence<TOrderOperand>(
        CliRowSelectionOccurrence<TOrderOperand> Occurrence,
        int Count,
        int? Start,
        int? End)
        where TOrderOperand : notnull;
}
