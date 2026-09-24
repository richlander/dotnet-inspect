using System.Runtime.ExceptionServices;

using DotnetInspect.Cli.Output;
using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal sealed class ExactLibraryInspectionSession(
    LibraryReference reference,
    LibraryContentOwner owner)
{
    public InspectionEnvelope<LibraryInspectionOutcome>? Execute(
        LibraryInspectionPlan plan,
        CancellationToken cancellationToken)
    {
        LibraryOperationLeaseIssueOutcome leaseIssue =
            owner.IssueOperationLease(reference);
        if (leaseIssue
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            CommandError.Write(
                "The exact Library owner could not issue the inspection "
                    + "operation lease.");
            return null;
        }

        using LibraryOperationLease lease = issued.Lease;
        var request = new LibraryInspectionRequest(reference, plan);
        return LibraryInspectionOperation.Execute(
            request,
            lease,
            cancellationToken);
    }
}

internal static class ExactLibraryInspectionExecutor
{
    private const long MaxAssemblyImageBytes =
        512L * 1024 * 1024;

    public static async Task<T?> ExecuteAsync<T>(
        string assemblyPath,
        string provenanceLabel,
        Func<ExactLibraryInspectionSession, T?> inspect,
        CancellationToken cancellationToken)
        where T : class
    {
        AssemblyDescriptorSelectionResult selection;
        try
        {
            selection = ResolvedAssemblyReference.SelectFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local(
                    provenanceLabel));
        }
        catch (Exception failure)
            when (failure is IOException
                or UnauthorizedAccessException)
        {
            CommandError.Write(
                "The direct Library file could not be read.");
            return null;
        }
        if (selection
            is not AssemblyDescriptorSelectionResult.Ready ready)
        {
            WriteSelectionFailure(selection);
            return null;
        }

        string? terminalFailure = null;
        ExceptionDispatchInfo? primaryFailure = null;
        List<string> cleanupFailures = [];
        T? result = null;
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
                result =
                    inspect(
                        new ExactLibraryInspectionSession(
                            available.Reference,
                            available.Owner));
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

        foreach (string failure in cleanupFailures)
            CommandError.Write(failure);
        primaryFailure?.Throw();

        if (cleanupFailures.Count > 0)
            return null;
        if (terminalFailure is not null)
        {
            CommandError.Write(terminalFailure);
            return null;
        }

        return result;
    }

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
                "The selected Library image exceeds the direct inspection "
                    + $"limit of {incomplete.MaxCapturedImageBytes} bytes.",
            AssemblyContextLibraryAdapterResult.ArtifactNotPublished =>
                "The selected Library image could not be published to "
                    + "the ephemeral Artifact generation.",
            AssemblyContextLibraryAdapterResult.MetadataNotProjected =>
                "The selected Library image could not be projected as "
                    + "managed Metadata.",
            AssemblyContextLibraryAdapterResult.PortablePdbRejected =>
                "The direct Library inspection rejected an unexpected "
                    + "Portable PDB companion.",
            _ => throw new InvalidOperationException(
                "Unknown direct Library realization result."),
        };
}
