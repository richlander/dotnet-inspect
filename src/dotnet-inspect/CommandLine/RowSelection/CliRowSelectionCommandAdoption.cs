using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;

namespace DotnetInspector.CommandLine;

internal sealed class CliRowSelectionCommandAdoption
{
    public CliRowSelectionCommandAdoption(
        CliRowSelectionOptionBindings bindings,
        CliRowSelectionCapabilities capabilities,
        Func<ParseResult, bool> isActive)
    {
        Bindings = bindings;
        Capabilities = capabilities;
        IsActive = isActive;
    }

    public CliRowSelectionOptionBindings Bindings { get; }

    public CliRowSelectionCapabilities Capabilities { get; }

    public Func<ParseResult, bool> IsActive { get; }
}

internal sealed class CliRowSelectionPreparation
{
    private CliRowSelectionPreparation(
        ParseResult parseResult,
        CliRowSelectionLowering<string>? lowering,
        string? error)
    {
        ParseResult = parseResult;
        Lowering = lowering;
        Error = error;
    }

    public ParseResult ParseResult { get; }

    public CliRowSelectionLowering<string>? Lowering { get; }

    public string? Error { get; }

    public bool IsActive => Lowering is not null || Error is not null;

    public static CliRowSelectionPreparation Inactive(ParseResult parseResult) =>
        new(parseResult, null, null);

    public static CliRowSelectionPreparation Success(
        ParseResult parseResult,
        CliRowSelectionLowering<string> lowering) =>
        new(parseResult, lowering, null);

    public static CliRowSelectionPreparation Failed(
        ParseResult parseResult,
        string error) =>
        new(parseResult, null, error);
}

internal static class CliRowSelectionCommandRegistry
{
    private static readonly ConditionalWeakTable<
        Command,
        CliRowSelectionCommandAdoption> Adoptions = new();

    private static readonly ConditionalWeakTable<
        ParseResult,
        CliRowSelectionLowering<string>> Lowerings = new();

    public static void Register(
        Command command,
        CliRowSelectionOptionBindings bindings,
        CliRowSelectionCapabilities capabilities,
        Func<ParseResult, bool> isActive)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(isActive);
        Adoptions.Add(
            command,
            new(bindings, capabilities, isActive));
    }

    public static bool OwnsShortLimit(
        ParseResult parseResult,
        IReadOnlyList<string> arguments)
    {
        if (TryGetActiveAdoption(parseResult, out _))
            return true;

        return parseResult.CommandResult.Command.Name == "router"
            && arguments.Any(
                static argument =>
                    argument is "--versions"
                        or "--versions-with-feed");
    }

    public static CliRowSelectionPreparation Prepare(
        ParseResult parseResult,
        string[]? arguments)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        if (!TryGetActiveAdoption(
                parseResult,
                out CliRowSelectionCommandAdoption? adoption))
        {
            return CliRowSelectionPreparation.Inactive(parseResult);
        }

        string[] effectiveArguments =
            arguments is null
                ? [.. parseResult.Tokens.Select(static token => token.Value)]
                : arguments;
        Command rootCommand = GetRootCommand(parseResult);
        CliRowSelectionArgumentResult result =
            CliRowSelectionArgumentAdapter.LowerExplicit(
                rootCommand,
                effectiveArguments,
                adoption!.Bindings,
                adoption.Capabilities);

        if (result.HasParseErrors)
            return CliRowSelectionPreparation.Inactive(result.ParseResult);

        if (result.ArgumentFailure is { } argumentFailure)
        {
            return CliRowSelectionPreparation.Failed(
                result.ParseResult,
                FormatArgumentFailure(argumentFailure));
        }

        CliRowSelectionLoweringResult<string> loweringResult =
            result.LoweringResult
            ?? throw new InvalidOperationException(
                "An adopted row-selection parse produced no lowering result.");
        if (!loweringResult.IsSuccess)
        {
            return CliRowSelectionPreparation.Failed(
                result.ParseResult,
                FormatLoweringFailure(loweringResult.Failure!));
        }

        CliRowSelectionLowering<string> lowering = loweringResult.Value!;
        Lowerings.Add(result.ParseResult, lowering);
        return CliRowSelectionPreparation.Success(
            result.ParseResult,
            lowering);
    }

    public static bool TryGetLowering(
        ParseResult parseResult,
        out CliRowSelectionLowering<string>? lowering) =>
        Lowerings.TryGetValue(parseResult, out lowering);

    private static bool TryGetActiveAdoption(
        ParseResult parseResult,
        out CliRowSelectionCommandAdoption? adoption)
    {
        if (Adoptions.TryGetValue(
                parseResult.CommandResult.Command,
                out adoption)
            && adoption.IsActive(parseResult))
        {
            return true;
        }

        adoption = null;
        return false;
    }

    private static Command GetRootCommand(ParseResult parseResult)
    {
        CommandResult commandResult = parseResult.CommandResult;
        while (commandResult.Parent is CommandResult parent)
            commandResult = parent;
        return commandResult.Command;
    }

    private static string FormatArgumentFailure(
        CliRowSelectionArgumentFailure failure) =>
        failure.Reason switch
        {
            CliRowSelectionArgumentFailureReason.MissingValue =>
                $"{OptionName(failure.OccurrenceKind)} requires a value.",
            CliRowSelectionArgumentFailureReason.AttachedValueOnModifier =>
                $"{OptionName(failure.OccurrenceKind)} does not accept a value.",
            _ => "The row-selection arguments are invalid."
        };

    private static string FormatLoweringFailure(
        CliRowSelectionFailure failure) =>
        failure.Reason switch
        {
            CliRowSelectionFailureReason.MalformedValue
                or CliRowSelectionFailureReason.NonPositiveValue
                or CliRowSelectionFailureReason.OverflowValue =>
                $"{OptionName(failure.OccurrenceKind)} requires a positive whole number.",
            CliRowSelectionFailureReason.InvalidWindowForm =>
                "--rows requires N..M, N.., or ..M with positive positions.",
            CliRowSelectionFailureReason.ReversedWindow =>
                "--rows end must not precede its start.",
            CliRowSelectionFailureReason.RepeatedGesture =>
                $"{OptionName(failure.OccurrenceKind)} may only be specified once.",
            CliRowSelectionFailureReason.ConflictingDirection =>
                "--head and --tail cannot be combined.",
            CliRowSelectionFailureReason.ModifierRequiresCount =>
                $"{OptionName(failure.OccurrenceKind)} requires -n.",
            CliRowSelectionFailureReason.UnsupportedCapability =>
                $"{OptionName(failure.OccurrenceKind)} is not available for this command.",
            _ => "The row-selection arguments are invalid."
        };

    private static string OptionName(
        CliRowSelectionOccurrenceKind kind) =>
        kind switch
        {
            CliRowSelectionOccurrenceKind.Limit => "-n",
            CliRowSelectionOccurrenceKind.Rows => "--rows",
            CliRowSelectionOccurrenceKind.Top => "--top",
            CliRowSelectionOccurrenceKind.OrderBy => "--order-by",
            CliRowSelectionOccurrenceKind.Head => "--head",
            CliRowSelectionOccurrenceKind.Tail => "--tail",
            CliRowSelectionOccurrenceKind.Lines => "--lines",
            CliRowSelectionOccurrenceKind.TailLines => "--tail-lines",
            _ => "row selection"
        };
}
