using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public static partial class ExactTypeInspectionOperation
{
    public static Task<TResult> ExecuteWithLiveTargetAsync<TResult>(
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
        => ExecuteWithLiveTargetCoreAsync(
            assembly, bindingPolicy, type, null, scope, use, cancellationToken);

    public static Task<TResult> ExecuteWithLiveTargetAsync<TResult>(
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        MetadataTypeDefinitionName type,
        ApiSurfaceScope scope,
        Func<
            InspectionEnvelope<ExactTypeInspectionResult>,
            SelectedContextExactTypeLiveTarget?,
            ApiSurface?,
            CancellationToken,
            ValueTask<TResult>> use,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ExecuteWithLiveTargetCoreAsync(
            assembly, bindingPolicy, type.ToEscapedFullName(), type,
            scope, use, cancellationToken);
    }

    private static async Task<TResult> ExecuteWithLiveTargetCoreAsync<TResult>(
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        string type,
        MetadataTypeDefinitionName? selectedDefinition,
        ApiSurfaceScope scope,
        Func<
            InspectionEnvelope<ExactTypeInspectionResult>,
            SelectedContextExactTypeLiveTarget?,
            ApiSurface?,
            CancellationToken,
            ValueTask<TResult>> use,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(use);
        cancellationToken.ThrowIfCancellationRequested();
        await using var workspace = new InspectionWorkspace();
        ResolvedAssemblyExactTypeInspectionExecution resolved =
            ExactTypeInspectionQuery.ExecuteResolvedAssembly(
                workspace, assembly, bindingPolicy, type, scope, selectedDefinition);
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
