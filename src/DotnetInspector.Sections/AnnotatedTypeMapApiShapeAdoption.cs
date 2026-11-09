using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Queries;

using ILInspector.Metadata;

using InertText;

using Inspector.Findings;

namespace DotnetInspector.Sections;

/// <summary>
/// Issues exact Type focuses for one acquired assembly side of an API-shape
/// family comparison.
/// </summary>
public sealed class AnnotatedTypeMapApiShapeAuthority
{
    public AnnotatedTypeMapApiShapeAuthority(
        StructuralSubjectIdentity.WorkspaceSubject workspace,
        AssemblyAcquisitionRegistration registration,
        AssemblyReferenceIdentity assembly,
        AssemblyResolutionProvenance provenance)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Registration = registration ?? throw new ArgumentNullException(nameof(registration));
        Assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    }

    public StructuralSubjectIdentity.WorkspaceSubject Workspace { get; }

    public AssemblyAcquisitionRegistration Registration { get; }

    public AssemblyReferenceIdentity Assembly { get; }

    public AssemblyResolutionProvenance Provenance { get; }

    internal SubjectRelationFocusAuthority Focus(MetadataTypeDefinitionName type) =>
        SubjectRelationFocusAuthority.ForAcquiredType(
            Workspace,
            Registration,
            Assembly,
            Provenance,
            type);
}

/// <summary>One side's execution context for the API-shape contribution adapter.</summary>
public sealed class AnnotatedTypeMapApiShapeContext
{
    public AnnotatedTypeMapApiShapeContext(
        AnnotatedTypeMapPlan plan,
        SubjectRelationFocusAuthority focus,
        PEReader image,
        TypeDefinitionHandle type)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(image);
        if (type.IsNil)
            throw new ArgumentException("The Type definition is nil.", nameof(type));
        if (!plan.Request.Focuses.Any(candidate => ReferenceEquals(candidate, focus)))
            throw new ArgumentException("The focus is not one of the plan's version-side focuses.", nameof(focus));

        Plan = plan;
        Focus = focus;
        Image = image;
        Type = type;
    }

    public AnnotatedTypeMapPlan Plan { get; }

    public SubjectRelationFocusAuthority Focus { get; }

    public PEReader Image { get; }

    public TypeDefinitionHandle Type { get; }
}

/// <summary>
/// Production registration, adapter, and Finding Diff Any settler for the
/// Annotated Type Map API-shape family.
/// </summary>
public static class AnnotatedTypeMapApiShapeAdoption
{
    public static InspectionQuery<AnnotatedTypeMapContribution> ContributionQuery { get; } =
        new("shape.api.contribution", InspectionCost.NetworkFree);

    public static InspectionQueryCatalog<AnnotatedTypeMapApiShapeContext> QueryCatalog { get; } =
        new InspectionQueryRegistry<AnnotatedTypeMapApiShapeContext>()
            .Add(ContributionQuery, Contribute)
            .Compile();

    public static AnnotatedTypeMapCatalog Catalog { get; } =
        Register(new AnnotatedTypeMapRegistry()).Compile();

    public static AnnotatedTypeMapRegistry Register(AnnotatedTypeMapRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.Add(new(
            AnnotatedTypeMapFamily.ApiShape,
            AnnotatedTypeMapFocusSupport.Type | AnnotatedTypeMapFocusSupport.Members,
            new(contribution: ContributionQuery)));
    }

    public static FindingDiffAnyApiSettler CreateSettler(
        AnnotatedTypeMapApiShapeAuthority before,
        AnnotatedTypeMapApiShapeAuthority after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return (beforeImage, beforeType, afterImage, afterType, memberKeys) =>
            Settle(
                before,
                beforeImage,
                beforeType,
                after,
                afterImage,
                afterType,
                memberKeys);
    }

    private static AnnotatedTypeMapContribution Contribute(
        AnnotatedTypeMapApiShapeContext context)
    {
        IReadOnlyCollection<OpaqueByteRange>? keys =
            context.Plan.Request.MemberKeys is { } memberKeys
                ? [.. memberKeys.Select(static key => key.Key)]
                : null;
        FindingDiffAnyApiShape shape;
        try
        {
            shape = FindingDiffAnyApiShapes.Produce(context.Image, context.Type, keys);
        }
        catch (Exception exception) when (IsArtifactFailure(exception))
        {
            return new(
                context.Focus,
                AnnotatedTypeMapTypePresence.Present,
                [
                    new AnnotatedTypeMapApiShapeContent(
                        AnnotatedTypeMapFamily.ApiShape,
                        new(new(AnnotatedTypeMapOutcomeKind.Failed, exception.Message)),
                        []),
                ]);
        }

        var members = ImmutableArray.CreateBuilder<
            KeyValuePair<AnnotatedTypeMapMemberKey, AnnotatedTypeMapEntry<AnnotatedTypeMapApiMemberFacts>>>(
            shape.Members.Length);
        foreach (FindingDiffAnyApiShapeMember member in shape.Members)
        {
            var key = new AnnotatedTypeMapMemberKey(member.Key);
            AnnotatedTypeMapEntry<AnnotatedTypeMapApiMemberFacts> entry =
                member.Projection is { } projection
                    ? new(AnnotatedTypeMapOutcome.Complete, new(projection))
                    : new(AnnotatedTypeMapOutcome.NoApplicableInput);
            members.Add(KeyValuePair.Create(key, entry));
        }

        var typeFacts = new AnnotatedTypeMapApiTypeFacts(
            shape.Admitted,
            shape.Admitted ? shape.Surface : null);
        return new(
            context.Focus,
            AnnotatedTypeMapTypePresence.Present,
            [
                new AnnotatedTypeMapApiShapeContent(
                    AnnotatedTypeMapFamily.ApiShape,
                    new(AnnotatedTypeMapOutcome.Complete, typeFacts),
                    members.MoveToImmutable()),
            ]);
    }

    private static FindingExistsDecision Settle(
        AnnotatedTypeMapApiShapeAuthority beforeAuthority,
        PEReader beforeImage,
        TypeDefinitionHandle beforeType,
        AnnotatedTypeMapApiShapeAuthority afterAuthority,
        PEReader afterImage,
        TypeDefinitionHandle afterType,
        IReadOnlyList<OpaqueByteRange>? memberKeys)
    {
        MetadataTypeDefinitionNameReadResult beforeRead =
            MetadataTypeDefinitionName.Read(beforeImage.GetMetadataReader(), beforeType);
        MetadataTypeDefinitionNameReadResult afterRead =
            MetadataTypeDefinitionName.Read(afterImage.GetMetadataReader(), afterType);
        if (beforeRead is not MetadataTypeDefinitionNameReadResult.Read beforeName
            || afterRead is not MetadataTypeDefinitionNameReadResult.Read afterName)
        {
            return FindingExistsDecision.Indeterminate;
        }

        SubjectRelationFocusAuthority beforeFocus = beforeAuthority.Focus(beforeName.Name);
        SubjectRelationFocusAuthority afterFocus = afterAuthority.Focus(afterName.Name);
        IEnumerable<AnnotatedTypeMapMemberKey>? keys = memberKeys is { Count: > 0 }
            ? memberKeys.Select(static key => new AnnotatedTypeMapMemberKey(key))
            : null;
        var request = new AnnotatedTypeMapPlanningRequest(
            AnnotatedTypeMapRequestForm.Contribution,
            [beforeFocus, afterFocus],
            [AnnotatedTypeMapFamily.ApiShape],
            memberKeys: keys);
        AnnotatedTypeMapPlanningResult resolution = Catalog.Resolve(request, QueryCatalog);
        if (resolution is not AnnotatedTypeMapPlanningResult.Accepted accepted)
        {
            var rejected = (AnnotatedTypeMapPlanningResult.Rejected)resolution;
            throw new InspectionQueryException(
                "The production API-shape family registration rejected its own contribution request: "
                    + rejected.Failure.Message);
        }

        AnnotatedTypeMapPlan plan = accepted.Plan;
        InspectionQueryResults beforeResults = QueryCatalog.Run(
            plan.Queries,
            new(plan, beforeFocus, beforeImage, beforeType));
        InspectionQueryResults afterResults = QueryCatalog.Run(
            plan.Queries,
            new(plan, afterFocus, afterImage, afterType));
        AnnotatedTypeMapContributionResult result =
            AnnotatedTypeMapGeneration.SettleContributions(
                plan,
                beforeResults,
                afterResults);
        return Decide(result);
    }

    private static FindingExistsDecision Decide(AnnotatedTypeMapContributionResult result)
    {
        if (!result.AbsentKeys.IsEmpty)
            return FindingExistsDecision.Indeterminate;
        var before = (AnnotatedTypeMapApiShapeContent)result.Content(
            AnnotatedTypeMapFamily.ApiShape,
            AnnotatedTypeMapSide.Before);
        var after = (AnnotatedTypeMapApiShapeContent)result.Content(
            AnnotatedTypeMapFamily.ApiShape,
            AnnotatedTypeMapSide.After);
        if (before.TypeEntry is not
                {
                    Outcome.Kind: AnnotatedTypeMapOutcomeKind.Complete,
                    Content: { } oldFacts,
                }
            || after.TypeEntry is not
                {
                    Outcome.Kind: AnnotatedTypeMapOutcomeKind.Complete,
                    Content: { } newFacts,
                }
            || before.Members.Values.Any(static entry => !entry.Outcome.IsSettled)
            || after.Members.Values.Any(static entry => !entry.Outcome.IsSettled))
        {
            return FindingExistsDecision.Indeterminate;
        }

        if (oldFacts.Admitted != newFacts.Admitted)
            return FindingExistsDecision.Changed;
        if (!oldFacts.Admitted)
            return FindingExistsDecision.Unchanged;

        ApiFindingComparison comparison = MetadataFindings.CompareApi(
            oldFacts.Projection!,
            newFacts.Projection!,
            new FindingSubject("api", "Settled Type API"),
            new ApiDiffOptions(ApiDiffScope.All));
        if (comparison.Types.Value is not FindingComparison<ApiTypeHandle>.Complete
            || comparison.Members.Value is not FindingComparison<ApiMemberHandle>.Complete
            || comparison.ApiDiff.InspectionFailures.Count != 0)
        {
            return FindingExistsDecision.Indeterminate;
        }
        return comparison.ApiDiff.IsEmpty
            ? FindingExistsDecision.Unchanged
            : FindingExistsDecision.Changed;
    }

    private static bool IsArtifactFailure(Exception exception) =>
        exception is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException
            or OverflowException
            or IndexOutOfRangeException;
}
