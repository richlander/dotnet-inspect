using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceSharePacketV6Tests
{
    private const string TypeJson =
        """{"f":6,"t":[[":Platform","11.0.0-rc.1.26425.128","net11.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"type","l":["System.Text.Json","11.0.0.0",null,"cc7b13ffcd2ddd51"],"y":"System.Text.Json.JsonElement"},"u":{"k":"type"},"f":"type.overview"}]}""";
    private const string LibraryJson =
        """{"f":6,"t":[[":Platform","11.0.0-rc.1.26425.128","net11.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"library","l":["System.Text.Json","11.0.0.0",null,"cc7b13ffcd2ddd51"]},"u":{"k":"library"},"f":"library.types"}]}""";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativePacket_RecordsAndJsonRoundTrip(bool selectType)
    {
        string json = selectType ? TypeJson : LibraryJson;
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json, TestContext.Current.CancellationToken);
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        var definitions = WorkspaceSharePacketTransposer.ToCommittedDefinitions(
            WorkspaceSharePacketCodec.Decode(
                encoded, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        Assert.All(definitions.Records, record =>
        {
            Assert.Equal(InspectionDefinitionSchema.Version6, record.SchemaVersion);
            Assert.Equal(
                InspectionDefinitionJson.Serialize(record),
                InspectionDefinitionJson.Serialize(InspectionDefinitionJson.Parse(
                    InspectionDefinitionJson.Serialize(record))));
        });
        var projected = WorkspaceSharePacketTransposer.ToPacket(
            definitions, TestContext.Current.CancellationToken);
        Assert.True(projected.Succeeded, projected.Failure?.Message);
        Assert.Equal(encoded,
            WorkspaceSharePacketCodec.Encode(
                Assert.IsType<WorkspaceSharePacket>(projected.Packet)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void PriorFormats_DoNotAcquireNativeSelection(int version)
    {
        string json = TypeJson.Replace("\"f\":6", $"\"f\":{version}",
            StringComparison.Ordinal);
        if (version is 2)
            json = json.Replace("\"r\":[],", "", StringComparison.Ordinal);
        if (version is 5)
            json = json.Replace(
                "\"t\":[",
                "\"s\":[[\"https://api.nuget.org/v3/index.json\"]],\"t\":[",
                StringComparison.Ordinal);
        Assert.Throws<WorkspaceSharePacketException>(() =>
            WorkspaceSharePacketCodec.ParseJson(json,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("\"q\":[[\"package/v1\",{}]],")]
    [InlineData("\"b\":\"net11.0\",")]
    public void NativePacket_RejectsUnsupportedSelectors(string additional)
    {
        string json = TypeJson.Replace(
            "\"r\":[],", $"\"r\":[],{additional}", StringComparison.Ordinal);
        Assert.Throws<WorkspaceSharePacketException>(() =>
            WorkspaceSharePacketCodec.ParseJson(json,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void NativeType_RejectsConstructedNameAtPacketBoundary()
    {
        string json = TypeJson.Replace(
            "\"y\":\"System.Text.Json.JsonElement\"",
            "\"y\":\"System.Text.Json.JsonElement[]\"",
            StringComparison.Ordinal);
        WorkspaceSharePacketException rejected =
            Assert.Throws<WorkspaceSharePacketException>(() =>
                WorkspaceSharePacketCodec.ParseJson(
                    json, TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceSharePacketFailureKind.InvalidShape, rejected.Kind);
    }
}
