using System.CommandLine;
using DotnetInspect.Cli.Output;

namespace DotnetInspect.Cli.CommandLine;

internal enum OutputItemProjection
{
    Items,
    Text,
}

internal static class CliOutputItemSelection
{
    public static OutputItemProjection ResolveProjection(bool print) =>
        print ? OutputItemProjection.Text : OutputItemProjection.Items;

    public static string? GetDocumentItemSelectionError<TModel>(OutputItemBinding<TModel>? binding) =>
        binding is { DocumentContainsItems: false }
            ? $"JSON document output does not expose {binding.Name} rows; "
                + "use --jsonl for item-limited rows."
            : null;

    public static void Register<TModel>(
        Command command,
        CliRowSelectionOptionBindings options,
        Func<ParseResult, OutputItemBinding<TModel>?> resolveBinding,
        Func<ParseResult, OutputItemProjection> resolveProjection,
        Func<ParseResult, bool> isJsonDocumentOutput)
        => Register(
            command,
            options,
            result => resolveBinding(result) is not null,
            resolveProjection,
            isJsonDocumentOutput,
            result => GetDocumentItemSelectionError(resolveBinding(result)));

    public static void Register(
        Command command,
        CliRowSelectionOptionBindings options,
        Func<ParseResult, bool> appliesTo,
        Func<ParseResult, OutputItemProjection> resolveProjection,
        Func<ParseResult, bool> isJsonDocumentOutput)
        => Register(command, options, appliesTo, resolveProjection, isJsonDocumentOutput,
            static _ => null);

    private static void Register(
        Command command,
        CliRowSelectionOptionBindings options,
        Func<ParseResult, bool> appliesTo,
        Func<ParseResult, OutputItemProjection> resolveProjection,
        Func<ParseResult, bool> isJsonDocumentOutput,
        Func<ParseResult, string?> jsonItemPopulationError)
    {
        RegisterUnit(OutputItemProjection.Items, CliRowSelectionDefaultUnit.SemanticRows);
        RegisterUnit(OutputItemProjection.Text, CliRowSelectionDefaultUnit.RenderedLines);

        void RegisterUnit(OutputItemProjection projection, CliRowSelectionDefaultUnit unit)
        {
            CliRowSelectionCommandRegistry.Register(
                command,
                options,
                CliRowSelectionCapabilities.HeadTail
                    | CliRowSelectionCapabilities.Window
                    | CliRowSelectionCapabilities.Lines,
                result => appliesTo(result)
                    && resolveProjection(result) == projection,
                validateLowering: (result, lowering) =>
                    CliRowSelectionValidation.ValidateLineSelectionForOutput(
                        isJsonDocumentOutput(result), lowering)
                    ?? (isJsonDocumentOutput(result)
                        && lowering.SemanticIntent.Operations.Count > 0
                            ? jsonItemPopulationError(result)
                            : null),
                defaultUnit: unit);
        }
    }
}
