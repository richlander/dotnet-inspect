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

public partial class PackageQueryTests
{
    [Fact]
    public async Task ExecuteAsync_SkillEvidenceUsesBoundedActualInventoryPaths()
    {
        string longPath = $"skills/{new string('a', 200)}/SKILL.md";
        var archive = new FakePackageContent(
            ("skills/z/skill.MD", "Not parsed as skill frontmatter."),
            ("docs/SKILL.md", ""),
            ("skills/SKILL.md", ""),
            ("skills/x/SKILL.md", ""),
            ("skills/b\nname/SKILL.md", ""),
            (longPath, ""),
            ("skills/not-skill.md", ""),
            ("SKILL.md", ""));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent> { ["Contoso.Package"] = archive });
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*", [Term(PackageQuery.SkillTermKey, "true")],
                MaximumCandidates: 1, MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(PackageQuery.ExecuteAsync(
            source, plan, content, TestContext.Current.CancellationToken));

        PackageQueryMatch match = Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence evidence = Assert.Single(match.Evidence,
            item => item.Id == PackageQuery.SkillTermKey);
        PackageQueryEvidenceSummary summary =
            Assert.IsType<PackageQueryEvidenceSummary>(evidence.Summary);
        Assert.Equal(5, summary.Count);
        Assert.Equal(PackageQuery.MaximumEvidencePreviewItems, summary.Preview.Length);
        Assert.Equal("skills/SKILL.md", summary.Preview[0].ToString());
        Assert.True(summary.Preview[1].IsTruncated);
        Assert.True(summary.Preview[2].WasEncoded);
        Assert.All(summary.Preview, preview =>
        {
            Assert.True(preview.ToString().Length <= PackageQuery.MaximumEvidencePreviewCharacters);
            Assert.True(InertString.IsPermitted(TextPolicy.Field, preview.ToString()));
        });
        Assert.Equal(PackageQueryEvidenceScope.Package, evidence.Scope);
        Assert.Single(source.ManifestRequests);
        Assert.Single(content.InventoryRequests);
        Assert.Empty(content.FileRequests);
        Assert.Empty(content.RequestedFilePaths);
        Assert.Empty(archive.EntryRequests);
    }

    [Theory]
    [InlineData(PackageQuery.ToolFormatTermKey, "v1")]
    [InlineData(PackageQuery.ReferencesTermKey, "System.Runtime")]
    public async Task ExecuteAsync_InventoryProvenBodyAbsenceSkipsFilesAcquisition(
        string key,
        string value)
    {
        var archive = new FakePackageContent(
            ("docs/README.md", "No requested body entries."));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = archive,
            });
        var source = SourceFor(
            Manifest(
                "Contoso.Package",
                packageTypes: key == PackageQuery.ToolFormatTermKey
                    ? """
                      <packageTypes>
                        <packageType name="DotnetTool" />
                      </packageTypes>
                      """
                    : ""),
            "Contoso.Package");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*",
                [Term(key, value)],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Empty(events.OfType<PackageQueryEvent.Failure>());
        Assert.Single(content.InventoryRequests);
        Assert.Empty(content.FileRequests);
        Assert.Empty(content.RequestedFilePaths);
        Assert.Empty(archive.EntryRequests);
    }

    [Fact]
    public async Task ExecuteAsync_MixedBodyPredicatesShareOneExactFilesRequest()
    {
        const string settingsPath =
            "tools/net8.0/any/DotnetToolSettings.xml";
        const string assemblyPath =
            "lib/net8.0/Contoso.Package.dll";
        var archive = FakePackageContent.FromBytes(
            (
                settingsPath,
                "<DotNetCliTool><Commands /></DotNetCliTool>"u8.ToArray()),
            (
                assemblyPath,
                ManagedAssemblyWithReferences(1)),
            (
                "content/unrelated.bin",
                new byte[1024 * 1024]));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = archive,
            });
        var source = SourceFor(
            Manifest(
                "Contoso.Package",
                packageTypes:
                """
                <packageTypes>
                  <packageType name="DotnetTool" />
                </packageTypes>
                """),
            "Contoso.Package");
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*",
                [
                    Term(PackageQuery.ToolFormatTermKey, "v1"),
                    Term(PackageQuery.ReferencesTermKey, "Reference00000"),
                ],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        PackageQueryMatch match = Assert.Single(
            (await CollectAsync(
                PackageQuery.ExecuteAsync(
                    source,
                    plan,
                    content,
                    TestContext.Current.CancellationToken)))
                .OfType<PackageQueryEvent.Match>()).Value;

        Assert.Equal(
            ["v1", "Reference00000"],
            match.Answers.Select(static answer => answer.Value));
        Assert.Single(content.InventoryRequests);
        Assert.Single(content.FileRequests);
        Assert.Equal(
            [[settingsPath, assemblyPath]],
            content.RequestedFilePaths);
        Assert.Equal(
            [settingsPath, assemblyPath],
            archive.EntryRequests);
    }

    [Fact]
    public async Task ExecuteAsync_ToolSettingsByteLimitSkipsFilesAcquisition()
    {
        var archive = FakePackageContent.FromBytes(
            (
                "tools/net8.0/any/DotnetToolSettings.xml",
                new byte[PackageQuery.MaximumToolSettingsBytes + 1]));
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Tool"] = archive,
            });
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
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*",
                [Term(PackageQuery.ToolFormatTermKey, "v1")],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Single(events.OfType<PackageQueryEvent.Failure>());
        Assert.Single(content.InventoryRequests);
        Assert.Empty(content.FileRequests);
        Assert.Empty(archive.EntryRequests);
    }

    [Fact]
    public async Task ExecuteAsync_PackageContentTermsMatchSkillsAndToolFormats()
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

        PackageQueryPlan anyToolPlan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.ToolTermKey, "true")],
                    MaximumCandidates: 3,
                    MaximumMatches: 3)));
        List<PackageQueryEvent> anyToolEvents = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                anyToolPlan,
                TestContext.Current.CancellationToken));
        List<PackageQueryMatch> anyTools =
        [
            .. anyToolEvents
                .OfType<PackageQueryEvent.Match>()
                .Select(item => item.Value),
        ];
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            anyTools.Select(item => item.Package.PackageId));
        Assert.All(
            anyTools,
            item => Assert.Equal(
                "DotnetTool",
                Assert.Single(item.Answers).Value));
        Assert.All(
            anyTools,
            item => Assert.Equal(
                PackageQueryAcquisitionTier.Nuspec,
                item.Tier));
        Assert.Empty(content.InventoryRequests);
        Assert.Empty(content.FileRequests);

        content.ClearRequests();
        PackageQueryPlan v1Plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.ToolFormatTermKey, "v1")],
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
        Assert.Equal(PackageQueryAcquisitionTier.PackageContent, v1.Tier);
        Assert.Equal(
            [PackageQuery.PrefixEvidenceId, PackageQuery.ToolFormatTermKey],
            v1.Evidence.Select(evidence => evidence.Id));
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.InventoryRequests);
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.FileRequests);
        Assert.Equal(
            [
                ["tools/net8.0/any/DotnetToolSettings.xml"],
                ["tools/any/any/DotnetToolSettings.xml"],
            ],
            content.RequestedFilePaths);

        content.ClearRequests();
        PackageQueryPlan v2Plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.ToolFormatTermKey, "v2")],
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
            content.InventoryRequests);
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.FileRequests);

        content.ClearRequests();
        PackageQueryPlan bothVersionsPlan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(PackageQuery.ToolFormatTermKey, "v1"),
                        Term(PackageQuery.ToolFormatTermKey, "v2"),
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
                [PackageQuery.PrefixEvidenceId, PackageQuery.ToolFormatTermKey],
                [PackageQuery.PrefixEvidenceId, PackageQuery.ToolFormatTermKey],
            ],
            bothVersions.Select(item =>
                item.Evidence.Select(evidence => evidence.Id)));
        Assert.Equal(
            "1",
            EvidenceProperty(
                bothVersions[0].Evidence[^1],
                "settings-version"));
        Assert.Equal(
            ["v1", "v2"],
            bothVersions.Select(item => Assert.Single(item.Answers).Value));
        Assert.Equal(
            "2",
            EvidenceProperty(
                bothVersions[1].Evidence[^1],
                "settings-version"));
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.InventoryRequests);
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.FileRequests);
        Assert.Equal(
            [0, 1, 2],
            bothVersionEvents
                .OfType<PackageQueryEvent.Progress>()
                .Where(item => item.Value.Phase
                    == PackageQueryProgressPhase.PackageContent)
                .Select(item => item.Value.Completed));

        content.ClearRequests();
        PackageQueryPlan bothVersionsAndSkillPlan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [
                        Term(PackageQuery.ToolFormatTermKey, "v1"),
                        Term(PackageQuery.ToolFormatTermKey, "v2"),
                        Term(PackageQuery.SkillTermKey, "true"),
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
                PackageQuery.ToolFormatTermKey,
                PackageQuery.SkillTermKey,
            ],
            versionAndSkill.Evidence.Select(evidence => evidence.Id));
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.InventoryRequests);
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2"],
            content.FileRequests);

        content.ClearRequests();
        PackageQueryPlan skillPlan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.SkillTermKey, "true")],
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
            ["SKILLS/demo/skill.MD", "skills/SKILL.md"],
            skillEvents.OfType<PackageQueryEvent.Match>()
                .Select(item => Assert.Single(
                    item.Value.Evidence[^1].Summary!.Preview).ToString()));
        Assert.Equal(
            ["Contoso.V1", "Contoso.V2", "Contoso.Library"],
            content.InventoryRequests);
        Assert.Empty(content.FileRequests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<DotNetCliTool Version=\"3\"><Commands /></DotNetCliTool>")]
    public async Task ExecuteAsync_BroadToolUsesManifestWithoutReadingSettings(
        string? settings)
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
                ["Contoso.Tool"] = settings is null
                    ? new FakePackageContent()
                    : new FakePackageContent(
                        ("tools/net8.0/any/DotnetToolSettings.xml",
                            settings)),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.ToolTermKey, "true")],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        PackageQueryMatch match = Assert.Single(
            (await CollectAsync(
                PackageQuery.ExecuteAsync(
                    source,
                    plan,
                    TestContext.Current.CancellationToken)))
                .OfType<PackageQueryEvent.Match>()).Value;

        Assert.Equal(
            "DotnetTool",
            EvidenceProperty(match.Evidence[^1], "package-type"));
        Assert.Empty(content.InventoryRequests);
        Assert.Empty(content.FileRequests);
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
                    "Contoso.*",
                    [Term(PackageQuery.ToolFormatTermKey, "v2")],
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
                    "Contoso.*",
                    [Term(PackageQuery.SkillTermKey, "true")],
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
    public async Task ExecuteAsync_PackageContentTermRequiresProviderBeforeSourceWork()
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.SkillTermKey, "true")],
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
}
