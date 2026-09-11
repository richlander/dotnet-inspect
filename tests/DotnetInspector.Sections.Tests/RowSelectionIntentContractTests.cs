using System.Collections;
using DotnetInspector.RowSelection;

namespace DotnetInspector.Sections.Tests;

public sealed class RowSelectionIntentContractTests
{
    [Fact]
    public void RowSelectionIntentConsumerExercisesDeclaration()
    {
        RowSelectionIntent<string> intent =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Head(2),
                    RowSelectionIntentOperation<string>.Tail(3),
                    RowSelectionIntentOperation<string>.Window(
                        2,
                        5),
                    RowSelectionIntentOperation<string>.Window(
                        null,
                        4),
                    RowSelectionIntentOperation<string>.Window(
                        3,
                        null),
                    RowSelectionIntentOperation<string>.Window(
                        null,
                        null),
                    RowSelectionIntentOperation<string>.Top(6),
                    RowSelectionIntentOperation<string>.Top(
                        7,
                        "confidence")
                ]);

        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Top,
                RowSelectionStageKind.Top
            ],
            intent.Operations.Select(operation => operation.Kind));
        Assert.Equal(2, intent.Operations[0].Count);
        Assert.Equal(3, intent.Operations[1].Count);
        Assert.Equal(2, intent.Operations[2].Start);
        Assert.Equal(5, intent.Operations[2].End);
        Assert.Null(intent.Operations[3].Start);
        Assert.Equal(4, intent.Operations[3].End);
        Assert.Equal(3, intent.Operations[4].Start);
        Assert.Null(intent.Operations[4].End);
        Assert.Null(intent.Operations[5].Start);
        Assert.Null(intent.Operations[5].End);
        Assert.Equal(6, intent.Operations[6].Count);
        Assert.False(
            intent.Operations[6].HasRankingOrderOperand);
        Assert.Equal(7, intent.Operations[7].Count);
        Assert.True(
            intent.Operations[7].HasRankingOrderOperand);
        Assert.Equal(
            "confidence",
            intent.Operations[7].RankingOrderOperand);

        Assert.Throws<InvalidOperationException>(
            () => _ = intent.Operations[0].Start);
        Assert.Throws<InvalidOperationException>(
            () => _ = intent.Operations[0].End);
        Assert.Throws<InvalidOperationException>(
            () => _ = intent.Operations[2].Count);
        Assert.Throws<InvalidOperationException>(
            () => _ =
                intent.Operations[1]
                    .HasRankingOrderOperand);
        Assert.Throws<InvalidOperationException>(
            () => _ =
                intent.Operations[6]
                    .RankingOrderOperand);
    }

    [Fact]
    public void RowSelectionIntentRejectsInvalidOperands()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionIntentOperation<string>.Head(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionIntentOperation<string>.Tail(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionIntentOperation<string>.Top(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionIntentOperation<string>.Top(
                0,
                "confidence"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionIntentOperation<string>.Window(
                0,
                1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionIntentOperation<string>.Window(
                1,
                0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowSelectionIntentOperation<string>.Window(
                2,
                1));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionIntentOperation<string>.Top(
                1,
                null!));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionIntent<string>.Create(null!));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Head(1),
                    null!
                ]));
        Assert.Throws<ArgumentNullException>(
            () => RowSelectionIntent<string>.Empty.Append(null!));
    }

    [Fact]
    public void RowSelectionIntentSnapshotsAreImmutable()
    {
        var operations =
            new List<RowSelectionIntentOperation<string>>
            {
                RowSelectionIntentOperation<string>.Head(2)
            };
        RowSelectionIntent<string> intent =
            RowSelectionIntent<string>.Create(operations);

        operations[0] =
            RowSelectionIntentOperation<string>.Tail(1);
        operations.Add(
            RowSelectionIntentOperation<string>.Top(1));

        Assert.Single(intent.Operations);
        Assert.Equal(
            RowSelectionStageKind.Head,
            intent.Operations[0].Kind);

        RowSelectionIntent<string> appended =
            intent.Append(
                RowSelectionIntentOperation<string>.Window(
                    2,
                    3));
        Assert.Single(intent.Operations);
        Assert.Equal(2, appended.Operations.Count);
        Assert.Empty(
            RowSelectionIntent<string>.Empty.Operations);
        AssertReadOnly(intent.Operations);
        AssertReadOnly(appended.Operations);
        AssertReadOnly(
            RowSelectionIntent<string>.Empty.Operations);
    }

    private static void AssertReadOnly<T>(
        IReadOnlyList<T> values)
    {
        IList list =
            Assert.IsAssignableFrom<IList>(values);
        Assert.True(list.IsReadOnly);
        if (values.Count > 0)
        {
            Assert.Throws<NotSupportedException>(
                () => list[0] = values[0]);
        }
    }
}
