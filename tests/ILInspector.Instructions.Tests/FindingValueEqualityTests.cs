using System.Collections.Immutable;

using ILInspector.Findings;

namespace ILInspector.Instructions.Tests;

public class FindingValueEqualityTests
{
    static readonly FindingSubject Subject = new("test", "test");
    static readonly FindingDescriptor Descriptor = new("test.item", "item");

    static Finding<string> Atom(string key)
        => new(Subject, Descriptor, new FindingKey(key), key);

    static ImmutableArray<Finding<string>> Atoms(params string[] keys)
        => [.. keys.Select(Atom)];

    static FindingSoftMatchCandidate SoftCandidate(
        int oldIndex,
        int newIndex,
        string tier,
        int confidence)
        => new(
            oldIndex,
            newIndex,
            new FindingMatchProvenance(
                new FindingMatchTier(tier, confidence),
                confidence));

    [Fact]
    public void FindingKey_UsesOrderedSoftKeyEquality()
    {
        var first = new FindingKey(
            "identity",
            "scope",
            [
                new FindingSoftKey(new FindingMatchTier("name", 80), "n", "v1"),
                new FindingSoftKey(new FindingMatchTier("shape", 70), "s", "v2"),
            ]);
        var equivalent = new FindingKey(
            "identity",
            "scope",
            [
                new FindingSoftKey(new FindingMatchTier("name", 80), "n", "v1"),
                new FindingSoftKey(new FindingMatchTier("shape", 70), "s", "v2"),
            ]);
        var reordered = new FindingKey(
            "identity",
            "scope",
            [
                new FindingSoftKey(new FindingMatchTier("shape", 70), "s", "v2"),
                new FindingSoftKey(new FindingMatchTier("name", 80), "n", "v1"),
            ]);

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(first, reordered);
    }

    [Fact]
    public void FindingKey_RejectsDefaultSoftKeyArray()
    {
        ImmutableArray<FindingSoftKey> softKeys = default;

        var error = Assert.Throws<ArgumentException>(
            () => new FindingKey("identity", null, softKeys));

        Assert.Equal("SoftKeys", error.ParamName);
    }

    [Fact]
    public void InspectionComplete_UsesOrderedSequenceEquality()
    {
        var first = new FindingInspection<string>.Complete(Atoms("a", "a", "b"));
        var equivalent = new FindingInspection<string>.Complete(Atoms("a", "a", "b"));
        var reordered = new FindingInspection<string>.Complete(Atoms("b", "a", "a"));
        var differentDuplicates = new FindingInspection<string>.Complete(Atoms("a", "b", "b"));

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.NotEqual(first, differentDuplicates);
        Assert.Equal(
            new FindingInspection<string>.Complete([]),
            new FindingInspection<string>.Complete([]));
        Assert.Throws<ArgumentException>(
            () => new FindingInspection<string>.Complete(default));
        Assert.Throws<ArgumentException>(
            () => first with { Findings = default });
    }

    [Fact]
    public void FindingMatch_UsesOrderedSequenceEquality()
    {
        var first = new FindingMatch(
            [
                new FindingEdge(FindingEdgeKind.Matched, 0, 0, 100),
                new FindingEdge(FindingEdgeKind.Added, -1, 1, 100),
            ],
            [
                new FindingMoveCandidate(0, 1, 75, "content+scope"),
                new FindingMoveCandidate(0, 1, 75, "content+scope"),
            ])
        {
            SoftCandidates =
            [
                SoftCandidate(0, 1, "name", 80),
                SoftCandidate(2, 3, "shape", 70),
            ],
        };
        var equivalent = new FindingMatch(
            [
                new FindingEdge(FindingEdgeKind.Matched, 0, 0, 100),
                new FindingEdge(FindingEdgeKind.Added, -1, 1, 100),
            ],
            [
                new FindingMoveCandidate(0, 1, 75, "content+scope"),
                new FindingMoveCandidate(0, 1, 75, "content+scope"),
            ])
        {
            SoftCandidates =
            [
                SoftCandidate(0, 1, "name", 80),
                SoftCandidate(2, 3, "shape", 70),
            ],
        };
        var reordered = new FindingMatch(
            [first.Edges[1], first.Edges[0]],
            first.MoveCandidates)
        {
            SoftCandidates = first.SoftCandidates,
        };
        var fewerDuplicates = new FindingMatch(
            first.Edges,
            [first.MoveCandidates[0]])
        {
            SoftCandidates = first.SoftCandidates,
        };
        var reorderedSoftCandidates = new FindingMatch(
            first.Edges,
            first.MoveCandidates)
        {
            SoftCandidates = [first.SoftCandidates[1], first.SoftCandidates[0]],
        };

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.NotEqual(first, fewerDuplicates);
        Assert.NotEqual(first, reorderedSoftCandidates);
        Assert.Throws<ArgumentException>(
            () => new FindingMatch(default, []));
        Assert.Throws<ArgumentException>(
            () => new FindingMatch([], default));
        Assert.Throws<ArgumentException>(
            () => new FindingMatch([null!], []));
        Assert.Throws<ArgumentException>(
            () => first with { MoveCandidates = default });
        Assert.Throws<ArgumentException>(
            () => first with { SoftCandidates = default });
    }

    [Fact]
    public void FindingEquivalence_UsesSetEquality()
    {
        var first = new FindingEquivalence(
            [PairKind.Present, PairKind.Added],
            [FindingDifferenceKind.None, FindingDifferenceKind.Moved]);
        var equivalent = new FindingEquivalence(
            [PairKind.Added, PairKind.Present, PairKind.Present],
            [FindingDifferenceKind.Moved, FindingDifferenceKind.None]);
        var different = new FindingEquivalence(
            [PairKind.Present],
            [FindingDifferenceKind.None, FindingDifferenceKind.Moved]);

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(first, different);
        Assert.Throws<ArgumentNullException>(
            () => new FindingEquivalence(null!, []));
        Assert.Throws<ArgumentNullException>(
            () => new FindingEquivalence([], null!));
        Assert.Throws<ArgumentNullException>(
            () => first with { AllowedKinds = null! });
    }

    [Fact]
    public void FindingEquivalence_NormalizesCustomSetComparers()
    {
        var customComparer = ImmutableHashSet.Create<PairKind>(
            new AllPairKindsEqualComparer(),
            PairKind.Present);
        var custom = new FindingEquivalence(
            customComparer,
            [FindingDifferenceKind.None]);
        var defaultComparer = new FindingEquivalence(
            [PairKind.Present],
            [FindingDifferenceKind.None]);

        Assert.Equal(defaultComparer, custom);
        Assert.Equal(defaultComparer.GetHashCode(), custom.GetHashCode());
        Assert.DoesNotContain(PairKind.Added, custom.AllowedKinds);
    }

    [Fact]
    public void FindingComparison_ComposesCollectionValueEquality()
    {
        FindingInspection<string> firstOld =
            new FindingInspection<string>.Complete(Atoms("a", "b"));
        FindingInspection<string> firstNew =
            new FindingInspection<string>.Complete(Atoms("a", "c"));
        FindingInspection<string> secondOld =
            new FindingInspection<string>.Complete(Atoms("a", "b"));
        FindingInspection<string> secondNew =
            new FindingInspection<string>.Complete(Atoms("a", "c"));

        var first = FindingComparison.Compare(firstOld, firstNew);
        var equivalent = FindingComparison.Compare(secondOld, secondNew);

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
    }

    [Fact]
    public void CorrelatedFinding_UsesOccurrenceSequenceEquality()
    {
        var firstFinding = Atom("a");
        var secondFinding = Atom("a");
        var key = FindingCorrelationKey.From(firstFinding);
        var first = new CorrelatedFinding<string>(
            key,
            [
                new(new FindingVersion("v1", "1.0", 0), firstFinding),
                new(new FindingVersion("v2", "2.0", 1), firstFinding),
            ]);
        var equivalent = new CorrelatedFinding<string>(
            FindingCorrelationKey.From(secondFinding),
            [
                new(new FindingVersion("v1", "1.0", 0), secondFinding),
                new(new FindingVersion("v2", "2.0", 1), secondFinding),
            ]);

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
    }

    [Fact]
    public void FindingCensusCorrelation_RejectsDefaultInspectionArray()
    {
        ImmutableArray<VersionedFindingInspection<string>> inspections = default;

        var error = Assert.Throws<ArgumentException>(
            () => FindingCensusCorrelation<string>.Create(inspections));

        Assert.Equal("inspections", error.ParamName);
    }

    [Fact]
    public void FindingPayloadEquality_IsProducerOwnedButMatchingRemainsKeyDriven()
    {
        var first = new Finding<CollectionPayload>(
            Subject,
            Descriptor,
            new FindingKey("same"),
            new CollectionPayload(ImmutableArray.Create(1, 2)));
        var second = new Finding<CollectionPayload>(
            Subject,
            Descriptor,
            new FindingKey("same"),
            new CollectionPayload(ImmutableArray.Create(1, 2)));

        Assert.NotEqual(first, second);

        var match = FindingMatcher.Match([first.Key], [second.Key]);
        Assert.Equal(FindingEdgeKind.Matched, Assert.Single(match.Edges).Kind);
    }

    sealed record CollectionPayload(ImmutableArray<int> Values);

    sealed class AllPairKindsEqualComparer : IEqualityComparer<PairKind>
    {
        public bool Equals(PairKind x, PairKind y) => true;
        public int GetHashCode(PairKind obj) => 0;
    }
}
