using DotnetInspector.RowSelection;
using DotnetInspector.RowSelectionConsumer;

namespace DotnetInspector.RowSelection.Tests;

public sealed class RowSelectionBoundaryTests
{
    [Fact]
    public void RowSelectionLanguageConsumerExercisesDeclaration()
    {
        LanguageObservation observation =
            LanguageConsumer.Inspect();
        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Top,
            ],
            observation.Kinds);
        Assert.Equal(2, observation.HeadCount);
        Assert.Equal(3, observation.TailCount);
        Assert.Equal(4, observation.WindowStart);
        Assert.Equal(8, observation.WindowEnd);
        Assert.Equal(5, observation.TopCount);
        Assert.Equal("rank", observation.TopOrder);
        Assert.Equal(0, observation.EmptyCount);
        Assert.Equal(4, observation.PriorCount);
        Assert.Equal(5, observation.AppendedCount);
        Assert.Equal(
            typeof(InvalidOperationException),
            observation.WrongCountAccessorException);
        Assert.Equal(
            typeof(InvalidOperationException),
            observation.WrongWindowAccessorException);
        Assert.Equal(
            typeof(InvalidOperationException),
            observation.WrongOrderAccessorException);
    }

    [Fact]
    public void RowSelectionReferenceEvaluatorExercisesSurface()
    {
        ReferenceEvaluatorObservation observation =
            ReferenceEvaluatorConsumer.Evaluate();
        Assert.Equal([5, 6], observation.Values);
        Assert.Equal(
            [1, 2, 3],
            observation.RankedValues);
        Assert.Equal(
            [10, 20],
            observation.NamedKeys);
        Assert.Equal(
            [1, 2],
            observation.NamedValues[0]);
        Assert.Equal(
            [4, 5],
            observation.NamedValues[1]);
        Assert.False(observation.FailureIsSuccess);
        Assert.Equal(10, observation.FailureKey);
        Assert.Equal(2, observation.FailureStage);
        Assert.Equal(
            3,
            observation.FailureRequiredPosition);
        Assert.Equal(
            2,
            observation.FailureAvailableCount);
    }

}
