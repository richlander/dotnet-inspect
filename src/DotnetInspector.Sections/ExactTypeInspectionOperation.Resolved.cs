using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public static partial class ExactTypeInspectionOperation
{
    public static async Task<TResult> ExecuteWithLiveTargetAsync<TResult>(
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        string type,
        ApiSurfaceScope scope,
        Func<
            InspectionEnvelope<ExactTypeInspectionResult>,
            SelectedContextExactTypeLiveTarget?,
            ApiSurface?,
            CancellationToken,
            ValueTask<TResult>> use,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(use);
        cancellationToken.ThrowIfCancellationRequested();
        await using var workspace = new InspectionWorkspace();
        ResolvedAssemblyExactTypeInspectionExecution resolved =
            ExactTypeInspectionQuery.ExecuteResolvedAssembly(
                workspace, assembly, bindingPolicy, type, scope);
        ExactTypeInspectionExecution execution = resolved.Execution;
        cancellationToken.ThrowIfCancellationRequested();
        var envelope = new InspectionEnvelope<ExactTypeInspectionResult>(
            execution.Result,
            new InspectionShare.NonProjectable(
                "resolved-type/share",
                "A complete portable Workspace scenario was not supplied."),
            Diagnostics(execution.Result));
        return await use(
            envelope, LiveTarget(execution.Target), resolved.Catalog, cancellationToken)
            .ConfigureAwait(false);
    }
}
