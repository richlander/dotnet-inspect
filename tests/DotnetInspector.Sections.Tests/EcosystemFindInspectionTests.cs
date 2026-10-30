using System.Runtime.CompilerServices;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Sections.Tests;

public class EcosystemFindInspectionTests
{
    [Theory]
    [InlineData(0, 5, false)]
    [InlineData(1, 5, false)]
    [InlineData(1, 1, false)]
    [InlineData(0, 5, true)]
    public async Task EvidencePreservesExecutionAndCountsEmptyCandidateWork(
        int indexedRows, int maximumRows, bool failCandidate)
    {
        var layer = new EcosystemFindLayer(
            new WorkspaceEcosystemRegistrationDeclaration(
                WorkspaceEcosystemRegistrationId.Create("ecosystem.example"),
                [], [new PackageCoordinate("Example.Core")],
                [new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                    new PackagePrefixDeclaration("Example."))]),
            Platform: false);
        var request = new EcosystemFindSearchRequest(
            MemberFindQuestion.Create(["Add*"], FindVisibility.Public),
            [layer], [layer.Registration.Id], 1, maximumRows);
        List<string> ordinaryCalls = [];
        List<string> evidenceCalls = [];
        using var ordinarySession = Session(ordinaryCalls);
        using var evidenceSession = Session(evidenceCalls);

        var ordinary = await EcosystemFindInspection.ExecuteAsync(
            ordinarySession, TestContext.Current.CancellationToken);
        var enriched = await EcosystemFindInspection.ExecuteWithEvidenceAsync(
            evidenceSession, "Legacy acquisition does not issue receipts.",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ordinaryCalls, evidenceCalls);
        Assert.Equal(ordinary.Share, enriched.Inspection.Share);
        Assert.Equal(ordinary.Diagnostics, enriched.Inspection.Diagnostics);
        Assert.Equal(ordinary.Content.RequestIdentity, enriched.Inspection.Content.RequestIdentity);
        Assert.Equal(ordinary.Content.BoundedBlocks, enriched.Inspection.Content.BoundedBlocks);
        Assert.Equal(ordinary.Content.IndexedBlocks, enriched.Inspection.Content.IndexedBlocks);
        Assert.Equal(ordinary.Content.PrefixBlocks, enriched.Inspection.Content.PrefixBlocks);
        Assert.Equal(ordinary.Content.Completion, enriched.Inspection.Content.Completion);
        Assert.Equal(ordinary.Content.Unsearched, enriched.Inspection.Content.Unsearched);
        Assert.Equal(ordinary.Content.PrefixCoverage, enriched.Inspection.Content.PrefixCoverage);
        Assert.Equal(failCandidate, enriched.Inspection.Content.HasFailures);
        if (failCandidate)
            Assert.Equal("Package could not be inspected.",
                Assert.Single(enriched.Inspection.Diagnostics).Summary.ToString());
        Assert.Empty(enriched.Inspection.Content.PrefixBlocks);
        bool filledByIndex = maximumRows == indexedRows;
        Assert.Equal(filledByIndex ? 0 : 1, enriched.Evidence.Search.PrefixCandidatesEvaluated);
        Assert.Equal(filledByIndex ? ["index"] : ["index", "bounded", "enumerate", "candidate"],
            evidenceCalls);
        Assert.Equal(filledByIndex
                ? EcosystemFindCompletion.RowLimitReached
                : EcosystemFindCompletion.CandidateLimitReached,
            enriched.Inspection.Content.Completion);
        if (!filledByIndex)
        {
            EcosystemFindWorkEvidence candidate =
                Assert.Single(enriched.Evidence.Search.Work, work => work.Phase == "Prefix");
            Assert.Equal(0, candidate.RowCount);
            Assert.Equal("Example.Widget@1.0.0", candidate.Source);
            Assert.Equal(failCandidate, candidate.HasFailures);
            Assert.Equal(EcosystemFindPrefixPageCompletion.Continuing,
                Assert.Single(enriched.Evidence.Search.Pages).Completion);
        }
        string json = JsonSerializer.Serialize(
            enriched.Evidence,
            EcosystemFindInspectionEvidenceJsonContext.Default.EcosystemFindInspectionEvidence);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("member", document.RootElement.GetProperty("search")
            .GetProperty("request").GetProperty("question").GetProperty("kind").GetString());
        Assert.Contains("does not issue receipts",
            document.RootElement.GetProperty("acquisition_receipts_unavailable_reason").GetString());

        EcosystemFindSearchSession<string> Session(List<string> calls) =>
            new(request,
                (_, _) =>
                {
                    calls.Add("bounded");
                    return Task.FromResult(new EcosystemFindBlock<string>("bounded", 0));
                },
                (_, _, token) => Pages(calls, token),
                (_, _, _) =>
                {
                    calls.Add("candidate");
                    return Task.FromResult(new EcosystemFindBlock<string>(
                        "empty", 0, HasFailures: failCandidate, Incomplete: failCandidate)
                    {
                        Failure = failCandidate ? "Package could not be inspected." : null,
                    });
                },
                indexed: (_, _) =>
                {
                    calls.Add("index");
                    return Task.FromResult(new EcosystemFindIndexedOutcome<string>(
                        indexedRows == 0 ? [] : [new("indexed", indexedRows)],
                        EcosystemFindIndexedCompletion.Exhausted, []));
                });
    }

    private static async IAsyncEnumerable<EcosystemFindPrefixPage> Pages(
        List<string> calls,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        calls.Add("enumerate");
        await Task.CompletedTask;
        yield return new([new("Example.Widget", "1.0.0")],
            EcosystemFindPrefixPageCompletion.Continuing);
        throw new InvalidOperationException("The candidate bound must prevent another pull.");
    }
}
