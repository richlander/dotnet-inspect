using System.Collections.Immutable;

namespace ILInspector.Analysis;

public enum OptimizationOpportunityPriority
{
    Low,
    Medium,
    High,
}

/// <summary>
/// One method's optimization opportunities in product-owned triage order.
/// </summary>
public sealed record OptimizationOpportunityMemberRanking(
    MethodIdentity Method,
    ImmutableArray<OptimizationOpportunity> Opportunities,
    OptimizationOpportunityPriority Priority,
    string Confidence,
    int InLoopCount,
    ImmutableArray<string> Shapes);

/// <summary>
/// Product-owned ordering for optimization opportunities and the methods that carry them.
/// </summary>
public static class OptimizationOpportunityRanking
{
    public static IComparer<OptimizationOpportunityMemberRanking>
        MemberComparer { get; } =
        Comparer<OptimizationOpportunityMemberRanking>.Create(
            CompareMembers);

    public static IOrderedEnumerable<OptimizationOpportunity> Order(
        IEnumerable<OptimizationOpportunity> opportunities)
    {
        ArgumentNullException.ThrowIfNull(opportunities);
        return opportunities
            .OrderByDescending(Priority)
            .ThenByDescending(
                opportunity => ConfidenceRank(opportunity.Confidence))
            .ThenByDescending(
                opportunity => WeightRank(opportunity.Weight))
            .ThenByDescending(opportunity => opportunity.RootReach)
            .ThenBy(
                opportunity =>
                    opportunity.Method.DeclaringType
                        .ToQualifiedDisplayString(),
                StringComparer.Ordinal)
            .ThenBy(
                opportunity => opportunity.Method.Name,
                StringComparer.Ordinal)
            .ThenBy(opportunity => opportunity.ILOffset ?? -1)
            .ThenBy(
                opportunity => opportunity.Shape,
                StringComparer.Ordinal);
    }

    public static ImmutableArray<OptimizationOpportunityMemberRanking>
        RankMembers(
            IEnumerable<OptimizationOpportunity> opportunities)
    {
        ArgumentNullException.ThrowIfNull(opportunities);
        return
        [
            .. OrderMembers(
                opportunities
                    .GroupBy(
                        opportunity =>
                            opportunity.SourceOwner
                                ?? opportunity.Method)
                    .Select(CreateMemberRanking)),
        ];
    }

    public static IOrderedEnumerable<OptimizationOpportunityMemberRanking>
        OrderMembers(
            IEnumerable<OptimizationOpportunityMemberRanking> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        return members.OrderBy(member => member, MemberComparer);
    }

    public static OptimizationOpportunityPriority Priority(
        OptimizationOpportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        if (opportunity.ColdPath)
            return OptimizationOpportunityPriority.Low;

        if (opportunity.Shape is
                "allocation-hotspot"
                or "cache-lookup-factory-delegate"
                or "linq-scan-in-loop"
                or "materialize-in-loop"
                or "scan-method-in-loop-call"
                or "string-build-in-loop"
            || (opportunity.Weight == "high"
                && opportunity.Shape != "small-array"))
        {
            return OptimizationOpportunityPriority.High;
        }

        if (opportunity.Shape == "generic-parameter-object-box")
        {
            return IteratesInLoop(opportunity)
                ? OptimizationOpportunityPriority.High
                : OptimizationOpportunityPriority.Medium;
        }

        return IteratesInLoop(opportunity)
            || opportunity.Weight == "medium"
                ? OptimizationOpportunityPriority.Medium
                : OptimizationOpportunityPriority.Low;
    }

    public static bool IteratesInLoop(
        OptimizationOpportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        return opportunity.Multiplicity == "loop"
            || (opportunity.Multiplicity is null
                && opportunity.InLoop);
    }

    static OptimizationOpportunityMemberRanking CreateMemberRanking(
        IGrouping<MethodIdentity, OptimizationOpportunity> group)
    {
        ImmutableArray<OptimizationOpportunity> opportunities =
        [
            .. Order(group),
        ];
        OptimizationOpportunity leading = opportunities[0];
        return new OptimizationOpportunityMemberRanking(
            group.Key,
            opportunities,
            Priority(leading),
            leading.Confidence,
            opportunities.Count(IteratesInLoop),
            [
                .. opportunities
                    .Select(opportunity => opportunity.Shape)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ]);
    }

    static int CompareMembers(
        OptimizationOpportunityMemberRanking? left,
        OptimizationOpportunityMemberRanking? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left is null)
            return 1;
        if (right is null)
            return -1;

        int comparison = right.Priority.CompareTo(left.Priority);
        if (comparison != 0)
            return comparison;
        comparison = ConfidenceRank(right.Confidence)
            .CompareTo(ConfidenceRank(left.Confidence));
        if (comparison != 0)
            return comparison;
        comparison = right.InLoopCount.CompareTo(left.InLoopCount);
        if (comparison != 0)
            return comparison;
        comparison = right.Opportunities.Length.CompareTo(
            left.Opportunities.Length);
        if (comparison != 0)
            return comparison;

        OptimizationOpportunity leftLeading = left.Opportunities[0];
        OptimizationOpportunity rightLeading = right.Opportunities[0];
        comparison = WeightRank(rightLeading.Weight)
            .CompareTo(WeightRank(leftLeading.Weight));
        if (comparison != 0)
            return comparison;
        comparison = rightLeading.RootReach.CompareTo(
            leftLeading.RootReach);
        if (comparison != 0)
            return comparison;
        comparison = string.Compare(
            left.Method.AssemblyName,
            right.Method.AssemblyName,
            StringComparison.Ordinal);
        if (comparison != 0)
            return comparison;
        comparison = left.Method.ModuleVersionId.CompareTo(
            right.Method.ModuleVersionId);
        if (comparison != 0)
            return comparison;
        comparison = string.Compare(
            left.Method.DeclaringType.ToQualifiedDisplayString(),
            right.Method.DeclaringType.ToQualifiedDisplayString(),
            StringComparison.Ordinal);
        if (comparison != 0)
            return comparison;
        comparison = string.Compare(
            left.Method.Name,
            right.Method.Name,
            StringComparison.Ordinal);
        if (comparison != 0)
            return comparison;
        return left.Method.MetadataToken.CompareTo(
            right.Method.MetadataToken);
    }

    static int ConfidenceRank(string confidence)
    {
        if (confidence.Equals(
                "high",
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (confidence.Equals(
                "medium",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    static int WeightRank(string? weight) =>
        weight is null ? -1 : ConfidenceRank(weight);
}
