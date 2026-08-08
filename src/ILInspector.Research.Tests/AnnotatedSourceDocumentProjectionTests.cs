using System.Text.Json;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Research.Tests;

public class AnnotatedSourceDocumentProjectionTests
{
    [Fact]
    public void ProjectionIsOptInAndFactsAgreeAcrossMedia()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var absent = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt)));
        Assert.Null(absent.SourceDocument);

        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);

        Assert.Contains(document.Lines, line => line.Kind == SourceLineKind.CSharp);
        Assert.Contains(document.Lines, line => line.Kind == SourceLineKind.Il);
        Assert.DoesNotContain(
            document.Lines,
            line => line.Text.Contains("alloc.", StringComparison.Ordinal));

        var csharpFacts = Facts(document, SourceLineKind.CSharp);
        var ilFacts = Facts(document, SourceLineKind.Il);
        Assert.NotEmpty(csharpFacts);
        Assert.Equal(csharpFacts, ilFacts);

        // The same observation in both media is one fact with two placements,
        // not two facts a consumer has to guess are the same.
        var box = Assert.Single(document.Facts, fact => fact.Descriptor == "alloc.box");
        var boxPlacements = Placements(document, box);
        Assert.Contains(boxPlacements, placement => placement.Target == AnnotatedSourcePlacementTarget.Node);
        Assert.Contains(boxPlacements, placement => placement.Target == AnnotatedSourcePlacementTarget.Line);

        var boxNode = Node(document, boxPlacements.Single(
            placement => placement.Target == AnnotatedSourcePlacementTarget.Node));
        Assert.Equal(SourceLineKind.CSharp, boxNode.Medium);
        var boxExtent = boxNode.Extent;
        Assert.Equal(boxExtent.StartLine, boxExtent.EndLine);

        // Node extents are C#-local: resolve them against the C# lines, in
        // order, not against the interleaved stream.
        Assert.Equal(
            "value",
            MediumLines(document, SourceLineKind.CSharp)[boxExtent.StartLine][
                boxExtent.StartColumn..boxExtent.EndColumn]);

        var expected = ResearchViews.CollectFacts(
                source,
                typeof(ResearchFixture).FullName!,
                nameof(ResearchFixture.BoxInt))
            .Select(FactKey.From)
            .Distinct()
            .Order()
            .ToArray();
        Assert.Equal(expected, ilFacts);

        var ilOffsets = document.Lines
            .Where(line => line.Kind == SourceLineKind.Il)
            .Select(line => line.Offset)
            .ToArray();
        Assert.True(ilOffsets.SequenceEqual(ilOffsets.Order()));
        Assert.Equal(ilOffsets.Length, ilOffsets.Distinct().Count());

        AssertNormalized(document);
    }

    [Fact]
    public void NodesAreTextStructureIndependentOfFacts()
    {
        var document = Document(nameof(AnnotatedTasteFixture.AllocateAndRead), options: null);

        Assert.NotEmpty(document.Nodes);
        Assert.All(document.Nodes, node => Assert.Equal(SourceLineKind.CSharp, node.Medium));
        Assert.Equal(
            Enumerable.Range(0, document.Nodes.Count),
            document.Nodes.Select(node => node.Id));

        // Most nodes carry no fact at all. Nodes exist because the text has
        // structure, not because something was observed about it.
        var placedNodes = document.Placements
            .Where(placement => placement.Target == AnnotatedSourcePlacementTarget.Node)
            .Select(placement => placement.TargetId)
            .ToHashSet();
        Assert.NotEmpty(placedNodes);
        Assert.Contains(document.Nodes, node => !placedNodes.Contains(node.Id));
    }

    [Fact]
    public void PrinterOptionsReachTheDocumentAndFactsDoNotChange()
    {
        var shipped = Document(nameof(AnnotatedTasteFixture.AllocateAndRead), options: null);
        var qualified = Document(
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
    public void ByteDivergentDocumentDoesNotMutateSiblingProjections()
    {
        var marker = new Annotation(
            new AnnotationDescriptor("cost.document-test", AnnotationCategory.Cost, "document test"),
            SourceOffset: 0,
            Detail: "kept");
        var registry = new ResearchFactRegistry(new MarkerProducer(marker));

        ResearchViews.MemberProjectionResult Project(bool sourceDocument)
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
                PrinterOptions: sourceDocument
                    ? new PrinterOptions { PreferConditionalExpressionReturn = true }
                    : null,
                SourceDocument: sourceDocument));
        }

        var overlaysOnly = Project(sourceDocument: false);
        var withDocument = Project(sourceDocument: true);
        var document = Assert.IsType<AnnotatedSourceDocument>(withDocument.SourceDocument);

        Assert.Contains("return a ? b : c;", CSharpText(document));
        Assert.DoesNotContain(document.Lines, line => line.Kind == SourceLineKind.Il);
        Assert.Contains(AllFacts(document), fact => fact.Descriptor == "cost.document-test");
        Assert.Equal(overlaysOnly.CostOverlay?.Body.Output, withDocument.CostOverlay?.Body.Output);
        Assert.Equal(overlaysOnly.SemanticsOverlay?.Output, withDocument.SemanticsOverlay?.Output);
        Assert.Equal(overlaysOnly.Facts, withDocument.Facts);
        Assert.DoesNotContain("a ? b : c", Assert.IsType<string>(withDocument.CostOverlay?.Body.Output));
        Assert.DoesNotContain("a ? b : c", Assert.IsType<string>(withDocument.SemanticsOverlay?.Output));
    }

    [Fact]
    public void AskingForTheDocumentLeavesEveryOtherProjectionIdentical()
    {
        ResearchViews.MemberProjectionResult Project(bool sourceDocument)
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
                SourceDocument: sourceDocument));
        }

        var withoutDocument = Project(sourceDocument: false);
        var withDocument = Project(sourceDocument: true);

        Assert.Contains(
            "this._count",
            CSharpText(Assert.IsType<AnnotatedSourceDocument>(withDocument.SourceDocument)));
        Assert.Equal(withoutDocument.AnnotatedSource?.Output, withDocument.AnnotatedSource?.Output);
        Assert.Equal(withoutDocument.CostOverlay?.Body.Output, withDocument.CostOverlay?.Body.Output);
        Assert.Equal(withoutDocument.SemanticsOverlay?.Output, withDocument.SemanticsOverlay?.Output);
        Assert.Equal(withoutDocument.Facts, withDocument.Facts);
    }

    [Fact]
    public void LoopFixturePinsNodeKindsAndRegionRoles()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.AllocInLoopCallee),
            SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);

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
            document.Nodes.Select(node => node.Kind).Distinct().Order());
        Assert.Equal(
            [PrintedRegionRole.Construct, PrintedRegionRole.Header, PrintedRegionRole.Body],
            document.Regions.Select(region => region.Role).Distinct().Order());

        var construct = Assert.Single(document.Regions, region => region.Role == PrintedRegionRole.Construct);
        foreach (var region in document.Regions.Where(region => region.Role != PrintedRegionRole.Construct))
            Assert.True(Contains(construct.Extent, region.Extent));
    }

    [Fact]
    public void MultiLineCSharpStructureDoesNotAbsorbInterleavedIl()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.AllocInLoopCallee),
            SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);

        var csharp = MediumLines(document, SourceLineKind.CSharp);
        int[] streamIds =
        [
            .. document.Lines
                .Where(line => line.Kind == SourceLineKind.CSharp)
                .Select(line => line.Id),
        ];

        var loop = Assert.Single(document.Nodes, node => node.Kind == "ForLoop");
        var body = Assert.Single(document.Regions, region => region.Role == PrintedRegionRole.Body);
        Assert.True(loop.Extent.StartLine < loop.Extent.EndLine);
        Assert.True(body.Extent.StartLine < body.Extent.EndLine);

        // Non-vacuity: IL really is printed between this node's first and last
        // C# lines, so an extent rebased onto stream ids would have enclosed it.
        foreach (var extent in new[] { loop.Extent, body.Extent })
        {
            int first = streamIds[extent.StartLine];
            int last = streamIds[extent.EndLine];
            Assert.True(last - first > extent.EndLine - extent.StartLine);
            Assert.Contains(
                document.Lines.Skip(first).Take(last - first + 1),
                line => line.Kind == SourceLineKind.Il);
        }

        // The contract: the exact selected characters come from the C#-filtered
        // lines, and are C# only.
        string selected = SelectText(csharp, loop.Extent);
        Assert.StartsWith("for (", selected.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("new object()", selected, StringComparison.Ordinal);
        Assert.Equal(
            loop.Extent.EndLine - loop.Extent.StartLine + 1,
            selected.Split('\n').Length);
        foreach (var extent in new[] { loop.Extent, body.Extent })
        {
            Assert.All(
                SelectText(csharp, extent).Split('\n'),
                line => Assert.DoesNotContain("IL_", line, StringComparison.Ordinal));
        }

        AssertNormalized(document);
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
            SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);

        var csharp = Facts(document, SourceLineKind.CSharp);
        var il = Facts(document, SourceLineKind.Il);
        Assert.Equal(markers.Length, il.Length);
        Assert.True(csharp.Length < il.Length);
        Assert.Empty(Unplaced(document));

        foreach (var placement in document.Placements
            .Where(placement => placement.Target == AnnotatedSourcePlacementTarget.Line))
        {
            Assert.Equal(
                document.Lines[placement.TargetId!.Value].Offset,
                document.Facts[placement.FactId].SourceOffset);
        }
        Assert.All(
            il.Except(csharp),
            missing => Assert.Contains(
                document.Placements,
                placement => placement.Target == AnnotatedSourcePlacementTarget.Line
                    && FactKey.From(document.Facts[placement.FactId]) == missing
                    && document.Lines[placement.TargetId!.Value].Offset == missing.SourceOffset));
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
            SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);

        var fact = Assert.Single(document.Facts, fact => fact.Descriptor == "test.unplaced");
        Assert.Equal(AnnotatedSourceFactOrigin.Body, fact.Origin);
        var placement = Assert.Single(Placements(document, fact));
        Assert.Equal(AnnotatedSourcePlacementTarget.Unplaced, placement.Target);
        Assert.Null(placement.TargetId);
    }

    [Fact]
    public void MemberHeaderFactsCarryHeaderOriginAndRemainUnplaced()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.HighLoopLeverageCallee),
            SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);

        var fact = Assert.Single(
            document.Facts,
            candidate => candidate.Descriptor == "cost.method"
                && candidate.Origin == AnnotatedSourceFactOrigin.MemberHeader);
        Assert.Equal(-1, fact.SourceOffset);
        var placement = Assert.Single(Placements(document, fact));
        Assert.Equal(AnnotatedSourcePlacementTarget.Unplaced, placement.Target);
    }

    [Fact]
    public void MalformedFactTextRemainsReplayable()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            Registry: new ResearchFactRegistry(new MalformedTextProducer()),
            SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);

        var unplaced = Unplaced(document);
        Assert.Contains(
            unplaced,
            fact => fact.Descriptor == "test.body.\\uD800"
                && fact.Detail == "body.\U0001F600\\uDC00");
        Assert.Contains(
            document.Facts,
            fact => fact.Descriptor == "test.header.\\uDC00"
                && fact.Detail == "header.\\uD800"
                && fact.Origin == AnnotatedSourceFactOrigin.MemberHeader);
        Assert.Contains(
            unplaced,
            fact => fact.Descriptor == "test.body.\\\\uD800"
                && fact.Detail == "literal.\\\\uD800");
        Assert.Equal(
            unplaced.Length,
            unplaced.Select(fact => (fact.Descriptor, fact.Detail)).Distinct().Count());

        // Escaping happens before identity, so the one observation with
        // placements in both media stays a single fact rather than splitting on
        // the medium-specific kind it used to carry.
        var placed = Assert.Single(document.Facts, fact => fact.Descriptor == "test.placed.\\uD800");
        Assert.Equal("placed.\U0001F600\\uDC00", placed.Detail);
        Assert.Equal(
            [AnnotatedSourcePlacementTarget.Node, AnnotatedSourcePlacementTarget.Line],
            Placements(document, placed).Select(placement => placement.Target).Order());

        string json = JsonSerializer.Serialize(document);
        var replayed = JsonSerializer.Deserialize<AnnotatedSourceDocument>(json);
        Assert.Equal(document, replayed);
    }

    [Fact]
    public void ProjectionIsDeterministicAcrossIndependentRuns()
    {
        // Fact ids are cut from a sort over dictionary keys, so a comparison that
        // stopped short of a total order would renumber the whole payload between
        // two runs over identical input -- and every placement with it.
        string Serialized() => JsonSerializer.Serialize(
            Document(nameof(AnnotatedTasteFixture.AllocateAndRead), options: null));

        Assert.Equal(Serialized(), Serialized());
    }

    [Fact]
    public void EmptyPrintedBodyStillCarriesItsIl()
    {
        var document = Document(nameof(AnnotatedTasteFixture.Noop), options: null);

        var line = Assert.Single(document.Lines);
        Assert.Equal(SourceLineKind.Il, line.Kind);
        Assert.Equal(0, line.Id);
        Assert.Contains("ret", line.Text, StringComparison.Ordinal);
        Assert.Empty(document.Nodes);
        Assert.Empty(document.Regions);
        Assert.Empty(document.Facts);
        Assert.Empty(document.Placements);
    }

    [Fact]
    public void ConstructorChainOnlyBodyKeepsIlAndFacts()
    {
        using var source = MetadataSource.Open(typeof(ResearchAllocatingConstructorFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchAllocatingConstructorFixture).FullName!,
            ".ctor",
            OverloadIndex: 0,
            SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);

        Assert.DoesNotContain(document.Lines, line => line.Kind == SourceLineKind.CSharp);
        Assert.NotEmpty(document.Lines);
        var allocation = Assert.Single(document.Facts, fact => fact.Descriptor == "alloc.new");
        var placement = Assert.Single(Placements(document, allocation));
        Assert.Equal(AnnotatedSourcePlacementTarget.Line, placement.Target);
        Assert.Equal(allocation.SourceOffset, document.Lines[placement.TargetId!.Value].Offset);
        Assert.Empty(Unplaced(document));
    }

    [Fact]
    public void PrinterFailureCannotBecomeAnEmptySuccessfulDocument()
    {
        var failure = DecompilerResult.Failure("test.failure", "printer failed");

        var exception = Assert.Throws<InvalidOperationException>(
            () => ResearchViews.RequireSuccessfulDocumentOutput(failure));

        Assert.Contains("test.failure: printer failed", exception.Message, StringComparison.Ordinal);
        Assert.Equal("", ResearchViews.RequireSuccessfulDocumentOutput(DecompilerResult.Success("")));
    }

    [Fact]
    public void DocumentFailureIsIsolatedForSiblingProjections()
    {
        var (document, failure) = ResearchViews.CaptureSourceDocument(
            () => throw new InvalidOperationException("document failed"));

        Assert.Null(document);
        Assert.NotNull(failure);
        Assert.False(failure.Succeeded);
        Assert.Contains(
            failure.Diagnostics,
            diagnostic => diagnostic.Id == DiagnosticIds.InternalError
                && diagnostic.Message.Contains("document failed", StringComparison.Ordinal));
    }

    [Fact]
    public void BodylessMemberDocumentFailureKeepsSiblingProjection()
    {
        using var source = MetadataSource.Open(typeof(Action<>).Assembly.Location);

        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(Action<>).FullName!,
            nameof(Action<int>.Invoke),
            AnnotatedSource: true,
            SourceDocument: true));

        Assert.Null(projection.SourceDocument);
        Assert.NotNull(projection.SourceDocumentFailure);
        Assert.Contains(
            projection.SourceDocumentFailure.Diagnostics,
            diagnostic => diagnostic.Message.Contains("has no IL body", StringComparison.Ordinal));
        Assert.NotNull(projection.AnnotatedSource);
        Assert.False(projection.AnnotatedSource.Succeeded);
    }

    [Fact]
    public void DocumentOnlyHeaderFailureKeepsSiblingProjection()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            AnnotatedSource: true,
            Registry: new ResearchFactRegistry(new ThrowingHeaderProducer()),
            SourceDocument: true));

        Assert.Null(projection.SourceDocument);
        Assert.NotNull(projection.SourceDocumentFailure);
        Assert.Contains(
            projection.SourceDocumentFailure.Diagnostics,
            diagnostic => diagnostic.Message.Contains("header failed", StringComparison.Ordinal));
        Assert.True(projection.AnnotatedSource?.Succeeded);
    }

    [Fact]
    public void CostHeaderFailureStaysOutOfUnrelatedSiblingProjections()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            AnnotatedSource: true,
            CostOverlay: true,
            SemanticsOverlay: true,
            Registry: new ResearchFactRegistry(new ThrowingHeaderProducer()),
            SourceDocument: true));

        Assert.True(projection.AnnotatedSource?.Succeeded);
        Assert.True(projection.SemanticsOverlay?.Succeeded);
        Assert.False(projection.CostOverlay?.Body.Succeeded);
        Assert.Null(projection.SourceDocument);
        Assert.NotNull(projection.SourceDocumentFailure);
    }

    [Fact]
    public void SilentConstructorProloguePrecedesTheFirstPrintedStatement()
    {
        using var source = MetadataSource.Open(typeof(ResearchConstructorFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchConstructorFixture).FullName!,
            ".ctor",
            OverloadIndex: 1,
            SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);

        int firstCSharp = document.Lines
            .First(line => line.Kind == SourceLineKind.CSharp)
            .Id;
        var prologue = document.Lines.Take(firstCSharp).ToArray();
        Assert.NotEmpty(prologue);
        Assert.All(prologue, line => Assert.Equal(SourceLineKind.Il, line.Kind));
        Assert.Contains(prologue, line => line.Text.Contains("::.ctor", StringComparison.Ordinal));
    }

    static AnnotatedSourceDocument Document(string method, PrinterOptions? options)
    {
        using var source = MetadataSource.Open(typeof(AnnotatedTasteFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(AnnotatedTasteFixture).FullName!,
            method,
            PrinterOptions: options,
            SourceDocument: true));
        return Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);
    }

    static string CSharpText(AnnotatedSourceDocument document) => string.Join(
        "\n",
        document.Lines
            .Where(line => line.Kind == SourceLineKind.CSharp)
            .Select(line => line.Text));

    static AnnotatedSourceNode Node(AnnotatedSourceDocument document, AnnotatedSourcePlacement placement)
        => document.Nodes[placement.TargetId!.Value];

    static AnnotatedSourcePlacement[] Placements(
        AnnotatedSourceDocument document,
        AnnotatedSourceFact fact) =>
        [.. document.Placements.Where(placement => placement.FactId == fact.Id)];

    /// <summary>
    /// The facts a medium actually shows, derived by joining placements back to
    /// their targets rather than reading an embedded per-line list. Facts are
    /// deduplicated, so an observation seen in both media contributes one row to
    /// each medium's set -- which is exactly the agreement being asserted.
    /// </summary>
    static FactKey[] Facts(AnnotatedSourceDocument document, SourceLineKind kind)
    {
        var wanted = kind == SourceLineKind.Il
            ? AnnotatedSourcePlacementTarget.Line
            : AnnotatedSourcePlacementTarget.Node;
        return [.. document.Placements
            .Where(placement => placement.Target == wanted)
            .Select(placement => FactKey.From(document.Facts[placement.FactId]))
            .Distinct()
            .Order()];
    }

    static AnnotatedSourceFact[] Unplaced(AnnotatedSourceDocument document) =>
        [.. document.Placements
            .Where(placement => placement.Target == AnnotatedSourcePlacementTarget.Unplaced)
            .Select(placement => document.Facts[placement.FactId])];

    static FactKey[] AllFacts(AnnotatedSourceDocument document) =>
        [.. document.Facts.Select(FactKey.From).Order()];

    static void AssertNormalized(AnnotatedSourceDocument document)
    {
        Assert.Equal(Enumerable.Range(0, document.Lines.Count), document.Lines.Select(line => line.Id));
        Assert.Equal(Enumerable.Range(0, document.Nodes.Count), document.Nodes.Select(node => node.Id));
        Assert.Equal(Enumerable.Range(0, document.Facts.Count), document.Facts.Select(fact => fact.Id));

        // Structural extents live in their own medium's line space, so they are
        // bounds-checked against that medium's lines, not the whole stream.
        var csharp = MediumLines(document, SourceLineKind.CSharp);
        foreach (var extent in document.Nodes
            .Where(node => node.Medium == SourceLineKind.CSharp)
            .Select(node => node.Extent)
            .Concat(document.Regions.Select(region => region.Extent)))
        {
            Assert.InRange(extent.StartLine, 0, csharp.Length - 1);
            Assert.InRange(extent.EndLine, 0, csharp.Length - 1);
            Assert.InRange(extent.StartColumn, 0, csharp[extent.StartLine].Length);
            Assert.InRange(extent.EndColumn, 0, csharp[extent.EndLine].Length);
            Assert.True(
                extent.StartLine < extent.EndLine
                || extent.StartColumn < extent.EndColumn);
        }

        Assert.All(document.Facts, fact => Assert.NotEmpty(Placements(document, fact)));
        Assert.Equal(document.Placements.Count, document.Placements.Distinct().Count());
    }

    static string[] MediumLines(AnnotatedSourceDocument document, SourceLineKind kind) =>
        [.. document.Lines.Where(line => line.Kind == kind).Select(line => line.Text)];

    static string SelectText(string[] lines, PrintedExtent extent)
    {
        if (extent.StartLine == extent.EndLine)
            return lines[extent.StartLine][extent.StartColumn..extent.EndColumn];

        var selected = new List<string> { lines[extent.StartLine][extent.StartColumn..] };
        for (int line = extent.StartLine + 1; line < extent.EndLine; line++)
            selected.Add(lines[line]);
        selected.Add(lines[extent.EndLine][..extent.EndColumn]);
        return string.Join('\n', selected);
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
        internal static FactKey From(AnnotatedSourceFact fact) => new(
            fact.Descriptor,
            fact.Category,
            fact.Conditionality,
            fact.Detail,
            fact.SourceOffset);

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
        public string Name => "document-marker";
        public IReadOnlyList<string> Produces => [marker.Descriptor.Id];
        public IReadOnlyList<string> DependsOn => [];
        public IReadOnlyList<IAnnotation> Produce(ResearchFactContext context) => [marker];
    }

    sealed class MarkersProducer(IReadOnlyList<IAnnotation> markers) : IResearchFactProducer
    {
        public string Name => "document-markers";
        public IReadOnlyList<string> Produces => [.. markers.Select(marker => marker.Descriptor.Id)];
        public IReadOnlyList<string> DependsOn => [];
        public IReadOnlyList<IAnnotation> Produce(ResearchFactContext context) => markers;
    }

    sealed class ThrowingHeaderProducer : IResearchFactProducer
    {
        public string Name => "throwing-header";
        public IReadOnlyList<string> Produces => [];
        public IReadOnlyList<string> DependsOn => [];
        public IReadOnlyList<IAnnotation> Produce(ResearchFactContext context) => [];
        public IReadOnlyList<ResearchHeaderFact> ProduceHeaderFacts(ResearchFactContext context)
            => throw new InvalidOperationException("header failed");
    }

    sealed class MalformedTextProducer : IResearchFactProducer
    {
        static readonly AnnotationDescriptor Body =
            new("test.body.\uD800", AnnotationCategory.Cost, "body");
        static readonly AnnotationDescriptor Placed =
            new("test.placed.\uD800", AnnotationCategory.Cost, "placed");
        static readonly AnnotationDescriptor Literal =
            new("test.body.\\uD800", AnnotationCategory.Cost, "literal");
        static readonly AnnotationDescriptor Header =
            new("test.header.\uDC00", AnnotationCategory.Cost, "header");

        public string Name => "malformed-text";
        public IReadOnlyList<string> Produces => [Body.Id, Placed.Id, Literal.Id, Header.Id];
        public IReadOnlyList<string> DependsOn => [];
        public IReadOnlyList<IAnnotation> Produce(ResearchFactContext context)
            =>
            [
                new Annotation(Body, SourceOffset: -1, Detail: "body.\U0001F600\uDC00"),
                new Annotation(Placed, SourceOffset: 0, Detail: "placed.\U0001F600\uDC00"),
                new Annotation(Literal, SourceOffset: -1, Detail: "literal.\\uD800"),
            ];
        public IReadOnlyList<ResearchHeaderFact> ProduceHeaderFacts(ResearchFactContext context)
            => [new ResearchHeaderFact(Header, "header.\uD800")];
    }
}
