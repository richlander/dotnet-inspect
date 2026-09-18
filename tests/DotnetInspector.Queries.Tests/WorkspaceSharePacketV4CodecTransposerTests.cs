using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceSharePacketV4CodecTransposerTests
{
    private const string AvaloniaJson =
        """{"f":4,"t":[["Avalonia","12.1.2","net8.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"member","l":["Avalonia.Base","12.1.2.0",null,"c8d484a7012f9a8b"],"y":"Avalonia.Data.MultiBinding","s":"M:Avalonia.Data.MultiBinding.#ctor()"},"u":{"k":"type"},"f":"type.metadata"}]}""";

    private const string RegistrationOnlyJson =
        """{"f":4,"t":[],"g":[],"r":[["p","Microsoft.Extensions."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""";

    private const string AvaloniaQueryJson =
        """{"f":4,"t":[["Avalonia","12.1.2","net8.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"q":[["type-query/v1",{}]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"member","l":["Avalonia.Base","12.1.2.0",null,"c8d484a7012f9a8b"],"y":"Avalonia.Data.MultiBinding","s":"M:Avalonia.Data.MultiBinding.#ctor()"},"u":{"k":"type"},"f":"type.metadata","q":[0],"l":[["Avalonia.Base","12.1.2.0",null,"c8d484a7012f9a8b"]]}]}""";

    [Fact]
    public void AvaloniaVersion4RecordJson_PreservesStructuredTypeAndMemberPath()
    {
        const string json =
            """
            {
              "schemaVersion": 4,
              "kind": "view",
              "id": "multi-binding-view",
              "states": [
                {
                  "navigation": null,
                  "subject": {"kind": "workspace"}
                },
                {
                  "navigation": "avalonia",
                  "subject": {"kind": "type"},
                  "context": {
                    "kind": "member",
                    "library": {
                      "name": "Avalonia.Base",
                      "version": "12.1.2.0",
                      "culture": null,
                      "publicKeyToken": "c8d484a7012f9a8b"
                    },
                    "type": {
                      "namespace": "Avalonia.Data",
                      "segments": ["MultiBinding"]
                    },
                    "memberSignature": "M:Avalonia.Data.MultiBinding.#ctor()"
                  },
                  "facet": "type.metadata"
                }
              ]
            }
            """;

        var view = Assert.IsType<CommittedViewDefinition>(
            InspectionDefinitionJson.Parse(json));
        var context = Assert.IsType<PortableRetainedSubjectContext.Member>(
            view.States[1].Context);
        string canonical = InspectionDefinitionJson.Serialize(view);
        var roundTripped = Assert.IsType<CommittedViewDefinition>(
            InspectionDefinitionJson.Parse(canonical));

        Assert.Equal(InspectionDefinitionSchema.Version4, view.SchemaVersion);
        Assert.IsType<PortableSubjectRequest.Type>(view.States[1].Subject);
        Assert.Equal("Avalonia.Base", context.LibraryIdentity.Name);
        Assert.Equal("Avalonia.Data", context.TypeIdentity.Namespace);
        Assert.Equal(["MultiBinding"], context.TypeIdentity.Segments);
        Assert.Equal("type.metadata", view.States[1].Facet);
        Assert.Equal(canonical, InspectionDefinitionJson.Serialize(roundTripped));
    }

    [Fact]
    public void AvaloniaTypeActiveMemberContext_PacketRecordsPacket_IsByteIdentical()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            AvaloniaJson,
            TestContext.Current.CancellationToken);

        Assert.Equal(WorkspaceSharePacketCodec.Format4Version, packet.FormatVersion);
        WorkspaceShareViewState packetState = packet.ViewStates[1];
        Assert.IsType<PortableSubjectRequest.Type>(packetState.Subject);
        var packetContext =
            Assert.IsType<PortableRetainedSubjectContext.EscapedMember>(
                packetState.Context);
        Assert.Equal("Avalonia.Base", packetContext.LibraryIdentity.Name);
        Assert.Equal("12.1.2.0", packetContext.LibraryIdentity.Version);
        Assert.Equal("c8d484a7012f9a8b", packetContext.LibraryIdentity.PublicKeyToken);
        Assert.Equal("Avalonia.Data.MultiBinding", packetContext.EscapedTypeIdentity);
        Assert.Equal(
            "M:Avalonia.Data.MultiBinding.#ctor()",
            packetContext.MemberSignature);
        Assert.Equal("type.metadata", packetState.Facet);

        CommittedScenarioDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet,
                TestContext.Current.CancellationToken);
        Assert.All(
            definitions.Records,
            record => Assert.Equal(
                InspectionDefinitionSchema.Version4,
                record.SchemaVersion));
        Assert.IsType<PortableSubjectRequest.Type>(
            definitions.View!.States[1].Subject);
        Assert.IsType<PortableRetainedSubjectContext.EscapedMember>(
            definitions.View.States[1].Context);

        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                definitions,
                TestContext.Current.CancellationToken);
        WorkspaceSharePacket roundTripped =
            Assert.IsType<WorkspaceSharePacket>(projection.Packet);

        Assert.True(projection.Succeeded);
        Assert.Equal(WorkspaceSharePacketCodec.Format4Version, roundTripped.FormatVersion);
        Assert.Equal(AvaloniaJson, WorkspaceSharePacketCodec.SerializeJson(roundTripped));
        Assert.Equal(
            WorkspaceSharePacketCodec.Encode(packet),
            WorkspaceSharePacketCodec.Encode(roundTripped));
    }

    [Fact]
    public void AvaloniaTypeQueryState_PacketRecordsPacket_IsByteIdentical()
    {
        var descriptor = new PortableQueryDefinitionDescriptor<string>(
            "type-query/v1",
            "type-query",
            PortableQueryDefinitionInputs.StateBound(
                [PortableSubjectRequestKind.Type],
                ["type.metadata"],
                PortableQueryInputRequirement.Required,
                PortableQueryInputRequirement.Required,
                PortableQueryInputRequirement.Required),
            static (_, _, _) =>
                new PortableQueryDefinitionResolution<string>.Accepted(
                    "bound"));
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            AvaloniaQueryJson,
            TestContext.Current.CancellationToken);

        CommittedScenarioDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet,
                [descriptor],
                TestContext.Current.CancellationToken);
        var binding = Assert.IsType<BoundCommittedQuery<string>>(
            Assert.Single(definitions.QueryBindings));
        CommittedQueryDefinition query = Assert.Single(definitions.Queries);
        CommittedViewStateDefinition state = definitions.View!.States[1];

        Assert.Equal(InspectionDefinitionSchema.Version4, query.SchemaVersion);
        Assert.Equal("bound", binding.Plan);
        Assert.Equal(
            PortableSubjectRequestKind.Type,
            binding.Attachment.SubjectKind);
        Assert.Equal("type.metadata", binding.Attachment.FacetId);
        Assert.Equal(
            definitions.Navigation!.Tabs[0].Coordinate,
            binding.Attachment.StateCoordinate);
        Assert.Same(
            definitions.Workspace!.Contexts[0],
            binding.Attachment.SelectedContext);
        Assert.Equal(
            state.Libraries,
            binding.Attachment.StateLibraryScope);
        Assert.Equal(["q0"], state.Queries);
        Assert.Equal(
            "Avalonia.Base",
            Assert.Single(state.Libraries).Name);
        Assert.Equal(
            query.Identity,
            Assert.IsType<CommittedQueryDefinition>(
                InspectionDefinitionJson.Parse(
                    InspectionDefinitionJson.Serialize(query))).Identity);

        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                definitions,
                TestContext.Current.CancellationToken);
        WorkspaceSharePacket roundTripped =
            Assert.IsType<WorkspaceSharePacket>(projection.Packet);

        Assert.True(projection.Succeeded);
        Assert.Equal(
            WorkspaceSharePacketCodec.Format4Version,
            roundTripped.FormatVersion);
        Assert.Equal(
            AvaloniaQueryJson,
            WorkspaceSharePacketCodec.SerializeJson(roundTripped));
        Assert.Equal(
            WorkspaceSharePacketCodec.Encode(packet),
            WorkspaceSharePacketCodec.Encode(roundTripped));
    }

    [Theory]
    [InlineData("workspace", PortableSubjectRequestKind.Workspace)]
    [InlineData("package", PortableSubjectRequestKind.Package)]
    [InlineData("library", PortableSubjectRequestKind.Library)]
    [InlineData("type", PortableSubjectRequestKind.Type)]
    [InlineData("member", PortableSubjectRequestKind.Member)]
    public void MemberContext_PreservesTheExplicitActiveAncestor(
        string subjectKind,
        PortableSubjectRequestKind expectedKind)
    {
        string json = AvaloniaJson.Replace(
            "\"u\":{\"k\":\"type\"}",
            $"\"u\":{{\"k\":\"{subjectKind}\"}}",
            StringComparison.Ordinal);

        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);
        CommittedScenarioDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet,
                TestContext.Current.CancellationToken);

        CommittedViewStateDefinition state = definitions.View!.States[1];
        Assert.Equal(expectedKind, state.Subject?.Kind);
        Assert.IsType<PortableRetainedSubjectContext.EscapedMember>(
            state.Context);
    }

    [Fact]
    public void LibrarySubject_AllLibrariesContext_IsValid()
    {
        const string json =
            """{"f":4,"t":[["Avalonia","12.1.2","net8.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"all-libraries"},"u":{"k":"library"},"f":"library.overview"}]}""";

        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);

        Assert.IsType<PortableSubjectRequest.Library>(
            packet.ViewStates[1].Subject);
        Assert.IsType<PortableRetainedSubjectContext.AllLibraries>(
            packet.ViewStates[1].Context);
        Assert.Equal(json, WorkspaceSharePacketCodec.SerializeJson(packet));
    }

    [Fact]
    public void WorkspaceOnlyRegistrationPacket_RemainsFormat4()
    {
        Assert.Equal(3, WorkspaceSharePacketCodec.CurrentFormatVersion);
        Assert.Equal(4, WorkspaceSharePacketCodec.Format4Version);

        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            RegistrationOnlyJson,
            TestContext.Current.CancellationToken);
        CommittedScenarioDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet,
                TestContext.Current.CancellationToken);
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                definitions,
                TestContext.Current.CancellationToken);
        WorkspaceSharePacket roundTripped =
            Assert.IsType<WorkspaceSharePacket>(projection.Packet);

        Assert.True(projection.Succeeded);
        Assert.Equal(InspectionDefinitionSchema.Version4, definitions.Scenario.SchemaVersion);
        Assert.Empty(definitions.Workspace!.Contexts);
        Assert.Single(definitions.Workspace.Registrations);
        Assert.Equal(WorkspaceSharePacketCodec.Format4Version, roundTripped.FormatVersion);
        Assert.Equal(RegistrationOnlyJson, WorkspaceSharePacketCodec.SerializeJson(roundTripped));
    }

    [Theory]
    [InlineData("library", """{"k":"package"}""")]
    [InlineData("type", """{"k":"all-libraries"}""")]
    [InlineData(
        "type",
        """{"k":"library","l":["Avalonia.Base","12.1.2.0",null,"c8d484a7012f9a8b"]}""")]
    [InlineData(
        "member",
        """{"k":"type","l":["Avalonia.Base","12.1.2.0",null,"c8d484a7012f9a8b"],"y":"Avalonia.Data.MultiBinding"}""")]
    [InlineData("member", null)]
    public void DescendantSubject_RejectsMissingOrShallowContext(
        string subjectKind,
        string? contextJson)
    {
        string retained = contextJson is null ? "" : $",\"r\":{contextJson}";
        string json =
            """{"f":4,"t":[["Avalonia","12.1.2","net8.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0"""
            + retained
            + $",\"u\":{{\"k\":\"{subjectKind}\"}}"
            + "}]}";

        WorkspaceSharePacketException exception =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    json,
                    TestContext.Current.CancellationToken));

        Assert.Equal(WorkspaceSharePacketFailureKind.InvalidShape, exception.Kind);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void OlderVersions_RejectDescendantTagsInJsonAndRecordComposition(
        int version)
    {
        string packetJson = version == 2
            ? """{"f":2,"t":[["P","1.0.0","net8.0",null]],"g":[[0]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"all-libraries"},"u":{"k":"library"}}]}"""
            : """{"f":3,"t":[["P","1.0.0","net8.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"all-libraries"},"u":{"k":"library"}}]}""";
        WorkspaceSharePacketException packetException =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    packetJson,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            WorkspaceSharePacketFailureKind.InvalidShape,
            packetException.Kind);

        string recordJson =
            $$$"""
            {
              "schemaVersion": {{{version}}},
              "kind": "view",
              "id": "view",
              "states": [
                {"navigation": null, "subject": {"kind": "workspace"}},
                {
                  "navigation": "t0",
                  "subject": {"kind": "library"},
                  "context": {"kind": "allLibraries"}
                }
              ]
            }
            """;
        Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Parse(recordJson));

        Assert.Throws<ArgumentException>(
            () => new CommittedViewDefinition(
                version,
                "view",
                [
                    new CommittedViewStateDefinition(
                        navigation: null,
                        subject: new PortableSubjectRequest.Workspace()),
                    new CommittedViewStateDefinition(
                        navigation: "t0",
                        subject: new PortableSubjectRequest.Library(),
                        context:
                            new PortableRetainedSubjectContext.AllLibraries()),
                ]));
    }

    [Fact]
    public void ExactFacetAndOmittedIntent_RemainDistinct()
    {
        string omittedJson = AvaloniaJson.Replace(
            ",\"f\":\"type.metadata\"",
            "",
            StringComparison.Ordinal);
        WorkspaceSharePacket exact = WorkspaceSharePacketCodec.ParseJson(
            AvaloniaJson,
            TestContext.Current.CancellationToken);
        WorkspaceSharePacket omitted = WorkspaceSharePacketCodec.ParseJson(
            omittedJson,
            TestContext.Current.CancellationToken);

        Assert.Equal("type.metadata", exact.ViewStates[1].Facet);
        Assert.Null(omitted.ViewStates[1].Facet);
        Assert.NotEqual(
            WorkspaceSharePacketCodec.Encode(exact),
            WorkspaceSharePacketCodec.Encode(omitted));

        foreach (WorkspaceSharePacket packet in new[] { exact, omitted })
        {
            CommittedScenarioDefinitionSet definitions =
                WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                    packet,
                    TestContext.Current.CancellationToken);
            WorkspaceSharePacketProjectionResult projection =
                WorkspaceSharePacketTransposer.ToPacket(
                    definitions,
                    TestContext.Current.CancellationToken);
            Assert.Equal(
                WorkspaceSharePacketCodec.Encode(packet),
                WorkspaceSharePacketCodec.Encode(
                    Assert.IsType<WorkspaceSharePacket>(projection.Packet)));
        }
    }

    [Fact]
    public void Version4_MixedPeerVersionsAndEmptyQueryTablesAreRejected()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version4,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "g0",
                    members:
                    [
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "Avalonia",
                            "12.1.2",
                            "net8.0",
                            RuntimeIdentifier: null),
                    ]),
            ],
            registrations: []));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version4,
            "navigation",
            [
                new NavigationTabDefinition(
                    "t0",
                    coordinate:
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "Avalonia",
                            "12.1.2",
                            "net8.0",
                            RuntimeIdentifier: null)),
            ],
            focus: "t0"));
        registry.Add(new CommittedViewDefinition(
            InspectionDefinitionSchema.Version4,
            "view",
            [
                new CommittedViewStateDefinition(
                    navigation: null,
                    subject: new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    navigation: "t0",
                    subject: new PortableSubjectRequest.Package(),
                    context: new PortableRetainedSubjectContext.Package()),
            ]));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version3,
            "scenario",
            workspace: "workspace",
            context: "g0",
            view: "view",
            navigation: "navigation"));

        Assert.Throws<InspectionDefinitionException>(
            () => registry.PrepareScenario("scenario"));

        const string queryJson =
            """{"f":4,"t":[],"g":[],"r":[["p","Microsoft.Extensions."]],"a":null,"x":null,"q":[],"v":[{"t":null,"u":{"k":"workspace"}}]}""";
        WorkspaceSharePacketException exception =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    queryJson,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            WorkspaceSharePacketFailureKind.InvalidShape,
            exception.Kind);
    }
}
