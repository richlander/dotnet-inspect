using System.Runtime.ExceptionServices;

using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
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
                "Library overview envelopes require one direct managed "
                    + "assembly file.");
            return 1;
        }
        if (options.Count)
        {
            CommandError.Write(
                "The scalar Library overview does not support --count.");
            return 1;
        }
        if (options.Rows is not null)
        {
            CommandError.Write(
                "The scalar Library overview does not support --rows.");
            return 1;
        }
        if (HasSectionSelection(options))
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

        InspectionEnvelope<LibraryOverviewOutcome>? result = null;
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
                    result =
                        LibraryOverviewInspectionOperation.Execute(
                            request,
                            operation.Lease,
                            cancellationToken);
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

        return WriteEnvelope(
            result
            ?? throw new InvalidOperationException(
                "Direct Library overview produced no terminal result."),
            options);
    }

    private static int WriteEnvelope(
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
}
