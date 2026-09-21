using DotnetInspector.Queries;
using QuerySpace.Rows;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public enum TypeDependencyRowSet
{
    Relationships,
}

public sealed class TypeDependencySectionPlan
{
    public TypeDependencySectionPlan(
        string targetType,
        ResolvedRowQueryPlan<TypeDependencyRelationship>
            relationshipQuery,
        int? maximumDepth = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        ArgumentNullException.ThrowIfNull(relationshipQuery);
        if (!TypeDependencyVocabulary.Owns(relationshipQuery))
        {
            throw new ArgumentException(
                "The relationship row plan was not resolved from the "
                    + "Type Dependency schema.",
                nameof(relationshipQuery));
        }
        if (maximumDepth is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDepth),
                maximumDepth,
                "A maximum dependency depth cannot be negative.");
        }

        TargetType = targetType;
        RelationshipQuery = relationshipQuery;
        MaximumDepth = maximumDepth;
    }

    public string TargetType { get; }

    public ResolvedRowQueryPlan<TypeDependencyRelationship> RelationshipQuery
    {
        get;
    }

    public int? MaximumDepth { get; }

    public static TypeDependencySectionPlan All(
        string targetType,
        int? maximumDepth = null) =>
        new(
            targetType,
            ResolveRequired(RowQueryIntent.Empty),
            maximumDepth);

    private static ResolvedRowQueryPlan<TypeDependencyRelationship>
        ResolveRequired(RowQueryIntent intent)
    {
        RowQueryResolutionResult<TypeDependencyRelationship> result =
            TypeDependencyVocabulary.Resolve(intent);
        return result.Plan
            ?? throw new InvalidOperationException(
                "The canonical Type Dependency row query did not resolve.");
    }
}

public sealed class TypeDependencyRowSelectionResult
{
    public TypeDependencyRowSelectionResult(
        IReadOnlyList<TypeDependencyRelationship> relationships,
        RowsCohortSemanticFailure<TypeDependencyRowSet>? failure)
    {
        Relationships = relationships;
        Failure = failure;
    }

    public bool IsSuccess => Failure is null;

    public IReadOnlyList<TypeDependencyRelationship> Relationships
    {
        get;
    }

    public RowsCohortSemanticFailure<TypeDependencyRowSet>? Failure
    {
        get;
    }
}

public sealed record TypeDependencySectionResult(
    AssemblyContextTypeDependencyResult QueryResult,
    TypeDependencyRowSelectionResult RowSelection)
{
    public static TypeDependencySectionResult NotFound() =>
        new(
            new AssemblyContextTypeDependencyResult(
                new TypeDependencyResult(null, []),
                []),
            new TypeDependencyRowSelectionResult([], failure: null));
}

public static class TypeDependencySectionExecutor
{
    public static TypeDependencySectionResult Execute(
        AssemblyContextGroup group,
        TypeDependencySectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(plan);

        AssemblyContextTypeDependencyResult query =
            AssemblyContextTypeDependencyQuery.Execute(
                group,
                plan.TargetType,
                plan.MaximumDepth);
        return new(
            query,
            Select(query.Dependency, plan));
    }

    public static TypeDependencySectionResult ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant rootParticipant,
        TypeDependencySectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(rootParticipant);
        ArgumentNullException.ThrowIfNull(plan);

        AssemblyContextTypeDependencyResult query =
            AssemblyContextTypeDependencyQuery.ExecuteParticipant(
                group,
                rootParticipant,
                plan.TargetType,
                plan.MaximumDepth);
        return new(
            query,
            Select(query.Dependency, plan));
    }

    public static TypeDependencyRowSelectionResult Select(
        TypeDependencyResult dependency,
        TypeDependencySectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        ArgumentNullException.ThrowIfNull(plan);
        if (!dependency.Found)
        {
            return new TypeDependencyRowSelectionResult(
                [],
                failure: null);
        }

        RowSelectionResult<TypeDependencyRelationship> selection =
            RowQueryExecutor.Apply(
                dependency.Relationships,
                plan.RelationshipQuery);
        if (!selection.IsSuccess)
        {
            return new TypeDependencyRowSelectionResult(
                [],
                new RowsCohortSemanticFailure<TypeDependencyRowSet>(
                    TypeDependencyRowSet.Relationships,
                    selection.Failure!));
        }

        return new TypeDependencyRowSelectionResult(
            selection.Values,
            failure: null);
    }
}
