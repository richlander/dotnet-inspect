using System.Collections.Immutable;
using System.Diagnostics;
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
    public void BuilderWithoutEvidenceRequestUsesOrdinaryEntryPoint()
    {
        var service = new ExampleInspectionService();
        var builder = new EvidenceBuilder<
            ExampleInspectionContent,
            ExampleInspectionEvidence>();

        (
            InspectionEnvelope<ExampleInspectionContent> inspection,
            EvidenceInspectionEnvelope<
                ExampleInspectionContent,
                ExampleInspectionEvidence>? evidence) =
            Build(builder, service, Request());

        Assert.Null(evidence);
        Assert.Equal(0, inspection.Content.MatchCount);
        Assert.Equal(1, service.ExecutionCount);
        Assert.Equal(0, service.EvidenceCaptureCount);
    }

    [Fact]
    public void BuilderRequestMatchesCompilationAndReusesBaseline()
    {
        var service = new ExampleInspectionService();
        var builder = new EvidenceBuilder<
            ExampleInspectionContent,
            ExampleInspectionEvidence>();
        var requestProbe = new EvidenceRequestProbe();

        builder.RequestEvidence(requestProbe.Request());
        (
            InspectionEnvelope<ExampleInspectionContent> inspection,
            EvidenceInspectionEnvelope<
                ExampleInspectionContent,
                ExampleInspectionEvidence>? evidence) =
            Build(builder, service, Request());

        var buildProbe = new DebugBuildProbe();
        buildProbe.Mark();

        Assert.Equal(
            buildProbe.IsDebugBuild ? 1 : 0,
            requestProbe.EvaluationCount);
        Assert.Equal(1, service.ExecutionCount);
        Assert.Equal(
            buildProbe.IsDebugBuild ? 1 : 0,
            service.EvidenceCaptureCount);
        Assert.Equal(buildProbe.IsDebugBuild, evidence is not null);
        if (evidence is not null)
        {
            Assert.Same(inspection, evidence.Inspection);
            Assert.Equal(2, evidence.Evidence.Decisions.Length);
        }
    }

    [Fact]
    public async Task AsyncBuilderWithoutEvidenceRequestUsesOrdinaryEntryPoint()
    {
        var service = new ExampleInspectionService();
        var builder = new EvidenceBuilder<
            ExampleInspectionContent,
            ExampleInspectionEvidence>();

        (
            InspectionEnvelope<ExampleInspectionContent> inspection,
            EvidenceInspectionEnvelope<
                ExampleInspectionContent,
                ExampleInspectionEvidence>? evidence) =
            await BuildAsync(builder, service, Request());

        Assert.Null(evidence);
        Assert.Equal(0, inspection.Content.MatchCount);
        Assert.Equal(1, service.ExecutionCount);
        Assert.Equal(0, service.EvidenceCaptureCount);
    }

    [Fact]
    public async Task AsyncBuilderRequestMatchesCompilationAndReusesBaseline()
    {
        var service = new ExampleInspectionService();
        var builder = new EvidenceBuilder<
            ExampleInspectionContent,
            ExampleInspectionEvidence>();
        var requestProbe = new EvidenceRequestProbe();

        builder.RequestEvidence(requestProbe.Request());
        (
            InspectionEnvelope<ExampleInspectionContent> inspection,
            EvidenceInspectionEnvelope<
                ExampleInspectionContent,
                ExampleInspectionEvidence>? evidence) =
            await BuildAsync(builder, service, Request());

        var buildProbe = new DebugBuildProbe();
        buildProbe.Mark();

        Assert.Equal(
            buildProbe.IsDebugBuild ? 1 : 0,
            requestProbe.EvaluationCount);
        Assert.Equal(1, service.ExecutionCount);
        Assert.Equal(
            buildProbe.IsDebugBuild ? 1 : 0,
            service.EvidenceCaptureCount);
        Assert.Equal(buildProbe.IsDebugBuild, evidence is not null);
        if (evidence is not null)
        {
            Assert.Same(inspection, evidence.Inspection);
            Assert.Equal(2, evidence.Evidence.Decisions.Length);
        }
    }

    private static (
        InspectionEnvelope<ExampleInspectionContent> Inspection,
        EvidenceInspectionEnvelope<
            ExampleInspectionContent,
            ExampleInspectionEvidence>? Evidence)
        Build(
            EvidenceBuilder<
                ExampleInspectionContent,
                ExampleInspectionEvidence> builder,
            ExampleInspectionService service,
            ExampleInspectionRequest request) =>
        builder.Build(
            (Service: service, Request: request),
            static state => state.Service.Execute(state.Request),
            static state => state.Service.ExecuteWithEvidence(state.Request));

    private static Task<(
        InspectionEnvelope<ExampleInspectionContent> Inspection,
        EvidenceInspectionEnvelope<
            ExampleInspectionContent,
            ExampleInspectionEvidence>? Evidence)>
        BuildAsync(
            EvidenceBuilder<
                ExampleInspectionContent,
                ExampleInspectionEvidence> builder,
            ExampleInspectionService service,
            ExampleInspectionRequest request) =>
        builder.BuildAsync(
            (Service: service, Request: request),
            static state =>
                Task.FromResult(state.Service.Execute(state.Request)),
            static state =>
                Task.FromResult(
                    state.Service.ExecuteWithEvidence(state.Request)));

    private static ExampleInspectionRequest Request() =>
        new(["alpha", "beta"], "z");

    private sealed class EvidenceRequestProbe
    {
        public int EvaluationCount { get; private set; }

        public bool Request()
        {
            EvaluationCount++;
            return true;
        }
    }

    private sealed class DebugBuildProbe
    {
        public bool IsDebugBuild { get; private set; }

        [Conditional("DEBUG")]
        public void Mark()
        {
            IsDebugBuild = true;
        }
    }
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
