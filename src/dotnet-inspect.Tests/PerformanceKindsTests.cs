using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

public class PerformanceKindsTests
{
    [Fact]
    public void Sections_MatchStructuredKeyOrdering()
    {
        Assert.Equal(
            [
                SectionNames.PerformanceBoxing,
                SectionNames.PerformanceArrays,
                SectionNames.PerformanceClosures,
                SectionNames.PerformanceEnumerators,
                SectionNames.PerformanceLoops,
                SectionNames.PerformanceHotspots,
                SectionNames.PerformanceAsync,
                SectionNames.PerformanceOther,
            ],
            PerformanceKinds.Sections);
    }

    [Fact]
    public void EveryKnownShape_MapsToNonOtherSection()
    {
        foreach (var shape in PerformanceTriageOptions.KnownShapes)
        {
            var section = PerformanceKinds.SectionForShape(shape);
            Assert.NotEqual(SectionNames.PerformanceOther, section);
            Assert.Contains(section, PerformanceKinds.Sections);
        }
    }

    [Fact]
    public void UnmappedShape_RoutesToOther_SoScanIsNeverLossy()
    {
        Assert.Equal(SectionNames.PerformanceOther, PerformanceKinds.SectionForShape("brand-new-shape"));
        Assert.Equal(SectionNames.PerformanceOther, PerformanceKinds.SectionForShape(null));
    }

    [Fact]
    public void SectionForShape_IsCaseInsensitive()
    {
        // Shape validation and row filtering are case-insensitive; the section mapping must agree so
        // a differently-cased shape does not silently route to Performance: Other.
        foreach (var shape in PerformanceTriageOptions.KnownShapes)
        {
            Assert.Equal(
                PerformanceKinds.SectionForShape(shape),
                PerformanceKinds.SectionForShape(shape.ToUpperInvariant()));
        }
    }

    [Fact]
    public void StructuredKeys_AreUniquePerSection()
    {
        var keys = PerformanceKinds.Sections
            .Select(PerformanceKinds.StructuredKey)
            .ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void StructuredKey_HasNoAmpersand_SoJsonAndMarkdownMatch()
    {
        // The closures section name intentionally drops "&" (markout HTML-escapes it); the JSON key
        // stays snake_case.
        Assert.Equal("closures_and_delegates", PerformanceKinds.StructuredKey(SectionNames.PerformanceClosures));
        Assert.DoesNotContain('&', SectionNames.PerformanceClosures);
    }

    [Fact]
    public void KindLabel_StripsPerformancePrefix_ForFlattenedTabularColumn()
    {
        Assert.Equal("Boxing", PerformanceKinds.KindLabel(SectionNames.PerformanceBoxing));
        Assert.Equal("Closures and delegates", PerformanceKinds.KindLabel(SectionNames.PerformanceClosures));
        Assert.Equal("Allocation hotspots", PerformanceKinds.KindLabel(SectionNames.PerformanceHotspots));
        // A label for every section, and none retains the "Performance: " prefix.
        foreach (var section in PerformanceKinds.Sections)
        {
            var label = PerformanceKinds.KindLabel(section);
            Assert.False(string.IsNullOrEmpty(label));
            Assert.DoesNotContain("Performance: ", label, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AllShareCommonView_TrueForPerformanceKinds_FalseOtherwise()
    {
        Assert.True(PerformanceKinds.AllShareCommonView(PerformanceKinds.Sections));
        Assert.True(PerformanceKinds.AllShareCommonView([SectionNames.PerformanceBoxing]));
        Assert.False(PerformanceKinds.AllShareCommonView([]));
        Assert.False(PerformanceKinds.AllShareCommonView(
            [SectionNames.PerformanceBoxing, "Top Leverage"]));
    }
}
