using System.Collections.Immutable;

using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Presentation;

public sealed record TypeOverviewHierarchyInspectionExecution(
    InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>? Inspection,
    TypeOverviewHierarchyPresentationPlan Presentation,
    string? Failure,
    ImmutableArray<string> CleanupFailures)
{
    public ExactTypeDefinitionSelection? Selection { get; init; }
}

public static class TypeOverviewHierarchyInspection
{
    public static async Task<TypeOverviewHierarchyInspectionExecution>
        ExecuteAsync(
            ResolvedAssemblyReference assembly,
            IAssemblyBindingPolicy bindingPolicy,
            string requestedType,
            TypeOverviewHierarchyPresentationFormat format,
            bool includeNonPublic,
            ApiSurfaceExtractionBounds bounds,
            AssemblyContextLibraryMaterializationLimits
                materializationLimits,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedType);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(materializationLimits);

        TypeOverviewHierarchyPresentationPlan presentation =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                format,
                includeNonPublic);
        AssemblyContextLibraryInspectionRun<LibraryInspectionResult> run =
            await AssemblyContextLibraryInspection.ExecuteAsync(
                    assembly,
                    bindingPolicy,
                    AssemblyContextLibraryRole.ApiOnly,
                    materializationLimits,
                    (reference, owner) =>
                        Inspect(
                            reference,
                            owner,
                            requestedType,
                            includeNonPublic,
                            presentation,
                            bounds,
                            cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        return new(
            run.Result?.Inspection,
            presentation,
            run.Result?.Failure ?? run.Failure,
            run.CleanupFailures)
        {
            Selection = run.Result?.Selection,
        };
    }

    private static LibraryInspectionResult Inspect(
        LibraryReference reference,
        LibraryContentOwner owner,
        string requestedType,
        bool includeNonPublic,
        TypeOverviewHierarchyPresentationPlan presentation,
        ApiSurfaceExtractionBounds bounds,
        CancellationToken cancellationToken)
    {
        return AssemblyContextLibraryInspection.ExecuteOperation(
            reference, owner,
            lease => ExactTypeInspectionOperation.ExecuteWithLibraryTarget(
                reference, lease, requestedType,
                includeNonPublic ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.Public,
                bounds,
                (selection, targetLease) =>
                {
                    if (selection.Content.Definition is not { } definition)
                        return new LibraryInspectionResult(null, selection.Content.Failure)
                        {
                            Selection = selection.Content,
                        };
                    TypeOverviewDocumentInspectionPlan plan =
                        TypeOverviewHierarchyPresentation.CreateInspectionPlan(
                            definition, presentation, bounds);
                    InspectionEnvelope<TypeOverviewDocumentInspectionOutcome> overview =
                        TypeOverviewDocumentInspectionOperation.Execute(
                            new(reference, plan), targetLease, cancellationToken);
                    return new LibraryInspectionResult(overview, null)
                    {
                        Selection = selection.Content,
                    };
                },
                includePrefixDiscovery: presentation.Format == TypeOverviewHierarchyPresentationFormat.Tree,
                cancellationToken: cancellationToken))
            ?? new LibraryInspectionResult(null, "The exact Library owner could not issue a Type selection lease.");
    }

    private sealed record LibraryInspectionResult(
        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>? Inspection,
        string? Failure)
    {
        public ExactTypeDefinitionSelection? Selection { get; init; }
    }
}
