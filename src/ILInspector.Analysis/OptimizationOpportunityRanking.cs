using System.Collections.Immutable;

using ILInspector.Metadata;

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
                    .Select(group =>
                        RankMember(group.Key, group))),
        ];
    }

    public static OptimizationOpportunityMemberRanking RankMember(
        MethodIdentity method,
        IEnumerable<OptimizationOpportunity> opportunities)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(opportunities);
        ImmutableArray<OptimizationOpportunity> ordered =
        [
            .. Order(opportunities),
        ];
        if (ordered.Length == 0)
        {
            throw new ArgumentException(
                "A member ranking requires at least one opportunity.",
                nameof(opportunities));
        }

        OptimizationOpportunity leading = ordered[0];
        return new OptimizationOpportunityMemberRanking(
            method,
            ordered,
            Priority(leading),
            leading.Confidence,
            ordered.Count(IteratesInLoop),
            [
                .. ordered
                    .Select(opportunity => opportunity.Shape)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ]);
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

    public static bool IncludePerformanceOpportunity(
        OptimizationOpportunity opportunity,
        IReadOnlySet<TypeRef> generatedFrameworkTypes)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        ArgumentNullException.ThrowIfNull(generatedFrameworkTypes);
        return !IsGeneratedMethod(
                opportunity.Method,
                generatedFrameworkTypes)
            || opportunity.Shape == "generic-parameter-object-box"
                && !IsInGeneratedFrameworkType(
                    opportunity,
                    generatedFrameworkTypes)
                && IsSourceFunctionName(opportunity.Method.Name);
    }

    public static bool IsGeneratedMethod(MethodIdentity method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return MemberFilters.IsCompilerGenerated(method.Name)
            || TypeFilters.IsCompilerGeneratedNested(
                method.DeclaringType.Name)
            || IsSystemTextJsonContextGeneratedMethod(method);
    }

    public static bool IsGeneratedMethod(
        MethodIdentity method,
        IReadOnlySet<TypeRef> generatedFrameworkTypes)
    {
        ArgumentNullException.ThrowIfNull(generatedFrameworkTypes);
        return IsGeneratedMethod(method)
            || LibraryBodyIndex.IsGeneratedFrameworkType(
                generatedFrameworkTypes,
                method.DeclaringType);
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

    static bool IsInGeneratedFrameworkType(
        OptimizationOpportunity opportunity,
        IReadOnlySet<TypeRef> generatedFrameworkTypes)
    {
        if (opportunity.SourceOwner is { } sourceOwner
            && LibraryBodyIndex.IsGeneratedFrameworkType(
                generatedFrameworkTypes,
                sourceOwner.DeclaringType))
        {
            return true;
        }

        return LibraryBodyIndex.IsGeneratedFrameworkType(
            generatedFrameworkTypes,
            opportunity.Method.DeclaringType);
    }

    static bool IsSourceFunctionName(string methodName)
        => methodName.Contains(">g__", StringComparison.Ordinal)
            || methodName.Contains(">b__", StringComparison.Ordinal);

    static bool IsSystemTextJsonContextGeneratedMethod(MethodIdentity method)
        => method.Name is "TryGetTypeInfoForRuntimeCustomConverter"
            && method.IsStatic
            && method.ReturnType.Equals(
                TypeRef.CoreLib("System", "Boolean"))
            && method.ParameterTypes.Length == 2
            && method.ParameterTypes[0].Equals(
                TypeRef.Definition(
                    "System.Text.Json",
                    "System.Text.Json",
                    "JsonSerializerOptions"))
            && method.ParameterTypes[1] is
                {
                    Kind: TypeRefKind.ByRef,
                    ElementType: { } jsonTypeInfo,
                }
            && IsJsonTypeInfo(jsonTypeInfo);

    static bool IsJsonTypeInfo(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is { } definition
            && definition.Equals(
                TypeRef.Definition(
                    "System.Text.Json",
                    "System.Text.Json.Serialization.Metadata",
                    "JsonTypeInfo`1"));
}
