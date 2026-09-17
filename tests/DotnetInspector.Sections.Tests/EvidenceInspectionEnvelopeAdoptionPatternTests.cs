using System.Collections.Immutable;
using DotnetInspector.Sections;

namespace DotnetInspector.Sections.Tests;

public sealed class EvidenceInspectionEnvelopeAdoptionPatternTests
{
    [Fact]
    public void OrdinaryEntryPointDoesNotCaptureSupplementalEvidence()
    {
        var service = new ExampleInspectionService();

        InspectionEnvelope<ExampleInspectionContent> inspection =
            service.Execute(Request());

        Assert.Equal(0, inspection.Content.MatchCount);
        Assert.True(inspection.Content.IsComplete);
        Assert.Equal(1, service.ExecutionCount);
        Assert.Equal(0, service.EvidenceCaptureCount);
    }

    [Fact]
    public void EvidenceEntryPointPreservesBaselineAndCapturesOneDecisionSet()
    {
        ExampleInspectionRequest request = Request();
        var ordinaryService = new ExampleInspectionService();
        var evidenceService = new ExampleInspectionService();

        InspectionEnvelope<ExampleInspectionContent> ordinary =
            ordinaryService.Execute(request);
        EvidenceInspectionEnvelope<
            ExampleInspectionContent,
            ExampleInspectionEvidence> enriched =
                evidenceService.ExecuteWithEvidence(request);

        Assert.Equal(ordinary, enriched.Inspection);
        Assert.Equal(1, ordinaryService.ExecutionCount);
        Assert.Equal(0, ordinaryService.EvidenceCaptureCount);
        Assert.Equal(1, evidenceService.ExecutionCount);
        Assert.Equal(1, evidenceService.EvidenceCaptureCount);
        Assert.Collection(
            enriched.Evidence.Decisions,
            decision =>
            {
                Assert.Equal("alpha", decision.Candidate);
                Assert.False(decision.Matched);
            },
            decision =>
            {
                Assert.Equal("beta", decision.Candidate);
                Assert.False(decision.Matched);
            });
    }

    [Fact]
    public void DebugHostAdapterAvailabilityMatchesCompilation()
    {
        Type? adapter = typeof(EvidenceInspectionEnvelopeAdoptionPatternTests)
            .Assembly.GetType(
                "DotnetInspector.Sections.Tests.DebugEvidenceEnvelopeHostPattern");

#if DEBUG
        Assert.NotNull(adapter);
#else
        Assert.Null(adapter);
#endif
    }

#if DEBUG
    [Fact]
    public void DebugHostAdapterSelectsEvidenceEntryPoint()
    {
        var service = new ExampleInspectionService();
        var host = new DebugEvidenceEnvelopeHostPattern(service);

        EvidenceInspectionEnvelope<
            ExampleInspectionContent,
            ExampleInspectionEvidence> enriched = host.Execute(Request());

        Assert.Equal(1, service.ExecutionCount);
        Assert.Equal(1, service.EvidenceCaptureCount);
        Assert.Equal(2, enriched.Evidence.Decisions.Length);
    }
#endif

    private static ExampleInspectionRequest Request() =>
        new(["alpha", "beta"], "z");
}

internal sealed record ExampleInspectionRequest
{
    public ExampleInspectionRequest(
        IEnumerable<string> candidates,
        string requiredPrefix)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredPrefix);
        Candidates = candidates.ToImmutableArray();
        if (Candidates.Any(static candidate => candidate is null))
        {
            throw new ArgumentException(
                "Candidates cannot contain null values.",
                nameof(candidates));
        }

        RequiredPrefix = requiredPrefix;
    }

    public ImmutableArray<string> Candidates { get; }

    public string RequiredPrefix { get; }
}

internal sealed record ExampleInspectionContent(
    int MatchCount,
    bool IsComplete);

internal sealed record ExampleCandidateDecision(
    string Candidate,
    bool Matched);

internal sealed record ExampleInspectionEvidence(
    ImmutableArray<ExampleCandidateDecision> Decisions);

internal sealed class ExampleInspectionService
{
    public int ExecutionCount { get; private set; }

    public int EvidenceCaptureCount { get; private set; }

    public InspectionEnvelope<ExampleInspectionContent> Execute(
        ExampleInspectionRequest request) =>
        ExecuteCore(request, decisions: null);

    public EvidenceInspectionEnvelope<
        ExampleInspectionContent,
        ExampleInspectionEvidence> ExecuteWithEvidence(
            ExampleInspectionRequest request)
    {
        var decisions =
            ImmutableArray.CreateBuilder<ExampleCandidateDecision>(
                request.Candidates.Length);
        InspectionEnvelope<ExampleInspectionContent> inspection =
            ExecuteCore(request, decisions);
        EvidenceCaptureCount++;
        return new(
            inspection,
            new ExampleInspectionEvidence(decisions.ToImmutable()));
    }

    private InspectionEnvelope<ExampleInspectionContent> ExecuteCore(
        ExampleInspectionRequest request,
        ImmutableArray<ExampleCandidateDecision>.Builder? decisions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ExecutionCount++;

        int matches = 0;
        foreach (string candidate in request.Candidates)
        {
            bool matched = candidate.StartsWith(
                request.RequiredPrefix,
                StringComparison.Ordinal);
            if (matched)
                matches++;
            decisions?.Add(new ExampleCandidateDecision(candidate, matched));
        }

        return new(
            new ExampleInspectionContent(matches, IsComplete: true),
            new InspectionShare.NonProjectable(
                "evidence-adoption-pattern/share",
                "The test-host pattern has no Workspace Share projection."));
    }
}
