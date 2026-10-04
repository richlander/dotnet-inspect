using System.Diagnostics.CodeAnalysis;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;
using QuerySpace;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public sealed record StringLiteralComparisonQueryPlan(
    PortableQueryIntent Intent,
    PortableQueryTerm Term,
    StringLiteralUsePredicate Predicate);

public abstract record StringLiteralComparisonQueryPlanResult
{
    private StringLiteralComparisonQueryPlanResult()
    {
    }

    public sealed record Accepted(StringLiteralComparisonQueryPlan Plan)
        : StringLiteralComparisonQueryPlanResult;

    public sealed record Rejected(PortableQueryFailure Failure)
        : StringLiteralComparisonQueryPlanResult;
}

/// <summary>
/// Resolves one string-literal predicate and compares its complete matching
/// values across one exact Library pair.
/// </summary>
public static class StringLiteralComparisonQuery
{
    public const string VocabularyIdentity =
        "diff-string-literals/v1";
    public const string LiteralKey = "Literal";

    private const string PredicateFamily = "literal-predicate";

    private static readonly Vocabulary QueryVocabulary = new();

    public static StringLiteralComparisonQueryPlanResult ResolveIntent(
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        PortableQueryResolution<StringLiteralComparisonQueryPlan> resolution =
            PortableQueryResolver.Resolve(
                VocabularyIdentity,
                QueryVocabulary,
                intent,
                cancellationToken);
        return resolution.IsResolved
            ? new StringLiteralComparisonQueryPlanResult.Accepted(
                resolution.Plan)
            : new StringLiteralComparisonQueryPlanResult.Rejected(
                resolution.Failure);
    }

    public static PortableQueryIntent CreateIntent(
        PortableQueryOperator @operator,
        string value)
        => PortableQueryIntent.Create(
            [new PortableQueryTerm(LiteralKey, @operator, value)],
            [],
            [],
            []);

    public static RetainedFindingComparisonSet ExecutePaths(
        string beforePath,
        string afterPath,
        string subjectId,
        string subjectDisplay,
        StringLiteralComparisonQueryPlan plan,
        StringLiteralUsePatternBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(beforePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(afterPath);
        ArgumentNullException.ThrowIfNull(plan);
        var subject = Subject(subjectId, subjectDisplay);
        var findingSubject = new FindingSubject(subject.Id, subject.Display);
        FindingInspection<StringLiteralUseOccurrence> before = InspectPath(
            beforePath,
            findingSubject,
            plan.Predicate,
            budget ?? StringLiteralUsePatternBudget.Default,
            cancellationToken);
        FindingInspection<StringLiteralUseOccurrence> after = InspectPath(
            afterPath,
            findingSubject,
            plan.Predicate,
            budget ?? StringLiteralUsePatternBudget.Default,
            cancellationToken);
        return Comparison(subject, before, after);
    }

    public static RetainedFindingComparisonSet ExecuteParticipants(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant after,
        string subjectId,
        string subjectDisplay,
        StringLiteralComparisonQueryPlan plan,
        StringLiteralUsePatternBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeGroup);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(afterGroup);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(plan);
        var subject = Subject(subjectId, subjectDisplay);
        var findingSubject = new FindingSubject(subject.Id, subject.Display);
        StringLiteralUsePatternBudget effectiveBudget =
            budget ?? StringLiteralUsePatternBudget.Default;
        FindingInspection<StringLiteralUseOccurrence> beforeInspection =
            InspectParticipant(
                beforeGroup,
                before,
                "before",
                findingSubject,
                plan.Predicate,
                effectiveBudget,
                cancellationToken);
        FindingInspection<StringLiteralUseOccurrence> afterInspection =
            InspectParticipant(
                afterGroup,
                after,
                "after",
                findingSubject,
                plan.Predicate,
                effectiveBudget,
                cancellationToken);
        return Comparison(
            subject,
            beforeInspection,
            afterInspection);
    }

    private static FindingInspection<StringLiteralUseOccurrence> InspectPath(
        string path,
        FindingSubject subject,
        StringLiteralUsePredicate predicate,
        StringLiteralUsePatternBudget budget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(path);
            return StringLiteralUseFindings.Inspect(
                StringLiteralUsePatternAnalysis.Inspect(
                    session,
                    predicate,
                    budget,
                    cancellationToken),
                subject);
        }
        catch (BadImageFormatException exception)
        {
            return Failed(
                subject,
                $"String-literal endpoint image was rejected: "
                    + exception.Message);
        }
        catch (IOException exception)
        {
            return Failed(
                subject,
                $"String-literal endpoint image could not be read: "
                    + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failed(
                subject,
                $"String-literal endpoint image could not be read: "
                    + exception.Message);
        }
    }

    private static FindingInspection<StringLiteralUseOccurrence>
        InspectParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            string side,
            FindingSubject subject,
            StringLiteralUsePredicate predicate,
            StringLiteralUsePatternBudget budget,
            CancellationToken cancellationToken)
    {
        AssemblyImageAccessResult<
            FindingInspection<StringLiteralUseOccurrence>> access =
            group.UseSnapshot(
                participant,
                cancellationToken,
                snapshot =>
                {
                    try
                    {
                        using AssemblyInspectionSession session =
                            AssemblyInspectionSession.Open(snapshot);
                        return StringLiteralUseFindings.Inspect(
                            StringLiteralUsePatternAnalysis.Inspect(
                                session,
                                predicate,
                                budget,
                                cancellationToken),
                            subject);
                    }
                    catch (BadImageFormatException exception)
                    {
                        return Failed(
                            subject,
                            $"String-literal {side} endpoint image was "
                                + $"rejected: {exception.Message}");
                    }
                });
        return access switch
        {
            AssemblyImageAccessResult<
                FindingInspection<StringLiteralUseOccurrence>>.Available
                available => available.Value,
            AssemblyImageAccessResult<
                FindingInspection<StringLiteralUseOccurrence>>.Rejected
                rejected => Failed(
                    subject,
                    $"String-literal {side} endpoint acquisition was "
                        + $"rejected: {rejected.Failure}."),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };
    }

    private static RetainedFindingComparisonSet Comparison(
        ResearchSubjectKey subject,
        FindingInspection<StringLiteralUseOccurrence> before,
        FindingInspection<StringLiteralUseOccurrence> after)
        => new(
            [
                new RetainedFindingComparison<StringLiteralUseOccurrence>(
                    subject,
                    StringLiteralUseFindings.Descriptor,
                    FindingComparison.Compare(before, after)),
            ]);

    private static FindingInspection<StringLiteralUseOccurrence> Failed(
        FindingSubject subject,
        string reason)
        => new FindingInspection<StringLiteralUseOccurrence>.Failed(
            new InspectionError(
                subject,
                StringLiteralUseFindings.Descriptor,
                reason));

    private static ResearchSubjectKey Subject(
        string subjectId,
        string subjectDisplay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectDisplay);
        return new(
            ResearchSubjectKind.Library,
            subjectId,
            subjectDisplay);
    }

    private sealed record Predicate(
        StringLiteralUsePredicate Value);

    private sealed class LiteralKeyDeclaration
        : PortableQueryKeyDeclaration<Predicate>
    {
        public override string Key => LiteralKey;

        public override string? Family => PredicateFamily;

        public override PortableQueryFamilyKind FamilyKind =>
            PortableQueryFamilyKind.Exclusive;

        public override bool AdmitsOperator(
            PortableQueryOperator @operator)
            => @operator is PortableQueryOperator.Contains
                or PortableQueryOperator.StartsWith;

        public override PortableQueryBinding<Predicate> Bind(
            PortableQueryOperator @operator,
            string value)
        {
            StringLiteralUsePredicateKind kind = @operator switch
            {
                PortableQueryOperator.Contains =>
                    StringLiteralUsePredicateKind.Contains,
                PortableQueryOperator.StartsWith =>
                    StringLiteralUsePredicateKind.StartsWith,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(@operator)),
            };
            try
            {
                return PortableQueryBinding<Predicate>.Bound(
                    $"{@operator}\0{value}",
                    new Predicate(
                        StringLiteralUsePredicate.Create(kind, value)));
            }
            catch (ArgumentException)
            {
                return PortableQueryBinding<Predicate>.Rejected;
            }
        }
    }

    private sealed class Vocabulary
        : PortableQueryVocabulary<
            Predicate,
            StringLiteralComparisonQueryPlan>
    {
        private static readonly LiteralKeyDeclaration Literal = new();

        public override string Identity => VocabularyIdentity;

        public override IReadOnlyList<string> RequiredTermFamilies =>
            [PredicateFamily];

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<Predicate>? declaration)
        {
            bool found = string.Equals(
                key,
                LiteralKey,
                StringComparison.Ordinal);
            declaration = found ? Literal : null;
            return found;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<Predicate>? declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(
            RowSelectionStageKind kind) => false;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) => false;

        public override StringLiteralComparisonQueryPlan CreatePlan(
            PortableQueryResolvedIntent<Predicate> resolved)
        {
            PortableQueryResolvedTerm<Predicate> term =
                resolved.Terms.Single();
            return new(
                PortableQueryIntent.Create(
                    [term.Term],
                    [],
                    [],
                    []),
                term.Term,
                term.Predicate.Value);
        }
    }
}
