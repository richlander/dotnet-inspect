using System.CommandLine;
using DotnetInspect.Cli.Output;

namespace DotnetInspect.Cli.CommandLine;

internal static class CliRowSelectionRouterPreflight
{
    private static readonly CliRowSelectionOptionBindings UndeclaredBindings =
        new(
            new Option<string?>("--__undeclared-row-limit"),
            new Option<string?>("--__undeclared-row-window"),
            new Option<string?>("--__undeclared-row-top"),
            new Option<string?>("--__undeclared-row-order"),
            new Option<bool>("--__undeclared-row-head"),
            new Option<bool>("--__undeclared-row-tail"),
            new Option<bool>("--__undeclared-row-lines"),
            new Option<bool>("--__undeclared-row-tail-lines"));

    public static CliRowSelectionRouteEnvelopeResult Evaluate(
        string[] arguments,
        IReadOnlyList<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0)
        {
            throw new ArgumentException(
                "At least one implicit-route command is required.",
                nameof(commands));
        }

        CliRowSelectionRouteCandidate[] candidates =
        [
            .. commands.Select(command =>
                CreateCandidate(arguments, command)),
        ];
        return CliRowSelectionRouteEnvelope.Evaluate(arguments, candidates);
    }

    public static bool HasActiveAdoption(
        string[] arguments,
        IReadOnlyList<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(commands);
        return commands.Any(command =>
            CliRowSelectionCommandRegistry.TryGetActiveAdoption(
                command.Parse(arguments),
                out _));
    }

    public static string? FindCommonOptionValueError(
        string[] arguments,
        IReadOnlyList<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0)
        {
            throw new ArgumentException(
                "At least one implicit-route command is required.",
                nameof(commands));
        }

        string?[] errors =
        [
            .. commands.Select(command =>
            {
                ParseResult parseResult = command.Parse(arguments);
                CliRowSelectionPreparation preparation =
                    CliRowSelectionCommandRegistry.Prepare(
                        parseResult,
                        arguments);
                if (preparation.HasCompatibilityError)
                    return preparation.Error;

                return CliOptionValueValidation.FindError(
                    preparation.ParseResult,
                    preparation.Arguments ?? arguments,
                    preparation.PresenceOptions);
            }),
        ];
        string? first = errors[0];
        return first is not null
            && errors.Skip(1).All(error =>
                error?.Equals(
                    first,
                    StringComparison.Ordinal) == true)
                ? first
                : null;
    }

    public static bool TryWriteFailure(
        CliRowSelectionRouteEnvelopeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string? error =
            result.Outcome switch
            {
                CliRowSelectionRouteEnvelopeOutcome.ArgumentFailure =>
                    CliRowSelectionCommandRegistry.FormatArgumentFailure(
                        result.ArgumentFailure!),
                CliRowSelectionRouteEnvelopeOutcome.LoweringFailure
                    or CliRowSelectionRouteEnvelopeOutcome
                        .UnsupportedCapability =>
                    CliRowSelectionCommandRegistry.FormatLoweringFailure(
                        result.Failure!),
                CliRowSelectionRouteEnvelopeOutcome
                    .ExplicitCommandRequired =>
                    $"{FormatRequest(result.RequestKind)} is not uniform "
                    + "across implicit routes; use an explicit command.",
                _ => null,
            };
        if (error is null)
            return false;

        CommandError.Write(error);
        return true;
    }

    private static CliRowSelectionRouteCandidate CreateCandidate(
        string[] arguments,
        Command command)
    {
        ParseResult parseResult =
            command.Parse(arguments);
        bool adopted =
            CliRowSelectionCommandRegistry.TryGetActiveAdoption(
                parseResult,
                out CliRowSelectionCommandAdoption? adoption);
        return new(
            command,
            command,
            [],
            adopted
                ? adoption!.Bindings
                : UndeclaredBindings,
            adopted
                ? adoption!.Capabilities
                : CliRowSelectionCapabilities.None);
    }

    private static string FormatRequest(
        CliRowSelectionOccurrenceKind? kind) =>
        kind is { } requestKind
            ? CliRowSelectionCommandRegistry.OptionName(requestKind)
            : "Row selection";
}
