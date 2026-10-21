using System.Collections.Immutable;

using DotnetInspector.Queries;

using InspectionGraphCharacteristicDerivationKind =
    Inspector.Graph.GraphCharacteristicDerivationKind;
using InspectionGraphOccurrence =
    Inspector.Graph.GraphOccurrence<
        DotnetInspector.Queries.InspectionGraphSubject,
        DotnetInspector.Queries.InspectionGraphRelationshipDescriptor,
        DotnetInspector.Queries.IInspectionGraphOccurrenceEvidence>;
using InspectionGraphTargetKind = Inspector.Graph.GraphTargetKind;

namespace DotnetInspector.Sections;

public sealed class AnnotatedTypeRelationsContractException :
    InvalidOperationException
{
    public AnnotatedTypeRelationsContractException(string message)
        : base(message)
    {
    }
}

public enum AnnotatedTypeRelationsExistsValue
{
    True,
    False,
    Undecided,
}

public abstract record AnnotatedTypeRelationsFamilyTerminal
{
    private AnnotatedTypeRelationsFamilyTerminal()
    {
    }

    public abstract AnnotatedTypeRelationsTerminal Terminal { get; }

    public sealed record Rows : AnnotatedTypeRelationsFamilyTerminal
    {
        public Rows(IEnumerable<SubjectRelationRow> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            ImmutableArray<SubjectRelationRow> snapshot = [.. values];
            if (snapshot.Any(static value => value is null))
            {
                throw new ArgumentException(
                    "Relationship Rows cannot contain null.",
                    nameof(values));
            }

            Values = snapshot;
        }

        public ImmutableArray<SubjectRelationRow> Values { get; }

        public override AnnotatedTypeRelationsTerminal Terminal =>
            AnnotatedTypeRelationsTerminal.Rows;
    }

    public sealed record Count : AnnotatedTypeRelationsFamilyTerminal
    {
        public Count(int value, bool isExact)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Value = value;
            IsExact = isExact;
        }

        public int Value { get; }

        public bool IsExact { get; }

        public override AnnotatedTypeRelationsTerminal Terminal =>
            AnnotatedTypeRelationsTerminal.Count;
    }

    public sealed record Exists : AnnotatedTypeRelationsFamilyTerminal
    {
        public Exists(AnnotatedTypeRelationsExistsValue value)
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public AnnotatedTypeRelationsExistsValue Value { get; }

        public override AnnotatedTypeRelationsTerminal Terminal =>
            AnnotatedTypeRelationsTerminal.Exists;
    }
}

public sealed class AnnotatedTypeRelationsRelationshipContribution
{
    public AnnotatedTypeRelationsRelationshipContribution(
        SubjectRelationForm form,
        InspectionGraphRelationshipDescriptor relationship,
        SubjectRelationPopulationAuthority population,
        SubjectRelationProducerDisposition disposition,
        SubjectRelationCoverage coverage,
        IEnumerable<InspectionGraphCharacteristicDescriptor>? predicateColors,
        AnnotatedTypeRelationsFamilyTerminal terminal,
        IEnumerable<SubjectRelationProducerDiagnostic>? diagnostics = null)
    {
        if (!Enum.IsDefined(form))
            throw new ArgumentOutOfRangeException(nameof(form));
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(population);
        ValidateDisposition(disposition);
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(terminal);

        ImmutableArray<InspectionGraphCharacteristicDescriptor> colors =
            SnapshotDistinct(
                predicateColors ?? [],
                static color => color.Id,
                nameof(predicateColors));
        ImmutableArray<SubjectRelationProducerDiagnostic> diagnosticSnapshot =
            SnapshotDiagnostics(diagnostics);

        ValidateCompletion(
            disposition,
            coverage,
            diagnosticSnapshot,
            nameof(disposition));
        ValidateTerminal(
            form,
            relationship,
            disposition,
            terminal);

        Form = form;
        Relationship = relationship;
        Population = population;
        Disposition = disposition;
        Coverage = coverage;
        PredicateColors = colors;
        Terminal = terminal;
        Diagnostics = diagnosticSnapshot;
    }

    public SubjectRelationForm Form { get; }

    public InspectionGraphRelationshipDescriptor Relationship { get; }

    public SubjectRelationPopulationAuthority Population { get; }

    public SubjectRelationProducerDisposition Disposition { get; }

    public SubjectRelationCoverage Coverage { get; }

    public ImmutableArray<InspectionGraphCharacteristicDescriptor>
        PredicateColors
    { get; }

    public AnnotatedTypeRelationsFamilyTerminal Terminal { get; }

    public ImmutableArray<SubjectRelationProducerDiagnostic> Diagnostics
    { get; }

    private static void ValidateTerminal(
        SubjectRelationForm form,
        InspectionGraphRelationshipDescriptor relationship,
        SubjectRelationProducerDisposition disposition,
        AnnotatedTypeRelationsFamilyTerminal terminal)
    {
        switch (terminal)
        {
            case AnnotatedTypeRelationsFamilyTerminal.Rows rows:
                if (disposition is
                        SubjectRelationProducerDisposition.Unavailable
                            or SubjectRelationProducerDisposition.Failed
                    && !rows.Values.IsEmpty)
                {
                    throw new ArgumentException(
                        "An unavailable or failed family cannot publish Rows.",
                        nameof(terminal));
                }

                foreach (SubjectRelationRow row in rows.Values)
                {
                    if (row.Form != form
                        || !ReferenceEquals(
                            row.Relationship,
                            relationship))
                    {
                        throw new ArgumentException(
                            "A relationship Row must match its family form "
                            + "and exact descriptor.",
                            nameof(terminal));
                    }
                }
                break;
            case AnnotatedTypeRelationsFamilyTerminal.Count count:
                if (disposition
                        == SubjectRelationProducerDisposition.Complete
                    != count.IsExact)
                {
                    throw new ArgumentException(
                        "Only a complete relationship family can publish an "
                        + "exact Count.",
                        nameof(terminal));
                }
                if (disposition is
                        SubjectRelationProducerDisposition.Unavailable
                            or SubjectRelationProducerDisposition.Failed
                    && count.Value != 0)
                {
                    throw new ArgumentException(
                        "An unavailable or failed family cannot publish a "
                        + "positive Count lower bound.",
                        nameof(terminal));
                }
                break;
            case AnnotatedTypeRelationsFamilyTerminal.Exists exists:
                if (disposition
                        == SubjectRelationProducerDisposition.Complete
                    && exists.Value
                        == AnnotatedTypeRelationsExistsValue.Undecided)
                {
                    throw new ArgumentException(
                        "A complete relationship family must settle Exists.",
                        nameof(terminal));
                }
                if (disposition
                        != SubjectRelationProducerDisposition.Complete
                    && exists.Value
                        == AnnotatedTypeRelationsExistsValue.False)
                {
                    throw new ArgumentException(
                        "Exists false requires a complete relationship family.",
                        nameof(terminal));
                }
                if (disposition is
                        SubjectRelationProducerDisposition.Unavailable
                            or SubjectRelationProducerDisposition.Failed
                    && exists.Value
                        != AnnotatedTypeRelationsExistsValue.Undecided)
                {
                    throw new ArgumentException(
                        "An unavailable or failed family cannot settle Exists.",
                        nameof(terminal));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(terminal));
        }
    }

    internal static void ValidateDisposition(
        SubjectRelationProducerDisposition disposition)
    {
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
    }

    internal static void ValidateCompletion(
        SubjectRelationProducerDisposition disposition,
        SubjectRelationCoverage coverage,
        ImmutableArray<SubjectRelationProducerDiagnostic> diagnostics,
        string parameterName)
    {
        if (disposition == SubjectRelationProducerDisposition.Complete
            && (coverage.Unavailable != 0
                || coverage.Limited != 0
                || !diagnostics.IsEmpty))
        {
            throw new ArgumentException(
                "A complete family cannot retain unavailable or limited "
                + "coverage or completion diagnostics.",
                parameterName);
        }
    }

    internal static ImmutableArray<T> SnapshotDistinct<T>(
        IEnumerable<T> values,
        Func<T, string> describe,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values);
        T[] snapshot = [.. values];
        if (snapshot.Any(static value => value is null)
            || snapshot.Distinct(ReferenceEqualityComparer.Instance).Count()
                != snapshot.Length)
        {
            throw new ArgumentException(
                "Values must be non-null and distinct by exact identity.",
                parameterName);
        }
        if (snapshot
                .Select(describe)
                .Distinct(StringComparer.Ordinal)
                .Count()
            != snapshot.Length)
        {
            throw new ArgumentException(
                "Values must have distinct diagnostic ids.",
                parameterName);
        }

        return [.. snapshot];
    }

    internal static ImmutableArray<SubjectRelationProducerDiagnostic>
        SnapshotDiagnostics(
            IEnumerable<SubjectRelationProducerDiagnostic>? diagnostics)
    {
        SubjectRelationProducerDiagnostic[] snapshot =
            [.. diagnostics ?? []];
        if (snapshot.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "Family diagnostics cannot contain null.",
                nameof(diagnostics));
        }

        return [.. snapshot];
    }
}

public abstract record AnnotatedTypeRelationsAnnotationTarget
{
    private AnnotatedTypeRelationsAnnotationTarget()
    {
    }

    public sealed record Subject : AnnotatedTypeRelationsAnnotationTarget
    {
        public Subject(InspectionGraphSubject value)
        {
            Value = value
                ?? throw new ArgumentNullException(nameof(value));
        }

        public InspectionGraphSubject Value { get; }
    }

    public sealed record Relation : AnnotatedTypeRelationsAnnotationTarget
    {
        public Relation(AnnotatedTypeRelationsRelationIdentity value)
        {
            Value = value
                ?? throw new ArgumentNullException(nameof(value));
        }

        public AnnotatedTypeRelationsRelationIdentity Value { get; }
    }

    public sealed record Occurrence : AnnotatedTypeRelationsAnnotationTarget
    {
        public Occurrence(AnnotatedTypeRelationsOccurrenceIdentity value)
        {
            Value = value
                ?? throw new ArgumentNullException(nameof(value));
        }

        public AnnotatedTypeRelationsOccurrenceIdentity Value { get; }
    }
}

public sealed record AnnotatedTypeRelationsRelationIdentity
{
    public AnnotatedTypeRelationsRelationIdentity(
        InspectionGraphRelationshipDescriptor relationship,
        InspectionGraphSubject source,
        InspectionGraphSubject target)
    {
        Relationship = relationship
            ?? throw new ArgumentNullException(nameof(relationship));
        Source = source
            ?? throw new ArgumentNullException(nameof(source));
        Target = target
            ?? throw new ArgumentNullException(nameof(target));
    }

    public InspectionGraphRelationshipDescriptor Relationship { get; }

    public InspectionGraphSubject Source { get; }

    public InspectionGraphSubject Target { get; }
}

public sealed record AnnotatedTypeRelationsOccurrenceIdentity
{
    public AnnotatedTypeRelationsOccurrenceIdentity(
        InspectionGraphRelationshipDescriptor relationship,
        object value)
    {
        Relationship = relationship
            ?? throw new ArgumentNullException(nameof(relationship));
        Value = value
            ?? throw new ArgumentNullException(nameof(value));
    }

    public InspectionGraphRelationshipDescriptor Relationship { get; }

    public object Value { get; }
}

public abstract class AnnotatedTypeRelationsAnnotation
{
    private protected AnnotatedTypeRelationsAnnotation(
        InspectionGraphCharacteristicDescriptor descriptor,
        InspectionGraphValue value,
        AnnotatedTypeRelationsAnnotationTarget target,
        InspectionGraphCharacteristicDerivationKind derivation,
        IEnumerable<AnnotatedTypeRelationsAnnotationTarget>? sources)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(derivation))
            throw new ArgumentOutOfRangeException(nameof(derivation));

        ImmutableArray<AnnotatedTypeRelationsAnnotationTarget> sourceSnapshot =
            [.. sources ?? []];
        if (sourceSnapshot.Any(static source => source is null)
            || sourceSnapshot.Distinct().Count() != sourceSnapshot.Length)
        {
            throw new ArgumentException(
                "Annotation derivation sources must be non-null and distinct.",
                nameof(sources));
        }
        if (!ReferenceEquals(value.Descriptor, descriptor.Value))
        {
            throw new ArgumentException(
                "An annotation value must match its descriptor contract.",
                nameof(value));
        }
        if (!descriptor.AdmittedDerivations.Contains(derivation))
        {
            throw new ArgumentException(
                "The annotation descriptor does not admit this derivation.",
                nameof(derivation));
        }
        if (derivation == InspectionGraphCharacteristicDerivationKind.Direct
            != sourceSnapshot.IsEmpty)
        {
            throw new ArgumentException(
                "Only a direct annotation has no derivation sources.",
                nameof(sources));
        }
        if (!descriptor.Targets.Contains(TargetKind(target)))
        {
            throw new ArgumentException(
                "The annotation descriptor does not admit this target kind.",
                nameof(target));
        }

        Descriptor = descriptor;
        Value = value;
        Target = target;
        Derivation = derivation;
        Sources = sourceSnapshot;
    }

    public InspectionGraphCharacteristicDescriptor Descriptor { get; }

    public InspectionGraphValue Value { get; }

    public AnnotatedTypeRelationsAnnotationTarget Target { get; }

    public InspectionGraphCharacteristicDerivationKind Derivation { get; }

    public ImmutableArray<AnnotatedTypeRelationsAnnotationTarget> Sources
    { get; }

    public abstract object Evidence { get; }

    public static AnnotatedTypeRelationsAnnotation Create<TEvidence>(
        InspectionGraphCharacteristicDescriptor descriptor,
        InspectionGraphValue value,
        AnnotatedTypeRelationsAnnotationTarget target,
        InspectionGraphCharacteristicDerivationKind derivation,
        TEvidence evidence,
        IEnumerable<AnnotatedTypeRelationsAnnotationTarget>? sources = null)
        where TEvidence : notnull =>
        new AnnotatedTypeRelationsAnnotation<TEvidence>(
            descriptor,
            value,
            target,
            derivation,
            evidence,
            sources);

    internal static InspectionGraphTargetKind TargetKind(
        AnnotatedTypeRelationsAnnotationTarget target) =>
        target switch
        {
            AnnotatedTypeRelationsAnnotationTarget.Subject =>
                InspectionGraphTargetKind.Node,
            AnnotatedTypeRelationsAnnotationTarget.Relation =>
                InspectionGraphTargetKind.Edge,
            AnnotatedTypeRelationsAnnotationTarget.Occurrence =>
                InspectionGraphTargetKind.Occurrence,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
}

public sealed class AnnotatedTypeRelationsAnnotation<TEvidence> :
    AnnotatedTypeRelationsAnnotation
    where TEvidence : notnull
{
    internal AnnotatedTypeRelationsAnnotation(
        InspectionGraphCharacteristicDescriptor descriptor,
        InspectionGraphValue value,
        AnnotatedTypeRelationsAnnotationTarget target,
        InspectionGraphCharacteristicDerivationKind derivation,
        TEvidence evidence,
        IEnumerable<AnnotatedTypeRelationsAnnotationTarget>? sources)
        : base(descriptor, value, target, derivation, sources)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        TypedEvidence = evidence;
    }

    public TEvidence TypedEvidence { get; }

    public override object Evidence => TypedEvidence;
}

public sealed class AnnotatedTypeRelationsColorContribution
{
    public AnnotatedTypeRelationsColorContribution(
        InspectionGraphCharacteristicDescriptor color,
        SubjectRelationPopulationAuthority population,
        SubjectRelationProducerDisposition disposition,
        SubjectRelationCoverage coverage,
        IEnumerable<AnnotatedTypeRelationsAnnotation>? annotations = null,
        IEnumerable<SubjectRelationProducerDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(color);
        ArgumentNullException.ThrowIfNull(population);
        AnnotatedTypeRelationsRelationshipContribution.ValidateDisposition(
            disposition);
        ArgumentNullException.ThrowIfNull(coverage);
        AnnotatedTypeRelationsAnnotation[] annotationSnapshot =
            [.. annotations ?? []];
        if (annotationSnapshot.Any(static annotation => annotation is null)
            || annotationSnapshot.Any(
                annotation =>
                    !ReferenceEquals(annotation.Descriptor, color)))
        {
            throw new ArgumentException(
                "Color annotations must be non-null and use the exact family "
                + "descriptor.",
                nameof(annotations));
        }
        ImmutableArray<SubjectRelationProducerDiagnostic> diagnosticSnapshot =
            AnnotatedTypeRelationsRelationshipContribution
                .SnapshotDiagnostics(diagnostics);
        AnnotatedTypeRelationsRelationshipContribution.ValidateCompletion(
            disposition,
            coverage,
            diagnosticSnapshot,
            nameof(disposition));
        if (disposition is
                SubjectRelationProducerDisposition.Unavailable
                    or SubjectRelationProducerDisposition.Failed
            && annotationSnapshot.Length != 0)
        {
            throw new ArgumentException(
                "An unavailable or failed color family cannot publish "
                + "annotations.",
                nameof(annotations));
        }

        Color = color;
        Population = population;
        Disposition = disposition;
        Coverage = coverage;
        Annotations = [.. annotationSnapshot];
        Diagnostics = diagnosticSnapshot;
    }

    public InspectionGraphCharacteristicDescriptor Color { get; }

    public SubjectRelationPopulationAuthority Population { get; }

    public SubjectRelationProducerDisposition Disposition { get; }

    public SubjectRelationCoverage Coverage { get; }

    public ImmutableArray<AnnotatedTypeRelationsAnnotation> Annotations
    { get; }

    public ImmutableArray<SubjectRelationProducerDiagnostic> Diagnostics
    { get; }
}

public sealed class AnnotatedTypeRelationsContribution
{
    public AnnotatedTypeRelationsContribution(
        SubjectRelationFocusAuthority focus,
        AnnotatedTypeRelationsTerminal terminal,
        IEnumerable<AnnotatedTypeRelationsRelationshipContribution>?
            relationships = null,
        IEnumerable<AnnotatedTypeRelationsColorContribution>? colors = null)
    {
        ArgumentNullException.ThrowIfNull(focus);
        if (!Enum.IsDefined(terminal))
            throw new ArgumentOutOfRangeException(nameof(terminal));

        AnnotatedTypeRelationsRelationshipContribution[] relationshipSnapshot =
            [.. relationships ?? []];
        AnnotatedTypeRelationsColorContribution[] colorSnapshot =
            [.. colors ?? []];
        if (relationshipSnapshot.Any(
                static relationship => relationship is null)
            || colorSnapshot.Any(static color => color is null))
        {
            throw new ArgumentException(
                "Contribution families cannot contain null.");
        }
        if (relationshipSnapshot.Any(
            relationship => relationship.Terminal.Terminal != terminal))
        {
            throw new ArgumentException(
                "Every relationship contribution must match the contribution "
                + "terminal.",
                nameof(relationships));
        }
        if (relationshipSnapshot.Any(
                relationship =>
                    !ReferenceEquals(
                        relationship.Population.Workspace.Identity,
                        focus.Workspace.Identity))
            || colorSnapshot.Any(
                color =>
                    !ReferenceEquals(
                        color.Population.Workspace.Identity,
                        focus.Workspace.Identity)))
        {
            throw new ArgumentException(
                "Every contribution family must belong to the focus "
                + "Workspace.");
        }
        if (terminal != AnnotatedTypeRelationsTerminal.Rows
            && colorSnapshot.Any(static color => !color.Annotations.IsEmpty))
        {
            throw new ArgumentException(
                "Scalar contributions cannot contain annotations.",
                nameof(colors));
        }

        Focus = focus;
        Terminal = terminal;
        Relationships = [.. relationshipSnapshot];
        Colors = [.. colorSnapshot];
    }

    public SubjectRelationFocusAuthority Focus { get; }

    public AnnotatedTypeRelationsTerminal Terminal { get; }

    public ImmutableArray<AnnotatedTypeRelationsRelationshipContribution>
        Relationships
    { get; }

    public ImmutableArray<AnnotatedTypeRelationsColorContribution> Colors
    { get; }
}

public sealed record AnnotatedTypeRelationsSelection(
    ImmutableArray<InspectionGraphRelationshipDescriptor> Relationships,
    SubjectRelationDirectionSelection Direction,
    ImmutableArray<InspectionGraphCharacteristicDescriptor> Colors,
    ImmutableArray<InspectionGraphCharacteristicDescriptor> PredicateColors,
    AnnotatedTypeRelationsTerminal Terminal);

public abstract record AnnotatedTypeRelationsTerminalOutcome
{
    private AnnotatedTypeRelationsTerminalOutcome()
    {
    }

    public sealed record Rows(int Count, bool IsComplete)
        : AnnotatedTypeRelationsTerminalOutcome;

    public sealed record CountResult(int Value, bool IsExact)
        : AnnotatedTypeRelationsTerminalOutcome;

    public sealed record Exists(AnnotatedTypeRelationsExistsValue Value)
        : AnnotatedTypeRelationsTerminalOutcome;
}

public sealed record AnnotatedTypeRelationsSubject(
    int Id,
    InspectionGraphSubject Subject);

public sealed record AnnotatedTypeRelationsRelation(
    int Id,
    AnnotatedTypeRelationsRelationIdentity Identity,
    SubjectRelationForm Form,
    SubjectRelationEvidenceKind EvidenceKind,
    int SourceSubjectId,
    int TargetSubjectId);

public sealed record AnnotatedTypeRelationsOccurrence(
    int Id,
    int RelationId,
    InspectionQueryDefinition Producer,
    int ProducerOccurrenceId,
    AnnotatedTypeRelationsOccurrenceIdentity Identity,
    int SourceSubjectId,
    int TargetSubjectId,
    IInspectionGraphOccurrenceEvidence Evidence,
    ImmutableArray<int> DerivedFromProducerOccurrenceIds);

public enum AnnotatedTypeRelationsDocumentTargetKind
{
    Subject,
    Relation,
    Occurrence,
}

public readonly record struct AnnotatedTypeRelationsDocumentTarget(
    AnnotatedTypeRelationsDocumentTargetKind Kind,
    int Id);

public sealed record AnnotatedTypeRelationsDocumentAnnotation(
    int Id,
    InspectionQueryDefinition Producer,
    InspectionGraphCharacteristicDescriptor Descriptor,
    InspectionGraphValue Value,
    AnnotatedTypeRelationsDocumentTarget Target,
    InspectionGraphCharacteristicDerivationKind Derivation,
    ImmutableArray<AnnotatedTypeRelationsDocumentTarget> Sources,
    object Evidence);

public sealed record AnnotatedTypeRelationsRelationshipFamilyOutcome(
    InspectionQueryDefinition Producer,
    AnnotatedTypeRelationsRelationshipContribution Contribution);

public sealed record AnnotatedTypeRelationsColorFamilyOutcome(
    InspectionQueryDefinition Producer,
    AnnotatedTypeRelationsColorContribution Contribution);

public sealed record AnnotatedTypeRelationsFocusJoin(
    int RelationId,
    SubjectRelationDirection Direction,
    SubjectRelationFocusCorrespondence Correspondence);

public sealed class AnnotatedTypeRelationsDocument
{
    internal AnnotatedTypeRelationsDocument(
        SubjectRelationFocusAuthority focus,
        AnnotatedTypeRelationsSelection selection,
        AnnotatedTypeRelationsTerminalOutcome terminal,
        ImmutableArray<AnnotatedTypeRelationsSubject> subjects,
        ImmutableArray<AnnotatedTypeRelationsRelation> relations,
        ImmutableArray<AnnotatedTypeRelationsOccurrence> occurrences,
        ImmutableArray<AnnotatedTypeRelationsDocumentAnnotation> annotations,
        ImmutableArray<AnnotatedTypeRelationsRelationshipFamilyOutcome>
            relationshipOutcomes,
        ImmutableArray<AnnotatedTypeRelationsColorFamilyOutcome> colorOutcomes,
        ImmutableArray<AnnotatedTypeRelationsFocusJoin> focusJoins)
    {
        Focus = focus;
        Selection = selection;
        Terminal = terminal;
        Subjects = subjects;
        Relations = relations;
        Occurrences = occurrences;
        Annotations = annotations;
        RelationshipOutcomes = relationshipOutcomes;
        ColorOutcomes = colorOutcomes;
        FocusJoins = focusJoins;
    }

    public SubjectRelationFocusAuthority Focus { get; }

    public AnnotatedTypeRelationsSelection Selection { get; }

    public AnnotatedTypeRelationsTerminalOutcome Terminal { get; }

    public ImmutableArray<AnnotatedTypeRelationsSubject> Subjects { get; }

    public ImmutableArray<AnnotatedTypeRelationsRelation> Relations { get; }

    public ImmutableArray<AnnotatedTypeRelationsOccurrence> Occurrences
    { get; }

    public ImmutableArray<AnnotatedTypeRelationsDocumentAnnotation> Annotations
    { get; }

    public ImmutableArray<AnnotatedTypeRelationsRelationshipFamilyOutcome>
        RelationshipOutcomes
    { get; }

    public ImmutableArray<AnnotatedTypeRelationsColorFamilyOutcome>
        ColorOutcomes
    { get; }

    public ImmutableArray<AnnotatedTypeRelationsFocusJoin> FocusJoins { get; }
}

public static class AnnotatedTypeRelationsGenerator
{
    public static AnnotatedTypeRelationsDocument Generate(
        AnnotatedTypeRelationsPlan plan,
        InspectionQueryResults results)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(results);

        var contributions =
            new Dictionary<
                InspectionQuery<AnnotatedTypeRelationsContribution>,
                AnnotatedTypeRelationsContribution>(
                    ReferenceEqualityComparer.Instance);
        foreach (
            InspectionQuery<AnnotatedTypeRelationsContribution> query in
            plan.Queries)
        {
            AnnotatedTypeRelationsContribution contribution =
                results.Get(query);
            if (!ReferenceEquals(contribution.Focus, plan.Request.Focus))
            {
                throw Contract(
                    $"Adapter query '{query.Name}' returned a foreign focus.");
            }
            if (contribution.Terminal != plan.Request.Terminal)
            {
                throw Contract(
                    $"Adapter query '{query.Name}' returned "
                    + $"{contribution.Terminal} for a "
                    + $"{plan.Request.Terminal} plan.");
            }
            contributions.Add(query, contribution);
        }

        var relationshipFamilies =
            new Dictionary<
                InspectionGraphRelationshipDescriptor,
                (
                    InspectionQuery<AnnotatedTypeRelationsContribution> Query,
                    AnnotatedTypeRelationsRelationshipContribution Family)>(
                        ReferenceEqualityComparer.Instance);
        var colorFamilies =
            new Dictionary<
                InspectionGraphCharacteristicDescriptor,
                (
                    InspectionQuery<AnnotatedTypeRelationsContribution> Query,
                    AnnotatedTypeRelationsColorContribution Family)>(
                        ReferenceEqualityComparer.Instance);

        foreach ((
            InspectionQuery<AnnotatedTypeRelationsContribution> query,
            AnnotatedTypeRelationsContribution contribution) in contributions)
        {
            foreach (
                AnnotatedTypeRelationsRelationshipContribution family in
                contribution.Relationships)
            {
                if (!relationshipFamilies.TryAdd(
                    family.Relationship,
                    (query, family)))
                {
                    throw Contract(
                        $"Relationship family '{family.Relationship.Id}' "
                        + "was contributed more than once.");
                }
            }
            foreach (
                AnnotatedTypeRelationsColorContribution family in
                contribution.Colors)
            {
                if (!colorFamilies.TryAdd(family.Color, (query, family)))
                {
                    throw Contract(
                        $"Color family '{family.Color.Id}' was contributed "
                        + "more than once.");
                }
            }
        }

        var relationshipOutcomes = ImmutableArray.CreateBuilder<
            AnnotatedTypeRelationsRelationshipFamilyOutcome>(
                plan.Relationships.Length);
        foreach (
            AnnotatedTypeRelationsRelationshipRegistration registration in
            plan.Relationships)
        {
            if (!relationshipFamilies.Remove(
                registration.Relationship,
                out var entry))
            {
                throw Contract(
                    $"Selected relationship '{registration.Relationship.Id}' "
                    + "has no contribution.");
            }
            InspectionQuery<AnnotatedTypeRelationsContribution>? expected =
                registration.Queries.For(plan.Request.Terminal);
            if (!ReferenceEquals(expected, entry.Query))
            {
                throw Contract(
                    $"Relationship '{registration.Relationship.Id}' was "
                    + "returned by the wrong adapter query.");
            }
            if (entry.Family.Form != registration.Form)
            {
                throw Contract(
                    $"Relationship '{registration.Relationship.Id}' changed "
                    + "its registered form.");
            }
            RequireExactColors(
                entry.Family.PredicateColors,
                plan.Request.RequiredColors,
                registration.Relationship.Id);
            relationshipOutcomes.Add(
                new(entry.Query, entry.Family));
        }
        if (relationshipFamilies.Count != 0)
        {
            throw Contract(
                "Adapter results contain an unselected relationship family.");
        }

        var colorOutcomes =
            ImmutableArray.CreateBuilder<
                AnnotatedTypeRelationsColorFamilyOutcome>(
                    plan.Colors.Length);
        foreach (
            AnnotatedTypeRelationsColorRegistration registration in
            plan.Colors)
        {
            if (!colorFamilies.Remove(registration.Color, out var entry))
            {
                throw Contract(
                    $"Selected color '{registration.Color.Id}' has no "
                    + "contribution.");
            }
            InspectionQuery<AnnotatedTypeRelationsContribution>? expected =
                registration.Queries.For(plan.Request.Terminal);
            if (!ReferenceEquals(expected, entry.Query))
            {
                throw Contract(
                    $"Color '{registration.Color.Id}' was returned by the "
                    + "wrong adapter query.");
            }
            colorOutcomes.Add(new(entry.Query, entry.Family));
        }
        if (colorFamilies.Count != 0)
            throw Contract("Adapter results contain an unselected color family.");

        AnnotatedTypeRelationsSelection selection =
            new(
                [.. plan.Relationships.Select(
                    static registration => registration.Relationship)],
                plan.Request.Direction,
                [.. plan.Colors.Select(
                    static registration => registration.Color)],
                plan.Request.RequiredColors,
                plan.Request.Terminal);

        return plan.Request.Terminal switch
        {
            AnnotatedTypeRelationsTerminal.Rows =>
                GenerateRows(
                    plan,
                    selection,
                    relationshipOutcomes.ToImmutable(),
                    colorOutcomes.ToImmutable()),
            AnnotatedTypeRelationsTerminal.Count =>
                GenerateCount(
                    plan,
                    selection,
                    relationshipOutcomes.ToImmutable(),
                    colorOutcomes.ToImmutable()),
            AnnotatedTypeRelationsTerminal.Exists =>
                GenerateExists(
                    plan,
                    selection,
                    relationshipOutcomes.ToImmutable(),
                    colorOutcomes.ToImmutable()),
            _ => throw new ArgumentOutOfRangeException(nameof(plan)),
        };
    }

    private static AnnotatedTypeRelationsDocument GenerateRows(
        AnnotatedTypeRelationsPlan plan,
        AnnotatedTypeRelationsSelection selection,
        ImmutableArray<AnnotatedTypeRelationsRelationshipFamilyOutcome>
            relationshipOutcomes,
        ImmutableArray<AnnotatedTypeRelationsColorFamilyOutcome> colorOutcomes)
    {
        var subjects = ImmutableArray.CreateBuilder<
            AnnotatedTypeRelationsSubject>();
        var subjectIds = new Dictionary<InspectionGraphSubject, int>();
        var relations = ImmutableArray.CreateBuilder<
            AnnotatedTypeRelationsRelation>();
        var relationIds =
            new Dictionary<AnnotatedTypeRelationsRelationIdentity, int>();
        var occurrences = ImmutableArray.CreateBuilder<
            AnnotatedTypeRelationsOccurrence>();
        var occurrenceIds =
            new Dictionary<AnnotatedTypeRelationsOccurrenceIdentity, int>();
        var focusJoins =
            ImmutableArray.CreateBuilder<AnnotatedTypeRelationsFocusJoin>();

        foreach (
            AnnotatedTypeRelationsRelationshipFamilyOutcome outcome in
            relationshipOutcomes)
        {
            var rows = (AnnotatedTypeRelationsFamilyTerminal.Rows)
                outcome.Contribution.Terminal;
            foreach (SubjectRelationRow row in rows.Values)
            {
                if (!ReferenceEquals(
                    row.Correspondence.FocusAuthority,
                    plan.Request.Focus))
                {
                    throw Contract(
                        $"Relationship '{row.Relationship.Id}' contains a "
                        + "foreign focus correspondence.");
                }
                if (!ReferenceEquals(
                    row.Correspondence.Population,
                    outcome.Contribution.Population))
                {
                    throw Contract(
                        $"Relationship '{row.Relationship.Id}' contains a "
                        + "foreign population correspondence.");
                }
                if (!DirectionMatches(
                    plan.Request.Direction,
                    row.Direction))
                {
                    throw Contract(
                        $"Relationship '{row.Relationship.Id}' contains a "
                        + "Row outside the selected direction.");
                }

                var identity = new AnnotatedTypeRelationsRelationIdentity(
                    row.Relationship,
                    row.Source,
                    row.Target);
                int relationId = relations.Count;
                if (!relationIds.TryAdd(identity, relationId))
                {
                    throw Contract(
                        $"Canonical relationship '{row.Relationship.Id}' "
                        + "was repeated.");
                }

                int sourceId = AddSubject(row.Source, subjects, subjectIds);
                int targetId = AddSubject(row.Target, subjects, subjectIds);
                relations.Add(
                    new(
                        relationId,
                        identity,
                        row.Form,
                        row.EvidenceKind,
                        sourceId,
                        targetId));
                focusJoins.Add(
                    new(
                        relationId,
                        row.Direction,
                        row.Correspondence));

                foreach (SubjectRelationOccurrence occurrence in row.Occurrences)
                {
                    var occurrenceIdentity =
                        new AnnotatedTypeRelationsOccurrenceIdentity(
                            row.Relationship,
                            occurrence.Identity);
                    int occurrenceId = occurrences.Count;
                    if (!occurrenceIds.TryAdd(
                        occurrenceIdentity,
                        occurrenceId))
                    {
                        throw Contract(
                            $"Relationship '{row.Relationship.Id}' repeated "
                            + "an occurrence identity.");
                    }

                    InspectionGraphOccurrence value = occurrence.Occurrence;
                    bool hasSourceReceipts =
                        !value.DerivedFromOccurrenceIds.IsEmpty;
                    if ((row.Relationship.Semantics
                                == InspectionGraphRelationshipSemantics.Derived
                            && !hasSourceReceipts)
                        || (row.Relationship.Semantics
                                != InspectionGraphRelationshipSemantics.Derived
                            && hasSourceReceipts))
                    {
                        throw Contract(
                            $"Relationship '{row.Relationship.Id}' has an "
                            + "occurrence whose source receipts do not match "
                            + "its semantics.");
                    }
                    occurrences.Add(
                        new(
                            occurrenceId,
                            relationId,
                            outcome.Producer,
                            value.Id,
                            occurrenceIdentity,
                            AddSubject(
                                value.SourceSubject,
                                subjects,
                                subjectIds),
                            AddSubject(
                                value.TargetSubject,
                                subjects,
                                subjectIds),
                            value.Evidence,
                            value.DerivedFromOccurrenceIds));
                }
            }
        }

        var annotations = ImmutableArray.CreateBuilder<
            AnnotatedTypeRelationsDocumentAnnotation>();
        var annotationIdentities =
            new HashSet<(
                InspectionGraphCharacteristicDescriptor Descriptor,
                AnnotatedTypeRelationsDocumentTarget Target)>();
        foreach (
            AnnotatedTypeRelationsColorFamilyOutcome outcome in colorOutcomes)
        {
            foreach (
                AnnotatedTypeRelationsAnnotation annotation in
                outcome.Contribution.Annotations)
            {
                AnnotatedTypeRelationsDocumentTarget target =
                    ResolveTarget(
                        annotation.Target,
                        subjects,
                        subjectIds,
                        relationIds,
                        occurrenceIds);
                if (!annotationIdentities.Add(
                    (annotation.Descriptor, target)))
                {
                    throw Contract(
                        $"Color '{annotation.Descriptor.Id}' repeated one "
                        + "annotation target.");
                }

                ImmutableArray<AnnotatedTypeRelationsDocumentTarget> sources =
                [
                    .. annotation.Sources.Select(
                        source => ResolveTarget(
                            source,
                            subjects,
                            subjectIds,
                            relationIds,
                            occurrenceIds)),
                ];
                annotations.Add(
                    new(
                        annotations.Count,
                        outcome.Producer,
                        annotation.Descriptor,
                        annotation.Value,
                        target,
                        annotation.Derivation,
                        sources,
                        annotation.Evidence));
            }
        }

        bool complete =
            relationshipOutcomes.All(
                static outcome =>
                    outcome.Contribution.Disposition
                        == SubjectRelationProducerDisposition.Complete)
            && PredicateColorsComplete(plan, colorOutcomes);
        return new(
            plan.Request.Focus,
            selection,
            new AnnotatedTypeRelationsTerminalOutcome.Rows(
                relations.Count,
                complete),
            subjects.ToImmutable(),
            relations.ToImmutable(),
            occurrences.ToImmutable(),
            annotations.ToImmutable(),
            relationshipOutcomes,
            colorOutcomes,
            focusJoins.ToImmutable());
    }

    private static AnnotatedTypeRelationsDocument GenerateCount(
        AnnotatedTypeRelationsPlan plan,
        AnnotatedTypeRelationsSelection selection,
        ImmutableArray<AnnotatedTypeRelationsRelationshipFamilyOutcome>
            relationshipOutcomes,
        ImmutableArray<AnnotatedTypeRelationsColorFamilyOutcome> colorOutcomes)
    {
        int count = 0;
        bool exact = true;
        foreach (
            AnnotatedTypeRelationsRelationshipFamilyOutcome outcome in
            relationshipOutcomes)
        {
            var family = (AnnotatedTypeRelationsFamilyTerminal.Count)
                outcome.Contribution.Terminal;
            count = checked(count + family.Value);
            exact &= family.IsExact;
        }
        exact &= PredicateColorsComplete(plan, colorOutcomes);

        return ScalarDocument(
            plan,
            selection,
            new AnnotatedTypeRelationsTerminalOutcome.CountResult(count, exact),
            relationshipOutcomes,
            colorOutcomes);
    }

    private static AnnotatedTypeRelationsDocument GenerateExists(
        AnnotatedTypeRelationsPlan plan,
        AnnotatedTypeRelationsSelection selection,
        ImmutableArray<AnnotatedTypeRelationsRelationshipFamilyOutcome>
            relationshipOutcomes,
        ImmutableArray<AnnotatedTypeRelationsColorFamilyOutcome> colorOutcomes)
    {
        AnnotatedTypeRelationsExistsValue value;
        if (relationshipOutcomes.Any(
            static outcome =>
                ((AnnotatedTypeRelationsFamilyTerminal.Exists)
                    outcome.Contribution.Terminal).Value
                == AnnotatedTypeRelationsExistsValue.True))
        {
            value = AnnotatedTypeRelationsExistsValue.True;
        }
        else if (relationshipOutcomes.All(
                static outcome =>
                    ((AnnotatedTypeRelationsFamilyTerminal.Exists)
                        outcome.Contribution.Terminal).Value
                    == AnnotatedTypeRelationsExistsValue.False)
            && PredicateColorsComplete(plan, colorOutcomes))
        {
            value = AnnotatedTypeRelationsExistsValue.False;
        }
        else
        {
            value = AnnotatedTypeRelationsExistsValue.Undecided;
        }

        return ScalarDocument(
            plan,
            selection,
            new AnnotatedTypeRelationsTerminalOutcome.Exists(value),
            relationshipOutcomes,
            colorOutcomes);
    }

    private static AnnotatedTypeRelationsDocument ScalarDocument(
        AnnotatedTypeRelationsPlan plan,
        AnnotatedTypeRelationsSelection selection,
        AnnotatedTypeRelationsTerminalOutcome terminal,
        ImmutableArray<AnnotatedTypeRelationsRelationshipFamilyOutcome>
            relationshipOutcomes,
        ImmutableArray<AnnotatedTypeRelationsColorFamilyOutcome> colorOutcomes)
        => new(
            plan.Request.Focus,
            selection,
            terminal,
            [],
            [],
            [],
            [],
            relationshipOutcomes,
            colorOutcomes,
            []);

    private static bool PredicateColorsComplete(
        AnnotatedTypeRelationsPlan plan,
        ImmutableArray<AnnotatedTypeRelationsColorFamilyOutcome> colorOutcomes)
    {
        var required = new HashSet<object>(
            plan.Request.RequiredColors.Cast<object>(),
            ReferenceEqualityComparer.Instance);
        return colorOutcomes
            .Where(outcome => required.Contains(outcome.Contribution.Color))
            .All(
                static outcome =>
                    outcome.Contribution.Disposition
                        == SubjectRelationProducerDisposition.Complete);
    }

    private static int AddSubject(
        InspectionGraphSubject subject,
        ImmutableArray<AnnotatedTypeRelationsSubject>.Builder subjects,
        Dictionary<InspectionGraphSubject, int> subjectIds)
    {
        if (subjectIds.TryGetValue(subject, out int id))
            return id;
        id = subjects.Count;
        subjectIds.Add(subject, id);
        subjects.Add(new(id, subject));
        return id;
    }

    private static AnnotatedTypeRelationsDocumentTarget ResolveTarget(
        AnnotatedTypeRelationsAnnotationTarget target,
        ImmutableArray<AnnotatedTypeRelationsSubject>.Builder subjects,
        Dictionary<InspectionGraphSubject, int> subjectIds,
        Dictionary<AnnotatedTypeRelationsRelationIdentity, int> relationIds,
        Dictionary<AnnotatedTypeRelationsOccurrenceIdentity, int> occurrenceIds)
        => target switch
        {
            AnnotatedTypeRelationsAnnotationTarget.Subject subject =>
                new(
                    AnnotatedTypeRelationsDocumentTargetKind.Subject,
                    AddSubject(subject.Value, subjects, subjectIds)),
            AnnotatedTypeRelationsAnnotationTarget.Relation relation
                when relationIds.TryGetValue(
                    relation.Value,
                    out int relationId) =>
                new(
                    AnnotatedTypeRelationsDocumentTargetKind.Relation,
                    relationId),
            AnnotatedTypeRelationsAnnotationTarget.Occurrence occurrence
                when occurrenceIds.TryGetValue(
                    occurrence.Value,
                    out int occurrenceId) =>
                new(
                    AnnotatedTypeRelationsDocumentTargetKind.Occurrence,
                    occurrenceId),
            AnnotatedTypeRelationsAnnotationTarget.Relation =>
                throw Contract(
                    "An annotation targets a relationship absent from Rows."),
            AnnotatedTypeRelationsAnnotationTarget.Occurrence =>
                throw Contract(
                    "An annotation targets an occurrence absent from Rows."),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

    private static void RequireExactColors(
        ImmutableArray<InspectionGraphCharacteristicDescriptor> actual,
        ImmutableArray<InspectionGraphCharacteristicDescriptor> expected,
        string relationshipId)
    {
        var actualSet = new HashSet<object>(
            actual.Cast<object>(),
            ReferenceEqualityComparer.Instance);
        if (actualSet.Count != expected.Length
            || expected.Any(color => !actualSet.Contains(color)))
        {
            throw Contract(
                $"Relationship '{relationshipId}' used a different "
                + "predicate-color set from the plan.");
        }
    }

    private static bool DirectionMatches(
        SubjectRelationDirectionSelection selection,
        SubjectRelationDirection direction) =>
        selection switch
        {
            SubjectRelationDirectionSelection.Incoming =>
                direction == SubjectRelationDirection.Incoming,
            SubjectRelationDirectionSelection.Outgoing =>
                direction == SubjectRelationDirection.Outgoing,
            SubjectRelationDirectionSelection.Both => true,
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };

    private static AnnotatedTypeRelationsContractException Contract(
        string message) =>
        new(message);
}
