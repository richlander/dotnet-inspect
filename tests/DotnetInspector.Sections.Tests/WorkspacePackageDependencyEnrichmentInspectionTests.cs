using System.IO.Compression;
using System.Text;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed class WorkspacePackageDependencyEnrichmentInspectionTests
{
    private const string RootPackageId = "workspace.enrichment.root";
    private const string RootVersion = "1.0.0";
    private const string TargetPackageId = "workspace.enrichment.target";
    private const string TargetVersion = "1.0.0";
    private const string SourceUrl = "https://example.test/v3/index.json";

    private static readonly PackageSource Source =
        new("test", SourceUrl);
    [Fact]
    public async Task ExecuteAsync_AppendsDirectDependenciesPerContext()
    {
        string feed = CreateTemporaryDirectory();
        try
        {
            WriteLocalSourcePackage(feed, "dependency.a", "2.0.0");
            WriteLocalSourcePackage(feed, "dependency.b", "3.0.0");
            WriteLocalSourcePackage(feed, "dependency.shared", "1.0.0");
            var store = await CachedRootStoreAsync(
                """
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="dependency.a" version="[2.0.0]" />
                    <dependency id="dependency.shared" version="[1.0.0]" />
                  </group>
                  <group targetFramework="net9.0">
                    <dependency id="dependency.b" version="[3.0.0]" />
                    <dependency id="dependency.shared" version="[1.0.0]" />
                  </group>
                </dependencies>
                """);
            using var client = new HttpClient(new FailingHandler());
            await using var composition =
                new DesktopPackageSourceComposition(TimeSpan.FromSeconds(10));

            InspectionEnvelope<WorkspacePackageDependencyEnrichmentOutcome>
                envelope =
                    await WorkspacePackageDependencyEnrichmentInspection
                        .ExecuteAsync(
                            new WorkspacePackageDependencyEnrichmentRequest(
                                Definitions(),
                                LoadOptions(client, store),
                                new DesktopPackageDependencyCandidateSource(
                                    composition,
                                    new NuGetSourceOptions
                                    {
                                        Sources = [feed],
                                    })),
                            TestContext.Current.CancellationToken);

            Assert.True(
                envelope.Content
                    is WorkspacePackageDependencyEnrichmentOutcome.Succeeded,
                envelope.Content
                    is WorkspacePackageDependencyEnrichmentOutcome.Failed
                        failedOutcome
                    ? $"{failedOutcome.Failure.Kind}: "
                        + failedOutcome.Failure.Message
                    : null);
            var success =
                (WorkspacePackageDependencyEnrichmentOutcome.Succeeded)
                    envelope.Content;
            Assert.Equal(3, success.SelectedRootCount);
            Assert.Equal(3, success.AddedMemberCount);
            Assert.All(
                success.Definitions.Records,
                record => Assert.Equal(
                    InspectionDefinitionSchema.Version4,
                    record.SchemaVersion));

            WorkspaceDefinition workspace = success.Definitions.Workspace!;
            Assert.Equal(
                [
                    $"{RootPackageId}@{RootVersion}",
                    "dependency.a@2.0.0",
                    "dependency.shared@1.0.0",
                ],
                PackageMembers(workspace.Contexts[0]));
            Assert.Equal(
                [
                    $"{RootPackageId}@{RootVersion}",
                    "dependency.b@3.0.0",
                    "dependency.shared@1.0.0",
                ],
                PackageMembers(workspace.Contexts[1]));
            Assert.All(
                workspace.Contexts[0].Members
                    .OfType<
                        DefinitionMemberCoordinate.PackageCoordinate>()
                    .Skip(2),
                member =>
                {
                    Assert.Null(member.Framework);
                    Assert.Null(member.RuntimeIdentifier);
                });
            Assert.Equal(
                ["t0", "t1", "t2", "t3", "t4", "t5"],
                success.Definitions.Navigation!.Tabs.Select(tab => tab.Id));
            Assert.Equal("t0", success.Definitions.Navigation.Focus);
            Assert.Equal(
                [null, "t0", "t1", "t2", "t3", "t4", "t5"],
                success.Definitions.View!.States.Select(
                    state => state.Navigation));
            Assert.IsType<PortableSubjectRequest.Library>(
                success.Definitions.View.States[1].Subject);
            Assert.IsType<PortableRetainedSubjectContext.AllLibraries>(
                success.Definitions.View.States[1].Context);

            var share =
                Assert.IsType<InspectionPortableProjection.Available>(envelope.PortableProjection);
            WorkspaceSharePacket packet =
                WorkspaceSharePacketCodec.Decode(
                    share.Packet,
                    TestContext.Current.CancellationToken);
            Assert.Equal(
                WorkspaceSharePacketCodec.Format4Version,
                packet.FormatVersion);
            Assert.IsType<PortableSubjectRequest.Library>(
                packet.ViewStates[1].Subject);
            Assert.IsType<PortableRetainedSubjectContext.AllLibraries>(
                packet.ViewStates[1].Context);
            Assert.Equal(6, packet.Tabs.Count);
            Assert.Equal([0, 1, 3], packet.Contexts[0].TabIndexes);
            Assert.Equal([2, 4, 5], packet.Contexts[1].TabIndexes);
            Assert.Equal(
                "https://dotnet-inspect.net/?w=" + share.Packet,
                share.FullUrl);
        }
        finally
        {
            Directory.Delete(feed, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_InheritsContextWideTargetAndDeduplicatesExistingMember()
    {
        string feed = CreateTemporaryDirectory();
        try
        {
            WriteLocalSourcePackage(feed, "dependency.a", "2.0.0");
            var store = await CachedRootStoreAsync(
                """
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="dependency.a" version="[2.0.0]" />
                  </group>
                </dependencies>
                """);
            await CommitCachedPackageAsync(
                store,
                TargetPackageId,
                TargetVersion,
                dependencies: "",
                frameworks: ["net8.0"]);
            using var client = new HttpClient(new FailingHandler());
            await using var composition =
                new DesktopPackageSourceComposition(TimeSpan.FromSeconds(10));

            var inheritedRoot =
                new DefinitionMemberCoordinate.PackageCoordinate(
                    RootPackageId,
                    RootVersion);
            var targetProvider =
                new DefinitionMemberCoordinate.PackageCoordinate(
                    TargetPackageId,
                    TargetVersion,
                    Framework: "net8.0",
                    RuntimeIdentifier: "linux-x64");
            var existingDependency =
                new DefinitionMemberCoordinate.PackageCoordinate(
                    "dependency.a",
                    "2.0.0");
            CommittedScenarioDefinitionSet definitions = Prepare(
                [
                    new WorkspaceContextDefinition(
                        "inherited-target",
                        members:
                        [
                            inheritedRoot,
                            targetProvider,
                            existingDependency,
                        ]),
                ],
                [
                    new NavigationTabDefinition(
                        "t0",
                        coordinate:
                            new DefinitionMemberCoordinate.PackageCoordinate(
                                RootPackageId,
                                RootVersion,
                                Framework: "net8.0",
                                RuntimeIdentifier: "linux-x64")),
                    new NavigationTabDefinition(
                        "t1",
                        coordinate: targetProvider),
                    new NavigationTabDefinition(
                        "t2",
                        coordinate:
                            new DefinitionMemberCoordinate.PackageCoordinate(
                                "dependency.a",
                                "2.0.0",
                                Framework: "net8.0",
                                RuntimeIdentifier: "linux-x64")),
                ],
                focus: "t0");

            InspectionEnvelope<WorkspacePackageDependencyEnrichmentOutcome>
                envelope =
                    await WorkspacePackageDependencyEnrichmentInspection
                        .ExecuteAsync(
                            new WorkspacePackageDependencyEnrichmentRequest(
                                definitions,
                                LoadOptions(client, store),
                                new DesktopPackageDependencyCandidateSource(
                                    composition,
                                    new NuGetSourceOptions
                                    {
                                        Sources = [feed],
                                    })),
                            TestContext.Current.CancellationToken);

            Assert.True(
                envelope.Content
                    is WorkspacePackageDependencyEnrichmentOutcome.Succeeded,
                envelope.Content
                    is WorkspacePackageDependencyEnrichmentOutcome.Failed
                        failedOutcome
                    ? $"{failedOutcome.Failure.Kind}: "
                        + failedOutcome.Failure.Message
                    : null);
            var success =
                (WorkspacePackageDependencyEnrichmentOutcome.Succeeded)
                    envelope.Content;
            Assert.Equal(3, success.SelectedRootCount);
            Assert.Equal(0, success.AddedMemberCount);
            Assert.Equal(
                [
                    $"{RootPackageId}@{RootVersion}",
                    $"{TargetPackageId}@{TargetVersion}",
                    "dependency.a@2.0.0",
                ],
                PackageMembers(success.Definitions.Workspace!.Contexts[0]));
            Assert.Null(
                Assert.IsType<
                    DefinitionMemberCoordinate.PackageCoordinate>(
                        success.Definitions.Workspace.Contexts[0].Members[0])
                    .Framework);
            Assert.Null(
                Assert.IsType<
                    DefinitionMemberCoordinate.PackageCoordinate>(
                        success.Definitions.Workspace.Contexts[0].Members[0])
                    .RuntimeIdentifier);
            Assert.Null(
                Assert.IsType<
                    DefinitionMemberCoordinate.PackageCoordinate>(
                        success.Definitions.Workspace.Contexts[0].Members[2])
                    .Framework);
            Assert.Null(
                Assert.IsType<
                    DefinitionMemberCoordinate.PackageCoordinate>(
                        success.Definitions.Workspace.Contexts[0].Members[2])
                    .RuntimeIdentifier);
            Assert.Equal(3, success.Definitions.Navigation!.Tabs.Count);
            Assert.IsType<InspectionPortableProjection.Available>(envelope.PortableProjection);
        }
        finally
        {
            Directory.Delete(feed, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CandidateFailureEmitsNoDerivedPacket()
    {
        string feed = CreateTemporaryDirectory();
        try
        {
            WriteLocalSourcePackage(feed, "dependency.a", "2.0.0");
            var store = await CachedRootStoreAsync(
                """
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="dependency.a" version="[2.0.0]" />
                    <dependency id="dependency.missing" version="[1.0.0,2.0.0)" />
                  </group>
                </dependencies>
                """);
            using var client = new HttpClient(new FailingHandler());
            await using var composition =
                new DesktopPackageSourceComposition(TimeSpan.FromSeconds(10));

            InspectionEnvelope<WorkspacePackageDependencyEnrichmentOutcome>
                envelope =
                    await WorkspacePackageDependencyEnrichmentInspection
                        .ExecuteAsync(
                            new WorkspacePackageDependencyEnrichmentRequest(
                                SingleContextDefinitions(),
                                LoadOptions(client, store),
                                new DesktopPackageDependencyCandidateSource(
                                    composition,
                                    new NuGetSourceOptions
                                    {
                                        Sources = [feed],
                                    })),
                            TestContext.Current.CancellationToken);

            var failed = Assert.IsType<
                WorkspacePackageDependencyEnrichmentOutcome.Failed>(
                    envelope.Content);
            Assert.True(
                failed.Failure.Kind
                    == WorkspacePackageDependencyEnrichmentFailureKind
                        .CandidateResolution,
                $"{failed.Failure.Kind}: {failed.Failure.Message}");
            Assert.Contains(
                "dependency.missing",
                failed.Failure.Message,
                StringComparison.Ordinal);
            var portableProjection = Assert.IsType<
                InspectionPortableProjection.NonProjectable>(
                    envelope.PortableProjection);
            Assert.Equal(failed.Failure.Message, portableProjection.Explanation);
        }
        finally
        {
            Directory.Delete(feed, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AcquisitionTargetOutsideNuGetFrameworksSucceeds()
    {
        string feed = CreateTemporaryDirectory();
        try
        {
            var store = await CachedRootStoreAsync("", ["custom"]);
            using var client = new HttpClient(new FailingHandler());
            await using var composition =
                new DesktopPackageSourceComposition(TimeSpan.FromSeconds(10));

            InspectionEnvelope<WorkspacePackageDependencyEnrichmentOutcome>
                envelope =
                    await WorkspacePackageDependencyEnrichmentInspection
                        .ExecuteAsync(
                            new WorkspacePackageDependencyEnrichmentRequest(
                                SingleContextDefinitions("custom"),
                                LoadOptions(client, store),
                                new DesktopPackageDependencyCandidateSource(
                                    composition,
                                    new NuGetSourceOptions
                                    {
                                        Sources = [feed],
                                    })),
                            TestContext.Current.CancellationToken);

            var success = Assert.IsType<
                WorkspacePackageDependencyEnrichmentOutcome.Succeeded>(
                    envelope.Content);
            Assert.Equal(1, success.SelectedRootCount);
            Assert.Equal(0, success.AddedMemberCount);
            Assert.Equal(
                "custom",
                Assert.IsType<
                    DefinitionMemberCoordinate.PackageCoordinate>(
                        success.Definitions.Workspace!
                            .Contexts[0].Members[0])
                    .Framework);
            Assert.IsType<InspectionPortableProjection.Available>(envelope.PortableProjection);
        }
        finally
        {
            Directory.Delete(feed, recursive: true);
        }
    }

    private static CommittedScenarioDefinitionSet Definitions()
    {
        var root8 = new DefinitionMemberCoordinate.PackageCoordinate(
            RootPackageId,
            RootVersion,
            Framework: "net8.0");
        var explicitDependency =
            new DefinitionMemberCoordinate.PackageCoordinate(
                "dependency.a",
                "2.0.0",
                Framework: "net8.0");
        var root9 = new DefinitionMemberCoordinate.PackageCoordinate(
            RootPackageId,
            RootVersion,
            Framework: "net9.0");
        return Prepare(
            [
                new WorkspaceContextDefinition(
                    "net8",
                    "net8.0",
                    members: [root8, explicitDependency]),
                new WorkspaceContextDefinition(
                    "net9",
                    "net9.0",
                    members: [root9]),
            ],
            [
                new NavigationTabDefinition("t0", coordinate: root8),
                new NavigationTabDefinition(
                    "t1",
                    coordinate: explicitDependency),
                new NavigationTabDefinition("t2", coordinate: root9),
            ],
            focus: "t0",
            schemaVersion: InspectionDefinitionSchema.Version4);
    }

    private static CommittedScenarioDefinitionSet SingleContextDefinitions(
        string framework = "net8.0")
    {
        var root = new DefinitionMemberCoordinate.PackageCoordinate(
            RootPackageId,
            RootVersion,
            Framework: framework);
        return Prepare(
            [
                new WorkspaceContextDefinition(
                    framework,
                    framework,
                    members: [root]),
            ],
            [new NavigationTabDefinition("t0", coordinate: root)],
            focus: "t0");
    }

    private static CommittedScenarioDefinitionSet Prepare(
        IReadOnlyList<WorkspaceContextDefinition> contexts,
        IReadOnlyList<NavigationTabDefinition> tabs,
        string? focus,
        int schemaVersion = InspectionDefinitionSchema.Version3)
    {
        var workspace = new WorkspaceDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.WorkspaceId,
            contexts);
        var navigation = new CommittedNavigationDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.NavigationId,
            tabs,
            focus);
        var view = new CommittedViewDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.ViewId,
            [
                new CommittedViewStateDefinition(
                    navigation: null,
                    subject: new PortableSubjectRequest.Workspace()),
                .. tabs.Select((tab, index) =>
                    schemaVersion == InspectionDefinitionSchema.Version4
                        && index == 0
                        ? new CommittedViewStateDefinition(
                            tab.Id,
                            subject: new PortableSubjectRequest.Library(),
                            context:
                                new PortableRetainedSubjectContext
                                    .AllLibraries())
                        : new CommittedViewStateDefinition(tab.Id)),
            ]);
        var scenario = new ScenarioDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.ScenarioId,
            workspace: workspace.Id,
            context: contexts[0].Name,
            view: view.Id,
            navigation: navigation.Id);
        var registry = new InspectionDefinitionRegistry();
        registry.Add(workspace);
        registry.Add(navigation);
        registry.Add(view);
        registry.Add(scenario);
        InspectionDefinitionScenarioPreparationResult prepared =
            registry.PreparePacketScenario(scenario.Id);
        return prepared switch
        {
            InspectionDefinitionScenarioPreparationResult.Version3 version3 =>
                version3.Definitions,
            InspectionDefinitionScenarioPreparationResult.Version4 version4 =>
                version4.Definitions,
            _ => throw new InvalidOperationException(
                "The enrichment fixture requires schema version 3 or 4."),
        };
    }

    private static string[] PackageMembers(
        WorkspaceContextDefinition context) =>
    [
        .. context.Members
            .OfType<DefinitionMemberCoordinate.PackageCoordinate>()
            .Select(member => $"{member.Id}@{member.Version}"),
    ];

    private static WorkspaceContextLoadOptions LoadOptions(
        HttpClient client,
        IPackageStore store) =>
        new()
        {
            HttpClient = client,
            SourceAuthorization =
                new UniformPackageSourceAuthorization([Source]),
            PackageStore = store,
        };

    private static async Task<IPackageStore> CachedRootStoreAsync(
        string dependencies,
        IReadOnlyList<string>? frameworks = null)
    {
        var store = new InMemoryPackageStore();
        await CommitCachedPackageAsync(
            store,
            RootPackageId,
            RootVersion,
            dependencies,
            frameworks ?? ["net8.0", "net9.0"]);
        await CommitCachedPackageAsync(
            store,
            "dependency.a",
            "2.0.0",
            dependencies: "",
            frameworks: ["net8.0"]);
        return store;
    }

    private static async Task CommitCachedPackageAsync(
        IPackageStore store,
        string packageId,
        string version,
        string dependencies,
        IReadOnlyList<string> frameworks)
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(WorkspacePackageDependencyEnrichmentInspection)
                .Assembly.Location,
            TestContext.Current.CancellationToken);
        var entries =
            new List<(string EntryPath, byte[] Content)>
            {
                ($"{packageId}.nuspec",
                    ManifestBytes(
                        packageId,
                        version,
                        dependencies)),
            };
        foreach (string framework in frameworks)
        {
            entries.Add(
                ($"ref/{framework}/DotnetInspector.Sections.dll", assembly));
        }
        await store.CommitAsync(
            packageId,
            version,
            NuGetCache.GetSourceKey(SourceUrl),
            new MemoryStream(Archive([.. entries])),
            TestContext.Current.CancellationToken);
    }

    private static void WriteLocalSourcePackage(
        string folder,
        string packageId,
        string version)
    {
        string path = Path.Combine(
            folder,
            $"{packageId}.{version}.nupkg");
        File.WriteAllBytes(
            path,
            Archive(
                ($"{packageId}.nuspec",
                    ManifestBytes(packageId, version, ""))));
    }

    private static byte[] ManifestBytes(
        string packageId,
        string version,
        string dependencies) =>
        Encoding.UTF8.GetBytes(
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
                <authors>dotnet-inspect</authors>
                <description>Workspace dependency enrichment fixture.</description>
                {{dependencies}}
              </metadata>
            </package>
            """);

    private static byte[] Archive(
        params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(content);
            }
        }
        return buffer.ToArray();
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-workspace-enrichment-"
                + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected request to {request.RequestUri}.");
    }
}
