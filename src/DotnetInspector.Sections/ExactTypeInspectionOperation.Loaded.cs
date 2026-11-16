using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public static partial class ExactTypeInspectionOperation
{
    /// <summary>
    /// Executes over the requested Package participants in an already loaded
    /// context, retaining the context's source authorization and binding policy.
    /// </summary>
    public static TResult ExecuteWithLiveTarget<TResult>(
        InspectionWorkspace workspace,
        WorkspaceContextLoadOutcome.Loaded loaded,
        ExactTypeInspectionRequest request,
        ApiSurfaceScope scope,
        Func<InspectionEnvelope<ExactTypeInspectionResult>, SelectedContextExactTypeLiveTarget?, TResult> use)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(use);
        ExactTypeInspectionExecution execution = ExactTypeInspectionQuery.ExecutePackageContext(
            workspace.Identity, new ExactTypeInspectionContext(loaded), request, scope,
            projectionLimits: null);
        return use(Envelope(execution.Result, request), LiveTarget(execution.Target));
    }
}
