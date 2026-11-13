using DotnetInspector.InspectionContracts;
using InertText;

namespace DotnetInspector.Sections.Tests;

public sealed class PresentationLeafContainmentTests
{
    private static readonly PresentationLeafContainmentLimits GenerousLimits =
        new(
            maximumLeaves: 16,
            maximumInputCharacters: 1024,
            maximumOutputCharacters: 4096);

    [Fact]
    public void Execute_ContainsCompleteMixedPolicyPopulation()
    {
        PresentationLeafContainmentOutcome.Contained contained =
            Assert.IsType<PresentationLeafContainmentOutcome.Contained>(
                PresentationLeafContainment.Execute(
                    "document-7",
                    [
                        new(2, TextPolicy.Prose, "line one\nline \u202Etwo"),
                        new(1, TextPolicy.Field, "field\nvalue"),
                        new(3, TextPolicy.Prose, @"literal \u202E"),
                    ],
                    GenerousLimits));

        Assert.Equal("document-7", contained.Generation);
        Assert.Collection(
            contained.Leaves,
            leaf =>
            {
                Assert.Equal(2, leaf.Identity);
                Assert.Equal(TextPolicy.Prose, leaf.Policy);
                Assert.Equal(
                    "line one\nline \\u202Etwo",
                    leaf.Text.ToString());
            },
            leaf =>
            {
                Assert.Equal(1, leaf.Identity);
                Assert.Equal(TextPolicy.Field, leaf.Policy);
                Assert.Equal("field\\^Jvalue", leaf.Text.ToString());
            },
            leaf =>
            {
                Assert.Equal(3, leaf.Identity);
                Assert.Equal(TextPolicy.Prose, leaf.Policy);
                Assert.Equal(@"literal \\u202E", leaf.Text.ToString());
            });
    }

    [Fact]
    public void Execute_RejectsDuplicateIdentityWithoutPartialPopulation()
    {
        PresentationLeafContainmentOutcome.Rejected rejected = Reject(
            [
                new(4, TextPolicy.Field, "first"),
                new(4, TextPolicy.Prose, "second"),
            ]);

        Assert.Equal(
            PresentationLeafContainmentFailureKind.DuplicateLeafIdentity,
            rejected.Failure.Kind);
        Assert.Equal(4, rejected.Failure.Identity);
    }

    [Fact]
    public void Execute_RejectsInvalidIdentity()
    {
        PresentationLeafContainmentOutcome.Rejected rejected = Reject(
            [new(-1, TextPolicy.Field, "value")]);

        Assert.Equal(
            PresentationLeafContainmentFailureKind.InvalidLeafIdentity,
            rejected.Failure.Kind);
        Assert.Equal(-1, rejected.Failure.Identity);
    }

    [Fact]
    public void Execute_RejectsMissingText()
    {
        PresentationLeafContainmentOutcome.Rejected rejected = Reject(
            [new(0, TextPolicy.Field, null!)]);

        Assert.Equal(
            PresentationLeafContainmentFailureKind.MissingLeafText,
            rejected.Failure.Kind);
        Assert.Equal(0, rejected.Failure.Identity);
    }

    [Fact]
    public void Execute_RejectsLeafCountBeforeContainment()
    {
        PresentationLeafContainmentOutcome.Rejected rejected =
            Assert.IsType<PresentationLeafContainmentOutcome.Rejected>(
                PresentationLeafContainment.Execute(
                    "document-7",
                    [
                        new(0, TextPolicy.Field, "first"),
                        new(1, TextPolicy.Field, "second"),
                    ],
                    new(
                        maximumLeaves: 1,
                        maximumInputCharacters: 1024,
                        maximumOutputCharacters: 1024)));

        Assert.Equal(
            PresentationLeafContainmentFailureKind.TooManyLeaves,
            rejected.Failure.Kind);
        Assert.Equal(2, rejected.Failure.Actual);
        Assert.Equal(1, rejected.Failure.Limit);
    }

    [Fact]
    public void Execute_RejectsAggregateInputBeforeContainment()
    {
        PresentationLeafContainmentOutcome.Rejected rejected =
            Assert.IsType<PresentationLeafContainmentOutcome.Rejected>(
                PresentationLeafContainment.Execute(
                    "document-7",
                    [
                        new(0, TextPolicy.Field, "123"),
                        new(1, TextPolicy.Field, "456"),
                    ],
                    new(
                        maximumLeaves: 2,
                        maximumInputCharacters: 5,
                        maximumOutputCharacters: 1024)));

        Assert.Equal(
            PresentationLeafContainmentFailureKind.InputTextTooLarge,
            rejected.Failure.Kind);
        Assert.Equal(1, rejected.Failure.Identity);
        Assert.Equal(6, rejected.Failure.Actual);
        Assert.Equal(5, rejected.Failure.Limit);
    }

    [Fact]
    public void Execute_RejectsAggregateOutputWithoutReturningPartialLeaves()
    {
        PresentationLeafContainmentOutcome.Rejected rejected =
            Assert.IsType<PresentationLeafContainmentOutcome.Rejected>(
                PresentationLeafContainment.Execute(
                    "document-7",
                    [
                        new(0, TextPolicy.Field, "safe"),
                        new(1, TextPolicy.Field, "\u202E"),
                    ],
                    new(
                        maximumLeaves: 2,
                        maximumInputCharacters: 5,
                        maximumOutputCharacters: 9)));

        Assert.Equal(
            PresentationLeafContainmentFailureKind.OutputTextTooLarge,
            rejected.Failure.Kind);
        Assert.Equal(1, rejected.Failure.Identity);
        Assert.Equal(10, rejected.Failure.Actual);
        Assert.Equal(9, rejected.Failure.Limit);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Execute_RejectsInvalidGeneration(string generation)
    {
        PresentationLeafContainmentOutcome.Rejected rejected =
            Assert.IsType<PresentationLeafContainmentOutcome.Rejected>(
                PresentationLeafContainment.Execute(
                    generation,
                    [],
                    GenerousLimits));

        Assert.Equal(
            PresentationLeafContainmentFailureKind.InvalidGeneration,
            rejected.Failure.Kind);
    }

    [Fact]
    public void Execute_RejectsUnsupportedPolicy()
    {
        PresentationLeafContainmentOutcome.Rejected rejected = Reject(
            [new(0, (TextPolicy)42, "value")]);

        Assert.Equal(
            PresentationLeafContainmentFailureKind.UnsupportedTextPolicy,
            rejected.Failure.Kind);
        Assert.Equal(0, rejected.Failure.Identity);
    }

    private static PresentationLeafContainmentOutcome.Rejected Reject(
        IReadOnlyList<PresentationLeaf> leaves) =>
        Assert.IsType<PresentationLeafContainmentOutcome.Rejected>(
            PresentationLeafContainment.Execute(
                "document-7",
                leaves,
                GenerousLimits));
}
