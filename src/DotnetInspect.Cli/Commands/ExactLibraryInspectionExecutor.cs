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

        ExceptionDispatchInfo? primaryFailure = null;
        List<string> cleanupFailures = [];
        AssemblyContextLibraryInspectionRun<T>? run = null;
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup? group = null;
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
            run = await AssemblyContextLibraryInspection.ExecuteAsync(
                    AssemblyContextLibraryAdapter.MaterializeAsync(
                        group,
                        participant,
                        AssemblyContextLibraryRole.ApiOnly,
                        new AssemblyContextLibraryMaterializationLimits(
                            MaxAssemblyImageBytes,
                            MaxAssemblyImageBytes),
                        cancellationToken),
                    (reference, owner) =>
                        inspect(
                            new ExactLibraryInspectionSession(
                                reference,
                                owner)))
                .ConfigureAwait(false);
            cleanupFailures.AddRange(run.CleanupFailures);
        }
        catch (Exception failure)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(failure);
        }
        finally
        {
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
        if (run?.Failure is { } terminalFailure)
        {
            CommandError.Write(terminalFailure);
            return null;
        }

        return run?.Result;
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
}
