using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public static class LibraryTypeCatalogInspectionOperation
{
    public static InspectionEnvelope<AssemblyContextApiSurfaceResult> Execute(
        AssemblyContextGroup group,
        ApiSurfaceScope scope,
        ApiSurfaceProjectionLimits limits,
        IReadOnlyList<AssemblyContextParticipant>? participants = null) =>
        Complete(AssemblyContextApiSurfaceQuery.ExecuteBounded(
            group, scope, limits, participants));

    public static InspectionEnvelope<AssemblyContextApiSurfaceResult> Complete(
        AssemblyContextApiSurfaceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var diagnostics = ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        foreach (AssemblyContextEntry<AssemblyApiSurface> entry in result.Assemblies.Assemblies)
        {
            string? detail = entry switch
            {
                AssemblyContextEntry<AssemblyApiSurface>.Rejected rejected => rejected.Failure.Detail,
                AssemblyContextEntry<AssemblyApiSurface>.Failed failed => failed.Error.Message,
                _ => null,
            };
            if (detail is not null)
            {
                diagnostics.Add(new("library-type-catalog.unavailable",
                    InspectionDiagnosticSeverity.Error, detail, entry.Subject.Identity.Name));
            }
            if (entry is AssemblyContextEntry<AssemblyApiSurface>.Available available)
            {
                foreach (ApiSurfaceInspectionFailure failure in available.Value.InspectionFailures)
                {
                    diagnostics.Add(new("library-type-catalog.inspection-incomplete",
                        InspectionDiagnosticSeverity.Warning, failure.Detail,
                        entry.Subject.Identity.Name));
                }
            }
        }
        if (result.Truncation is { } truncation)
        {
            diagnostics.Add(new("library-type-catalog.truncated",
                InspectionDiagnosticSeverity.Warning,
                $"The catalog exceeded its {truncation.Limit} bound of {truncation.Bound}."));
        }
        return new(result,
            new InspectionShare.NonProjectable("library-type-catalog/share",
                "A complete portable Workspace scenario was not supplied."),
            diagnostics.ToImmutable());
    }

    public static async Task<TResult> ExecuteWithLiveSourcesAsync<TResult>(
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        ApiSurfaceScope scope,
        Func<InspectionEnvelope<AssemblyContextApiSurfaceResult>,
            IReadOnlyDictionary<ApiType, LibraryTypeCatalogSource>,
            CancellationToken, ValueTask<TResult>> use,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentNullException.ThrowIfNull(use);
        cancellationToken.ThrowIfCancellationRequested();
        var participant = new AssemblyContextParticipant(assembly, bindingPolicy);
        ResolvedLibraryTypeCatalogExecution execution =
            ResolvedLibraryTypeCatalogQuery.Execute(participant, scope);
        cancellationToken.ThrowIfCancellationRequested();
        return await use(Complete(execution.Result), execution.Sources, cancellationToken)
            .ConfigureAwait(false);
    }
}
