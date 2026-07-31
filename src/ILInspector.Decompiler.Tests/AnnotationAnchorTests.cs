using System.Collections.Generic;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

public class AnnotationAnchorTests
{
    // Classify the imported tree, then raise it in place and anchor the
    // annotations onto the raised statements — the real pipeline order.
    static (IrFunction Raised, IReadOnlyDictionary<IrNode, IReadOnlyList<IAnnotation>> Map) AnchorFor(string methodName)
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(AllocSampleClass).FullName!, methodName);
        Assert.NotNull(function);

        var annotations = ResearchViews.CollectFacts(source, function!);
        CSharpPrinter.PrintRaised(function!);   // raises in place
        return (function!, AnnotationAnchor.Anchor(function!, annotations));
    }

    [Fact]
    public void Box_AnchorsToTheReturnStatement_EvenThoughTheBoxWasRaisedAway()
    {
        // object BoxInt(int x) => x;  the box is erased by the raise, so no node
        // carries its exact offset — range anchoring still lands it on `return`.
        var (_, map) = AnchorFor(nameof(AllocSampleClass.BoxInt));

        var owner = Assert.Single(map);
        Assert.IsType<Return>(owner.Key);
        Assert.Contains(owner.Value, a => a.Descriptor.Id == "alloc.box");
    }

    [Fact]
    public void RaisedArrayLiteral_AnchorsToItsOwnStatement_NotThePrecedingOne()
    {
        // object first = new object(); return new object[] { first };
        // Both allocations are real and distinct. The array's newarr is subsumed
        // by the array-literal fold, so this only holds because the fold hands
        // the literal the offset it consumed — otherwise nothing in the tree
        // covers that offset and the fact falls back onto the preceding store.
        var (_, map) = AnchorFor(nameof(AllocSampleClass.AllocatesTwice));

        var arrayOwner = Assert.Single(map, e => e.Value.Any(a => a.Descriptor.Id == "alloc.array")).Key;
        var newOwner = Assert.Single(map, e => e.Value.Any(a => a.Descriptor.Id == "alloc.new")).Key;

        Assert.IsType<Return>(arrayOwner);
        Assert.IsType<StoreLocal>(newOwner);
        Assert.NotSame(arrayOwner, newOwner);
    }

    [Fact]
    public void EveryAnnotation_IsAnchoredSomewhere()
    {
        // Positive-only: a fact is never silently dropped.
        foreach (var method in new[]
        {
            nameof(AllocSampleClass.MakeArray),
            nameof(AllocSampleClass.Capture),
            nameof(AllocSampleClass.SumEnumerable),
            nameof(AllocSampleClass.Range),
        })
        {
            var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
            var function = IrImporter.Import(source, typeof(AllocSampleClass).FullName!, method)!;
            var annotations = ResearchViews.CollectFacts(source, function);
            CSharpPrinter.PrintRaised(function);

            var map = AnnotationAnchor.Anchor(function, annotations);
            int anchored = map.Values.Sum(list => list.Count);

            Assert.Equal(annotations.Count, anchored);
        }
    }

    [Fact]
    public void Array_AnchorsToAStatementThatStillContainsTheNewArray()
    {
        // The array survives the raise, so its fact should land on the very
        // statement whose subtree holds the NewArray.
        var (_, map) = AnchorFor(nameof(AllocSampleClass.MakeArray));

        var entry = Assert.Single(map, kv => kv.Value.Any(a => a.Descriptor.Id == "alloc.array"));
        var statementNodes = new List<IrNode> { entry.Key };
        statementNodes.AddRange(entry.Key.Descendants);
        Assert.Contains(statementNodes, n => n is NewArray);
    }

    [Theory]
    [InlineData("S_0")]
    [InlineData("__exception")]
    public void CaretExtent_PrefersTheAllocation_OverAStandInSharingItsOffset(string standInText)
    {
        // `return new Holder(S_0);` where the raise could not recover the
        // argument, so it prints a stand-in that carries the same instruction
        // offset as the newobj it feeds. The stand-in is the narrower node, so
        // width alone would underline the argument instead of the allocation the
        // fact reports. Measured over System.Private.CoreLib, that mistake
        // affected 50 of 10,664 alloc.new underlines for the stack-slot form;
        // __exception has the same shape and is excluded for the same reason.
        const int Offset = 5;
        var objectType = TypeRef.CoreLib("System", "Object");
        var holder = TypeRef.CoreLib("Test", "Holder");
        IrExpression slot = standInText == "S_0"
            ? new LoadStackSlot(0, objectType)
            : new CaughtException(objectType);
        slot.SetSourceOffset(Offset);
        var allocation = new NewObject(
            new MethodRef(holder, ".ctor", TypeRef.CoreLib("System", "Void"), [objectType], HasThis: true),
            [slot]);
        allocation.SetSourceOffset(Offset);
        var statement = new Return(allocation);
        statement.SetSourceOffset(Offset);

        var block = new Block(0);
        block.Add(statement);
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M", holder,
            new MethodSignature(holder, [], HasThis: false, GenericParameterCount: 0),
            [], container);

        CSharpPrinter.PrintRaised(function, out var ranges);
        var annotation = new Annotation(
            new AnnotationDescriptor("alloc.new", AnnotationCategory.Allocation, "allocation"),
            Offset);

        var extents = AnnotationAnchor.ComputeCaretExtents(
            [annotation], AnnotationAnchor.ComputeSpans(function), ranges);

        var extent = Assert.Contains(annotation, (IDictionary<IAnnotation, AnnotationAnchor.CaretExtent>)extents);
        Assert.True(AnnotationAnchor.TryGetPrintedLine(statement, ranges, out int lineIndex));
        string line = ranges.Output.Split('\n')[lineIndex].TrimEnd('\r');

        // The stand-in is on the line and is the narrower node, so a width-only
        // rule underlines it; this asserts the allocation wins instead.
        Assert.Contains(standInText, line, StringComparison.Ordinal);
        Assert.StartsWith("new ", line.Substring(extent.Column, extent.Length), StringComparison.Ordinal);
    }

    [Fact]
    public void CaretExtent_TrimsAStatementWideExtentOffTheLineIndent()
    {
        // When the statement itself is the narrowest node carrying the offset,
        // its printed range starts at the line's indent, so an untrimmed extent
        // would draw carets left of the code. Nest the statement so the indent
        // is non-zero and give only the statement the offset, leaving the
        // expression inside it without one.
        const int Offset = 7;
        var objectType = TypeRef.CoreLib("System", "Object");
        var holder = TypeRef.CoreLib("Test", "Holder");
        var allocation = new NewObject(
            new MethodRef(objectType, ".ctor", TypeRef.CoreLib("System", "Void"), [], HasThis: true),
            []);
        var statement = new ExpressionStatement(allocation);
        statement.SetSourceOffset(Offset);

        // A second offset inside the same block widens the if's span past the
        // statement's, so Best picks the statement rather than the if.
        var second = new ExpressionStatement(new NewObject(
            new MethodRef(objectType, ".ctor", TypeRef.CoreLib("System", "Void"), [], HasThis: true),
            []));
        second.SetSourceOffset(Offset + 2);

        var inner = new Block(1);
        inner.Add(statement);
        inner.Add(second);
        var outer = new Block(0);
        outer.Add(new IfStatement(new Constant(true, TypeRef.CoreLib("System", "Boolean")), inner, null));
        outer.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(outer);
        var function = new IrFunction(
            "M", holder,
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            [], container);

        CSharpPrinter.PrintRaised(function, out var ranges);
        var annotation = new Annotation(
            new AnnotationDescriptor("alloc.new", AnnotationCategory.Allocation, "allocation"),
            Offset);

        var extents = AnnotationAnchor.ComputeCaretExtents(
            [annotation], AnnotationAnchor.ComputeSpans(function), ranges);
        var extent = Assert.Contains(annotation, (IDictionary<IAnnotation, AnnotationAnchor.CaretExtent>)extents);

        Assert.True(AnnotationAnchor.TryGetPrintedLine(statement, ranges, out int lineIndex));
        string line = ranges.Output.Split('\n')[lineIndex].TrimEnd('\r');

        // The line is indented, so an untrimmed extent would start at column 0.
        Assert.NotEqual(0, line.Length - line.AsSpan().TrimStart().Length);
        Assert.False(char.IsWhiteSpace(line[extent.Column]));
        Assert.False(char.IsWhiteSpace(line[extent.Column + extent.Length - 1]));
        Assert.Equal(line.Trim(), line.Substring(extent.Column, extent.Length));
    }

    [Fact]
    public void CaretExtent_IsWithheldWhenTheNarrowNodePrintsOnAContinuationLine()
    {
        // A statement that wraps puts its narrow sub-expression on a later line
        // than the one the fact is attached to, so narrowing there would draw
        // the caret under a line the fact does not belong to. 279 facts in
        // System.Private.CoreLib take this path; all keep the statement-wide
        // caret. Only the if and the allocation carry the offset -- giving it to
        // the inner statement too would make Best pick that, and the owner line
        // would agree, which is the case this test must avoid.
        const int Offset = 7;
        var objectType = TypeRef.CoreLib("System", "Object");
        var holder = TypeRef.CoreLib("Test", "Holder");
        var allocation = new NewObject(
            new MethodRef(objectType, ".ctor", TypeRef.CoreLib("System", "Void"), [], HasThis: true),
            []);
        allocation.SetSourceOffset(Offset);

        var inner = new Block(1);
        inner.Add(new ExpressionStatement(allocation));
        var conditional = new IfStatement(
            new Constant(true, TypeRef.CoreLib("System", "Boolean")), inner, null);
        conditional.SetSourceOffset(Offset);

        var outer = new Block(0);
        outer.Add(conditional);
        outer.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(outer);
        var function = new IrFunction(
            "M", holder,
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            [], container);

        CSharpPrinter.PrintRaised(function, out var ranges);

        // Non-vacuity: the allocation must really print on a different line from
        // the statement the fact anchors to, or this gates nothing.
        Assert.True(AnnotationAnchor.TryGetPrintedLine(conditional, ranges, out int ownerLine));
        Assert.True(ranges.TryGetLineColumn(allocation, out int nodeLine, out _, out _));
        Assert.NotEqual(ownerLine, nodeLine);

        var annotation = new Annotation(
            new AnnotationDescriptor("alloc.new", AnnotationCategory.Allocation, "allocation"),
            Offset);

        var extents = AnnotationAnchor.ComputeCaretExtents(
            [annotation], AnnotationAnchor.ComputeSpans(function), ranges);

        Assert.DoesNotContain(annotation, (IDictionary<IAnnotation, AnnotationAnchor.CaretExtent>)extents);
    }

    [Fact]
    public void CaretExtent_IsWithheldWhenTheTrimRejectsTheRange()
    {
        // The caller must honour a rejected trim rather than storing the raw
        // range. The C# printer never emits a node as whitespace alone, so the
        // rejection branch is unreachable through it; a hand-built range map is
        // the only way to hold the caller to the helper's contract.
        const int Offset = 7;
        const string Output = "a   b\n";
        var objectType = TypeRef.CoreLib("System", "Object");
        var holder = TypeRef.CoreLib("Test", "Holder");
        var allocation = new NewObject(
            new MethodRef(objectType, ".ctor", TypeRef.CoreLib("System", "Void"), [], HasThis: true),
            []);
        allocation.SetSourceOffset(Offset);
        var statement = new ExpressionStatement(allocation);
        statement.SetSourceOffset(Offset);

        var block = new Block(0);
        block.Add(statement);
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M", holder,
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            [], container);

        var ranges = new PrintedRangeMap();
        ranges.Record(statement, 0, 5);
        // Narrower than the statement and carrying the same offset, so it wins
        // "narrowest" -- but it covers only the three spaces between a and b.
        ranges.Record(allocation, 1, 4);
        ranges.Complete(Output);

        Assert.True(ranges.TryGetLineColumn(allocation, out int line, out int column, out int length));
        Assert.Equal(0, line);
        Assert.Equal("   ", Output.Substring(column, length));

        var annotation = new Annotation(
            new AnnotationDescriptor("alloc.new", AnnotationCategory.Allocation, "allocation"),
            Offset);

        var extents = AnnotationAnchor.ComputeCaretExtents(
            [annotation], AnnotationAnchor.ComputeSpans(function), ranges);

        Assert.DoesNotContain(annotation, (IDictionary<IAnnotation, AnnotationAnchor.CaretExtent>)extents);
    }

    [Theory]
    // Leading indent only — what a statement's own printed range looks like.
    [InlineData("    new object();", 0, 17, 4, 13)]
    // Trailing padding. No printer output produces this today, which is exactly
    // why it needs a direct gate: the corpus cannot tell whether the trailing
    // loop still works, so a regression there would be invisible.
    [InlineData("    x   ", 4, 4, 4, 1)]
    [InlineData("    x   ", 0, 8, 4, 1)]
    // Already tight: trimming must not move an extent that names an expression.
    [InlineData("    new object();", 4, 12, 4, 12)]
    // Non-space whitespace. The C# printer indents with spaces, so only a
    // direct gate can hold the helper to char.IsWhiteSpace rather than ' '.
    [InlineData("\tx\t", 0, 3, 1, 1)]
    [InlineData("\u00a0x", 0, 2, 1, 1)]
    // Reaching exactly the last character, so a clamp that stopped one short
    // would silently drop it. 87,400 of the corpus's printed ranges end here.
    [InlineData("    new object();", 4, 13, 4, 13)]
    // Overhanging the end of the line. ComputeCaretExtents cannot deliver this
    // today -- TryGetLineColumn rejects every range that crosses a line break,
    // 16,895 of them in the corpus, so 0 of 359,584 survivors overhang -- but
    // the clamp is the reason an overhang is survivable at all, and an internal
    // helper is not entitled to assume it keeps only its current caller.
    [InlineData("    x", 4, 99, 4, 1)]
    public void TryTrimToPrinted_ShrinksOntoPrintedCharactersAtBothEnds(
        string lineText, int column, int length, int expectedColumn, int expectedLength)
    {
        Assert.True(AnnotationAnchor.TryTrimToPrinted(lineText, ref column, ref length));
        Assert.Equal(expectedColumn, column);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    // All whitespace, and a degenerate range: there is nothing to point at, so
    // the caller must fall back to the statement rather than draw a caret.
    [InlineData("        ", 0, 8)]
    [InlineData("    x", 0, 4)]
    [InlineData("    x", 2, 0)]
    [InlineData("    x", -1, 3)]
    [InlineData("    x", 9, 3)]
    public void TryTrimToPrinted_RefusesARangeWithNoPrintedCharacters(
        string lineText, int column, int length)
        => Assert.False(AnnotationAnchor.TryTrimToPrinted(lineText, ref column, ref length));

    [Fact]
    public void ResearchRegistry_RunsAllocationProducer()
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(AllocSampleClass).FullName!, nameof(AllocSampleClass.BoxInt))!;

        var annotations = ResearchViews.CollectFacts(source, function);

        Assert.Contains(annotations, a => a.Descriptor.Id == "alloc.box");
    }
}
