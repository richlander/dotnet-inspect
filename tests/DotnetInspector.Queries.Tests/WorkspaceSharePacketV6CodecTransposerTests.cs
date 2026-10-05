using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceSharePacketV6CodecTransposerTests
{
    private const string CanonicalJson =
        """{"f":6,"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"a":0,"x":0,"v":"api","y":"P.HiddenType","m":"0123456789","z":"all","d":["implementation","ref/net10.0/P.dll"]}""";

    [Fact]
    public void ExactImplementationMember_PacketRecordsPacket_IsByteIdentical()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            CanonicalJson,
            TestContext.Current.CancellationToken);

        WorkspaceSharePacketDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToDefinitions(
                packet,
                TestContext.Current.CancellationToken);
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                definitions,
                TestContext.Current.CancellationToken);
        WorkspaceSharePacket roundTripped =
            Assert.IsType<WorkspaceSharePacket>(projection.Packet);

        Assert.True(projection.Succeeded);
        Assert.Equal(InspectionDefinitionSchema.Version6,
            definitions.View.SchemaVersion);
        Assert.Equal(
            WorkspaceShareMemberAccessibility.All,
            definitions.View.MemberAccessibility);
        Assert.Equal(
            WorkspaceShareDeclarationSource.Implementation,
            definitions.View.DeclarationSourceRequirement?.Source);
        Assert.Equal(
            "ref/net10.0/P.dll",
            definitions.View.DeclarationSourceRequirement?.LibraryAsset);
        Assert.Equal(
            CanonicalJson,
            WorkspaceSharePacketCodec.SerializeJson(roundTripped));
    }

    [Theory]
    [InlineData("all", WorkspaceShareMemberAccessibility.All)]
    [InlineData("public", WorkspaceShareMemberAccessibility.Public)]
    [InlineData("protected", WorkspaceShareMemberAccessibility.Protected)]
    [InlineData("internal", WorkspaceShareMemberAccessibility.Internal)]
    [InlineData("private", WorkspaceShareMemberAccessibility.Private)]
    public void MemberAccessibility_UsesClosedCanonicalVocabulary(
        string value,
        WorkspaceShareMemberAccessibility expected)
    {
        string json = CanonicalJson.Replace(
            "\"z\":\"all\"",
            $"\"z\":\"{value}\"",
            StringComparison.Ordinal);

        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, packet.MemberAccessibility);
        Assert.Equal(json, WorkspaceSharePacketCodec.SerializeJson(packet));
    }

    [Theory]
    [InlineData("""{"f":6,"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"a":0,"x":0,"y":"P.T","d":["surface","P.dll"]}""")]
    [InlineData("""{"f":6,"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"a":0,"x":0,"y":"P.T","z":"public"}""")]
    [InlineData("""{"f":6,"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"a":0,"x":0,"z":"public","d":["surface","P.dll"]}""")]
    [InlineData("""{"f":6,"t":[["P","1.0.0","net10.0",null]],"g":[[0]],"a":0,"x":0,"y":"P.T","z":"public","d":["other","P.dll"]}""")]
    public void IncompleteOrUnknownExactSymbolRequirements_AreRejected(
        string json)
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

    [Fact]
    public void Format1_RemainsClosedToFormat6Fields()
    {
        string json = CanonicalJson.Replace(
            "\"f\":6",
            "\"f\":1",
            StringComparison.Ordinal);

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
