using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;

using InspectionGraphCharacteristicDerivationKind =
    Inspector.Graph.GraphCharacteristicDerivationKind;
using InspectionGraphOccurrence =
    Inspector.Graph.GraphOccurrence<
        DotnetInspector.Queries.InspectionGraphSubject,
        DotnetInspector.Queries.InspectionGraphRelationshipDescriptor,
        DotnetInspector.Queries.IInspectionGraphOccurrenceEvidence>;

namespace DotnetInspector.Sections;

public sealed class AnnotatedTypeRelationsCallRequest
{
    public AnnotatedTypeRelationsCallRequest(
        SubjectRelationFocusAuthority focus,
        SubjectRelationPopulationAuthority population,
        AssemblyContextTypeCallCompatibilityResult evidence,
        IEnumerable<InspectionGraphCharacteristicDescriptor>? colors = null,
        IEnumerable<InspectionGraphCharacteristicDescriptor>?
            requiredColors = null)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(evidence);
        SubjectRelationFocusCorrespondence.RequireSameWorkspace(
            focus,
            population);
        NavigationTypeIdentity focusIdentity =
            focus.RequireTypeIdentity();
        if (!ReferenceEquals(
                focusIdentity.Registration,
                NavigationRegistrationIdentity.From(
                    evidence.FocusAssembly))
            || focusIdentity.Type != evidence.FocusType)
        {
            throw new ArgumentException(
                "The call evidence must match the exact Type focus.",
                nameof(evidence));
        }

        ImmutableArray<InspectionGraphCharacteristicDescriptor>
            colorSnapshot = SnapshotColors(colors, nameof(colors));
        ImmutableArray<InspectionGraphCharacteristicDescriptor>
            requiredSnapshot =
                SnapshotColors(requiredColors, nameof(requiredColors));
        foreach (InspectionGraphCharacteristicDescriptor required in
            requiredSnapshot)
        {
            if (!colorSnapshot.Any(color =>
                ReferenceEquals(color, required)))
            {
                throw new ArgumentException(
                    $"Required color '{required.Id}' is not selected.",
                    nameof(requiredColors));
            }
        }

        Focus = focus;
        Population = population;
        Evidence = evidence;
        Colors = colorSnapshot;
        RequiredColors = requiredSnapshot;
    }

    public SubjectRelationFocusAuthority Focus { get; }

    public SubjectRelationPopulationAuthority Population { get; }

    public AssemblyContextTypeCallCompatibilityResult Evidence { get; }

    public ImmutableArray<InspectionGraphCharacteristicDescriptor> Colors
    { get; }

    public ImmutableArray<InspectionGraphCharacteristicDescriptor>
        RequiredColors
    { get; }

    static ImmutableArray<InspectionGraphCharacteristicDescriptor>
        SnapshotColors(
            IEnumerable<InspectionGraphCharacteristicDescriptor>? colors,
            string parameterName)
    {
        InspectionGraphCharacteristicDescriptor[] snapshot =
            [.. colors ?? []];
        if (snapshot.Any(color =>
                color is null
                || !AnnotatedTypeRelationsCallAdoption.IsCompatibilityColor(
                    color))
            || snapshot.Distinct(ReferenceEqualityComparer.Instance).Count()
                != snapshot.Length)
        {
            throw new ArgumentException(
                "Colors must be distinct exact compatibility descriptors.",
                parameterName);
        }
        return [.. snapshot];
    }
}

/// <summary>
/// Adapts exact Type call and compatibility evidence to typed relation
/// contributions.
/// </summary>
public static class AnnotatedTypeRelationsCallAdoption
{
    public static InspectionQuery<AnnotatedTypeRelationsContribution>
        RowsQuery
    { get; } =
        new("Annotated Type outgoing compatibility calls: Rows",
            InspectionCost.Unbounded);

    public static InspectionQuery<AnnotatedTypeRelationsContribution>
        CountQuery
    { get; } =
        new("Annotated Type outgoing compatibility calls: Count",
            InspectionCost.Unbounded);

    public static InspectionQuery<AnnotatedTypeRelationsContribution>
        ExistsQuery
    { get; } =
        new("Annotated Type outgoing compatibility calls: Exists",
            InspectionCost.Unbounded);

    public static AnnotatedTypeRelationsAdapterQueries Queries { get; } =
        new(RowsQuery, CountQuery, ExistsQuery);

    public static AnnotatedTypeRelationsRegistry Register(
        AnnotatedTypeRelationsRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddRelationship(
            new(
                SubjectRelationForm.Invocation,
                CallGraphInspectionGraphCatalog.Call,
                Queries));
        registry.AddColor(
            new(
                CompatibilityInspectionGraphCatalog
                    .RequiresUnreferencedCode,
                Queries));
        registry.AddColor(
            new(
                CompatibilityInspectionGraphCatalog.RequiresDynamicCode,
                Queries));
        registry.AddColor(
            new(
                CompatibilityInspectionGraphCatalog.RequiresAssemblyFiles,
                Queries));
        return registry;
    }

    public static AnnotatedTypeRelationsContribution Rows(
        AnnotatedTypeRelationsCallRequest request) =>
        Create(request, AnnotatedTypeRelationsTerminal.Rows);

    public static AnnotatedTypeRelationsContribution Count(
        AnnotatedTypeRelationsCallRequest request) =>
        Create(request, AnnotatedTypeRelationsTerminal.Count);

    public static AnnotatedTypeRelationsContribution Exists(
        AnnotatedTypeRelationsCallRequest request) =>
        Create(request, AnnotatedTypeRelationsTerminal.Exists);

    internal static bool IsCompatibilityColor(
        InspectionGraphCharacteristicDescriptor color) =>
        ReferenceEquals(
            color,
            CompatibilityInspectionGraphCatalog.RequiresUnreferencedCode)
        || ReferenceEquals(
            color,
            CompatibilityInspectionGraphCatalog.RequiresDynamicCode)
        || ReferenceEquals(
            color,
            CompatibilityInspectionGraphCatalog.RequiresAssemblyFiles);

    static AnnotatedTypeRelationsContribution Create(
        AnnotatedTypeRelationsCallRequest request,
        AnnotatedTypeRelationsTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(request);
        AssemblyContextTypeCallCompatibilityResult evidence =
            request.Evidence;
        Dictionary<MethodIdentity, TypeCallCompatibilityTarget> targets =
            evidence.Targets.ToDictionary(target => target.Method);
        RelationshipGroup[] groups =
        [
            .. evidence.Occurrences
                .GroupBy(occurrence => (
                    occurrence.SourceMethod,
                    occurrence.TargetMethod))
                .Select(group => new RelationshipGroup(
                    group.Key.SourceMethod,
                    group.Key.TargetMethod,
                    [.. group]))
                .Where(group =>
                    Satisfies(
                        targets[group.Target],
                        request.RequiredColors)),
        ];

        bool predicateComplete = request.RequiredColors.IsEmpty
            || evidence.Targets.All(target =>
                target.Disposition
                    == TypeCallCompatibilityTargetDisposition.Available);
        bool relationshipComplete =
            evidence.TopologyComplete && predicateComplete;
        SubjectRelationProducerDisposition relationshipDisposition =
            evidence.TopologyDisposition switch
            {
                TypeCallCompatibilityTopologyDisposition.Unavailable =>
                    SubjectRelationProducerDisposition.Unavailable,
                _ when relationshipComplete =>
                    SubjectRelationProducerDisposition.Complete,
                _ => SubjectRelationProducerDisposition.Partial,
            };
        SubjectRelationCoverage relationshipCoverage =
            RelationshipCoverage(evidence, relationshipComplete);

        AnnotatedTypeRelationsFamilyTerminal familyTerminal =
            terminal switch
            {
                AnnotatedTypeRelationsTerminal.Rows =>
                    new AnnotatedTypeRelationsFamilyTerminal.Rows(
                        CreateRows(request, groups)),
                AnnotatedTypeRelationsTerminal.Count =>
                    new AnnotatedTypeRelationsFamilyTerminal.Count(
                        groups.Length,
                        relationshipComplete),
                AnnotatedTypeRelationsTerminal.Exists =>
                    new AnnotatedTypeRelationsFamilyTerminal.Exists(
                        groups.Length != 0
                            ? AnnotatedTypeRelationsExistsValue.True
                            : relationshipComplete
                                ? AnnotatedTypeRelationsExistsValue.False
                                : AnnotatedTypeRelationsExistsValue.Undecided),
                _ => throw new ArgumentOutOfRangeException(nameof(terminal)),
            };
        var relationship =
            new AnnotatedTypeRelationsRelationshipContribution(
                SubjectRelationForm.Invocation,
                CallGraphInspectionGraphCatalog.Call,
                request.Population,
                relationshipDisposition,
                relationshipCoverage,
                request.RequiredColors,
                familyTerminal);
        ImmutableArray<AnnotatedTypeRelationsColorContribution> colors =
            CreateColors(
                request,
                terminal,
                groups,
                targets);

        return new(
            request.Focus,
            terminal,
            [relationship],
            colors);
    }

    static ImmutableArray<SubjectRelationRow> CreateRows(
        AnnotatedTypeRelationsCallRequest request,
        RelationshipGroup[] groups)
    {
        var rows = ImmutableArray.CreateBuilder<SubjectRelationRow>(
            groups.Length);
        int occurrenceId = 0;
        foreach (RelationshipGroup group in groups)
        {
            InspectionGraphSubject source =
                SourceSubject(group.Source);
            InspectionGraphSubject target =
                TargetSubject(group.Occurrences[0]);
            rows.Add(
                new(
                    SubjectRelationForm.Invocation,
                    SubjectRelationEvidenceKind.StaticIlObservation,
                    CallGraphInspectionGraphCatalog.Call,
                    source,
                    target,
                    SubjectRelationFocusCorrespondence.Create(
                        request.Focus,
                        request.Population,
                        source,
                        InspectionGraphEndpointRole.Source,
                        request.Evidence),
                    group.Occurrences.Select(occurrence =>
                        new InspectionGraphOccurrence(
                            occurrenceId++,
                            CallGraphInspectionGraphCatalog.Call,
                            source,
                            target,
                            new CallGraphCensusCallSiteEvidence(
                                occurrence.OrderingKey,
                                occurrence.Call,
                                occurrence.CallSiteEvidence),
                            []))));
        }
        return rows.MoveToImmutable();
    }

    static ImmutableArray<AnnotatedTypeRelationsColorContribution>
        CreateColors(
            AnnotatedTypeRelationsCallRequest request,
            AnnotatedTypeRelationsTerminal terminal,
            RelationshipGroup[] groups,
            IReadOnlyDictionary<MethodIdentity,
                TypeCallCompatibilityTarget> targets)
    {
        HashSet<MethodIdentity> retainedTargets =
            groups.Select(group => group.Target).ToHashSet();
        var colors =
            ImmutableArray.CreateBuilder<
                AnnotatedTypeRelationsColorContribution>(
                    request.Colors.Length);
        foreach (InspectionGraphCharacteristicDescriptor color in
            request.Colors)
        {
            string attributeName = AttributeName(color);
            var annotations =
                ImmutableArray.CreateBuilder<AnnotatedTypeRelationsAnnotation>();
            if (terminal == AnnotatedTypeRelationsTerminal.Rows)
            {
                foreach (MethodIdentity method in retainedTargets)
                {
                    TypeCallCompatibilityTarget target = targets[method];
                    CompatibilityAttributeEvidence? attribute =
                        target.Attributes.FirstOrDefault(candidate =>
                            StringComparer.Ordinal.Equals(
                                candidate.AttributeTypeName,
                                attributeName));
                    if (attribute is null)
                        continue;
                    AssemblyContextCallCensusOccurrence occurrence =
                        groups.First(group => group.Target == method)
                            .Occurrences[0];
                    annotations.Add(
                        AnnotatedTypeRelationsAnnotation.Create(
                            color,
                            new InspectionGraphValue.Boolean(true),
                            new AnnotatedTypeRelationsAnnotationTarget.Subject(
                                TargetSubject(occurrence)),
                            InspectionGraphCharacteristicDerivationKind.Direct,
                            attribute));
                }
            }

            int available = request.Evidence.Targets.Count(target =>
                target.Disposition
                    == TypeCallCompatibilityTargetDisposition.Available);
            int unavailable = request.Evidence.Targets.Count(target =>
                target.Disposition
                    == TypeCallCompatibilityTargetDisposition.Unavailable);
            int limited = request.Evidence.Targets.Length
                - available
                - unavailable;
            bool complete = unavailable == 0 && limited == 0;
            colors.Add(
                new(
                    color,
                    request.Population,
                    complete
                        ? SubjectRelationProducerDisposition.Complete
                        : SubjectRelationProducerDisposition.Partial,
                    new(
                        request.Evidence.Targets.Length,
                        available,
                        excluded: 0,
                        unavailable,
                        limited),
                    annotations));
        }
        return colors.MoveToImmutable();
    }

    static SubjectRelationCoverage RelationshipCoverage(
        AssemblyContextTypeCallCompatibilityResult evidence,
        bool complete)
    {
        bool unavailable =
            evidence.TopologyDisposition
                == TypeCallCompatibilityTopologyDisposition.Unavailable;
        int qualification = !complete
            && evidence.UnresolvedOccurrences.IsEmpty
            ? 1
            : 0;
        return new(
            evidence.Occurrences.Length
                + evidence.UnresolvedOccurrences.Length
                + qualification,
            evidence.Occurrences.Length,
            excluded: 0,
            unavailable ? qualification : 0,
            evidence.UnresolvedOccurrences.Length
                + (unavailable ? 0 : qualification));
    }

    static bool Satisfies(
        TypeCallCompatibilityTarget target,
        ImmutableArray<InspectionGraphCharacteristicDescriptor>
            requiredColors) =>
        requiredColors.All(required =>
        {
            string name = AttributeName(required);
            return target.Attributes.Any(attribute =>
                StringComparer.Ordinal.Equals(
                    attribute.AttributeTypeName,
                    name));
        });

    static string AttributeName(
        InspectionGraphCharacteristicDescriptor color)
    {
        if (ReferenceEquals(
            color,
            CompatibilityInspectionGraphCatalog.RequiresUnreferencedCode))
        {
            return KnownAttributeNames.RequiresUnreferencedCodeAttribute;
        }
        if (ReferenceEquals(
            color,
            CompatibilityInspectionGraphCatalog.RequiresDynamicCode))
        {
            return KnownAttributeNames.RequiresDynamicCodeAttribute;
        }
        if (ReferenceEquals(
            color,
            CompatibilityInspectionGraphCatalog.RequiresAssemblyFiles))
        {
            return KnownAttributeNames.RequiresAssemblyFilesAttribute;
        }
        throw new ArgumentException(
            "The descriptor is not a compatibility color.",
            nameof(color));
    }

    static InspectionGraphSubject SourceSubject(MethodIdentity method) =>
        InspectionGraphSubject.ForMember(
            GraphNodeIdentity.FromMethod(method),
            new(
                method.DeclaringType,
                method.Name,
                method.ParameterTypes,
                method.ReturnType,
                method.Name is ".ctor" or ".cctor"
                    ? MemberKind.Constructor
                    : MemberKind.Method));

    static InspectionGraphSubject TargetSubject(
        AssemblyContextCallCensusOccurrence occurrence) =>
        InspectionGraphSubject.ForMember(
            GraphNodeIdentity.FromMethod(occurrence.TargetMethod),
            occurrence.Call.Callee);

    sealed record RelationshipGroup(
        MethodIdentity Source,
        MethodIdentity Target,
        ImmutableArray<AssemblyContextCallCensusOccurrence> Occurrences);
}
