using System.Collections.Immutable;

using ILInspector.Findings;

namespace ILInspector.Instructions.Tests;

public class AnalysisDiffTests
{
    [Fact]
    public void AnalysisDiff_ConstructsEmptyDiff()
    {
        var diff = new AnalysisDiff<string>([], [], []);

        Assert.Empty(diff.Before);
        Assert.Empty(diff.After);
        Assert.Empty(diff.Relations);
    }

    [Fact]
    public void AnalysisDiff_ConstructsOneSidedAndAllCorrespondenceArities()
    {
        var additionOnly = new AnalysisDiff<string>(
            [],
            ["a", "b"],
            [Addition(1), Addition(0)]);
        var removalOnly = new AnalysisDiff<string>(
            ["a", "b"],
            [],
            [Removal(1), Removal(0)]);
        var oneToOne = Corresponding(
            ["a"],
            ["b"],
            Correspondence([0], [0]));
        var oneToMany = Corresponding(
            ["a"],
            ["b", "c"],
            Correspondence([0], [0, 1]));
        var manyToOne = Corresponding(
            ["a", "b"],
            ["c"],
            Correspondence([0, 1], [0]));
        var manyToMany = Corresponding(
            ["a", "b"],
            ["c", "d", "e"],
            Correspondence([0, 1], [0, 1, 2]));

        Assert.Collection(
            additionOnly.Relations,
            relation => Assert.Equal([0], Assert.IsType<AnalysisDiffRelation.Addition>(relation).AfterCoordinates),
            relation => Assert.Equal([1], Assert.IsType<AnalysisDiffRelation.Addition>(relation).AfterCoordinates));
        Assert.Collection(
            removalOnly.Relations,
            relation => Assert.Equal([0], Assert.IsType<AnalysisDiffRelation.Removal>(relation).BeforeCoordinates),
            relation => Assert.Equal([1], Assert.IsType<AnalysisDiffRelation.Removal>(relation).BeforeCoordinates));
        Assert.Single(oneToOne.Relations);
        Assert.Single(oneToMany.Relations);
        Assert.Single(manyToOne.Relations);
        Assert.Single(manyToMany.Relations);
    }

    [Fact]
    public void AnalysisDiff_CarriesOrthogonalCorrespondenceFacets()
    {
        var movedAndChanged = Correspondence(
            [0],
            [0],
            AnalysisDiffContentKind.Changed,
            AnalysisDiffPlacementKind.Moved);
        var unclassified = Correspondence(
            [1],
            [1],
            AnalysisDiffContentKind.Unclassified,
            AnalysisDiffPlacementKind.Unclassified);
        var diff = new AnalysisDiff<string>(
            ["a", "b"],
            ["c", "d"],
            [unclassified, movedAndChanged]);

        var first = Assert.IsType<AnalysisDiffRelation.Correspondence>(diff.Relations[0]);
        Assert.Equal(AnalysisDiffContentKind.Changed, first.Content);
        Assert.Equal(AnalysisDiffPlacementKind.Moved, first.Placement);

        var second = Assert.IsType<AnalysisDiffRelation.Correspondence>(diff.Relations[1]);
        Assert.Equal(AnalysisDiffContentKind.Unclassified, second.Content);
        Assert.Equal(AnalysisDiffPlacementKind.Unclassified, second.Placement);
    }

    [Fact]
    public void AnalysisDiff_PreservesPathologicalMixedTopology()
    {
        var diff = new AnalysisDiff<string>(
            ["beta", "alpha", "gamma", "obsolete"],
            ["beta-a", "beta-b", "gamma", "alpha-2", "delta"],
            [
                Addition(4),
                Removal(3),
                Correspondence(
                    [2],
                    [2],
                    AnalysisDiffContentKind.Unchanged,
                    AnalysisDiffPlacementKind.Stable),
                Correspondence(
                    [1],
                    [3],
                    AnalysisDiffContentKind.Changed,
                    AnalysisDiffPlacementKind.Moved),
                Correspondence(
                    [0],
                    [0, 1],
                    AnalysisDiffContentKind.Changed,
                    AnalysisDiffPlacementKind.Stable),
            ]);

        Assert.Collection(
            diff.Relations,
            relation =>
            {
                var correspondence =
                    Assert.IsType<AnalysisDiffRelation.Correspondence>(relation);
                Assert.Equal([0], correspondence.BeforeCoordinates);
                Assert.Equal([0, 1], correspondence.AfterCoordinates);
            },
            relation =>
            {
                var correspondence =
                    Assert.IsType<AnalysisDiffRelation.Correspondence>(relation);
                Assert.Equal([1], correspondence.BeforeCoordinates);
                Assert.Equal([3], correspondence.AfterCoordinates);
                Assert.Equal(AnalysisDiffContentKind.Changed, correspondence.Content);
                Assert.Equal(AnalysisDiffPlacementKind.Moved, correspondence.Placement);
            },
            relation => Assert.Equal(
                [2],
                Assert.IsType<AnalysisDiffRelation.Correspondence>(relation).BeforeCoordinates),
            relation => Assert.Equal(
                [3],
                Assert.IsType<AnalysisDiffRelation.Removal>(relation).BeforeCoordinates),
            relation => Assert.Equal(
                [4],
                Assert.IsType<AnalysisDiffRelation.Addition>(relation).AfterCoordinates));
    }

    [Fact]
    public void AnalysisDiff_RejectsDefaultArraysAndNullValues()
    {
        ImmutableArray<string> defaultItems = default;
        ImmutableArray<AnalysisDiffRelation> defaultRelations = default;

        Assert.Throws<ArgumentException>(
            () => new AnalysisDiff<string>(defaultItems, [], []));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiff<string>([], defaultItems, []));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiff<string>([], [], defaultRelations));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiff<string>([null!], [], [Removal(0)]));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiff<string>([], [null!], [Addition(0)]));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiff<string>([], [], [null!]));
    }

    [Fact]
    public void AnalysisDiffRelation_RejectsInvalidCoordinates()
    {
        ImmutableArray<int> defaultCoordinates = default;

        Assert.Throws<ArgumentException>(
            () => new AnalysisDiffRelation.Addition(defaultCoordinates));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiffRelation.Removal(defaultCoordinates));
        Assert.Throws<ArgumentException>(
            () => Correspondence(defaultCoordinates, [0]));
        Assert.Throws<ArgumentException>(
            () => Correspondence([0], defaultCoordinates));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiffRelation.Addition([]));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiffRelation.Removal([]));
        Assert.Throws<ArgumentException>(
            () => Correspondence([], [0]));
        Assert.Throws<ArgumentException>(
            () => Correspondence([0], []));
        Assert.Throws<ArgumentException>(
            () => Correspondence([1, 0], [0]));
        Assert.Throws<ArgumentException>(
            () => Correspondence([0, 0], [0]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Correspondence([-1], [0]));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiffRelation.Addition([0, 1]));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiffRelation.Removal([0, 1]));
    }

    [Fact]
    public void AnalysisDiffRelation_RejectsUnknownClassifications()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Correspondence(
                [0],
                [0],
                (AnalysisDiffContentKind)int.MaxValue,
                AnalysisDiffPlacementKind.Stable));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Correspondence(
                [0],
                [0],
                AnalysisDiffContentKind.Unchanged,
                (AnalysisDiffPlacementKind)int.MaxValue));
    }

    [Fact]
    public void AnalysisDiff_RejectsOutOfRangeOverlapAndIncompleteCoverage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AnalysisDiff<string>([], ["a"], [Addition(1)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AnalysisDiff<string>(["a"], [], [Removal(1)]));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiff<string>(
                ["a"],
                ["b"],
                [Correspondence([0], [0]), Removal(0)]));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiff<string>(
                ["a"],
                ["b"],
                [Correspondence([0], [0]), Addition(0)]));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiff<string>(["a", "b"], ["c"], [Correspondence([0], [0])]));
        Assert.Throws<ArgumentException>(
            () => new AnalysisDiff<string>(["a"], ["b", "c"], [Correspondence([0], [0])]));
    }

    [Fact]
    public void AnalysisDiff_CanonicalizesRelationOrder()
    {
        var first = new AnalysisDiff<string>(
            ["a", "b", "c"],
            ["d", "e", "f"],
            [
                Addition(2),
                Correspondence([1], [0]),
                Removal(2),
                Correspondence([0], [1]),
            ]);
        var equivalent = new AnalysisDiff<string>(
            ["a", "b", "c"],
            ["d", "e", "f"],
            [
                Correspondence([0], [1]),
                Removal(2),
                Addition(2),
                Correspondence([1], [0]),
            ]);

        Assert.Collection(
            first.Relations,
            relation => Assert.Equal([0], Assert.IsType<AnalysisDiffRelation.Correspondence>(relation).BeforeCoordinates),
            relation => Assert.Equal([1], Assert.IsType<AnalysisDiffRelation.Correspondence>(relation).BeforeCoordinates),
            relation => Assert.Equal([2], Assert.IsType<AnalysisDiffRelation.Removal>(relation).BeforeCoordinates),
            relation => Assert.Equal([2], Assert.IsType<AnalysisDiffRelation.Addition>(relation).AfterCoordinates));
        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
    }

    [Fact]
    public void AnalysisDiff_UsesSequenceValueEquality()
    {
        string firstA = new('a', 1);
        string secondA = new('a', 1);
        string firstB = new('b', 1);
        string secondB = new('b', 1);
        var relations = new AnalysisDiffRelation[]
        {
            Correspondence([0], [0]),
            Correspondence([1], [1]),
        };

        var first = new AnalysisDiff<string>(
            [firstA, firstB],
            ["c", "d"],
            [.. relations]);
        var equivalent = new AnalysisDiff<string>(
            [secondA, secondB],
            [new('c', 1), new('d', 1)],
            [
                Correspondence([0], [0]),
                Correspondence([1], [1]),
            ]);
        var reordered = new AnalysisDiff<string>(
            [firstB, firstA],
            ["c", "d"],
            [.. relations]);
        var differentMultiplicity = new AnalysisDiff<string>(
            [firstA, firstA],
            ["c", "d"],
            [.. relations]);

        Assert.NotSame(firstA, secondA);
        Assert.NotSame(firstB, secondB);
        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.NotEqual(first, differentMultiplicity);
    }

    [Fact]
    public void AnalysisDiff_DistinguishesMembershipAndClassification()
    {
        var original = Corresponding(
            ["a", "b"],
            ["c", "d"],
            Correspondence(
                [0, 1],
                [0, 1],
                AnalysisDiffContentKind.Changed,
                AnalysisDiffPlacementKind.Stable));
        var differentMembership = new AnalysisDiff<string>(
            ["a", "b"],
            ["c", "d"],
            [
                Correspondence([0], [0]),
                Correspondence([1], [1]),
            ]);
        var differentContent = Corresponding(
            ["a", "b"],
            ["c", "d"],
            Correspondence(
                [0, 1],
                [0, 1],
                AnalysisDiffContentKind.Unchanged,
                AnalysisDiffPlacementKind.Stable));
        var differentPlacement = Corresponding(
            ["a", "b"],
            ["c", "d"],
            Correspondence(
                [0, 1],
                [0, 1],
                AnalysisDiffContentKind.Changed,
                AnalysisDiffPlacementKind.Moved));

        Assert.NotEqual(original, differentMembership);
        Assert.NotEqual(original, differentContent);
        Assert.NotEqual(original, differentPlacement);
        Assert.NotEqual<AnalysisDiffRelation>(Addition(0), Removal(0));
    }

    [Fact]
    public void AnalysisDiff_PayloadEqualityDoesNotEstablishCorrespondence()
    {
        var oneSided = new AnalysisDiff<string>(
            ["same"],
            ["same"],
            [Addition(0), Removal(0)]);
        var corresponding = Corresponding(
            ["same"],
            ["same"],
            Correspondence(
                [0],
                [0],
                AnalysisDiffContentKind.Unclassified,
                AnalysisDiffPlacementKind.Unclassified));

        Assert.NotEqual(oneSided, corresponding);
    }

    static AnalysisDiff<string> Corresponding(
        ImmutableArray<string> before,
        ImmutableArray<string> after,
        AnalysisDiffRelation.Correspondence relation)
        => new(before, after, [relation]);

    static AnalysisDiffRelation.Addition Addition(params int[] coordinates)
        => new([.. coordinates]);

    static AnalysisDiffRelation.Removal Removal(params int[] coordinates)
        => new([.. coordinates]);

    static AnalysisDiffRelation.Correspondence Correspondence(
        ImmutableArray<int> beforeCoordinates,
        ImmutableArray<int> afterCoordinates,
        AnalysisDiffContentKind content = AnalysisDiffContentKind.Unchanged,
        AnalysisDiffPlacementKind placement = AnalysisDiffPlacementKind.Stable)
        => new(beforeCoordinates, afterCoordinates, content, placement);
}
