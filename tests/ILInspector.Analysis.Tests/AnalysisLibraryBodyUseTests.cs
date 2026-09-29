using AnalysisBodyUseFixtures;
using ILInspector.Analysis.Planning;

namespace ILInspector.Analysis.Tests;

public sealed class AnalysisLibraryBodyUseTests
{
    static string FixturePath =>
        typeof(BodyUseSource).Assembly.Location;

    [Fact]
    public void ExecutePath_PublishesTypedLocalOccurrences()
    {
        AnalysisLibraryBodyUseOutcome.Available available =
            Available(
                AnalysisLibraryBodyUseService.ExecutePath(
                    FixturePath,
                    new(),
                    TestContext.Current.CancellationToken));
        AnalysisLibraryBodyUseResult result = available.Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Qualified,
            result.Disposition);
        Assert.NotEqual(Guid.Empty, result.Receipt.ModuleVersionId);
        Assert.Contains(
            result.Types,
            static type =>
                Name(type.Name)
                    == "AnalysisBodyUseFixtures.BodyUseSource");
        Assert.Contains(
            result.Types,
            static type =>
                Name(type.Name)
                    == "AnalysisBodyUseFixtures.BodyUseTarget");

        AnalysisLibraryBodyUseOccurrence[] uses =
            [.. result.Occurrences.Where(
                static occurrence =>
                    Name(occurrence.SourceType)
                        == "AnalysisBodyUseFixtures.BodyUseSource"
                    && Name(occurrence.TargetType)
                        == "AnalysisBodyUseFixtures.BodyUseTarget")];
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Call);
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Field);
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Array);
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Cast);
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.TypeToken);
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind
                        .GenericMethodInstantiation);

        ProducerParticipation participation =
            result.Receipt.Work.Producers.Single();
        Assert.Equal("AnalysisLibraryBodyUse", participation.Producer);
        Assert.True(
            participation.Layers.Single(
                static layer => layer.Layer == "Body").Acquired > 0);
        Assert.True(
            participation.Layers.Single(
                static layer => layer.Layer == "ModuleLookup").Acquired > 0);
    }

    [Fact]
    public void ExecutePath_AttributesAsyncBodyToDeclaredType()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecutePath(
                    FixturePath,
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Contains(
            result.Occurrences,
            static occurrence =>
                Name(occurrence.SourceType)
                    == "AnalysisBodyUseFixtures.BodyUseSource"
                && Name(occurrence.TargetType)
                    == "AnalysisBodyUseFixtures.BodyUseTarget"
                && occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Call);
        Assert.DoesNotContain(
            result.Occurrences,
            static occurrence =>
                Name(occurrence.SourceType).Contains(
                    "<AsyncUse>d__",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ExecutePath_ReportsExactInstructionLimit()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecutePath(
                    FixturePath,
                    new(
                        new(
                            MaximumInstructionsPerBody: 1)),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        AnalysisLibraryBodyUseDiagnostic[] diagnostics =
            [.. result.Diagnostics.Where(
                static diagnostic =>
                    diagnostic.Kind
                        == AnalysisLibraryBodyUseDiagnosticKind.Limit)];
        Assert.NotEmpty(diagnostics);
        Assert.All(
            diagnostics,
            static diagnostic =>
            {
                Assert.Equal(1, diagnostic.Limit);
                Assert.True(
                    diagnostic.AttemptedCharge > diagnostic.Limit);
            });
        Assert.True(result.Coverage.BodiesLimited > 0);
    }

    [Fact]
    public void ExecuteImage_ReportsExactOccurrenceLimit()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    FixturePath,
                    [.. File.ReadAllBytes(FixturePath)],
                    new(
                        new(
                            MaximumOccurrences: 1)),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        AnalysisLibraryBodyUseDiagnostic diagnostic =
            Assert.Single(
                result.Diagnostics,
                static diagnostic =>
                    diagnostic.Kind
                            == AnalysisLibraryBodyUseDiagnosticKind.Limit
                        && diagnostic.Detail.Contains(
                            "Library body-use occurrence",
                            StringComparison.Ordinal));
        Assert.Equal(1, diagnostic.Limit);
        Assert.True(diagnostic.AttemptedCharge > diagnostic.Limit);
        Assert.True(result.Occurrences.Length <= 1);
    }

    [Fact]
    public void ExecuteImage_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => AnalysisLibraryBodyUseService.ExecuteImage(
                FixturePath,
                [.. File.ReadAllBytes(FixturePath)],
                new(),
                cancellation.Token));
    }

    static string Name(
        ILInspector.Metadata.MetadataTypeDefinitionName name) =>
        name.ToEscapedFullName();

    static AnalysisLibraryBodyUseOutcome.Available Available(
        AnalysisLibraryBodyUseOutcome outcome) =>
        outcome is AnalysisLibraryBodyUseOutcome.Available available
            ? available
            : throw new Xunit.Sdk.XunitException(
                outcome is AnalysisLibraryBodyUseOutcome.Rejected rejected
                    ? $"{rejected.Kind}: {rejected.Detail}"
                    : $"Unexpected outcome {outcome.GetType().Name}.");
}
