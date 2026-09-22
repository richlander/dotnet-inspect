using System.Runtime.ExceptionServices;

using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal static class DirectLibraryOverviewCommand
{
    private const long MaxAssemblyImageBytes =
        512L * 1024 * 1024;

    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    private static readonly InspectionEnvelopeJsonContract<
        LibraryOverviewOutcome> s_overviewJson =
            new(
                "library-overview",
                1,
                LibraryOverviewInspectionJsonContext.Default
                    .LibraryOverviewOutcome);

    private static readonly InspectionEnvelopeJsonContract<int>
        s_countJson =
            new(
                "library-overview-count",
                1,
                static (writer, count) =>
                    writer.WriteNumberValue(count));

    internal static bool ShouldExecute(
        LibraryOptions options,
        LibrarySourceBinding source) =>
        options.EnvelopeOutput
        || source.Selector is SourceSelector.Library
            && IsExactLibraryInfoCount(options);

    internal static async Task<int> ExecuteAsync(
        LibraryOptions options,
        LibrarySourceBinding source,
        CancellationToken cancellationToken)
    {
        if (source.Selector is not SourceSelector.Library
            || string.IsNullOrWhiteSpace(source.AssemblyName))
        {
            CommandError.Write(
                "Library overview envelopes require one direct managed "
                    + "assembly file.");
            return 1;
        }
        if (options.Count && !IsExactLibraryInfoCount(options))
        {
            CommandError.Write(
                "Library overview --count requires exactly "
                    + $"-S \"{SectionNames.LibraryInfo}\".");
            return 1;
        }
        if (!options.Count && HasSectionSelection(options))
        {
            CommandError.Write(
                "Library overview --envelope does not accept section "
                    + "selection.");
            return 1;
        }
        if (!File.Exists(source.AssemblyName))
        {
            CommandError.Write(
                "The direct Library file does not exist.");
            return 1;
        }

        AssemblyDescriptorSelectionResult selection;
        try
        {
            selection = ResolvedAssemblyReference.SelectFromPath(
                source.AssemblyName,
                AssemblyResolutionProvenance.Local(
                    "direct Library overview"));
        }
        catch (Exception failure)
            when (failure is IOException
                or UnauthorizedAccessException)
        {
            CommandError.Write(
                "The direct Library file could not be read.");
            return 1;
        }
        if (selection
            is not AssemblyDescriptorSelectionResult.Ready ready)
        {
            WriteSelectionFailure(selection);
            return 1;
        }

        LibraryOverviewCliResult? result = null;
        string? terminalFailure = null;
        ExceptionDispatchInfo? primaryFailure = null;
        List<string> cleanupFailures = [];
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup? group = null;
        AssemblyContextLibraryAdapterResult.Completed? completed =
            null;
        try
        {
            var participant = new AssemblyContextParticipant(
                ready.Reference,
                NoResolverAssemblyBindingPolicy.Instance);
            group = workspace.CreateAssemblyContextGroup(
                [participant],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes =
                        MaxAssemblyImageBytes,
                });
            AssemblyContextLibraryAdapterResult materialization =
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                        group,
                        participant,
                        AssemblyContextLibraryRole.ApiOnly,
                        new AssemblyContextLibraryMaterializationLimits(
                            MaxAssemblyImageBytes,
                            MaxAssemblyImageBytes),
                        cancellationToken)
                    .ConfigureAwait(false);
            if (materialization
                is not AssemblyContextLibraryAdapterResult.Completed
                    available)
            {
                terminalFailure = Describe(materialization);
                if (materialization
                    is AssemblyContextLibraryAdapterResult.Terminal
                        terminal
                    && terminal.CleanupFailures.Count > 0)
                {
                    cleanupFailures.Add(
                        "Direct Library realization reported one or "
                            + "more cleanup failures.");
                }
            }
            else
            {
                completed = available;
                LibraryOperationLeaseIssueOutcome issued =
                    available.Owner.IssueOperationLease(
                        available.Reference);
                if (issued
                    is not LibraryOperationLeaseIssueOutcome.Issued
                        operation)
                {
                    terminalFailure =
                        "The direct Library owner could not issue the "
                            + "overview operation lease.";
                }
                else
                {
                    var request = new LibraryOverviewRequest(
                        available.Reference,
                        s_bounds);
                    result = options.Count
                        ? new LibraryOverviewCliResult.Count(
                            LibraryOverviewInspectionOperation
                                .ExecuteCount(
                                    request,
                                    operation.Lease,
                                    cancellationToken))
                        : new LibraryOverviewCliResult.Rows(
                            LibraryOverviewInspectionOperation.Execute(
                                request,
                                operation.Lease,
                                cancellationToken));
                }
            }
        }
        catch (Exception failure)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(failure);
        }
        finally
        {
            if (completed is not null)
            {
                try
                {
                    await completed.Owner.DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch
                {
                    cleanupFailures.Add(
                        "The direct Library owner could not retire.");
                }
                if (completed.Owner.CleanupFailures.Count > 0
                    || completed.Owner.ReleaseFailures.Count > 0)
                {
                    cleanupFailures.Add(
                        "The direct Library owner reported one or more "
                            + "content release failures.");
                }

                try
                {
                    await completed.Artifacts.DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch
                {
                    cleanupFailures.Add(
                        "The adjacent Artifact session could not retire.");
                }
                if (completed.Artifacts.CleanupFailures.Count > 0)
                {
                    cleanupFailures.Add(
                        "The adjacent Artifact session reported one or "
                            + "more cleanup failures.");
                }
            }

            try
            {
                group?.Dispose();
            }
            catch
            {
                cleanupFailures.Add(
                    "The ephemeral assembly group could not retire.");
            }

            try
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                cleanupFailures.Add(
                    "The ephemeral inspection Workspace could not retire.");
            }
        }

        if (primaryFailure is not null)
        {
            foreach (string failure in cleanupFailures)
                CommandError.Write(failure);
            primaryFailure.Throw();
        }
        if (cleanupFailures.Count > 0)
        {
            foreach (string failure in cleanupFailures)
                CommandError.Write(failure);
            return 1;
        }
        if (terminalFailure is not null)
        {
            CommandError.Write(terminalFailure);
            return 1;
        }

        return Write(
            result
            ?? throw new InvalidOperationException(
                "Direct Library overview produced no terminal result."),
            options);
    }

    private static int Write(
        LibraryOverviewCliResult result,
        LibraryOptions options) =>
        result switch
        {
            LibraryOverviewCliResult.Rows rows =>
                WriteRows(rows.Inspection, options),
            LibraryOverviewCliResult.Count count =>
                WriteCount(count.Result, options),
            _ => throw new InvalidOperationException(
                "Unknown direct Library overview result."),
        };

    private static int WriteRows(
        InspectionEnvelope<LibraryOverviewOutcome> envelope,
        LibraryOptions options)
    {
        bool wrote = InspectionEnvelopeOutput.TryWrite(
            envelope,
            s_overviewJson,
            includeEnvelope: true,
            options.CompactJson,
            options.OutputPath);
        WriteDiagnostics(envelope.Diagnostics);
        return wrote
            && envelope.Content
                is LibraryOverviewOutcome.Available
                    ? 0
                    : 1;
    }

    private static int WriteCount(
        LibraryOverviewCountResult result,
        LibraryOptions options)
    {
        if (result
            is LibraryOverviewCountResult.NotAvailable notAvailable)
        {
            if (options.EnvelopeOutput)
            {
                InspectionEnvelopeOutput.TryWrite(
                    notAvailable.Inspection,
                    s_overviewJson,
                    includeEnvelope: true,
                    options.CompactJson,
                    options.OutputPath);
            }
            WriteDiagnostics(
                notAvailable.Inspection.Diagnostics);
            return 1;
        }

        InspectionEnvelope<int> envelope =
            ((LibraryOverviewCountResult.Completed)result)
                .Inspection;
        WriteDiagnostics(envelope.Diagnostics);
        if (!options.EnvelopeOutput)
        {
            CountOutput.WriteCount(
                envelope.Content,
                options.OutputPath);
            return 0;
        }

        ProjectionAudit.MarkHonored(ProjectionAudit.Count);
        return InspectionEnvelopeOutput.TryWrite(
            envelope,
            s_countJson,
            includeEnvelope: true,
            options.CompactJson,
            options.OutputPath)
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
                        "Unknown Library overview diagnostic severity.");
            }
        }
    }

    private static bool IsExactLibraryInfoCount(
        LibraryOptions options) =>
        options.Count
        && options.Rows is null
        && options.Columns is null
        && options.Fields is null
        && !options.SelectDefault
        && (options.Select is [var selected]
                && string.Equals(
                    selected,
                    SectionNames.LibraryInfo,
                    StringComparison.OrdinalIgnoreCase)
            || options.Select is null
                && options.IncludeSections is { Count: 1 }
                    sections
                && sections.Contains(
                    SectionNames.LibraryInfo));

    private static bool HasSectionSelection(
        LibraryOptions options) =>
        options.Select is { Length: > 0 }
        || options.SelectDefault
        || options.IncludeSections is not null;

    private static void WriteSelectionFailure(
        AssemblyDescriptorSelectionResult selection)
    {
        switch (selection)
        {
            case AssemblyDescriptorSelectionResult.Descriptorless:
                CommandError.Write(
                    "The selected Library is not a managed assembly.");
                break;
            case AssemblyDescriptorSelectionResult.Rejected rejected:
                CommandError.Write(
                    "The selected Library could not be admitted as a "
                        + "managed assembly.",
                    [$"Admission kind: {rejected.Failure.Kind}"]);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown direct Library descriptor selection result.");
        }
    }

    private static string Describe(
        AssemblyContextLibraryAdapterResult result) =>
        result switch
        {
            AssemblyContextLibraryAdapterResult.SnapshotRejected
                rejected =>
                "The selected Library image could not be captured "
                    + $"({rejected.Failure.Kind}).",
            AssemblyContextLibraryAdapterResult.Incomplete incomplete =>
                "The selected Library image exceeds the direct overview "
                    + $"limit of {incomplete.MaxCapturedImageBytes} bytes.",
            AssemblyContextLibraryAdapterResult.ArtifactNotPublished =>
                "The selected Library image could not be published to "
                    + "the ephemeral Artifact generation.",
            AssemblyContextLibraryAdapterResult.MetadataNotProjected =>
                "The selected Library image could not be projected as "
                    + "managed Metadata.",
            AssemblyContextLibraryAdapterResult.PortablePdbRejected =>
                "The direct Library overview rejected an unexpected "
                    + "Portable PDB companion.",
            _ => throw new InvalidOperationException(
                "Unknown direct Library realization result."),
        };

    private abstract record LibraryOverviewCliResult
    {
        private LibraryOverviewCliResult()
        {
        }

        internal sealed record Rows(
            InspectionEnvelope<LibraryOverviewOutcome> Inspection)
            : LibraryOverviewCliResult;

        internal sealed record Count(
            LibraryOverviewCountResult Result)
            : LibraryOverviewCliResult;
    }
}
