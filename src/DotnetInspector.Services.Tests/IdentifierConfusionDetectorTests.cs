namespace DotnetInspector.Services.Tests;

public class IdentifierConfusionDetectorTests
{
    [Theory]
    [InlineData("System.Text.Json")]
    [InlineData("Microsoft.Extensions.Logging")]
    [InlineData("Azure.Core")]
    [InlineData("Contoso.Utilities")]
    [InlineData("System\\Text")]
    public void AsciiIdentifiersHaveNoConcern(string identifier)
        => Assert.Null(IdentifierConfusionDetector.Inspect(identifier));

    [Theory]
    [InlineData("Ѕystem.Text.Json", "System", 0x0405, 's')]
    [InlineData("Micrοsoft.Extensions", "Microsoft", 0x03BF, 'o')]
    [InlineData("Αzure.Core", "Azure", 0x0391, 'a')]
    public void HighSimilarityReservedPrefixesReportConfirmedHomoglyphs(
        string identifier,
        string reservedPrefix,
        int codePoint,
        char looksLike)
    {
        IdentifierConfusion result = Assert.IsType<IdentifierConfusion>(
            IdentifierConfusionDetector.Inspect(identifier));

        Assert.Equal(
            IdentifierConcern.NonAscii | IdentifierConcern.ReservedPrefixHomoglyph,
            result.Concerns);
        Assert.Equal([codePoint], result.NonAsciiCodePoints);
        ReservedPrefixHomoglyphMatch match = Assert.IsType<ReservedPrefixHomoglyphMatch>(
            result.ReservedPrefixMatch);
        Assert.Equal(reservedPrefix, match.ReservedPrefix);
        Assert.True(match.Similarity > 0);
        Assert.Equal(new IdentifierHomoglyph(codePoint, looksLike), Assert.Single(match.Homoglyphs));
    }

    [Theory]
    [InlineData("Ѕуѕtеm.Tools", "System", 4)]
    [InlineData("Miсrοsoft.Tools", "Microsoft", 2)]
    [InlineData("ΑΖurе.Tools", "Azure", 3)]
    public void MultipleConfirmedHomoglyphsRemainReservedPrefixMatches(
        string identifier,
        string reservedPrefix,
        int homoglyphCount)
    {
        IdentifierConfusion result = Assert.IsType<IdentifierConfusion>(
            IdentifierConfusionDetector.Inspect(identifier));

        ReservedPrefixHomoglyphMatch match =
            Assert.IsType<ReservedPrefixHomoglyphMatch>(
                result.ReservedPrefixMatch);
        Assert.Equal(reservedPrefix, match.ReservedPrefix);
        Assert.Equal(
            1d - ((double)homoglyphCount / reservedPrefix.Length),
            match.Similarity,
            precision: 3);
        Assert.Equal(homoglyphCount, match.Homoglyphs.Count);
    }

    [Theory]
    [InlineData("Systèm.Tools")]
    [InlineData("Σystem.Tools")]
    [InlineData("Δelta.Tools")]
    [InlineData("Ѕyxtem.Tools")]
    public void OtherNonAsciiIdentifiersDoNotClaimAReservedPrefixHomoglyph(string identifier)
    {
        IdentifierConfusion result = Assert.IsType<IdentifierConfusion>(
            IdentifierConfusionDetector.Inspect(identifier));

        Assert.Equal(IdentifierConcern.NonAscii, result.Concerns);
        Assert.NotEmpty(result.NonAsciiCodePoints);
        Assert.Null(result.ReservedPrefixMatch);
    }

    [Fact]
    public void NonAsciiEvidencePreservesFirstSeenDistinctOrder()
    {
        IdentifierConfusion result = Assert.IsType<IdentifierConfusion>(
            IdentifierConfusionDetector.Inspect("Δelta.ΣΔЖΣ"));

        Assert.Equal([0x0394, 0x03A3, 0x0416], result.NonAsciiCodePoints);
    }
}
