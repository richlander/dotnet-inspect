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
                            return new(
                                DoesNotAcceptValue(option.Name),
                                argumentPositions?[index] ?? index);
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
                        return new(
                            DoesNotAcceptValue(precedingFlag.Name),
                            argumentPositions?[index] ?? index);
                    }
                    positionalCounts[owner] = count + 1;
                }
            }

            precedingFlag = flag;
        }

        return null;
    }
}
