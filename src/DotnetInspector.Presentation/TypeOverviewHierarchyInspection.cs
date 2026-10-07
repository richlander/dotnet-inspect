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
    ImmutableArray<string> CleanupFailures);

public static class TypeOverviewHierarchyInspection
{
    private const int TypeSegmentSize = 256;

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
            run.CleanupFailures);
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
        var identities =
            new Dictionary<string, MetadataTypeDefinitionName>(
                StringComparer.Ordinal);
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        LibraryTypePopulationBinding? binding = null;
        LibraryTypePopulationContinuation? continuation = null;
        var continuations = new HashSet<string>(StringComparer.Ordinal);

        do
        {
            var plan =
                new LibraryInspectionPlan(
                    new LibraryTypePopulationRequest(
                        includeNonPublic
                            ? LibraryTypeAccessibility.All
                            : LibraryTypeAccessibility.Public,
                        count: null,
                        new LibraryTypePopulationRowsRequest(
                            TypeSegmentSize,
                            memberCount:
                                new LibraryTypeMemberCountRequest(),
                            continuation: continuation),
                        LibraryTypeDeclarationSelection.Definitions,
                        ApiTypeInventoryKinds.All),
                    bounds);
            InspectionEnvelope<LibraryInspectionOutcome>? listing =
                AssemblyContextLibraryInspection.ExecuteOperation(
                    reference,
                    owner,
                    lease => LibraryInspectionOperation.Execute(
                        new(reference, plan),
                        lease,
                        cancellationToken));
            if (listing is null)
            {
                return new(
                    Inspection: null,
                    Failure:
                        "The exact Library owner could not issue a Type "
                        + "inventory lease.");
            }

            diagnostics.AddRange(listing.Diagnostics);
            if (listing.Diagnostics.Any(static diagnostic =>
                    diagnostic.Severity
                        == InspectionDiagnosticSeverity.Error))
            {
                return new(
                    Inspection: null,
                    Failure:
                        "The exact Type inventory reported errors.");
            }
            if (listing.Content
                is not LibraryInspectionOutcome.Available available)
            {
                return new(
                    Inspection: null,
                    Failure: listing.Content switch
                    {
                        LibraryInspectionOutcome.Rejected rejected =>
                            $"The exact Type inventory was rejected: "
                                + rejected.Reason,
                        LibraryInspectionOutcome.Failed failed =>
                            $"The exact Type inventory failed: "
                                + failed.Reason,
                        _ =>
                            "The exact Type inventory returned an unknown "
                                + "outcome.",
                    });
            }
            if (available.Document.Types is not { } types)
            {
                return new(
                    Inspection: null,
                    Failure:
                        "The exact Type inventory returned no Type "
                        + "population.");
            }

            binding ??= types.Binding;
            if (types.Binding != binding)
            {
                return new(
                    Inspection: null,
                    Failure:
                        "The exact Type inventory changed while rows were "
                        + "being read.");
            }
            if (types.Rows
                is not LibraryTypePopulationRowsOutcome.Read read)
            {
                return new(
                    Inspection: null,
                    Failure:
                        "The exact Type inventory did not return complete "
                        + "Rows.");
            }
            if (identities.Count + read.Items.Length > bounds.MaxTypes)
            {
                return new(
                    Inspection: null,
                    Failure:
                        "The exact Type inventory exceeded its Type bound.");
            }
            foreach (LibraryTypeShape row in read.Items)
            {
                identities.TryAdd(
                    row.Identity.ToEscapedFullName(),
                    row.Identity);
            }

            continuation = read.Continuation;
            if (continuation is not null
                && !continuations.Add(continuation.Value.ToString()))
            {
                return new(
                    Inspection: null,
                    Failure:
                        "The exact Type inventory repeated a continuation.");
            }
        }
        while (continuation is not null);

        LookupResult lookup =
            TypeMatcher.Lookup(
                identities.Keys,
                requestedType);
        if (lookup.Match is not { } match)
        {
            string suggestions = lookup.Suggestions.Count == 0
                ? string.Empty
                : " Suggestions: "
                    + string.Join(", ", lookup.Suggestions)
                    + ".";
            return new(
                Inspection: null,
                Failure:
                    $"Type '{requestedType}' was not found."
                    + suggestions);
        }

        TypeOverviewDocumentInspectionPlan inspectionPlan =
            TypeOverviewHierarchyPresentation.CreateInspectionPlan(
                identities[match],
                presentation,
                bounds);
        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>? inspection =
            AssemblyContextLibraryInspection.ExecuteOperation(
                reference,
                owner,
                lease => TypeOverviewDocumentInspectionOperation.Execute(
                    new(reference, inspectionPlan),
                    lease,
                    cancellationToken));
        if (inspection is null)
        {
            return new(
                Inspection: null,
                Failure:
                    "The exact Library owner could not issue a Type overview "
                    + "inspection lease.");
        }
        if (diagnostics.Count > 0)
        {
            inspection = new(
                inspection.Content,
                inspection.Share,
                [
                    .. diagnostics,
                    .. inspection.Diagnostics,
                ]);
        }

        return new(inspection, Failure: null);
    }

    private sealed record LibraryInspectionResult(
        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>? Inspection,
        string? Failure);
}
