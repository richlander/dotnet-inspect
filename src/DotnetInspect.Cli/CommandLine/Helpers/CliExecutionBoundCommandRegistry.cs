using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace DotnetInspect.Cli.CommandLine;

internal sealed record CliExecutionBoundPreparation(
    string? Error,
    int? ErrorPosition,
    CliSelectionFailureCategory? ErrorCategory,
    bool IsActive);

internal sealed class CliExecutionBoundAdoption
{
    public CliExecutionBoundAdoption(
        Option option,
        Func<ParseResult, int> maximum,
        Func<ParseResult, bool> isActive)
    {
        Option = option;
        Maximum = maximum;
        IsActive = isActive;
    }

    public Option Option { get; }
    public Func<ParseResult, int> Maximum { get; }
    public Func<ParseResult, bool> IsActive { get; }
}

internal static class CliExecutionBoundCommandRegistry
{
    private static readonly ConditionalWeakTable<
        Command,
        CliExecutionBoundAdoption> Adoptions = new();

    private static readonly ConditionalWeakTable<
        ParseResult,
        StrongBox<int>> Values = new();

    public static void Register(
        Command command,
        Option option,
        Func<ParseResult, int> maximum,
        Func<ParseResult, bool> isActive)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(maximum);
        ArgumentNullException.ThrowIfNull(isActive);
        Adoptions.Add(
            command,
            new CliExecutionBoundAdoption(
                option,
                maximum,
                isActive));
    }

    public static CliExecutionBoundPreparation Prepare(
        ParseResult parseResult,
        IReadOnlyList<string> arguments,
        IReadOnlyList<int>? argumentPositions = null)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(arguments);
        if (argumentPositions is not null
            && argumentPositions.Count != arguments.Count)
        {
            throw new ArgumentException(
                "Every argument must have one source position.",
                nameof(argumentPositions));
        }

        if (!Adoptions.TryGetValue(
                parseResult.CommandResult.Command,
                out CliExecutionBoundAdoption? adoption)
            || !adoption.IsActive(parseResult))
        {
            return new(null, null, null, false);
        }

        int maximum = adoption.Maximum(parseResult);
        if (maximum < 1)
        {
            throw new InvalidOperationException(
                "The execution-bound maximum must be positive.");
        }

        CliArgumentOwnership.ParsedArgument[] mapped =
            CliArgumentOwnership.MapArguments(
                parseResult,
                arguments);
        IReadOnlyDictionary<Token, Option> optionValueOwners =
            CliArgumentOwnership.GetOptionValueOwners(parseResult);
        string optionName = adoption.Option.Name;
        var occurrences =
            new List<(int Position, string? Value, bool MissingValue)>();
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument == "--")
                break;

            if (!IsOwnedOptionOccurrence(
                    mapped[index],
                    adoption.Option,
                    optionName))
            {
                continue;
            }

            if (argument.Equals(
                    optionName,
                    StringComparison.Ordinal))
            {
                string? value =
                    index + 1 < arguments.Count
                        ? arguments[index + 1]
                        : null;
                bool parserOwnsValue =
                    value is not null
                    && mapped[index + 1].Tokens.Any(
                        token =>
                            token.Value.Equals(
                                value,
                                StringComparison.Ordinal)
                            && optionValueOwners.TryGetValue(
                                token,
                                out Option? owner)
                            && ReferenceEquals(
                                owner,
                                adoption.Option));
                bool authoredOption =
                    value is not null
                    && (value.StartsWith(
                            "--",
                            StringComparison.Ordinal)
                        || CliArgumentOwnership.FindOption(
                            mapped[index + 1].Scope,
                            value) is not null);
                bool missingValue =
                    value is null
                    || (IsOptionToken(value)
                        && (!parserOwnsValue
                            || authoredOption));
                occurrences.Add(
                    (
                        argumentPositions?[index] ?? index,
                        missingValue ? null : value,
                        missingValue));
                if (!missingValue)
                    index++;
                continue;
            }

            if (argument.StartsWith(
                    optionName + "=",
                    StringComparison.Ordinal)
                || argument.StartsWith(
                    optionName + ":",
                    StringComparison.Ordinal))
            {
                occurrences.Add(
                    (
                        argumentPositions?[index] ?? index,
                        argument[(optionName.Length + 1)..],
                        MissingValue: false));
            }
        }

        if (occurrences.Count == 0)
            return new(null, null, null, true);

        if (occurrences.FirstOrDefault(
                static occurrence =>
                    occurrence.MissingValue) is { MissingValue: true } missing)
        {
            return new(
                $"{optionName} requires a value.",
                missing.Position,
                CliSelectionFailureCategory.Arity,
                true);
        }

        foreach ((int position, string? value, _) in occurrences)
        {
            if (!TryParsePositive(value, out int parsed))
            {
                return new(
                    $"{optionName} requires a positive whole number.",
                    position,
                    CliSelectionFailureCategory.Value,
                    true);
            }

            if (parsed > maximum)
            {
                return new(
                    $"{optionName} must be between 1 and "
                    + $"{maximum.ToString(CultureInfo.InvariantCulture)}.",
                    position,
                    CliSelectionFailureCategory.Value,
                    true);
            }
        }

        if (occurrences.Count > 1)
        {
            return new(
                $"{optionName} may only be specified once.",
                occurrences[1].Position,
                CliSelectionFailureCategory.Conflict,
                true);
        }

        Values.Add(
            parseResult,
            new StrongBox<int>(
                int.Parse(
                    occurrences[0].Value!,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture)));
        return new(null, null, null, true);
    }

    private static bool IsOwnedOptionOccurrence(
        CliArgumentOwnership.ParsedArgument argument,
        Option option,
        string alias)
    {
        return argument.Tokens.Any(
                token =>
                    token.Type == TokenType.Option
                    && token.Value.Equals(
                        alias,
                        StringComparison.Ordinal))
            && ReferenceEquals(
                CliArgumentOwnership.FindOption(
                    argument.Scope,
                    alias),
                option);
    }

    public static int? GetPreparedValue(
        ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        return Values.TryGetValue(
            parseResult,
            out StrongBox<int>? value)
                ? value.Value
                : null;
    }

    private static bool TryParsePositive(
        string? value,
        out int parsed)
    {
        parsed = 0;
        return value is { Length: > 0 }
            && value.All(
                static character =>
                    character is >= '0' and <= '9')
            && int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out parsed)
            && parsed > 0;
    }

    private static bool IsOptionToken(string value)
    {
        if (value.Length == 0 || value[0] != '-')
            return false;

        if (value.Length == 1)
            return true;

        for (int index = 1; index < value.Length; index++)
        {
            if (value[index] is < '0' or > '9')
                return true;
        }

        return false;
    }
}
