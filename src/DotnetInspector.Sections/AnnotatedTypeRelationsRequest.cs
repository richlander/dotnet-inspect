using System.Collections.Immutable;

using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public enum AnnotatedTypeRelationsFamilySelectionKind
{
    AllApplicable,
    Explicit,
}

public sealed class AnnotatedTypeRelationsFamilySelection
{
    private AnnotatedTypeRelationsFamilySelection(
        AnnotatedTypeRelationsFamilySelectionKind kind,
        ImmutableArray<string> relationshipIds)
    {
        Kind = kind;
        RelationshipIds = relationshipIds;
    }

    public AnnotatedTypeRelationsFamilySelectionKind Kind { get; }

    public ImmutableArray<string> RelationshipIds { get; }

    public static AnnotatedTypeRelationsFamilySelection AllApplicable { get; } =
        new(AnnotatedTypeRelationsFamilySelectionKind.AllApplicable, []);

    public static AnnotatedTypeRelationsFamilySelection Explicit(
        IEnumerable<string> relationshipIds)
    {
        ArgumentNullException.ThrowIfNull(relationshipIds);
        ImmutableArray<string> snapshot = [.. relationshipIds];
        if (snapshot.IsEmpty)
        {
            throw new ArgumentException(
                "An explicit relationship selection cannot be empty.",
                nameof(relationshipIds));
        }

        ValidateIds(snapshot, nameof(relationshipIds));
        return new(
            AnnotatedTypeRelationsFamilySelectionKind.Explicit,
            snapshot);
    }

    internal static void ValidateIds(
        ImmutableArray<string> ids,
        string parameterName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "Descriptor ids cannot be null or whitespace.",
                    parameterName);
            }

            if (!seen.Add(id))
            {
                throw new ArgumentException(
                    $"Descriptor id '{id}' is selected more than once.",
                    parameterName);
            }
        }
    }
}

public sealed class AnnotatedTypeRelationsRequest
{
    public AnnotatedTypeRelationsRequest(
        SubjectRelationFocusAuthority focus,
        AnnotatedTypeRelationsFamilySelection relationships,
        SubjectRelationDirectionSelection direction,
        IEnumerable<string>? colorIds,
        IEnumerable<string>? requiredColorIds,
        AnnotatedTypeRelationsTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(relationships);
        if (focus.Kind != StructuralSubjectKind.Type)
        {
            throw new ArgumentException(
                "Annotated Type Relations requires an exact Type focus.",
                nameof(focus));
        }
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (!Enum.IsDefined(terminal))
            throw new ArgumentOutOfRangeException(nameof(terminal));

        ImmutableArray<string> colors = [.. colorIds ?? []];
        ImmutableArray<string> requiredColors = [.. requiredColorIds ?? []];
        AnnotatedTypeRelationsFamilySelection.ValidateIds(
            colors,
            nameof(colorIds));
        AnnotatedTypeRelationsFamilySelection.ValidateIds(
            requiredColors,
            nameof(requiredColorIds));

        var selectedColors = new HashSet<string>(
            colors,
            StringComparer.Ordinal);
        foreach (string requiredColor in requiredColors)
        {
            if (!selectedColors.Contains(requiredColor))
            {
                throw new ArgumentException(
                    $"Required color '{requiredColor}' is not selected.",
                    nameof(requiredColorIds));
            }
        }

        Focus = focus;
        Relationships = relationships;
        Direction = direction;
        ColorIds = colors;
        RequiredColorIds = requiredColors;
        Terminal = terminal;
    }

    public SubjectRelationFocusAuthority Focus { get; }

    public AnnotatedTypeRelationsFamilySelection Relationships { get; }

    public SubjectRelationDirectionSelection Direction { get; }

    public ImmutableArray<string> ColorIds { get; }

    public ImmutableArray<string> RequiredColorIds { get; }

    public AnnotatedTypeRelationsTerminal Terminal { get; }
}

public enum AnnotatedTypeRelationsRequestFailureKind
{
    NoApplicableRelationships,
    UnknownRelationship,
    UnknownColor,
    PlanningRejected,
}

public sealed record AnnotatedTypeRelationsRequestFailure(
    AnnotatedTypeRelationsRequestFailureKind Kind,
    string? DescriptorId,
    string Message,
    AnnotatedTypeRelationsPlanningFailure? PlanningFailure = null);

public abstract record AnnotatedTypeRelationsRequestResult
{
    private AnnotatedTypeRelationsRequestResult()
    {
    }

    public sealed record Accepted(
        AnnotatedTypeRelationsRequest Request,
        ImmutableArray<string> ResolvedRelationshipIds,
        ImmutableArray<string> ResolvedColorIds,
        ImmutableArray<string> ResolvedRequiredColorIds,
        AnnotatedTypeRelationsPlan Plan)
        : AnnotatedTypeRelationsRequestResult;

    public sealed record Rejected(
        AnnotatedTypeRelationsRequestFailure Failure)
        : AnnotatedTypeRelationsRequestResult;
}

public static class AnnotatedTypeRelationsRequestResolver
{
    public static AnnotatedTypeRelationsRequestResult Resolve(
        AnnotatedTypeRelationsCatalog relationsCatalog,
        AnnotatedTypeRelationsRequest request,
        IInspectionQueryCatalog queryCatalog)
    {
        ArgumentNullException.ThrowIfNull(relationsCatalog);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(queryCatalog);

        ImmutableArray<AnnotatedTypeRelationsRelationshipRegistration>
            relationships = ResolveRelationships(relationsCatalog, request);
        if (relationships.IsDefault)
        {
            string unknownId = request.Relationships.RelationshipIds.First(
                id => !relationsCatalog.Relationships.Any(
                    registration => StringComparer.Ordinal.Equals(
                        registration.Relationship.Id,
                        id)));
            return Reject(
                AnnotatedTypeRelationsRequestFailureKind.UnknownRelationship,
                unknownId,
                $"Relationship '{unknownId}' is not registered.");
        }
        if (relationships.IsEmpty)
        {
            return Reject(
                AnnotatedTypeRelationsRequestFailureKind
                    .NoApplicableRelationships,
                null,
                $"No registered relationship supports {request.Direction} "
                    + "Type incidence.");
        }

        if (!TryResolveColors(
                relationsCatalog,
                request.ColorIds,
                out ImmutableArray<AnnotatedTypeRelationsColorRegistration>
                    colors,
                out string? unknownColor))
        {
            return Reject(
                AnnotatedTypeRelationsRequestFailureKind.UnknownColor,
                unknownColor,
                $"Color '{unknownColor}' is not registered.");
        }

        var colorsById = colors.ToDictionary(
            registration => registration.Color.Id,
            StringComparer.Ordinal);
        ImmutableArray<InspectionGraphCharacteristicDescriptor>
            requiredColors =
            [
                .. request.RequiredColorIds.Select(
                    id => colorsById[id].Color),
            ];
        var planningRequest = new AnnotatedTypeRelationsPlanningRequest(
            request.Focus,
            relationships.Select(registration => registration.Relationship),
            request.Direction,
            colors.Select(registration => registration.Color),
            requiredColors,
            request.Terminal);

        AnnotatedTypeRelationsPlanningResult planning =
            relationsCatalog.Resolve(planningRequest, queryCatalog);
        if (planning is AnnotatedTypeRelationsPlanningResult.Rejected rejected)
        {
            return new AnnotatedTypeRelationsRequestResult.Rejected(
                new(
                    AnnotatedTypeRelationsRequestFailureKind.PlanningRejected,
                    rejected.Failure.DescriptorId,
                    rejected.Failure.Message,
                    rejected.Failure));
        }

        AnnotatedTypeRelationsPlan plan =
            ((AnnotatedTypeRelationsPlanningResult.Accepted)planning).Plan;
        return new AnnotatedTypeRelationsRequestResult.Accepted(
            request,
            [.. plan.Relationships.Select(
                registration => registration.Relationship.Id)],
            [.. plan.Colors.Select(registration => registration.Color.Id)],
            [.. requiredColors.Select(color => color.Id)],
            plan);
    }

    private static ImmutableArray<
        AnnotatedTypeRelationsRelationshipRegistration> ResolveRelationships(
            AnnotatedTypeRelationsCatalog catalog,
            AnnotatedTypeRelationsRequest request)
    {
        if (request.Relationships.Kind
            == AnnotatedTypeRelationsFamilySelectionKind.AllApplicable)
        {
            return
            [
                .. catalog.Relationships.Where(
                    registration => registration.Supports(request.Direction)),
            ];
        }

        var requested = new HashSet<string>(
            request.Relationships.RelationshipIds,
            StringComparer.Ordinal);
        if (requested.Any(id => !catalog.Relationships.Any(
                registration => StringComparer.Ordinal.Equals(
                    registration.Relationship.Id,
                    id))))
        {
            return default;
        }

        return
        [
            .. catalog.Relationships.Where(
                registration => requested.Contains(
                    registration.Relationship.Id)),
        ];
    }

    private static bool TryResolveColors(
        AnnotatedTypeRelationsCatalog catalog,
        ImmutableArray<string> requestedIds,
        out ImmutableArray<AnnotatedTypeRelationsColorRegistration> colors,
        out string? unknownId)
    {
        var requested = new HashSet<string>(
            requestedIds,
            StringComparer.Ordinal);
        unknownId = requestedIds.FirstOrDefault(
            id => !catalog.Colors.Any(
                registration => StringComparer.Ordinal.Equals(
                    registration.Color.Id,
                    id)));
        if (unknownId is not null)
        {
            colors = default;
            return false;
        }

        colors =
        [
            .. catalog.Colors.Where(
                registration => requested.Contains(registration.Color.Id)),
        ];
        return true;
    }

    private static AnnotatedTypeRelationsRequestResult.Rejected Reject(
        AnnotatedTypeRelationsRequestFailureKind kind,
        string? descriptorId,
        string message) =>
        new(new(kind, descriptorId, message));
}
