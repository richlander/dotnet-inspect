using System.Text.Json;

using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceSharePacketV5CodecTransposerTests
{
    private const string PacketJson =
        """{"f":5,"s":[["https://nuget.pkg.github.com/example/index.json","a"],["https://api.nuget.org/v3/index.json"],["https://pkgs.dev.azure.com/example/_packaging/feed/nuget/v3/index.json","a"]],"t":[["Private.Package","1.2.3","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""";

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
                Assert.Equal(
                    "https://nuget.pkg.github.com/example/index.json",
                    source.Endpoint);
                Assert.Equal(
                    WorkspacePackageSourceAuthentication.AuthenticationRequired,
                    source.Authentication);
            },
            source =>
            {
                Assert.Equal(
                    WorkspacePackageSourceAuthentication.Anonymous,
                    source.Authentication);
            },
            source =>
            {
                Assert.Equal(
                    WorkspacePackageSourceAuthentication.AuthenticationRequired,
                    source.Authentication);
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
                    "https://nuget.pkg.github.com/example/index.json",
                    WorkspacePackageSourceAuthentication.AuthenticationRequired),
                new WorkspacePackageSourceDefinition(
                    "https://pkgs.dev.azure.com/example/_packaging/feed/nuget/v3/index.json",
                    WorkspacePackageSourceAuthentication.AuthenticationRequired),
            ]);

        string json = InspectionDefinitionJson.Serialize(workspace);
        var roundTripped = Assert.IsType<WorkspaceDefinition>(
            InspectionDefinitionJson.Parse(json));
        WorkspacePackageSourceDefinition source =
            roundTripped.PackageSources[0];

        Assert.Equal(
            WorkspacePackageSourceAuthentication.AuthenticationRequired,
            source.Authentication);
        Assert.Equal(
            WorkspacePackageSourceAuthentication.AuthenticationRequired,
            roundTripped.PackageSources[1].Authentication);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            ["endpoint", "authentication"],
            document.RootElement.GetProperty("packageSources")[0]
                .EnumerateObject()
                .Select(static property => property.Name));
        Assert.Equal(json, InspectionDefinitionJson.Serialize(roundTripped));
    }

    [Theory]
    [InlineData(
        """{"f":5,"s":[["http://example.invalid/v3/index.json"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":5,"s":[["https://user@example.invalid/v3/index.json"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":5,"s":[["https://example.invalid/v3/index.json?token=x"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":5,"s":[["https://example.invalid/v3/index.json","p"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":5,"s":[["https://example.invalid/a"],["https://example.invalid/a","a"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":5,"s":[["https://example.invalid/public"],["https://example.invalid/private","a"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
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
        """{"f":3,"s":[["https://example.invalid/v3/index.json"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":4,"s":[["https://example.invalid/v3/index.json"]],"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
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
