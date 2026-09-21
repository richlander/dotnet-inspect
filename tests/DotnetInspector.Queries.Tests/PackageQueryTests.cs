using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Packages;
using QuerySpace;
using QuerySpace.Operations;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspector.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageQueryTests
{    [Fact]
    public async Task ExecuteToEnvelopeReturnsPackageQueryDocument()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.One", verified: true),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate => $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(candidate.Id)));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        InspectionEnvelope<PackageQueryDocument> envelope =
            await PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken);

        PackageQueryMatch result = Assert.Single(envelope.Content.Results);
        Assert.Equal("Contoso.One", result.Package.PackageId);
        Assert.Empty(envelope.Content.Failures);
        Assert.Equal(1, envelope.Content.Summary.Matches);
        Assert.Equal(
            PackageQueryCompletionKind.MatchLimitReached,
            envelope.Content.Summary.Completion);
        InspectionPortableProjection.NonProjectable share =
            Assert.IsType<InspectionPortableProjection.NonProjectable>(envelope.PortableProjection);
        Assert.Equal("package-query", envelope.ResourcePath.Value);
        Assert.Null(share.Location);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task ExecuteToEnvelopeSinkSeesOnlyNonterminalEvents()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.One", verified: true),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate => $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(candidate.Id)));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));
        var sink = new RecordingPackageQueryNonterminalSink();

        InspectionEnvelope<PackageQueryDocument> envelope =
            await PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                contentProvider: null,
                sink,
                TestContext.Current.CancellationToken);

        Assert.Contains(
            sink.Events,
            queryEvent => queryEvent is PackageQueryEvent.Progress);
        Assert.Equal(
            envelope.Content.Results,
            sink.Events
                .OfType<PackageQueryEvent.Match>()
                .Select(queryEvent => queryEvent.Value));
        Assert.Equal(
            envelope.Content.Failures,
            sink.Events
                .OfType<PackageQueryEvent.Failure>()
                .Select(queryEvent => queryEvent.Value));
        Assert.IsType<InspectionPortableProjection.NonProjectable>(envelope.PortableProjection);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task ExecuteToEnvelopeExplicitNullSinkRemainsUnambiguous()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.One", verified: true),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate => $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(candidate.Id)));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        InspectionEnvelope<PackageQueryDocument> envelope =
            await PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                null,
                null,
                TestContext.Current.CancellationToken);

        Assert.Single(envelope.Content.Results);
        Assert.Empty(envelope.Content.Failures);
    }

    [Fact]
    public async Task ExecuteToEnvelopeCancellationProducesNoDocument()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.One", verified: true),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate => $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(candidate.Id)));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));
        using var cancellation = new CancellationTokenSource();
        var sink = new CancelOnMatchSink(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                contentProvider: null,
                sink,
                cancellation.Token).AsTask());

        Assert.True(sink.SawMatch);
    }
}
