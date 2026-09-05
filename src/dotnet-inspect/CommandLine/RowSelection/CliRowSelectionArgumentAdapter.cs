using System.Collections.ObjectModel;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace DotnetInspector.CommandLine;

internal sealed class CliRowSelectionOptionBindings
{
    public CliRowSelectionOptionBindings(
        Option limit,
        Option rows,
        Option? top,
        Option? orderBy,
        Option<bool> head,
        Option<bool> tail,
        Option<bool> lines,
        Option<bool> tailLines)
    {
        ArgumentNullException.ThrowIfNull(limit);
        ArgumentNullException.ThrowIfNull(rows);
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

    public Option Limit { get; }

    public Option Rows { get; }

    public Option? Top { get; }

    public Option? OrderBy { get; }

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
    private readonly ReadOnlyCollection<CliRowSelectionArgumentFailure> _argumentFailures;
    private readonly ReadOnlyCollection<int> _requiredValuePositions;
    private readonly ReadOnlyCollection<
        CliRowSelectionOccurrence<string>> _occurrences;

    public CliRowSelectionArgumentResult(
        string[] arguments,
        ParseResult parseResult,
        IReadOnlyList<ParseError> parseErrors,
        IReadOnlyList<int> requiredValuePositions,
        IReadOnlyList<CliRowSelectionOccurrence<string>> occurrences,
        IReadOnlyList<CliRowSelectionArgumentFailure> argumentFailures,
        CliRowSelectionLoweringResult<string>? loweringResult,
        int? selectedCommandPosition)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(parseErrors);
        ArgumentNullException.ThrowIfNull(argumentFailures);
        ArgumentNullException.ThrowIfNull(requiredValuePositions);
        ArgumentNullException.ThrowIfNull(occurrences);

        _arguments =
            Array.AsReadOnly((string[])arguments.Clone());
        _parseErrors =
            Array.AsReadOnly(parseErrors.ToArray());
        _argumentFailures =
            Array.AsReadOnly(argumentFailures.ToArray());
        _requiredValuePositions =
            Array.AsReadOnly(requiredValuePositions.ToArray());
        _occurrences =
            Array.AsReadOnly(occurrences.ToArray());
        ParseResult = parseResult;
        LoweringResult = loweringResult;
        SelectedCommandPosition = selectedCommandPosition;
    }

    public IReadOnlyList<string> Arguments => _arguments;

    public ParseResult ParseResult { get; }

    public IReadOnlyList<ParseError> ParseErrors =>
        _parseErrors;

    public IReadOnlyList<
        CliRowSelectionOccurrence<string>> Occurrences =>
        _occurrences;

    public bool HasParseErrors => _parseErrors.Count > 0;

    public IReadOnlyList<int> RequiredValuePositions =>
        _requiredValuePositions;

    public IReadOnlyList<CliRowSelectionArgumentFailure> ArgumentFailures =>
        _argumentFailures;

    public CliRowSelectionArgumentFailure? ArgumentFailure =>
        _argumentFailures.FirstOrDefault();

    public CliRowSelectionLoweringResult<string>? LoweringResult { get; }

    public int? SelectedCommandPosition { get; }
}

internal static class CliRowSelectionArgumentAdapter
{
    private static readonly (string Alias, CliRowSelectionOccurrenceKind Kind)[]
        CanonicalOptions =
        [
            ("-n", CliRowSelectionOccurrenceKind.Limit),
            ("--rows", CliRowSelectionOccurrenceKind.Rows),
            ("--top", CliRowSelectionOccurrenceKind.Top),
            ("--order-by", CliRowSelectionOccurrenceKind.OrderBy),
            ("--head", CliRowSelectionOccurrenceKind.Head),
            ("--tail", CliRowSelectionOccurrenceKind.Tail),
            ("--lines", CliRowSelectionOccurrenceKind.Lines),
            ("--tail-lines", CliRowSelectionOccurrenceKind.TailLines)
        ];

    private static readonly ParserConfiguration
        ExplicitParserConfiguration =
            new()
            {
                EnablePosixBundling = false,
                ResponseFileTokenReplacer = null
            };

    public static CliRowSelectionArgumentResult LowerExplicit(
        Command command,
        string[] arguments,
        CliRowSelectionOptionBindings bindings,
        CliRowSelectionCapabilities capabilities)
        => AnalyzeExplicit(
            command,
            arguments,
            bindings,
            capabilities,
            preserveRowEvidenceAcrossParseErrors: false);

    internal static CliRowSelectionArgumentResult InspectExplicit(
        Command command,
        string[] arguments,
        CliRowSelectionOptionBindings bindings)
        => AnalyzeExplicit(
            command,
            arguments,
            bindings,
            CliRowSelectionCapabilities.All,
            preserveRowEvidenceAcrossParseErrors: true);

    private static CliRowSelectionArgumentResult AnalyzeExplicit(
        Command command,
        string[] arguments,
        CliRowSelectionOptionBindings bindings,
        CliRowSelectionCapabilities capabilities,
        bool preserveRowEvidenceAcrossParseErrors)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(bindings);

        ParseResult ownershipParse =
            ParseExplicit(
                command,
                arguments,
                bindings);
        ParsedArgument[] ownershipArguments =
            MapArguments(
                ownershipParse,
                arguments);
        int[] requiredValuePositions =
            preserveRowEvidenceAcrossParseErrors
                ? FindRequiredValuePositions(
                    ownershipParse,
                    ownershipArguments)
                : [];
        NormalizedArguments normalized =
            NormalizeShortLimitForms(
                ownershipParse,
                ownershipArguments,
                arguments,
                bindings.Limit);
        ParseResult authoritativeParse =
            ReferenceEquals(normalized.Arguments, arguments)
                ? ownershipParse
                : ParseExplicit(
                    command,
                    normalized.Arguments,
                    bindings);
        ParsedArgument[] authoritativeArguments =
            ReferenceEquals(normalized.Arguments, arguments)
                ? ownershipArguments
                : MapArguments(
                    authoritativeParse,
                    normalized.Arguments);
        int? selectedCommandPosition =
            FindTokenPosition(
                authoritativeArguments,
                normalized,
                authoritativeParse.CommandResult
                    .IdentifierToken);
        ParseError[] parseErrors =
            authoritativeParse.Errors.ToArray();
        CliRowSelectionArgumentFailure[] argumentFailures =
            FindArgumentFailures(
                authoritativeParse,
                authoritativeArguments,
                normalized,
                bindings);

        if (!preserveRowEvidenceAcrossParseErrors
            && (parseErrors.Length > 0
                || argumentFailures.Length > 0))
        {
            return new(
                normalized.Arguments,
                authoritativeParse,
                parseErrors,
                requiredValuePositions,
                Array.Empty<
                    CliRowSelectionOccurrence<string>>(),
                argumentFailures,
                null,
                selectedCommandPosition);
        }

        CliRowSelectionOccurrence<string>[] occurrences =
            ExtractOccurrences(
                authoritativeArguments,
                normalized,
                bindings,
                argumentFailures
                    .Select(failure => failure.Position)
                    .ToHashSet());
        return new(
            normalized.Arguments,
            authoritativeParse,
            parseErrors,
            requiredValuePositions,
            occurrences,
            argumentFailures,
            CliRowSelectionLowerer.Lower(
                occurrences,
                capabilities),
            selectedCommandPosition);
    }

    internal static bool IsBareLimitShorthand(string token) =>
        IsBareShorthand(token);

    internal static bool HasShortLimitAlias(
        CliRowSelectionOptionBindings bindings) =>
        Aliases(bindings.Limit).Any(
            alias =>
                alias.Equals(
                    "-n",
                    StringComparison.Ordinal));

    internal static bool TryClassifyExplicitRowToken(
        string token,
        CliRowSelectionOptionBindings bindings,
        out CliRowSelectionOccurrenceKind kind)
    {
        if (TryClassifyBoundRowToken(
                token,
                bindings,
                out kind))
        {
            return true;
        }

        foreach (var canonical in CanonicalOptions)
        {
            if (MatchesCanonicalOption(token, canonical.Alias)
                || canonical.Kind == CliRowSelectionOccurrenceKind.Limit
                && IsCompactShortLimitToken(token, canonical.Alias))
            {
                kind = canonical.Kind;
                return true;
            }
        }

        kind = default;
        return false;
    }

    internal static bool TryClassifyBoundRowToken(
        string token,
        CliRowSelectionOptionBindings bindings,
        out CliRowSelectionOccurrenceKind kind)
    {
        foreach (BoundOption bound in BoundOptions(bindings))
        {
            if (IsOptionToken(
                    token,
                    bound.Option,
                    bindings.Limit))
            {
                kind = bound.Kind;
                return true;
            }
        }

        kind = default;
        return false;
    }

    private static bool MatchesCanonicalOption(
        string token,
        string alias) =>
        token.Equals(
            alias,
            StringComparison.Ordinal)
        || token.Length > alias.Length
        && token.StartsWith(
            alias,
            StringComparison.Ordinal)
        && token[alias.Length] is '=' or ':';

    internal static bool IsDeclared(
        ParseResult parseResult,
        CliRowSelectionOptionBindings bindings,
        CliRowSelectionOccurrenceKind kind,
        bool recursiveAncestorsOnly = false)
    {
        Option? option = null;
        foreach (BoundOption bound in BoundOptions(bindings))
        {
            if (bound.Kind == kind)
            {
                option = bound.Option;
                break;
            }
        }

        if (option is null)
        {
            return false;
        }

        for (CommandResult? command = parseResult.CommandResult;
            command is not null;
            command = command.Parent as CommandResult)
        {
            if ((!recursiveAncestorsOnly
                    || ReferenceEquals(command, parseResult.CommandResult)
                    || option.Recursive)
                && command.Command.Options.Any(
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

    private static ParseResult ParseExplicit(
        Command command,
        string[] arguments,
        CliRowSelectionOptionBindings bindings)
    {
        (Option<bool> Option, ArgumentArity Arity)[] modifiers =
        [
            (bindings.Head, bindings.Head.Arity),
            (bindings.Tail, bindings.Tail.Arity),
            (bindings.Lines, bindings.Lines.Arity),
            (bindings.TailLines, bindings.TailLines.Arity)
        ];

        // Shared legacy flags may accept values; the adopted parse uses presence arity.
        try
        {
            foreach (var modifier in modifiers)
                modifier.Option.Arity = ArgumentArity.Zero;

            return command.Parse(arguments, ExplicitParserConfiguration);
        }
        finally
        {
            foreach (var modifier in modifiers)
                modifier.Option.Arity = modifier.Arity;
        }
    }

    private static BoundOption[] BoundOptions(
        CliRowSelectionOptionBindings bindings)
    {
        var options = new List<BoundOption>
        {
            new(
                bindings.Limit,
                CliRowSelectionOccurrenceKind.Limit,
                true),
            new(
                bindings.Rows,
                CliRowSelectionOccurrenceKind.Rows,
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
        };
        if (bindings.Top is not null)
        {
            options.Add(
                new(
                    bindings.Top,
                    CliRowSelectionOccurrenceKind.Top,
                    true));
        }

        if (bindings.OrderBy is not null)
        {
            options.Add(
                new(
                    bindings.OrderBy,
                    CliRowSelectionOccurrenceKind.OrderBy,
                    true));
        }

        return [.. options];
    }

    private static NormalizedArguments NormalizeShortLimitForms(
        ParseResult ownershipParse,
        IReadOnlyList<ParsedArgument> ownershipArguments,
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
        if (shorthandAlias is null)
        {
            return OriginalArguments(arguments);
        }

        int? firstOwnedArgumentIndex =
            FindOptionScopeStartArgumentIndex(
                ownershipParse,
                ownershipArguments,
                limit);
        if (firstOwnedArgumentIndex is null)
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

            if (index < firstOwnedArgumentIndex.Value
                || !TryGetNormalizedLimitValue(
                    token,
                    shorthandAlias,
                    out string? value)
                || HasOptionToken(
                    ownershipArguments[index])
                || IsClaimedByRequiredOption(
                    ownershipArguments[index],
                    ownershipParse))
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
            rewritten.Add(value!);
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
        return token.Length >= 2
            && token[0] == '-'
            && IsAsciiDigits(
                token,
                1);
    }

    private static bool TryGetNormalizedLimitValue(
        string token,
        string shorthandAlias,
        out string? value)
    {
        if (IsBareShorthand(token))
        {
            value = token[1..];
            return true;
        }

        if (IsCompactShortLimitToken(
                token,
                shorthandAlias))
        {
            value = token[shorthandAlias.Length..];
            return true;
        }

        value = null;
        return false;
    }

    private static bool IsCompactShortLimitToken(
        string token,
        string shorthandAlias) =>
        token.StartsWith(
            shorthandAlias,
            StringComparison.Ordinal)
        && IsAsciiDigits(
            token,
            shorthandAlias.Length);

    private static bool IsAsciiDigits(
        string token,
        int startIndex)
    {
        if (startIndex >= token.Length)
        {
            return false;
        }

        for (int index = startIndex;
            index < token.Length;
            index++)
        {
            if (!char.IsAsciiDigit(token[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasOptionToken(
        ParsedArgument argument) =>
        argument.Tokens.Any(
            token =>
                token.Type == TokenType.Option);

    private static bool IsClaimedByRequiredOption(
        ParsedArgument argument,
        ParseResult parseResult)
    {
        IReadOnlyList<OptionResult> optionResults =
            GetOptionResults(parseResult);
        return optionResults.Any(
                option =>
                    option.Option.Arity.MinimumNumberOfValues > 0
                    && argument.Tokens.Any(
                        candidate =>
                            option.Tokens.Any(
                                token =>
                                    ReferenceEquals(
                                        token,
                                        candidate))));
    }

    private static int[] FindRequiredValuePositions(
        ParseResult parseResult,
        IReadOnlyList<ParsedArgument> parsedArguments)
    {
        IReadOnlyList<OptionResult> optionResults =
            GetOptionResults(parseResult);
        var positions = new List<int>();
        for (int index = 0;
            index < parsedArguments.Count;
            index++)
        {
            ParsedArgument argument =
                parsedArguments[index];
            if (!HasOptionToken(argument)
                && optionResults.Any(
                    option =>
                        option.Option.Arity.MinimumNumberOfValues > 0
                        && argument.Tokens.Any(
                            candidate =>
                                candidate.Type == TokenType.Argument
                                && option.Tokens.Any(
                                    token =>
                                        ReferenceEquals(
                                            token,
                                            candidate)))))
            {
                positions.Add(index);
            }
        }

        return [.. positions];
    }

    private static int? FindTokenPosition(
        IReadOnlyList<ParsedArgument> parsedArguments,
        NormalizedArguments normalized,
        Token? target)
    {
        if (target is null)
        {
            return null;
        }

        for (int index = 0;
            index < parsedArguments.Count;
            index++)
        {
            if (parsedArguments[index].Tokens.Any(
                    token =>
                        ReferenceEquals(
                            token,
                            target)))
            {
                return normalized.Positions[index];
            }
        }

        return null;
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
        ParsedArgument argument,
        string alias) =>
        argument.Tokens.Any(
            token =>
                token.Type == TokenType.Option
                && token.Value.Equals(
                    alias,
                    StringComparison.Ordinal));

    private static ParsedArgument[] MapArguments(
        ParseResult parseResult,
        IReadOnlyList<string> arguments)
    {
        IReadOnlyList<Token> tokens =
            parseResult.Tokens;
        var result =
            new ParsedArgument[arguments.Count];
        int tokenIndex = 0;

        for (int argumentIndex = 0;
            argumentIndex < arguments.Count;
            argumentIndex++)
        {
            int start = tokenIndex;
            if (tokenIndex < tokens.Count)
            {
                string argument =
                    arguments[argumentIndex];
                Token token =
                    tokens[tokenIndex];
                if (argument.Equals(
                        token.Value,
                        StringComparison.Ordinal))
                {
                    tokenIndex++;
                }
                else if (!TryConsumeAttachedArgument(
                    argument,
                    tokens,
                    ref tokenIndex))
                {
                    tokenIndex++;
                }
            }

            result[argumentIndex] =
                new(
                    tokens
                        .Skip(start)
                        .Take(tokenIndex - start)
                        .ToArray());
        }

        return result;
    }

    private static bool TryConsumeAttachedArgument(
        string argument,
        IReadOnlyList<Token> tokens,
        ref int tokenIndex)
    {
        Token option =
            tokens[tokenIndex];
        if (option.Type != TokenType.Option)
        {
            return false;
        }

        if (TryGetDelimitedValue(
                argument,
                option.Value,
                out string? attachedValue))
        {
            tokenIndex++;
            ConsumeMatchingArgument(
                tokens,
                ref tokenIndex,
                attachedValue!);
            return true;
        }

        if (IsShortAlias(option.Value)
            && argument.Length > option.Value.Length
            && argument.StartsWith(option.Value, StringComparison.Ordinal)
            && tokenIndex + 1 < tokens.Count
            && tokens[tokenIndex + 1].Type == TokenType.Argument
            && tokens[tokenIndex + 1].Value.Equals(
                argument[option.Value.Length..],
                StringComparison.Ordinal))
        {
            tokenIndex += 2;
            return true;
        }

        return false;
    }

    private static bool TryGetDelimitedValue(
        string argument,
        string alias,
        out string? value)
    {
        if (argument.Length > alias.Length
            && argument.StartsWith(
                alias,
                StringComparison.Ordinal)
            && argument[alias.Length] is '=' or ':')
        {
            value =
                argument[(alias.Length + 1)..];
            return true;
        }

        value = null;
        return false;
    }

    private static void ConsumeMatchingArgument(
        IReadOnlyList<Token> tokens,
        ref int tokenIndex,
        string value)
    {
        if (tokenIndex < tokens.Count
            && tokens[tokenIndex].Type
                == TokenType.Argument
            && tokens[tokenIndex].Value.Equals(
                value,
                StringComparison.Ordinal))
        {
            tokenIndex++;
        }
    }

    private static CliRowSelectionArgumentFailure[]
        FindArgumentFailures(
            ParseResult parseResult,
            IReadOnlyList<ParsedArgument> parsedArguments,
            NormalizedArguments normalized,
            CliRowSelectionOptionBindings bindings)
    {
        var failures = new List<CliRowSelectionArgumentFailure>();
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
                        parsedArguments,
                        normalized.Arguments,
                        bound.Option,
                        bindings.Limit))
                {
                    failures.Add(
                        MissingValueFailure(
                            bound.Kind,
                            normalized.Positions[index]));
                    break;
                }

                if (!bound.HasValue
                    && HasDelimitedAttachedValue(
                        token,
                        bound.Option,
                        out string? alias)
                    && IsOwnedOptionToken(
                        parsedArguments[index],
                        alias!))
                {
                    failures.Add(
                        AttachedModifierFailure(
                            bound.Kind,
                            normalized.Positions[index]));
                    break;
                }
            }
        }

        return [.. failures];
    }

    private static bool IsMissingValue(
        string token,
        int index,
        ParseResult parseResult,
        IReadOnlyList<ParsedArgument> parsedArguments,
        IReadOnlyList<string> arguments,
        Option option,
        Option limit)
    {
        if (!TryGetExactOptionAlias(
                token,
                option,
                out string? alias)
            || !IsOwnedOptionToken(
                parsedArguments[index],
                alias!))
        {
            return false;
        }

        return index + 1 >= arguments.Count
            || arguments[index + 1] == "--"
            || IsKnownOptionToken(
                arguments[index + 1],
                parseResult,
                limit);
    }

    private static bool IsKnownOptionToken(
        string token,
        ParseResult parseResult,
        Option limit)
    {
        for (CommandResult? command = parseResult.CommandResult;
            command is not null;
            command = command.Parent as CommandResult)
        {
            if (command.Command.Options.Any(
                    option =>
                        IsOptionToken(
                            token,
                            option,
                            limit)))
            {
                return true;
            }
        }

        return false;
    }

    private static int? FindOptionScopeStartArgumentIndex(
        ParseResult parseResult,
        IReadOnlyList<ParsedArgument> parsedArguments,
        Option option)
    {
        int? firstOwnedArgumentIndex = null;
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
                if (command.Parent is null)
                {
                    return 0;
                }

                Token? identifier =
                    command.IdentifierToken;
                bool mappedCommand = false;
                for (int index = 0;
                    index < parsedArguments.Count;
                    index++)
                {
                    if (parsedArguments[index].Tokens.Any(
                            token =>
                                ReferenceEquals(
                                    token,
                                    identifier)))
                    {
                        int ownedArgumentIndex =
                            index + 1;
                        firstOwnedArgumentIndex =
                            firstOwnedArgumentIndex is null
                                ? ownedArgumentIndex
                                : Math.Min(
                                    firstOwnedArgumentIndex.Value,
                                    ownedArgumentIndex);
                        mappedCommand = true;
                        break;
                    }
                }

                if (!mappedCommand)
                {
                    throw new InvalidOperationException(
                        "The active command token was not mapped to raw argv.");
                }
            }
        }

        return firstOwnedArgumentIndex;
    }

    private static bool IsOptionToken(
        string token,
        Option option,
        Option limit)
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
                || (ReferenceEquals(
                        option,
                        limit)
                    && alias.Equals(
                        "-n",
                        StringComparison.Ordinal)
                    && IsCompactShortLimitToken(
                        token,
                        alias)))
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
            IReadOnlyList<ParsedArgument> parsedArguments,
            NormalizedArguments normalized,
            CliRowSelectionOptionBindings bindings,
            IReadOnlySet<int> invalidPositions)
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
            if (invalidPositions.Contains(position))
            {
                continue;
            }

            foreach (BoundOption bound in boundOptions)
            {
                int valueIndex = index;
                if (bound.HasValue
                    && TryReadValue(
                        parsedArguments[index],
                        normalized.Arguments,
                        ref valueIndex,
                        token,
                        bound.Option,
                        bindings.Limit,
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
                        parsedArguments[index],
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
        ParsedArgument parsedArgument,
        IReadOnlyList<string> arguments,
        ref int index,
        string token,
        Option option,
        Option limit,
        out string value)
    {
        if (!TryClassifyValueToken(
                token,
                option,
                limit,
                out bool consumesNext,
                out string? attachedValue,
                out string? alias)
            || !IsOwnedOptionToken(
                parsedArgument,
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
        Option limit,
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

                if (ReferenceEquals(
                        option,
                        limit)
                    && alias.Equals(
                        "-n",
                        StringComparison.Ordinal)
                    && IsCompactShortLimitToken(
                        token,
                        alias))
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

    private readonly record struct NormalizedArguments(
        string[] Arguments,
        int[] Positions);

    private readonly record struct ParsedArgument(
        Token[] Tokens);

    private readonly record struct BoundOption(
        Option Option,
        CliRowSelectionOccurrenceKind Kind,
        bool HasValue);
}
