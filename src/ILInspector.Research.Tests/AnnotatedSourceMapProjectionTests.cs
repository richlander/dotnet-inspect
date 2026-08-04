using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Research.Tests;

public class AnnotatedSourceMapProjectionTests
{
    [Fact]
    public void ProjectionIsOptInAndFactsAgreeAcrossMedia()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var absent = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt)));
        Assert.Null(absent.SourceMap);

        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            SourceMap: true));
        var map = Assert.IsType<AnnotatedSourceMap>(projection.SourceMap);

        Assert.Contains(map.Lines, line => line.Kind == SourceLineKind.CSharp);
        Assert.Contains(map.Lines, line => line.Kind == SourceLineKind.Il);
        Assert.DoesNotContain(
            map.Lines,
            line => line.Text.Contains("alloc.", StringComparison.Ordinal));

        var csharpFacts = Facts(map, SourceLineKind.CSharp);
        var ilFacts = Facts(map, SourceLineKind.Il);
        Assert.NotEmpty(csharpFacts);
        Assert.Equal(csharpFacts, ilFacts);

        var csharpBox = Assert.Single(
            map.Lines
                .Where(line => line.Kind == SourceLineKind.CSharp)
                .SelectMany(line => line.Annotations),
            annotation => annotation.Descriptor == "alloc.box");
        var boxExtent = Assert.IsType<PrintedExtent>(csharpBox.Extent);
        Assert.Equal(boxExtent.StartLine, boxExtent.EndLine);
        Assert.Equal(
            "value",
            map.Lines[boxExtent.StartLine].Text[
                boxExtent.StartColumn..boxExtent.EndColumn]);

        var expected = ResearchViews.CollectFacts(
                source,
                typeof(ResearchFixture).FullName!,
                nameof(ResearchFixture.BoxInt))
            .Select(FactKey.From)
            .Order()
            .ToArray();
        Assert.Equal(expected, ilFacts);

        var ilOffsets = map.Lines
            .Where(line => line.Kind == SourceLineKind.Il)
            .Select(line => line.Offset)
            .ToArray();
        Assert.True(ilOffsets.SequenceEqual(ilOffsets.Order()));
        Assert.Equal(ilOffsets.Length, ilOffsets.Distinct().Count());

        AssertAllExtentsAddressText(map);
    }

    [Fact]
    public void PrinterOptionsReachTheMapAndFactsDoNotChange()
    {
        var shipped = Map(nameof(AnnotatedTasteFixture.AllocateAndRead), options: null);
        var qualified = Map(
            nameof(AnnotatedTasteFixture.AllocateAndRead),
            new PrinterOptions { QualifyFieldAccess = true, QualifyPropertyAccess = true });

        string shippedText = CSharpText(shipped);
        string qualifiedText = CSharpText(qualified);
        Assert.DoesNotContain("this._count", shippedText);
        Assert.Contains("this._count", qualifiedText);
        Assert.Contains("this.Extra", qualifiedText);
        Assert.NotEqual(shippedText, qualifiedText);

        var shippedFacts = AllFacts(shipped);
        Assert.NotEmpty(shippedFacts);
        Assert.Equal(shippedFacts, AllFacts(qualified));
    }

    [Fact]
    public void ByteDivergentMapDoesNotMutateSiblingProjections()
    {
        var marker = new Annotation(
            new AnnotationDescriptor("cost.map-test", AnnotationCategory.Cost, "map test"),
            SourceOffset: 0,
            Detail: "kept");
        var registry = new ResearchFactRegistry(new MarkerProducer(marker));

        ResearchViews.MemberProjectionResult Project(bool sourceMap)
        {
            using var source = MetadataSource.Open(typeof(AnnotatedTasteFixture).Assembly.Location);
            return ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
                source,
                typeof(AnnotatedTasteFixture).FullName!,
                nameof(AnnotatedTasteFixture.GuardBothVariable),
                AnnotatedSource: true,
                CostOverlay: true,
                SemanticsOverlay: true,
                FactRows: true,
                Registry: registry,
                PrinterOptions: sourceMap
                    ? new PrinterOptions { PreferConditionalExpressionReturn = true }
                    : null,
                SourceMap: sourceMap));
        }

        var overlaysOnly = Project(sourceMap: false);
        var withMap = Project(sourceMap: true);
        var map = Assert.IsType<AnnotatedSourceMap>(withMap.SourceMap);

        Assert.Contains("return a ? b : c;", CSharpText(map));
        Assert.DoesNotContain(map.Lines, line => line.Kind == SourceLineKind.Il);
        Assert.Contains(AllFacts(map), fact => fact.Descriptor == "cost.map-test");
        Assert.Equal(overlaysOnly.CostOverlay?.Body.Output, withMap.CostOverlay?.Body.Output);
        Assert.Equal(overlaysOnly.SemanticsOverlay?.Output, withMap.SemanticsOverlay?.Output);
        Assert.Equal(overlaysOnly.Facts, withMap.Facts);
        Assert.DoesNotContain("a ? b : c", Assert.IsType<string>(withMap.CostOverlay?.Body.Output));
        Assert.DoesNotContain("a ? b : c", Assert.IsType<string>(withMap.SemanticsOverlay?.Output));
    }

    [Fact]
    public void AskingForTheMapLeavesEveryOtherProjectionIdentical()
    {
        ResearchViews.MemberProjectionResult Project(bool sourceMap)
        {
            using var source = MetadataSource.Open(typeof(AnnotatedTasteFixture).Assembly.Location);
            return ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
                source,
                typeof(AnnotatedTasteFixture).FullName!,
                nameof(AnnotatedTasteFixture.AllocateAndRead),
                AnnotatedSource: true,
                CostOverlay: true,
                SemanticsOverlay: true,
                FactRows: true,
                PrinterOptions: new PrinterOptions
                {
                    QualifyFieldAccess = true,
                    QualifyPropertyAccess = true,
                },
                SourceMap: sourceMap));
        }

        var withoutMap = Project(sourceMap: false);
        var withMap = Project(sourceMap: true);

        Assert.Contains("this._count", CSharpText(Assert.IsType<AnnotatedSourceMap>(withMap.SourceMap)));
        Assert.Equal(withoutMap.AnnotatedSource?.Output, withMap.AnnotatedSource?.Output);
        Assert.Equal(withoutMap.CostOverlay?.Body.Output, withMap.CostOverlay?.Body.Output);
        Assert.Equal(withoutMap.SemanticsOverlay?.Output, withMap.SemanticsOverlay?.Output);
        Assert.Equal(withoutMap.Facts, withMap.Facts);
    }

    [Fact]
    public void LoopFixturePinsNodeKindsAndRegionRoles()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.AllocInLoopCallee),
            SourceMap: true));
        var map = Assert.IsType<AnnotatedSourceMap>(projection.SourceMap);

        Assert.Equal(
            [
                "Call",
                "Comparison",
                "Constant",
                "ForLoop",
                "LoadArgument",
                "LoadLocal",
                "NewObject",
                "Return",
                "StoreLocal",
            ],
            map.Nodes.Select(node => node.Kind).Distinct().Order());
        Assert.Equal(
            [PrintedRegionRole.Construct, PrintedRegionRole.Header, PrintedRegionRole.Body],
            map.Regions.Select(region => region.Role).Distinct().Order());

        var construct = Assert.Single(map.Regions, region => region.Role == PrintedRegionRole.Construct);
        foreach (var region in map.Regions.Where(region => region.Role != PrintedRegionRole.Construct))
            Assert.True(Contains(construct.Extent, region.Extent));
    }

    [Fact]
    public void FactsWithoutCSharpPlacementRemainOnTheirExactIlLines()
    {
        using var source = MetadataSource.Open(typeof(AnnotatedTasteFixture).Assembly.Location);
        var ilLines = IlProjection.RenderIlBodyLines(
            source,
            typeof(AnnotatedTasteFixture).FullName!,
            nameof(AnnotatedTasteFixture.GuardBothVariable),
            overloadIndex: 0,
            publicOnly: false);
        var markers = ilLines
            .Select(line => (IAnnotation)new Annotation(
                new AnnotationDescriptor($"test.offset.{line.Offset}", AnnotationCategory.Cost, "offset marker"),
                line.Offset))
            .ToArray();
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(AnnotatedTasteFixture).FullName!,
            nameof(AnnotatedTasteFixture.GuardBothVariable),
            Registry: new ResearchFactRegistry(new MarkersProducer(markers)),
            SourceMap: true));
        var map = Assert.IsType<AnnotatedSourceMap>(projection.SourceMap);

        var csharp = Facts(map, SourceLineKind.CSharp);
        var il = Facts(map, SourceLineKind.Il);
        Assert.Equal(markers.Length, il.Length);
        Assert.True(csharp.Length < il.Length);
        Assert.Empty(map.UnplacedAnnotations);

        foreach (var line in map.Lines.Where(line => line.Kind == SourceLineKind.Il))
            Assert.All(line.Annotations, annotation => Assert.Equal(line.Offset, annotation.SourceOffset));
        Assert.All(
            il.Except(csharp),
            missing => Assert.Contains(
                map.Lines,
                line => line.Kind == SourceLineKind.Il
                    && line.Offset == missing.SourceOffset
                    && line.Annotations.Any(annotation => FactKey.From(annotation) == missing)));
    }

    [Fact]
    public void FactWithNoEmittedPlacementRemainsExplicitlyUnplaced()
    {
        var marker = new Annotation(
            new AnnotationDescriptor("test.unplaced", AnnotationCategory.Cost, "unplaced marker"),
            SourceOffset: -1);
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            Registry: new ResearchFactRegistry(new MarkerProducer(marker)),
            SourceMap: true));
        var map = Assert.IsType<AnnotatedSourceMap>(projection.SourceMap);

        var unplaced = Assert.Single(map.UnplacedAnnotations);
        Assert.Equal("test.unplaced", unplaced.Descriptor);
        Assert.Null(unplaced.Extent);
        Assert.DoesNotContain(
            map.Lines.SelectMany(line => line.Annotations),
            annotation => annotation.Descriptor == "test.unplaced");
    }

    static AnnotatedSourceMap Map(string method, PrinterOptions? options)
    {
        using var source = MetadataSource.Open(typeof(AnnotatedTasteFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(AnnotatedTasteFixture).FullName!,
            method,
            PrinterOptions: options,
            SourceMap: true));
        return Assert.IsType<AnnotatedSourceMap>(projection.SourceMap);
    }

    static string CSharpText(AnnotatedSourceMap map) => string.Join(
        "\n",
        map.Lines
            .Where(line => line.Kind == SourceLineKind.CSharp)
            .Select(line => line.Text));

    static FactKey[] Facts(AnnotatedSourceMap map, SourceLineKind kind) =>
        [.. map.Lines
            .Where(line => line.Kind == kind)
            .SelectMany(line => line.Annotations)
            .Select(FactKey.From)
            .Order()];

    static FactKey[] AllFacts(AnnotatedSourceMap map)
    {
        var medium = map.Lines.Any(line => line.Kind == SourceLineKind.Il)
            ? SourceLineKind.Il
            : SourceLineKind.CSharp;
        return [.. map.Lines
            .Where(line => line.Kind == medium)
            .SelectMany(line => line.Annotations)
            .Concat(map.UnplacedAnnotations)
            .Select(FactKey.From)
            .Order()];
    }

    static void AssertAllExtentsAddressText(AnnotatedSourceMap map)
    {
        foreach (var extent in map.Nodes.Select(node => node.Extent)
            .Concat(map.Regions.Select(region => region.Extent))
            .Concat(map.Lines.SelectMany(line => line.Annotations)
                .Select(annotation => Assert.IsType<PrintedExtent>(annotation.Extent))))
        {
            Assert.InRange(extent.StartLine, 0, map.Lines.Count - 1);
            Assert.InRange(extent.EndLine, 0, map.Lines.Count - 1);
            Assert.InRange(extent.StartColumn, 0, map.Lines[extent.StartLine].Text.Length);
            Assert.InRange(extent.EndColumn, 0, map.Lines[extent.EndLine].Text.Length);
            Assert.True(
                extent.StartLine < extent.EndLine
                || extent.StartColumn < extent.EndColumn);
        }
    }

    static bool Contains(PrintedExtent outer, PrintedExtent inner)
        => Compare(outer.StartLine, outer.StartColumn, inner.StartLine, inner.StartColumn) <= 0
           && Compare(inner.EndLine, inner.EndColumn, outer.EndLine, outer.EndColumn) <= 0;

    static int Compare(int line, int column, int otherLine, int otherColumn)
    {
        int c = line.CompareTo(otherLine);
        return c != 0 ? c : column.CompareTo(otherColumn);
    }

    readonly record struct FactKey(
        string Descriptor,
        string Category,
        AnnotationConditionality Conditionality,
        string? Detail,
        int SourceOffset) : IComparable<FactKey>
    {
        internal static FactKey From(PrintedAnnotationSpan annotation) => new(
            annotation.Descriptor,
            annotation.Category,
            annotation.Conditionality,
            annotation.Detail,
            annotation.SourceOffset);

        internal static FactKey From(IAnnotation annotation) => new(
            annotation.Descriptor.Id,
            annotation.Descriptor.Category.ToString(),
            annotation.Conditionality,
            annotation.Detail,
            annotation.SourceOffset);

        public int CompareTo(FactKey other)
        {
            int c = SourceOffset.CompareTo(other.SourceOffset);
            if (c != 0) return c;
            c = string.CompareOrdinal(Descriptor, other.Descriptor);
            if (c != 0) return c;
            c = string.CompareOrdinal(Category, other.Category);
            if (c != 0) return c;
            c = Conditionality.CompareTo(other.Conditionality);
            if (c != 0) return c;
            return string.CompareOrdinal(Detail, other.Detail);
        }
    }

    sealed class MarkerProducer(IAnnotation marker) : IResearchFactProducer
    {
        public string Name => "map-marker";
        public IReadOnlyList<string> Produces => [marker.Descriptor.Id];
        public IReadOnlyList<string> DependsOn => [];
        public IReadOnlyList<IAnnotation> Produce(ResearchFactContext context) => [marker];
    }

    sealed class MarkersProducer(IReadOnlyList<IAnnotation> markers) : IResearchFactProducer
    {
        public string Name => "map-markers";
        public IReadOnlyList<string> Produces => [.. markers.Select(marker => marker.Descriptor.Id)];
        public IReadOnlyList<string> DependsOn => [];
        public IReadOnlyList<IAnnotation> Produce(ResearchFactContext context) => markers;
    }
}
