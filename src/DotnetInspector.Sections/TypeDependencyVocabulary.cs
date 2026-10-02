using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public static class TypeDependencyVocabulary
{
    public const string SourceKey = "Source";
    public const string TargetKey = "Target";
    public const string KindKey = "Kind";
    public const string TraversalOrderKey = "Traversal";

    private static readonly RowQueryNamedOrder<TypeDependencyRelationship>
        TraversalOrderDefinition =
        new(
            RowQueryNamedOrderIdentity.Create(),
            TraversalOrderKey,
            RowQueryOrderPurpose.Sequence,
            direction => Directional(
                Comparer<TypeDependencyRelationship>.Create(
                    static (left, right) =>
                        left.Ordinal.CompareTo(right.Ordinal)),
                direction));

    private static readonly RowQueryVocabulary<TypeDependencyRelationship>
        Vocabulary =
        RowQueryVocabulary<TypeDependencyRelationship>.Create(
            RowQueryVocabularyIdentity.Create(),
            [
                TextKey(
                    SourceKey,
                    static relationship =>
                        relationship.SourceTypeName),
                TextKey(
                    TargetKey,
                    static relationship =>
                        relationship.TargetTypeName),
                KindQueryKey(),
            ],
            [TraversalOrderDefinition],
            defaultBaselineOrder:
                new(
                    TraversalOrderDefinition,
                    RowQueryOrderDirection.Ascending));

    internal static RowQueryVocabulary<TypeDependencyRelationship> Query =>
        Vocabulary;

    public static RowQueryResolutionResult<TypeDependencyRelationship>
        Resolve(RowQueryIntent intent) =>
        RowQueryResolver.Resolve(Vocabulary, intent);

    internal static bool Owns(
        ResolvedRowQueryPlan<TypeDependencyRelationship> plan) =>
        ReferenceEquals(plan.VocabularyIdentity, Vocabulary.Identity);

    private static RowQueryKey<TypeDependencyRelationship> TextKey(
        string key,
        Func<TypeDependencyRelationship, string> accessor) =>
        RowQueryText.Key(key, accessor);

    private static RowQueryKey<TypeDependencyRelationship> KindQueryKey() =>
        RowQueryKey<TypeDependencyRelationship>.Create(
            RowQueryKeyIdentity.Create(),
            KindKey,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<TypeDependencyRelationshipKind>.Present(
                row.Kind),
            BindKind,
            direction => RowQueryValueOrder.Create(
                Comparer<TypeDependencyRelationshipKind>.Default,
                direction,
                missingLast: false));

    private static Predicate<TypeDependencyRelationshipKind>? BindKind(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!Enum.TryParse(
                token.Text,
                ignoreCase: true,
                out TypeDependencyRelationshipKind expected)
            || !Enum.IsDefined(expected))
        {
            return null;
        }

        return operation switch
        {
            RowQueryOperator.Equals => value => value == expected,
            RowQueryOperator.NotEquals => value => value != expected,
            _ => null,
        };
    }

    private static IComparer<T> Directional<T>(
        IComparer<T> ascendingComparer,
        RowQueryOrderDirection direction) =>
        direction is RowQueryOrderDirection.Ascending
            ? ascendingComparer
            : Comparer<T>.Create(
                (left, right) =>
                    ascendingComparer.Compare(right, left));
}
