using System.Text;
using DotnetInspector.Queries.Definitions;
using QuerySpace;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceDiffIntentTests
{
    static WorkspaceSharePacket Presentation() => WorkspaceSharePacketCodec.Decode(
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            """{"f":1,"t":[["System.Text.Json","10.0.12","netstandard2.0",null]],"g":[[0]],"a":0,"x":0,"v":"api","y":"System.Text.Json.JsonDocumentOptions","m":"0123456789","c":"compare","l":["compile:lib/netstandard2.0/System.Text.Json.dll"]}"""))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_'), TestContext.Current.CancellationToken);

    static readonly PortableLibraryIdentity Library = new("System.Text.Json", "10.0.0.0", null, "cc7b13ffcd2ddd51");
    static readonly WorkspaceDiffIntent Intent = new("9.0.20", "member-body", "compile:lib/netstandard2.0/System.Text.Json.dll", "Il", "AllowDuplicateProperties~0123456789:1");

    [Fact]
    public void ExactMemberDiff_RoundTripsCommittedRecordsAndPacket()
    {
        WorkspaceSharePacket packet = WorkspaceDiffShareProjection.Create(Presentation(), Library, Intent);
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        WorkspaceDiffShare restored = WorkspaceDiffShareProjection.Read(WorkspaceSharePacketCodec.Decode(encoded, TestContext.Current.CancellationToken));
        Assert.Equal(Intent, restored.Intent);
        Assert.Equal(Library, restored.Library);
        Assert.Equal("10.0.12", restored.Presentation.Tabs[0].Version);
        Assert.Equal("0123456789", restored.Presentation.MemberAnchor);
        Assert.Equal("netstandard2.0", restored.Presentation.Tabs[0].Framework);
        CommittedScenarioDefinitionSet definitions = WorkspaceSharePacketTransposer.ToCommittedDefinitions(packet, TestContext.Current.CancellationToken);
        WorkspaceSharePacketProjectionResult projected = WorkspaceSharePacketTransposer.ToPacket(definitions, TestContext.Current.CancellationToken);
        Assert.True(projected.Succeeded, projected.Failure?.Message);
        Assert.Equal(encoded, WorkspaceSharePacketCodec.Encode(projected.Packet!));
        Assert.Equal(encoded, WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(WorkspaceSharePacketCodec.SerializeJson(packet), TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("baseline", "latest")]
    [InlineData("content", "future-content")]
    [InlineData("medium", "source")]
    [InlineData("body", "")]
    [InlineData("unknown", "value")]
    public void UnsupportedOrMalformedIntent_IsRefused(string key, string value)
    {
        PortableQueryIntent valid = PortableQueryPayloadCodec.Decode(Intent.ToIdentity().Payload, TestContext.Current.CancellationToken);
        var terms = valid.Terms.Where(term => term.Key != key).Append(new PortableQueryTerm(key, PortableQueryOperator.Equal, value)).ToArray();
        Assert.Throws<ArgumentException>(() => WorkspaceDiffIntent.FromIntent(PortableQueryIntent.Create(terms, [], [], [])));
    }

    [Fact]
    public void BodyFromAnotherMember_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceDiffShareProjection.Create(
            Presentation(), Library, Intent with { Body = "AllowDuplicateProperties~abcdef0123:1" }));
    }

    [Fact]
    public void MissingMemberBody_IsRejectedByAttachment()
    {
        Assert.Throws<InspectionDefinitionException>(() => WorkspaceDiffShareProjection.Create(
            Presentation(), Library, Intent with { Body = null }));
    }
}
