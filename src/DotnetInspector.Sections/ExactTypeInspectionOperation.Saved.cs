using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Sections;

public static partial class ExactTypeInspectionOperation
{
    /// <summary>
    /// Projects the exact saved Type during the caller's selected-subject borrow.
    /// The consumer must return detached content before that borrow ends.
    /// </summary>
    public static ValueTask<T> ExecuteSelectedSubjectAsync<T>(
        InspectionWorkspace workspace,
        CompleteRestorationSelectedSubject.Type selected,
        PackageAssemblyContextRealization roles,
        Func<ExactTypeInspectionResult, IReadOnlyList<InspectionDiagnostic>,
            SelectedContextExactTypeLiveTarget?, CancellationToken, ValueTask<T>> consume,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(consume);
        cancellationToken.ThrowIfCancellationRequested();
        ExactTypeInspectionExecution execution =
            ExactTypeInspectionQuery.ExecuteSelectedSubject(workspace, selected, roles);
        SelectedContextExactTypeLiveTarget? target = execution.Target is { } live
            ? new(live.Surface.Surface, live.Type, live.Occurrence.Assembly,
                live.Occurrence, live.BindingPolicy, live.AssemblyPath,
                live.PackageAssetPath, live.PackageExtractPath)
            {
                OpenCompiledDocumentation = live.OpenCompiledDocumentation,
            }
            : null;
        return consume(execution.Result, Diagnostics(execution.Result), target, cancellationToken);
    }
}
