namespace ILInspector.Metadata.Tests;

/// <summary>
/// Tests for FqnParser covering all FQN patterns.
/// </summary>
public class FqnParserTests
{
    // ── Simple type names ────────────────────────────────────────────────

    [Theory]
    [InlineData("string", "System.String")]
    [InlineData("int", "System.Int32")]
    [InlineData("bool", "System.Boolean")]
    [InlineData("String", "String")]
    [InlineData("Type", "Type")]
    [InlineData("JsonSerializer", "JsonSerializer")]
    public void SimpleTypeName_ParsesCorrectly(string input, string expectedType)
    {
        var result = FqnParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedType, result.TypeName);
        Assert.Null(result.QualifiedPrefix);
        Assert.Null(result.MemberName);
        Assert.Null(result.OverloadIndex);
    }

    // ── Generic types ────────────────────────────────────────────────────

    [Theory]
    [InlineData("List<T>", "List`1")]
    [InlineData("Dictionary<TKey,TValue>", "Dictionary`2")]
    [InlineData("Span<T>", "Span`1")]
    [InlineData("ReadOnlySpan<T>", "ReadOnlySpan`1")]
    [InlineData("List`1", "List`1")]
    [InlineData("Dictionary`2", "Dictionary`2")]
    public void GenericType_NormalizesToBacktickNotation(string input, string expectedType)
    {
        var result = FqnParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedType, result.TypeName);
        Assert.Null(result.MemberName);
    }

    [Theory]
    [InlineData("Dictionary<int,List<string>>", "Dictionary`2")]
    [InlineData("Action<Func<int,string>>", "Action`1")]
    public void NestedGenericType_NormalizesCorrectly(string input, string expectedType)
    {
        var result = FqnParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedType, result.TypeName);
    }

    // ── Type.Member patterns ─────────────────────────────────────────────

    [Theory]
    [InlineData("Type.GetTypeFromHandle", "Type", "GetTypeFromHandle")]
    [InlineData("String.Concat", "String", "Concat")]
    [InlineData("Math.Max", "Math", "Max")]
    [InlineData("JsonSerializer.Deserialize", "JsonSerializer", "Deserialize")]
    public void TypeDotMember_SplitsCorrectly(string input, string expectedType, string expectedMember)
    {
        var result = FqnParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedType, result.TypeName);
        Assert.Equal(expectedMember, result.MemberName);
        Assert.Null(result.OverloadIndex);
    }

    [Theory]
    [InlineData("List<T>.IndexOf", "List`1", "IndexOf")]
    [InlineData("Dictionary<TKey,TValue>.TryGetValue", "Dictionary`2", "TryGetValue")]
    [InlineData("Span<T>.Slice", "Span`1", "Slice")]
    [InlineData("ReadOnlySpan<T>.IndexOf", "ReadOnlySpan`1", "IndexOf")]
    [InlineData("List`1.IndexOf", "List`1", "IndexOf")]
    [InlineData("Dictionary`2.TryGetValue", "Dictionary`2", "TryGetValue")]
    public void GenericTypeDotMember_SplitsCorrectly(string input, string expectedType, string expectedMember)
    {
        var result = FqnParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedType, result.TypeName);
        Assert.Equal(expectedMember, result.MemberName);
    }

    // ── Overload index (:N) ──────────────────────────────────────────────

    [Theory]
    [InlineData("IndexOf:1", "IndexOf", 1)]
    [InlineData("IndexOf:3", "IndexOf", 3)]
    [InlineData("Deserialize:10", "Deserialize", 10)]
    public void MemberWithOverloadIndex_ParsesCorrectly(string input, string expectedMember, int expectedIndex)
    {
        var result = FqnParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedMember, result.TypeName);  // No dot, so it's treated as type
        Assert.Null(result.MemberName);
        Assert.Equal(expectedIndex, result.OverloadIndex);
    }

    [Theory]
    [InlineData("Type.GetTypeFromHandle:1", "Type", "GetTypeFromHandle", 1)]
    [InlineData("String.Concat:10", "String", "Concat", 10)]
    [InlineData("List<T>.IndexOf:3", "List`1", "IndexOf", 3)]
    [InlineData("Dictionary<TKey,TValue>.TryGetValue:1", "Dictionary`2", "TryGetValue", 1)]
    [InlineData("Span<T>.Slice:2", "Span`1", "Slice", 2)]
    public void TypeDotMemberWithOverloadIndex_ParsesCorrectly(
        string input, string expectedType, string expectedMember, int expectedIndex)
    {
        var result = FqnParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedType, result.TypeName);
        Assert.Equal(expectedMember, result.MemberName);
        Assert.Equal(expectedIndex, result.OverloadIndex);
    }

    //  ── Namespace-qualified patterns ─────────────────────────────────────
    // Note: FqnParser does NOT split namespace from type for standalone type references
    // (that's handled by type resolution logic elsewhere). These tests verify that
    // unambiguous qualified type names are preserved intact.

    [Theory]
    [InlineData("System.Collections.Generic.List`1", "System.Collections.Generic.List`1")]
    public void QualifiedTypeName_PreservedIntact(string input, string expectedType)
    {
        var result = FqnParser.Parse(input);

        Assert.NotNull(result);
        Assert.Null(result.QualifiedPrefix);
        Assert.Equal(expectedType, result.TypeName);
        Assert.Null(result.MemberName);
    }

    // A bare dotted PascalCase name (e.g. "System.String") is structurally indistinguishable
    // from a Type.Member reference (e.g. "System.String.Concat"): Namespace.Type and Type.Member
    // have identical shape. A purely structural parser therefore CANNOT tell them apart — it
    // optimistically treats the trailing segment as a member. Real disambiguation requires
    // metadata (does the prefix resolve to a type or a namespace?) and is performed downstream
    // by SourceResolver, not by FqnParser. These tests document that known limitation.
    [Fact]
    public void AmbiguousDottedName_TwoSegments_TreatedAsTypeDotMember()
    {
        var result = FqnParser.Parse("System.String");

        Assert.NotNull(result);
        Assert.Null(result.QualifiedPrefix);
        Assert.Equal("System", result.TypeName);
        Assert.Equal("String", result.MemberName);
    }

    [Fact]
    public void AmbiguousDottedName_MultiSegment_TreatedAsTypeDotMember()
    {
        var result = FqnParser.Parse("System.Text.Json.JsonSerializer");

        Assert.NotNull(result);
        Assert.Equal("System.Text", result.QualifiedPrefix);
        Assert.Equal("Json", result.TypeName);
        Assert.Equal("JsonSerializer", result.MemberName);
    }

    [Theory]
    [InlineData("System.String.Concat", "System", "String", "Concat")]
    [InlineData("System.Type.GetTypeFromHandle", "System", "Type", "GetTypeFromHandle")]
    [InlineData("System.Text.Json.JsonSerializer.Deserialize", "System.Text.Json", "JsonSerializer", "Deserialize")]
    public void QualifiedTypeDotMember_SplitsAllParts(
        string input, string expectedPrefix, string expectedType, string expectedMember)
    {
        var result = FqnParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedPrefix, result.QualifiedPrefix);
        Assert.Equal(expectedType, result.TypeName);
        Assert.Equal(expectedMember, result.MemberName);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<T>.IndexOf", "System.Collections.Generic", "List`1", "IndexOf")]
    [InlineData("System.Span<T>.Slice", "System", "Span`1", "Slice")]
    [InlineData("System.Text.Json.JsonSerializer.Deserialize:5", "System.Text.Json", "JsonSerializer", "Deserialize", 5)]
    public void QualifiedGenericTypeDotMember_SplitsAllParts(
        string input, string expectedPrefix, string expectedType, string expectedMember, int? expectedIndex = null)
    {
        var result = FqnParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedPrefix, result.QualifiedPrefix);
        Assert.Equal(expectedType, result.TypeName);
        Assert.Equal(expectedMember, result.MemberName);
        if (expectedIndex.HasValue)
            Assert.Equal(expectedIndex.Value, result.OverloadIndex);
    }

    // ── Edge cases ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrEmptyInput_ReturnsNull(string? input)
    {
        var result = FqnParser.Parse(input!);
        Assert.Null(result);
    }

    [Fact]
    public void TypeNameWithMultipleDots_PreservedIntact()
    {
        var result = FqnParser.Parse("System.Collections.Generic.List`1");

        Assert.NotNull(result);
        Assert.Null(result.QualifiedPrefix);
        Assert.Equal("System.Collections.Generic.List`1", result.TypeName);
    }

    // ── ParseMemberFilter tests ──────────────────────────────────────────

    [Theory]
    [InlineData("IndexOf", "IndexOf", null)]
    [InlineData("IndexOf:3", "IndexOf", 3)]
    [InlineData("Deserialize:10", "Deserialize", 10)]
    [InlineData("TryGetValue:1", "TryGetValue", 1)]
    public void ParseMemberFilter_ExtractsNameAndIndex(string input, string expectedName, int? expectedIndex)
    {
        var (name, index) = FqnParser.ParseMemberFilter(input);

        Assert.Equal(expectedName, name);
        Assert.Equal(expectedIndex, index);
    }
}
