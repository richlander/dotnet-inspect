using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The structured type-name identity carries an artifact-authored namespace and up to
/// <see cref="MetadataSafetyPolicy.MaxRelationshipNodes"/> artifact-authored segments, each of
/// unbounded length. These tests pin the cumulative character budget that bounds the aggregate,
/// and the linear flattening that the identity owner — not each consumer — performs.
/// </summary>
public class MetadataTypeNameBudgetTests
{
    [Fact]
    public void NameAtTheCharacterBudget_IsAccepted()
    {
        // namespace + one delimiter per segment + segment text == the budget exactly.
        const string ns = "N";
        int budget = MetadataSafetyPolicy.MaxTypeNameCharacters;
        var segments = ImmutableArray.Create(new string('a', budget - ns.Length - 1));

        MetadataTypeDefinitionNameResult result =
            MetadataTypeDefinitionName.Create(ns, segments);

        var valid = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(result);
        Assert.Equal(segments[0], valid.Name.ToNestedMetadataName());
    }

    [Fact]
    public void NameOneCharacterOverTheBudget_IsRejectedWithTypedEvidence()
    {
        const string ns = "N";
        int budget = MetadataSafetyPolicy.MaxTypeNameCharacters;
        var segments = ImmutableArray.Create(new string('a', budget - ns.Length));

        MetadataTypeDefinitionNameResult result =
            MetadataTypeDefinitionName.Create(ns, segments);

        var rejected = Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(result);
        Assert.Equal(
            MetadataTypeNameRejectionKind.SegmentsTooLong,
            rejected.Rejection.Kind);
        Assert.Equal(0, rejected.Rejection.SegmentIndex);
    }

    [Fact]
    public void GlobalNamespaceNestedNameAtTheCharacterBudget_IsAccepted()
    {
        ImmutableArray<string> segments =
        [
            new string('a', 2048),
            new string('b', 2047),
        ];

        var valid = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("", segments));

        Assert.Equal(
            MetadataSafetyPolicy.MaxTypeNameCharacters,
            valid.Name.ToNestedMetadataName().Length);
    }

    [Fact]
    public void ManySegmentsWithinTheNodeBudget_AreRejectedOnAggregateSize()
    {
        // Every individual segment is ordinary and the segment count is inside the relationship
        // node budget; only the aggregate is absurd. The node budget alone would accept this.
        ImmutableArray<string> segments =
        [
            .. Enumerable
                .Range(0, MetadataSafetyPolicy.MaxRelationshipNodes)
                .Select(index => new string('s', 64)),
        ];

        MetadataTypeDefinitionNameResult result =
            MetadataTypeDefinitionName.Create("N", segments);

        var rejected = Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(result);
        Assert.Equal(
            MetadataTypeNameRejectionKind.SegmentsTooLong,
            rejected.Rejection.Kind);

        // Refused before the remaining segments were measured, and long before any flattened
        // spelling could be built.
        Assert.NotNull(rejected.Rejection.SegmentIndex);
        Assert.True(rejected.Rejection.SegmentIndex < segments.Length - 1);
    }

    [Fact]
    public void SegmentCountIsBoundedAtTheRelationshipNodeLimit()
    {
        ImmutableArray<string> atLimit =
        [
            .. Enumerable.Range(
                0,
                MetadataSafetyPolicy.MaxRelationshipNodes)
                .Select(static index => index.ToString()),
        ];
        ImmutableArray<string> overLimit = [.. atLimit, "overflow"];

        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", atLimit));
        var rejected =
            Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(
                MetadataTypeDefinitionName.Create("N", overLimit));
        Assert.Equal(
            MetadataTypeNameRejectionKind.TooManySegments,
            rejected.Rejection.Kind);
    }

    [Fact]
    public void DeepNestedName_FlattensToTheExactMetadataSpelling()
    {
        ImmutableArray<string> segments =
        [
            .. Enumerable.Range(0, 32).Select(index => $"Level{index}`1"),
        ];

        var valid = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("Deep.Space", segments));

        Assert.Equal(string.Join('+', segments), valid.Name.ToNestedMetadataName());
        Assert.Equal(
            $"Deep.Space.{string.Join('+', segments)}",
            valid.Name.ToEscapedFullName());
    }

    [Fact]
    public void FlattenedSpellingAndEscapedIdentityRemainDistinct()
    {
        var nested = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Outer", "Inner"])).Name;
        var literalPlus = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Outer+Inner"])).Name;

        // The flattened metadata spelling is ambiguous by construction; the escaped identity is
        // not, which is why identity keys use the latter.
        Assert.Equal(nested.ToNestedMetadataName(), literalPlus.ToNestedMetadataName());
        Assert.NotEqual(nested.ToEscapedFullName(), literalPlus.ToEscapedFullName());
        Assert.Equal(@"N.Outer\+Inner", literalPlus.ToEscapedFullName());
    }
}
