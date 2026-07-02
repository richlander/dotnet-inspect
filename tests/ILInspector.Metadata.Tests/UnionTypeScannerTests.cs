using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class UnionTypeScannerTests
{
    [Fact]
    public void Scan_finds_union_attribute_types_and_cases()
    {
        using var stream = File.OpenRead(typeof(MetadataUnionFixture).Assembly.Location);
        using var peReader = new PEReader(stream);

        var union = Assert.Single(
            UnionTypeScanner.Scan(peReader),
            t => t.TypeName == typeof(MetadataUnionFixture).FullName);

        Assert.Equal("struct", union.Kind);
        Assert.True(union.ImplementsIUnion);
        Assert.Equal(
            [typeof(MetadataUnionCat).FullName!, typeof(MetadataUnionDog).FullName!],
            union.CaseTypes);
    }

    [Fact]
    public void Scan_keeps_union_attribute_lookalike_but_marks_missing_iunion()
    {
        using var stream = File.OpenRead(typeof(MetadataUnionLookalike).Assembly.Location);
        using var peReader = new PEReader(stream);

        var union = Assert.Single(
            UnionTypeScanner.Scan(peReader),
            t => t.TypeName == typeof(MetadataUnionLookalike).FullName);

        Assert.Equal("class", union.Kind);
        Assert.False(union.ImplementsIUnion);
        Assert.Equal([typeof(MetadataUnionCat).FullName!], union.CaseTypes);
    }
}

public sealed class MetadataUnionCat;
public sealed class MetadataUnionDog;

[Union]
public readonly struct MetadataUnionFixture : IUnion
{
    public MetadataUnionFixture(MetadataUnionCat value) => Value = value;
    public MetadataUnionFixture(MetadataUnionDog value) => Value = value;
    private MetadataUnionFixture(string ignored) => Value = ignored;

    public object? Value { get; }
}

[Union]
public sealed class MetadataUnionLookalike
{
    public MetadataUnionLookalike(MetadataUnionCat value) => Value = value;

    public object? Value { get; }
}
