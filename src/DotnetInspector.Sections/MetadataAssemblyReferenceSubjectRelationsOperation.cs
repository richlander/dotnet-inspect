using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed record MetadataAssemblyReferenceSubjectRelationsExecution
{
    public MetadataAssemblyReferenceSubjectRelationsExecution(
        SubjectRelationPopulationResult population,
        SubjectRelationPopulationContinuationAuthority?
            continuationAuthority)
    {
        Population = population
            ?? throw new ArgumentNullException(nameof(population));
        SubjectRelationPopulationContinuation? continuation =
            (population.Rows
                as SubjectRelationPopulationRowsOutcome.Read)
                ?.Continuation;
        if ((continuation is null)
            != (continuationAuthority is null)
            || continuation is not null
                && continuationAuthority!.Continuation
                    != continuation)
        {
            throw new ArgumentException(
                "Continuation authority must exactly accompany the returned Rows continuation.",
                nameof(continuationAuthority));
        }

        ContinuationAuthority = continuationAuthority;
    }

    public SubjectRelationPopulationResult Population { get; }

    public SubjectRelationPopulationContinuationAuthority?
        ContinuationAuthority
    { get; }
}

/// <summary>
/// Executes one outgoing assembly-reference Subject Relations population by
/// carrying Count and Rows intent into the Metadata producer.
/// </summary>
public static class MetadataAssemblyReferenceSubjectRelationsOperation
{
    public static MetadataAssemblyReferenceSubjectRelationsExecution Execute(
        AssemblyInspectionSession session,
        ResolvedAssemblyReference source,
        SubjectRelationsInspectionRequest request,
        SubjectRelationFocusCorrespondence correspondence,
        MetadataOperationPolicy policy,
        SubjectRelationPopulationContinuationAuthority?
            continuationAuthority = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(correspondence);
        ArgumentNullException.ThrowIfNull(policy);
        ValidateRequest(source, request, correspondence);

        SubjectRelationPopulationRowsRejection? continuationRejection =
            ValidateContinuation(request, continuationAuthority);
        if (continuationRejection is { } rejection
            && request.Request.Count is null)
        {
            return RejectedContinuation(request, rejection);
        }
        MetadataAssemblyReferenceRelationPopulationRowsRequest? sourceRows =
            request.Request.Rows is not { } rows
                || continuationRejection is not null
                ? null
                : new(
                    continuationAuthority?.NextOrdinal ?? 0,
                    rows.MaximumRows);
        Guid sourceModuleVersionId =
            source.Registration.ModuleVersionId
            ?? throw new InvalidOperationException(
                "Assembly-reference relation execution requires an "
                    + "MVID-bound source acquisition.");
        var sourceRequest =
            new MetadataAssemblyReferenceRelationPopulationRequest(
                policy,
                request.Request.Count is null
                    ? null
                    : new
                        MetadataAssemblyReferenceRelationPopulationCountRequest(),
                sourceRows,
                sourceModuleVersionId);
        MetadataAssemblyReferenceRelationPopulationOutcome outcome =
            session.AssemblyReferenceRelations(
                sourceRequest,
                cancellationToken);
        return outcome switch
        {
            MetadataAssemblyReferenceRelationPopulationOutcome.Available
                available =>
                Project(
                    source,
                    request,
                    correspondence,
                    available.Result,
                    continuationAuthority,
                    continuationRejection),
            MetadataAssemblyReferenceRelationPopulationOutcome.Rejected
                rejected =>
                Rejected(
                    request,
                    rejected,
                    continuationRejection),
            _ => throw new InvalidOperationException(
                "Unknown Metadata assembly-reference population outcome."),
        };
    }

    private static MetadataAssemblyReferenceSubjectRelationsExecution Project(
        ResolvedAssemblyReference source,
        SubjectRelationsInspectionRequest request,
        SubjectRelationFocusCorrespondence correspondence,
        MetadataAssemblyReferenceRelationPopulationResult sourceResult,
        SubjectRelationPopulationContinuationAuthority?
            inputContinuationAuthority,
        SubjectRelationPopulationRowsRejection? continuationRejection)
    {
        MetadataRelationGraphAdapter.ValidateAssemblyReferencePopulation(
            source,
            sourceResult);
        SubjectRelationProducerOutcome producer =
            MetadataRelationGraphAdapter
                .AssemblyReferenceProducerOutcome(sourceResult);
        var evidence =
            new SubjectRelationPopulationEvidence(
                request.Population,
                [producer]);
        SubjectRelationPopulationCountOutcome? count =
            MapCount(sourceResult.Count);
        SubjectRelationPopulationRowsOutcome? rows;
        SubjectRelationPopulationContinuationAuthority? outputAuthority =
            null;
        if (continuationRejection is { } rejection)
        {
            rows =
                new SubjectRelationPopulationRowsOutcome.Rejected(
                    rejection);
        }
        else
        {
            rows = MapRows(
                source,
                request,
                correspondence,
                sourceResult.Receipt.ModuleVersionId,
                sourceResult.Rows,
                out outputAuthority);
        }

        SubjectRelationPopulationResult population =
            SubjectRelationsPopulationOperation.Settle(
                request,
                evidence,
                count,
                rows,
                rows is SubjectRelationPopulationRowsOutcome.Read
                    && request.Request.Rows?.Continuation is not null
                    ? inputContinuationAuthority
                    : null);
        return new(population, outputAuthority);
    }

    private static SubjectRelationPopulationCountOutcome? MapCount(
        MetadataAssemblyReferenceRelationPopulationCountOutcome? count) =>
        count switch
        {
            null => null,
            MetadataAssemblyReferenceRelationPopulationCountOutcome
                    .Counted counted =>
                new SubjectRelationPopulationCountOutcome.Counted(
                    counted.Value),
            MetadataAssemblyReferenceRelationPopulationCountOutcome
                    .Unavailable =>
                new SubjectRelationPopulationCountOutcome.Unavailable(),
            MetadataAssemblyReferenceRelationPopulationCountOutcome
                    .Incomplete =>
                new SubjectRelationPopulationCountOutcome.Incomplete(),
            MetadataAssemblyReferenceRelationPopulationCountOutcome
                    .Failed =>
                new SubjectRelationPopulationCountOutcome.Failed(),
            _ => throw new InvalidOperationException(
                "Unknown Metadata assembly-reference Count outcome."),
        };

    private static SubjectRelationPopulationRowsOutcome? MapRows(
        ResolvedAssemblyReference source,
        SubjectRelationsInspectionRequest request,
        SubjectRelationFocusCorrespondence correspondence,
        Guid? moduleVersionId,
        MetadataAssemblyReferenceRelationPopulationRowsOutcome? rows,
        out SubjectRelationPopulationContinuationAuthority?
            continuationAuthority)
    {
        continuationAuthority = null;
        switch (rows)
        {
            case null:
                return null;
            case MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Read read:
                ImmutableArray<SubjectRelationRow> relationRows =
                    MetadataRelationGraphAdapter.BindAssemblyReferenceRows(
                        source,
                        read.Items,
                        correspondence);
                SubjectRelationPopulationContinuation? continuation =
                    null;
                if (read.NextOrdinal is int nextOrdinal)
                {
                    Guid sourceModuleVersionId =
                        moduleVersionId
                        ?? throw new InvalidOperationException(
                            "Usable Metadata assembly-reference Rows require "
                                + "a source MVID.");
                    continuation =
                        new SubjectRelationPopulationContinuation(
                            new InertString(
                                TextPolicy.Field,
                                $"metadata-reference-{sourceModuleVersionId:N}-"
                                    + $"{nextOrdinal}"));
                    continuationAuthority =
                        SubjectRelationPopulationContinuationAuthority
                            .Capture(
                                continuation,
                                request.Focus,
                                request.Population,
                                request.Request.Selection,
                                SubjectRelationPopulationOrdering.Producer,
                                SubjectRelationRowProjection.Canonical,
                                nextOrdinal);
                }
                return new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    relationRows,
                    continuation);
            case MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Rejected rejected:
                return new SubjectRelationPopulationRowsOutcome.Rejected(
                    rejected.Reason switch
                    {
                        MetadataAssemblyReferenceRelationPopulationRowsRejection
                                .StaleSource =>
                            SubjectRelationPopulationRowsRejection
                                .StaleContinuation,
                        MetadataAssemblyReferenceRelationPopulationRowsRejection
                                .ContinuationOutOfRange =>
                            SubjectRelationPopulationRowsRejection
                                .ContinuationOutOfRange,
                        _ => throw new InvalidOperationException(
                            "Unknown Metadata assembly-reference Rows rejection."),
                    });
            case MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Unavailable:
                return new SubjectRelationPopulationRowsOutcome.Unavailable();
            case MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Incomplete:
                return new SubjectRelationPopulationRowsOutcome.Incomplete();
            case MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Failed:
                return new SubjectRelationPopulationRowsOutcome.Failed();
            default:
                throw new InvalidOperationException(
                    "Unknown Metadata assembly-reference Rows outcome.");
        }
    }

    private static MetadataAssemblyReferenceSubjectRelationsExecution Rejected(
        SubjectRelationsInspectionRequest request,
        MetadataAssemblyReferenceRelationPopulationOutcome.Rejected rejected,
        SubjectRelationPopulationRowsRejection? continuationRejection)
    {
        var diagnostic =
            SubjectRelationProducerDiagnostic.Create(
                SubjectRelationProducerDiagnosticKind.Failure,
                rejected);
        var producer =
            new SubjectRelationProducerOutcome(
                MetadataRelationGraphAdapter.AssemblyReferenceQuery,
                SubjectRelationProducerDisposition.Failed,
                new(1, 0, 0, 1, 0),
                [
                    InspectionGraphIntegrationsCatalog
                        .MetadataReference,
                ],
                [diagnostic]);
        var evidence =
            new SubjectRelationPopulationEvidence(
                request.Population,
                [producer]);
        SubjectRelationPopulationResult population =
            SubjectRelationsPopulationOperation.Settle(
                request,
                evidence,
                request.Request.Count is null
                    ? null
                    : new SubjectRelationPopulationCountOutcome.Failed(),
                request.Request.Rows is null
                    ? null
                    : continuationRejection is { } reason
                        ? new
                            SubjectRelationPopulationRowsOutcome.Rejected(
                                reason)
                        : new
                            SubjectRelationPopulationRowsOutcome.Failed());
        return new(population, continuationAuthority: null);
    }

    private static MetadataAssemblyReferenceSubjectRelationsExecution
        RejectedContinuation(
            SubjectRelationsInspectionRequest request,
            SubjectRelationPopulationRowsRejection rejection)
    {
        SubjectRelationPopulationResult population =
            SubjectRelationsPopulationOperation.Settle(
                request,
                new SubjectRelationPopulationEvidence(
                    request.Population,
                    []),
                count: null,
                new SubjectRelationPopulationRowsOutcome.Rejected(
                    rejection));
        return new(population, continuationAuthority: null);
    }

    private static SubjectRelationPopulationRowsRejection?
        ValidateContinuation(
            SubjectRelationsInspectionRequest request,
            SubjectRelationPopulationContinuationAuthority? authority)
    {
        SubjectRelationPopulationContinuation? continuation =
            request.Request.Rows?.Continuation;
        if (continuation is null)
        {
            if (authority is not null)
            {
                throw new ArgumentException(
                    "Continuation authority requires a continued Rows request.",
                    nameof(authority));
            }
            return null;
        }
        if (authority is null)
        {
            return SubjectRelationPopulationRowsRejection
                .InvalidContinuation;
        }
        return SubjectRelationsPopulationOperation
            .ContinuationRejection(request, authority);
    }

    private static void ValidateRequest(
        ResolvedAssemblyReference source,
        SubjectRelationsInspectionRequest request,
        SubjectRelationFocusCorrespondence correspondence)
    {
        if (source.Registration.ModuleVersionId is null)
        {
            throw new ArgumentException(
                "Assembly-reference relation execution requires an "
                    + "MVID-bound source acquisition.",
                nameof(source));
        }
        if (request.Route != SubjectRelationsRouteKind.Library
            || correspondence.Focus != request.Focus
            || !ReferenceEquals(
                correspondence.Population,
                request.Population)
            || correspondence.Role
                != InspectionGraphEndpointRole.Source
            || correspondence.Endpoint
                != InspectionGraphSubject.ForAcquiredAssembly(source))
        {
            throw new ArgumentException(
                "Assembly-reference relation execution requires exact "
                    + "outgoing Library focus correspondence.",
                nameof(correspondence));
        }

        SubjectRelationPopulationSelection selection =
            request.Request.Selection;
        if (selection.Form
                is not SubjectRelationForm.AssemblyReference
            || selection.Relationship is not null
                && !string.Equals(
                    selection.Relationship,
                    InspectionGraphIntegrationsCatalog
                        .MetadataReference.Id,
                    StringComparison.Ordinal)
            || selection.Direction
                is not SubjectRelationDirectionSelection.Both
                    and not SubjectRelationDirectionSelection.Outgoing
            || selection.Evidence
                is not null
                and not SubjectRelationEvidenceKind.Declaration
            || selection.Integration
                != SubjectRelationIntegrationSelection.Any
            || selection.Ecosystem is not null
            || selection.Concept is not null)
        {
            throw new ArgumentException(
                "The request must select the outgoing assembly-reference "
                    + "population owned by the Metadata producer.",
                nameof(request));
        }
    }
}
