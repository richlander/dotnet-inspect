using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;
using static DotnetInspect.Cli.CommandLine.CliArgumentOwnership;

namespace DotnetInspect.Cli.CommandLine;

internal sealed record CliOptionValueFailure(
    string Error,
    int Position);

internal static class CliOptionValueValidation
{
    private static readonly ConditionalWeakTable<Argument, Func<ParseResult, int>> Capacities = new();
    private static readonly ConditionalWeakTable<Command, IReadOnlyList<Option>>
        PresenceOptionsByCommand = new();
    private static readonly ConditionalWeakTable<Option, HashSet<string>>
        AcceptedValuesByOption = new();
    private static readonly ConditionalWeakTable<Option, object>
        RequiredValuePerOccurrenceOptions = new();

    public static void RegisterCapacity(Argument argument, Func<ParseResult, int> capacity) =>
        Capacities.Add(argument, capacity);

    public static void RegisterPresenceOptions(
        Command command,
        params Option[] options) =>
        PresenceOptionsByCommand.Add(command, options);

    public static void AcceptOnlyFromAmong<T>(
        Option<T> option,
        StringComparer comparer,
        params string[] values)
    {
        option.AcceptOnlyFromAmong(comparer, values);
        AcceptedValuesByOption.Add(
            option,
            new HashSet<string>(values, comparer));
    }

    public static void RequireValueForEveryOccurrence(Option option) =>
        RequiredValuePerOccurrenceOptions.Add(option, new object());

    public static int? FindFirstRejectedValuePosition(
        OptionResult optionResult,
        IReadOnlyList<ParsedArgument> mapped,
        IReadOnlyList<int>? argumentPositions)
    {
        if (!AcceptedValuesByOption.TryGetValue(
                optionResult.Option,
                out HashSet<string>? acceptedValues))
        {
            return null;
        }

        foreach (Token value in optionResult.Tokens.Where(
            static token => token.Type == TokenType.Argument))
        {
            if (acceptedValues.Contains(value.Value))
                continue;

            int index = Enumerable.Range(0, mapped.Count)
                .FirstOrDefault(
                    index => mapped[index].Tokens.Any(
                        token => ReferenceEquals(
                            token,
                            value)),
                    -1);
            if (index >= 0)
                return argumentPositions?[index] ?? index;
        }

        return null;
    }

    public static string DoesNotAcceptValue(string optionName) =>
        $"{optionName} does not accept a value.";

    public static string? FindError(
        ParseResult parseResult,
        IReadOnlyList<string> arguments,
        IReadOnlyList<Option>? presenceOptions = null)
        => FindFailure(
            parseResult,
            arguments,
            presenceOptions)?.Error;

    public static CliOptionValueFailure? FindFailure(
        ParseResult parseResult,
        IReadOnlyList<string> arguments,
        IReadOnlyList<Option>? presenceOptions = null,
        IReadOnlyList<int>? argumentPositions = null)
    {
        if (argumentPositions is not null
            && argumentPositions.Count != arguments.Count)
        {
            throw new ArgumentException(
                "Every argument must have one source position.",
                nameof(argumentPositions));
        }

        ParsedArgument[] mapped = MapArguments(parseResult, arguments);
        IReadOnlyDictionary<Token, Option> optionValueOwners =
            GetOptionValueOwners(parseResult);
        CliOptionValueFailure? requiredValueFailure =
            FindRequiredValueFailure(
                parseResult,
                mapped,
                optionValueOwners,
                argumentPositions);
        var scopes = new List<CommandResult>();
        for (CommandResult? scope = parseResult.CommandResult;
            scope is not null;
            scope = scope.Parent as CommandResult)
        {
            scopes.Add(scope);
        }

        var effectivePresenceOptions =
            new HashSet<Option>(ReferenceEqualityComparer.Instance);
        if (presenceOptions is not null)
            effectivePresenceOptions.UnionWith(presenceOptions);
        foreach (CommandResult scope in scopes)
        {
            if (PresenceOptionsByCommand.TryGetValue(
                    scope.Command,
                    out IReadOnlyList<Option>? registered))
            {
                effectivePresenceOptions.UnionWith(registered);
            }
        }

        var positionalCounts = new Dictionary<CommandResult, int>();
        var positionalOwners = new Dictionary<Token, CommandResult>(ReferenceEqualityComparer.Instance);
        foreach (CommandResult scope in scopes)
        foreach (ArgumentResult argument in scope.Children.OfType<ArgumentResult>())
        foreach (Token token in argument.Tokens)
            positionalOwners.Add(token, scope);
        Option? precedingFlag = null;

        for (int index = 0; index < mapped.Length; index++)
        {
            if (arguments[index] == "--")
                break;

            CommandResult current = mapped[index].Scope;
            Option? flag = null;
            foreach (Token token in mapped[index].Tokens)
            {
                if (optionValueOwners.TryGetValue(
                        token,
                        out Option? valueOwner)
                    && !effectivePresenceOptions.Contains(valueOwner))
                {
                    continue;
                }

                if (token.Type == TokenType.Option)
                {
                    Option? option = FindOption(current, token.Value);
                    if (option is not null
                        && (option.Arity.MaximumNumberOfValues == 0
                            || effectivePresenceOptions.Contains(option)))
                    {
                        if (ReferenceEquals(mapped[index].AttachedOption, token))
                        {
                            return Earlier(
                                requiredValueFailure,
                                new(
                                    DoesNotAcceptValue(option.Name),
                                    argumentPositions?[index] ?? index));
                        }
                        flag = option;
                    }
                }
                else if (token.Type == TokenType.Argument)
                {
                    CommandResult owner = positionalOwners.GetValueOrDefault(token, current);
                    int count = positionalCounts.GetValueOrDefault(owner);
                    long capacity = owner.Command.Arguments.Sum(argument =>
                        (long)(Capacities.TryGetValue(argument, out var getCapacity)
                            ? getCapacity(parseResult)
                            : argument.Arity.MaximumNumberOfValues));
                    if (count >= capacity && precedingFlag is not null)
                    {
                        return Earlier(
                            requiredValueFailure,
                            new(
                                DoesNotAcceptValue(precedingFlag.Name),
                                argumentPositions?[index] ?? index));
                    }
                    positionalCounts[owner] = count + 1;
                }
            }

            precedingFlag = flag;
        }

        return requiredValueFailure;
    }

    private static CliOptionValueFailure? FindRequiredValueFailure(
        ParseResult parseResult,
        IReadOnlyList<ParsedArgument> mapped,
        IReadOnlyDictionary<Token, Option> optionValueOwners,
        IReadOnlyList<int>? argumentPositions)
    {
        for (int index = 0; index < mapped.Count; index++)
        {
            foreach (Token token in mapped[index].Tokens.Where(
                static token => token.Type == TokenType.Option))
            {
                Option? option = FindOption(
                    mapped[index].Scope,
                    token.Value);
                if (option is null
                    || !RequiredValuePerOccurrenceOptions.TryGetValue(
                        option,
                        out _))
                {
                    continue;
                }

                (string Value, int Position)? ownedValue =
                    FindOwnedValue(
                        mapped[index],
                        option,
                        optionValueOwners,
                        argumentPositions?[index] ?? index)
                    ?? (index + 1 < mapped.Count
                        && !mapped[index + 1].Tokens.Any(
                            static token => token.Type == TokenType.Option)
                        ? FindOwnedValue(
                            mapped[index + 1],
                            option,
                            optionValueOwners,
                            argumentPositions?[index + 1] ?? index + 1)
                        : null);
                if (ownedValue is null)
                {
                    return new(
                        $"Required argument missing for option: '{option.Name}'.",
                        argumentPositions?[index] ?? index);
                }

                var (value, valuePosition) = ownedValue.Value;
                if (AcceptedValuesByOption.TryGetValue(
                        option,
                        out HashSet<string>? acceptedValues)
                    && !acceptedValues.Contains(value)
                    && parseResult.Errors.Count == 0)
                {
                    return new(
                        $"Argument '{value}' is not "
                        + $"recognized for option '{option.Name}'.",
                        valuePosition);
                }
            }
        }

        return null;
    }

    private static (string Value, int Position)? FindOwnedValue(
        ParsedArgument argument,
        Option option,
        IReadOnlyDictionary<Token, Option> optionValueOwners,
        int position)
    {
        Token? value = argument.Tokens.FirstOrDefault(token =>
            token.Type == TokenType.Argument
            && optionValueOwners.TryGetValue(
                token,
                out Option? owner)
            && ReferenceEquals(owner, option));
        return value is null ? null : (value.Value, position);
    }

    private static CliOptionValueFailure Earlier(
        CliOptionValueFailure? first,
        CliOptionValueFailure second) =>
        first is not null && first.Position <= second.Position
            ? first
            : second;
}
