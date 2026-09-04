using System.Diagnostics.CodeAnalysis;
using System.Text;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageQueryTests
{
    [Fact]
    public void FacetDescriptors_HaveStableOrderedIds()
    {
        Assert.Equal(
            [
                ("package.query.source-verified", 100),
                ("package.query.dotnet-tool", 200),
                ("package.query.dotnet-tool-v1", 210),
                ("package.query.dotnet-tool-v2", 220),
                ("package.query.has-dependencies", 300),
                ("package.query.no-dependencies", 400),
                ("package.query.downloads-1m", 500),
                ("package.query.embedded-readme", 600),
                ("package.query.embedded-skill", 700),
            ],
            PackageQuery.Facets.Select(facet =>
                (facet.Id, facet.Weight)));
        Assert.Equal(
            [
                PackageQueryFacetTier.Nuspec,
                PackageQueryFacetTier.Nuspec,
                PackageQueryFacetTier.PackageContent,
                PackageQueryFacetTier.PackageContent,
                PackageQueryFacetTier.Nuspec,
                PackageQueryFacetTier.Nuspec,
                PackageQueryFacetTier.Nuspec,
                PackageQueryFacetTier.Nuspec,
                PackageQueryFacetTier.PackageContent,
            ],
            PackageQuery.Facets.Select(facet => facet.Tier));
        Assert.Equal(
            PackageQuery.DependencySelectionGroupId,
            PackageQuery.Facets.Single(facet =>
                facet.Id == PackageQuery.HasDependenciesFacetId)
                .SelectionGroupId);
        Assert.Equal(
            PackageQuery.DependencySelectionGroupId,
            PackageQuery.Facets.Single(facet =>
                facet.Id == PackageQuery.NoDependenciesFacetId)
                .SelectionGroupId);
        Assert.Equal(
            ".NET Tool",
            PackageQuery.Facets.Single(facet =>
                facet.Id == PackageQuery.ToolFacetId).Label);
        Assert.Equal(
            "embedded SKILL.md",
            PackageQuery.Facets.Single(facet =>
                facet.Id == PackageQuery.EmbeddedSkillFacetId).Label);
        Assert.All(
            PackageQuery.Facets.Where(facet =>
                facet.Id is PackageQuery.ToolFacetId
                    or PackageQuery.ToolV1FacetId
                    or PackageQuery.ToolV2FacetId),
            facet =>
            {
                Assert.Equal(
                    PackageQuery.ToolSelectionGroupId,
                    facet.SelectionGroupId);
                Assert.Equal(
                    PackageQuery.ToolDisplayGroupId,
                    facet.DisplayGroupId);
                Assert.Equal(".NET tool format", facet.DisplayGroupLabel);
            });
        Assert.False(
            PackageQuery.Facets.Single(facet =>
                facet.Id == PackageQuery.ToolFacetId)
                .CombinesWithinSelectionGroup);
        Assert.All(
            PackageQuery.Facets.Where(facet =>
                facet.Id is PackageQuery.ToolV1FacetId
                    or PackageQuery.ToolV2FacetId),
            facet => Assert.True(facet.CombinesWithinSelectionGroup));
    }

    [Theory]
    [InlineData("", PackageQueryRequestFailureReason.InvalidPrefix)]
    [InlineData(" Contoso.", PackageQueryRequestFailureReason.InvalidPrefix)]
    [InlineData("\u202EContoso.", PackageQueryRequestFailureReason.InvalidPrefix)]
    [InlineData("Contoso.", PackageQueryRequestFailureReason.InvalidCandidateLimit, 0, 1)]
    [InlineData("Contoso.", PackageQueryRequestFailureReason.InvalidCandidateLimit, PackageProfileQuery.MaximumPackageLimit + 1, 1)]
    [InlineData("Contoso.", PackageQueryRequestFailureReason.InvalidMatchLimit, 1, 0)]
    [InlineData("Contoso.", PackageQueryRequestFailureReason.InvalidMatchLimit, 1, PackageProfileQuery.MaximumPackageLimit + 1)]
    public void Plan_RejectsInvalidScopeAndBoundsWithoutThrowing(
        string prefix,
        PackageQueryRequestFailureReason expected,
        int maximumCandidates = 1,
        int maximumMatches = 1)
    {
        PackageQueryPlanResult result = PackageQuery.Plan(
            new PackageQueryRequest(
                prefix,
                MaximumCandidates: maximumCandidates,
                MaximumMatches: maximumMatches));

        Assert.Equal(
            expected,
            Assert.IsType<PackageQueryPlanResult.Rejected>(result)
                .Failure.Reason);
    }

    [Fact]
    public void Plan_AcceptsScopeOnlyQuery()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(new PackageQueryRequest("Contoso.")));

        Assert.Empty(plan.Facets);
        Assert.Equal("Contoso.", plan.Prefix.ToString());
        Assert.Equal(PackageQuery.DefaultMaximumCandidates, plan.MaximumCandidates);
        Assert.Equal(PackageQuery.DefaultMaximumMatches, plan.MaximumMatches);
    }

    [Fact]
    public void Plan_TreatsOneTrailingWildcardAsPrefixShorthand()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(new PackageQueryRequest("System.*")));

        Assert.Equal("System.", plan.Prefix.ToString());
        Assert.Equal(
            "Package ID matches prefix \"System.\".",
            plan.PrefixEvidence.ToString());
        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidPrefix,
            Rejected(PackageQuery.Plan(
                new PackageQueryRequest("System.*.Json")))
                .Reason);
        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidPrefix,
            Rejected(PackageQuery.Plan(
                new PackageQueryRequest("*")))
                .Reason);
    }

    [Fact]
    public void Plan_AcceptsMatchLimitAboveCandidateLimit()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    MaximumCandidates: 50)));

        Assert.Equal(50, plan.MaximumCandidates);
        Assert.Equal(PackageQuery.DefaultMaximumMatches, plan.MaximumMatches);
    }

    [Fact]
    public void Plan_AcceptsMaximumBounds()
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    MaximumCandidates: PackageProfileQuery.MaximumPackageLimit,
                    MaximumMatches: PackageProfileQuery.MaximumPackageLimit)));

        Assert.Equal(
            PackageProfileQuery.MaximumPackageLimit,
            plan.MaximumCandidates);
        Assert.Equal(
            PackageProfileQuery.MaximumPackageLimit,
            plan.MaximumMatches);
    }

    [Fact]
    public void Plan_RequiresThePackageContentCandidateBound()
    {
        PackageQueryRequestFailure rejected = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.EmbeddedSkillFacetId],
                    MaximumCandidates:
                        PackageQuery.MaximumPackageContentCandidates + 1)));

        Assert.Equal(
            PackageQueryRequestFailureReason
                .PackageContentCandidateLimitExceeded,
            rejected.Reason);
        Assert.Equal(
            PackageQuery.MaximumPackageContentCandidates + 1,
            rejected.Value);

        PackageQueryPlan accepted = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.EmbeddedSkillFacetId],
                    MaximumCandidates:
                        PackageQuery.MaximumPackageContentCandidates)));
        Assert.Equal(
            PackageQueryFacetTier.PackageContent,
            Assert.Single(accepted.Facets).Tier);
    }

    [Fact]
    public void Plan_RejectsInvalidUnknownDuplicateAndIncompatibleFacets()
    {
        PackageQueryRequestFailure invalid = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest("Contoso.", [""])));
        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidFacetId,
            invalid.Reason);

        PackageQueryRequestFailure unknown = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    ["package.query.unknown"])));
        Assert.Equal(
            PackageQueryRequestFailureReason.UnknownFacet,
            unknown.Reason);
        Assert.Empty(unknown.FacetIds);

        PackageQueryRequestFailure duplicate = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [
                        PackageQuery.ToolFacetId,
                        PackageQuery.ToolFacetId,
                    ])));
        Assert.Equal(
            PackageQueryRequestFailureReason.DuplicateFacet,
            duplicate.Reason);
        Assert.Equal([PackageQuery.ToolFacetId], duplicate.FacetIds);

        PackageQueryRequestFailure incompatible = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [
                        PackageQuery.NoDependenciesFacetId,
                        PackageQuery.HasDependenciesFacetId,
                    ])));
        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleFacets,
            incompatible.Reason);
        Assert.Equal(
            [
                PackageQuery.HasDependenciesFacetId,
                PackageQuery.NoDependenciesFacetId,
            ],
            incompatible.FacetIds);

        PackageQueryPlan toolVersions = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [
                        PackageQuery.ToolV1FacetId,
                        PackageQuery.ToolV2FacetId,
                    ],
                    MaximumCandidates:
                        PackageQuery.MaximumPackageContentCandidates)));
        Assert.Equal(
            [PackageQuery.ToolV1FacetId, PackageQuery.ToolV2FacetId],
            toolVersions.Facets.Select(facet => facet.Id));

        PackageQueryRequestFailure broadAndSpecific = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [
                        PackageQuery.ToolFacetId,
                        PackageQuery.ToolV1FacetId,
                    ],
                    MaximumCandidates:
                        PackageQuery.MaximumPackageContentCandidates)));
        Assert.Equal(
            PackageQueryRequestFailureReason.IncompatibleFacets,
            broadAndSpecific.Reason);
    }

    [Fact]
    public void Plan_RejectsUnsafeOrExcessiveFacetSelectionsWithoutEchoingThem()
    {
        PackageQueryRequestFailure unsafeId = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    ["package.query.\u202Eunsafe"])));
        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidFacetId,
            unsafeId.Reason);
        Assert.Empty(unsafeId.FacetIds);

        PackageQueryRequestFailure longId = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [new string('a', PackageQuery.MaximumFacetIdLength + 1)])));
        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidFacetId,
            longId.Reason);
        Assert.Empty(longId.FacetIds);

        PackageQueryRequestFailure tooMany = Rejected(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    Enumerable.Repeat(
                        PackageQuery.ToolFacetId,
                        PackageQuery.Facets.Length + 1).ToArray())));
        Assert.Equal(
            PackageQueryRequestFailureReason.TooManyFacets,
            tooMany.Reason);
        Assert.Empty(tooMany.FacetIds);
    }

    [Fact]
    public async Task ExecuteAsync_FiltersBeforeMatchLimitAndStopsManifestAcquisition()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.One", verified: false),
            Match("Contoso.Two", verified: true),
            Match("Contoso.Three", verified: false),
            Match("Contoso.Four", verified: false),
            Match("Contoso.Five", verified: true),
            Match("Contoso.Six", verified: true),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate =>
                    $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(candidate.Id)));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.VerifiedFacetId],
                    MaximumCandidates: 6,
                    MaximumMatches: 2)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            ["Contoso.Two", "Contoso.Five"],
            events.OfType<PackageQueryEvent.Match>()
                .Select(item => item.Value.Package.PackageId));
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(PackageQueryCompletionKind.MatchLimitReached, summary.Completion);
        Assert.Equal(5, summary.Candidates);
        Assert.Equal(2, summary.Matches);
        Assert.Equal(5, source.ManifestRequests.Count);
        Assert.Equal(6, source.LastSearchTake);
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsProductOrderedEvidenceForEverySelectedFacet()
    {
        var source = new FakePackageSource(
            [
                Match(
                    "Contoso.Tool",
                    verified: true,
                    totalDownloads: 1_500_000),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.tool@1.0.0"] = Manifest(
                    "Contoso.Tool",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Example.Dependency" version="[1.0.0]" />
                    </group>
                    """,
                    packageTypes:
                    """
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                    """,
                    readme: "<readme>README.md</readme>"),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [
                        PackageQuery.EmbeddedReadmeFacetId,
                        PackageQuery.MillionDownloadsFacetId,
                        PackageQuery.HasDependenciesFacetId,
                        PackageQuery.ToolFacetId,
                        PackageQuery.VerifiedFacetId,
                    ],
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal(PackageQueryFacetTier.Nuspec, match.Tier);
        Assert.Equal(
            [
                PackageQuery.PrefixEvidenceId,
                PackageQuery.VerifiedFacetId,
                PackageQuery.ToolFacetId,
                PackageQuery.HasDependenciesFacetId,
                PackageQuery.MillionDownloadsFacetId,
                PackageQuery.EmbeddedReadmeFacetId,
            ],
            match.Evidence.Select(evidence => evidence.Id));
        Assert.Equal(
            "Package ID matches prefix \"Contoso.\".",
            match.Evidence[0].Value);
        Assert.Contains(
            "1 dependency across 1 target-framework group.",
            match.Evidence[3].Value,
            StringComparison.Ordinal);
        Assert.Contains(
            "1,500,000 total downloads",
            match.Evidence[4].Value,
            StringComparison.Ordinal);
        Assert.All(
            match.Evidence,
            evidence =>
            {
                Assert.NotEmpty(evidence.Value);
                Assert.True(
                    InertString.IsPermitted(
                        TextPolicy.Prose,
                        evidence.Value));
            });
    }

    [Fact]
    public async Task ExecuteAsync_RequiresEverySelectedFacet()
    {
        var source = new FakePackageSource(
            [
                Match("Contoso.Tool", verified: true),
                Match("Contoso.Library", verified: true),
                Match("Contoso.UnverifiedTool"),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.tool@1.0.0"] = Manifest(
                    "Contoso.Tool",
                    packageTypes:
                    """
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                    """),
                ["contoso.library@1.0.0"] = Manifest(
                    "Contoso.Library"),
                ["contoso.unverifiedtool@1.0.0"] = Manifest(
                    "Contoso.UnverifiedTool",
                    packageTypes:
                    """
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [
                        PackageQuery.VerifiedFacetId,
                        PackageQuery.ToolFacetId,
                    ],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match = Assert.Single(
            events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("Contoso.Tool", match.Package.PackageId);
        Assert.Equal(
            [
                PackageQuery.PrefixEvidenceId,
                PackageQuery.VerifiedFacetId,
                PackageQuery.ToolFacetId,
            ],
            match.Evidence.Select(evidence => evidence.Id));
    }

    [Fact]
    public async Task ExecuteAsync_PackageContentFacetsMatchSkillsAndToolFormats()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.V1"),
            Match("Contoso.V2"),
            Match("Contoso.Library"),
        ];
        var source = new FakePackageSource(
            candidates,
            candidates.ToDictionary(
                candidate =>
                    $"{candidate.Id.ToLowerInvariant()}@1.0.0",
                candidate => Manifest(
                    candidate.Id,
                    packageTypes: candidate.Id == "Contoso.Library"
                        ? ""
                        : """
                          <packageTypes>
                            <packageType name="DotnetTool" />
                          </packageTypes>
                          """)));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.V1"] = new FakePackageContent(
                    ("tools/net8.0/any/DotnetToolSettings.xml",
                        "<DotNetCliTool><Commands /></DotNetCliTool>"),
                    ("SKILLS/demo/skill.MD", "# Demo")),
                ["Contoso.V2"] = new FakePackageContent(
                    ("tools/any/any/DotnetToolSettings.xml",
                        "\uFEFF<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                        + "<DotNetCliTool Version=\"2\"><Commands /></DotNetCliTool>")),
                ["Contoso.Library"] = new FakePackageContent(
                    ("skills/SKILL.md", "# Library")),
            });

        PackageQueryPlan v1Plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.ToolV1FacetId],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        List<PackageQueryEvent> v1Events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                v1Plan,
                content,
                TestContext.Current.CancellationToken));
        PackageQueryMatch v1 = Assert.Single(
            v1Events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("Contoso.V1", v1.Package.PackageId);
        Assert.Equal(PackageQueryFacetTier.PackageContent, v1.Tier);
        Assert.Equal(
            [PackageQuery.PrefixEvidenceId, PackageQuery.ToolV1FacetId],
            v1.Evidence.Select(evidence => evidence.Id));
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.Requests);

        content.Requests.Clear();
        PackageQueryPlan v2Plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.ToolV2FacetId],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        List<PackageQueryEvent> v2Events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                v2Plan,
                content,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            "Contoso.V2",
            Assert.Single(v2Events.OfType<PackageQueryEvent.Match>())
                .Value.Package.PackageId);
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.Requests);

        content.Requests.Clear();
        PackageQueryPlan bothVersionsPlan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [
                        PackageQuery.ToolV1FacetId,
                        PackageQuery.ToolV2FacetId,
                    ],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        List<PackageQueryEvent> bothVersionEvents = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                bothVersionsPlan,
                content,
                TestContext.Current.CancellationToken));
        List<PackageQueryMatch> bothVersions =
        [
            .. bothVersionEvents
                .OfType<PackageQueryEvent.Match>()
                .Select(item => item.Value),
        ];
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            bothVersions.Select(item => item.Package.PackageId));
        Assert.Equal(
            [
                [PackageQuery.PrefixEvidenceId, PackageQuery.ToolV1FacetId],
                [PackageQuery.PrefixEvidenceId, PackageQuery.ToolV2FacetId],
            ],
            bothVersions.Select(item =>
                item.Evidence.Select(evidence => evidence.Id)));
        Assert.Contains(
            "v1 format",
            bothVersions[0].Evidence[^1].Value,
            StringComparison.Ordinal);
        Assert.Contains(
            "v2 format",
            bothVersions[1].Evidence[^1].Value,
            StringComparison.Ordinal);
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.Requests);
        Assert.Equal(
            [0, 1, 2],
            bothVersionEvents
                .OfType<PackageQueryEvent.Progress>()
                .Where(item => item.Value.Phase
                    == PackageQueryProgressPhase.PackageContent)
                .Select(item => item.Value.Completed));

        content.Requests.Clear();
        PackageQueryPlan bothVersionsAndSkillPlan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [
                        PackageQuery.ToolV1FacetId,
                        PackageQuery.ToolV2FacetId,
                        PackageQuery.EmbeddedSkillFacetId,
                    ],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        PackageQueryMatch versionAndSkill = Assert.Single(
            (await CollectAsync(
                PackageQuery.ExecuteAsync(
                    source,
                    bothVersionsAndSkillPlan,
                    content,
                    TestContext.Current.CancellationToken)))
                .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("Contoso.V1", versionAndSkill.Package.PackageId);
        Assert.Equal(
            [
                PackageQuery.PrefixEvidenceId,
                PackageQuery.ToolV1FacetId,
                PackageQuery.EmbeddedSkillFacetId,
            ],
            versionAndSkill.Evidence.Select(evidence => evidence.Id));
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.Requests);

        content.Requests.Clear();
        PackageQueryPlan skillPlan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.EmbeddedSkillFacetId],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        List<PackageQueryEvent> skillEvents = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                skillPlan,
                content,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            ["Contoso.V1", "Contoso.Library"],
            skillEvents.OfType<PackageQueryEvent.Match>()
                .Select(item => item.Value.Package.PackageId));
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2", "Contoso.Library"],
            content.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidToolSettingsRemainVisible()
    {
        var source = SourceFor(
            Manifest(
                "Contoso.Tool",
                packageTypes:
                """
                <packageTypes>
                  <packageType name="DotnetTool" />
                </packageTypes>
                """),
            "Contoso.Tool");
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Tool"] = new FakePackageContent(
                    ("tools/net8.0/any/DotnetToolSettings.xml",
                        "<DotNetCliTool Version=\"2\"><Commands>")),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.ToolV2FacetId],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        PackageQueryFailure failure =
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value;
        Assert.Equal(
            PackageQueryFailureKind.PackageContentEvaluation,
            failure.Kind);
        Assert.Equal(
            1,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Failures);
    }

    [Fact]
    public async Task ExecuteAsync_PackageContentAcquisitionFailureRemainsVisible()
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>(),
            "package payload unavailable");
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.EmbeddedSkillFacetId],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        PackageQueryFailure failure =
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value;
        Assert.Equal(
            PackageQueryFailureKind.PackageContentAcquisition,
            failure.Kind);
        Assert.Equal("package payload unavailable", failure.Message);
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(1, summary.Failures);
        Assert.Equal(0, summary.Matches);
    }

    [Fact]
    public async Task ExecuteAsync_PackageContentFacetRequiresProviderBeforeSourceWork()
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.EmbeddedSkillFacetId],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CollectAsync(
                PackageQuery.ExecuteAsync(
                    source,
                    plan,
                    TestContext.Current.CancellationToken)));

        Assert.Empty(source.ManifestRequests);
        Assert.Equal(0, source.LastSearchTake);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsCloseFacetNegatives()
    {
        await AssertNoMatchesAsync(
            new FakePackageSource(
                [Match("Contoso.Downloads", totalDownloads: 999_999)],
                new Dictionary<string, byte[]>
                {
                    ["contoso.downloads@1.0.0"] =
                        Manifest("Contoso.Downloads"),
                }),
            PackageQuery.MillionDownloadsFacetId);

        await AssertNoMatchesAsync(
            SourceFor(Manifest("Contoso.NoReadme"), "Contoso.NoReadme"),
            PackageQuery.EmbeddedReadmeFacetId);

        await AssertNoMatchesAsync(
            SourceFor(
                Manifest(
                    "Contoso.BlankReadme",
                    readme: "<readme> </readme>"),
                "Contoso.BlankReadme"),
            PackageQuery.EmbeddedReadmeFacetId);

        await AssertNoMatchesAsync(
            SourceFor(
                Manifest(
                    "Contoso.Dependent",
                    dependencies:
                    """
                    <dependency id="Example.Dependency" version="[1.0.0]" />
                    """),
                "Contoso.Dependent"),
            PackageQuery.NoDependenciesFacetId);

        await AssertNoMatchesAsync(
            SourceFor(
                Manifest(
                    "Contoso.NotTool",
                    packageTypes:
                    """
                    <packageTypes>
                      <packageType name="DotnetTooling" />
                    </packageTypes>
                    """),
                "Contoso.NotTool"),
            PackageQuery.ToolFacetId);
    }

    [Fact]
    public async Task ExecuteAsync_MillionDownloadsIncludesExactThreshold()
    {
        var source = new FakePackageSource(
            [Match("Contoso.Downloads", totalDownloads: 1_000_000)],
            new Dictionary<string, byte[]>
            {
                ["contoso.downloads@1.0.0"] =
                    Manifest("Contoso.Downloads"),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.MillionDownloadsFacetId],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Single(events.OfType<PackageQueryEvent.Match>());
    }

    [Fact]
    public async Task ExecuteAsync_ScopeOnlyQueryCarriesNonEmptyScopeEvidence()
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence evidence = Assert.Single(match.Evidence);
        Assert.Equal(PackageQuery.PrefixEvidenceId, evidence.Id);
    }

    [Fact]
    public async Task ExecuteAsync_UsesNormalizedTrailingWildcardPrefix()
    {
        var source = SourceFor(
            Manifest("System.Text.Json"),
            "System.Text.Json");
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "System.*",
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("System.Text.Json", match.Package.PackageId);
        Assert.Equal(
            "Package ID matches prefix \"System.\".",
            Assert.Single(match.Evidence).Value);
        Assert.Equal("System.", plan.Prefix.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_AllEmptyDependencyGroupsMatchNoDependencies()
    {
        byte[] manifest = Manifest(
            "Contoso.EmptyGroups",
            dependencies:
            """
            <group targetFramework="net8.0"></group>
            <group targetFramework="net9.0"></group>
            """);

        var noDependenciesSource = SourceFor(
            manifest,
            "Contoso.EmptyGroups");
        PackageQueryPlan noDependencies = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.NoDependenciesFacetId],
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));
        List<PackageQueryEvent> noDependencyEvents = await CollectAsync(
            PackageQuery.ExecuteAsync(
                noDependenciesSource,
                noDependencies,
                TestContext.Current.CancellationToken));
        Assert.Single(noDependencyEvents.OfType<PackageQueryEvent.Match>());

        var hasDependenciesSource = SourceFor(
            manifest,
            "Contoso.EmptyGroups");
        PackageQueryPlan hasDependencies = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.HasDependenciesFacetId],
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));
        List<PackageQueryEvent> hasDependencyEvents = await CollectAsync(
            PackageQuery.ExecuteAsync(
                hasDependenciesSource,
                hasDependencies,
                TestContext.Current.CancellationToken));
        Assert.Empty(hasDependencyEvents.OfType<PackageQueryEvent.Match>());
        Assert.Equal(
            PackageQueryCompletionKind.Exhausted,
            Assert.IsType<PackageQueryEvent.Completed>(
                hasDependencyEvents[^1]).Value.Completion);
    }

    [Fact]
    public async Task ExecuteAsync_PerCandidateFailureDoesNotBecomeTerminalFailure()
    {
        var source = new FakePackageSource(
            [
                Match("Other.Package"),
                Match("Contoso.Valid"),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.valid@1.0.0"] = Manifest("Contoso.Valid"),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    MaximumCandidates: 2,
                    MaximumMatches: 2)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Single(events.OfType<PackageQueryEvent.Failure>());
        Assert.Single(events.OfType<PackageQueryEvent.Match>());
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(PackageQueryCompletionKind.Exhausted, summary.Completion);
        Assert.Equal(2, summary.Candidates);
        Assert.Equal(1, summary.Failures);
    }

    [Fact]
    public async Task ExecuteAsync_TerminalSearchFailureProducesFailedCompletion()
    {
        var source = new FakePackageSource(
            [],
            new Dictionary<string, byte[]>())
        {
            SearchFailureKind = PackageSourceFailureKind.Timeout,
        };
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(new PackageQueryRequest("Contoso.")));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Single(events.OfType<PackageQueryEvent.Failure>());
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(PackageQueryCompletionKind.Failed, summary.Completion);
        Assert.Equal(0, summary.Candidates);
        Assert.Equal(1, summary.Failures);
        Assert.Equal(
            [
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 0, 1),
            ],
            events.OfType<PackageQueryEvent.Progress>()
                .Select(item => item.Value));
    }

    [Fact]
    public async Task ExecuteAsync_TerminalSearchContractFailureProducesFailedCompletion()
    {
        var source = new FakePackageSource(
            [
                Match("Contoso.One"),
                Match("Contoso.Two"),
            ],
            new Dictionary<string, byte[]>());
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryFailure failure =
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value;
        Assert.Equal(PackageQueryFailureKind.SearchContract, failure.Kind);
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(PackageQueryCompletionKind.Failed, summary.Completion);
        Assert.Equal(0, summary.Candidates);
        Assert.Equal(1, summary.Failures);
        Assert.Empty(source.ManifestRequests);
        Assert.Equal(
            [
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 0, 1),
            ],
            events.OfType<PackageQueryEvent.Progress>()
                .Select(item => item.Value));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsBoundedProgressBeforeSparseCompletion()
    {
        var source = new FakePackageSource(
            [
                Match("Contoso.One"),
                Match("Contoso.Two"),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.one@1.0.0"] = Manifest("Contoso.One"),
                ["contoso.two@1.0.0"] = Manifest("Contoso.Two"),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.ToolFacetId],
                    MaximumCandidates: 2,
                    MaximumMatches: 2)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));
        PackageQueryProgress[] progress =
        [
            .. events.OfType<PackageQueryEvent.Progress>()
                .Select(item => item.Value),
        ];

        Assert.Equal(
            [
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 0, 1),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 1, 1),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Manifest, 1, 2),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Manifest, 2, 2),
            ],
            progress);
        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.IsType<PackageQueryEvent.Completed>(events[^1]);
        Assert.Single(events.OfType<PackageQueryEvent.Completed>());
    }

    [Fact]
    public async Task ExecuteAsync_PreservesCandidateLimitAfterFiltering()
    {
        var source = SourceFor(
            Manifest("Contoso.Library"),
            "Contoso.Library",
            PackageSearchTruncationReason.RequestedLimit);
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [PackageQuery.ToolFacetId],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(
            PackageQueryCompletionKind.CandidateLimitReached,
            summary.Completion);
        Assert.Equal(1, summary.Candidates);
        Assert.Equal(0, summary.Matches);
    }

    [Fact]
    public async Task ExecuteAsync_CountsOutOfPrefixCandidateBeforeMatchLimit()
    {
        var source = new FakePackageSource(
            [
                new SearchResult("Other.Malformed", "1.0.0"),
                Match("Contoso.Valid"),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.valid@1.0.0"] = Manifest("Contoso.Valid"),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQuerySummary summary =
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(PackageQueryCompletionKind.MatchLimitReached, summary.Completion);
        Assert.Equal(2, summary.Candidates);
        Assert.Equal(1, summary.Failures);
        Assert.Equal(
            [
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 0, 1),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Search, 1, 1),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Manifest, 1, 2),
                new PackageQueryProgress(
                    PackageQueryProgressPhase.Manifest, 2, 2),
            ],
            events.OfType<PackageQueryEvent.Progress>()
                .Select(item => item.Value));
    }

    [Fact]
    public async Task ExecuteAsync_ExactExhaustionAtMatchLimitIsConservative()
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageQueryCompletionKind.MatchLimitReached,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Completion);
        Assert.Single(source.ManifestRequests);
    }

    [Theory]
    [InlineData(
        PackageSearchTruncationReason.SourcePageLimit,
        PackageQueryCompletionKind.SourcePageLimitReached)]
    [InlineData(
        PackageSearchTruncationReason.ClientPageLimit,
        PackageQueryCompletionKind.ClientPageLimitReached)]
    public async Task ExecuteAsync_PreservesPaginationCompletion(
        PackageSearchTruncationReason truncationReason,
        PackageQueryCompletionKind expected)
    {
        var source = SourceFor(
            Manifest("Contoso.Package"),
            truncationReason: truncationReason);
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    MaximumCandidates: 2,
                    MaximumMatches: 2)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            expected,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Completion);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationStopsFurtherManifestWork()
    {
        var source = new FakePackageSource(
            [
                Match("Contoso.One"),
                Match("Contoso.Two"),
                Match("Contoso.Three"),
            ],
            new Dictionary<string, byte[]>
            {
                ["contoso.one@1.0.0"] = Manifest("Contoso.One"),
                ["contoso.two@1.0.0"] = Manifest("Contoso.Two"),
                ["contoso.three@1.0.0"] = Manifest("Contoso.Three"),
            });
        using var cancellation = new CancellationTokenSource();
        source.OnManifestRequest = count =>
        {
            if (count == 1)
                cancellation.Cancel();
        };
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CollectAsync(
                PackageQuery.ExecuteAsync(
                    source,
                    plan,
                    cancellation.Token)));

        Assert.Single(source.ManifestRequests);
        Assert.Equal(0, source.PackageRequests);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExecuteAsync_CancellationAfterMatchSuppressesCompletion(
        int maximumMatches)
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        using var cancellation = new CancellationTokenSource();
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    MaximumCandidates: 2,
                    MaximumMatches: maximumMatches)));
        await using IAsyncEnumerator<PackageQueryEvent> events =
            PackageQuery.ExecuteAsync(
                    source,
                    plan,
                    cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token);

        do
        {
            Assert.True(await events.MoveNextAsync());
        }
        while (events.Current is not PackageQueryEvent.Match);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await events.MoveNextAsync());
    }

    private static PackageQueryPlan Accepted(PackageQueryPlanResult result) =>
        Assert.IsType<PackageQueryPlanResult.Accepted>(result).Plan;

    private static PackageQueryRequestFailure Rejected(
        PackageQueryPlanResult result) =>
        Assert.IsType<PackageQueryPlanResult.Rejected>(result).Failure;

    private static SearchResult Match(
        string packageId,
        string version = "1.0.0",
        bool verified = false,
        long totalDownloads = 0)
        => new(
            packageId,
            version,
            TotalDownloads: totalDownloads,
            Verified: verified);

    private static FakePackageSource SourceFor(
        byte[] manifest,
        string packageId = "Contoso.Package",
        PackageSearchTruncationReason truncationReason =
            PackageSearchTruncationReason.None) =>
        new(
            [Match(packageId)],
            new Dictionary<string, byte[]>
            {
                [$"{packageId.ToLowerInvariant()}@1.0.0"] = manifest,
            })
        {
            SearchTruncationReason = truncationReason,
        };

    private static byte[] Manifest(
        string packageId,
        string version = "1.0.0",
        string dependencies = "",
        string packageTypes = "",
        string readme = "") =>
        Encoding.UTF8.GetBytes(
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
                <authors>Manifest Author</authors>
                <description>Package query test.</description>
                {{packageTypes}}
                {{readme}}
                <dependencies>{{dependencies}}</dependencies>
              </metadata>
            </package>
            """);

    private static async Task<List<PackageQueryEvent>> CollectAsync(
        IAsyncEnumerable<PackageQueryEvent> source)
    {
        List<PackageQueryEvent> events = [];
        await foreach (PackageQueryEvent item in source)
            events.Add(item);
        return events;
    }

    private static async Task AssertNoMatchesAsync(
        FakePackageSource source,
        string facetId)
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.",
                    [facetId],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Equal(
            PackageQueryCompletionKind.Exhausted,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Completion);
    }

    private static PackageSourceResultFactory CreateResultFactory()
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                PackageSourceAssociation.Create(),
                factory =>
                {
                    captured = factory;
                    return new FactoryOnlyPackageSourceClient(factory.Source);
                });
        return Assert.IsType<PackageSourceResultFactory>(captured);
    }

    private sealed class FactoryOnlyPackageSourceClient(
        PackageSourceResultIdentity source)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.None;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class FakePackageSource(
        IReadOnlyList<SearchResult> matches,
        IReadOnlyDictionary<string, byte[]> manifests)
        : IPackageSourceClient
    {
        private readonly PackageSourceResultFactory _results =
            CreateResultFactory();

        public PackageSourceFailureKind? SearchFailureKind { get; init; }
        public PackageSearchTruncationReason SearchTruncationReason
        {
            get;
            init;
        }
        public Action<int>? OnManifestRequest { get; set; }
        public List<string> ManifestRequests { get; } = [];
        public int LastSearchTake { get; private set; }
        public int PackageRequests { get; private set; }
        public PackageSourceResultIdentity Source => _results.Source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Search
            | PackageSourceCapabilities.Manifest;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSearchTake = take;
            PackageSourceOperationResult<PackageSearchResult> result =
                SearchFailureKind is null
                    ? _results.SucceededSearch(
                        _results.Search(
                            matches,
                            SearchTruncationReason))
                    : _results.FailedSearch(SearchFailureKind.Value);
            return Task.FromResult(result);
        }

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(packageId, version);
            string key = $"{coordinate.PackageId}@{coordinate.Version}";
            ManifestRequests.Add(key);
            OnManifestRequest?.Invoke(ManifestRequests.Count);
            PackageSourceOperationResult<PackageSourceManifest> result =
                manifests.TryGetValue(key, out byte[]? content)
                    ? _results.SucceededManifest(
                        coordinate,
                        _results.Manifest(coordinate, content))
                    : _results.FailedManifest(
                        coordinate,
                        PackageSourceFailureKind.NotFound);
            return Task.FromResult(result);
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            PackageRequests++;
            throw new NotSupportedException();
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class FakePackageQueryContentProvider(
        IReadOnlyDictionary<string, IPackageContent> content,
        string unavailableMessage = "package content unavailable")
        : IPackageQueryContentProvider
    {
        public List<string> Requests { get; } = [];

        public ValueTask<PackageQueryContentResult> GetContentAsync(
            PackageProfileMatch package,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(package.PackageId);
            return ValueTask.FromResult<PackageQueryContentResult>(
                content.TryGetValue(
                    package.PackageId,
                    out IPackageContent? packageContent)
                    ? new PackageQueryContentResult.Available(packageContent)
                    : new PackageQueryContentResult.Unavailable(
                        unavailableMessage));
        }
    }

    private sealed class FakePackageContent(
        params (string Path, string Content)[] entries)
        : IPackageContent
    {
        readonly IReadOnlyDictionary<string, byte[]> _entries =
            entries.ToDictionary(
                entry => entry.Path,
                entry => Encoding.UTF8.GetBytes(entry.Content),
                StringComparer.Ordinal);

        public string? RootPath => null;
        public string? NupkgPath => null;
        public bool FromCache => false;
        public string ProducerKey => "nuget.org";
        public bool RequiresArchiveTreeMatch => false;

        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            if (_entries.TryGetValue(relativePath, out byte[]? content))
            {
                stream = new MemoryStream(content, writable: false);
                return true;
            }

            stream = null;
            return false;
        }

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            if (!_entries.TryGetValue(relativePath, out byte[]? content)
                || content.LongLength > maxExpandedBytes)
            {
                stream = null;
                return false;
            }

            stream = new MemoryStream(content, writable: false);
            return true;
        }

        public IEnumerable<string> EnumerateEntries() => _entries.Keys;
    }
}
