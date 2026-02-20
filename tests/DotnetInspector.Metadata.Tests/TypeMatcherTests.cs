using DotnetInspector.Metadata;

namespace DotnetInspector.Metadata.Tests;

public class TypeMatcherTests
{
    [Theory]
    [InlineData("Option<T>", 1)]
    [InlineData("Dictionary<K,V>", 2)]
    [InlineData("Action<T1,T2,T3>", 3)]
    [InlineData("Func<T1,T2,T3,TResult>", 4)]
    public void GetPatternArity_returns_correct_arity_for_generic_patterns(string pattern, int expected)
        => Assert.Equal(expected, TypeMatcher.GetPatternArity(pattern));

    [Theory]
    [InlineData("Option")]
    [InlineData("String")]
    [InlineData("List")]
    public void GetPatternArity_returns_negative_one_for_non_generic_patterns(string pattern)
        => Assert.Equal(-1, TypeMatcher.GetPatternArity(pattern));

    [Theory]
    [InlineData("Option`1", 1)]
    [InlineData("Dictionary`2", 2)]
    [InlineData("Action`3", 3)]
    [InlineData("Func`4", 4)]
    public void GetPatternArity_returns_correct_arity_for_clr_backtick_notation(string pattern, int expected)
        => Assert.Equal(expected, TypeMatcher.GetPatternArity(pattern));

    [Theory]
    [InlineData("Option<>")]
    [InlineData("Option<  >")]
    public void GetPatternArity_returns_negative_one_for_empty_type_args(string pattern)
        => Assert.Equal(-1, TypeMatcher.GetPatternArity(pattern));

    [Theory]
    [InlineData("Func<Tuple<int,int>,string>", 2)]
    [InlineData("Dictionary<List<T>,Action<T1,T2>>", 2)]
    public void GetPatternArity_handles_nested_generics(string pattern, int expected)
        => Assert.Equal(expected, TypeMatcher.GetPatternArity(pattern));

    [Fact]
    public void Lookup_prefers_generic_type_when_pattern_has_generic_notation()
    {
        var candidates = new[]
        {
            "System.CommandLine.Option",      // Non-generic base class
            "System.CommandLine.Option`1"     // Generic Option<T>
        };

        var result = TypeMatcher.Lookup(candidates, "Option<T>");

        Assert.NotNull(result.Match);
        Assert.Equal("System.CommandLine.Option`1", result.Match);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void Lookup_prefers_non_generic_type_when_pattern_has_no_generic_notation()
    {
        var candidates = new[]
        {
            "System.CommandLine.Option",      // Non-generic base class
            "System.CommandLine.Option`1"     // Generic Option<T>
        };

        var result = TypeMatcher.Lookup(candidates, "Option");

        Assert.NotNull(result.Match);
        Assert.Equal("System.CommandLine.Option", result.Match);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void Lookup_returns_generic_with_matching_arity_for_multi_param_pattern()
    {
        var candidates = new[]
        {
            "System.Collections.Generic.Dictionary`2",
            "System.Collections.Generic.KeyValuePair`2"
        };

        var result = TypeMatcher.Lookup(candidates, "Dictionary<K,V>");

        Assert.NotNull(result.Match);
        Assert.Equal("System.Collections.Generic.Dictionary`2", result.Match);
    }

    [Fact]
    public void Lookup_falls_back_to_first_match_when_no_arity_matches()
    {
        var candidates = new[]
        {
            "MyNamespace.Option`2",  // Two type params
            "MyNamespace.Option`3"   // Three type params
        };

        var result = TypeMatcher.Lookup(candidates, "Option<T>");  // Expects arity 1

        Assert.NotNull(result.Match);
        Assert.Equal("MyNamespace.Option`2", result.Match);
    }

    [Fact]
    public void Lookup_handles_namespace_qualified_pattern()
    {
        var candidates = new[]
        {
            "System.CommandLine.Argument",
            "System.CommandLine.Argument`1"
        };

        var result = TypeMatcher.Lookup(candidates, "System.CommandLine.Argument<T>");

        Assert.NotNull(result.Match);
        Assert.Equal("System.CommandLine.Argument`1", result.Match);
    }

    [Fact]
    public void Lookup_prefers_generic_type_when_pattern_uses_clr_backtick_notation()
    {
        var candidates = new[]
        {
            "System.CommandLine.Option",      // Non-generic base class
            "System.CommandLine.Option`1"     // Generic Option<T>
        };

        // This is the pattern after GenericTypeNameConverter.Convert("Option<T>")
        var result = TypeMatcher.Lookup(candidates, "Option`1");

        Assert.NotNull(result.Match);
        Assert.Equal("System.CommandLine.Option`1", result.Match);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void Lookup_with_clr_notation_matches_correct_arity()
    {
        var candidates = new[]
        {
            "System.Action",
            "System.Action`1",
            "System.Action`2",
            "System.Action`3"
        };

        var result = TypeMatcher.Lookup(candidates, "Action`2");

        Assert.NotNull(result.Match);
        Assert.Equal("System.Action`2", result.Match);
    }
}
