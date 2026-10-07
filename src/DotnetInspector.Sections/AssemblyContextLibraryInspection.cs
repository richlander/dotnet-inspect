using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// The terminal result of inspecting one assembly-context participant as an
/// exact direct Library. <see cref="Failure"/> describes a materialization
/// that produced no Library; <see cref="CleanupFailures"/> reports owner or
/// Artifact retirement problems, which hosts must surface.
/// </summary>
public sealed record AssemblyContextLibraryInspectionRun<T>(
    T? Result,
    string? Failure,
    ImmutableArray<string> CleanupFailures)
    where T : class;

/// <summary>
/// Materializes one assembly-context participant as an independently owned
/// direct Library (<c>docs/design/assembly-context-library-adapter.md</c>),
/// hands its reference and owner to one inspection, and retires the owner and
/// adjacent Artifact session on every path. Hosts own participant selection
/// and presentation; this owns the materialization lifecycle they share.
/// </summary>
public static class AssemblyContextLibraryInspection
{
    /// <summary>
    /// Materializes one selected assembly through a fresh directly owned
    /// Workspace, executes one Library inspection, and retires every acquired
    /// resource before returning.
    /// </summary>
    public static async Task<AssemblyContextLibraryInspectionRun<T>>
        ExecuteAsync<T>(
            ResolvedAssemblyReference assembly,
            IAssemblyBindingPolicy bindingPolicy,
            AssemblyContextLibraryRole role,
            AssemblyContextLibraryMaterializationLimits limits,
            Func<LibraryReference, LibraryContentOwner, T?> inspect,
            CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(inspect);

        ExceptionDispatchInfo? primaryFailure = null;
        var cleanupFailures = new List<string>();
        AssemblyContextLibraryInspectionRun<T>? run = null;
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup? group = null;
        try
        {
            var participant =
                new AssemblyContextParticipant(
                    assembly,
                    bindingPolicy);
            group = workspace.CreateAssemblyContextGroup(
                [participant],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes =
                        limits.MaxCapturedImageBytes,
                });
            run = await ExecuteAsync(
                    AssemblyContextLibraryAdapter.MaterializeAsync(
                        group,
                        participant,
                        role,
                        limits,
                        cancellationToken),
                    inspect)
                .ConfigureAwait(false);
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
                    "The direct Library assembly group could not retire.");
            }

            try
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                cleanupFailures.Add(
                    "The direct Library inspection Workspace could not retire.");
            }
        }

        if (primaryFailure is not null)
        {
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "Direct Library inspection and resource retirement both "
                        + "failed.",
                    [
                        primaryFailure.SourceException,
                        .. cleanupFailures.Select(
                            static failure =>
                                new InvalidOperationException(failure)),
                    ]);
            }

            primaryFailure.Throw();
        }
        if (run is null)
        {
            return new(
                Result: null,
                Failure: "Direct Library inspection did not complete.",
                CleanupFailures: [.. cleanupFailures]);
        }

        return cleanupFailures.Count == 0
            ? run
            : run with
            {
                CleanupFailures =
                [
                    .. run.CleanupFailures,
                    .. cleanupFailures,
                ],
            };
    }

    /// <summary>
    /// Issues one operation lease for a materialized Library, executes a
    /// resource-free inspection, and releases the lease before owner
    /// retirement begins.
    /// </summary>
    public static T? ExecuteOperation<T>(
        LibraryReference reference,
        LibraryContentOwner owner,
        Func<LibraryOperationLease, T> inspect)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(inspect);
        if (owner.IssueOperationLease(reference)
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            return null;
        }

        using LibraryOperationLease lease = issued.Lease;
        return inspect(lease);
    }

    /// <param name="materialization">
    /// A started <see cref="AssemblyContextLibraryAdapter.MaterializeAsync(AssemblyContextGroup, AssemblyContextParticipant, AssemblyContextLibraryRole, AssemblyContextLibraryMaterializationLimits, CancellationToken)"/>
    /// call. The adapter captures its group snapshot before its first await,
    /// so a host may start it inside a scoped group callback and await it here.
    /// </param>
    /// <param name="cleanupFailureSink">
    /// Receives every retirement failure, including when <paramref name="inspect"/>
    /// throws and no run is returned.
    /// </param>
    public static async Task<AssemblyContextLibraryInspectionRun<T>> ExecuteAsync<T>(
        ValueTask<AssemblyContextLibraryAdapterResult> materialization,
        Func<LibraryReference, LibraryContentOwner, T?> inspect,
        ICollection<string>? cleanupFailureSink = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(inspect);
        string? failure = null;
        ExceptionDispatchInfo? primaryFailure = null;
        List<string> cleanupFailures = [];
        T? result = null;
        AssemblyContextLibraryAdapterResult.Completed? completed = null;
        try
        {
            AssemblyContextLibraryAdapterResult outcome =
                await materialization.ConfigureAwait(false);
            if (outcome is AssemblyContextLibraryAdapterResult.Completed available)
            {
                completed = available;
                result = inspect(available.Reference, available.Owner);
            }
            else
            {
                failure = Describe(outcome);
                if (outcome is AssemblyContextLibraryAdapterResult.Terminal terminal
                    && terminal.CleanupFailures.Count > 0)
                {
                    cleanupFailures.Add(
                        "Direct Library realization reported one or more cleanup failures.");
                }
            }
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            if (completed is not null)
                await RetireAsync(completed, cleanupFailures).ConfigureAwait(false);
            if (cleanupFailureSink is not null)
            {
                foreach (string cleanupFailure in cleanupFailures)
                    cleanupFailureSink.Add(cleanupFailure);
            }
        }

        primaryFailure?.Throw();
        return new(result, failure, [.. cleanupFailures]);
    }

    /// <summary>
    /// Keeps the materialized Library alive while one asynchronous
    /// resource-free composition completes, then retires every authority.
    /// </summary>
    public static async Task<AssemblyContextLibraryInspectionRun<T>>
        ExecuteComposedAsync<T>(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            Func<
                LibraryReference,
                LibraryContentOwner,
                ValueTask<T?>> inspect,
            ICollection<string>? cleanupFailureSink = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(inspect);
        string? failure = null;
        ExceptionDispatchInfo? primaryFailure = null;
        List<string> cleanupFailures = [];
        T? result = null;
        AssemblyContextLibraryAdapterResult.Completed? completed = null;
        try
        {
            AssemblyContextLibraryAdapterResult outcome =
                await materialization.ConfigureAwait(false);
            if (outcome
                is AssemblyContextLibraryAdapterResult.Completed available)
            {
                completed = available;
                result =
                    await inspect(
                            available.Reference,
                            available.Owner)
                        .ConfigureAwait(false);
            }
            else
            {
                failure = Describe(outcome);
                if (outcome
                    is AssemblyContextLibraryAdapterResult.Terminal terminal
                    && terminal.CleanupFailures.Count > 0)
                {
                    cleanupFailures.Add(
                        "Direct Library realization reported one or more cleanup failures.");
                }
            }
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            if (completed is not null)
            {
                await RetireAsync(
                        completed,
                        cleanupFailures)
                    .ConfigureAwait(false);
            }
            if (cleanupFailureSink is not null)
            {
                foreach (string cleanupFailure in cleanupFailures)
                    cleanupFailureSink.Add(cleanupFailure);
            }
        }

        primaryFailure?.Throw();
        return new(result, failure, [.. cleanupFailures]);
    }

    private static async Task RetireAsync(
        AssemblyContextLibraryAdapterResult.Completed completed,
        List<string> cleanupFailures)
    {
        try
        {
            await completed.Owner.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            cleanupFailures.Add("The direct Library owner could not retire.");
        }
        if (completed.Owner.CleanupFailures.Count > 0
            || completed.Owner.ReleaseFailures.Count > 0)
        {
            cleanupFailures.Add(
                "The direct Library owner reported one or more content release failures.");
        }

        try
        {
            await completed.Artifacts.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            cleanupFailures.Add("The adjacent Artifact session could not retire.");
        }
        if (completed.Artifacts.CleanupFailures.Count > 0)
        {
            cleanupFailures.Add(
                "The adjacent Artifact session reported one or more cleanup failures.");
        }
    }

    private static string Describe(AssemblyContextLibraryAdapterResult result) =>
        result switch
        {
            AssemblyContextLibraryAdapterResult.SnapshotRejected rejected =>
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
