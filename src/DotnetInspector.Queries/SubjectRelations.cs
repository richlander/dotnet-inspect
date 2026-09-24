using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>How one canonical relation is expressed.</summary>
public enum SubjectRelationForm
{
    Interface,
    BaseType,
    Extension,
    Signature,
    Exception,
    Invocation,
    ObjectCreation,
    AssemblyReference,
    PackageDependency,
    Pattern,
}

/// <summary>Incidence of one relation relative to the focused subject.</summary>
public enum SubjectRelationDirection
{
    Incoming,
    Outgoing,
}

/// <summary>How a producer established one relation.</summary>
public enum SubjectRelationEvidenceKind
{
    Declaration,
    StaticIlObservation,
    BoundedPatternCandidate,
    InferredOpportunity,
}

/// <summary>The completion state of one relation producer.</summary>
public enum SubjectRelationProducerDisposition
{
    Complete,
    Partial,
    Unavailable,
    Failed,
}

/// <summary>The kind of one producer-owned completion diagnostic.</summary>
public enum SubjectRelationProducerDiagnosticKind
{
    Limit,
    Failure,
}

/// <summary>
/// Process-local authority for one exact candidate population.
/// </summary>
public abstract class SubjectRelationPopulationAuthority
{
    private protected SubjectRelationPopulationAuthority(
        StructuralSubjectIdentity.WorkspaceSubject workspace)
    {
        Workspace = workspace
            ?? throw new ArgumentNullException(nameof(workspace));
    }

    public StructuralSubjectIdentity.WorkspaceSubject Workspace { get; }

    public static SubjectRelationPopulationAuthority<TIdentity> Capture<
        TIdentity>(
            StructuralSubjectIdentity.WorkspaceSubject workspace,
            TIdentity identity)
        where TIdentity : notnull =>
        new(workspace, identity);
}

/// <summary>Typed owner authority for one candidate population.</summary>
public sealed class SubjectRelationPopulationAuthority<TIdentity> :
    SubjectRelationPopulationAuthority
    where TIdentity : notnull
{
    internal SubjectRelationPopulationAuthority(
        StructuralSubjectIdentity.WorkspaceSubject workspace,
        TIdentity identity)
        : base(workspace)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Value = identity;
    }

    public TIdentity Value { get; }
}

/// <summary>
/// Owner-issued evidence relating one focus and candidate population to one
/// graph endpoint.
/// </summary>
public abstract class SubjectRelationFocusCorrespondence
{
    private protected SubjectRelationFocusCorrespondence(
        StructuralSubjectIdentity focus,
        SubjectRelationPopulationAuthority population,
        InspectionGraphSubject endpoint,
        InspectionGraphEndpointRole role)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        RequireSameWorkspace(focus, population);

        Focus = focus;
        Population = population;
        Endpoint = endpoint;
        Role = role;
    }

    public StructuralSubjectIdentity Focus { get; }

    public SubjectRelationPopulationAuthority Population { get; }

    public InspectionGraphSubject Endpoint { get; }

    public InspectionGraphEndpointRole Role { get; }

    public abstract object Evidence { get; }

    public static SubjectRelationFocusCorrespondence<TEvidence> Create<
        TEvidence>(
            StructuralSubjectIdentity focus,
            SubjectRelationPopulationAuthority population,
            InspectionGraphSubject endpoint,
            InspectionGraphEndpointRole role,
            TEvidence evidence)
        where TEvidence : notnull =>
        new(focus, population, endpoint, role, evidence);

    internal static void RequireSameWorkspace(
        StructuralSubjectIdentity focus,
        SubjectRelationPopulationAuthority population)
    {
        if (!ReferenceEquals(
                focus.Workspace.Identity,
                population.Workspace.Identity))
        {
            throw new ArgumentException(
                "The focused subject and candidate population must belong "
                + "to the exact same Workspace.",
                nameof(population));
        }
    }
}

/// <summary>One typed focus/population correspondence.</summary>
public sealed class SubjectRelationFocusCorrespondence<TEvidence> :
    SubjectRelationFocusCorrespondence
    where TEvidence : notnull
{
    internal SubjectRelationFocusCorrespondence(
        StructuralSubjectIdentity focus,
        SubjectRelationPopulationAuthority population,
        InspectionGraphSubject endpoint,
        InspectionGraphEndpointRole role,
        TEvidence evidence)
        : base(focus, population, endpoint, role)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Value = evidence;
    }

    public TEvidence Value { get; }

    public override object Evidence => Value;
}

/// <summary>
/// One producer-issued Integration classification attached to a relation.
/// </summary>
public abstract class SubjectRelationIntegrationAssociation
{
    private protected SubjectRelationIntegrationAssociation(
        IntegrationConceptDescriptor concept,
        IntegrationProducerPolicyDescriptor producerPolicy,
        WorkspaceEcosystemRegistrationId? ecosystem)
    {
        ArgumentNullException.ThrowIfNull(concept);
        ArgumentNullException.ThrowIfNull(producerPolicy);
        if (!producerPolicy.Concepts.Any(
                candidate => ReferenceEquals(candidate, concept)))
        {
            throw new ArgumentException(
                "The Integration producer policy does not declare the "
                + "associated concept.",
                nameof(concept));
        }

        Concept = concept;
        ProducerPolicy = producerPolicy;
        Ecosystem = ecosystem;
    }

    public IntegrationConceptDescriptor Concept { get; }

    public IntegrationProducerPolicyDescriptor ProducerPolicy { get; }

    public WorkspaceEcosystemRegistrationId? Ecosystem { get; }

    public abstract object Evidence { get; }

    public static SubjectRelationIntegrationAssociation<TEvidence> Create<
        TEvidence>(
            IntegrationConceptDescriptor concept,
            IntegrationProducerPolicyDescriptor producerPolicy,
            WorkspaceEcosystemRegistrationId? ecosystem,
            TEvidence evidence)
        where TEvidence : notnull =>
        new(concept, producerPolicy, ecosystem, evidence);
}

/// <summary>One typed Integration association evidence value.</summary>
public sealed class SubjectRelationIntegrationAssociation<TEvidence> :
    SubjectRelationIntegrationAssociation
    where TEvidence : notnull
{
    internal SubjectRelationIntegrationAssociation(
        IntegrationConceptDescriptor concept,
        IntegrationProducerPolicyDescriptor producerPolicy,
        WorkspaceEcosystemRegistrationId? ecosystem,
        TEvidence evidence)
        : base(concept, producerPolicy, ecosystem)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Value = evidence;
    }

    public TEvidence Value { get; }

    public override object Evidence => Value;
}

/// <summary>
/// One graph occurrence together with its producer-defined identity.
/// </summary>
public sealed class SubjectRelationOccurrence
{
    internal SubjectRelationOccurrence(
        InspectionGraphOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        Occurrence = occurrence;
        Identity = occurrence.Relationship.OccurrenceIdentity.Project(
            occurrence);
        if (Identity is null)
        {
            throw new ArgumentException(
                "A relation occurrence identity cannot be null.",
                nameof(occurrence));
        }
    }

    public object Identity { get; }

    public InspectionGraphOccurrence Occurrence { get; }
}

/// <summary>One canonical logical relation.</summary>
public sealed class SubjectRelationRow
{
    public SubjectRelationRow(
        SubjectRelationForm form,
        SubjectRelationEvidenceKind evidenceKind,
        InspectionGraphRelationshipDescriptor relationship,
        InspectionGraphSubject source,
        InspectionGraphSubject target,
        SubjectRelationFocusCorrespondence correspondence,
        IEnumerable<InspectionGraphOccurrence> occurrences,
        IEnumerable<SubjectRelationIntegrationAssociation>?
            integrationAssociations = null)
    {
        if (!Enum.IsDefined(form))
            throw new ArgumentOutOfRangeException(nameof(form));
        if (!Enum.IsDefined(evidenceKind))
            throw new ArgumentOutOfRangeException(nameof(evidenceKind));
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(correspondence);
        ArgumentNullException.ThrowIfNull(occurrences);

        if (!relationship.AdmitsEdgeSource(source)
            || !relationship.AdmitsEdgeTarget(target))
        {
            throw new ArgumentException(
                "The relation endpoints do not match the relationship "
                + "descriptor.",
                nameof(relationship));
        }

        InspectionGraphSubject expectedFocusEndpoint =
            correspondence.Role switch
            {
                InspectionGraphEndpointRole.Source => source,
                InspectionGraphEndpointRole.Target => target,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(correspondence)),
            };
        if (correspondence.Endpoint != expectedFocusEndpoint)
        {
            throw new ArgumentException(
                "The focus correspondence must identify the relation "
                + "endpoint selected by its semantic role.",
                nameof(correspondence));
        }

        InspectionGraphOccurrence[] occurrenceCopy = [.. occurrences];
        if (occurrenceCopy.Any(static occurrence => occurrence is null))
        {
            throw new ArgumentException(
                "Relation occurrences cannot contain null.",
                nameof(occurrences));
        }
        if (relationship.Semantics
                != InspectionGraphRelationshipSemantics.Synthetic
            && occurrenceCopy.Length == 0)
        {
            throw new ArgumentException(
                "A non-synthetic relation requires producer evidence.",
                nameof(occurrences));
        }
        if (relationship.Semantics
                == InspectionGraphRelationshipSemantics.Synthetic
            && occurrenceCopy.Length != 0)
        {
            throw new ArgumentException(
                "A synthetic relation cannot carry occurrences.",
                nameof(occurrences));
        }

        var occurrenceRows =
            new SubjectRelationOccurrence[occurrenceCopy.Length];
        for (int index = 0; index < occurrenceCopy.Length; index++)
        {
            InspectionGraphOccurrence occurrence = occurrenceCopy[index];
            if (!ReferenceEquals(
                    occurrence.Relationship,
                    relationship)
                || !relationship.AdmitsEvidence(occurrence.Evidence)
                || !relationship.EndpointProjection.Supports(
                    occurrence,
                    InspectionGraphEndpointRole.Source,
                    source)
                || !relationship.EndpointProjection.Supports(
                    occurrence,
                    InspectionGraphEndpointRole.Target,
                    target))
            {
                throw new ArgumentException(
                    "A relation occurrence does not support the canonical "
                    + "logical endpoints and relationship.",
                    nameof(occurrences));
            }
            occurrenceRows[index] = new(occurrence);
        }
        if (occurrenceRows
                .Select(static occurrence => occurrence.Identity)
                .Distinct()
                .Count()
            != occurrenceRows.Length)
        {
            throw new ArgumentException(
                "A canonical relation row cannot repeat a producer-issued "
                + "occurrence identity.",
                nameof(occurrences));
        }

        SubjectRelationIntegrationAssociation[] associationCopy =
            [.. integrationAssociations ?? []];
        if (associationCopy.Any(static association => association is null))
        {
            throw new ArgumentException(
                "Integration associations cannot contain null.",
                nameof(integrationAssociations));
        }

        Form = form;
        EvidenceKind = evidenceKind;
        Relationship = relationship;
        Source = source;
        Target = target;
        Correspondence = correspondence;
        Occurrences = [.. occurrenceRows];
        IntegrationAssociations = [.. associationCopy];
    }

    public SubjectRelationForm Form { get; }

    public SubjectRelationEvidenceKind EvidenceKind { get; }

    public InspectionGraphRelationshipDescriptor Relationship { get; }

    public InspectionGraphSubject Source { get; }

    public InspectionGraphSubject Target { get; }

    public SubjectRelationFocusCorrespondence Correspondence { get; }

    public SubjectRelationDirection Direction =>
        Correspondence.Role is InspectionGraphEndpointRole.Source
            ? SubjectRelationDirection.Outgoing
            : SubjectRelationDirection.Incoming;

    public ImmutableArray<SubjectRelationOccurrence> Occurrences { get; }

    public ImmutableArray<SubjectRelationIntegrationAssociation>
        IntegrationAssociations
    { get; }

    public bool IsIntegration => !IntegrationAssociations.IsEmpty;
}

/// <summary>Finite candidate accounting for one producer.</summary>
public sealed record SubjectRelationCoverage
{
    public SubjectRelationCoverage(
        int considered,
        int examined,
        int excluded,
        int unavailable,
        int limited)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(considered);
        ArgumentOutOfRangeException.ThrowIfNegative(examined);
        ArgumentOutOfRangeException.ThrowIfNegative(excluded);
        ArgumentOutOfRangeException.ThrowIfNegative(unavailable);
        ArgumentOutOfRangeException.ThrowIfNegative(limited);
        if (examined + excluded + unavailable + limited != considered)
        {
            throw new ArgumentException(
                "Relation coverage categories must account for every "
                + "considered candidate.");
        }

        Considered = considered;
        Examined = examined;
        Excluded = excluded;
        Unavailable = unavailable;
        Limited = limited;
    }

    public int Considered { get; }

    public int Examined { get; }

    public int Excluded { get; }

    public int Unavailable { get; }

    public int Limited { get; }
}

/// <summary>One typed producer-owned completion diagnostic.</summary>
public abstract class SubjectRelationProducerDiagnostic
{
    private protected SubjectRelationProducerDiagnostic(
        SubjectRelationProducerDiagnosticKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind;
    }

    public SubjectRelationProducerDiagnosticKind Kind { get; }

    public abstract object Evidence { get; }

    public static SubjectRelationProducerDiagnostic<TEvidence> Create<
        TEvidence>(
            SubjectRelationProducerDiagnosticKind kind,
            TEvidence evidence)
        where TEvidence : notnull =>
        new(kind, evidence);
}

/// <summary>One typed producer diagnostic evidence value.</summary>
public sealed class SubjectRelationProducerDiagnostic<TEvidence> :
    SubjectRelationProducerDiagnostic
    where TEvidence : notnull
{
    internal SubjectRelationProducerDiagnostic(
        SubjectRelationProducerDiagnosticKind kind,
        TEvidence evidence)
        : base(kind)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Value = evidence;
    }

    public TEvidence Value { get; }

    public override object Evidence => Value;
}

/// <summary>One relation producer's finite-population outcome.</summary>
public sealed class SubjectRelationProducerOutcome
{
    public SubjectRelationProducerOutcome(
        InspectionQueryDefinition producer,
        SubjectRelationProducerDisposition disposition,
        SubjectRelationCoverage coverage,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        IEnumerable<SubjectRelationProducerDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(producer);
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(relationships);

        InspectionGraphRelationshipDescriptor[] relationshipCopy =
            [.. relationships];
        if (relationshipCopy.Length == 0
            || relationshipCopy.Any(static relationship =>
                relationship is null)
            || relationshipCopy
                .Distinct(ReferenceEqualityComparer.Instance)
                .Count() != relationshipCopy.Length)
        {
            throw new ArgumentException(
                "A producer outcome requires distinct relationship "
                + "descriptors.",
                nameof(relationships));
        }

        SubjectRelationProducerDiagnostic[] diagnosticCopy =
            [.. diagnostics ?? []];
        if (diagnosticCopy.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "Producer diagnostics cannot contain null.",
                nameof(diagnostics));
        }
        if (disposition == SubjectRelationProducerDisposition.Complete
            && (coverage.Unavailable != 0
                || coverage.Limited != 0
                || diagnosticCopy.Length != 0))
        {
            throw new ArgumentException(
                "A complete relation producer cannot retain unavailable or "
                + "limited coverage or completion diagnostics.",
                nameof(disposition));
        }

        Producer = producer;
        Disposition = disposition;
        Coverage = coverage;
        Relationships = [.. relationshipCopy];
        Diagnostics = [.. diagnosticCopy];
    }

    public InspectionQueryDefinition Producer { get; }

    public SubjectRelationProducerDisposition Disposition { get; }

    public SubjectRelationCoverage Coverage { get; }

    public ImmutableArray<InspectionGraphRelationshipDescriptor>
        Relationships
    { get; }

    public ImmutableArray<SubjectRelationProducerDiagnostic> Diagnostics
    { get; }
}

/// <summary>Producer evidence retained for one relation population.</summary>
public sealed class SubjectRelationPopulationEvidence
{
    public SubjectRelationPopulationEvidence(
        SubjectRelationPopulationAuthority population,
        IEnumerable<SubjectRelationProducerOutcome> producers)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(producers);
        SubjectRelationProducerOutcome[] producerCopy = [.. producers];
        if (producerCopy.Any(static producer => producer is null)
            || producerCopy.Select(static producer => producer.Producer)
                .Distinct()
                .Count() != producerCopy.Length)
        {
            throw new ArgumentException(
                "Relation producer outcomes require distinct query "
                + "identities.",
                nameof(producers));
        }

        Population = population;
        Producers = [.. producerCopy];
    }

    public SubjectRelationPopulationAuthority Population { get; }

    public ImmutableArray<SubjectRelationProducerOutcome> Producers { get; }

    public bool IsComplete =>
        Producers.All(static producer =>
            producer.Disposition
                == SubjectRelationProducerDisposition.Complete);

    public bool HasUsableRows =>
        Producers.IsEmpty
        || Producers.Any(static producer =>
            producer.Disposition is
                SubjectRelationProducerDisposition.Complete
                or SubjectRelationProducerDisposition.Partial);
}
