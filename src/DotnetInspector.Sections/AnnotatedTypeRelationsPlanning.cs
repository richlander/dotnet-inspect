using System.Collections.Immutable;

using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public enum AnnotatedTypeRelationsTerminal
{
    Rows,
    Count,
    Exists,
}

public sealed class AnnotatedTypeRelationsAdapterQueries
{
    public AnnotatedTypeRelationsAdapterQueries(
        InspectionQueryDefinition? rows = null,
        InspectionQueryDefinition? count = null,
        InspectionQueryDefinition? exists = null)
    {
        if (rows is null && count is null && exists is null)
        {
            throw new ArgumentException(
                "At least one terminal adapter query is required.");
        }

        Rows = rows;
        Count = count;
        Exists = exists;
    }

    public InspectionQueryDefinition? Rows { get; }

    public InspectionQueryDefinition? Count { get; }

    public InspectionQueryDefinition? Exists { get; }

    public InspectionQueryDefinition? For(
        AnnotatedTypeRelationsTerminal terminal) =>
        terminal switch
        {
            AnnotatedTypeRelationsTerminal.Rows => Rows,
            AnnotatedTypeRelationsTerminal.Count => Count,
            AnnotatedTypeRelationsTerminal.Exists => Exists,
            _ => throw new ArgumentOutOfRangeException(nameof(terminal)),
        };
}

public sealed class AnnotatedTypeRelationsRelationshipRegistration
{
    private readonly AnnotatedTypeRelationsDirectionSupport _directionSupport;

    public AnnotatedTypeRelationsRelationshipRegistration(
        SubjectRelationForm form,
        InspectionGraphRelationshipDescriptor relationship,
        AnnotatedTypeRelationsAdapterQueries queries)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(queries);

        if (!Enum.IsDefined(form))
        {
            throw new ArgumentOutOfRangeException(nameof(form));
        }

        AnnotatedTypeRelationsDirectionSupport directionSupport =
            AnnotatedTypeRelationsDirectionSupport.None;
        foreach (InspectionGraphSeedAdmission admission in
            relationship.GetSeedAdmissions(
                InspectionGraphSubjectKind.Type))
        {
            directionSupport |= admission.Role switch
            {
                InspectionGraphEndpointRole.Source =>
                    AnnotatedTypeRelationsDirectionSupport.Outgoing,
                InspectionGraphEndpointRole.Target =>
                    AnnotatedTypeRelationsDirectionSupport.Incoming,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(relationship)),
            };
        }

        if (directionSupport == AnnotatedTypeRelationsDirectionSupport.None)
        {
            throw new ArgumentException(
                $"Relationship '{relationship.Id}' does not admit a Type seed.",
                nameof(relationship));
        }

        Form = form;
        Relationship = relationship;
        Queries = queries;
        _directionSupport = directionSupport;
    }

    public SubjectRelationForm Form { get; }

    public InspectionGraphRelationshipDescriptor Relationship { get; }

    public AnnotatedTypeRelationsAdapterQueries Queries { get; }

    public bool Supports(SubjectRelationDirectionSelection direction) =>
        direction switch
        {
            SubjectRelationDirectionSelection.Incoming =>
                (_directionSupport &
                    AnnotatedTypeRelationsDirectionSupport.Incoming) != 0,
            SubjectRelationDirectionSelection.Outgoing =>
                (_directionSupport &
                    AnnotatedTypeRelationsDirectionSupport.Outgoing) != 0,
            SubjectRelationDirectionSelection.Both =>
                _directionSupport != AnnotatedTypeRelationsDirectionSupport.None,
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
}

public sealed class AnnotatedTypeRelationsColorRegistration
{
    public AnnotatedTypeRelationsColorRegistration(
        InspectionGraphCharacteristicDescriptor color,
        AnnotatedTypeRelationsAdapterQueries queries)
    {
        ArgumentNullException.ThrowIfNull(color);
        ArgumentNullException.ThrowIfNull(queries);

        Color = color;
        Queries = queries;
    }

    public InspectionGraphCharacteristicDescriptor Color { get; }

    public AnnotatedTypeRelationsAdapterQueries Queries { get; }
}

public sealed class AnnotatedTypeRelationsRegistry
{
    private readonly List<AnnotatedTypeRelationsRelationshipRegistration>
        _relationships = [];
    private readonly List<AnnotatedTypeRelationsColorRegistration> _colors = [];

    public AnnotatedTypeRelationsRegistry AddRelationship(
        AnnotatedTypeRelationsRelationshipRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        RejectRepeatedRelationship(registration.Relationship);
        _relationships.Add(registration);
        return this;
    }

    public AnnotatedTypeRelationsRegistry AddColor(
        AnnotatedTypeRelationsColorRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        RejectRepeatedColor(registration.Color);
        _colors.Add(registration);
        return this;
    }

    public AnnotatedTypeRelationsCatalog Compile() =>
        new([.. _relationships], [.. _colors]);

    private void RejectRepeatedRelationship(
        InspectionGraphRelationshipDescriptor relationship)
    {
        foreach (AnnotatedTypeRelationsRelationshipRegistration existing in
            _relationships)
        {
            if (ReferenceEquals(existing.Relationship, relationship))
            {
                throw new ArgumentException(
                    $"Relationship '{relationship.Id}' is already registered.",
                    nameof(relationship));
            }

            if (StringComparer.Ordinal.Equals(
                existing.Relationship.Id,
                relationship.Id))
            {
                throw new ArgumentException(
                    $"Relationship id '{relationship.Id}' is already registered.",
                    nameof(relationship));
            }
        }
    }

    private void RejectRepeatedColor(
        InspectionGraphCharacteristicDescriptor color)
    {
        foreach (AnnotatedTypeRelationsColorRegistration existing in _colors)
        {
            if (ReferenceEquals(existing.Color, color))
            {
                throw new ArgumentException(
                    $"Color '{color.Id}' is already registered.",
                    nameof(color));
            }

            if (StringComparer.Ordinal.Equals(existing.Color.Id, color.Id))
            {
                throw new ArgumentException(
                    $"Color id '{color.Id}' is already registered.",
                    nameof(color));
            }
        }
    }
}

public sealed class AnnotatedTypeRelationsPlanningRequest
{
    public AnnotatedTypeRelationsPlanningRequest(
        SubjectRelationFocusAuthority focus,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        SubjectRelationDirectionSelection direction,
        IEnumerable<InspectionGraphCharacteristicDescriptor>? colors,
        IEnumerable<InspectionGraphCharacteristicDescriptor>? requiredColors,
        AnnotatedTypeRelationsTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(relationships);

        if (focus.Kind != StructuralSubjectKind.Type)
        {
            throw new ArgumentException(
                "Annotated Type Relations planning requires an exact Type focus.",
                nameof(focus));
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (!Enum.IsDefined(terminal))
        {
            throw new ArgumentOutOfRangeException(nameof(terminal));
        }

        ImmutableArray<InspectionGraphRelationshipDescriptor>
            relationshipSnapshot = [.. relationships];
        if (relationshipSnapshot.IsEmpty)
        {
            throw new ArgumentException(
                "At least one relationship descriptor is required.",
                nameof(relationships));
        }

        ImmutableArray<InspectionGraphCharacteristicDescriptor> colorSnapshot =
            colors is null ? [] : [.. colors];
        ImmutableArray<InspectionGraphCharacteristicDescriptor>
            requiredColorSnapshot =
                requiredColors is null ? [] : [.. requiredColors];

        RejectNullOrRepeated(
            relationshipSnapshot,
            static descriptor => descriptor.Id,
            nameof(relationships));
        RejectNullOrRepeated(
            colorSnapshot,
            static descriptor => descriptor.Id,
            nameof(colors));
        RejectNullOrRepeated(
            requiredColorSnapshot,
            static descriptor => descriptor.Id,
            nameof(requiredColors));

        var selectedColors = new HashSet<object>(
            colorSnapshot.Cast<object>(),
            ReferenceEqualityComparer.Instance);
        foreach (InspectionGraphCharacteristicDescriptor color in
            requiredColorSnapshot)
        {
            if (!selectedColors.Contains(color))
            {
                throw new ArgumentException(
                    $"Required color '{color.Id}' is not selected.",
                    nameof(requiredColors));
            }
        }

        Focus = focus;
        Relationships = relationshipSnapshot;
        Direction = direction;
        Colors = colorSnapshot;
        RequiredColors = requiredColorSnapshot;
        Terminal = terminal;
    }

    public SubjectRelationFocusAuthority Focus { get; }

    public ImmutableArray<InspectionGraphRelationshipDescriptor>
        Relationships
    { get; }

    public SubjectRelationDirectionSelection Direction { get; }

    public ImmutableArray<InspectionGraphCharacteristicDescriptor> Colors
    {
        get;
    }

    public ImmutableArray<InspectionGraphCharacteristicDescriptor>
        RequiredColors
    { get; }

    public AnnotatedTypeRelationsTerminal Terminal { get; }

    private static void RejectNullOrRepeated<T>(
        ImmutableArray<T> values,
        Func<T, string> describe,
        string parameterName)
        where T : class
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (T? value in values)
        {
            if (value is null)
            {
                throw new ArgumentException(
                    "Descriptor selections cannot contain null.",
                    parameterName);
            }

            if (!seen.Add(value))
            {
                throw new ArgumentException(
                    $"Descriptor '{describe(value)}' is selected more than once.",
                    parameterName);
            }
        }
    }
}

public enum AnnotatedTypeRelationsPlanningFailureKind
{
    UnknownRelationship,
    UnknownColor,
    UnsupportedDirection,
    UnsupportedTerminal,
    QueryUnavailable,
}

public sealed record AnnotatedTypeRelationsPlanningFailure(
    AnnotatedTypeRelationsPlanningFailureKind Kind,
    string DescriptorId,
    string Message);

public abstract record AnnotatedTypeRelationsPlanningResult
{
    private AnnotatedTypeRelationsPlanningResult()
    {
    }

    public sealed record Accepted(AnnotatedTypeRelationsPlan Plan)
        : AnnotatedTypeRelationsPlanningResult;

    public sealed record Rejected(AnnotatedTypeRelationsPlanningFailure Failure)
        : AnnotatedTypeRelationsPlanningResult;
}

public sealed class AnnotatedTypeRelationsPlan
{
    internal AnnotatedTypeRelationsPlan(
        AnnotatedTypeRelationsPlanningRequest request,
        ImmutableArray<AnnotatedTypeRelationsRelationshipRegistration>
            relationships,
        ImmutableArray<AnnotatedTypeRelationsColorRegistration> colors,
        ImmutableArray<InspectionQueryDefinition> queries,
        InspectionCost cost)
    {
        Request = request;
        Relationships = relationships;
        Colors = colors;
        Queries = queries;
        Cost = cost;
    }

    public AnnotatedTypeRelationsPlanningRequest Request { get; }

    public ImmutableArray<AnnotatedTypeRelationsRelationshipRegistration>
        Relationships
    { get; }

    public ImmutableArray<AnnotatedTypeRelationsColorRegistration> Colors
    {
        get;
    }

    public ImmutableArray<InspectionQueryDefinition> Queries { get; }

    public InspectionCost Cost { get; }
}

public sealed class AnnotatedTypeRelationsCatalog
{
    private readonly ImmutableArray<
        AnnotatedTypeRelationsRelationshipRegistration> _relationships;
    private readonly ImmutableArray<AnnotatedTypeRelationsColorRegistration>
        _colors;

    internal AnnotatedTypeRelationsCatalog(
        ImmutableArray<AnnotatedTypeRelationsRelationshipRegistration>
            relationships,
        ImmutableArray<AnnotatedTypeRelationsColorRegistration> colors)
    {
        _relationships = relationships;
        _colors = colors;
    }

    public AnnotatedTypeRelationsPlanningResult Resolve(
        AnnotatedTypeRelationsPlanningRequest request,
        IInspectionQueryCatalog queryCatalog)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(queryCatalog);

        var requestedRelationships = new HashSet<object>(
            request.Relationships.Cast<object>(),
            ReferenceEqualityComparer.Instance);
        var requestedColors = new HashSet<object>(
            request.Colors.Cast<object>(),
            ReferenceEqualityComparer.Instance);

        foreach (InspectionGraphRelationshipDescriptor descriptor in
            request.Relationships)
        {
            if (!ContainsExactRelationship(descriptor))
            {
                return Reject(
                    AnnotatedTypeRelationsPlanningFailureKind
                        .UnknownRelationship,
                    descriptor.Id,
                    $"Relationship '{descriptor.Id}' is not registered.");
            }
        }

        foreach (InspectionGraphCharacteristicDescriptor descriptor in
            request.Colors)
        {
            if (!ContainsExactColor(descriptor))
            {
                return Reject(
                    AnnotatedTypeRelationsPlanningFailureKind.UnknownColor,
                    descriptor.Id,
                    $"Color '{descriptor.Id}' is not registered.");
            }
        }

        ImmutableArray<AnnotatedTypeRelationsRelationshipRegistration>
            relationships =
                [.. _relationships.Where(
                    registration =>
                        requestedRelationships.Contains(
                            registration.Relationship))];
        ImmutableArray<AnnotatedTypeRelationsColorRegistration> colors =
            [.. _colors.Where(
                registration => requestedColors.Contains(registration.Color))];
        var queries = ImmutableArray.CreateBuilder<InspectionQueryDefinition>();
        var seenQueries = new HashSet<object>(
            ReferenceEqualityComparer.Instance);
        InspectionCost cost = InspectionCost.NetworkFree;

        foreach (AnnotatedTypeRelationsRelationshipRegistration registration in
            relationships)
        {
            if (!registration.Supports(request.Direction))
            {
                return Reject(
                    AnnotatedTypeRelationsPlanningFailureKind
                        .UnsupportedDirection,
                    registration.Relationship.Id,
                    $"Relationship '{registration.Relationship.Id}' does not " +
                    $"support {request.Direction} Type incidence.");
            }

            AnnotatedTypeRelationsPlanningResult.Rejected? failure =
                AddTerminalQuery(
                    registration.Relationship.Id,
                    registration.Queries,
                    request.Terminal,
                    queryCatalog,
                    queries,
                    seenQueries,
                    ref cost);
            if (failure is not null)
            {
                return failure;
            }
        }

        foreach (AnnotatedTypeRelationsColorRegistration registration in colors)
        {
            AnnotatedTypeRelationsPlanningResult.Rejected? failure =
                AddTerminalQuery(
                    registration.Color.Id,
                    registration.Queries,
                    request.Terminal,
                    queryCatalog,
                    queries,
                    seenQueries,
                    ref cost);
            if (failure is not null)
            {
                return failure;
            }
        }

        return new AnnotatedTypeRelationsPlanningResult.Accepted(
            new(
                request,
                relationships,
                colors,
                queries.ToImmutable(),
                cost));
    }

    private static AnnotatedTypeRelationsPlanningResult.Rejected?
        AddTerminalQuery(
            string descriptorId,
            AnnotatedTypeRelationsAdapterQueries terminalQueries,
            AnnotatedTypeRelationsTerminal terminal,
            IInspectionQueryCatalog queryCatalog,
            ImmutableArray<InspectionQueryDefinition>.Builder queries,
            HashSet<object> seenQueries,
            ref InspectionCost cost)
    {
        InspectionQueryDefinition? query = terminalQueries.For(terminal);
        if (query is null)
        {
            return Reject(
                AnnotatedTypeRelationsPlanningFailureKind.UnsupportedTerminal,
                descriptorId,
                $"Descriptor '{descriptorId}' does not support {terminal}.");
        }

        if (!queryCatalog.Contains(query))
        {
            return Reject(
                AnnotatedTypeRelationsPlanningFailureKind.QueryUnavailable,
                descriptorId,
                $"Adapter query '{query.Name}' for '{descriptorId}' is not " +
                "registered in the inspection query catalog.");
        }

        if (seenQueries.Add(query))
        {
            queries.Add(query);
            InspectionCost queryCost = queryCatalog.CostOf(query);
            if (queryCost > cost)
            {
                cost = queryCost;
            }
        }

        return null;
    }

    private bool ContainsExactRelationship(
        InspectionGraphRelationshipDescriptor descriptor) =>
        _relationships.Any(
            registration =>
                ReferenceEquals(registration.Relationship, descriptor));

    private bool ContainsExactColor(
        InspectionGraphCharacteristicDescriptor descriptor) =>
        _colors.Any(
            registration => ReferenceEquals(registration.Color, descriptor));

    private static AnnotatedTypeRelationsPlanningResult.Rejected Reject(
        AnnotatedTypeRelationsPlanningFailureKind kind,
        string descriptorId,
        string message) =>
        new(new(kind, descriptorId, message));
}

[Flags]
internal enum AnnotatedTypeRelationsDirectionSupport
{
    None = 0,
    Incoming = 1,
    Outgoing = 2,
}
