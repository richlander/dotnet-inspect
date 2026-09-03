using System.Collections.ObjectModel;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace DotnetInspector.CommandLine;

internal sealed class CliRowSelectionOptionBindings
{
    public CliRowSelectionOptionBindings(
        Option<string[]> limit,
        Option<string[]> rows,
        Option<string[]> top,
        Option<string[]> orderBy,
        Option<bool> head,
        Option<bool> tail,
        Option<bool> lines,
        Option<bool> tailLines)
    {
        ArgumentNullException.ThrowIfNull(limit);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(orderBy);
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(tail);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(tailLines);

        Limit = limit;
        Rows = rows;
        Top = top;
        OrderBy = orderBy;
        Head = head;
        Tail = tail;
        Lines = lines;
        TailLines = tailLines;
    }

    public Option<string[]> Limit { get; }

    public Option<string[]> Rows { get; }

    public Option<string[]> Top { get; }

    public Option<string[]> OrderBy { get; }

    public Option<bool> Head { get; }

    public Option<bool> Tail { get; }

    public Option<bool> Lines { get; }

    public Option<bool> TailLines { get; }
}

internal enum CliRowSelectionArgumentFailureReason
{
    MissingValue,
    AttachedValueOnModifier
}

internal sealed class CliRowSelectionArgumentFailure
{
    public CliRowSelectionArgumentFailure(
        CliRowSelectionArgumentFailureReason reason,
        CliRowSelectionOccurrenceKind occurrenceKind,
        int position)
    {
        Reason = reason;
        OccurrenceKind = occurrenceKind;
        Position = position;
    }

    public CliRowSelectionArgumentFailureReason Reason { get; }

    public CliRowSelectionOccurrenceKind OccurrenceKind { get; }

    public int Position { get; }
}

internal sealed class CliRowSelectionArgumentResult
{
    private readonly ReadOnlyCollection<string> _arguments;
    private readonly ReadOnlyCollection<ParseError> _parseErrors;
    private readonly ReadOnlyCollection<
        CliRowSelectionOccurrence<string>> _occurrences;

    public CliRowSelectionArgumentResult(
        string[] arguments,
        ParseResult parseResult,
        IReadOnlyList<ParseError> parseErrors,
        IReadOnlyList<CliRowSelectionOccurrence<string>> occurrences,
        CliRowSelectionArgumentFailure? argumentFailure,
        CliRowSelectionLoweringResult<string>? loweringResult)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(parseErrors);
        ArgumentNullException.ThrowIfNull(occurrences);

        _arguments =
            Array.AsReadOnly((string[])arguments.Clone());
        _parseErrors =
            Array.AsReadOnly(parseErrors.ToArray());
        _occurrences =
            Array.AsReadOnly(occurrences.ToArray());
        ParseResult = parseResult;
        ArgumentFailure = argumentFailure;
        LoweringResult = loweringResult;
    }

    public IReadOnlyList<string> Arguments => _arguments;

    public ParseResult ParseResult { get; }

    public IReadOnlyList<ParseError> ParseErrors =>
        _parseErrors;

    public IReadOnlyList<
        CliRowSelectionOccurrence<string>> Occurrences =>
        _occurrences;

    public bool HasParseErrors => _parseErrors.Count > 0;

    public CliRowSelectionArgumentFailure? ArgumentFailure { get; }

    public CliRowSelectionLoweringResult<string>? LoweringResult { get; }
}

internal static class CliRowSelectionArgumentAdapter
{
    public static CliRowSelectionArgumentResult LowerExplicit(
        Command command,
        string[] arguments,
        CliRowSelectionOptionBindings bindings,
        CliRowSelectionCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(bindings);

        ParseResult ownershipParse =
            command.Parse(arguments);
        NormalizedArguments normalized =
            NormalizeBareShorthand(
                ownershipParse,
                arguments,
                bindings.Limit);
        ParseResult authoritativeParse =
            ReferenceEquals(normalized.Arguments, arguments)
                ? ownershipParse
                : command.Parse(normalized.Arguments);
        ParseError[] parseErrors =
            authoritativeParse.Errors.ToArray();
        CliRowSelectionArgumentFailure? argumentFailure =
            FindArgumentFailure(
                authoritativeParse,
                normalized,
                bindings);

        if (parseErrors.Length > 0
            || argumentFailure is not null)
        {
            return new(
                normalized.Arguments,
                authoritativeParse,
                parseErrors,
                Array.Empty<
                    CliRowSelectionOccurrence<string>>(),
                argumentFailure,
                null);
        }

        CliRowSelectionOccurrence<string>[] occurrences =
            ExtractOccurrences(
                authoritativeParse,
                normalized,
                bindings);
        return new(
            normalized.Arguments,
            authoritativeParse,
            parseErrors,
            occurrences,
            null,
            CliRowSelectionLowerer.Lower(
                occurrences,
                capabilities));
    }

    private static BoundOption[] BoundOptions(
        CliRowSelectionOptionBindings bindings) =>
        [
            new(
                bindings.Limit,
                CliRowSelectionOccurrenceKind.Limit,
                true),
            new(
                bindings.Rows,
                CliRowSelectionOccurrenceKind.Rows,
                true),
            new(
                bindings.Top,
                CliRowSelectionOccurrenceKind.Top,
                true),
            new(
                bindings.OrderBy,
                CliRowSelectionOccurrenceKind.OrderBy,
                true),
            new(
                bindings.Head,
                CliRowSelectionOccurrenceKind.Head,
                false),
            new(
                bindings.Tail,
                CliRowSelectionOccurrenceKind.Tail,
                false),
            new(
                bindings.Lines,
                CliRowSelectionOccurrenceKind.Lines,
                false),
            new(
                bindings.TailLines,
                CliRowSelectionOccurrenceKind.TailLines,
                false)
        ];

    private static NormalizedArguments NormalizeBareShorthand(
        ParseResult ownershipParse,
        string[] arguments,
        Option limit)
    {
        string? shorthandAlias =
            Aliases(limit)
                .FirstOrDefault(
                    alias =>
                        alias.Equals(
                            "-n",
                            StringComparison.Ordinal));
        if (shorthandAlias is null
            || !IsActiveOption(
                ownershipParse,
                limit))
        {
            return OriginalArguments(arguments);
        }

        List<string>? rewritten = null;
        List<int>? positions = null;

        for (int index = 0; index < arguments.Length; index++)
        {
            string token = arguments[index];
            if (token == "--")
            {
                if (rewritten is not null)
                {
                    for (int suffix = index;
                        suffix < arguments.Length;
                        suffix++)
                    {
                        rewritten.Add(arguments[suffix]);
                        positions!.Add(suffix);
                    }
                }

                break;
            }

            if (!IsBareShorthand(token)
                || IsClaimedByRequiredOption(
                    ownershipParse,
                    arguments,
                    index))
            {
                if (rewritten is not null)
                {
                    rewritten.Add(token);
                    positions!.Add(index);
                }

                continue;
            }

            if (rewritten is null)
            {
                rewritten =
                    new(arguments.Length + 1);
                positions =
                    new(arguments.Length + 1);
                for (int prefix = 0; prefix < index; prefix++)
                {
                    rewritten.Add(arguments[prefix]);
                    positions.Add(prefix);
                }
            }

            rewritten.Add(shorthandAlias);
            positions!.Add(index);
            rewritten.Add(token[1..]);
            positions.Add(index);
        }

        if (rewritten is null)
        {
            return OriginalArguments(arguments);
        }

        return new(
            [.. rewritten],
            [.. positions!]);
    }

    private static NormalizedArguments OriginalArguments(
        string[] arguments) =>
        new(
            arguments,
            Enumerable.Range(
                0,
                arguments.Length)
                .ToArray());

    private static bool IsBareShorthand(string token)
    {
        if (token.Length < 2
            || token[0] != '-')
        {
            return false;
        }

        for (int index = 1; index < token.Length; index++)
        {
            if (!char.IsAsciiDigit(token[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsClaimedByRequiredOption(
        ParseResult parseResult,
        IReadOnlyList<string> arguments,
        int index)
    {
        string value = arguments[index];
        IReadOnlyList<OptionResult> optionResults =
            GetOptionResults(parseResult);
        Token? candidate =
            FindParseTokenForRawArgument(
                parseResult,
                arguments,
                index,
                value);
        return candidate is not null
            && optionResults.Any(
                option =>
                    option.Option.Arity.MinimumNumberOfValues > 0
                    && option.Tokens.Any(
                        token =>
                            ReferenceEquals(
                                token,
                                candidate)));
    }

    private static IReadOnlyList<OptionResult> GetOptionResults(
        ParseResult parseResult)
    {
        var results =
            new List<OptionResult>();
        for (SymbolResult? scope = parseResult.CommandResult;
            scope is not null;
            scope = scope.Parent)
        {
            if (scope is not CommandResult command)
            {
                continue;
            }

            results.AddRange(
                command.Children.OfType<OptionResult>());
        }

        return results;
    }

    private static bool IsOwnedOptionToken(
        ParseResult parseResult,
        IReadOnlyList<string> arguments,
        int index,
        string alias) =>
        FindParseTokenForRawArgument(
            parseResult,
            arguments,
            index,
            alias)
            is { Type: TokenType.Option };

    private static Token? FindParseTokenForRawArgument(
        ParseResult parseResult,
        IReadOnlyList<string> arguments,
        int index,
        string parsedValue)
    {
        IReadOnlyList<OptionResult> optionResults =
            GetOptionResults(parseResult);
        int occurrence = 0;
        for (int argumentIndex = 0;
            argumentIndex <= index;
            argumentIndex++)
        {
            string argument =
                arguments[argumentIndex];
            if (ProducesOptionIdentifier(
                    argument,
                    parsedValue))
            {
                occurrence++;
                continue;
            }

            if (optionResults.Any(
                    option =>
                        option.Tokens.Any(
                            optionToken =>
                                optionToken.Value.Equals(
                                    parsedValue,
                                    StringComparison.Ordinal))
                        && IsInlineOptionValue(
                            argument,
                            option.Option,
                            parsedValue)))
            {
                occurrence++;
            }
        }

        return occurrence == 0
            ? null
            : parseResult.Tokens
                .Where(
                    token =>
                        token.Value.Equals(
                            parsedValue,
                            StringComparison.Ordinal))
                .Skip(occurrence - 1)
                .FirstOrDefault();
    }

    private static bool ProducesOptionIdentifier(
        string argument,
        string alias)
    {
        if (argument.Equals(
                alias,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (argument.Length <= alias.Length
            || !argument.StartsWith(
                alias,
                StringComparison.Ordinal))
        {
            return false;
        }

        return argument[alias.Length] is '=' or ':'
            || IsShortAlias(alias);
    }

    private static bool IsInlineOptionValue(
        string argument,
        Option option,
        string value)
    {
        foreach (string alias in Aliases(option))
        {
            if (argument.Equals(
                    $"{alias}={value}",
                    StringComparison.Ordinal)
                || argument.Equals(
                    $"{alias}:{value}",
                    StringComparison.Ordinal)
                || IsShortAlias(alias)
                    && argument.Equals(
                        $"{alias}{value}",
                        StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static CliRowSelectionArgumentFailure?
        FindArgumentFailure(
            ParseResult parseResult,
            NormalizedArguments normalized,
            CliRowSelectionOptionBindings bindings)
    {
        BoundOption[] boundOptions =
            BoundOptions(bindings);
        for (int index = 0;
            index < normalized.Arguments.Length;
            index++)
        {
            string token =
                normalized.Arguments[index];
            if (token == "--")
            {
                break;
            }

            foreach (BoundOption bound in boundOptions)
            {
                if (bound.HasValue
                    && IsMissingValue(
                        token,
                        index,
                        parseResult,
                        normalized.Arguments,
                        bound.Option))
                {
                    return MissingValueFailure(
                        bound.Kind,
                        normalized.Positions[index]);
                }

                if (!bound.HasValue
                    && HasDelimitedAttachedValue(
                        token,
                        bound.Option,
                        out string? alias)
                    && IsOwnedOptionToken(
                        parseResult,
                        normalized.Arguments,
                        index,
                        alias!))
                {
                    return AttachedModifierFailure(
                        bound.Kind,
                        normalized.Positions[index]);
                }
            }
        }

        return null;
    }

    private static bool IsMissingValue(
        string token,
        int index,
        ParseResult parseResult,
        IReadOnlyList<string> arguments,
        Option option)
    {
        if (!TryGetExactOptionAlias(
                token,
                option,
                out string? alias)
            || !IsOwnedOptionToken(
                parseResult,
                arguments,
                index,
                alias!))
        {
            return false;
        }

        return index + 1 >= arguments.Count
            || arguments[index + 1] == "--"
            || IsKnownOptionToken(
                arguments[index + 1],
                parseResult);
    }

    private static bool IsKnownOptionToken(
        string token,
        ParseResult parseResult)
    {
        for (CommandResult? command = parseResult.CommandResult;
            command is not null;
            command = command.Parent as CommandResult)
        {
            if (command.Command.Options.Any(
                    option =>
                        IsOptionToken(
                            token,
                            option)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsActiveOption(
        ParseResult parseResult,
        Option option)
    {
        for (CommandResult? command = parseResult.CommandResult;
            command is not null;
            command = command.Parent as CommandResult)
        {
            if (command.Command.Options.Any(
                    candidate =>
                        ReferenceEquals(
                            candidate,
                            option)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOptionToken(
        string token,
        Option option)
    {
        foreach (string alias in Aliases(option))
        {
            if (token.Equals(
                    alias,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (token.Length <= alias.Length
                || !token.StartsWith(
                    alias,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (token[alias.Length] is '=' or ':'
                || IsShortAlias(alias))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDelimitedAttachedValue(
        string token,
        Option option,
        out string? matchedAlias)
    {
        foreach (string alias in Aliases(option))
        {
            if (token.Length > alias.Length
                && token.StartsWith(
                    alias,
                    StringComparison.Ordinal)
                && token[alias.Length] is '=' or ':')
            {
                matchedAlias = alias;
                return true;
            }
        }

        matchedAlias = null;
        return false;
    }

    private static CliRowSelectionArgumentFailure
        MissingValueFailure(
            CliRowSelectionOccurrenceKind kind,
            int position) =>
        new(
            CliRowSelectionArgumentFailureReason.MissingValue,
            kind,
            position);

    private static CliRowSelectionArgumentFailure
        AttachedModifierFailure(
            CliRowSelectionOccurrenceKind kind,
            int position) =>
        new(
            CliRowSelectionArgumentFailureReason
                .AttachedValueOnModifier,
            kind,
            position);

    private static CliRowSelectionOccurrence<string>[]
        ExtractOccurrences(
            ParseResult parseResult,
            NormalizedArguments normalized,
            CliRowSelectionOptionBindings bindings)
    {
        var occurrences =
            new List<CliRowSelectionOccurrence<string>>();
        BoundOption[] boundOptions =
            BoundOptions(bindings);
        for (int index = 0;
            index < normalized.Arguments.Length;
            index++)
        {
            string token =
                normalized.Arguments[index];
            if (token == "--")
            {
                break;
            }

            int position =
                normalized.Positions[index];
            foreach (BoundOption bound in boundOptions)
            {
                int valueIndex = index;
                if (bound.HasValue
                    && TryReadValue(
                        parseResult,
                        normalized.Arguments,
                        ref valueIndex,
                        token,
                        bound.Option,
                        out string? value))
                {
                    occurrences.Add(
                        ValueOccurrence(
                            bound.Kind,
                            position,
                            value));
                    index = valueIndex;
                    break;
                }

                if (!bound.HasValue
                    && TryGetExactOptionAlias(
                        token,
                        bound.Option,
                        out string? alias)
                    && IsOwnedOptionToken(
                        parseResult,
                        normalized.Arguments,
                        index,
                        alias!))
                {
                    occurrences.Add(
                        ModifierOccurrence(
                            bound.Kind,
                            position));
                    break;
                }
            }
        }

        return [.. occurrences];
    }

    private static CliRowSelectionOccurrence<string>
        ValueOccurrence(
            CliRowSelectionOccurrenceKind kind,
            int position,
            string value) =>
        kind switch
        {
            CliRowSelectionOccurrenceKind.Limit =>
                CliRowSelectionOccurrence<string>
                    .Limit(position, value),
            CliRowSelectionOccurrenceKind.Rows =>
                CliRowSelectionOccurrence<string>
                    .Rows(position, value),
            CliRowSelectionOccurrenceKind.Top =>
                CliRowSelectionOccurrence<string>
                    .Top(position, value),
            CliRowSelectionOccurrenceKind.OrderBy =>
                CliRowSelectionOccurrence<string>
                    .OrderBy(position, value),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                null)
        };

    private static CliRowSelectionOccurrence<string>
        ModifierOccurrence(
            CliRowSelectionOccurrenceKind kind,
            int position) =>
        kind switch
        {
            CliRowSelectionOccurrenceKind.Head =>
                CliRowSelectionOccurrence<string>
                    .Head(position),
            CliRowSelectionOccurrenceKind.Tail =>
                CliRowSelectionOccurrence<string>
                    .Tail(position),
            CliRowSelectionOccurrenceKind.Lines =>
                CliRowSelectionOccurrence<string>
                    .Lines(position),
            CliRowSelectionOccurrenceKind.TailLines =>
                CliRowSelectionOccurrence<string>
                    .TailLines(position),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                null)
        };

    private static bool TryReadValue(
        ParseResult parseResult,
        IReadOnlyList<string> arguments,
        ref int index,
        string token,
        Option option,
        out string value)
    {
        if (!TryClassifyValueToken(
                token,
                option,
                out bool consumesNext,
                out string? attachedValue,
                out string? alias)
            || !IsOwnedOptionToken(
                parseResult,
                arguments,
                index,
                alias!))
        {
            value = null!;
            return false;
        }

        if (consumesNext)
        {
            value = arguments[++index];
            return true;
        }

        value = attachedValue!;
        return true;
    }

    private static bool TryClassifyValueToken(
        string token,
        Option option,
        out bool consumesNext,
        out string? matchedAlias) =>
        TryClassifyValueToken(
            token,
            option,
            out consumesNext,
            out _,
            out matchedAlias);

    private static bool TryClassifyValueToken(
        string token,
        Option option,
        out bool consumesNext,
        out string? attachedValue,
        out string? matchedAlias)
    {
        foreach (string alias in Aliases(option))
        {
            if (token.Equals(
                    alias,
                    StringComparison.Ordinal))
            {
                consumesNext = true;
                attachedValue = null;
                matchedAlias = alias;
                return true;
            }

            if (token.Length > alias.Length
                && token.StartsWith(
                    alias,
                    StringComparison.Ordinal))
            {
                char separator =
                    token[alias.Length];
                if (separator is '=' or ':')
                {
                    consumesNext = false;
                    attachedValue =
                        token[(alias.Length + 1)..];
                    matchedAlias = alias;
                    return true;
                }

                if (IsShortAlias(alias))
                {
                    consumesNext = false;
                    attachedValue =
                        token[alias.Length..];
                    matchedAlias = alias;
                    return true;
                }
            }
        }

        consumesNext = false;
        attachedValue = null;
        matchedAlias = null;
        return false;
    }

    private static bool TryGetExactOptionAlias(
        string token,
        Option option,
        out string? matchedAlias)
    {
        foreach (string alias in Aliases(option))
        {
            if (token.Equals(
                    alias,
                    StringComparison.Ordinal))
            {
                matchedAlias = alias;
                return true;
            }
        }

        matchedAlias = null;
        return false;
    }

    private static IEnumerable<string> Aliases(
        Option option)
    {
        yield return option.Name;
        foreach (string alias in option.Aliases)
        {
            if (!alias.Equals(
                    option.Name,
                    StringComparison.Ordinal))
            {
                yield return alias;
            }
        }
    }

    private static bool IsShortAlias(string alias) =>
        alias is ['-', not '-'];

    private readonly record struct NormalizedArguments(
        string[] Arguments,
        int[] Positions);

    private readonly record struct BoundOption(
        Option Option,
        CliRowSelectionOccurrenceKind Kind,
        bool HasValue);
}
