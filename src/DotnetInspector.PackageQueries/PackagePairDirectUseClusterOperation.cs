using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

using DotnetInspector.Queries;

namespace DotnetInspector.PackageQueries;

public sealed record PackagePairDirectUseClusterRequest
{
    public PackagePairDirectUseClusterRequest(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope,
        ImmutableArray<PackageRootBinding> bindings,
        PackagePairDirectUseClusterLimits? queryLimits = null,
        PackageAssemblyContextRealizationOptions? realizationOptions = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scope);
        if (bindings.IsDefault
            || bindings.Length != 2
            || bindings.Any(static binding => binding is null))
        {
            throw new ArgumentException(
                "Package-pair call use requires exactly two Package Root bindings.",
                nameof(bindings));
        }
        if (!ReferenceEquals(
                scope.Revision.Workspace,
                workspace.Identity))
        {
            throw new ArgumentException(
                "The captured Scope belongs to another Workspace.",
                nameof(scope));
        }

        Workspace = workspace;
        Scope = scope;
        Bindings = bindings;
        QueryLimits = queryLimits ?? new();
        RealizationOptions = realizationOptions;
    }

    public InspectionWorkspace Workspace { get; }

    public WorkspaceScopeSnapshot Scope { get; }

    public ImmutableArray<PackageRootBinding> Bindings { get; }

    public PackagePairDirectUseClusterLimits QueryLimits { get; }

    public PackageAssemblyContextRealizationOptions? RealizationOptions
    {
        get;
    }
}

public enum PackagePairDirectUseClusterOperationFailureReason
{
    PackageContextCleanupFailed,
}

public abstract record PackagePairDirectUseClusterOperationOutcome
{
    private PackagePairDirectUseClusterOperationOutcome()
    {
    }

    public sealed record Completed(
        WorkspaceScopeRevisionIdentity ScopeRevision,
        PackagePairDirectUseClusterOutcome Content)
        : PackagePairDirectUseClusterOperationOutcome;

    public sealed record WorkspaceNotCommitted(string Detail)
        : PackagePairDirectUseClusterOperationOutcome;

    public sealed record Failed(
        PackagePairDirectUseClusterOperationFailureReason Reason,
        string Detail,
        PackageRoleCleanupReport Cleanup)
        : PackagePairDirectUseClusterOperationOutcome;
}

/// <summary>
/// Realizes one exact two-Package implementation context, executes the
/// Package-pair Direct Use Cluster query, and publishes only detached content.
/// </summary>
public static class PackagePairDirectUseClusterOperation
{
    public static InspectionQuery<
        PackagePairDirectUseClusterOperationOutcome> Definition { get; } =
            new(
                "Package pair direct use cluster operation",
                InspectionCost.Unbounded);

    public static async Task<PackagePairDirectUseClusterOperationOutcome>
        ExecuteAsync(
            PackagePairDirectUseClusterRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (PackageRootBinding binding in request.Bindings)
        {
            if (request.Scope.FindExactPackageOccurrence(binding)
                    ?.Realization.Status
                is not ArtifactRootRealizationStatus.Ready)
            {
                return new PackagePairDirectUseClusterOperationOutcome
                    .WorkspaceNotCommitted(
                        "Both exact Package roots must be Ready in one committed Workspace Scope.");
            }
        }

        PackageAssemblyContextCompletionOperation contextOperation =
            request.Workspace.PreparePackageAssemblyContextCompletion(
                request.Bindings,
                request.RealizationOptions,
                () => YieldAndObserveCancellationAsync(
                    cancellationToken));
        PackageAssemblyContextCompletion completion =
            await contextOperation.ExecuteAsync(
                    contextOperation.Identity)
                .ConfigureAwait(false);

        PackagePairDirectUseClusterOutcome? queryOutcome = null;
        ExceptionDispatchInfo? queryFailure = null;
        PackageRoleCleanupReport cleanup;
        PackageAssemblyContextProjection? projection = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            projection = completion.CreateProjection(request.Bindings);
            queryOutcome = PackagePairDirectUseClusterQuery.Execute(
                projection,
                request.Bindings[0].Root.Identity,
                request.Bindings[0].Coordinate,
                request.Bindings[1].Root.Identity,
                request.Bindings[1].Coordinate,
                request.QueryLimits);
        }
        catch (Exception exception)
        {
            queryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            try
            {
                if (projection is not null)
                {
                    await projection.ReturnAsync()
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                queryFailure ??=
                    ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                cleanup = await completion.CloseAsync()
                    .ConfigureAwait(false);
            }
        }

        if (cleanup.Groups.Any(static group =>
                group is PackageRoleGroupCleanupRecord.Failed))
        {
            return new PackagePairDirectUseClusterOperationOutcome.Failed(
                PackagePairDirectUseClusterOperationFailureReason
                    .PackageContextCleanupFailed,
                "The Package-pair implementation context could not be released completely.",
                cleanup);
        }

        queryFailure?.Throw();
        cancellationToken.ThrowIfCancellationRequested();
        return new PackagePairDirectUseClusterOperationOutcome.Completed(
            request.Scope.Revision.Identity,
            queryOutcome!);
    }

    static async ValueTask YieldAndObserveCancellationAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
