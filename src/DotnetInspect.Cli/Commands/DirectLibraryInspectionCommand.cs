using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal static class DirectLibraryInspectionCommand
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    private static readonly InspectionEnvelopeJsonContract<
        LibraryInspectionOutcome> s_inspectionJson =
            new(
                "library-inspection",
                1,
                LibraryInspectionJsonContext.Default
                    .LibraryInspectionOutcome);

    internal static bool ShouldExecute(
        LibraryOptions options) =>
        options.EnvelopeOutput;

    internal static async Task<int> ExecuteAsync(
        LibraryOptions options,
        LibrarySourceBinding source,
        CancellationToken cancellationToken)
    {
        if (source.Selector is not SourceSelector.Library
            || string.IsNullOrWhiteSpace(source.AssemblyName))
        {
            CommandError.Write(
                "Library inspection envelopes require one direct managed "
                    + "assembly file.");
            return 1;
        }
        if (options.Count || options.Rows is not null)
        {
            CommandError.Write(
                "Library inspection --envelope does not accept CLI terminal "
                    + "modifiers.");
            return 1;
        }
        if (HasSectionSelection(options))
        {
            CommandError.Write(
                "Library inspection --envelope does not accept section "
                    + "selection.");
            return 1;
        }
        if (!File.Exists(source.AssemblyName))
        {
            CommandError.Write(
                "The direct Library file does not exist.");
            return 1;
        }

        var plan = new LibraryInspectionPlan(
            new(
                LibraryTypeAccessibility.Public,
                new()),
            s_bounds);
        InspectionEnvelope<LibraryInspectionOutcome>? result =
            await ExactLibraryInspectionExecutor.ExecuteAsync(
                source.AssemblyName,
                "direct Library inspection",
                session => session.Execute(plan, cancellationToken),
                cancellationToken);
        if (result is null)
            return 1;

        return WriteEnvelope(
            result
            ?? throw new InvalidOperationException(
                "Direct Library inspection produced no terminal result."),
            options);
    }

    private static int WriteEnvelope(
        InspectionEnvelope<LibraryInspectionOutcome> envelope,
        LibraryOptions options)
    {
        bool wrote = InspectionEnvelopeOutput.TryWrite(
            envelope,
            s_inspectionJson,
            includeEnvelope: true,
            options.CompactJson,
            options.OutputPath);
        WriteDiagnostics(envelope.Diagnostics);
        return wrote
            && envelope.Content
                is LibraryInspectionOutcome.Available available
            && available.Document.Types.Count
                is LibraryTypePopulationCountOutcome.Counted
            ? 0
            : 1;
    }

    private static void WriteDiagnostics(
        IEnumerable<InspectionDiagnostic> diagnostics)
    {
        foreach (InspectionDiagnostic diagnostic in diagnostics)
        {
            string message = diagnostic.Summary.ToString();
            switch (diagnostic.Severity)
            {
                case InspectionDiagnosticSeverity.Information:
                    CommandError.WriteNote(message);
                    break;
                case InspectionDiagnosticSeverity.Warning:
                    CommandError.WriteWarning(message);
                    break;
                case InspectionDiagnosticSeverity.Error:
                    CommandError.Write(message);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Library inspection diagnostic severity.");
            }
        }
    }

    private static bool HasSectionSelection(
        LibraryOptions options) =>
        options.Select is { Length: > 0 }
        || options.SelectDefault
        || options.IncludeSections is not null;

}
