using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Findings;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Pilot gates for the finding spine (#2564/#2585): the domain-free alignment engine, the atom
/// (<see cref="Finding{T}"/>) / pair (<see cref="PairFinding{T}"/>) split, and the IL adapter. The
/// engine's committed core is an LCS (so it reproduces the existing IL sequence diff on move-free
/// inputs); the move pass recovers relocations; equivalence is a consumer fold over the pairs.
/// </summary>
public class FindingPilotTests
{
    // ---- Leaf engine: synthetic key/atom streams ---------------------------------------------

    static readonly FindingSubject Subject = new("test", "test");
    static readonly FindingDescriptor Descriptor = new("test.item", "item");

    // Matcher-only tests need just the alignment keys.
    static FindingKey[] Keys(params string[] keys) => [.. keys.Select(k => new FindingKey(k))];

    // Fold tests need atoms: payload is the key string, position is the stream index.
    static ImmutableArray<Finding<string>> Atoms(params string[] keys)
    {
        var builder = ImmutableArray.CreateBuilder<Finding<string>>(keys.Length);
        for (int i = 0; i < keys.Length; i++)
            builder.Add(new Finding<string>(Subject, Descriptor, new FindingKey(keys[i]), i, keys[i]));
        return builder.MoveToImmutable();
    }

    static int Count(FindingMatch c, FindingEdgeKind kind) => c.Edges.Count(e => e.Kind == kind);

    [Fact]
    public void FindingInspection_CasesRemainDistinctWithAnEmptyCensus()
    {
        FindingInspection<string> complete = new FindingInspection<string>.Complete([]);
        FindingInspection<string> absent = new FindingInspection<string>.Absent();
        var error = new InspectionError(Subject, Descriptor, "declined");
        FindingInspection<string> failed = new FindingInspection<string>.Failed(error);

        Assert.True(complete is FindingInspection<string>.Complete);
        Assert.True(absent is FindingInspection<string>.Absent);
        Assert.True(failed is FindingInspection<string>.Failed);
        Assert.Equal("declined", error.Reason);
    }

    [Fact]
    public void FindingContracts_KeepObservationsTransitionsAndFailuresDistinct()
    {
        var stringFinding = Atoms("a")[0];
        var intFinding = new Finding<int>(
            Subject,
            Descriptor,
            new FindingKey("1"),
            0,
            1);
        IFinding[] observations = [stringFinding, intFinding];
        IPairFinding[] transitions =
        [
            new PairFinding<string>.Added(stringFinding),
            new PairFinding<int>.Added(intFinding),
        ];

        Assert.Equal(2, observations.Length);
        Assert.Equal(2, transitions.Length);
        Assert.False(typeof(IFinding).IsAssignableFrom(typeof(PairFinding<string>)));
        Assert.False(typeof(IFinding).IsAssignableFrom(typeof(PairFinding<string>.Added)));
        Assert.False(typeof(IFinding).IsAssignableFrom(typeof(InspectionError)));
    }

    [Fact]
    public void FindingInspection_CasesRejectInvalidPayloads()
    {
        Assert.Throws<ArgumentException>(
            () => new FindingInspection<string>.Complete(default));
        Assert.Throws<ArgumentException>(
            () => new FindingInspection<string>.Complete([null!]));
        Assert.Throws<ArgumentNullException>(
            () => new FindingInspection<string>.Failed(null!));
        Assert.Throws<ArgumentNullException>(
            () => new InspectionError(null!, Descriptor, "declined"));
        Assert.Throws<ArgumentNullException>(
            () => new InspectionError(Subject, null!, "declined"));
        Assert.Throws<ArgumentNullException>(
            () => new InspectionError(Subject, Descriptor, null!));
    }

    [Fact]
    public void FindingPrimitives_RejectInvalidIdentityAndPayloadStates()
    {
        Assert.Throws<ArgumentNullException>(() => new FindingSubject(null!, "subject"));
        Assert.Throws<ArgumentException>(() => new FindingSubject("", "subject"));
        Assert.Throws<ArgumentNullException>(() => new FindingSubject("subject", null!));
        Assert.Throws<ArgumentNullException>(() => new FindingDescriptor(null!, "item"));
        Assert.Throws<ArgumentException>(() => new FindingDescriptor("", "item"));
        Assert.Throws<ArgumentNullException>(() => new FindingDescriptor("test.item", null!));
        Assert.Throws<ArgumentNullException>(() => new FindingKey(null!));
        Assert.Throws<ArgumentNullException>(
            () => new Finding<string>(null!, Descriptor, new FindingKey("k"), 0, "k"));
        Assert.Throws<ArgumentNullException>(
            () => new Finding<string>(Subject, null!, new FindingKey("k"), 0, "k"));
        Assert.Throws<ArgumentException>(
            () => new Finding<string>(Subject, Descriptor, default, 0, "k"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Finding<string>(Subject, Descriptor, new FindingKey("k"), -1, "k"));
        Assert.Throws<ArgumentNullException>(
            () => new Finding<string>(Subject, Descriptor, new FindingKey("k"), 0, null!));

        // Empty content is a valid identity for an empty logical text line.
        Assert.Equal("", new FindingKey("").IdentityKey);
    }

    [Fact]
    public void FindingComparison_CasesEnforceRanVersusNeverRan()
    {
        FindingInspection<string> completeInspection =
            new FindingInspection<string>.Complete([]);
        FindingInspection<string> absentInspection =
            new FindingInspection<string>.Absent();
        FindingInspection<string> failedInspection =
            new FindingInspection<string>.Failed(
                new InspectionError(Subject, Descriptor, "declined"));

        FindingComparison<string> complete =
            FindingComparison.Compare(absentInspection, absentInspection);
        FindingComparison<string> failed =
            FindingComparison.Compare(failedInspection, completeInspection);

        Assert.True(complete is FindingComparison<string>.Complete);
        Assert.True(complete.IsExact);
        Assert.Null(complete.Failure);
        Assert.True(failed is FindingComparison<string>.Failed);
        Assert.False(failed.IsExact);
        Assert.Equal("old: declined", failed.Failure);
    }

    [Fact]
    public void FindingComparison_DifferentCensusesCannotReportExact()
    {
        FindingInspection<string> oldInspection =
            new FindingInspection<string>.Complete(Atoms("old"));
        FindingInspection<string> newInspection =
            new FindingInspection<string>.Complete(Atoms("new"));

        var comparison = FindingComparison.Compare(oldInspection, newInspection);

        Assert.False(comparison.IsExact);
        var complete = comparison switch
        {
            FindingComparison<string>.Complete value => value,
            FindingComparison<string>.Failed failed => throw new Xunit.Sdk.XunitException(failed.Failure),
        };
        Assert.Equal(1, complete.Pairs.Count(pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, complete.Pairs.Count(pair => pair.Kind == PairKind.Removed));
    }

    [Fact]
    public void FindingComparison_TransformationMustPreserveAlignmentAtoms()
    {
        FindingInspection<string> inspection =
            new FindingInspection<string>.Complete(Atoms("same"));
        var comparison = FindingComparison.Compare(inspection, inspection);

        Assert.Throws<ArgumentException>(() => comparison.TransformPairs(_ => []));
        Assert.Throws<ArgumentException>(() => comparison.TransformPairs(
            pairs =>
            [
                new PairFinding<string>.Present(
                    Atoms("replacement")[0],
                    Atoms("replacement")[0]),
            ]));
    }

    [Fact]
    public void FindingComparison_FailedDerivesBothInspectionErrors()
    {
        FindingInspection<string> oldFailed =
            new FindingInspection<string>.Failed(
                new InspectionError(Subject, Descriptor, "old failure"));
        FindingInspection<string> newFailed =
            new FindingInspection<string>.Failed(
                new InspectionError(Subject, Descriptor, "new failure"));

        FindingComparison<string> comparison =
            FindingComparison.Compare(oldFailed, newFailed);

        Assert.Equal("old: old failure; new: new failure", comparison.Failure);
    }

    [Fact]
    public void CommittedCore_IsAnOptimalLcs_OnManyShapes()
    {
        (string[] Old, string[] New)[] cases =
        [
            (["a", "b", "c"], ["a", "b", "c"]),
            (["a", "b", "c"], ["a", "x", "c"]),
            (["a", "b", "c", "d"], ["a", "c"]),
            (["a"], ["b", "a", "c"]),
            (["a", "b", "c", "d", "e"], ["e", "d", "c", "b", "a"]),
            ([], ["a", "b"]),
            (["a", "b"], []),
            (["a", "a", "b", "a"], ["a", "b", "a", "a"]),
        ];

        foreach (var (old, @new) in cases)
        {
            var match = FindingMatcher.Match(Keys(old), Keys(@new));
            var matched = match.Edges.Where(e => e.Kind == FindingEdgeKind.Matched).ToArray();

            Assert.Equal(ReferenceLcsLength(old, @new), matched.Length);

            for (int i = 1; i < matched.Length; i++)
            {
                Assert.True(matched[i].OldIndex > matched[i - 1].OldIndex);
                Assert.True(matched[i].NewIndex > matched[i - 1].NewIndex);
            }

            foreach (var edge in matched)
                Assert.Equal(old[edge.OldIndex], @new[edge.NewIndex]);
        }
    }

    [Fact]
    public void MovePass_RelocatedBlock_IsDetectedAsMoved()
    {
        var match = FindingMatcher.Match(
            Keys("A", "B", "C", "m1", "m2", "D", "E"),
            Keys("m1", "m2", "A", "B", "C", "D", "E"));

        Assert.Equal(2, Count(match, FindingEdgeKind.Moved));
        Assert.Equal(0, Count(match, FindingEdgeKind.Added));
        Assert.Equal(0, Count(match, FindingEdgeKind.Removed));
    }

    [Fact]
    public void MismatchResistance_CoincidentalSingleton_StaysAddedRemoved()
    {
        var match = FindingMatcher.Match(Keys("c0", "c1", "c2"), Keys("c1", "c2", "c0"));

        Assert.Equal(0, Count(match, FindingEdgeKind.Moved));
        Assert.Equal(1, Count(match, FindingEdgeKind.Added));
        Assert.Equal(1, Count(match, FindingEdgeKind.Removed));
        var candidate = Assert.Single(match.MoveCandidates);
        Assert.Equal(50, candidate.Confidence);
    }

    [Fact]
    public void Acceptance_IsAConsumerFold_LoweringThresholdPromotesFringeToMove()
    {
        var old = Atoms("c0", "c1", "c2");
        var @new = Atoms("c1", "c2", "c0");
        var match = FindingMatcher.Match(old.Keys(), @new.Keys());

        var strict = FindingFold.ToPairs(match, old, @new);
        Assert.Equal(0, strict.Count(p => p.Difference == FindingDifferenceKind.Moved));
        Assert.Equal(1, strict.Count(p => p.Kind == PairKind.Added));
        Assert.Equal(1, strict.Count(p => p.Kind == PairKind.Removed));

        var recall = FindingFold.ToPairs(match, old, @new, acceptanceThreshold: 50);
        Assert.Equal(1, recall.Count(p => p.Difference == FindingDifferenceKind.Moved));
        Assert.Equal(0, recall.Count(p => p.Kind == PairKind.Added));
        Assert.Equal(0, recall.Count(p => p.Kind == PairKind.Removed));
    }

    [Fact]
    public void ScopeCorroboration_RaisesFringeScore()
    {
        var old = new[] { new FindingKey("c0", "loop1"), new FindingKey("c1"), new FindingKey("c2") };
        var @new = new[] { new FindingKey("c1"), new FindingKey("c2"), new FindingKey("c0", "loop1") };

        var match = FindingMatcher.Match(old, @new);
        var candidate = Assert.Single(match.MoveCandidates);
        Assert.Equal(75, candidate.Confidence);
        Assert.Equal("content+scope", candidate.Reason);
    }

    [Fact]
    public void EquivalenceFolds_MoveIsExactDiffButMultisetEquivalent()
    {
        var old = Atoms("A", "B", "C", "m1", "m2", "D", "E");
        var @new = Atoms("m1", "m2", "A", "B", "C", "D", "E");
        var match = FindingMatcher.Match(old.Keys(), @new.Keys());
        var pairs = FindingFold.ToPairs(match, old, @new);

        Assert.False(FindingEquivalence.Exact.IsEquivalent(pairs));
        Assert.True(FindingEquivalence.Multiset.IsEquivalent(pairs));
    }

    [Fact]
    public void IdenticalStreams_AreExact()
    {
        var stream = Atoms("a", "b", "c", "d");
        var match = FindingMatcher.Match(stream.Keys(), stream.Keys());
        var pairs = FindingFold.ToPairs(match, stream, stream);

        Assert.All(pairs, p => Assert.Equal(PairKind.Present, p.Kind));
        Assert.True(FindingEquivalence.Exact.IsEquivalent(pairs));
    }

    [Fact]
    public void MovePass_NonContiguousSingletons_AreNotAFalseBlockMove()
    {
        var match = FindingMatcher.Match(Keys("M1", "A", "M2", "B"), Keys("A", "B", "M1", "M2"));

        Assert.Equal(0, Count(match, FindingEdgeKind.Moved));
        Assert.Equal(2, Count(match, FindingEdgeKind.Added));
        Assert.Equal(2, Count(match, FindingEdgeKind.Removed));
    }

    [Fact]
    public void Fringe_EmitsFullCandidateGraph_NotIndexOrderThinned()
    {
        var old = Keys("A", "B");
        var @new = Keys("B", "A", "A");

        var match = FindingMatcher.Match(old, @new);

        Assert.Equal(2, match.MoveCandidates.Length);
        Assert.All(match.MoveCandidates, c => Assert.Equal("A", old[c.OldIndex].IdentityKey));
        Assert.All(match.MoveCandidates, c => Assert.Equal("A", @new[c.NewIndex].IdentityKey));
    }

    [Fact]
    public void Match_IsDeterministic_UnderAmbiguity()
    {
        var old = Keys("x", "a", "y", "a", "z", "a");
        var @new = Keys("a", "z", "a", "x", "a", "y");

        var first = FindingMatcher.Match(old, @new);
        var second = FindingMatcher.Match(old, @new);

        Assert.Equal(first.Edges, second.Edges);
        Assert.Equal(first.MoveCandidates, second.MoveCandidates);
    }

    [Fact]
    public void Match_RejectsStreamsTooLargeForOrderedMatrix()
    {
        var big = Enumerable.Range(0, 8000)
            .Select(i => new FindingKey(i.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();

        var ex = Assert.Throws<ArgumentException>(() => FindingMatcher.Match(big, big));
        Assert.Contains("identity-set", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_OnEmptyStreams_IsEmptyMatch()
    {
        var match = FindingMatcher.Match(Keys(), Keys());
        Assert.True(match.Edges.IsEmpty);
        Assert.True(match.MoveCandidates.IsEmpty);
    }

    [Fact]
    public void Match_MaterializesLazyKeyEnumerable()
    {
        // The matcher accepts a lazy IEnumerable<FindingKey> and materializes the ordered path
        // once; a filtered (non-array) sequence must match identically to the array form.
        IEnumerable<FindingKey> old = Keys("a", "b", "c").Where(_ => true);
        IEnumerable<FindingKey> @new = Keys("a", "b", "c").Where(_ => true);

        var match = FindingMatcher.Match(old, @new);
        Assert.Equal(3, Count(match, FindingEdgeKind.Matched));
    }

    [Fact]
    public void Match_NullStream_Throws()
        => Assert.Throws<ArgumentNullException>(() => FindingMatcher.Match(null!, Keys("a")));

    static readonly FindingMatchOptions IdentitySet = new() { MatchMode = FindingMatchMode.IdentitySet };

    [Fact]
    public void IdentitySet_Permutation_IsAllMatched_NoAddRemoveOrMove()
    {
        // Order is not semantic for a set: a permutation is a non-event, unlike the ordered matcher
        // (which reports the relocation as moves or add/remove). Every element matches by identity key.
        var old = Keys("a", "b", "c", "d");
        var @new = Keys("d", "b", "a", "c");

        var match = FindingMatcher.Match(old, @new, IdentitySet);

        Assert.Equal(4, Count(match, FindingEdgeKind.Matched));
        Assert.Equal(0, Count(match, FindingEdgeKind.Added));
        Assert.Equal(0, Count(match, FindingEdgeKind.Removed));
        Assert.Equal(0, Count(match, FindingEdgeKind.Moved));
        Assert.True(match.MoveCandidates.IsEmpty);
    }

    [Fact]
    public void IdentitySet_DistinctMultisets_AreMatchedByIntersection()
    {
        // old {A,B,C,C} vs new {C,C,D,B}: multiset intersection {B,C,C} matches; old-only {A} removed;
        // new-only {D} added. Duplicates are matched by count, not collapsed.
        var old = Keys("A", "B", "C", "C");
        var @new = Keys("C", "C", "D", "B");

        var match = FindingMatcher.Match(old, @new, IdentitySet);

        Assert.Equal(3, Count(match, FindingEdgeKind.Matched));
        Assert.Equal(1, Count(match, FindingEdgeKind.Removed));
        Assert.Equal(1, Count(match, FindingEdgeKind.Added));
        Assert.Equal(0, Count(match, FindingEdgeKind.Moved));
    }

    [Fact]
    public void IdentitySet_DuplicateKeys_MatchByMultisetCount()
    {
        var match = FindingMatcher.Match(Keys("A", "A", "A"), Keys("A", "A"), IdentitySet);

        Assert.Equal(2, Count(match, FindingEdgeKind.Matched));
        Assert.Equal(1, Count(match, FindingEdgeKind.Removed));
        Assert.Equal(0, Count(match, FindingEdgeKind.Added));
    }

    [Fact]
    public void IdentitySet_EmptyAndOneSided_AreWhollyAddedOrRemoved()
    {
        Assert.Empty(FindingMatcher.Match(Keys(), Keys(), IdentitySet).Edges);
        Assert.Equal(2, Count(FindingMatcher.Match(Keys(), Keys("a", "b"), IdentitySet), FindingEdgeKind.Added));
        Assert.Equal(2, Count(FindingMatcher.Match(Keys("a", "b"), Keys(), IdentitySet), FindingEdgeKind.Removed));
    }

    [Fact]
    public void IdentitySet_ScalesPastTheOrderedGuard_AndIsDeterministic()
    {
        // A stream large enough that the ordered committer's O(N*M) matrix trips its cell cap; the
        // identity-set committer has no matrix, so it matches all of it.
        var big = Keys([.. Enumerable.Range(0, 9000).Select(i => $"k{i}")]);

        Assert.Throws<ArgumentException>(() => FindingMatcher.Match(big, big));

        var first = FindingMatcher.Match(big, big, IdentitySet);
        Assert.Equal(9000, Count(first, FindingEdgeKind.Matched));
        Assert.Equal(0, Count(first, FindingEdgeKind.Added));
        Assert.Equal(0, Count(first, FindingEdgeKind.Removed));

        // Deterministic: the same inputs yield the same edge sequence.
        var second = FindingMatcher.Match(big, big, IdentitySet);
        Assert.Equal(first.Edges, second.Edges);
    }

    [Fact]
    public void UninitializedIdentityKeys_AreRejectedByBothMatchers()
    {
        var stream = new FindingKey[] { default, default };

        Assert.Throws<ArgumentException>(() => FindingMatcher.Match(stream, stream, IdentitySet));
        Assert.Throws<ArgumentException>(() => FindingMatcher.Match(stream, stream));
    }

    [Fact]
    public void IdentitySet_FoldsThroughToSummary_EndToEnd()
    {
        // Locks the full set pipeline the Metadata/Analysis producers will build on:
        // match -> FindingFold.ToPairs -> FindingSummary.
        var old = Atoms("a", "b", "c", "d");
        var permuted = Atoms("d", "a", "c", "b");
        var permutedPairs = FindingFold.ToPairs(FindingMatcher.Match(old.Keys(), permuted.Keys(), IdentitySet), old, permuted);
        Assert.All(permutedPairs, p => Assert.Equal(PairKind.Present, p.Kind));
        // A permuted set is a non-event: the summary reads Identical (no moves, no add/remove).
        Assert.Equal(DiffShape.Identical, FindingSummary.Summarize(permutedPairs).Shape);

        var changedNew = Atoms("a", "c");
        var oldSmall = Atoms("a", "b");
        var changedPairs = FindingFold.ToPairs(FindingMatcher.Match(oldSmall.Keys(), changedNew.Keys(), IdentitySet), oldSmall, changedNew);
        var summary = FindingSummary.Summarize(changedPairs);
        Assert.Equal(1, summary.Added);
        Assert.Equal(1, summary.Removed);
    }

    [Fact]
    public void PairFinding_CasesFixTheirKind_AndBothNullIsUnrepresentable()
    {
        var a = new Finding<string>(Subject, Descriptor, new FindingKey("k"), 0, "k");
        var b = new Finding<string>(Subject, Descriptor, new FindingKey("k"), 1, "k");

        // Each union case fixes its own polarity; there is no Kind field to set out of step with the
        // sides. The cases convert implicitly to the union.
        Assert.Equal(PairKind.Present, ((PairFinding<string>)new PairFinding<string>.Present(a, b)).Kind);
        Assert.Equal(PairKind.Changed, ((PairFinding<string>)new PairFinding<string>.Changed(a, b)).Kind);
        Assert.Equal(PairKind.Added, ((PairFinding<string>)new PairFinding<string>.Added(b)).Kind);
        Assert.Equal(PairKind.Removed, ((PairFinding<string>)new PairFinding<string>.Removed(a)).Kind);

        // A both-null pair is not a runtime error to reject — it is unrepresentable: every case
        // requires the atom(s) it carries, so there is no constructor that takes neither side.
        // (Uncommenting the next line would be a compile error, which is the point.)
        //   _ = new PairFinding<string>(null, null);

        // Value equality holds across the union (record class over its case).
        Assert.Equal((PairFinding<string>)new PairFinding<string>.Present(a, b), new PairFinding<string>.Present(a, b));
    }

    [Fact]
    public void PairFinding_Cases_RejectNullAtoms_FailClosed()
    {
        var a = new Finding<string>(Subject, Descriptor, new FindingKey("k"), 0, "k");

        // A plain null literal is already a compile error under the non-null annotations; a
        // null-forgiving caller is caught at construction so a case can never hold a missing side.
        Assert.Throws<ArgumentNullException>(() => new PairFinding<string>.Added(null!));
        Assert.Throws<ArgumentNullException>(() => new PairFinding<string>.Removed(null!));
        Assert.Throws<ArgumentNullException>(() => new PairFinding<string>.Present(a, null!));
        Assert.Throws<ArgumentNullException>(() => new PairFinding<string>.Present(null!, a));
        Assert.Throws<ArgumentNullException>(() => new PairFinding<string>.Changed(a, null!));

        // The union wrapper likewise never holds a null case.
        Assert.Throws<ArgumentNullException>(() => new PairFinding<string>((PairFinding<string>.Added)null!));
    }

    [Fact]
    public void SkeletonConsumer_ClassifiesFromPolarityAndDifferenceAlone()
    {
        // A stream-agnostic consumer: it reads only the pair skeleton, so the same pairs could have
        // come from IL, C#, or a fact producer. Here we drive it from the IL adapter.
        var identical = Body(Ldc0, Ldc1, Ret);
        var identicalPairs = CompleteComparison(
            IlFindings.Compare(identical, null, identical, null, IlSubject)).Pairs;
        Assert.Equal(DiffShape.Identical, FindingSummary.Summarize(identicalPairs).Shape);

        var old = Body(Ldc0, Ldc1, Ldc2, Ldc3, Ldc4, Ldc5, Ldc6, Ret);
        var reordered = Body(Ldc3, Ldc4, Ldc0, Ldc1, Ldc2, Ldc5, Ldc6, Ret);
        var reorderPairs = CompleteComparison(
            IlFindings.Compare(old, null, reordered, null, IlSubject)).Pairs;
        var reorderSummary = FindingSummary.Summarize(reorderPairs);
        Assert.Equal(DiffShape.ReorderOnly, reorderSummary.Shape);
        Assert.Equal(2, reorderSummary.Moved);

        var changed = Body(Ldc0, Ldc2, Ldc3, Ret);
        var changedPairs = CompleteComparison(
            IlFindings.Compare(old, null, changed, null, IlSubject)).Pairs;
        Assert.Equal(DiffShape.Structural, FindingSummary.Summarize(changedPairs).Shape);
    }

    [Fact]
    public void SkeletonConsumer_IsStreamAgnostic_OverHandBuiltPairs()
    {
        // Pairs fabricated as if from an arbitrary (non-IL) stream: the consumer does not care.
        var subject = new FindingSubject("s", "s");
        var descriptor = new FindingDescriptor("any.kind", "any");
        Finding<string> Atom(string key, int position) => new(subject, descriptor, new FindingKey(key), position, key);

        IPairFinding[] pairs =
        [
            new PairFinding<string>.Present(Atom("k0", 0), Atom("k0", 0)),
            new PairFinding<string>.Added(Atom("k1", 1)),
        ];

        var summary = FindingSummary.Summarize(pairs);
        Assert.Equal(DiffShape.Structural, summary.Shape);
        Assert.Equal(1, summary.Added);
        Assert.Equal(1, summary.Present);
    }

    static int ReferenceLcsLength(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var dp = new int[a.Count + 1, b.Count + 1];
        for (int i = a.Count - 1; i >= 0; i--)
        {
            for (int j = b.Count - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        }

        return dp[0, 0];
    }

    [Fact]
    public void IlFindings_Inspect_DistinguishesCompleteAbsentAndFailed()
    {
        var body = Body(Ldc0, Ldc1, Ldc2, Ret);
        var malformed = MethodInstructions.Decode(
            [],
            ilLength: 1,
            Array.Empty<ExceptionRegion>());

        var complete = IlFindings.Inspect(body, null, IlSubject) switch
        {
            FindingInspection<CanonicalIlOperation>.Complete value => value,
            _ => throw new InvalidOperationException("Expected a complete IL inspection."),
        };
        var absent = IlFindings.Inspect(null, null, IlSubject);
        var failed = IlFindings.Inspect(malformed, null, IlSubject);

        Assert.Contains(complete.Findings, f => f.Descriptor.Id == "il.op");
        Assert.Equal(
            Enumerable.Range(0, complete.Findings.Length),
            complete.Findings.Select(f => f.Position));
        Assert.True(absent is FindingInspection<CanonicalIlOperation>.Absent);
        Assert.True(failed is FindingInspection<CanonicalIlOperation>.Failed);
    }

    [Fact]
    public void IlFindings_Compare_AbsentBodiesIsAnExactCompletedComparison()
    {
        var result = CompleteComparison(
            IlFindings.Compare(null, null, null, null, IlSubject));

        Assert.True(result.OldInspection is FindingInspection<CanonicalIlOperation>.Absent);
        Assert.True(result.NewInspection is FindingInspection<CanonicalIlOperation>.Absent);
        Assert.Empty(result.Pairs);
        Assert.True(result.IsExact);
    }

    [Fact]
    public void IlFindings_Compare_InspectionFailureDoesNotManufactureAMatch()
    {
        var malformed = MethodInstructions.Decode(
            [],
            ilLength: 1,
            Array.Empty<ExceptionRegion>());

        var result = IlFindings.Compare(
            malformed,
            null,
            Body(Ret),
            null,
            IlSubject);

        Assert.True(result is FindingComparison<CanonicalIlOperation>.Failed);
        Assert.NotNull(result.Failure);
    }

    // ---- IL adapter: real decode + raw-byte fixtures -----------------------------------------

    static MethodInstructions Body(params byte[] il)
        => MethodInstructions.Decode(il, il.Length, Array.Empty<ExceptionRegion>());

    static MethodInstructions OverLimitBody()
    {
        var il = new byte[IlFindings.MaxCanonicalOperations + 1];
        il[^1] = Ret;
        return Body(il);
    }

    const byte Ldc0 = 0x16, Ldc1 = 0x17, Ldc2 = 0x18, Ldc3 = 0x19, Ldc4 = 0x1A, Ldc5 = 0x1B, Ldc6 = 0x1C, Ret = 0x2A;
    const byte BrS = 0x2B, Nop = 0x00;

    static readonly FindingSubject IlSubject = new("M", "M");

    [Fact]
    public void IlFindings_Inspect_OverLimitBodyFailsBeforeMatching()
    {
        var inspection = IlFindings.Inspect(OverLimitBody(), null, IlSubject);

        var failed = inspection switch
        {
            FindingInspection<CanonicalIlOperation>.Failed value => value,
            _ => throw new InvalidOperationException("Expected a failed IL inspection."),
        };
        Assert.Equal(IlFindings.InspectionDescriptor, failed.Error.Descriptor);
        Assert.Contains("limit", failed.Error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void IlFindings_Compare_OneSidedOverLimitFailsWithoutAddRemovePairs()
    {
        var result = IlFindings.Compare(
            OverLimitBody(),
            null,
            Body(Ret),
            null,
            IlSubject);

        var failed = result switch
        {
            FindingComparison<CanonicalIlOperation>.Failed value => value,
            _ => throw new InvalidOperationException("Expected a failed IL comparison."),
        };
        Assert.True(failed.OldInspection is FindingInspection<CanonicalIlOperation>.Failed);
        Assert.True(failed.NewInspection is FindingInspection<CanonicalIlOperation>.Complete);
        Assert.Contains("old:", failed.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain("new:", failed.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void IlFindings_Compare_TwoSidedOverLimitFailsWithoutMatcherException()
    {
        var result = IlFindings.Compare(
            OverLimitBody(),
            null,
            OverLimitBody(),
            null,
            IlSubject);

        var failed = result switch
        {
            FindingComparison<CanonicalIlOperation>.Failed value => value,
            _ => throw new InvalidOperationException("Expected a failed IL comparison."),
        };
        Assert.True(failed.OldInspection is FindingInspection<CanonicalIlOperation>.Failed);
        Assert.True(failed.NewInspection is FindingInspection<CanonicalIlOperation>.Failed);
        Assert.Contains("old:", failed.Failure, StringComparison.Ordinal);
        Assert.Contains("new:", failed.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void IlFindings_BranchRetarget_IsChangedNotExact()
    {
        var old = Body(BrS, 0x03, Nop, Ret, Nop, Ret);
        var @new = Body(BrS, 0x01, Nop, Ret, Nop, Ret);

        var result = CompleteComparison(
            IlFindings.Compare(old, null, @new, null, IlSubject));

        Assert.False(result.IsExact);
        Assert.Contains(result.Pairs, p => p.Kind == PairKind.Changed);
        Assert.False(IlBodyDiff.Compare(old, @new).IsExact);
    }

    [Fact]
    public void IlFindings_BranchInsideMovedBlock_IsReorderNotRetarget()
    {
        var old = Body(Nop, Nop, Nop, Ldc0, BrS, 0xFD, Ret);
        var @new = Body(Ldc0, BrS, 0xFD, Nop, Nop, Nop, Ret);

        var result = CompleteComparison(
            IlFindings.Compare(old, null, @new, null, IlSubject));

        Assert.DoesNotContain(result.Pairs, p => p.Kind == PairKind.Changed);
        Assert.Equal(2, result.Pairs.Count(p => p.Difference == FindingDifferenceKind.Moved));
        Assert.True(FindingEquivalence.Multiset.IsEquivalent(result.Pairs));
    }

    [Fact]
    public void IlFindings_Exactness_AgreesWithIlBodyDiff_AcrossFixtures()
    {
        (byte[] Old, byte[] New)[] pairs =
        [
            ([Ldc0, Ldc1, Ret], [Ldc0, Ldc1, Ret]),
            ([Ldc0, Ldc1, Ret], [Ldc0, Ldc2, Ret]),
            ([BrS, 0x03, Nop, Ret, Nop, Ret], [BrS, 0x01, Nop, Ret, Nop, Ret]),
            ([BrS, 0x00, Ldc0, Ret], [BrS, 0x00, Ldc1, Ret]),
            ([Ldc0, Ldc1, Ldc2, Ret], [Ldc0, Ldc2, Ret]),
        ];

        foreach (var (oldBytes, newBytes) in pairs)
        {
            var old = Body(oldBytes);
            var @new = Body(newBytes);
            bool findingExact = IlFindings.Compare(old, null, @new, null, IlSubject).IsExact;
            bool classicExact = IlBodyDiff.Compare(old, @new).IsExact;
            Assert.Equal(classicExact, findingExact);
        }
    }

    [Fact]
    public void IlFindings_BranchTargetContentChangedButSlotStable_BranchIsNotRetargeted()
    {
        var old = Body(BrS, 0x00, Ldc0, Ret);
        var @new = Body(BrS, 0x00, Ldc1, Ret);

        var result = CompleteComparison(
            IlFindings.Compare(old, null, @new, null, IlSubject));

        Assert.DoesNotContain(result.Pairs, p => p.Kind == PairKind.Changed);
        Assert.Equal(1, result.Pairs.Count(p => p.Kind == PairKind.Added));
        Assert.Equal(1, result.Pairs.Count(p => p.Kind == PairKind.Removed));
    }

    [Fact]
    public void IlFindings_SameBranch_IsExact()
    {
        var body = Body(BrS, 0x01, Nop, Ret, Nop, Ret);
        var result = CompleteComparison(
            IlFindings.Compare(body, null, body, null, IlSubject));

        Assert.True(result.IsExact);
    }

    [Fact]
    public void IlFindings_SameBody_IsExact()
    {
        var body = Body(Ldc0, Ldc1, Ldc2, Ret);
        var result = CompleteComparison(
            IlFindings.Compare(body, null, body, null, IlSubject));

        Assert.True(result.IsExact);
        Assert.All(result.Pairs, p => Assert.Equal(PairKind.Present, p.Kind));
    }

    [Fact]
    public void IlFindings_RelocatedBlock_IsDetectedAsMoved()
    {
        var old = Body(Ldc0, Ldc1, Ldc2, Ldc3, Ldc4, Ldc5, Ldc6, Ret);
        var @new = Body(Ldc3, Ldc4, Ldc0, Ldc1, Ldc2, Ldc5, Ldc6, Ret);

        var result = CompleteComparison(
            IlFindings.Compare(old, null, @new, null, IlSubject));

        Assert.Equal(2, result.Pairs.Count(p => p.Difference == FindingDifferenceKind.Moved));
        Assert.Equal(0, result.Pairs.Count(p => p.Kind is PairKind.Added or PairKind.Removed));
        Assert.False(result.IsExact);
        Assert.True(FindingEquivalence.Multiset.IsEquivalent(result.Pairs));
    }

    [Fact]
    public void IlFindings_CommittedCore_ReproducesIlBodyDiffResidual()
    {
        var old = Body(Ldc0, Ldc1, Ldc2, Ret);
        var @new = Body(Ldc0, Ldc2, Ldc3, Ret);

        var classic = IlBodyDiff.Compare(old, @new);
        var finding = CompleteComparison(
            IlFindings.Compare(old, null, @new, null, IlSubject));

        Assert.Equal(0, finding.Pairs.Count(p => p.Difference == FindingDifferenceKind.Moved));

        var classicRemoved = classic.Rows
            .Where(r => r.Kind == IlDiffKind.Remove)
            .Select(r => IlFindings.GetIdentityKey(r.Operation))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(classicRemoved, ResidualKeys(finding, PairKind.Removed));

        var classicAdded = classic.Rows
            .Where(r => r.Kind == IlDiffKind.Add)
            .Select(r => IlFindings.GetIdentityKey(r.Operation))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(classicAdded, ResidualKeys(finding, PairKind.Added));
    }

    [Fact]
    public void IlFindings_MismatchResistance_SingletonStaysAddedRemoved()
    {
        var old = Body(Ldc0, Ldc1, Ldc2, Ret);
        var @new = Body(Ldc1, Ldc2, Ldc0, Ret);

        var result = CompleteComparison(
            IlFindings.Compare(old, null, @new, null, IlSubject));

        Assert.Equal(0, result.Pairs.Count(p => p.Difference == FindingDifferenceKind.Moved));
        Assert.Equal(1, result.Pairs.Count(p => p.Kind == PairKind.Added));
        Assert.Equal(1, result.Pairs.Count(p => p.Kind == PairKind.Removed));
    }

    static string[] ResidualKeys(
        FindingComparison<CanonicalIlOperation>.Complete result,
        PairKind polarity)
        => result.Pairs
            .Where(p => p.Kind == polarity)
            .Select(p => CurrentAtom(p).Key.IdentityKey)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

    static FindingComparison<CanonicalIlOperation>.Complete CompleteComparison(
        FindingComparison<CanonicalIlOperation> comparison)
        => comparison switch
        {
            FindingComparison<CanonicalIlOperation>.Complete complete => complete,
            FindingComparison<CanonicalIlOperation>.Failed failed => throw new InvalidOperationException(
                $"Expected a completed IL comparison: {failed.Failure}"),
        };

    // The representative atom of a pair (new side if present, else old), recovered by matching the
    // union directly (`pair switch`, not `pair.Value switch`) so the compiler enforces exhaustiveness:
    // adding a fifth case makes this fail to build rather than fall through at runtime.
    static Finding<CanonicalIlOperation> CurrentAtom(PairFinding<CanonicalIlOperation> pair) => pair switch
    {
        PairFinding<CanonicalIlOperation>.Added a => a.New,
        PairFinding<CanonicalIlOperation>.Removed r => r.Old,
        PairFinding<CanonicalIlOperation>.Present p => p.New,
        PairFinding<CanonicalIlOperation>.Changed c => c.New,
    };

    // ---- IL adapter: real-assembly corpus ----------------------------------------------------

    [Theory]
    [InlineData(typeof(IlBodyDiff))]
    [InlineData(typeof(FindingPilotTests))]
    [InlineData(typeof(System.Linq.Enumerable))]
    public void IlFindings_SelfCompare_IsExactAcrossWholeAssembly(Type anchorType)
    {
        using var stream = File.OpenRead(anchorType.Assembly.Location);
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        var reader = pe.GetMetadataReader();

        int canonicalized = 0;
        int declined = 0;
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            MethodInstructions body;
            try
            {
                body = MethodInstructions.Decode(pe.GetMethodBody(method.RelativeVirtualAddress));
            }
            catch (System.BadImageFormatException)
            {
                continue;
            }

            if (!body.IsComplete)
                continue;

            var subject = new FindingSubject(handle.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture), "m");
            var comparison = IlFindings.Compare(body, reader, body, reader, subject);
            if (comparison is FindingComparison<CanonicalIlOperation>.Failed)
            {
                declined++;
                continue;
            }

            var finding = CompleteComparison(comparison);
            Assert.True(finding.IsExact, $"IlFindings self-compare not exact for RVA {method.RelativeVirtualAddress}");
            canonicalized++;
        }

        Assert.True(canonicalized > 50, $"expected a substantial corpus; canonicalized={canonicalized} declined={declined}");
        Assert.True(declined <= canonicalized / 10, $"too many canonicalization declines: canonicalized={canonicalized} declined={declined}");
    }
}
