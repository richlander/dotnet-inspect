using System.Globalization;

namespace DotnetInspector.Sections;

public static class SourceViewLineVocabulary
{
    public const string NumberKey = "Number";
    public const string StartKey = "Start";
    public const string ContentKey = "Content";
    public const string TerminatorKey = "Terminator";

    private static readonly RowQueryVocabulary<SourceViewLine> Vocabulary =
        RowQueryVocabulary<SourceViewLine>.Create(
            RowQueryVocabularyIdentity.Create(),
            [
                IntegerKey(
                    NumberKey,
                    static line => line.Number),
                IntegerKey(
                    StartKey,
                    static line => line.Start),
                RowQueryText.Key<SourceViewLine>(
                    ContentKey,
                    static line => line.Content),
                TerminatorQueryKey(),
            ],
            []);

    public static RowQueryResolutionResult<SourceViewLine> Resolve(
        RowQueryIntent intent) =>
        RowQueryResolver.Resolve(Vocabulary, intent);

    internal static bool Owns(
        ResolvedRowQueryPlan<SourceViewLine> plan) =>
        ReferenceEquals(
            plan.VocabularyIdentity,
            Vocabulary.Identity);

    private static RowQueryKey<SourceViewLine> IntegerKey(
        string key,
        Func<SourceViewLine, int> accessor) =>
        RowQueryKey<SourceViewLine>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            [
                RowQueryOperator.Equals,
                RowQueryOperator.NotEquals,
                RowQueryOperator.GreaterOrEqual,
                RowQueryOperator.LessOrEqual,
            ],
            line => RowQueryValue<int>.Present(accessor(line)),
            BindInteger,
            direction => RowQueryValueOrder.Create(
                Comparer<int>.Default,
                direction,
                missingLast: false));

    private static Predicate<int>? BindInteger(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!int.TryParse(
                token.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int expected))
        {
            return null;
        }

        return operation switch
        {
            RowQueryOperator.Equals =>
                value => value == expected,
            RowQueryOperator.NotEquals =>
                value => value != expected,
            RowQueryOperator.GreaterOrEqual =>
                value => value >= expected,
            RowQueryOperator.LessOrEqual =>
                value => value <= expected,
            _ => null,
        };
    }

    private static RowQueryKey<SourceViewLine>
        TerminatorQueryKey() =>
        RowQueryKey<SourceViewLine>.Create(
            RowQueryKeyIdentity.Create(),
            TerminatorKey,
            [
                RowQueryOperator.Equals,
                RowQueryOperator.NotEquals,
            ],
            static line =>
                RowQueryValue<
                    SourceViewLineTerminator>.Present(
                    line.Terminator),
            BindTerminator,
            direction => RowQueryValueOrder.Create(
                Comparer<SourceViewLineTerminator>.Default,
                direction,
                missingLast: false));

    private static Predicate<SourceViewLineTerminator>?
        BindTerminator(
            RowQueryOperator operation,
            RowQueryValueToken token)
    {
        if (!Enum.TryParse(
                token.Text,
                ignoreCase: true,
                out SourceViewLineTerminator expected)
            || !Enum.IsDefined(expected))
        {
            return null;
        }

        return operation switch
        {
            RowQueryOperator.Equals =>
                value => value == expected,
            RowQueryOperator.NotEquals =>
                value => value != expected,
            _ => null,
        };
    }
}
