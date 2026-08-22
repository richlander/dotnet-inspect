using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

public sealed class JsonWireMemberRulesTests
{
    static ApiMember Property(
        params IEnumerable<JsonWireIgnoreCondition?> conditions) =>
        new()
        {
            Name = "Value",
            Kind = "property",
            HasGetter = true,
            ReturnType = "int",
            JsonIgnoreConditions = [.. conditions],
        };

    /// <summary>
    /// The directional table: only <c>WhenWriting</c> and <c>WhenReading</c>
    /// split the two directions, and the value-dependent conditions stay
    /// conservatively absent from both.
    /// </summary>
    [Theory]
    [InlineData(null, true, true)]
    [InlineData(JsonWireIgnoreCondition.Never, true, true)]
    [InlineData(JsonWireIgnoreCondition.Always, false, false)]
    [InlineData(JsonWireIgnoreCondition.WhenWritingDefault, false, false)]
    [InlineData(JsonWireIgnoreCondition.WhenWritingNull, false, false)]
    [InlineData(JsonWireIgnoreCondition.WhenWriting, false, true)]
    [InlineData(JsonWireIgnoreCondition.WhenReading, true, false)]
    public void DirectionalIgnoreConditionsSelectDirections(
        JsonWireIgnoreCondition? condition,
        bool serialized,
        bool deserialized)
    {
        ApiMember member = condition is { } value
            ? Property(value)
            : Property();

        Assert.Equal(
            serialized,
            JsonWireMemberRules.IsSerialized(
                member,
                JsonWireDirection.Serialize));
        Assert.Equal(
            deserialized,
            JsonWireMemberRules.IsSerialized(
                member,
                JsonWireDirection.Deserialize));
        Assert.Equal(
            serialized || deserialized,
            JsonWireMemberRules.IsSerialized(member));
        Assert.Equal(
            serialized != deserialized,
            JsonWireMemberRules.IsDirectionSensitive(member));
    }

    [Fact]
    public void MalformedIgnoreRowIsExcludedFromEveryDirection()
    {
        ApiMember member = Property([null]);

        Assert.True(
            JsonWireMemberRules.HasUnsupportedJsonIgnoreMetadata(member));
        Assert.False(JsonWireMemberRules.IsSerialized(member));
        Assert.False(
            JsonWireMemberRules.IsSerialized(
                member,
                JsonWireDirection.Serialize));
        Assert.False(
            JsonWireMemberRules.IsSerialized(
                member,
                JsonWireDirection.Deserialize));
        Assert.False(JsonWireMemberRules.IsDirectionSensitive(member));
    }

    [Fact]
    public void DuplicateIgnoreRowsAreExcludedFromEveryDirection()
    {
        ApiMember member = Property(
            JsonWireIgnoreCondition.Never,
            JsonWireIgnoreCondition.WhenReading);

        Assert.True(
            JsonWireMemberRules.HasUnsupportedJsonIgnoreMetadata(member));
        Assert.False(JsonWireMemberRules.IsSerialized(member));
    }

    [Fact]
    public void MalformedIncludeRowIsExcludedFromEveryDirection()
    {
        ApiMember member = Property();
        member.HasMalformedJsonInclude = true;

        Assert.True(
            JsonWireMemberRules.HasUnsupportedJsonIncludeMetadata(member));
        Assert.False(JsonWireMemberRules.IsSerialized(member));
    }

    [Fact]
    public void StaticAndCompilerGeneratedMembersRemainExcluded()
    {
        ApiMember member = Property(JsonWireIgnoreCondition.WhenReading);
        member.IsStatic = true;

        Assert.False(
            JsonWireMemberRules.IsSerialized(
                member,
                JsonWireDirection.Serialize));
    }
}
