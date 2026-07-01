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

    [Fact]
    public void FormatGenericTypeName_LeavesMalformedArityUnchanged()
        => Assert.Equal("Bad`x", MetadataTypeNameFormatter.FormatGenericTypeName("Bad`x"));

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
