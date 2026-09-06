using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;
using static DotnetInspector.CommandLine.CliArgumentOwnership;

namespace DotnetInspector.CommandLine;

internal static class CliOptionValueValidation
{
    private static readonly ConditionalWeakTable<Argument, Func<ParseResult, int>> Capacities = new();

    public static void RegisterCapacity(Argument argument, Func<ParseResult, int> capacity) =>
        Capacities.Add(argument, capacity);

    public static string DoesNotAcceptValue(string optionName) =>
        $"{optionName} does not accept a value.";

    public static string? FindError(
        ParseResult parseResult,
        IReadOnlyList<string> arguments,
        IReadOnlyList<Option>? presenceOptions = null)
    {
        ParsedArgument[] mapped = MapArguments(parseResult, arguments);
        IReadOnlyList<OptionResult> options = GetOptionResults(parseResult);
        var optionValues = new HashSet<Token>(
            options.SelectMany(option => option.Tokens),
            ReferenceEqualityComparer.Instance);
        var scopes = new List<CommandResult>();
        for (CommandResult? scope = parseResult.CommandResult;
            scope is not null;
            scope = scope.Parent as CommandResult)
        {
            scopes.Add(scope);
        }

        CommandResult current = scopes[^1];
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

            Option? flag = null;
            foreach (Token token in mapped[index].Tokens)
            {
                if (optionValues.Contains(token))
                    continue;

                if (token.Type == TokenType.Command)
                {
                    current = scopes.First(scope => ReferenceEquals(scope.IdentifierToken, token));
                }
                else if (token.Type == TokenType.Option)
                {
                    Option? option = FindOption(current, token.Value);
                    if (option is not null
                        && (option.Arity.MaximumNumberOfValues == 0
                            || presenceOptions?.Contains(option) == true))
                    {
                        if (ReferenceEquals(mapped[index].AttachedOption, token))
                            return DoesNotAcceptValue(option.Name);
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
                        return DoesNotAcceptValue(precedingFlag.Name);
                    positionalCounts[owner] = count + 1;
                }
            }

            precedingFlag = flag;
        }

        return null;
    }

    private static Option? FindOption(CommandResult scope, string alias)
    {
        for (CommandResult? current = scope;
            current is not null;
            current = current.Parent as CommandResult)
        {
            Option? option = current.Children.OfType<OptionResult>()
                .Select(result => result.Option)
                .FirstOrDefault(option => option.Name == alias || option.Aliases.Contains(alias));
            if (option is not null)
                return option;
        }

        return null;
    }
}
