using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed class MetadataExtensionSubjectRelationsContinuationAuthority
{
    private MetadataExtensionSubjectRelationsContinuationAuthority(
        SubjectRelationPopulationContinuationAuthority population,
        Guid sourceModuleVersionId,
        MetadataExtensionReceiverSelection receiver,
        bool includeNonPublic)
    {
        PopulationAuthority = population
            ?? throw new ArgumentNullException(nameof(population));
        SourceModuleVersionId = sourceModuleVersionId;
        Receiver = receiver
            ?? throw new ArgumentNullException(nameof(receiver));
        IncludeNonPublic = includeNonPublic;
    }

    internal SubjectRelationPopulationContinuationAuthority
        PopulationAuthority
    { get; }

    internal SubjectRelationPopulationContinuation Continuation =>
        PopulationAuthority.Continuation;

    internal int NextOrdinal => PopulationAuthority.NextOrdinal;

    internal Guid SourceModuleVersionId { get; }

    internal MetadataExtensionReceiverSelection Receiver { get; }

    internal bool IncludeNonPublic { get; }

    internal static MetadataExtensionSubjectRelationsContinuationAuthority
        Capture(
            SubjectRelationPopulationContinuationAuthority population,
            Guid sourceModuleVersionId,
            MetadataExtensionReceiverSelection receiver,
            bool includeNonPublic) =>
        new(
            population,
            sourceModuleVersionId,
            receiver,
            includeNonPublic);
}

public sealed record MetadataExtensionSubjectRelationsExecution
{
    public MetadataExtensionSubjectRelationsExecution(
        SubjectRelationPopulationResult population,
        MetadataExtensionSubjectRelationsContinuationAuthority?
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
                && continuationAuthority!.Continuation != continuation)
        {
            throw new ArgumentException(
                "Continuation authority must exactly accompany the returned Rows continuation.",
                nameof(continuationAuthority));
        }

        ContinuationAuthority = continuationAuthority;
    }

    public SubjectRelationPopulationResult Population { get; }

    public MetadataExtensionSubjectRelationsContinuationAuthority?
        ContinuationAuthority
    { get; }
}

/// <summary>
/// Executes one incoming extension Subject Relations population by carrying
/// Count and Rows intent into one exact Metadata producer.
/// </summary>
public static class MetadataExtensionSubjectRelationsOperation
{
    public static MetadataExtensionSubjectRelationsExecution Execute(
        AssemblyInspectionSession session,
        ResolvedAssemblyReference source,
        MetadataExtensionReceiverSelection receiver,
        SubjectRelationsInspectionRequest request,
        MetadataOperationPolicy policy,
        bool includeNonPublic = false,
        MetadataExtensionSubjectRelationsContinuationAuthority?
            continuationAuthority = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);
        ValidateRequest(source, request);

        Guid sourceModuleVersionId =
            source.Registration.ModuleVersionId
            ?? throw new InvalidOperationException(
                "Extension relation execution requires an MVID-bound "
                    + "source acquisition.");
        SubjectRelationPopulationRowsRejection? continuationRejection =
            ValidateContinuation(
                request,
                continuationAuthority,
                sourceModuleVersionId,
                receiver,
                includeNonPublic);
        if (continuationRejection is { } rejection
            && request.Request.Count is null)
        {
            return RejectedContinuation(request, rejection);
        }

        MetadataExtensionRelationPopulationRowsRequest? sourceRows =
            request.Request.Rows is not { } rows
                || continuationRejection is not null
                ? null
                : new(
                    continuationAuthority?.NextOrdinal ?? 0,
                    rows.MaximumRows);
        var sourceRequest =
            new MetadataExtensionRelationPopulationRequest(
                receiver,
                policy,
                request.Request.Count is null
                    ? null
                    : new
                        MetadataExtensionRelationPopulationCountRequest(),
                sourceRows,
                includeNonPublic,
                sourceModuleVersionId);
        MetadataExtensionRelationPopulationOutcome outcome =
            session.ExtensionRelations(
                sourceRequest,
                cancellationToken);
        return outcome switch
        {
            MetadataExtensionRelationPopulationOutcome.Available available =>
                Project(
                    source,
                    receiver,
                    request,
                    includeNonPublic,
                    available.Result,
                    continuationAuthority,
                    continuationRejection),
            MetadataExtensionRelationPopulationOutcome.Rejected rejected =>
                Rejected(
                    request,
                    rejected,
                    continuationRejection),
            _ => throw new InvalidOperationException(
                "Unknown Metadata extension population outcome."),
        };
    }

    private static MetadataExtensionSubjectRelationsExecution Project(
        ResolvedAssemblyReference source,
        MetadataExtensionReceiverSelection receiver,
        SubjectRelationsInspectionRequest request,
        bool includeNonPublic,
        MetadataExtensionRelationPopulationResult sourceResult,
        MetadataExtensionSubjectRelationsContinuationAuthority?
            inputContinuationAuthority,
        SubjectRelationPopulationRowsRejection? continuationRejection)
    {
        MetadataRelationGraphAdapter.ValidateExtensionPopulation(
            source,
            sourceResult);
        SubjectRelationProducerOutcome producer =
            MetadataRelationGraphAdapter.ExtensionProducerOutcome(
                sourceResult);
        var evidence =
            new SubjectRelationPopulationEvidence(
                request.Population,
                [producer]);
        SubjectRelationPopulationCountOutcome? count =
            MapCount(sourceResult.Count);
        SubjectRelationPopulationRowsOutcome? rows;
        MetadataExtensionSubjectRelationsContinuationAuthority?
            outputAuthority = null;
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
                receiver,
                request,
                includeNonPublic,
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
                    ? inputContinuationAuthority?.PopulationAuthority
                    : null);
        return new(population, outputAuthority);
    }

    private static SubjectRelationPopulationCountOutcome? MapCount(
        MetadataExtensionRelationPopulationCountOutcome? count) =>
        count switch
        {
            null => null,
            MetadataExtensionRelationPopulationCountOutcome.Counted counted =>
                new SubjectRelationPopulationCountOutcome.Counted(
                    counted.Value),
            MetadataExtensionRelationPopulationCountOutcome.Unavailable =>
                new SubjectRelationPopulationCountOutcome.Unavailable(),
            MetadataExtensionRelationPopulationCountOutcome.Incomplete =>
                new SubjectRelationPopulationCountOutcome.Incomplete(),
            MetadataExtensionRelationPopulationCountOutcome.Failed =>
                new SubjectRelationPopulationCountOutcome.Failed(),
            _ => throw new InvalidOperationException(
                "Unknown Metadata extension Count outcome."),
        };

    private static SubjectRelationPopulationRowsOutcome? MapRows(
        ResolvedAssemblyReference source,
        MetadataExtensionReceiverSelection receiver,
        SubjectRelationsInspectionRequest request,
        bool includeNonPublic,
        Guid? moduleVersionId,
        MetadataExtensionRelationPopulationRowsOutcome? rows,
        out MetadataExtensionSubjectRelationsContinuationAuthority?
            continuationAuthority)
    {
        continuationAuthority = null;
        switch (rows)
        {
            case null:
                return null;
            case MetadataExtensionRelationPopulationRowsOutcome.Read read:
                ImmutableArray<SubjectRelationRow> relationRows =
                    MetadataRelationGraphAdapter.BindExtensionRows(
                        source,
                        read.Items,
                        request.Focus,
                        request.Population,
                        receiver);
                SubjectRelationPopulationContinuation? continuation = null;
                if (read.NextOrdinal is int nextOrdinal)
                {
                    Guid sourceModuleVersionId =
                        moduleVersionId
                        ?? throw new InvalidOperationException(
                            "Usable Metadata extension Rows require a source MVID.");
                    continuation =
                        new SubjectRelationPopulationContinuation(
                            new InertString(
                                TextPolicy.Field,
                                $"metadata-extension-"
                                    + $"{sourceModuleVersionId:N}-"
                                    + $"{nextOrdinal}"));
                    SubjectRelationPopulationContinuationAuthority
                        populationAuthority =
                        SubjectRelationPopulationContinuationAuthority
                            .Capture(
                                continuation,
                                request.Focus,
                                request.Population,
                                request.Request.Selection,
                                SubjectRelationPopulationOrdering.Producer,
                                SubjectRelationRowProjection.Canonical,
                                nextOrdinal);
                    continuationAuthority =
                        MetadataExtensionSubjectRelationsContinuationAuthority
                            .Capture(
                                populationAuthority,
                                sourceModuleVersionId,
                                receiver,
                                includeNonPublic);
                }
                return new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    relationRows,
                    continuation);
            case MetadataExtensionRelationPopulationRowsOutcome.Rejected
                    rejected:
                return new SubjectRelationPopulationRowsOutcome.Rejected(
                    rejected.Reason switch
                    {
                        MetadataExtensionRelationPopulationRowsRejection
                                .StaleSource =>
                            SubjectRelationPopulationRowsRejection
                                .StaleContinuation,
                        MetadataExtensionRelationPopulationRowsRejection
                                .ContinuationOutOfRange =>
                            SubjectRelationPopulationRowsRejection
                                .ContinuationOutOfRange,
                        _ => throw new InvalidOperationException(
                            "Unknown Metadata extension Rows rejection."),
                    });
            case MetadataExtensionRelationPopulationRowsOutcome.Unavailable:
                return new SubjectRelationPopulationRowsOutcome.Unavailable();
            case MetadataExtensionRelationPopulationRowsOutcome.Incomplete:
                return new SubjectRelationPopulationRowsOutcome.Incomplete();
            case MetadataExtensionRelationPopulationRowsOutcome.Failed:
                return new SubjectRelationPopulationRowsOutcome.Failed();
            default:
                throw new InvalidOperationException(
                    "Unknown Metadata extension Rows outcome.");
        }
    }

    private static MetadataExtensionSubjectRelationsExecution Rejected(
        SubjectRelationsInspectionRequest request,
        MetadataExtensionRelationPopulationOutcome.Rejected rejected,
        SubjectRelationPopulationRowsRejection? continuationRejection)
    {
        var diagnostic =
            SubjectRelationProducerDiagnostic.Create(
                SubjectRelationProducerDiagnosticKind.Failure,
                rejected);
        var producer =
            new SubjectRelationProducerOutcome(
                MetadataRelationGraphAdapter.ExtensionQuery,
                SubjectRelationProducerDisposition.Failed,
                new(1, 0, 0, 1, 0),
                [MetadataRelationGraphCatalog.Extension],
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

    private static MetadataExtensionSubjectRelationsExecution
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
            MetadataExtensionSubjectRelationsContinuationAuthority?
                authority,
            Guid sourceModuleVersionId,
            MetadataExtensionReceiverSelection receiver,
            bool includeNonPublic)
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
        if (authority.SourceModuleVersionId != sourceModuleVersionId)
        {
            return SubjectRelationPopulationRowsRejection
                .StaleContinuation;
        }
        if (authority.Receiver != receiver
            || authority.IncludeNonPublic != includeNonPublic)
        {
            return SubjectRelationPopulationRowsRejection
                .IncompatibleContinuation;
        }
        return SubjectRelationsPopulationOperation
            .ContinuationRejection(
                request,
                authority.PopulationAuthority);
    }

    private static void ValidateRequest(
        ResolvedAssemblyReference source,
        SubjectRelationsInspectionRequest request)
    {
        if (source.Registration.ModuleVersionId is null)
        {
            throw new ArgumentException(
                "Extension relation execution requires an MVID-bound "
                    + "source acquisition.",
                nameof(source));
        }
        if (request.Route != SubjectRelationsRouteKind.Type
            || request.Focus.Kind != StructuralSubjectKind.Type)
        {
            throw new ArgumentException(
                "Extension relation execution requires exact Type focus.",
                nameof(request));
        }

        SubjectRelationPopulationSelection selection =
            request.Request.Selection;
        if (selection.Form is not SubjectRelationForm.Extension
            || selection.Relationship is not null
                && !string.Equals(
                    selection.Relationship,
                    MetadataRelationGraphCatalog.Extension.Id,
                    StringComparison.Ordinal)
            || selection.Direction
                is not SubjectRelationDirectionSelection.Incoming
            || selection.Evidence
                is not null
                and not SubjectRelationEvidenceKind.Declaration
            || selection.Integration
                != SubjectRelationIntegrationSelection.Any
            || selection.Ecosystem is not null
            || selection.Concept is not null)
        {
            throw new ArgumentException(
                "The request must select the incoming extension population "
                    + "owned by the Metadata producer.",
                nameof(request));
        }
    }
}
