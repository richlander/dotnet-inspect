using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class TypeMatcherTests
{
    [Theory]
    [InlineData("System.Diagnostics.Metrics.UpDownCounter`1", "System.Diagnostics.Metrics.UpDownCounter<T>")]
    [InlineData("System.Collections.Generic.Dictionary`2", "System.Collections.Generic.Dictionary<T1, T2>")]
    [InlineData("Outer`1+Inner`2", "Outer`1+Inner`2")]
    [InlineData("System.String", "System.String")]
    public void FormatDisplayName_rewrites_clr_generic_arity(string input, string expected)
        => Assert.Equal(expected, TypeResolver.FormatDisplayName(input));

    [Fact]
    public void FormatDisplayName_UsesExactSegmentsForNestedGenericNames()
        => Assert.Equal(
            "Outer<T>.Inner<T1, T2>",
            TypeResolver.FormatDisplayName(["Outer`1", "Inner`2"]));

    [Fact]
    public void FormatDisplayName_DoesNotParseDecorationInsideExactSegments()
        => Assert.Equal(
            "Widget`1[]",
            TypeResolver.FormatDisplayName(["Widget`1[]"]));

    /// <summary>
    /// #4217: search keys are built from the canonical arity grammar, so a
    /// backtick that is name text is not deleted. The old prefix-digit grammar
    /// turned <c>Ns.Widget`1Extra</c> into <c>Ns.WidgetExtra</c> and matched an
    /// unrelated type.
    /// </summary>
    [Theory]
    [InlineData("System.Collections.Generic.List`1", "System.Collections.Generic.List")]
    [InlineData("System.Collections.Generic.SortedDictionary`2.KeyCollection", "System.Collections.Generic.SortedDictionary.KeyCollection")]
    [InlineData("Ns.Widget`1Extra", "Ns.Widget`1Extra")]
    [InlineData("Ns.Widget`Literal", "Ns.Widget`Literal")]
    [InlineData("Ns.Widget`0", "Ns.Widget`0")]
    [InlineData("Ns.Widget`65537", "Ns.Widget`65537")]
    public void GetBaseName_removes_only_canonical_arity(string typeName, string expected)
        => Assert.Equal(expected, TypeMatcher.GetBaseName(typeName));

    [Fact]
    public void Matches_does_not_fold_a_non_arity_backtick_onto_another_type()
    {
        Assert.False(TypeMatcher.Matches("Ns.Widget`1Extra", "Ns.WidgetExtra"));
        Assert.False(TypeMatcher.Matches("Ns.WidgetExtra", "Ns.Widget`1Extra"));
        Assert.True(TypeMatcher.Matches("Ns.Widget`1Extra", "Ns.Widget`1Extra"));

        // The ordinary generic case still matches its unsuffixed spelling.
        Assert.True(TypeMatcher.Matches("Ns.Widget`1", "Ns.Widget"));
    }

    [Theory]
    [InlineData("List`1", 1)]
    [InlineData("Dictionary`2", 2)]
    [InlineData("Dictionary`2.KeyCollection", 2)]
    [InlineData("Outer`1+Inner`2", 2)]
    [InlineData("String", 0)]
    [InlineData("Widget`1Extra", 0)]
    [InlineData("Widget`+1", 0)]
    [InlineData("Widget`0", 0)]
    [InlineData("Widget`65536", 65536)]
    [InlineData("Widget`65537", 0)]
    public void GetGenericArity_reads_only_canonical_suffixes(string typeName, int expected)
        => Assert.Equal(expected, TypeMatcher.GetGenericArity(typeName));

    [Theory]
    [InlineData("Option<T>", 1)]
    [InlineData("Option<>", 1)]
    [InlineData("Option<  >", 1)]
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

    [Fact]
    public void GetPatternArity_returns_negative_one_for_incomplete_type_args()
        => Assert.Equal(-1, TypeMatcher.GetPatternArity("Option<T,>"));

    [Theory]
    [InlineData("Option<>")]
    [InlineData("Option`0")]
    [InlineData("Option`")]
    [InlineData("Option`999999999999999999999")]
    public void HasExplicitGenericNotation_includes_unbound_and_malformed_arity(
        string pattern) =>
        Assert.True(TypeMatcher.HasExplicitGenericNotation(pattern));

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
    public void Lookup_nullable_generic_argument_is_not_a_glob()
    {
        var result = TypeMatcher.Lookup(
            ["System.Collections.Generic.Dictionary`2"],
            "System.Collections.Generic.Dictionary<List<T>?,string>");

        Assert.Equal(
            "System.Collections.Generic.Dictionary`2",
            result.Match);
    }

    [Theory]
    [InlineData("System.Action", false)]
    [InlineData("System.Action`1", true)]
    [InlineData("System.Action`2", false)]
    public void MatchesTypeFilter_explicit_generic_arity_is_exact(
        string candidate,
        bool expected) =>
        Assert.Equal(
            expected,
            TypeMatcher.MatchesTypeFilter(
                candidate,
                "System.Action<T>"));

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
    public void Lookup_rejects_when_no_explicit_arity_matches()
    {
        var candidates = new[]
        {
            "MyNamespace.Option`2",  // Two type params
            "MyNamespace.Option`3"   // Three type params
        };

        var result = TypeMatcher.Lookup(candidates, "Option<T>");  // Expects arity 1

        Assert.Null(result.Match);
        Assert.Equal(candidates, result.Suggestions);
    }

    [Theory]
    [InlineData("Option<>")]
    [InlineData("Option<  >")]
    public void Lookup_accepts_unbound_arity_one_notation(string pattern)
    {
        var result = TypeMatcher.Lookup(
            ["MyNamespace.Option`1"],
            pattern);

        Assert.Equal("MyNamespace.Option`1", result.Match);
    }

    [Theory]
    [InlineData("Option`0")]
    [InlineData("Option`")]
    [InlineData("Option`999999999999999999999")]
    public void Lookup_does_not_broaden_malformed_explicit_arity(string pattern)
    {
        var result = TypeMatcher.Lookup(
            ["MyNamespace.Option`1"],
            pattern);

        Assert.Null(result.Match);
    }

    [Fact]
    public void Lookup_does_not_normalize_malformed_same_arity_pattern()
    {
        var result = TypeMatcher.Lookup(
            ["MyNamespace.Option`2"],
            "Option<T,>");

        Assert.Null(result.Match);
    }

    [Theory]
    [InlineData("List<T,U>")]
    [InlineData("List`2")]
    [InlineData("List`0")]
    public void FindUniquePublicType_does_not_broaden_explicit_arity(
        string pattern) =>
        Assert.Null(
            AssemblyReader.FindUniquePublicType(
                typeof(string).Assembly.Location,
                pattern));

    [Fact]
    public void FindUniquePublicType_accepts_unbound_arity_one_notation()
        => Assert.Equal(
            "System.Collections.Generic.List`1",
            AssemblyReader.FindUniquePublicType(
                typeof(string).Assembly.Location,
                "List<>"));

    [Theory]
    [InlineData(
        "Dictionary<TKey,TValue>.KeyCollection",
        "System.Collections.Generic.Dictionary`2.KeyCollection")]
    [InlineData(
        "Dictionary`2.KeyCollection",
        "System.Collections.Generic.Dictionary`2.KeyCollection")]
    public void FindUniquePublicType_accepts_exact_nested_generic_identity(
        string pattern,
        string expected) =>
        Assert.Equal(
            expected,
            AssemblyReader.FindUniquePublicType(
                typeof(string).Assembly.Location,
                pattern));

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

    [Fact]
    public void Lookup_prefers_exact_nested_segment_arities()
    {
        var candidates = new[]
        {
            "Example.ShiftedSiblingOuter`1.Inner`2",
            "Example.ShiftedSiblingOuter`1.Inner`3"
        };

        var result = TypeMatcher.Lookup(
            candidates,
            "Example.ShiftedSiblingOuter<T>.Inner<A,B,C>");

        Assert.Equal("Example.ShiftedSiblingOuter`1.Inner`3", result.Match);
        Assert.Empty(result.Suggestions);
    }
}
