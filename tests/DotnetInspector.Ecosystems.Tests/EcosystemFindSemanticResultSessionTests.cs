using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspector.Ecosystems.Tests;

public partial class EcosystemFindIndexTests
{
    [Fact]
    public void IndexedFind_PublishesSemanticFindingsBeforeReturningAndRetainsOrder()
    {
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Core",
                    assetPath: "lib/net10.0/Fixture.Core.dll",
                    assemblyName: "Fixture.Core",
                    ScanFixture()),
            ]);
        TypeFindQuestion question = TypeFindQuestion.Create(
            [
                "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                "Microsoft.Extensions.DependencyInjection.IService*",
            ],
            FindVisibility.Public);
        bool returned = false;
        var published = new List<FindSemanticFinding>();
        var session = new FindSemanticResultSession(
            sink: new ActionSink(finding =>
            {
                Assert.False(returned);
                published.Add(finding);
            }));

        InspectionEnvelope<
            EcosystemFindIndexedIntegrationResult.Type> inspection =
            EcosystemFindIndexedTypeInspection.Execute(
                [artifact],
                [Demand(artifact, prefixOrdinal: 0)],
                question,
                maximumRows: null,
                session,
                cancellationToken: TestContext.Current.CancellationToken);
        returned = true;
        FindSemanticResult completed = session.Complete(
            FindSemanticResultCompletion.Exhausted);

        Assert.Equal(inspection.Content.Matches.Length, published.Count);
        Assert.Equal(published, completed.Findings);
        Assert.Equal(
            [0, 1],
            completed.Findings.Select(finding =>
                Assert.IsType<FindSemanticFinding.Type>(finding)
                    .Match.Pattern.Ordinal));
        Assert.Equal(
            2,
            completed.Findings.Select(finding => finding.Identity)
                .Distinct()
                .Count());
    }

    [Fact]
    public void IndexedMemberFind_PublishesEachExactFindingOnce()
    {
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Core",
                    assetPath: "lib/net10.0/Fixture.Core.dll",
                    assemblyName: "Fixture.Core",
                    ScanFixture()),
            ]);
        var published = new List<FindSemanticFinding>();
        var session = new FindSemanticResultSession(
            sink: new ActionSink(published.Add));
        MemberFindQuestion question = MemberFindQuestion.Create(
            ["AddIndexedThing", "AddIndexed*"],
            FindVisibility.All,
            declaringTypeFilter:
                "Microsoft.Extensions.DependencyInjection."
                    + "TestServiceCollectionExtensions");

        var result =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Member>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [artifact],
                        [Demand(artifact, prefixOrdinal: 0)],
                        question),
                    session,
                    TestContext.Current.CancellationToken));
        foreach (MemberFindSemanticMatch match in result.Matches)
        {
            Assert.Equal(
                FindSemanticFindingPublication.Duplicate,
                session.Publish(match));
        }
        FindSemanticResult completed = session.Complete(
            FindSemanticResultCompletion.Exhausted);

        Assert.Equal(4, published.Count);
        Assert.Equal(published, completed.Findings);
        Assert.Equal(
            [0, 0, 1, 1],
            completed.Findings
                .Cast<FindSemanticFinding.Member>()
                .Select(finding => finding.Match.Pattern.Ordinal));
    }

    [Fact]
    public void ResultSession_SuppressesOnlyExactRepeatedFindingIdentity()
    {
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.One",
                    assetPath: "lib/net10.0/Fixture.One.dll",
                    assemblyName: "Fixture.One",
                    ScanFixture()),
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 1,
                    packageId: "Fixture.Two",
                    assetPath: "lib/net10.0/Fixture.Two.dll",
                    assemblyName: "Fixture.Two",
                    ScanFixture()),
            ]);
        var result =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Type>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [artifact],
                        [Demand(artifact, prefixOrdinal: 0)],
                        TypeFindQuestion.Create(
                            ["IServiceCollection"],
                            FindVisibility.Public)),
                    TestContext.Current.CancellationToken));
        Assert.Equal(2, result.Matches.Length);
        var published = new List<FindSemanticFinding>();
        var session = new FindSemanticResultSession(
            sink: new ActionSink(published.Add));

        Assert.Equal(
            FindSemanticFindingPublication.Published,
            session.Publish(result.Matches[0]));
        Assert.Equal(
            FindSemanticFindingPublication.Duplicate,
            session.Publish(result.Matches[0]));
        Assert.Equal(
            FindSemanticFindingPublication.Published,
            session.Publish(result.Matches[1]));
        FindSemanticResult completed = session.Complete(
            FindSemanticResultCompletion.Exhausted);
        var withoutSink = new FindSemanticResultSession();
        Assert.Equal(
            FindSemanticFindingPublication.Published,
            withoutSink.Publish(result.Matches[0]));
        Assert.Equal(
            FindSemanticFindingPublication.Duplicate,
            withoutSink.Publish(result.Matches[0]));
        Assert.Equal(
            FindSemanticFindingPublication.Published,
            withoutSink.Publish(result.Matches[1]));
        FindSemanticResult completedWithoutSink = withoutSink.Complete(
            FindSemanticResultCompletion.Exhausted);

        Assert.Equal(2, published.Count);
        Assert.Equal(published, completed.Findings);
        Assert.Equal(completed.Findings, completedWithoutSink.Findings);
        Assert.Single(
            completed.Findings.Select(finding =>
                    Assert.IsType<FindSemanticFinding.Type>(finding)
                        .Match.FullName)
                .Distinct());
        Assert.Equal(
            2,
            completed.Findings.Select(finding => finding.Identity)
                .Distinct()
                .Count());
    }

    [Fact]
    public void ResultSession_RowCapacityStopsIndexedWorkAndRetainsCanceledFindings()
    {
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Core",
                    assetPath: "lib/net10.0/Fixture.Core.dll",
                    assemblyName: "Fixture.Core",
                    ScanFixture()),
            ]);
        var session = new FindSemanticResultSession(maximumFindings: 1);

        InspectionEnvelope<
            EcosystemFindIndexedIntegrationResult.Type> inspection =
            EcosystemFindIndexedTypeInspection.Execute(
                [artifact],
                [Demand(artifact, prefixOrdinal: 0)],
                TypeFindQuestion.Create(
                    [
                        "IServiceCollection",
                        "Microsoft.Extensions.DependencyInjection.IService*",
                    ],
                    FindVisibility.Public),
                maximumRows: null,
                session,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(inspection.Content.Matches);
        Assert.Equal(
            EcosystemFindIndexedIntegrationCompletion.RowLimitReached,
            inspection.Content.Completion);
        Assert.Equal(0, session.RemainingCapacity);
        FindSemanticResult completed = session.Complete(
            FindSemanticResultCompletion.Canceled,
            unsearched: ["resident", "prefix"]);
        Assert.Single(completed.Findings);
        Assert.Equal(FindSemanticResultCompletion.Canceled, completed.Completion);
        Assert.Equal(["resident", "prefix"], completed.Unsearched);
        Assert.True(completed.Incomplete);
        Assert.Throws<InvalidOperationException>(
            () => session.Publish(inspection.Content.Matches[0]));
    }

    [Fact]
    public void ResultSession_RetainsPublishedFindingsWithVisibleFailure()
    {
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Core",
                    assetPath: "lib/net10.0/Fixture.Core.dll",
                    assemblyName: "Fixture.Core",
                    ScanFixture()),
            ]);
        var result =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Type>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [artifact],
                        [Demand(artifact, prefixOrdinal: 0)],
                        TypeFindQuestion.Create(
                            ["IServiceCollection"],
                            FindVisibility.Public)),
                    TestContext.Current.CancellationToken));
        var session = new FindSemanticResultSession();

        Assert.Equal(
            FindSemanticFindingPublication.Published,
            session.Publish(Assert.Single(result.Matches)));
        FindSemanticResult completed = session.Complete(
            FindSemanticResultCompletion.Failed,
            failures: [new("prefix", "Later source failed.")],
            unsearched: ["remaining prefix"]);

        Assert.Single(completed.Findings);
        Assert.Equal(
            new FindSemanticResultFailure(
                "prefix",
                "Later source failed."),
            Assert.Single(completed.Failures));
        Assert.Equal(["remaining prefix"], completed.Unsearched);
    }

    [Fact]
    public void ResultSession_DoesNotRetainFindingWhenSinkFails()
    {
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Core",
                    assetPath: "lib/net10.0/Fixture.Core.dll",
                    assemblyName: "Fixture.Core",
                    ScanFixture()),
            ]);
        var result =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Type>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [artifact],
                        [Demand(artifact, prefixOrdinal: 0)],
                        TypeFindQuestion.Create(
                            ["IServiceCollection"],
                            FindVisibility.Public)),
                    TestContext.Current.CancellationToken));
        var session = new FindSemanticResultSession(
            sink: new ActionSink(
                _ => throw new InvalidOperationException("sink failed")));

        InvalidOperationException failure = Assert.Throws<
            InvalidOperationException>(
                () => session.Publish(Assert.Single(result.Matches)));

        Assert.Equal("sink failed", failure.Message);
        Assert.Equal(0, session.Count);
    }

    private sealed class ActionSink(Action<FindSemanticFinding> publish) :
        IFindSemanticFindingSink
    {
        public void Publish(FindSemanticFinding finding) => publish(finding);
    }
}
