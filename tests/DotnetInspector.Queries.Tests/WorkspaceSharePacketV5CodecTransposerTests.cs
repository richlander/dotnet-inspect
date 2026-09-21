using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceSharePacketV5CodecTransposerTests
{
    private const string PacketJson =
        """{"f":5,"s":[["github","https://nuget.pkg.github.com/example/index.json","p","example"],["nuget","https://api.nuget.org/v3/index.json"],["ado","https://pkgs.dev.azure.com/example/_packaging/feed/nuget/v3/index.json","c"]],"t":[["Private.Package","1.2.3","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""";

    [Fact]
    public void PackageSources_PacketRecordsPacket_IsByteIdenticalAndCredentialFree()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            PacketJson,
            TestContext.Current.CancellationToken);

        Assert.Equal(WorkspaceSharePacketCodec.Format5Version, packet.FormatVersion);
        Assert.Collection(
            packet.PackageSources,
            source =>
            {
                Assert.Equal("github", source.Id);
                Assert.Equal(
                    "https://nuget.pkg.github.com/example/index.json",
                    source.Endpoint);
                Assert.Equal(
                    WorkspacePackageSourceAuthentication.BasicPat,
                    source.Authentication);
                Assert.Equal("example", source.Username);
            },
            source =>
            {
                Assert.Equal("nuget", source.Id);
                Assert.Equal(
                    WorkspacePackageSourceAuthentication.Anonymous,
                    source.Authentication);
                Assert.Null(source.Username);
            },
            source =>
            {
                Assert.Equal("ado", source.Id);
                Assert.Equal(
                    WorkspacePackageSourceAuthentication.CredentialProvider,
                    source.Authentication);
                Assert.Null(source.Username);
            });

        CommittedScenarioDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet,
                TestContext.Current.CancellationToken);
        WorkspaceDefinition workspace = Assert.IsType<WorkspaceDefinition>(
            definitions.Workspace);
        Assert.Equal(
            InspectionDefinitionSchema.Version5,
            definitions.Scenario.SchemaVersion);
        Assert.Equal(3, workspace.PackageSources.Count);

        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                definitions,
                TestContext.Current.CancellationToken);
        WorkspaceSharePacket roundTripped =
            Assert.IsType<WorkspaceSharePacket>(projection.Packet);

        Assert.True(projection.Succeeded);
        Assert.Equal(
            PacketJson,
            WorkspaceSharePacketCodec.SerializeJson(roundTripped));
        Assert.Equal(
            WorkspaceSharePacketCodec.Encode(packet),
            WorkspaceSharePacketCodec.Encode(roundTripped));
        Assert.DoesNotContain(
            "secret",
            WorkspaceSharePacketCodec.SerializeJson(roundTripped),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkspaceRecordJson_RoundTripsPackageSourceRequirements()
    {
        var workspace = new WorkspaceDefinition(
            InspectionDefinitionSchema.Version5,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "g0",
                    members:
                    [
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            "Private.Package",
                            "1.2.3",
                            "net10.0",
                            RuntimeIdentifier: null),
                    ]),
            ],
            registrations: [],
            packageSources:
            [
                new WorkspacePackageSourceDefinition(
                    "github",
                    "https://nuget.pkg.github.com/example/index.json",
                    WorkspacePackageSourceAuthentication.BasicPat,
                    "example"),
                new WorkspacePackageSourceDefinition(
                    "ado",
                    "https://pkgs.dev.azure.com/example/_packaging/feed/nuget/v3/index.json",
                    WorkspacePackageSourceAuthentication.CredentialProvider),
            ]);

        string json = InspectionDefinitionJson.Serialize(workspace);
        var roundTripped = Assert.IsType<WorkspaceDefinition>(
            InspectionDefinitionJson.Parse(json));
        WorkspacePackageSourceDefinition source =
            roundTripped.PackageSources[0];

        Assert.Equal("github", source.Id);
        Assert.Equal("example", source.Username);
        Assert.Equal(
            WorkspacePackageSourceAuthentication.BasicPat,
            source.Authentication);
        Assert.Equal(
            WorkspacePackageSourceAuthentication.CredentialProvider,
            roundTripped.PackageSources[1].Authentication);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(json, InspectionDefinitionJson.Serialize(roundTripped));
    }

    [Theory]
    [InlineData(
        """{"f":5,"s":[["github","http://example.invalid/v3/index.json"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":5,"s":[["github","https://user@example.invalid/v3/index.json"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":5,"s":[["github","https://example.invalid/v3/index.json?token=x"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":5,"s":[["github","https://example.invalid/v3/index.json","p",""]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":5,"s":[["github","https://example.invalid/a"],["github","https://example.invalid/b"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":5,"s":[["public","https://example.invalid/public"],["private","https://example.invalid/private","c"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    public void InvalidSourceDeclarations_AreRejected(string json)
    {
        WorkspaceSharePacketException exception =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    json,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceSharePacketFailureKind.InvalidShape,
            exception.Kind);
    }

    [Theory]
    [InlineData(
        """{"f":3,"s":[["github","https://example.invalid/v3/index.json"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":4,"s":[["github","https://example.invalid/v3/index.json"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    public void OlderPacketFormats_RejectPackageSources(string json)
    {
        WorkspaceSharePacketException exception =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    json,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceSharePacketFailureKind.InvalidShape,
            exception.Kind);
    }
}
