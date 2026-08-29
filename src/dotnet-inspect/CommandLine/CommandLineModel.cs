using System.CommandLine;

namespace DotnetInspector.CommandLine;

internal static class CommandLineModel
{
    public static Option? FindOption(
        RootCommand rootCommand,
        Command command,
        string token)
    {
        string optionName = GetOptionName(token);
        List<Command> path = [];
        if (!TryFindCommandPath(rootCommand, command, path))
            return null;

        for (int i = path.Count - 1; i >= 0; i--)
        {
            Option? option = path[i].Options.FirstOrDefault(
                option => Matches(option, optionName));
            if (option is not null)
                return option;
        }

        return null;
    }

    public static IEnumerable<Option> FindOptions(
        RootCommand rootCommand,
        string token)
    {
        string optionName = GetOptionName(token);
        foreach (Command command in EnumerateCommands(rootCommand))
        {
            foreach (Option option in command.Options)
            {
                if (Matches(option, optionName))
                    yield return option;
            }
        }
    }

    public static string GetOptionName(string token)
    {
        int separator = token.AsSpan().IndexOfAny('=', ':');
        return separator < 0
            ? token
            : token[..separator];
    }

    public static bool HasAttachedValue(string token) =>
        token.AsSpan().IndexOfAny('=', ':') >= 0;

    public static bool CanConsumeFollowingValue(Option option) =>
        option.ValueType != typeof(bool)
        && option.Arity.MaximumNumberOfValues > 0;

    public static bool CanConsumeFollowingToken(
        Option option,
        string token) =>
        CanConsumeFollowingValue(option)
        || (option.ValueType == typeof(bool)
            && bool.TryParse(token, out _));

    public static bool IsLimitShorthand(string token) =>
        token.Length >= 2
        && token[0] == '-'
        && char.IsDigit(token[1])
        && int.TryParse(token.AsSpan(1), out _);

    private static IEnumerable<Command> EnumerateCommands(
        Command command)
    {
        yield return command;
        foreach (Command child in command.Subcommands)
        {
            foreach (Command descendant in EnumerateCommands(child))
                yield return descendant;
        }
    }

    private static bool TryFindCommandPath(
        Command current,
        Command target,
        List<Command> path)
    {
        path.Add(current);
        if (ReferenceEquals(current, target))
            return true;

        foreach (Command child in current.Subcommands)
        {
            if (TryFindCommandPath(child, target, path))
                return true;
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private static bool Matches(
        Option option,
        string optionName) =>
        string.Equals(
            option.Name,
            optionName,
            StringComparison.OrdinalIgnoreCase)
        || option.Aliases.Contains(
            optionName,
            StringComparer.OrdinalIgnoreCase);
}
