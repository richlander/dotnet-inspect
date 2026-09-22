using QuerySpace.Rows;

namespace DotnetInspector.PortableQueries.Tests;

/// <summary>
/// The named gates the intent contract requires of any resolver.
/// </summary>
/// <remarks>
/// Each test carries the gate's name from
/// <c>docs/design/portable-query-intent.md</c>, so a gate that is renamed,
/// weakened, or dropped in the design has a matching test to answer for.
/// </remarks>
public sealed class PortableQueryResolutionGateTests
{
    /// <summary>
    /// <c>IntentResolutionIsAtomic</c> — an invalid vocabulary, key, operator,
    /// value, required family, bound, required dimension, stage, or order
    /// reference returns one structured failure with no plan and no partial
    /// binding.
    /// </summary>
    [Fact]
    public void IntentResolutionIsAtomic()
    {
        var vocabulary = new TestVocabulary { AdmitsRankingStages = true };

        (PortableQueryIntent intent, PortableQueryFailureReason expected)[] cases =
        [
            (Intent(terms: [Term("nope", "v")]), PortableQueryFailureReason.UnknownKey),
            (Intent(terms: [Term(TestVocabulary.ToolFormatKey, "v1", PortableQueryOperator.AtLeast)]),
                PortableQueryFailureReason.OperatorNotAdmitted),
            (Intent(terms: [Term(TestVocabulary.ToolFormatKey, "v3")]),
                PortableQueryFailureReason.ValueRejected),
            (Intent(), PortableQueryFailureReason.RequiredTermFamilyMissing),
            (Intent(bounds: [new PortableQueryBound("nope", 1)]),
                PortableQueryFailureReason.UnknownDimension),
            (Intent(bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, 10_000)]),
                PortableQueryFailureReason.MaximumOutsideRange),
            (Intent(terms: [Term(TestVocabulary.DependenciesKey, "none")]),
                PortableQueryFailureReason.RequiredDimensionMissing),
            (Intent(order: [Named(PortableQueryOrderRole.Baseline, "nope")]),
                PortableQueryFailureReason.UnknownOrderReference),
        ];

        foreach ((PortableQueryIntent intent, PortableQueryFailureReason expected) in cases)
        {
            TestVocabulary subject = expected switch
            {
                PortableQueryFailureReason.RequiredTermFamilyMissing =>
                    new TestVocabulary
                    {
                        RequiredFamilies = ["dependency"],
                    },
                PortableQueryFailureReason.RequiredDimensionMissing =>
                    new TestVocabulary
                    {
                        RequiredBounds = [TestVocabulary.CandidatesDimension],
                    },
                _ => vocabulary,
            };
            PortableQueryResolution<TestPlan> resolution = Resolve(subject, intent);

            Assert.False(resolution.IsResolved);
            Assert.Equal(expected, resolution.Failure.Reason);
            Assert.Throws<InvalidOperationException>(() => resolution.Plan);
        }

        Assert.Equal(0, vocabulary.PlansCreated);
    }

    /// <summary>
    /// <c>FailurePrecedenceIsContractFixed</c> — an intent carrying several
    /// independent defects reports the same failure regardless of how it was
    /// constructed: vocabulary, then terms in semantic order, then bounds, then
    /// the baseline, then stages.
    /// </summary>
    [Fact]
    public void FailurePrecedenceIsContractFixed()
    {
        var vocabulary = new TestVocabulary();

        // An unknown key, an unknown dimension, an unknown baseline reference,
        // and an inadmissible stage, all at once.
        PortableQueryIntent everything = Intent(
            terms: [Term("nope", "v")],
            bounds: [new PortableQueryBound("nope", 1)],
            stages: [PortableQueryStage.Top(5)],
            order: [Named(PortableQueryOrderRole.Baseline, "nope")]);

        Assert.Equal(
            PortableQueryFailureReason.UnknownKey,
            Resolve(vocabulary, everything).Failure.Reason);

        // The vocabulary outranks every part.
        Assert.Equal(
            PortableQueryFailureReason.UnknownVocabulary,
            ResolveUnknown(everything).Failure.Reason);

        // Terms resolve in semantic order, not construction order: with two
        // unknown keys the one that sorts first is reported, whichever was built
        // first.
        foreach (PortableQueryTerm[] built in new[]
        {
            new[] { Term("zulu", "v"), Term("alpha", "v") },
            new[] { Term("alpha", "v"), Term("zulu", "v") },
        })
        {
            PortableQueryFailure failure = Resolve(vocabulary, Intent(terms: built)).Failure;
            Assert.Equal("alpha", failure.Offender);
            Assert.Equal(0, failure.Location.Index);
        }

        // Within one element: existence, then admissibility, then binding. A
        // term with both an inadmissible operator and a value its binder would
        // reject reports the operator.
        Assert.Equal(
            PortableQueryFailureReason.OperatorNotAdmitted,
            Resolve(
                vocabulary,
                Intent(terms: [Term(TestVocabulary.ToolFormatKey, "v3", PortableQueryOperator.AtLeast)]))
                .Failure.Reason);

        // A missing required family follows present terms and outranks every
        // later part.
        var requiredFamily = new TestVocabulary
        {
            RequiredFamilies = ["population"],
        };
        Assert.Equal(
            PortableQueryFailureReason.UnknownKey,
            Resolve(
                requiredFamily,
                Intent(
                    terms: [Term("nope", "v")],
                    bounds: [new PortableQueryBound("nope", 1)]))
                .Failure.Reason);
        Assert.Equal(
            PortableQueryFailureReason.RequiredTermFamilyMissing,
            Resolve(
                requiredFamily,
                Intent(
                    bounds: [new PortableQueryBound("nope", 1)],
                    stages: [PortableQueryStage.Top(5)],
                    order: [Named(PortableQueryOrderRole.Baseline, "nope")]))
                .Failure.Reason);

        // Bounds outrank their missing requirements, the baseline, and stages.
        var requiredBound = new TestVocabulary
        {
            RequiredBounds = [TestVocabulary.CandidatesDimension],
        };
        Assert.Equal(
            PortableQueryFailureReason.UnknownDimension,
            Resolve(
                requiredBound,
                Intent(
                    bounds: [new PortableQueryBound("nope", 1)],
                    stages: [PortableQueryStage.Top(5)],
                    order: [Named(PortableQueryOrderRole.Baseline, "nope")]))
                .Failure.Reason);

        Assert.Equal(
            PortableQueryFailureReason.RequiredDimensionMissing,
            Resolve(
                requiredBound,
                Intent(
                    stages: [PortableQueryStage.Top(5)],
                    order: [Named(PortableQueryOrderRole.Baseline, "nope")]))
                .Failure.Reason);

        Assert.Equal(
            PortableQueryFailureReason.UnknownOrderReference,
            Resolve(
                vocabulary,
                Intent(
                    stages: [PortableQueryStage.Top(5)],
                    order: [Named(PortableQueryOrderRole.Baseline, "nope")]))
                .Failure.Reason);
    }

    /// <summary>
    /// <c>IntentResolutionStartsNoWork</c> — a rejected intent issues no
    /// acquisition, source request, or payload fetch, gated with a capability
    /// that fails the test if invoked.
    /// </summary>
    [Fact]
    public void IntentResolutionStartsNoWork()
    {
        var acquisition = new AcquisitionCapability();
        var vocabulary = new TestVocabulary(acquisition);

        PortableQueryResolution<TestPlan> resolution = Resolve(
            vocabulary,
            Intent(
                terms: [Term(TestVocabulary.ContentKey, "skill")],
                bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, 200)]));

        Assert.False(resolution.IsResolved);
        Assert.Equal(0, acquisition.Invocations);
        Assert.Equal(0, vocabulary.PlansCreated);
    }

    /// <summary>
    /// <c>UnresolvableTermFailsVisibly</c> — an intent naming a key, operator,
    /// or dimension absent from this build fails; it is never dropped,
    /// defaulted, narrowed, or widened.
    /// </summary>
    [Fact]
    public void UnresolvableTermFailsVisibly()
    {
        var vocabulary = new TestVocabulary();

        // A good term beside an unresolvable one. Dropping the unresolvable term
        // would answer a different question while looking like the shared one.
        PortableQueryResolution<TestPlan> resolution = Resolve(
            vocabulary,
            Intent(terms: [Term(TestVocabulary.DependsKey, "Serilog"), Term("gone", "x")]));

        Assert.False(resolution.IsResolved);
        Assert.Equal(PortableQueryFailureReason.UnknownKey, resolution.Failure.Reason);
        Assert.Equal("gone", resolution.Failure.Offender);
    }

    /// <summary>
    /// <c>RequiredQueryPartsFailVisibly</c> — a vocabulary may require one
    /// member of a term family and one execution-bound dimension; absence
    /// fails after present elements in that part validate and before a plan or
    /// acquisition exists.
    /// </summary>
    [Fact]
    public void RequiredQueryPartsFailVisibly()
    {
        var acquisition = new AcquisitionCapability();
        var vocabulary = new TestVocabulary(acquisition)
        {
            RequiredFamilies = ["z-population", "dependency"],
            RequiredBounds = ["z-work", TestVocabulary.CandidatesDimension],
        };

        PortableQueryFailure missingFamily = Resolve(
            vocabulary,
            Intent(
                terms: [Term(TestVocabulary.DependsKey, "Serilog")],
                bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, 20)]))
            .Failure;

        Assert.Equal(
            PortableQueryFailureReason.RequiredTermFamilyMissing,
            missingFamily.Reason);
        Assert.Equal(PortableQueryPart.Terms, missingFamily.Location.Part);
        Assert.Equal(1, missingFamily.Location.Index);
        Assert.Equal("dependency", missingFamily.Offender);
        Assert.Equal(0, vocabulary.PlansCreated);
        Assert.Equal(0, acquisition.Invocations);

        PortableQueryFailure missingDimension = Resolve(
            new TestVocabulary
            {
                RequiredFamilies = ["dependency"],
                RequiredBounds = ["z-work", TestVocabulary.CandidatesDimension],
            },
            Intent(
                terms: [Term(TestVocabulary.DependenciesKey, "none")],
                bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, 20)]))
            .Failure;

        Assert.Equal(
            PortableQueryFailureReason.RequiredDimensionMissing,
            missingDimension.Reason);
        Assert.Equal(PortableQueryPart.Bounds, missingDimension.Location.Part);
        Assert.Equal(1, missingDimension.Location.Index);
        Assert.Equal("z-work", missingDimension.Offender);

        var satisfied = new TestVocabulary
        {
            RequiredFamilies = ["dependency"],
            RequiredBounds = [TestVocabulary.CandidatesDimension],
        };
        Assert.True(Resolve(
            satisfied,
            Intent(
                terms: [Term(TestVocabulary.DependenciesKey, "none")],
                bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, 20)]))
            .IsResolved);
    }

    /// <summary>
    /// <c>IntentCarriesNoResolvedOrPresentationState</c> — what resolves is the
    /// request, and the plan is reached only through the vocabulary.
    /// </summary>
    [Fact]
    public void IntentCarriesNoResolvedOrPresentationState()
    {
        var vocabulary = new TestVocabulary();
        PortableQueryIntent intent = Intent(
            terms: [Term(TestVocabulary.DependsKey, "Serilog")],
            bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, 200)]);

        TestPlan plan = Resolve(vocabulary, intent).Plan;

        // The intent that went in is unchanged: no binding, no resolved
        // identity, and no outcome is written back onto it.
        Assert.Single(intent.Terms);
        Assert.Equal("Serilog", intent.Terms[0].Value);

        // The binding exists only on the resolved side.
        Assert.Equal("serilog", plan.Resolved.Terms[0].Predicate.Value);
        Assert.Equal("depends:serilog", plan.Resolved.Terms[0].PredicateIdentity);
    }

    /// <summary>
    /// <c>BoundKindsRemainDistinct</c> — an execution bound never resolves as a
    /// selection stage or the reverse, and each keeps its owner-issued identity.
    /// </summary>
    [Fact]
    public void BoundKindsRemainDistinct()
    {
        var vocabulary = new TestVocabulary();

        TestPlan plan = Resolve(
            vocabulary,
            Intent(
                bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, 200)],
                stages: [PortableQueryStage.Head(20)])).Plan;

        Assert.Equal(TestVocabulary.CandidatesDimension, Assert.Single(plan.Resolved.Bounds).Dimension);
        Assert.Equal(200, plan.Resolved.Bounds[0].RequestedMaximum);
        Assert.Equal(RowSelectionStageKind.Head, Assert.Single(plan.Resolved.Stages).Kind);
        Assert.Equal(20, plan.Resolved.Stages[0].Count);

        // A stage count of 200 and a bound of 20 is a different request from the
        // reverse, and neither reads as the other.
        TestPlan swapped = Resolve(
            vocabulary,
            Intent(
                bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, 20)],
                stages: [PortableQueryStage.Head(200)])).Plan;

        Assert.Equal(20, swapped.Resolved.Bounds[0].RequestedMaximum);
        Assert.Equal(200, swapped.Resolved.Stages[0].Count);
    }

    /// <summary>
    /// <c>IntentFailureShapeIsPresentationFree</c> — a failure carries the
    /// vocabulary identity, a typed location, an optional owner-issued offender,
    /// and a typed reason, and a reason without an offender carries none.
    /// </summary>
    [Fact]
    public void IntentFailureShapeIsPresentationFree()
    {
        var vocabulary = new TestVocabulary { AdmitsRankingStages = true };

        PortableQueryFailure failure = Resolve(
            vocabulary,
            Intent(terms: [Term("gone", "x")])).Failure;

        Assert.Equal("test.query", failure.Vocabulary);
        Assert.Equal(PortableQueryPart.Terms, failure.Location.Part);
        Assert.Equal(0, failure.Location.Index);
        Assert.Null(failure.Location.FieldTermIndex);
        Assert.Equal("gone", failure.Offender);

        // Ranking missing has no offending identity, and carries none rather
        // than an empty or invented one.
        PortableQueryFailure missing = Resolve(
            vocabulary,
            Intent(stages: [PortableQueryStage.Top(5)])).Failure;

        Assert.Equal(PortableQueryFailureReason.RankingMissing, missing.Reason);
        Assert.Null(missing.Offender);

        // The pairing of reason with location and offender is checked at
        // construction, so a resolver cannot report a reason at the wrong part.
        Assert.Throws<ArgumentException>(() => PortableQueryFailure.Create(
            "test.query",
            PortableQueryFailureReason.UnknownKey,
            PortableQueryLocation.Bound(0),
            "k"));
        Assert.Throws<ArgumentException>(() => PortableQueryFailure.Create(
            "test.query",
            PortableQueryFailureReason.RankingMissing,
            PortableQueryLocation.Stage(0),
            "invented"));
    }

    /// <summary>
    /// <c>DuplicateAfterBindingIsReachableAndVocabularyOwned</c> — two distinct
    /// terms that bind to one predicate reach the vocabulary's declared
    /// collapse-or-fail outcome, and an exact duplicate never reaches
    /// resolution because membership is set-valued.
    /// </summary>
    [Fact]
    public void DuplicateAfterBindingIsReachableAndVocabularyOwned()
    {
        // Two spellings this vocabulary's binder folds together.
        PortableQueryTerm[] collide =
        [
            Term(TestVocabulary.DependsKey, "Serilog"),
            Term(TestVocabulary.DependsKey, "serilog"),
        ];

        PortableQueryFailure refused =
            Resolve(new TestVocabulary(), Intent(terms: collide)).Failure;
        Assert.Equal(PortableQueryFailureReason.DuplicateAfterBinding, refused.Reason);
        Assert.Equal(TestVocabulary.DependsKey, refused.Offender);
        // Located at the later of the two in semantic order: "Serilog" sorts
        // before "serilog" by scalar value, so the second is the offender.
        Assert.Equal(1, refused.Location.Index);

        // The same intent under a vocabulary that collapses instead.
        TestPlan collapsed = Resolve(
            new TestVocabulary { CollapsesDuplicates = true },
            Intent(terms: collide)).Plan;
        Assert.Single(collapsed.Resolved.Terms);

        // An exact duplicate is one term before resolution ever sees it.
        var once = Term(TestVocabulary.DependsKey, "Serilog");
        TestPlan exact = Resolve(
            new TestVocabulary(),
            Intent(terms: [once, once, once])).Plan;
        Assert.Single(exact.Resolved.Terms);
    }

    /// <summary>
    /// <c>StagesCannotFailResolutionExceptByAdmission</c> — a structurally valid
    /// stage is refused only when the vocabulary does not declare its kind, and
    /// admission is per kind.
    /// </summary>
    [Fact]
    public void StagesCannotFailResolutionExceptByAdmission()
    {
        var packageLike = new TestVocabulary();

        foreach (PortableQueryStage admitted in new[]
        {
            PortableQueryStage.Head(5),
            PortableQueryStage.Tail(5),
            PortableQueryStage.Window(2, 8),
        })
        {
            Assert.True(Resolve(packageLike, Intent(stages: [admitted])).IsResolved);
        }

        PortableQueryFailure refused =
            Resolve(packageLike, Intent(stages: [PortableQueryStage.Top(5)])).Failure;
        Assert.Equal(PortableQueryFailureReason.StageNotAdmitted, refused.Reason);
        Assert.Equal("top", refused.Offender);
        Assert.Equal(PortableQueryPart.Stages, refused.Location.Part);

        // A row-like vocabulary admits the same stage.
        var rowLike = new TestVocabulary
        {
            AdmitsRankingStages = true,
            DeclaredDefaultRanking = TestVocabulary.RankingOrder,
        };
        Assert.True(Resolve(rowLike, Intent(stages: [PortableQueryStage.Top(5)])).IsResolved);
    }

    /// <summary>
    /// <c>RankingStagesResolveOrFail</c> — a ranking stage takes the operation
    /// bound to it, else the declared default, else fails at the stage; and a
    /// sequence-purpose named order in a ranking role fails as not a ranking.
    /// </summary>
    [Fact]
    public void RankingStagesResolveOrFail()
    {
        var withoutDefault = new TestVocabulary { AdmitsRankingStages = true };
        var withDefault = new TestVocabulary
        {
            AdmitsRankingStages = true,
            DeclaredDefaultRanking = TestVocabulary.RankingOrder,
        };

        // The bound operation wins.
        TestPlan bound = Resolve(
            withDefault,
            Intent(
                stages: [PortableQueryStage.Top(5)],
                order: [Named(PortableQueryOrderRole.ForStage(0), TestVocabulary.RankingOrder)]))
            .Plan;
        PortableQueryResolvedRanking ranking = Assert.Single(bound.Resolved.Rankings);
        Assert.Equal(0, ranking.StageIndex);
        Assert.NotNull(ranking.Operation);
        Assert.Null(ranking.DefaultReference);

        // Else the declared default.
        TestPlan defaulted = Resolve(withDefault, Intent(stages: [PortableQueryStage.Top(5)])).Plan;
        Assert.Equal(
            TestVocabulary.RankingOrder,
            Assert.Single(defaulted.Resolved.Rankings).DefaultReference);

        // Else ranking missing, at the stage, with no offender.
        PortableQueryFailure missing =
            Resolve(withoutDefault, Intent(stages: [PortableQueryStage.Top(5)])).Failure;
        Assert.Equal(PortableQueryFailureReason.RankingMissing, missing.Reason);
        Assert.Equal(PortableQueryPart.Stages, missing.Location.Part);
        Assert.Equal(0, missing.Location.Index);
        Assert.Null(missing.Offender);

        // With two ranking stages, the earlier stage's missing ranking is
        // reported before the later stage's unknown reference.
        PortableQueryFailure earlier = Resolve(
            withoutDefault,
            Intent(
                stages: [PortableQueryStage.Top(5), PortableQueryStage.Top(5)],
                order: [Named(PortableQueryOrderRole.ForStage(1), "nope")]))
            .Failure;
        Assert.Equal(PortableQueryFailureReason.RankingMissing, earlier.Reason);
        Assert.Equal(0, earlier.Location.Index);

        // A sequence-purpose order in a ranking role is not a ranking.
        PortableQueryFailure notRanking = Resolve(
            withDefault,
            Intent(
                stages: [PortableQueryStage.Top(5)],
                order: [Named(PortableQueryOrderRole.ForStage(0), TestVocabulary.SequenceOrder)]))
            .Failure;
        Assert.Equal(PortableQueryFailureReason.OrderNotARanking, notRanking.Reason);
        Assert.Equal(TestVocabulary.SequenceOrder, notRanking.Offender);

        // The same order serves a baseline, whose role requires no ranking.
        Assert.True(Resolve(
            withDefault,
            Intent(order: [Named(PortableQueryOrderRole.Baseline, TestVocabulary.SequenceOrder)]))
            .IsResolved);
    }

    /// <summary>
    /// <c>ExclusiveFamilyMembersAreRefused</c> — two bound terms the vocabulary
    /// declares mutually exclusive fail at the later term, distinct from
    /// duplicate-after-binding; and where a term is both, exclusivity is
    /// reported.
    /// </summary>
    [Fact]
    public void ExclusiveFamilyMembersAreRefused()
    {
        var vocabulary = new TestVocabulary();

        PortableQueryFailure incompatible = Resolve(
            vocabulary,
            Intent(terms:
            [
                Term(TestVocabulary.DependenciesKey, "none"),
                Term(TestVocabulary.DependenciesKey, "any"),
            ])).Failure;

        Assert.Equal(PortableQueryFailureReason.TermsIncompatible, incompatible.Reason);
        Assert.Equal(TestVocabulary.DependenciesKey, incompatible.Offender);
        Assert.Equal(1, incompatible.Location.Index);

        // A combining family is an OR-union, not a contradiction.
        Assert.True(Resolve(
            vocabulary,
            Intent(terms:
            [
                Term(TestVocabulary.ToolFormatKey, "v1"),
                Term(TestVocabulary.ToolFormatKey, "v2"),
            ])).IsResolved);

        // Exclusivity is checked before duplication, so a later term that is
        // both reports the contradiction rather than the collapsible duplicate.
        var collapsing = new TestVocabulary { CollapsesDuplicates = true };
        PortableQueryFailure both = Resolve(
            collapsing,
            Intent(terms:
            [
                Term(TestVocabulary.DependenciesKey, "none"),
                Term(TestVocabulary.DependenciesKey, "any"),
            ])).Failure;
        Assert.Equal(PortableQueryFailureReason.TermsIncompatible, both.Reason);

        TestPlan duplicate = Resolve(
            collapsing,
            Intent(terms:
            [
                Term(TestVocabulary.DependenciesKey, "none"),
                Term(TestVocabulary.DependenciesKey, "none"),
            ])).Plan;
        Assert.Single(duplicate.Resolved.Terms);

        // A collapsed duplicate still bound, so its family membership stands.
        // The alias sorts before the family key, so it binds first and the
        // family member is the term whose predicate collapses. The
        // contradiction that member contradicts must still be refused, rather
        // than resolving because the collapse hid the membership.
        PortableQueryFailure behindAlias = Resolve(
            collapsing,
            Intent(terms:
            [
                Term(TestVocabulary.DependenciesAliasKey, "any"),
                Term(TestVocabulary.DependenciesKey, "any"),
                Term(TestVocabulary.DependenciesKey, "none"),
            ])).Failure;
        Assert.Equal(PortableQueryFailureReason.TermsIncompatible, behindAlias.Reason);
        Assert.Equal(TestVocabulary.DependenciesKey, behindAlias.Offender);
    }

    /// <summary>
    /// <c>BoundTermCompatibilityIsVocabularyOwned</c> — an owner can refuse a
    /// pair that overlaps a combining family without changing that family's
    /// valid OR-union, and a collapsed occurrence still participates.
    /// </summary>
    [Fact]
    public void BoundTermCompatibilityIsVocabularyOwned()
    {
        var vocabulary = new TestVocabulary();

        // The format family still combines.
        Assert.True(Resolve(
            vocabulary,
            Intent(terms:
            [
                Term(TestVocabulary.ToolFormatKey, "v1"),
                Term(TestVocabulary.ToolFormatKey, "v2"),
            ])).IsResolved);

        // Broad tool presence and one specific format are incompatible. The
        // semantic order, not construction order, fixes the later offender.
        foreach (PortableQueryTerm[] built in new[]
        {
            new[]
            {
                Term(TestVocabulary.ToolKey, "true"),
                Term(TestVocabulary.ToolFormatKey, "v1"),
            },
            new[]
            {
                Term(TestVocabulary.ToolFormatKey, "v1"),
                Term(TestVocabulary.ToolKey, "true"),
            },
        })
        {
            PortableQueryFailure incompatible =
                Resolve(vocabulary, Intent(terms: built)).Failure;
            Assert.Equal(PortableQueryFailureReason.TermsIncompatible, incompatible.Reason);
            Assert.Equal(TestVocabulary.ToolFormatKey, incompatible.Offender);
            Assert.Equal(1, incompatible.Location.Index);
        }

        // The later format term is also a duplicate of the alias, but
        // incompatibility precedes duplicate handling and cannot collapse.
        var collapsing = new TestVocabulary { CollapsesDuplicates = true };
        PortableQueryFailure incompatibleDuplicate = Resolve(
            collapsing,
            Intent(terms:
            [
                Term(TestVocabulary.ToolAliasKey, "v1"),
                Term(TestVocabulary.ToolKey, "true"),
                Term(TestVocabulary.ToolFormatKey, "v1"),
            ])).Failure;
        Assert.Equal(
            PortableQueryFailureReason.TermsIncompatible,
            incompatibleDuplicate.Reason);
        Assert.Equal(TestVocabulary.ToolFormatKey, incompatibleDuplicate.Offender);

        // The actual format term collapses behind an earlier same-family alias,
        // but its declaration still participates in compatibility with a later
        // term.
        PortableQueryFailure behindCollapse = Resolve(
            collapsing,
            Intent(terms:
            [
                Term(TestVocabulary.ToolFormatAliasKey, "v1"),
                Term(TestVocabulary.ToolFormatKey, "v1"),
                Term(TestVocabulary.ToolFormatConsumerKey, "true"),
            ])).Failure;
        Assert.Equal(PortableQueryFailureReason.TermsIncompatible, behindCollapse.Reason);
        Assert.Equal(TestVocabulary.ToolFormatConsumerKey, behindCollapse.Offender);
        Assert.Equal(2, behindCollapse.Location.Index);
        Assert.Equal(0, collapsing.PlansCreated);
    }

    /// <summary>
    /// Collapse preserves the question. A duplicate collapses only inside one
    /// composition context, because that is the only place idempotence holds.
    /// </summary>
    /// <remarks>
    /// Two plain conjuncts asking the same thing ask it once, and so do two
    /// members of one combining family, since <c>A OR A</c> is <c>A</c>. Across
    /// contexts it fails: an ungrouped term beside a family member with the same
    /// predicate is <c>A AND (A OR B)</c>, and collapsing the member leaves
    /// <c>A AND B</c> — a narrower question than the one asked, silently
    /// returned as though it resolved.
    /// </remarks>
    [Fact]
    public void CollapseNarrowsNoQuery()
    {
        var vocabulary = new TestVocabulary { CollapsesDuplicates = true };

        // v1 AND (v1 OR v2). Every term is part of the question.
        TestPlan plan = Resolve(
            vocabulary,
            Intent(terms:
            [
                Term(TestVocabulary.ToolAliasKey, "v1"),
                Term(TestVocabulary.ToolFormatKey, "v1"),
                Term(TestVocabulary.ToolFormatKey, "v2"),
            ])).Plan;

        Assert.Equal(
            ["alias-tool=v1", "tool-format=v1", "tool-format=v2"],
            plan.Resolved.Terms
                .Select(term => $"{term.Term.Key}={term.Term.Value}")
                .Order()
                .ToArray());

        // Inside one context it still collapses: the same family member twice
        // under two spellings the binder folds together is one member.
        TestPlan inside = Resolve(
            vocabulary,
            Intent(terms:
            [
                Term(TestVocabulary.DependsKey, "Serilog"),
                Term(TestVocabulary.DependsKey, "serilog"),
            ])).Plan;
        Assert.Single(inside.Resolved.Terms);

        // And a vocabulary that refuses duplicate bindings refuses both a
        // same-context collision and a cross-context collision. Composition
        // context decides whether an allowed collision may collapse; it does
        // not override the vocabulary's collision policy.
        var refusing = new TestVocabulary();
        Assert.Equal(
            PortableQueryFailureReason.DuplicateAfterBinding,
            Resolve(
                refusing,
                Intent(terms:
                [
                    Term(TestVocabulary.DependsKey, "Serilog"),
                    Term(TestVocabulary.DependsKey, "serilog"),
                ])).Failure.Reason);

        PortableQueryIntent crossContext = Intent(terms:
        [
            Term(TestVocabulary.ToolAliasKey, "v1"),
            Term(TestVocabulary.ToolFormatKey, "v1"),
        ]);
        PortableQueryFailure crossContextFailure =
            Resolve(refusing, crossContext).Failure;
        Assert.Equal(
            PortableQueryFailureReason.DuplicateAfterBinding,
            crossContextFailure.Reason);
        Assert.Equal(TestVocabulary.ToolFormatKey, crossContextFailure.Offender);

        string payload = PortableQueryPayloadCodec.Encode(
            crossContext,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            crossContextFailure,
            Resolve(
                new TestVocabulary(),
                PortableQueryPayloadCodec.Decode(
                    payload,
                    TestContext.Current.CancellationToken)).Failure);
    }

    /// <summary>
    /// A failure is the same failure whichever way the intent reached the
    /// resolver: built directly, or encoded and decoded first.
    /// </summary>
    /// <remarks>
    /// The order part has no caller-meaningful sequence — every operation names
    /// its own role — so a location in it must be a position in the model's
    /// semantic order. Reported as a caller position it would move across a
    /// round trip, and two hosts holding one query would disagree about where
    /// it failed while agreeing on why.
    /// </remarks>
    [Fact]
    public void FailuresSurviveCanonicalReplay()
    {
        var vocabulary = new TestVocabulary { AdmitsRankingStages = true };

        // Built with the ranking first, which canonical form puts second.
        PortableQueryIntent built = Intent(
            stages: [PortableQueryStage.Top(5)],
            order:
            [
                Named(PortableQueryOrderRole.ForStage(0), TestVocabulary.RankingOrder),
                Named(PortableQueryOrderRole.Baseline, "gone"),
            ]);

        string payload = PortableQueryPayloadCodec.Encode(
            built,
            TestContext.Current.CancellationToken);
        PortableQueryIntent replayed = PortableQueryPayloadCodec.Decode(
            payload,
            TestContext.Current.CancellationToken);

        // Same query by the identity rule, so the same failure in every part.
        Assert.Equal(
            payload,
            PortableQueryPayloadCodec.Encode(replayed, TestContext.Current.CancellationToken));
        Assert.Equal(Resolve(vocabulary, built).Failure, Resolve(vocabulary, replayed).Failure);

        // And the location is the semantic position: the baseline is first
        // however it was built.
        Assert.Equal(0, Resolve(vocabulary, built).Failure.Location.Index);
    }

    /// <summary>
    /// <c>BoundRangeSeesResolvedTerms</c> — a dimension whose range depends on
    /// the bound terms is checked with those terms resolved, at the bound,
    /// before any acquisition.
    /// </summary>
    [Fact]
    public void BoundRangeSeesResolvedTerms()
    {
        var acquisition = new AcquisitionCapability();
        var vocabulary = new TestVocabulary(acquisition);

        // Without a content term, the wider ceiling admits it.
        Assert.True(Resolve(
            new TestVocabulary(),
            Intent(bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, 200)]))
            .IsResolved);

        // With one, the same bound is outside the range — and the term's
        // position in the intent does not matter, because terms are complete
        // before any bound is asked.
        PortableQueryFailure narrowed = Resolve(
            vocabulary,
            Intent(
                terms: [Term(TestVocabulary.ContentKey, "skill"), Term(TestVocabulary.DependsKey, "Serilog")],
                bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, 200)]))
            .Failure;

        Assert.Equal(PortableQueryFailureReason.MaximumOutsideRange, narrowed.Reason);
        Assert.Equal(PortableQueryPart.Bounds, narrowed.Location.Part);
        Assert.Equal(TestVocabulary.CandidatesDimension, narrowed.Offender);
        Assert.Equal(0, acquisition.Invocations);

        // At the narrowed ceiling it resolves.
        Assert.True(Resolve(
            new TestVocabulary(),
            Intent(
                terms: [Term(TestVocabulary.ContentKey, "skill")],
                bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, TestVocabulary.ContentCandidateCeiling)]))
            .IsResolved);
    }

    /// <summary>
    /// <c>FailureReasonUnionIsClosed</c> — every failure carries one reason from
    /// the table, at that reason's location, with that reason's offender or
    /// none, and the resolver reaches every reason in the union.
    /// </summary>
    [Fact]
    public void FailureReasonUnionIsClosed()
    {
        var withRanking = new TestVocabulary { AdmitsRankingStages = true };
        var packageLike = new TestVocabulary();

        var reached = new Dictionary<PortableQueryFailureReason, PortableQueryFailure>
        {
            [PortableQueryFailureReason.UnknownVocabulary] =
                ResolveUnknown(PortableQueryIntent.Empty).Failure,
        };

        void Reach(TestVocabulary vocabulary, PortableQueryIntent intent)
        {
            PortableQueryFailure failure = Resolve(vocabulary, intent).Failure;
            reached[failure.Reason] = failure;
        }

        Reach(withRanking, Intent(terms: [Term("gone", "x")]));
        Reach(withRanking, Intent(terms: [Term(TestVocabulary.ToolFormatKey, "v1", PortableQueryOperator.AtMost)]));
        Reach(withRanking, Intent(terms: [Term(TestVocabulary.ToolFormatKey, "v9")]));
        Reach(withRanking, Intent(terms:
        [
            Term(TestVocabulary.DependsKey, "Serilog"),
            Term(TestVocabulary.DependsKey, "serilog"),
        ]));
        Reach(withRanking, Intent(terms:
        [
            Term(TestVocabulary.DependenciesKey, "none"),
            Term(TestVocabulary.DependenciesKey, "any"),
        ]));
        Reach(
            new TestVocabulary { RequiredFamilies = ["population"] },
            Intent());
        Reach(withRanking, Intent(bounds: [new PortableQueryBound("gone", 1)]));
        Reach(withRanking, Intent(bounds: [new PortableQueryBound(TestVocabulary.CandidatesDimension, 10_000)]));
        Reach(
            new TestVocabulary
            {
                RequiredBounds = [TestVocabulary.CandidatesDimension],
            },
            Intent());
        Reach(packageLike, Intent(stages: [PortableQueryStage.Top(5)]));
        Reach(withRanking, Intent(order: [Named(PortableQueryOrderRole.Baseline, "gone")]));
        Reach(withRanking, Intent(order:
        [
            Fields(PortableQueryOrderRole.Baseline, TestVocabulary.OpaqueKey),
        ]));
        Reach(withRanking, Intent(
            stages: [PortableQueryStage.Top(5)],
            order: [Named(PortableQueryOrderRole.ForStage(0), TestVocabulary.SequenceOrder)]));
        Reach(withRanking, Intent(stages: [PortableQueryStage.Top(5)]));

        Assert.Equal(
            Enum.GetValues<PortableQueryFailureReason>().Order().ToArray(),
            reached.Keys.Order().ToArray());

        // Every reason sits at its own location and carries its own offender.
        foreach ((PortableQueryFailureReason reason, PortableQueryFailure failure) in reached)
        {
            Assert.Equal(PortableQueryFailure.PartOf(reason), failure.Location.Part);
            Assert.Equal(PortableQueryFailure.CarriesOffender(reason), failure.Offender is not null);
        }
    }

    /// <summary>
    /// A field-list order names the failing term, because the operation alone
    /// does not say which of its terms failed.
    /// </summary>
    [Fact]
    public void FieldListFailuresNameTheTerm()
    {
        var vocabulary = new TestVocabulary();

        PortableQueryFailure failure = Resolve(
            vocabulary,
            Intent(order:
            [
                Fields(
                    PortableQueryOrderRole.Baseline,
                    TestVocabulary.NameKey,
                    TestVocabulary.OpaqueKey),
            ])).Failure;

        Assert.Equal(PortableQueryFailureReason.OrderReferenceNotOrderable, failure.Reason);
        Assert.Equal(0, failure.Location.Index);
        Assert.Equal(1, failure.Location.FieldTermIndex);
        Assert.Equal(TestVocabulary.OpaqueKey, failure.Offender);
    }

    /// <summary>Cancellation is observed before any binder runs.</summary>
    [Fact]
    public void CancellationIsObservedBeforeAnyBinder()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => PortableQueryResolver.Resolve(
            "test.query",
            new TestVocabulary(new AcquisitionCapability()),
            Intent(terms: [Term(TestVocabulary.DependsKey, "Serilog")]),
            source.Token));
    }

    /// <summary>Resolves against a vocabulary this build does not offer.</summary>
    private static PortableQueryResolution<TestPlan> ResolveUnknown(
        PortableQueryIntent intent) =>
        PortableQueryResolver.Resolve<TestPredicate, TestPlan>(
            "gone.query",
            null,
            intent,
            TestContext.Current.CancellationToken);

    private static PortableQueryResolution<TestPlan> Resolve(
        TestVocabulary vocabulary,
        PortableQueryIntent intent) =>
        PortableQueryResolver.Resolve(
            vocabulary.Identity,
            vocabulary,
            intent,
            TestContext.Current.CancellationToken);

    private static PortableQueryIntent Intent(
        IReadOnlyList<PortableQueryTerm>? terms = null,
        IReadOnlyList<PortableQueryBound>? bounds = null,
        IReadOnlyList<PortableQueryStage>? stages = null,
        IReadOnlyList<PortableQueryOrderOperation>? order = null) =>
        PortableQueryIntent.Create(terms ?? [], bounds ?? [], stages ?? [], order ?? []);

    private static PortableQueryTerm Term(
        string key,
        string value,
        PortableQueryOperator @operator = PortableQueryOperator.Equal) =>
        new(key, @operator, value);

    private static PortableQueryOrderOperation Named(
        PortableQueryOrderRole role,
        string reference) =>
        PortableQueryOrderOperation.Named(role, reference, PortableQueryDirection.Ascending);

    private static PortableQueryOrderOperation Fields(
        PortableQueryOrderRole role,
        params string[] keys) =>
        PortableQueryOrderOperation.Fields(
            role,
            [.. keys.Select(key => new PortableQueryOrderTerm(key, PortableQueryDirection.Ascending))]);
}
