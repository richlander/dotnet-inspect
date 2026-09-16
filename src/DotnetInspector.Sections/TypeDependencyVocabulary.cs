using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public static class TypeDependencyVocabulary
{
    private static readonly RowQueryNamedOrder<TypeDependencyRelationship>
        TraversalOrder =
        new(
            RowQueryNamedOrderIdentity.Create(),
            "Traversal",
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
                    "Source",
                    static relationship =>
                        relationship.SourceTypeName),
                TextKey(
                    "Target",
                    static relationship =>
                        relationship.TargetTypeName),
                KindKey(),
            ],
            [TraversalOrder],
            defaultBaselineOrder:
                new(
                    TraversalOrder,
                    RowQueryOrderDirection.Ascending));

    public static RowQueryResolutionResult<TypeDependencyRelationship>
        Resolve(RowQueryIntent intent) =>
        RowQueryResolver.Resolve(Vocabulary, intent);

    internal static bool Owns(
        ResolvedRowQueryPlan<TypeDependencyRelationship> plan) =>
        ReferenceEquals(plan.VocabularyIdentity, Vocabulary.Identity);

    private static RowQueryKey<TypeDependencyRelationship> TextKey(
        string key,
        Func<TypeDependencyRelationship, string> accessor) =>
        RowQueryKey<TypeDependencyRelationship>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<string>.Present(accessor(row)),
            BindText,
            direction => RowQueryValueOrder.Create(
                Comparer<string>.Create(
                    static (left, right) =>
                        StringComparer.OrdinalIgnoreCase.Compare(
                            left,
                            right)),
                direction,
                missingLast: false));

    private static RowQueryKey<TypeDependencyRelationship> KindKey() =>
        RowQueryKey<TypeDependencyRelationship>.Create(
            RowQueryKeyIdentity.Create(),
            "Kind",
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<TypeDependencyRelationshipKind>.Present(
                row.Kind),
            BindKind,
            direction => RowQueryValueOrder.Create(
                Comparer<TypeDependencyRelationshipKind>.Default,
                direction,
                missingLast: false));

    private static Predicate<string>? BindText(
        RowQueryOperator operation,
        RowQueryValueToken token) =>
        operation switch
        {
            RowQueryOperator.Equals =>
                value => WildcardMatch(value, token.Text),
            RowQueryOperator.NotEquals =>
                value => !WildcardMatch(value, token.Text),
            _ => null,
        };

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

    private static bool WildcardMatch(
        string actual,
        string pattern)
    {
        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return string.Equals(
                actual,
                pattern,
                StringComparison.OrdinalIgnoreCase);
        }

        int textIndex = 0;
        int patternIndex = 0;
        int starIndex = -1;
        int matchIndex = 0;
        while (textIndex < actual.Length)
        {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?'
                    || char.ToUpperInvariant(pattern[patternIndex])
                        == char.ToUpperInvariant(actual[textIndex])))
            {
                textIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length
                && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                matchIndex = textIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                textIndex = ++matchIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length
            && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
