using System.Collections.Immutable;
using System.Reflection.Metadata;

using DotnetInspector.Queries;

using ILInspector.Metadata;

using InertText;

using Inspector.Findings;

namespace DotnetInspector.Sections;

// docs/design/annotated-type-map-api-family.md owns this family, its adapter,
// the production registry, and the settler.

/// <summary>
/// One version side of a family contribution request, as the adapter queries
/// read it: the side's exact Type focus, its image, read through the side's
/// API shape producer, its Type definition, and the plan's Member keys, or
/// null for the Type's own entry alone.
/// </summary>
public sealed record AnnotatedTypeMapSideInput
{
    public AnnotatedTypeMapSideInput(
        SubjectRelationFocusAuthority focus,
        FindingDiffAnyApiShapeProducer image,
        TypeDefinitionHandle type,
        ImmutableArray<AnnotatedTypeMapMemberKey>? memberKeys)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(image);
        if (type.IsNil)
            throw new ArgumentException("The Type definition is nil.", nameof(type));
        Focus = focus;
        Image = image;
        Type = type;
        MemberKeys = memberKeys;
    }

    public SubjectRelationFocusAuthority Focus { get; }

    /// <summary>The side's image, read through its API shape producer.</summary>
    public FindingDiffAnyApiShapeProducer Image { get; }

    public TypeDefinitionHandle Type { get; }

    public ImmutableArray<AnnotatedTypeMapMemberKey>? MemberKeys { get; }
}

/// <summary>
/// The API shape family's registration and its family contribution adapter,
/// which wraps the API shape producer.
/// </summary>
public static class AnnotatedTypeMapApiShapeFamily
{
    /// <summary>The adapter query for family contribution requests.</summary>
    public static InspectionQuery<AnnotatedTypeMapContribution> ContributionQuery { get; } =
        new("annotated-type-map.shape.api.contribution", InspectionCost.NetworkFree);

    /// <summary>
    /// The family's registration: Type focus and Member-scope questions, with
    /// one adapter for family contribution requests. Map result kinds join
    /// with the Body shape family.
    /// </summary>
    public static AnnotatedTypeMapRegistration Registration { get; } = new(
        AnnotatedTypeMapFamily.ApiShape,
        AnnotatedTypeMapFocusSupport.Type | AnnotatedTypeMapFocusSupport.Members,
        new AnnotatedTypeMapAdapterQueries(contribution: ContributionQuery));

    /// <summary>
    /// One side's API shape contribution: the Type entry with its admission
    /// and projection, and one entry per requested key present on the side,
    /// <c>Complete</c> with its projection when the surface admits the Member
    /// and <c>NoApplicableInput</c> when it does not. A producer failure is
    /// the Type entry's <c>Failed</c> outcome, with no Member entries.
    /// </summary>
    public static AnnotatedTypeMapContribution Contribute(AnnotatedTypeMapSideInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        OpaqueByteRange[]? keys = input.MemberKeys is { } requested
            ? [.. requested.Select(static key => key.Key)]
            : null;
        FindingDiffAnyApiShape shape;
        try
        {
            shape = input.Image.Produce(input.Type, keys);
        }
        catch (BadImageFormatException error)
        {
            return new(
                input.Focus,
                AnnotatedTypeMapTypePresence.Present,
                [
                    new AnnotatedTypeMapApiShapeContent(
                        AnnotatedTypeMapFamily.ApiShape,
                        new(new AnnotatedTypeMapOutcome(AnnotatedTypeMapOutcomeKind.Failed, error.Message)),
                        []),
                ]);
        }

        AnnotatedTypeMapApiTypeFacts typeFacts = shape.Admitted
            ? new(admitted: true, shape.Surface)
            : new(admitted: false, projection: null);
        var members = new List<KeyValuePair<AnnotatedTypeMapMemberKey, AnnotatedTypeMapEntry<AnnotatedTypeMapApiMemberFacts>>>(
            shape.Members.Length);
        foreach (FindingDiffAnyApiShapeMember member in shape.Members)
        {
            members.Add(KeyValuePair.Create(
                new AnnotatedTypeMapMemberKey(member.Key),
                member.Projection is { } projection
                    ? new AnnotatedTypeMapEntry<AnnotatedTypeMapApiMemberFacts>(
                        AnnotatedTypeMapOutcome.Complete, new AnnotatedTypeMapApiMemberFacts(projection))
                    : new AnnotatedTypeMapEntry<AnnotatedTypeMapApiMemberFacts>(
                        AnnotatedTypeMapOutcome.NoApplicableInput)));
        }

        return new(
            input.Focus,
            AnnotatedTypeMapTypePresence.Present,
            [
                new AnnotatedTypeMapApiShapeContent(
                    AnnotatedTypeMapFamily.ApiShape,
                    new(AnnotatedTypeMapOutcome.Complete, typeFacts),
                    members),
            ]);
    }
}

/// <summary>
/// The production Annotated Type Map registry and the inspection query
/// catalog its adapters run in. <see cref="AnnotatedTypeMapCatalog.Describe"/>
/// of <see cref="Catalog"/> is the pinned registry snapshot.
/// </summary>
public static class AnnotatedTypeMapProduction
{
    public static AnnotatedTypeMapCatalog Catalog { get; } = new AnnotatedTypeMapRegistry()
        .Add(AnnotatedTypeMapApiShapeFamily.Registration)
        .Compile();

    public static InspectionQueryCatalog<AnnotatedTypeMapSideInput> Queries { get; } =
        new InspectionQueryRegistry<AnnotatedTypeMapSideInput>()
            .Add(AnnotatedTypeMapApiShapeFamily.ContributionQuery, AnnotatedTypeMapApiShapeFamily.Contribute)
            .Compile();
}

/// <summary>
/// Settles Finding Diff Any's uncertain API differences through the API shape
/// family (docs/design/annotated-type-map-api-family.md#settlement-through-the-family):
/// one family contribution request over the pair at the owning Members' keys,
/// resolved against the production registry, run once per side, settled by
/// generation, and compared with the complete API comparison. Pass
/// <see cref="Settle"/> as the <see cref="FindingDiffAnyApiSettler"/>. Each
/// side's image is read through the producer the comparison hands it.
/// </summary>
public sealed class AnnotatedTypeMapApiShapeSettler
{
    private readonly Func<MetadataTypeDefinitionName, SubjectRelationFocusAuthority> _before;
    private readonly Func<MetadataTypeDefinitionName, SubjectRelationFocusAuthority> _after;
    private readonly Action<AnnotatedTypeMapPlan>? _observePlan;

    /// <param name="before">Forms the Before side's exact Type focus authority for a Type the host's image declares.</param>
    /// <param name="after">Forms the After side's exact Type focus authority.</param>
    /// <param name="observePlan">Receives each accepted plan, for its trail as Debug evidence.</param>
    public AnnotatedTypeMapApiShapeSettler(
        Func<MetadataTypeDefinitionName, SubjectRelationFocusAuthority> before,
        Func<MetadataTypeDefinitionName, SubjectRelationFocusAuthority> after,
        Action<AnnotatedTypeMapPlan>? observePlan = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        _before = before;
        _after = after;
        _observePlan = observePlan;
    }

    public FindingExistsDecision Settle(
        FindingDiffAnyApiShapeProducer before,
        TypeDefinitionHandle beforeType,
        FindingDiffAnyApiShapeProducer after,
        TypeDefinitionHandle afterType,
        IReadOnlyList<OpaqueByteRange>? memberKeys)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (MetadataTypeDefinitionName.Read(before.Metadata, beforeType)
                is not MetadataTypeDefinitionNameReadResult.Read { Name: var beforeName }
            || MetadataTypeDefinitionName.Read(after.Metadata, afterType)
                is not MetadataTypeDefinitionNameReadResult.Read { Name: var afterName })
        {
            return FindingExistsDecision.Indeterminate;
        }

        SubjectRelationFocusAuthority beforeFocus = _before(beforeName);
        SubjectRelationFocusAuthority afterFocus = _after(afterName);
        // The seam passes no key set rather than an empty one, which planning rejects.
        ImmutableArray<AnnotatedTypeMapMemberKey>? keys = memberKeys is { Count: > 0 }
            ? [.. memberKeys.Select(static key => new AnnotatedTypeMapMemberKey(key))]
            : null;
        var request = new AnnotatedTypeMapPlanningRequest(
            AnnotatedTypeMapRequestForm.Contribution,
            [beforeFocus, afterFocus],
            versionPair: true,
            [AnnotatedTypeMapFamily.ApiShape],
            memberKeys: keys);
        AnnotatedTypeMapPlan plan = AnnotatedTypeMapProduction.Catalog.Resolve(request, AnnotatedTypeMapProduction.Queries) switch
        {
            AnnotatedTypeMapPlanningResult.Accepted accepted => accepted.Plan,
            AnnotatedTypeMapPlanningResult.Rejected rejected => throw new InvalidOperationException(
                $"The production registry rejected the API shape request: {rejected.Failure.Message}"),
            _ => throw new InvalidOperationException("Unknown planning result."),
        };
        _observePlan?.Invoke(plan);

        AnnotatedTypeMapContributionResult result = AnnotatedTypeMapGeneration.SettleContributions(
            plan,
            AnnotatedTypeMapProduction.Queries.Run(plan.Queries, new(beforeFocus, before, beforeType, keys)),
            AnnotatedTypeMapProduction.Queries.Run(plan.Queries, new(afterFocus, after, afterType, keys)));
        return Compare(result);
    }

    /// <summary>
    /// The consumer's comparison: any unsettled entry or a requested key present
    /// on no side is <c>Indeterminate</c>; a Type admitted on both sides compares
    /// its projections with the complete API comparison; a Type admitted on one
    /// side is <c>Changed</c>, and on neither, <c>Unchanged</c>.
    /// </summary>
    internal static FindingExistsDecision Compare(AnnotatedTypeMapContributionResult result)
    {
        if (!result.AbsentKeys.IsEmpty)
            return FindingExistsDecision.Indeterminate;
        if (Settled(result, AnnotatedTypeMapSide.Before) is not { } before
            || Settled(result, AnnotatedTypeMapSide.After) is not { } after)
        {
            return FindingExistsDecision.Indeterminate;
        }

        if (before.Admitted != after.Admitted)
            return FindingExistsDecision.Changed;
        if (!before.Admitted)
            return FindingExistsDecision.Unchanged;

        ApiFindingComparison comparison = MetadataFindings.CompareApi(
            before.Projection!, after.Projection!, new FindingSubject("api", "Selected Type API"),
            new ApiDiffOptions(ApiDiffScope.All));
        if (comparison.Types.Value is not FindingComparison<ApiTypeHandle>.Complete
            || comparison.Members.Value is not FindingComparison<ApiMemberHandle>.Complete
            || comparison.ApiDiff.InspectionFailures.Count != 0)
        {
            return FindingExistsDecision.Indeterminate;
        }
        return comparison.ApiDiff.IsEmpty ? FindingExistsDecision.Unchanged : FindingExistsDecision.Changed;
    }

    private static AnnotatedTypeMapApiTypeFacts? Settled(
        AnnotatedTypeMapContributionResult result,
        AnnotatedTypeMapSide side)
    {
        var content = (AnnotatedTypeMapApiShapeContent)result.Content(AnnotatedTypeMapFamily.ApiShape, side);
        if (content.TypeEntry is not { Outcome.IsSettled: true, Content: { } facts })
            return null;
        foreach (AnnotatedTypeMapEntry<AnnotatedTypeMapApiMemberFacts> member in content.Members.Values)
        {
            if (!member.Outcome.IsSettled)
                return null;
        }
        return facts;
    }
}
