using System.Text.Json;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;

namespace ILInspector.Research.Tests;

[Collection(AnalysisIndexCacheCollection.Name)]
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
            FactRows: true,
            SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);

        // The text buffer is the artifact: the exact interleaved rendering, with
        // the facts kept out of it so a consumer can show clean source.
        Assert.NotEmpty(document.Text);
        Assert.DoesNotContain("alloc.", document.Text, StringComparison.Ordinal);
        Assert.Contains(document.Nodes, node => node.Medium == SourceLineKind.CSharp);
        Assert.Contains(document.Nodes, node => node.Medium == SourceLineKind.Il);

        var csharpFacts = InstanceKeys(
            document,
            projection.SourceDocumentFactIdentities,
            SourceLineKind.CSharp);
        var ilFacts = InstanceKeys(
            document,
            projection.SourceDocumentFactIdentities,
            SourceLineKind.Il);
        Assert.NotEmpty(csharpFacts);
        Assert.Equal(csharpFacts, ilFacts);

        // The same observation in both media is one fact with two targets, not
        // two facts a consumer has to guess are the same.
        var box = Assert.Single(document.Facts, fact => fact.Descriptor == "alloc.box");
        var boxNodes = Targets(document, box).Select(target => document.Nodes[target.NodeId]).ToArray();
        Assert.Equal(
            [SourceLineKind.CSharp, SourceLineKind.Il],
            boxNodes.Select(node => node.Medium).Order());

        // Fact -> target -> node -> spans -> text is the whole join, and it lands
        // on the exact sub-line characters in C# and the whole rendered
        // instruction in IL.
        var boxCSharp = Assert.Single(boxNodes, node => node.Medium == SourceLineKind.CSharp);
        Assert.Equal("value", Selected(document, boxCSharp));
        Assert.Single(boxCSharp.Spans);

        var boxIl = Assert.Single(boxNodes, node => node.Medium == SourceLineKind.Il);
        Assert.Equal("Instruction", boxIl.Kind);
        Assert.Equal(box.SourceOffset, boxIl.IlOffset);
        Assert.Contains(Lines(document), line => line.Il && line.Text == Selected(document, boxIl));

        var expected = Assert.IsAssignableFrom<IReadOnlyList<ResearchViews.FactRow>>(
                projection.Facts)
            .Where(row => row.InstanceKey is not null)
            .Select(row => row.InstanceKey!.Value)
            .OrderBy(key => key.Value)
            .ToArray();
        Assert.Equal(expected, ilFacts);

        var ilOffsets = Instructions(document).Select(node => node.IlOffset!.Value).ToArray();
        Assert.NotEmpty(ilOffsets);
        Assert.True(ilOffsets.SequenceEqual(ilOffsets.Order()));
        Assert.Equal(ilOffsets.Length, ilOffsets.Distinct().Count());

        AssertNormalized(document);
    }

    [Fact]
    public void MemberProjection_PreservesOneCensusAcrossFactsAndAnnotatedSource()
    {
        var producer = new CountingFindingsProducer(
        [
            Finding(new Annotation(
                new AnnotationDescriptor(
                    "test.first",
                    AnnotationCategory.Cost,
                    "first"),
                SourceOffset: 0,
                Detail: "first"),
                identity: "first"),
            Finding(new Annotation(
                new AnnotationDescriptor(
                    "test.second",
                    AnnotationCategory.Semantics,
                    "second"),
                SourceOffset: 0,
                Detail: "second"),
                identity: "second"),
        ]);
        using var source = MetadataSource.Open(
            typeof(ResearchFixture).Assembly.Location);

        var projection = ResearchViews.ProjectMember(
            new ResearchViews.MemberProjectionRequest(
                source,
                typeof(ResearchFixture).FullName!,
                nameof(ResearchFixture.BoxInt),
                FactRows: true,
                Registry: new ResearchFactRegistry(producer),
                SourceDocument: true));

        Assert.Equal(1, producer.ProduceCount);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<ResearchViews.FactRow>>(
            projection.Facts);
        var identities = Assert.IsAssignableFrom<
            IReadOnlyList<ResearchViews.AnnotatedSourceFactIdentity>>(
                projection.SourceDocumentFactIdentities);
        FindingCensusReceipt receipt = Assert.Single(
            rows.Select(row => Assert.IsType<FindingCensusReceipt>(
                    row.CensusReceipt))
                .Distinct());
        Assert.False(receipt.IsDefault);
        Assert.Equal(receipt, projection.FactCensusReceipt);
        Assert.All(identities, identity =>
            Assert.Equal(receipt, identity.CensusReceipt));
        Assert.Equal(
            rows.Select(row => Assert.IsType<FindingInstanceKey>(
                    row.InstanceKey))
                .OrderBy(key => key.Value),
            identities.Select(identity => identity.InstanceKey)
                .OrderBy(key => key.Value));
    }

    [Fact]
    public void MemberProjection_ReceiptsSuccessfulEmptyBodyCensus()
    {
        using var source = MetadataSource.Open(
            typeof(ResearchFixture).Assembly.Location);

        var projection = ResearchViews.ProjectMember(
            new ResearchViews.MemberProjectionRequest(
                source,
                typeof(ResearchFixture).FullName!,
                nameof(ResearchFixture.BoxInt),
                Registry: new ResearchFactRegistry(),
                SourceDocument: true));

        FindingCensusReceipt receipt = Assert.IsType<FindingCensusReceipt>(
            projection.FactCensusReceipt);
        Assert.False(receipt.IsDefault);
        Assert.Empty(Assert.IsType<AnnotatedSourceDocument>(
            projection.SourceDocument).Facts);
        Assert.Empty(Assert.IsAssignableFrom<
            IReadOnlyList<ResearchViews.AnnotatedSourceFactIdentity>>(
                projection.SourceDocumentFactIdentities));
    }

    [Fact]
    public void MemberProjection_PreservesDisplayIdenticalFindingMultiplicity()
    {
        var descriptor = new AnnotationDescriptor(
            "test.duplicate",
            AnnotationCategory.Cost,
            "duplicate");
        var subject = new FindingSubject("test-member", "test member");
        var key = new FindingKey("same-correspondence");
        Finding<IAnnotation> first = ResearchFactFinding.Create(
            subject,
            new Annotation(descriptor, SourceOffset: 0, Detail: "same"),
            key,
            ordinal: 0);
        Finding<IAnnotation> second = ResearchFactFinding.Create(
            subject,
            new Annotation(descriptor, SourceOffset: 0, Detail: "same"),
            key,
            ordinal: 0);
        Assert.Equal(first, second);
        Assert.NotSame(first, second);

        using var source = MetadataSource.Open(
            typeof(ResearchFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(
            new ResearchViews.MemberProjectionRequest(
                source,
                typeof(ResearchFixture).FullName!,
                nameof(ResearchFixture.BoxInt),
                FactRows: true,
                Registry: new ResearchFactRegistry(
                    new CountingFindingsProducer([first, second])),
                SourceDocument: true));
        Assert.True(
            projection.SourceDocumentFailure is null,
            projection.SourceDocumentFailure is null
                ? null
                : string.Join(
                    "; ",
                    projection.SourceDocumentFailure.Diagnostics));
        var document = Assert.IsType<AnnotatedSourceDocument>(
            projection.SourceDocument);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<ResearchViews.FactRow>>(
            projection.Facts);
        var identities = Assert.IsAssignableFrom<
            IReadOnlyList<ResearchViews.AnnotatedSourceFactIdentity>>(
                projection.SourceDocumentFactIdentities);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(row => row.InstanceKey).Distinct().Count());
        Assert.Equal(
            2,
            document.Facts.Count(fact =>
                fact.Descriptor == descriptor.Id
                && fact.Detail == "same"
                && fact.SourceOffset == 0));
        Assert.Equal(2, identities.Count);
        Assert.Equal(2, identities.Select(identity => identity.FactId).Distinct().Count());
        Assert.Equal(2, identities.Select(identity => identity.InstanceKey).Distinct().Count());
    }

    [Fact]
    public void NodesAreTextStructureIndependentOfFacts()
    {
        var document = Document(nameof(AnnotatedTasteFixture.AllocateAndRead), options: null);

        Assert.NotEmpty(document.Nodes);
        Assert.Equal(
            Enumerable.Range(0, document.Nodes.Count),
            document.Nodes.Select(node => node.Id));

        // Most nodes carry no fact at all. Nodes exist because the text has
        // structure, not because something was observed about it.
        var targeted = document.Targets.Select(target => target.NodeId).ToHashSet();
        Assert.NotEmpty(targeted);
        Assert.Contains(document.Nodes, node => !targeted.Contains(node.Id));
        Assert.Contains(
            document.Nodes,
            node => node.Medium == SourceLineKind.CSharp && !targeted.Contains(node.Id));

        AssertNormalized(document);
    }

    [Fact]
    public void CSharpNodesKeepThePrinterIdsAndInstructionsFollowThem()
    {
        var document = Document(nameof(AnnotatedTasteFixture.AllocateAndRead), options: null);

        // C# ids are the printer projection's, minted while IrNode identity was
        // alive; instruction nodes are appended after them, in IL order. A
        // consumer joining on an id therefore reaches the node the fact was
        // actually anchored to.
        var csharp = document.Nodes.TakeWhile(node => node.Medium == SourceLineKind.CSharp).ToArray();
        var instructions = document.Nodes.Skip(csharp.Length).ToArray();
        Assert.NotEmpty(csharp);
        Assert.NotEmpty(instructions);
        Assert.All(instructions, node => Assert.Equal(SourceLineKind.Il, node.Medium));
        Assert.All(instructions, node => Assert.Equal("Instruction", node.Kind));
        Assert.All(instructions, node => Assert.Single(node.Spans));
        Assert.All(csharp, node => Assert.Null(node.IlOffset));

        // Each instruction node covers its whole rendered line, so a consumer
        // rendering a caret under one selects the instruction and nothing else.
        var lines = Lines(document);
        Assert.Equal(
            [.. lines.Where(line => line.Il).Select(line => line.Text)],
            [.. instructions.Select(node => Selected(document, node))]);
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

        Assert.Contains("return a ? b : c;", document.Text);
        Assert.DoesNotContain(document.Nodes, node => node.Medium == SourceLineKind.Il);
        Assert.Contains(AllFacts(document), fact => fact.Descriptor == "cost.document-test");
        Assert.Equal(overlaysOnly.CostOverlay?.Body.Output, withDocument.CostOverlay?.Body.Output);
        Assert.Equal(overlaysOnly.SemanticsOverlay?.Output, withDocument.SemanticsOverlay?.Output);
        Assert.Equal(
            WithoutIdentity(overlaysOnly.Facts),
            WithoutIdentity(withDocument.Facts));
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
        Assert.Equal(
            WithoutIdentity(withoutDocument.Facts),
            WithoutIdentity(withDocument.Facts));
    }

    [Fact]
    public void LoopFixturePinsNodeKindsAndRegionRoles()
    {
        var document = LoopDocument();

        Assert.Equal(
            [
                "AssignmentStatement",
                "BinaryExpression",
                "ForStatement",
                "InvocationExpression",
                "LiteralExpression",
                "NameExpression",
                "ObjectCreationExpression",
                "ReturnStatement",
            ],
            document.Nodes
                .Where(node => node.Medium == SourceLineKind.CSharp)
                .Select(node => node.Kind)
                .Distinct()
                .Order());
        Assert.Equal(
            ["Instruction"],
            document.Nodes
                .Where(node => node.Medium == SourceLineKind.Il)
                .Select(node => node.Kind)
                .Distinct());
        Assert.Equal(
            [PrintedRegionRole.Construct, PrintedRegionRole.Header, PrintedRegionRole.Body],
            document.Regions.Select(region => region.Role).Distinct().Order());

        var construct = Assert.Single(document.Regions, region => region.Role == PrintedRegionRole.Construct);
        foreach (var region in document.Regions.Where(region => region.Role != PrintedRegionRole.Construct))
            Assert.True(Covers(construct.Spans, region.Spans));
    }

    [Fact]
    public void MultiLineCSharpStructureIsSpannedAroundTheInterleavedIl()
    {
        var document = LoopDocument();

        var loop = Assert.Single(document.Nodes, node => node.Kind == "ForStatement");
        var body = Assert.Single(document.Regions, region => region.Role == PrintedRegionRole.Body);

        // A construct printed across lines with IL woven between them is
        // discontinuous in the rendered text, so its exact characters are
        // several runs. One span would have swallowed the instructions.
        Assert.True(loop.Spans.Count > 1);
        Assert.True(body.Spans.Count > 1);

        // Non-vacuity: instructions really are printed inside the loop's range.
        var inside = Instructions(document)
            .SelectMany(node => node.Spans)
            .Where(span => span.Start > loop.Spans[0].Start && span.Start < End(loop.Spans[^1]))
            .ToArray();
        Assert.NotEmpty(inside);

        string selected = Selected(document, loop);
        Assert.StartsWith("for (", selected.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("new object()", selected, StringComparison.Ordinal);
        Assert.DoesNotContain("IL_", selected, StringComparison.Ordinal);
        Assert.DoesNotContain("IL_", SelectedText(document, body.Spans), StringComparison.Ordinal);

        // The spans are exactly the text between the instructions, so the loop's
        // own line breaks survive inside a span while an instruction ends one.
        Assert.Contains('\n', selected);

        // A line break the construct printed is the construct's text, even where
        // an instruction is printed straight after it. Losing it would collapse
        // `for (...)` onto `{` and `...;` onto `}`, so the runs would concatenate
        // to C# that was never printed.
        Assert.DoesNotContain("){", selected, StringComparison.Ordinal);
        Assert.DoesNotContain(";}", selected, StringComparison.Ordinal);
        Assert.DoesNotContain(";}", SelectedText(document, body.Spans), StringComparison.Ordinal);

        // The strong form of the same claim: concatenating the runs reproduces a
        // verbatim stretch of the C# the document rendered, breaks included.
        string csharp = CSharpText(document);
        Assert.Contains(selected, csharp, StringComparison.Ordinal);
        Assert.Contains(SelectedText(document, body.Spans), csharp, StringComparison.Ordinal);
        Assert.True(selected.Split('\n').Length > 2);

        AssertNormalized(document);
    }

    [Fact]
    public void FactsWithoutCSharpAnchorsRemainOnTheirExactInstructions()
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
        Assert.Empty(Untargeted(document));

        // An instruction target is an exact-offset claim, so the node's offset is
        // the fact's own.
        foreach (var target in document.Targets)
        {
            var node = document.Nodes[target.NodeId];
            if (node.Medium != SourceLineKind.Il)
                continue;
            Assert.Equal(document.Facts[target.FactId].SourceOffset, node.IlOffset);
        }
        Assert.All(
            il.Except(csharp),
            missing => Assert.Contains(
                document.Targets,
                target => FactKey.From(document.Facts[target.FactId]) == missing
                    && document.Nodes[target.NodeId].IlOffset == missing.SourceOffset));
    }

    [Fact]
    public void FactWithNothingToAnchorToSimplyHasNoTarget()
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

        // Unanchored is the absence of a target, not a third kind of row: the
        // observation is kept, and nothing invents a coordinate for it.
        var fact = Assert.Single(document.Facts, fact => fact.Descriptor == "test.unplaced");
        Assert.Equal(AnnotatedSourceFactOrigin.Body, fact.Origin);
        Assert.Empty(Targets(document, fact));
        Assert.Contains(Untargeted(document), untargeted => untargeted.Id == fact.Id);
    }

    [Fact]
    public void MemberHeaderFactsCarryHeaderOriginAndTargetNothing()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.HighLoopLeverageCallee),
            FactRows: true,
            SourceDocument: true));
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);

        var fact = Assert.Single(
            document.Facts,
            candidate => candidate.Descriptor == "cost.method"
                && candidate.Origin == AnnotatedSourceFactOrigin.MemberHeader);
        Assert.Equal(-1, fact.SourceOffset);
        Assert.Empty(Targets(document, fact));
        Assert.DoesNotContain(
            Assert.IsAssignableFrom<
                IReadOnlyList<ResearchViews.AnnotatedSourceFactIdentity>>(
                projection.SourceDocumentFactIdentities),
            identity => identity.FactId == fact.Id);

        ResearchViews.FactRow row = Assert.Single(
            Assert.IsAssignableFrom<IReadOnlyList<ResearchViews.FactRow>>(
                projection.Facts),
            candidate => candidate.Id == "cost.method");
        Assert.Null(row.CensusReceipt);
        Assert.Null(row.InstanceKey);
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

        var untargeted = Untargeted(document);
        Assert.Contains(
            untargeted,
            fact => fact.Descriptor == "test.body.\\uD800"
                && fact.Detail == "body.\U0001F600\\uDC00");
        Assert.Contains(
            document.Facts,
            fact => fact.Descriptor == "test.header.\\uDC00"
                && fact.Detail == "header.\\uD800"
                && fact.Origin == AnnotatedSourceFactOrigin.MemberHeader);
        Assert.Contains(
            untargeted,
            fact => fact.Descriptor == "test.body.\\\\uD800"
                && fact.Detail == "literal.\\\\uD800");
        Assert.Equal(
            untargeted.Length,
            untargeted.Select(fact => (fact.Descriptor, fact.Detail)).Distinct().Count());

        // Escaping happens before identity, so the one observation targeted in
        // both media stays a single fact rather than splitting on the medium.
        var placed = Assert.Single(document.Facts, fact => fact.Descriptor == "test.placed.\\uD800");
        Assert.Equal("placed.\U0001F600\\uDC00", placed.Detail);
        Assert.Equal(
            [SourceLineKind.CSharp, SourceLineKind.Il],
            Targets(document, placed).Select(target => document.Nodes[target.NodeId].Medium).Order());

        string json = JsonSerializer.Serialize(document);
        var replayed = JsonSerializer.Deserialize<AnnotatedSourceDocument>(json);
        Assert.Equal(document, replayed);
        Assert.Equal(document.GetHashCode(), replayed!.GetHashCode());
        Assert.Equal(document.Text, replayed.Text);
        Assert.Equal(document.Nodes, replayed.Nodes);
        Assert.Equal(document.Targets, replayed.Targets);
    }

    [Fact]
    public void ProjectionIsDeterministicAcrossIndependentRuns()
    {
        // Fact ids are cut from a sort over dictionary keys, so a comparison that
        // stopped short of a total order would renumber the whole payload between
        // two runs over identical input -- and every target with it.
        string Serialized() => JsonSerializer.Serialize(
            Document(nameof(AnnotatedTasteFixture.AllocateAndRead), options: null));

        Assert.Equal(Serialized(), Serialized());
    }

    [Fact]
    public void EmptyPrintedBodyStillCarriesItsIl()
    {
        var document = Document(nameof(AnnotatedTasteFixture.Noop), options: null);

        var node = Assert.Single(document.Nodes);
        Assert.Equal(SourceLineKind.Il, node.Medium);
        Assert.Equal("Instruction", node.Kind);
        Assert.Equal(0, node.IlOffset);
        Assert.Contains("ret", document.Text, StringComparison.Ordinal);
        Assert.Equal(document.Text, Selected(document, node));
        Assert.Empty(document.Regions);
        Assert.Empty(document.Facts);
        Assert.Empty(document.Targets);
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

        Assert.DoesNotContain(document.Nodes, node => node.Medium == SourceLineKind.CSharp);
        Assert.NotEmpty(Instructions(document));
        var allocation = Assert.Single(document.Facts, fact => fact.Descriptor == "alloc.new");
        var target = Assert.Single(Targets(document, allocation));
        Assert.Equal(allocation.SourceOffset, document.Nodes[target.NodeId].IlOffset);
        Assert.Empty(Untargeted(document));
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

        var lines = Lines(document);
        var prologue = lines.TakeWhile(line => line.Il).ToArray();
        Assert.NotEmpty(prologue);
        Assert.Contains(prologue, line => line.Text.Contains("::.ctor", StringComparison.Ordinal));
        Assert.Contains(lines, line => !line.Il);
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

    static AnnotatedSourceDocument LoopDocument()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.AllocInLoopCallee),
            SourceDocument: true));
        return Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);
    }

    static AnnotatedSourceNode[] Instructions(AnnotatedSourceDocument document) =>
        [.. document.Nodes.Where(node => node.IlOffset is not null)];

    static int End(AnnotatedSourceSpan span) => span.Start + span.Length;

    static string Selected(AnnotatedSourceDocument document, AnnotatedSourceNode node)
        => SelectedText(document, node.Spans);

    static string SelectedText(
        AnnotatedSourceDocument document,
        IReadOnlyList<AnnotatedSourceSpan> spans) => string.Concat(
            spans.Select(span => document.Text.Substring(span.Start, span.Length)));

    /// <summary>
    /// The rendered lines, derived the way the payload says they are: split the
    /// text on newlines, and read the medium off the instruction nodes rather
    /// than off an identity the document no longer carries.
    /// </summary>
    static (string Text, bool Il)[] Lines(AnnotatedSourceDocument document)
    {
        var instructionStarts = Instructions(document)
            .Select(node => node.Spans[0].Start)
            .ToHashSet();
        var lines = new List<(string Text, bool Il)>();
        int start = 0;
        foreach (string line in document.Text.Split('\n'))
        {
            lines.Add((line, instructionStarts.Contains(start)));
            start += line.Length + 1;
        }
        return [.. lines];
    }

    static string CSharpText(AnnotatedSourceDocument document) => string.Join(
        '\n',
        Lines(document).Where(line => !line.Il).Select(line => line.Text));

    static AnnotatedSourceTarget[] Targets(AnnotatedSourceDocument document, AnnotatedSourceFact fact) =>
        [.. document.Targets.Where(target => target.FactId == fact.Id)];

    static FindingInstanceKey[] InstanceKeys(
        AnnotatedSourceDocument document,
        IReadOnlyList<ResearchViews.AnnotatedSourceFactIdentity>? identities,
        SourceLineKind medium)
    {
        var byFact = Assert.IsAssignableFrom<
                IReadOnlyList<ResearchViews.AnnotatedSourceFactIdentity>>(
                identities)
            .ToDictionary(identity => identity.FactId);
        return
        [
            .. document.Targets
                .Where(target =>
                    document.Nodes[target.NodeId].Medium == medium)
                .Select(target => byFact[target.FactId].InstanceKey)
                .Distinct()
                .OrderBy(key => key.Value),
        ];
    }

    static ResearchViews.FactRow[] WithoutIdentity(
        IReadOnlyList<ResearchViews.FactRow>? facts)
        => facts is null
            ? []
            :
            [
                .. facts.Select(fact => fact with
                {
                    CensusReceipt = null,
                    InstanceKey = null,
                }),
            ];

    /// <summary>
    /// The facts a medium actually shows, derived by joining targets back to
    /// their nodes. Facts are deduplicated, so an observation seen in both media
    /// contributes one row to each medium's set -- which is exactly the
    /// agreement being asserted.
    /// </summary>
    static FactKey[] Facts(AnnotatedSourceDocument document, SourceLineKind medium) =>
        [.. document.Targets
            .Where(target => document.Nodes[target.NodeId].Medium == medium)
            .Select(target => FactKey.From(document.Facts[target.FactId]))
            .Distinct()
            .Order()];

    static AnnotatedSourceFact[] Untargeted(AnnotatedSourceDocument document)
    {
        var targeted = document.Targets.Select(target => target.FactId).ToHashSet();
        return [.. document.Facts.Where(fact => !targeted.Contains(fact.Id))];
    }

    static FactKey[] AllFacts(AnnotatedSourceDocument document) =>
        [.. document.Facts.Select(FactKey.From).Order()];

    static void AssertNormalized(AnnotatedSourceDocument document)
    {
        Assert.Equal(Enumerable.Range(0, document.Nodes.Count), document.Nodes.Select(node => node.Id));
        Assert.Equal(Enumerable.Range(0, document.Facts.Count), document.Facts.Select(fact => fact.Id));

        // Spans are the only coordinate currency, so each set is ordered,
        // non-overlapping, non-empty, and inside the buffer.
        foreach (var spans in document.Nodes
            .Select(node => node.Spans)
            .Concat(document.Regions.Select(region => region.Spans)))
        {
            Assert.NotEmpty(spans);
            int previousEnd = 0;
            foreach (var span in spans)
            {
                Assert.True(span.Length > 0);
                Assert.True(span.Start >= previousEnd);
                Assert.True(End(span) <= document.Text.Length);
                previousEnd = End(span);
            }
        }

        // No node selects another medium's characters: the instructions woven
        // into a C# construct are outside every one of its spans.
        var instructionSpans = Instructions(document).SelectMany(node => node.Spans).ToArray();
        foreach (var span in document.Nodes
            .Where(node => node.Medium == SourceLineKind.CSharp)
            .SelectMany(node => node.Spans))
        {
            Assert.DoesNotContain(
                instructionSpans,
                instruction => instruction.Start < End(span) && span.Start < End(instruction));
        }

        Assert.All(document.Targets, target =>
        {
            Assert.InRange(target.FactId, 0, document.Facts.Count - 1);
            Assert.InRange(target.NodeId, 0, document.Nodes.Count - 1);
        });
        Assert.Equal(document.Targets.Count, document.Targets.Distinct().Count());
    }

    static bool Covers(
        IReadOnlyList<AnnotatedSourceSpan> outer,
        IReadOnlyList<AnnotatedSourceSpan> inner) => inner.All(
            span => outer.Any(candidate => candidate.Start <= span.Start && End(span) <= End(candidate)));

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
        public IReadOnlyList<Finding<IAnnotation>> Produce(ResearchFactContext context)
            => [TestFinding(marker)];
    }

    sealed class MarkersProducer(IReadOnlyList<IAnnotation> markers) : IResearchFactProducer
    {
        public string Name => "document-markers";
        public IReadOnlyList<string> Produces => [.. markers.Select(marker => marker.Descriptor.Id)];
        public IReadOnlyList<string> DependsOn => [];
        public IReadOnlyList<Finding<IAnnotation>> Produce(ResearchFactContext context)
            => [.. markers.Select(TestFinding)];
    }

    sealed class CountingFindingsProducer(
        IReadOnlyList<Finding<IAnnotation>> findings) : IResearchFactProducer
    {
        public int ProduceCount { get; private set; }
        public string Name => "counting-findings";
        public IReadOnlyList<string> Produces =>
            [.. findings.Select(finding => finding.Payload.Descriptor.Id)];
        public IReadOnlyList<string> DependsOn => [];

        public IReadOnlyList<Finding<IAnnotation>> Produce(
            ResearchFactContext context)
        {
            ProduceCount++;
            return findings;
        }
    }

    sealed class ThrowingHeaderProducer : IResearchFactProducer
    {
        public string Name => "throwing-header";
        public IReadOnlyList<string> Produces => [];
        public IReadOnlyList<string> DependsOn => [];
        public IReadOnlyList<Finding<IAnnotation>> Produce(ResearchFactContext context) => [];
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
        public IReadOnlyList<Finding<IAnnotation>> Produce(ResearchFactContext context)
            =>
            [
                TestFinding(new Annotation(
                    Body,
                    SourceOffset: -1,
                    Detail: "body.\U0001F600\uDC00")),
                TestFinding(new Annotation(
                    Placed,
                    SourceOffset: 0,
                    Detail: "placed.\U0001F600\uDC00")),
                TestFinding(new Annotation(
                    Literal,
                    SourceOffset: -1,
                    Detail: "literal.\\uD800")),
            ];
        public IReadOnlyList<ResearchHeaderFact> ProduceHeaderFacts(ResearchFactContext context)
            => [new ResearchHeaderFact(Header, "header.\uD800")];
    }

    static Finding<IAnnotation> TestFinding(
        IAnnotation annotation,
        int ordinal = 0)
        => ResearchFactFinding.Create(
            new FindingSubject("test-member", "test member"),
            annotation,
            new FindingKey($"{annotation.Descriptor.Id}|{ordinal}"),
            ordinal);

    static Finding<IAnnotation> Finding(
        IAnnotation annotation,
        string identity)
        => ResearchFactFinding.Create(
            new FindingSubject("test-member", "test member"),
            annotation,
            new FindingKey(identity));
}
