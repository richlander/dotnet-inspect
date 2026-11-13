using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public class EcosystemFindResultSessionTests
{
    [Fact]
    public void MembershipsPreserveRegistrationOrderAndPreferCoreBasis()
    {
        EcosystemFindLayer[] layers =
        [
            Layer("first", "Contoso.Core", "Contoso."),
            Layer("second", core: null, "Contoso."),
        ];
        var request = new EcosystemFindSearchRequest(
            TypeFindQuestion.Create(["Thing"], FindVisibility.Public),
            layers,
            layers.Select(static layer => layer.Registration.Id),
            maximumPrefixPackages: 10);

        var memberships = request.MembershipsForPackage("Contoso.Core");

        Assert.Equal(
            ["ecosystem.first", "ecosystem.second"],
            memberships.Select(static membership =>
                membership.Ecosystem.Value));
        Assert.Equal(
            [
                EcosystemFindMembershipBasisKind.CorePackage,
                EcosystemFindMembershipBasisKind.PackagePrefix,
            ],
            memberships.Select(static membership => membership.Basis));
    }

    [Fact]
    public void WindowExaminesUniqueFindingsAndPublishesOnlySelectedPosition()
    {
        var sink = new RecordingSink();
        var session = new EcosystemFindResultSession(
            new FindInputRowSelection(2, 2),
            sink);
        EcosystemFindFinding first = Finding("First", 1);
        EcosystemFindFinding second = Finding("Second", 2);

        Assert.Equal(
            EcosystemFindFindingPublication.Skipped,
            session.Publish(first));
        Assert.Equal(
            EcosystemFindFindingPublication.Duplicate,
            session.Publish(first));
        Assert.Equal(
            EcosystemFindFindingPublication.Published,
            session.Publish(second));
        Assert.Equal(
            EcosystemFindFindingPublication.RowLimitReached,
            session.Publish(Finding("Third", 3)));

        Assert.Equal(2, session.UniqueCandidateCount);
        Assert.Equal(1, session.Count);
        Assert.True(session.IsSatisfied);
        Assert.Same(second, Assert.Single(sink.Findings));

        EcosystemFindResult result = session.Complete(
            EcosystemFindCompletion.RowLimitReached);
        Assert.Same(second, Assert.Single(result.Findings));
    }

    [Fact]
    public void SinkFailureDoesNotCommitIdentityProgressOrRetention()
    {
        var sink = new ThrowOnceSink();
        var session = new EcosystemFindResultSession(sink: sink);
        EcosystemFindFinding finding = Finding("Retry", 1);

        Assert.Throws<InvalidOperationException>(
            () => session.Publish(finding));
        Assert.Equal(0, session.UniqueCandidateCount);
        Assert.Equal(0, session.Count);

        Assert.Equal(
            EcosystemFindFindingPublication.Published,
            session.Publish(finding));
        Assert.Equal(1, session.UniqueCandidateCount);
        Assert.Equal(1, session.Count);
        Assert.Same(
            finding,
            Assert.Single(
                session.Complete(EcosystemFindCompletion.Exhausted)
                    .Findings));
    }

    [Fact]
    public void CompletionRetainsFailuresAndUnsearchedSources()
    {
        var session = new EcosystemFindResultSession();
        session.Publish(Finding("Found", 1));
        var failure = new EcosystemFindSourceFailure(
            "Prefix",
            "Contoso.",
            "Source unavailable.");

        EcosystemFindResult result = session.Complete(
            EcosystemFindCompletion.Partial,
            [failure],
            ["Fabrikam."]);

        Assert.True(result.Incomplete);
        Assert.Same(failure, Assert.Single(result.Failures));
        Assert.Equal("Fabrikam.", Assert.Single(result.Unsearched));
    }

    private static EcosystemFindLayer Layer(
        string id,
        string? core,
        params string[] prefixes) =>
        new(new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create($"ecosystem.{id}"),
            [],
            core is null ? [] : [new PackageCoordinate(core)],
            prefixes.Select(prefix =>
                (WorkspaceEcosystemPopulationDeclaration)new
                    WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                        new PackagePrefixDeclaration(prefix)))),
            Platform: false);

    internal static EcosystemFindFinding Finding(
        string memberName,
        int order)
    {
        string fingerprint = order.ToString("x10");
        var identity = new AssemblyReferenceIdentity(
            "Fixture.Library",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        var source = new FindSourceIdentity(
            new ExactLibrarySourceCoordinate.Local(new(identity)),
            AssemblyResolutionProvenance.Local("fixture"),
            packageRequest: null,
            contextOrder: 0,
            memberOrder: order,
            identity);
        MetadataTypeDefinitionName declaringType =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixture",
                    ["Extensions"]))
            .Name;
        var anchor = new MemberAnchor(
            $"{memberName}~{fingerprint}",
            $"M:Fixture.Extensions.{memberName}()",
            fingerprint,
            declaringType.ToMetadataFullName(),
            memberName);
        MemberFindQuestion question = MemberFindQuestion.Create(
            [memberName],
            FindVisibility.Public);
        MemberFindCandidateEvaluation evaluation =
            MemberFindCandidateEvaluator.Evaluate(
                question,
                question.Patterns[0],
                source,
                [
                    new(
                        declaringType,
                        anchor,
                        DeclarationOrder: order,
                        MemberOrder: order,
                        MemberName: memberName,
                        DeclaringTypeDisplay:
                            declaringType.ToMetadataFullName(),
                        DeclaringNamespace: declaringType.Namespace,
                        Kind: "method",
                        Signature: $"{memberName}()",
                        ReturnType: "void"),
                ]);
        EcosystemFindMembership membership =
            EcosystemFindMembership.ForCorePackage(
                WorkspaceEcosystemRegistrationId.Create("ecosystem.fixture"),
                new PackageCoordinate("Fixture.Package", "1.0.0"));
        return EcosystemFindFinding.Create(
            Assert.Single(evaluation.Matches),
            [membership]);
    }

    private sealed class RecordingSink : IEcosystemFindFindingSink
    {
        public List<EcosystemFindFinding> Findings { get; } = [];

        public void Publish(EcosystemFindFinding finding) =>
            Findings.Add(finding);
    }

    private sealed class ThrowOnceSink : IEcosystemFindFindingSink
    {
        private bool _throw = true;

        public void Publish(EcosystemFindFinding finding)
        {
            if (_throw)
            {
                _throw = false;
                throw new InvalidOperationException("Publication failed.");
            }
        }
    }
}
