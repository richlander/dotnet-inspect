using DotnetInspector.Queries;
using DotnetInspector.RowSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public enum TypeDependencyRowSet
{
    Relationships,
}

public enum TypeDependencyRowOrder
{
    Traversal,
}

public sealed class TypeDependencySectionPlan
{
    public TypeDependencySectionPlan(
        string targetType,
        RowSelectionIntent<TypeDependencyRowOrder> relationshipRows,
        int? maximumDepth = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        ArgumentNullException.ThrowIfNull(relationshipRows);
        if (maximumDepth is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDepth),
                maximumDepth,
                "A maximum dependency depth cannot be negative.");
        }
        if (relationshipRows.Operations.Any(
                static operation =>
                    operation.Kind is RowSelectionStageKind.Top))
        {
            throw new ArgumentException(
                "Type-dependency relationships have one declared traversal "
                    + "order and do not support ranked Top selection.",
                nameof(relationshipRows));
        }

        TargetType = targetType;
        RelationshipRows = relationshipRows;
        MaximumDepth = maximumDepth;
    }

    public string TargetType { get; }

    public RowSelectionIntent<TypeDependencyRowOrder> RelationshipRows
    {
        get;
    }

    public int? MaximumDepth { get; }

    public static TypeDependencySectionPlan All(
        string targetType,
        int? maximumDepth = null) =>
        new(
            targetType,
            RowSelectionIntent<TypeDependencyRowOrder>.Empty,
            maximumDepth);
}

public sealed class TypeDependencyRowSelectionResult
{
    internal TypeDependencyRowSelectionResult(
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
    TypeDependencyRowSelectionResult RowSelection);

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

        TypeDependencyRelationship[] relationships =
        [
            .. dependency.Relationships.OrderBy(
                static relationship => relationship.Ordinal),
        ];
        RowsCohortResult<
            TypeDependencyRowSet,
            TypeDependencyRelationship> selection =
                RowsCohortExecutor.ApplyUnordered(
                    [
                        RowsCohortSequence<
                            TypeDependencyRowSet,
                            TypeDependencyRelationship>.Create(
                                TypeDependencyRowSet.Relationships,
                                relationships),
                    ],
                    plan.RelationshipRows);
        if (!selection.IsSuccess)
        {
            return new TypeDependencyRowSelectionResult(
                [],
                selection.Failure);
        }

        return new TypeDependencyRowSelectionResult(
            selection.RowSets.Single().Values,
            failure: null);
    }
}
