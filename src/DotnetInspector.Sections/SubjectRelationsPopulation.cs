using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public enum SubjectRelationsRouteKind
{
    Package,
    Library,
    Type,
    Member,
}

public enum SubjectRelationDirectionSelection
{
    Incoming,
    Outgoing,
    Both,
}

public enum SubjectRelationIntegrationSelection
{
    Any,
    Associated,
}

public enum SubjectRelationPopulationOrdering
{
    Producer,
}

public enum SubjectRelationRowProjection
{
    Canonical,
}

/// <summary>Canonical selection for one relation population.</summary>
public sealed record SubjectRelationPopulationSelection
{
    public SubjectRelationPopulationSelection(
        SubjectRelationForm? form = null,
        string? relationship = null,
        SubjectRelationDirectionSelection direction =
            SubjectRelationDirectionSelection.Both,
        SubjectRelationEvidenceKind? evidence = null,
        SubjectRelationIntegrationSelection integration =
            SubjectRelationIntegrationSelection.Any,
        WorkspaceEcosystemRegistrationId? ecosystem = null,
        string? concept = null)
    {
        if (form is not null && !Enum.IsDefined(form.Value))
            throw new ArgumentOutOfRangeException(nameof(form));
        if (relationship is not null
            && string.IsNullOrWhiteSpace(relationship))
        {
            throw new ArgumentException(
                "A relationship selection cannot be empty.",
                nameof(relationship));
        }
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (evidence is not null && !Enum.IsDefined(evidence.Value))
            throw new ArgumentOutOfRangeException(nameof(evidence));
        if (!Enum.IsDefined(integration))
            throw new ArgumentOutOfRangeException(nameof(integration));
        if (concept is not null
            && !IntegrationConceptCatalog.Concepts.Any(
                candidate => string.Equals(
                    candidate.Id.Value,
                    concept,
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"'{concept}' is not a configured Integration concept.",
                nameof(concept));
        }

        Form = form;
        Relationship = relationship;
        Direction = direction;
        Evidence = evidence;
        Integration = integration;
        Ecosystem = ecosystem;
        Concept = concept;
    }

    public SubjectRelationForm? Form { get; }

    public string? Relationship { get; }

    public SubjectRelationDirectionSelection Direction { get; }

    public SubjectRelationEvidenceKind? Evidence { get; }

    public SubjectRelationIntegrationSelection Integration { get; }

    public WorkspaceEcosystemRegistrationId? Ecosystem { get; }

    public string? Concept { get; }

    internal bool Matches(SubjectRelationRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return (Form is null || row.Form == Form)
            && (Relationship is null
                || string.Equals(
                    row.Relationship.Id,
                    Relationship,
                    StringComparison.Ordinal))
            && DirectionMatches(row.Direction)
            && (Evidence is null || row.EvidenceKind == Evidence)
            && (Integration
                    != SubjectRelationIntegrationSelection.Associated
                || row.IsIntegration)
            && AssociationsMatch(row);
    }

    private bool AssociationsMatch(SubjectRelationRow row) =>
        Ecosystem is null && Concept is null
        || row.IntegrationAssociations.Any(
            association =>
                (Ecosystem is null
                    || association.Ecosystem == Ecosystem)
                && (Concept is null
                    || string.Equals(
                        association.Concept.Id.Value,
                        Concept,
                        StringComparison.Ordinal)));

    private bool DirectionMatches(SubjectRelationDirection direction) =>
        Direction switch
        {
            SubjectRelationDirectionSelection.Incoming =>
                direction == SubjectRelationDirection.Incoming,
            SubjectRelationDirectionSelection.Outgoing =>
                direction == SubjectRelationDirection.Outgoing,
            SubjectRelationDirectionSelection.Both => true,
            _ => throw new InvalidOperationException(
                "Unknown relation direction selection."),
        };
}

/// <summary>Request for exact Count over one relation population.</summary>
public sealed record SubjectRelationPopulationCountRequest;

/// <summary>Opaque receipt for continuing one exact relation population.</summary>
public sealed record SubjectRelationPopulationContinuation
{
    public SubjectRelationPopulationContinuation(InertString value)
    {
        if (value.IsEmpty || value.Length > 256)
        {
            throw new ArgumentException(
                "A relation continuation must contain a bounded value.",
                nameof(value));
        }

        Value = value;
    }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString Value { get; }
}

/// <summary>Request for one bounded relation Rows segment.</summary>
public sealed record SubjectRelationPopulationRowsRequest
{
    public SubjectRelationPopulationRowsRequest(
        int maximumRows,
        SubjectRelationPopulationOrdering ordering =
            SubjectRelationPopulationOrdering.Producer,
        SubjectRelationRowProjection projection =
            SubjectRelationRowProjection.Canonical,
        SubjectRelationPopulationContinuation? continuation = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        if (!Enum.IsDefined(ordering))
            throw new ArgumentOutOfRangeException(nameof(ordering));
        if (!Enum.IsDefined(projection))
            throw new ArgumentOutOfRangeException(nameof(projection));

        MaximumRows = maximumRows;
        Ordering = ordering;
        Projection = projection;
        Continuation = continuation;
    }

    public int MaximumRows { get; }

    public SubjectRelationPopulationOrdering Ordering { get; }

    public SubjectRelationRowProjection Projection { get; }

    public SubjectRelationPopulationContinuation? Continuation { get; }
}

/// <summary>Detached request for one canonical relation population.</summary>
public sealed record SubjectRelationPopulationRequest
{
    public SubjectRelationPopulationRequest(
        SubjectRelationPopulationSelection selection,
        SubjectRelationPopulationCountRequest? count = null,
        SubjectRelationPopulationRowsRequest? rows = null)
    {
        Selection = selection
            ?? throw new ArgumentNullException(nameof(selection));
        if (count is null && rows is null)
        {
            throw new ArgumentException(
                "A relation population must request Count, Rows, or both.");
        }

        Count = count;
        Rows = rows;
    }

    public SubjectRelationPopulationSelection Selection { get; }

    public SubjectRelationPopulationCountRequest? Count { get; }

    public SubjectRelationPopulationRowsRequest? Rows { get; }
}

/// <summary>
/// In-process request pairing detached intent with exact subject authority.
/// </summary>
public sealed class SubjectRelationsInspectionRequest
{
    public SubjectRelationsInspectionRequest(
        SubjectRelationsRouteKind route,
        StructuralSubjectIdentity focus,
        SubjectRelationPopulationAuthority population,
        SubjectRelationPopulationRequest request)
    {
        if (!Enum.IsDefined(route))
            throw new ArgumentOutOfRangeException(nameof(route));
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(request);
        if (!MatchesRoute(route, focus))
        {
            throw new ArgumentException(
                "The relation route does not match the exact subject kind.",
                nameof(focus));
        }
        SubjectRelationFocusCorrespondence.RequireSameWorkspace(
            focus,
            population);

        Route = route;
        Focus = focus;
        Population = population;
        Request = request;
    }

    public SubjectRelationsRouteKind Route { get; }

    public StructuralSubjectIdentity Focus { get; }

    public SubjectRelationPopulationAuthority Population { get; }

    public SubjectRelationPopulationRequest Request { get; }

    private static bool MatchesRoute(
        SubjectRelationsRouteKind route,
        StructuralSubjectIdentity focus) =>
        (route, focus) switch
        {
            (
                SubjectRelationsRouteKind.Package,
                StructuralSubjectIdentity.PackageSubject) =>
                true,
            (
                SubjectRelationsRouteKind.Library,
                StructuralSubjectIdentity.LibrarySubject) =>
                true,
            (
                SubjectRelationsRouteKind.Type,
                StructuralSubjectIdentity.TypeSubject) =>
                true,
            (
                SubjectRelationsRouteKind.Member,
                StructuralSubjectIdentity.MemberSubject) =>
                true,
            _ => false,
        };
}

/// <summary>Exact membership binding shared by requested terminals.</summary>
public sealed record SubjectRelationPopulationBinding
{
    internal SubjectRelationPopulationBinding(
        StructuralSubjectIdentity focus,
        SubjectRelationPopulationAuthority population,
        SubjectRelationPopulationSelection selection)
    {
        Focus = focus;
        Population = population;
        Selection = selection;
    }

    public StructuralSubjectIdentity Focus { get; }

    public SubjectRelationPopulationAuthority Population { get; }

    public SubjectRelationPopulationSelection Selection { get; }
}

/// <summary>
/// Producer-issued process-local authority recovered from one opaque
/// continuation.
/// </summary>
public sealed class SubjectRelationPopulationContinuationAuthority
{
    private SubjectRelationPopulationContinuationAuthority(
        SubjectRelationPopulationContinuation continuation,
        StructuralSubjectIdentity focus,
        SubjectRelationPopulationAuthority population,
        SubjectRelationPopulationSelection selection,
        SubjectRelationPopulationOrdering ordering,
        SubjectRelationRowProjection projection,
        int nextOrdinal)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(ordering))
            throw new ArgumentOutOfRangeException(nameof(ordering));
        if (!Enum.IsDefined(projection))
            throw new ArgumentOutOfRangeException(nameof(projection));
        ArgumentOutOfRangeException.ThrowIfNegative(nextOrdinal);
        SubjectRelationFocusCorrespondence.RequireSameWorkspace(
            focus,
            population);

        Continuation = continuation;
        Focus = focus;
        Population = population;
        Selection = selection;
        Ordering = ordering;
        Projection = projection;
        NextOrdinal = nextOrdinal;
    }

    public SubjectRelationPopulationContinuation Continuation { get; }

    public StructuralSubjectIdentity Focus { get; }

    public SubjectRelationPopulationAuthority Population { get; }

    public SubjectRelationPopulationSelection Selection { get; }

    public SubjectRelationPopulationOrdering Ordering { get; }

    public SubjectRelationRowProjection Projection { get; }

    public int NextOrdinal { get; }

    public static SubjectRelationPopulationContinuationAuthority Capture(
        SubjectRelationPopulationContinuation continuation,
        StructuralSubjectIdentity focus,
        SubjectRelationPopulationAuthority population,
        SubjectRelationPopulationSelection selection,
        SubjectRelationPopulationOrdering ordering,
        SubjectRelationRowProjection projection,
        int nextOrdinal) =>
        new(
            continuation,
            focus,
            population,
            selection,
            ordering,
            projection,
            nextOrdinal);
}

public abstract record SubjectRelationPopulationCountOutcome
{
    private protected SubjectRelationPopulationCountOutcome()
    {
    }

    public sealed record Counted : SubjectRelationPopulationCountOutcome
    {
        public Counted(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Value = value;
        }

        public int Value { get; }
    }

    public sealed record Unavailable :
        SubjectRelationPopulationCountOutcome;

    public sealed record Incomplete :
        SubjectRelationPopulationCountOutcome;

    public sealed record Failed :
        SubjectRelationPopulationCountOutcome;
}

public enum SubjectRelationPopulationRowsRejection
{
    InvalidContinuation,
    IncompatibleContinuation,
    StaleContinuation,
    ContinuationOutOfRange,
}

public abstract record SubjectRelationPopulationRowsOutcome
{
    private protected SubjectRelationPopulationRowsOutcome()
    {
    }

    public sealed record Read : SubjectRelationPopulationRowsOutcome
    {
        public Read(
            SubjectRelationPopulationOrdering ordering,
            ImmutableArray<SubjectRelationRow> items,
            SubjectRelationPopulationContinuation? continuation)
        {
            if (!Enum.IsDefined(ordering))
                throw new ArgumentOutOfRangeException(nameof(ordering));
            if (items.IsDefault
                || items.Any(static item => item is null))
            {
                throw new ArgumentException(
                    "Relation Rows require initialized non-null items.",
                    nameof(items));
            }

            Ordering = ordering;
            Items = items;
            Continuation = continuation;
        }

        public SubjectRelationPopulationOrdering Ordering { get; }

        public ImmutableArray<SubjectRelationRow> Items { get; }

        public SubjectRelationPopulationContinuation? Continuation { get; }
    }

    public sealed record Unavailable :
        SubjectRelationPopulationRowsOutcome;

    public sealed record Rejected(
        SubjectRelationPopulationRowsRejection Reason)
        : SubjectRelationPopulationRowsOutcome;

    public sealed record Incomplete :
        SubjectRelationPopulationRowsOutcome;

    public sealed record Failed :
        SubjectRelationPopulationRowsOutcome;
}

/// <summary>Settled result for one requested relation population.</summary>
public sealed record SubjectRelationPopulationResult(
    SubjectRelationPopulationBinding Binding,
    SubjectRelationPopulationEvidence Evidence,
    SubjectRelationPopulationCountOutcome? Count,
    SubjectRelationPopulationRowsOutcome? Rows);

/// <summary>
/// Settles producer-issued relation terminal outcomes without manufacturing
/// source work, Count, or continuation.
/// </summary>
public static class SubjectRelationsPopulationOperation
{
    public static SubjectRelationPopulationRowsRejection?
        ContinuationRejection(
            SubjectRelationsInspectionRequest request,
            SubjectRelationPopulationContinuationAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authority);
        SubjectRelationPopulationRowsRequest rows =
            request.Request.Rows
            ?? throw new ArgumentException(
                "Continuation validation requires a Rows request.",
                nameof(request));
        if (rows.Continuation is null)
        {
            throw new ArgumentException(
                "Continuation validation requires a continued Rows request.",
                nameof(request));
        }
        if (rows.Continuation != authority.Continuation)
        {
            return SubjectRelationPopulationRowsRejection
                .InvalidContinuation;
        }
        if (!ReferenceEquals(request.Focus, authority.Focus)
            || !ReferenceEquals(
                request.Population,
                authority.Population))
        {
            return SubjectRelationPopulationRowsRejection
                .StaleContinuation;
        }
        if (request.Request.Selection != authority.Selection
            || rows.Ordering != authority.Ordering
            || rows.Projection != authority.Projection)
        {
            return SubjectRelationPopulationRowsRejection
                .IncompatibleContinuation;
        }

        return null;
    }

    public static SubjectRelationPopulationResult Settle(
        SubjectRelationsInspectionRequest request,
        SubjectRelationPopulationEvidence evidence,
        SubjectRelationPopulationCountOutcome? count,
        SubjectRelationPopulationRowsOutcome? rows,
        SubjectRelationPopulationContinuationAuthority?
            continuationAuthority = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidence);
        if (!ReferenceEquals(
                evidence.Population,
                request.Population))
        {
            throw new ArgumentException(
                "Relation evidence must belong to the exact requested "
                + "candidate population.",
                nameof(evidence));
        }
        if ((request.Request.Count is null) != (count is null))
        {
            throw new ArgumentException(
                "The Count outcome must exactly match the requested "
                + "terminal.",
                nameof(count));
        }
        if ((request.Request.Rows is null) != (rows is null))
        {
            throw new ArgumentException(
                "The Rows outcome must exactly match the requested "
                + "terminal.",
                nameof(rows));
        }
        if (count is SubjectRelationPopulationCountOutcome.Counted
            && !evidence.IsComplete)
        {
            throw new ArgumentException(
                "Exact relation Count requires complete producer evidence.",
                nameof(count));
        }
        if (rows is SubjectRelationPopulationRowsOutcome.Read read)
        {
            int startOrdinal = ValidateRows(
                request,
                evidence,
                read,
                continuationAuthority);
            if (count
                    is SubjectRelationPopulationCountOutcome.Counted counted
                && evidence.IsComplete)
            {
                long endOrdinal =
                    (long)startOrdinal + read.Items.Length;
                bool disagreesWithRows =
                    read.Continuation is null
                        ? counted.Value != endOrdinal
                        : counted.Value <= endOrdinal;
                if (disagreesWithRows)
                {
                    throw new ArgumentException(
                        "Exact Count must contain the returned Rows segment "
                        + "and equal the final population ordinal.",
                        nameof(rows));
                }
            }
        }
        else if (continuationAuthority is not null)
        {
            throw new ArgumentException(
                "Continuation authority accompanies only a successful Rows "
                + "read.",
                nameof(continuationAuthority));
        }

        return new(
            new(
                request.Focus,
                request.Population,
                request.Request.Selection),
            evidence,
            count,
            rows);
    }

    private static int ValidateRows(
        SubjectRelationsInspectionRequest request,
        SubjectRelationPopulationEvidence evidence,
        SubjectRelationPopulationRowsOutcome.Read rows,
        SubjectRelationPopulationContinuationAuthority?
            continuationAuthority)
    {
        SubjectRelationPopulationRowsRequest rowRequest =
            request.Request.Rows
            ?? throw new InvalidOperationException(
                "Rows validation requires a Rows request.");
        int startOrdinal;
        if (rowRequest.Continuation is null)
        {
            if (continuationAuthority is not null)
            {
                throw new ArgumentException(
                    "An initial Rows request cannot use continuation "
                    + "authority.",
                    nameof(continuationAuthority));
            }
            startOrdinal = 0;
        }
        else
        {
            if (continuationAuthority is null)
            {
                throw new ArgumentException(
                    "A successful continued Rows read requires "
                    + "producer-resolved continuation authority.",
                    nameof(continuationAuthority));
            }
            if (ContinuationRejection(
                    request,
                    continuationAuthority)
                is { } rejection)
            {
                throw new ArgumentException(
                    $"A successful Rows read used {rejection} authority.",
                    nameof(continuationAuthority));
            }
            startOrdinal = continuationAuthority.NextOrdinal;
        }
        if (rows.Ordering != rowRequest.Ordering)
        {
            throw new ArgumentException(
                "The returned relation ordering does not match the "
                + "request.",
                nameof(rows));
        }
        if (rows.Items.Length > rowRequest.MaximumRows)
        {
            throw new ArgumentException(
                "The returned relation segment exceeds its requested "
                + "maximum.",
                nameof(rows));
        }
        if (!evidence.HasUsableRows && !rows.Items.IsEmpty)
        {
            throw new ArgumentException(
                "Unavailable relation producers cannot publish rows.",
                nameof(rows));
        }

        HashSet<InspectionGraphRelationshipDescriptor> relationships =
            evidence.Producers
                .Where(static producer =>
                    producer.Disposition is
                        SubjectRelationProducerDisposition.Complete
                        or SubjectRelationProducerDisposition.Partial)
                .SelectMany(static producer => producer.Relationships)
                .ToHashSet<InspectionGraphRelationshipDescriptor>(
                    ReferenceEqualityComparer.Instance);
        foreach (SubjectRelationRow row in rows.Items)
        {
            if (!ReferenceEquals(
                    row.Correspondence.Focus,
                    request.Focus)
                || !ReferenceEquals(
                    row.Correspondence.Population,
                    request.Population)
                || !relationships.Contains(row.Relationship)
                || !request.Request.Selection.Matches(row))
            {
                throw new ArgumentException(
                    "A returned relation row does not match the exact focus, "
                    + "population, producer evidence, or canonical selection.",
                    nameof(rows));
            }
        }

        return startOrdinal;
    }
}
