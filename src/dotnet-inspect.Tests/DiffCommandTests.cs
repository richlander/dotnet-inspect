using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for DiffCommand output formatting and comparison logic.
/// </summary>
public class DiffCommandTests
{
    [Fact]
    public void GetSimpleName_WithNamespace_ReturnsSimpleName()
    {
        var result = TypeMatcher.GetSimpleName("System.Text.Json.JsonSerializer");
        Assert.Equal("JsonSerializer", result);
    }

    [Fact]
    public void GetSimpleName_WithoutNamespace_ReturnsSameName()
    {
        var result = TypeMatcher.GetSimpleName("JsonSerializer");
        Assert.Equal("JsonSerializer", result);
    }

    [Fact]
    public void GetSimpleName_WithGenericType_ReturnsSimpleName()
    {
        var result = TypeMatcher.GetSimpleName("System.Collections.Generic.List`1");
        Assert.Equal("List`1", result);
    }

    [Fact]
    public void GetBaseName_WithNestedGenericType_PreservesNestedSuffix()
    {
        var result = TypeMatcher.GetBaseName("System.Collections.Generic.SortedDictionary`2.KeyCollection");
        Assert.Equal("System.Collections.Generic.SortedDictionary.KeyCollection", result);
    }

    [Fact]
    public void Matches_NestedGenericType_DoesNotMatchDeclaringType()
    {
        Assert.False(TypeMatcher.Matches(
            "System.Collections.Generic.SortedDictionary`2",
            "System.Collections.Generic.SortedDictionary<TKey,TValue>.KeyCollection"));
    }

    [Fact]
    public void Matches_NestedGenericType_MatchesNestedType()
    {
        Assert.True(TypeMatcher.Matches(
            "System.Collections.Generic.SortedDictionary`2.KeyCollection",
            "System.Collections.Generic.SortedDictionary<TKey,TValue>.KeyCollection"));
    }

    [Fact]
    public void ApiType_FullName_WithNamespace_ReturnsFullName()
    {
        var type = new ApiType { Name = "JsonSerializer", Namespace = "System.Text.Json" };
        Assert.Equal("System.Text.Json.JsonSerializer", type.FullName);
    }

    [Fact]
    public void ApiType_FullName_WithoutNamespace_ReturnsName()
    {
        var type = new ApiType { Name = "MyType", Namespace = null };
        Assert.Equal("MyType", type.FullName);
    }

    [Fact]
    public void ApiType_FullName_WithEmptyNamespace_ReturnsName()
    {
        var type = new ApiType { Name = "MyType", Namespace = "" };
        Assert.Equal("MyType", type.FullName);
    }
}
