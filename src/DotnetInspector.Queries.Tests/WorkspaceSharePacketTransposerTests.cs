using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceSharePacketTransposerTests
{
    private const string CanonicalVector =
        "eyJmIjoxLCJ0IjpbWyI6UGxhdGZvcm0iLCIxMC4wLjEwIiwibmV0MTAuMCIsbnVsbF0s"
        + "WyJTeXN0ZW0uVGV4dC5Kc29uIiwiMTAuMC4wIiwibmV0MTAuMCIsbnVsbF1dLCJnIjpb"
        + "WzAsMV1dLCJhIjoxLCJ4IjowLCJ2IjoiYXBpIiwieSI6IlN5c3RlbS5UZXh0Lkpzb24u"
        + "SnNvblNlcmlhbGl6ZXIiLCJsIjpbIlN5c3RlbS5UZXh0Lkpzb24iXX0";

    [Fact]
    public void ToDefinitions_TransposesCanonicalComposition()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            CanonicalVector,
            TestContext.Current.CancellationToken);

        WorkspaceSharePacketDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToDefinitions(
                packet,
                TestContext.Current.CancellationToken);

        Assert.Equal(4, definitions.Records.Count);
        WorkspaceContextDefinition context = Assert.Single(
            definitions.Workspace.Contexts);
        Assert.Equal("g0", context.Name);
        Assert.Equal(":Platform@10.0.10", context.Subscribe);
        Assert.Equal("net10.0", context.Framework);
        var package = Assert.IsType<DefinitionMemberCoordinate.PackageCoordinate>(
            Assert.Single(context.Members));
        Assert.Equal("System.Text.Json", package.Id);
        Assert.Equal("10.0.0", package.Version);
        Assert.Equal("net10.0", package.Framework);

        Assert.Equal(["t0", "t1"], definitions.Navigation.Tabs.Select(tab => tab.Id));
        Assert.Equal(":Platform@10.0.10", definitions.Navigation.Tabs[0].Subscribe);
        Assert.Equal("t1", definitions.Navigation.Focus);
        Assert.Equal("api", definitions.View.Lens);
        Assert.Equal("System.Text.Json.JsonSerializer", definitions.View.Type);
        Assert.Equal(["System.Text.Json"], definitions.View.Libraries);
        string viewJson = InspectionDefinitionJson.Serialize(definitions.View);
        Assert.Contains("\"library\": \"System.Text.Json\"", viewJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"libraries\"", viewJson, StringComparison.Ordinal);
        Assert.Equal("g0", definitions.Scenario.Context);
        Assert.Equal(definitions.Workspace.Id, definitions.Scenario.Workspace);
        Assert.Equal(definitions.Navigation.Id, definitions.Scenario.Navigation);
        Assert.Equal(definitions.View.Id, definitions.Scenario.View);
    }

    [Fact]
    public void Transpose_CanonicalPacket_RoundTripsByteForByte()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            CanonicalVector,
            TestContext.Current.CancellationToken);

        WorkspaceSharePacketProjectionResult projection =
            Project(Transpose(packet));

        Assert.True(projection.Succeeded);
        Assert.Null(projection.Failure);
        Assert.Equal(
            CanonicalVector,
            WorkspaceSharePacketCodec.Encode(Assert.IsType<WorkspaceSharePacket>(
                projection.Packet)));
    }

    [Fact]
    public void Transpose_PreservesIndependentFocusAndSelectedContext()
    {
        WorkspaceSharePacket packet = CreatePacket(
            [
                Package("P", framework: "net10.0"),
                Package("Q", framework: "net10.0"),
            ],
            [new WorkspaceShareContext([0]), new WorkspaceShareContext([1])],
            activeTabIndex: 1,
            selectedContextIndex: 0);

        WorkspaceSharePacketDefinitionSet definitions =
            Transpose(packet);
        WorkspaceSharePacketProjectionResult projection =
            Project(definitions);

        Assert.Equal("t1", definitions.Navigation.Focus);
        Assert.Equal("g0", definitions.Scenario.Context);
        WorkspaceSharePacket roundTripped = Assert.IsType<WorkspaceSharePacket>(
            projection.Packet);
        Assert.Equal(1, roundTripped.ActiveTabIndex);
        Assert.Equal(0, roundTripped.SelectedContextIndex);
    }

    [Fact]
    public void Transpose_PreservesRepeatedTupleAcrossContexts()
    {
        WorkspaceSharePacket packet = CreatePacket(
            [
                Group(":Platform", "10.0.10", "net10.0"),
                Package("P", "1.0.0", "net10.0"),
            ],
            [
                new WorkspaceShareContext([0]),
                new WorkspaceShareContext([0, 1]),
            ],
            activeTabIndex: 0,
            selectedContextIndex: 1);

        WorkspaceSharePacketProjectionResult projection =
            Project(Transpose(packet));

        WorkspaceSharePacket roundTripped = Assert.IsType<WorkspaceSharePacket>(
            projection.Packet);
        Assert.Equal([0], roundTripped.Contexts[0].TabIndexes);
        Assert.Equal([0, 1], roundTripped.Contexts[1].TabIndexes);
        Assert.Equal(
            WorkspaceSharePacketCodec.Encode(packet),
            WorkspaceSharePacketCodec.Encode(roundTripped));
    }

    [Fact]
    public void Transpose_PreservesComposedGroupBasePin()
    {
        WorkspaceSharePacket packet = CreatePacket(
            [Group(":Platform:AspNetCore+Extensions", "10.0.10", "net10.0")],
            [new WorkspaceShareContext([0])]);

        WorkspaceSharePacketDefinitionSet definitions = Transpose(packet);
        WorkspaceSharePacketProjectionResult projection = Project(definitions);

        Assert.Equal(
            ":Platform@10.0.10:AspNetCore+Extensions",
            definitions.Workspace.Contexts[0].Subscribe);
        Assert.Equal(
            WorkspaceSharePacketCodec.Encode(packet),
            WorkspaceSharePacketCodec.Encode(Assert.IsType<WorkspaceSharePacket>(
                projection.Packet)));
    }

    [Fact]
    public void Transpose_PreservesMultipleLibraryScopeAndDefinitionJson()
    {
        WorkspaceSharePacket packet = CreatePacket(
            [Package("P", framework: "net10.0")],
            [new WorkspaceShareContext([0])],
            libraries: ["A", "Z"]);

        WorkspaceSharePacketDefinitionSet definitions =
            Transpose(packet);
        string json = InspectionDefinitionJson.Serialize(definitions.View);
        var parsed = Assert.IsType<ViewDefinition>(InspectionDefinitionJson.Parse(json));
        WorkspaceSharePacketDefinitionSet reparsed = new(
            definitions.Workspace,
            definitions.Navigation,
            parsed,
            definitions.Scenario);

        Assert.Equal(["A", "Z"], parsed.Libraries);
        Assert.Contains("\"libraries\": [", json, StringComparison.Ordinal);
        Assert.Equal(
            WorkspaceSharePacketCodec.Encode(packet),
            WorkspaceSharePacketCodec.Encode(Assert.IsType<WorkspaceSharePacket>(
                Project(reparsed).Packet)));
    }

    [Fact]
    public void ToPacket_CanonicalizesInheritedContextTargets()
    {
        WorkspaceSharePacketDefinitionSet definitions = CreateDefinitions(
            new WorkspaceDefinition(
                1,
                "ws",
                [
                    new WorkspaceContextDefinition(
                        "c",
                        members:
                        [
                            new DefinitionMemberCoordinate.PackageCoordinate(
                                "P",
                                "1.0.0",
                                "net10.0"),
                        ]),
                ]),
            new NavigationDefinition(
                1,
                "nav",
                [
                    new NavigationTabDefinition(
                        "p",
                        coordinate: new DefinitionMemberCoordinate.PackageCoordinate(
                            "P",
                            "1.0.0")),
                ],
                "p"));

        WorkspaceSharePacketProjectionResult projection =
            Project(definitions);

        WorkspaceSharePacket packet = Assert.IsType<WorkspaceSharePacket>(
            projection.Packet);
        Assert.Equal("net10.0", packet.Tabs[0].Framework);
        Assert.Equal([0], packet.Contexts[0].TabIndexes);
    }

    [Fact]
    public void ToPacket_RejectsAmbiguousNavigationTarget()
    {
        WorkspaceSharePacketDefinitionSet definitions = CreateDefinitions(
            new WorkspaceDefinition(
                1,
                "ws",
                [
                    Context("a", "net9.0", PackageCoordinate("P", "1.0.0")),
                    Context("b", "net10.0", PackageCoordinate("P", "1.0.0")),
                ]),
            new NavigationDefinition(
                1,
                "nav",
                [
                    new NavigationTabDefinition(
                        "p",
                        coordinate: PackageCoordinate("P", "1.0.0")),
                ],
                "p"));

        WorkspaceSharePacketProjectionResult projection =
            Project(definitions);

        Assert.False(projection.Succeeded);
        Assert.Equal(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            projection.Failure?.Kind);
        Assert.Equal("navigation.tabs[0]", projection.Failure?.Path);
        Assert.Contains("ambiguous", projection.Failure?.Message);
    }

    [Fact]
    public void ToPacket_RejectsMissingNavigationTarget()
    {
        WorkspaceSharePacketDefinitionSet definitions = CreateDefinitions(
            new WorkspaceDefinition(
                1,
                "ws",
                [Context("c", "net10.0", PackageCoordinate("P", "1.0.0"))]),
            new NavigationDefinition(
                1,
                "nav",
                [
                    new NavigationTabDefinition(
                        "q",
                        coordinate: PackageCoordinate("Q", "1.0.0", "net10.0")),
                ],
                "q"));

        WorkspaceSharePacketProjectionResult projection =
            Project(definitions);

        Assert.Equal("navigation.tabs[0]", projection.Failure?.Path);
        Assert.Contains("does not match", projection.Failure?.Message);
    }

    [Theory]
    [InlineData(":Custom@1.0.0")]
    [InlineData(":Platform+:Custom@1.0.0")]
    [InlineData(":Platform@10.0.10+:Custom@1.0.0")]
    public void ToPacket_RejectsNonV1GroupPins(string subscribe)
    {
        WorkspaceSharePacketDefinitionSet definitions = CreateDefinitions(
            new WorkspaceDefinition(
                1,
                "ws",
                [new WorkspaceContextDefinition("c", "net10.0", subscribe: subscribe)]),
            new NavigationDefinition(
                1,
                "nav",
                [new NavigationTabDefinition("g", subscribe: subscribe, framework: "net10.0")],
                "g"));

        WorkspaceSharePacketProjectionResult projection =
            Project(definitions);

        Assert.Equal(
            WorkspaceSharePacketProjectionFailureKind.NonProjectable,
            projection.Failure?.Kind);
        Assert.Contains("pin", projection.Failure?.Message);
    }

    [Fact]
    public void ToPacket_RejectsMemberKeyRatherThanDroppingIt()
    {
        WorkspaceSharePacketDefinitionSet baseDefinitions = CreateDefinitions(
            new WorkspaceDefinition(
                1,
                "ws",
                [Context("c", "net10.0", PackageCoordinate("P", "1.0.0"))]),
            new NavigationDefinition(
                1,
                "nav",
                [
                    new NavigationTabDefinition(
                        "p",
                        coordinate: PackageCoordinate("P", "1.0.0", "net10.0")),
                ],
                "p"));
        var view = new ViewDefinition(1, "view", type: "N.T", memberKey: "method:M");
        var definitions = new WorkspaceSharePacketDefinitionSet(
            baseDefinitions.Workspace,
            baseDefinitions.Navigation,
            view,
            new ScenarioDefinition(
                1,
                "scenario",
                workspace: "ws",
                context: "c",
                view: "view",
                navigation: "nav"));

        WorkspaceSharePacketProjectionResult projection =
            Project(definitions);

        Assert.Equal("view.memberKey", projection.Failure?.Path);
    }

    [Fact]
    public void ToPacket_RejectsPackageTabTargetOverridesRatherThanDroppingThem()
    {
        WorkspaceSharePacketDefinitionSet definitions = CreateDefinitions(
            new WorkspaceDefinition(
                1,
                "ws",
                [Context("c", "net10.0", PackageCoordinate("P", "1.0.0"))]),
            new NavigationDefinition(
                1,
                "nav",
                [
                    new NavigationTabDefinition(
                        "p",
                        coordinate: PackageCoordinate("P", "1.0.0", "net10.0"),
                        framework: "net10.0"),
                ],
                "p"));

        WorkspaceSharePacketProjectionResult projection = Project(definitions);

        Assert.Equal("navigation.tabs[0]", projection.Failure?.Path);
        Assert.Contains("coordinate", projection.Failure?.Message);
    }

    [Fact]
    public void ToPacket_RejectsDuplicateNavigationTabIdsWithTypedFailure()
    {
        WorkspaceSharePacketDefinitionSet definitions = CreateDefinitions(
            new WorkspaceDefinition(
                1,
                "ws",
                [
                    new WorkspaceContextDefinition(
                        "c",
                        "net10.0",
                        members:
                        [
                            PackageCoordinate("P", "1.0.0"),
                            PackageCoordinate("Q", "1.0.0"),
                        ]),
                ]),
            new NavigationDefinition(
                1,
                "nav",
                [
                    new NavigationTabDefinition(
                        "same",
                        coordinate: PackageCoordinate("P", "1.0.0", "net10.0")),
                    new NavigationTabDefinition(
                        "same",
                        coordinate: PackageCoordinate("Q", "1.0.0", "net10.0")),
                ],
                "same"));

        WorkspaceSharePacketProjectionResult projection = Project(definitions);

        Assert.Equal("navigation.tabs[1].id", projection.Failure?.Path);
    }

    [Fact]
    public void ToDefinitions_UsesCodecValidityGate()
    {
        WorkspaceShareTab[] tabs = Enumerable.Range(0, 13)
            .Select(index => Package($"P{index}", framework: "net10.0"))
            .ToArray();
        var packet = CreatePacket(
            tabs,
            [new WorkspaceShareContext(Enumerable.Range(0, 13).ToArray())]);

        WorkspaceSharePacketException exception = Assert.Throws<WorkspaceSharePacketException>(
            () => Transpose(packet));

        Assert.Equal(WorkspaceSharePacketFailureKind.InvalidShape, exception.Kind);
    }

    private static WorkspaceSharePacketDefinitionSet CreateDefinitions(
        WorkspaceDefinition workspace,
        NavigationDefinition navigation)
    {
        var view = new ViewDefinition(1, "view");
        var scenario = new ScenarioDefinition(
            1,
            "scenario",
            workspace: workspace.Id,
            context: workspace.Contexts[0].Name,
            view: view.Id,
            navigation: navigation.Id);
        return new WorkspaceSharePacketDefinitionSet(
            workspace,
            navigation,
            view,
            scenario);
    }

    private static WorkspaceSharePacketDefinitionSet Transpose(
        WorkspaceSharePacket packet) =>
        WorkspaceSharePacketTransposer.ToDefinitions(
            packet,
            TestContext.Current.CancellationToken);

    private static WorkspaceSharePacketProjectionResult Project(
        WorkspaceSharePacketDefinitionSet definitions) =>
        WorkspaceSharePacketTransposer.ToPacket(
            definitions,
            TestContext.Current.CancellationToken);

    private static WorkspaceContextDefinition Context(
        string name,
        string framework,
        params DefinitionMemberCoordinate[] members) =>
        new(name, framework, members: members);

    private static DefinitionMemberCoordinate.PackageCoordinate PackageCoordinate(
        string id,
        string? version = null,
        string? framework = null) =>
        new(id, version, framework);

    private static WorkspaceShareTab Package(
        string id,
        string? version = null,
        string? framework = null,
        string? runtimeIdentifier = null) =>
        new(
            WorkspaceShareSourceKind.Package,
            id,
            version,
            framework,
            runtimeIdentifier);

    private static WorkspaceShareTab Group(
        string expression,
        string? version = null,
        string? framework = null,
        string? runtimeIdentifier = null) =>
        new(
            WorkspaceShareSourceKind.Group,
            expression,
            version,
            framework,
            runtimeIdentifier);

    private static WorkspaceSharePacket CreatePacket(
        WorkspaceShareTab[] tabs,
        WorkspaceShareContext[] contexts,
        int activeTabIndex = 0,
        int selectedContextIndex = 0,
        string? lens = null,
        string? type = null,
        string? memberAnchor = null,
        string? memberSignature = null,
        string? section = null,
        string[]? libraries = null) =>
        new(
            tabs,
            contexts,
            activeTabIndex,
            selectedContextIndex,
            lens,
            type,
            memberAnchor,
            memberSignature,
            section,
            libraries ?? []);
}
