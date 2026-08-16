using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataTypeNameFormatterTests
{
    [Fact]
    public void FormatFullName_UsesApiTypeNamespaceAndGenericParameterNames()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Pair`2",
            TypeParameters =
            [
                new TypeParameter { Name = "TKey" },
                new TypeParameter { Name = "TValue" }
            ]
        };

        Assert.Equal("Samples.Pair<TKey, TValue>", MetadataTypeNameFormatter.FormatFullName(type));
    }

    [Fact]
    public void FormatGenericTypeName_ConsumesTypeParametersAcrossNestedSegments()
    {
        TypeParameter[] typeParameters =
        [
            new() { Name = "TOuter" },
            new() { Name = "TInnerKey" },
            new() { Name = "TInnerValue" }
        ];

        var displayName = MetadataTypeNameFormatter.FormatGenericTypeName("Outer`1.Inner`2", typeParameters);

        Assert.Equal("Outer<TOuter>.Inner<TInnerKey, TInnerValue>", displayName);
    }

    [Theory]
    [InlineData("List`1", "List<T>")]
    [InlineData("Dictionary`2", "Dictionary<T1, T2>")]
    [InlineData("Outer`1.Inner`2", "Outer<T>.Inner<T1, T2>")]
    public void FormatGenericTypeName_UsesStableFallbackNamesWhenTypeParametersAreUnavailable(
        string name,
        string expected)
        => Assert.Equal(expected, MetadataTypeNameFormatter.FormatGenericTypeName(name));

    [Theory]
    // Only a canonical `N is an arity marker (MetadataNameArity). Everything here
    // used to be read as arity by a digit-run-plus-int.TryParse grammar, or —
    // for the last two — expanded into an unbounded placeholder list.
    [InlineData("Bad`x")]
    [InlineData("Widget`1Extra")]
    [InlineData("Widget`+1")]
    [InlineData("Widget` 1")]
    [InlineData("Widget`0")]
    [InlineData("Widget`01")]
    [InlineData("Widget`\u0661")]
    [InlineData("Bomb`2147483647")]
    [InlineData("Bomb`4294967296")]
    public void FormatGenericTypeName_LeavesNonCanonicalArityUnchanged(string name)
        => Assert.Equal(name, MetadataTypeNameFormatter.FormatGenericTypeName(name));

    /// <summary>
    /// A canonical arity can reach <see cref="MetadataNameArity.MaxArity"/>, so
    /// placeholder expansion carries its own budget: at the bound the name
    /// expands, one over it the raw <c>`N</c> spelling is kept. Without the
    /// budget a 12-character name expands into hundreds of kilobytes of display
    /// text.
    /// </summary>
    [Fact]
    public void FormatGenericTypeName_BoundsPlaceholderExpansion()
    {
        string atBound = MetadataTypeNameFormatter.FormatGenericTypeName("Wide`64");
        Assert.StartsWith("Wide<T1, T2, ", atBound, StringComparison.Ordinal);
        Assert.EndsWith(", T64>", atBound, StringComparison.Ordinal);

        Assert.Equal("Wide`65", MetadataTypeNameFormatter.FormatGenericTypeName("Wide`65"));
        Assert.Equal("Wide`65536", MetadataTypeNameFormatter.FormatGenericTypeName("Wide`65536"));

        // The suffix is preserved rather than dropped, so the name keeps its
        // identity and stays distinguishable from the unsuffixed type.
        Assert.NotEqual(
            MetadataTypeNameFormatter.FormatGenericTypeName("Wide"),
            MetadataTypeNameFormatter.FormatGenericTypeName("Wide`65536"));
    }

    /// <summary>
    /// Argument substitution shares the same canonical grammar, so a digit run
    /// that is not an arity marker no longer swallows the text after it
    /// (<c>Widget`1Extra</c> used to render <c>Widget&lt;int&gt;Extra</c>).
    /// </summary>
    [Fact]
    public void FormatGenericTypeName_SubstitutesArgumentsOnlyAtCanonicalMarkers()
    {
        TypeParameter[] one = [new() { Name = "int" }];

        Assert.Equal("Widget<int>", MetadataTypeNameFormatter.FormatGenericTypeName("Widget`1", one));
        Assert.Equal("Widget`1Extra", MetadataTypeNameFormatter.FormatGenericTypeName("Widget`1Extra", one));
        Assert.Equal("Widget`Literal", MetadataTypeNameFormatter.FormatGenericTypeName("Widget`Literal", one));
    }

    /// <summary>
    /// A compiler-generated name is name text, angle brackets and all, so its
    /// arity marker still expands. Treating the leading <c>&lt;&gt;</c> as
    /// display decoration left a raw backtick in emitted C# (CS1056).
    /// </summary>
    [Fact]
    public void FormatGenericTypeName_ExpandsArityOnCompilerGeneratedNames()
    {
        TypeParameter[] one = [new() { Name = "int" }];

        Assert.Equal(
            "<>c__DisplayClass22_0<int>",
            MetadataTypeNameFormatter.FormatGenericTypeName("<>c__DisplayClass22_0`1", one));
        Assert.Equal(
            "<>c__DisplayClass22_0<T>",
            MetadataTypeNameFormatter.FormatGenericTypeName("<>c__DisplayClass22_0`1"));
        Assert.DoesNotContain(
            '`',
            MetadataTypeNameFormatter.FormatGenericTypeName("<M>d__3`2", one));
    }

    /// <summary>
    /// A namespace is passed beside the type-name chain, never inside it, so
    /// namespace text is not rewritten by name formatting.
    /// </summary>
    [Fact]
    public void FormatFullName_KeepsNamespaceTextOutOfTheArityGrammar()
    {
        var type = new ApiType
        {
            Namespace = "Ns`1",
            Name = "Widget`1",
            TypeParameters = [new TypeParameter { Name = "T" }]
        };

        Assert.Equal("Ns`1.Widget<T>", MetadataTypeNameFormatter.FormatFullName(type));
    }

    [Theory]
    [InlineData("List`1[]", "List<T>[]")]
    [InlineData("List`1*", "List<T>*")]
    [InlineData("List`1&", "List<T>&")]
    public void FormatGenericTypeName_PreservesTypeSuffixesWithoutTypeParameters(
        string name,
        string expected)
        => Assert.Equal(expected, MetadataTypeNameFormatter.FormatGenericTypeName(name));

    [Fact]
    public void FormatGenericTypeName_PreservesTypeSuffixesWithTypeParameters()
    {
        TypeParameter[] typeParameters =
        [
            new() { Name = "TOuter" },
            new() { Name = "TInnerKey" },
            new() { Name = "TInnerValue" }
        ];

        var displayName = MetadataTypeNameFormatter.FormatGenericTypeName("Outer`1.Inner`2[]", typeParameters);

        Assert.Equal("Outer<TOuter>.Inner<TInnerKey, TInnerValue>[]", displayName);
    }
}
