using System.Text.Json;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;

namespace ILInspector.Decompiler.Tests;

// The rich map the printer builds is keyed by IrNode, so it cannot leave the
// process that built it. These pin the projection that can: an extent, a name,
// and an integer id -- no references, and therefore serialisable.
[Trait("Area", "Printer")]
public class PrintedBodyMapTests
{
    static (string Output, PrintedRangeMap Ranges) Print(string methodName)
        => Print(typeof(AllocSampleClass), methodName);

    static (string Output, PrintedRangeMap Ranges) Print(Type fixtureType, string methodName)
    {
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!, out var ranges);
        Assert.NotNull(result.Output);
        return (result.Output!, ranges);
    }

    [Theory]
    [InlineData(nameof(AllocSampleClass.SumList))]
    [InlineData(nameof(AllocSampleClass.MakeArray))]
    public void EverySpanSelectsExactlyTheCharactersTheNodePrinted(string methodName)
    {
        // The whole point of the projection is that a consumer holding only text
        // can slice it. If the extent did not select the same characters
        // the node-keyed range does, the payload would be confidently wrong.
        var (output, ranges) = Print(methodName);
        var map = PrintedBodyMap.Create(ranges);
        Assert.NotEmpty(map.Nodes);

        // Independently slice the printer's absolute offsets and compare them
        // with the portable line/column projection. Reusing only TryGetExtent
        // on both sides would let a coordinate conversion defect agree with
        // itself.
        var expected = new HashSet<(string Kind, PrintedExtent Extent)>();
        foreach (var printed in ranges)
        {
            if (!ranges.TryGetExtent(printed.Node, out var extent))
                continue;
            int start = printed.Characters.Start.GetOffset(output.Length);
            int end = printed.Characters.End.GetOffset(output.Length);
            Assert.Equal(
                output[start..end].TrimEnd('\r', '\n'),
                Text(map, extent));
            expected.Add((
                AnnotatedSourceNodeKindProjection.From(printed.Node),
                extent));
        }

        Assert.Equal(expected.Count, map.Nodes.Count);
        Assert.True(expected.SetEquals(map.Nodes.Select(node => (node.Kind, node.Extent))));
    }

    [Fact]
    public void StableKindProjectionMakesAnExplicitDecisionForEveryIrNode()
    {
        var concreteNodes = typeof(IrNode).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(IrNode).IsAssignableFrom(type))
            .OrderBy(type => type.FullName)
            .ToArray();
        var mappings = AnnotatedSourceNodeKindProjection.Mappings
            .OrderBy(pair => pair.Key.FullName)
            .ToArray();

        Assert.NotEmpty(concreteNodes);
        Assert.Equal(concreteNodes, mappings.Select(pair => pair.Key));
        Assert.All(mappings, pair => Assert.True(
            AnnotatedSourceNodeKinds.IsKnown(pair.Value),
            $"{pair.Key.Name} maps to undocumented kind {pair.Value}."));
        Assert.DoesNotContain(mappings, pair => pair.Value == AnnotatedSourceNodeKinds.Unknown);

        Assert.Equal("EmptyStatement", AnnotatedSourceNodeKindProjection.From(new LabelAnchor()));
        Assert.Equal("ConversionExpression", AnnotatedSourceNodeKindProjection.From(
            new Coerce(
                TypeRef.CoreLib("System", "Int64"),
                new Constant(1, TypeRef.CoreLib("System", "Int32")))));
        Assert.Equal("AssignmentStatement", AnnotatedSourceNodeKindProjection.From(
            new StoreStackSlot(0, new Constant(1, TypeRef.CoreLib("System", "Int32")))));
        Assert.Equal("BinaryExpression", AnnotatedSourceNodeKindProjection.From(
            new LogicalBinary(
                LogicalKind.And,
                new Constant(true, TypeRef.CoreLib("System", "Boolean")),
                new Constant(false, TypeRef.CoreLib("System", "Boolean")))));

        var objectType = TypeRef.CoreLib("System", "Object");
        var intType = TypeRef.CoreLib("System", "Int32");
        var indexer = new MethodRef(objectType, "get_Item", objectType, [intType], HasThis: true);
        Assert.Equal("ElementAccessExpression", AnnotatedSourceNodeKindProjection.From(
            new LoadProperty(
                indexer,
                new LoadArgument(0, "items", objectType),
                [new Constant(0, intType)])));
        Assert.Equal("MemberAccessExpression", AnnotatedSourceNodeKindProjection.From(
            new LoadProperty(
                indexer with { Name = "get_Count", ParameterTypes = [] },
                new LoadArgument(0, "items", objectType),
                [])));

        Assert.Equal("TypeOfExpression", AnnotatedSourceNodeKindProjection.From(
            new LoadToken(RuntimeTokenKind.Type, objectType, objectType.ToDisplayString())));
        Assert.Equal("UnsupportedExpression", AnnotatedSourceNodeKindProjection.From(
            new LoadToken(RuntimeTokenKind.Field, null, "C.F")));
        Assert.Equal("UnsupportedExpression", AnnotatedSourceNodeKindProjection.From(
            new LoadFunctionPointer(
                new MethodRef(objectType, "Target", intType, [], HasThis: false),
                isVirtual: false,
                instance: null)));
        Assert.Equal("UnsupportedExpression", AnnotatedSourceNodeKindProjection.From(new EndFinally()));
        Assert.Equal("UnsupportedExpression", AnnotatedSourceNodeKindProjection.From(
            new EndFilter(new Constant(1, intType))));
        Assert.Equal("UnsupportedExpression", AnnotatedSourceNodeKindProjection.From(
            new CopyBlock(
                new Constant(0, intType),
                new Constant(0, intType),
                new Constant(1, intType))));
    }

    [Fact]
    public void ReplayToleratesKindsAddedByANewerProducer()
    {
        var map = new PrintedBodyMap(
            ["future"],
            [new PrintedNodeSpan(0, "FutureSyntax", new PrintedExtent(0, 0, 0, 6))],
            [],
            []);

        Assert.False(AnnotatedSourceNodeKinds.IsKnown(Assert.Single(map.Nodes).Kind));
    }

    [Theory]
    [MemberData(nameof(UnsupportedCommentPlaceholders))]
    public void UnsupportedCommentPlaceholdersRecordUnsupportedKind(
        IrNode node,
        string expectedText)
    {
        var block = new Block(0);
        block.Add(node);
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        CSharpPrinter.Print(function, out var ranges);

        Assert.True(ranges.TryGetRange(node, out var range));
        Assert.Equal(expectedText, ranges.Output[range]);
        var map = PrintedBodyMap.Create(ranges);
        Assert.Contains(
            map.Nodes,
            candidate => candidate.Kind == "UnsupportedExpression"
                && Text(map, candidate.Extent) == expectedText.TrimEnd());
        Assert.DoesNotContain(
            map.Nodes,
            candidate => candidate.Kind == "ExpressionStatement"
                && Text(map, candidate.Extent) == expectedText.TrimEnd());
    }

    public static TheoryData<IrNode, string> UnsupportedCommentPlaceholders =>
        new()
        {
            { new EndFinally(), "// endfinally\n" },
            {
                new EndFilter(new Constant(1, TypeRef.CoreLib("System", "Int32"))),
                "// endfilter(1)\n"
            },
            {
                new CopyBlock(
                    new Constant(0, TypeRef.CoreLib("System", "Int32")),
                    new Constant(0, TypeRef.CoreLib("System", "Int32")),
                    new Constant(1, TypeRef.CoreLib("System", "Int32"))),
                "/* unsupported cpblk */\n"
            },
            {
                new ExpressionStatement(
                    new UnsupportedNode(0x05, "calli", "unsupported call site")),
                "/* Unsupported IL_0005 calli: unsupported call site */\n"
            },
        };

    [Fact]
    public void NestedUnsupportedStatementCanonicalizesWrapperAndExpression()
    {
        var unsupported = new UnsupportedNode(0x05, "calli", "unsupported call site");
        var statement = new ExpressionStatement(unsupported);
        var nested = new Block();
        nested.Add(statement);
        var block = new Block(0);
        block.Add(new IfStatement(
            new Constant(true, TypeRef.CoreLib("System", "Boolean")),
            nested,
            null));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        CSharpPrinter.Print(function, out var ranges);
        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [statement] = [new Annotation(Alloc, 0, "wrapper")],
                [unsupported] = [new Annotation(Alloc, 0, "expression")],
            });

        var node = Assert.Single(
            map.Nodes,
            candidate => candidate.Kind == "UnsupportedExpression");
        Assert.Equal(
            "/* Unsupported IL_0005 calli: unsupported call site */",
            Text(map, node.Extent));
        Assert.Equal(2, map.Annotations.Count);
        Assert.All(map.Annotations, annotation => Assert.Equal(node.Id, annotation.NodeId));
    }

    [Fact]
    public void EndFilterCommentDoesNotPublishOperandSyntax()
    {
        var value = new LoadStackSlot(6, TypeRef.CoreLib("System", "Int32"));
        var endFilter = new EndFilter(value);
        var block = new Block(0);
        block.Add(endFilter);
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        CSharpPrinter.Print(function, out var ranges);
        var map = PrintedBodyMap.Create(ranges);

        Assert.Contains(
            map.Nodes,
            node => node.Kind == "UnsupportedExpression"
                && Text(map, node.Extent) == "// endfilter(S_6)");
        Assert.DoesNotContain(
            map.Nodes,
            node => node.Kind == "NameExpression"
                && Text(map, node.Extent) == "S_6");
    }

    [Theory]
    [MemberData(nameof(NonFiniteConstants))]
    public void NonFiniteConstantsRecordMemberAccessKind(
        Constant constant,
        string expectedText)
    {
        var block = new Block(0);
        block.Add(new Return(constant));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                constant.Type!,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        CSharpPrinter.Print(function, out var ranges);

        AssertSurfaceKind(ranges, constant, expectedText, "MemberAccessExpression");
    }

    public static TheoryData<Constant, string> NonFiniteConstants =>
        new()
        {
            {
                new Constant(
                    float.NaN,
                    TypeRef.CoreLib("System", "Single")),
                "float.NaN"
            },
            {
                new Constant(
                    float.PositiveInfinity,
                    TypeRef.CoreLib("System", "Single")),
                "float.PositiveInfinity"
            },
            {
                new Constant(
                    float.NegativeInfinity,
                    TypeRef.CoreLib("System", "Single")),
                "float.NegativeInfinity"
            },
            {
                new Constant(
                    double.NaN,
                    TypeRef.CoreLib("System", "Double")),
                "double.NaN"
            },
            {
                new Constant(
                    double.PositiveInfinity,
                    TypeRef.CoreLib("System", "Double")),
                "double.PositiveInfinity"
            },
            {
                new Constant(
                    double.NegativeInfinity,
                    TypeRef.CoreLib("System", "Double")),
                "double.NegativeInfinity"
            },
        };

    [Theory]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.NegateSum), "-(a + b)", "UnaryExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.NegateSum), "a + b", "BinaryExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.MoneyToInt), "(int)m", "ConversionExpression")]
    [InlineData(typeof(GenericIsInstanceSpecimens<>), nameof(GenericIsInstanceSpecimens<object>.DirectIs), "value is T", "PatternExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.IsNotNullReference), "o is not null", "PatternExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.FloatUnordered), "!(a <= b)", "UnaryExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.ConstantUIntSpan), "new uint[] { 1, 10, 100, 1000, 10000 }", "ArrayCreationExpression")]
    [InlineData(typeof(CfgSampleClass), nameof(CfgSampleClass.AsWithoutPattern), "o as string", "ConversionExpression")]
    [InlineData(typeof(LifetimeSampleClass), nameof(LifetimeSampleClass.EscapingStackPointer), "return (int*)__stackalloc;", "ReturnStatement")]
    [InlineData(typeof(RectangularArraySamples), nameof(RectangularArraySamples.MdGet), "a[i, j]", "ElementAccessExpression")]
    [InlineData(typeof(RectangularArraySamples), nameof(RectangularArraySamples.MdSet), "a[i, j] = v", "AssignmentStatement")]
    [InlineData(typeof(RectangularArraySamples), nameof(RectangularArraySamples.MdNew), "new int[3, 4]", "ArrayCreationExpression")]
    public void RenderSpecializationsRecordTheirSurfaceKind(
        Type fixtureType,
        string methodName,
        string text,
        string expectedKind)
    {
        var (_, ranges) = Print(fixtureType, methodName);
        var map = PrintedBodyMap.Create(ranges);

        Assert.Contains(map.Nodes, node => node.Kind == expectedKind && Text(map, node.Extent) == text);
        Assert.DoesNotContain(
            map.Nodes,
            node => node.Kind is "InvocationExpression" or "ObjectCreationExpression"
                && Text(map, node.Extent) == text);
    }

    [Fact]
    public void RectangularArrayPseudoMemberReadRecordsInvocationKind()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var arrayType = TypeRef.MdArray(intType, 2);
        var tupleType = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "ValueTuple`2"),
            [intType, intType]);
        var load = new LoadElement(
            intType,
            new LoadArgument(0, "a", arrayType),
            new TupleExpression(
                tupleType,
                [new LoadStackSlot(0, intType), new LoadStackSlot(0, intType)]));
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Constant(0, intType)));
        block.Add(new Return(load));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                intType,
                [new Parameter("a", arrayType)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        CSharpPrinter.Print(function, out var ranges);

        AssertSurfaceKind(ranges, load, "a.Get(S_0, S_0)", "InvocationExpression");
        var map = PrintedBodyMap.Create(ranges);
        Assert.DoesNotContain(
            map.Nodes,
            node => node.Kind == "ElementAccessExpression"
                && Text(map, node.Extent) == "a.Get(S_0, S_0)");
    }

    [Fact]
    public void ContextRenderedSwitchRecordsSwitchAndArmKinds()
    {
        var (_, ranges) = Print(
            typeof(CfgSampleClass),
            nameof(CfgSampleClass.PowerOfTwo));
        var map = PrintedBodyMap.Create(ranges);

        var switchNode = Assert.Single(
            map.Nodes,
            node => node.Kind == "SwitchExpression");
        Assert.Equal(
            """
            x switch
            {
                0 => 1,
                1 => 2,
                2 => 4,
                3 => 8,
                _ => 0,
            }
            """,
            Text(map, switchNode.Extent));
        Assert.Equal(
            ["0 => 1", "1 => 2", "2 => 4", "3 => 8", "_ => 0"],
            map.Nodes
                .Where(node => node.Kind == "SwitchExpressionArm")
                .Select(node => Text(map, node.Extent)));
    }

    [Fact]
    public void TargetCoercedInlineSwitchRecordsRootAndFactJoin()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var switchExpression = new SwitchExpression(
            new LoadArgument(0, "x", intType),
            [
                new SwitchExpressionArm(
                    [0],
                    isDefault: false,
                    new Constant(1, intType)),
                new SwitchExpressionArm(
                    [],
                    isDefault: true,
                    new Constant(2, intType)),
            ]);
        var block = new Block(0);
        block.Add(new StoreLocal(0, intType, switchExpression));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("x", intType)],
                HasThis: false,
                GenericParameterCount: 0),
            [intType],
            container);

        CSharpPrinter.Print(function, out var ranges);
        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [switchExpression] = [new Annotation(Alloc, 0)],
            });

        var node = Assert.Single(
            map.Nodes,
            candidate => candidate.Kind == "SwitchExpression");
        Assert.Equal(
            "x switch { 0 => 1, _ => 2 }",
            Text(map, node.Extent));
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(node.Id, fact.NodeId);
        Assert.Equal(node.Kind, fact.Kind);
        Assert.Equal(node.Extent, fact.Extent);
    }

    [Fact]
    public void PatternSwitchRecordsSynthesizedDefaultArm()
    {
        using var source = MetadataSource.Open(typeof(PatternSwitchSample).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(PatternSwitchSample).FullName!,
            nameof(PatternSwitchSample.Classify));
        CSharpPrinter.PrintRaised(
            function!,
            out var ranges,
            method => IrImporter.Import(source, method),
            source.AreProvablyDisjoint);
        var synthesizedDefault = Assert.Single(
            function!.Descendants.OfType<SynthesizedSwitchExpressionArm>());
        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [synthesizedDefault] = [new Annotation(Alloc, 0)],
            });

        var arms = map.Nodes
            .Where(node => node.Kind == "SwitchExpressionArm")
            .Select(node => Text(map, node.Extent))
            .ToArray();
        Assert.Equal(3, arms.Length);
        Assert.Contains("_ => false", arms);
        var fact = Assert.Single(map.Annotations);
        Assert.Equal("SwitchExpressionArm", fact.Kind);
        Assert.True(fact.Extent.HasValue);
        Assert.Equal("_ => false", Text(map, fact.Extent.Value));
    }

    [Fact]
    public void ContextRenderedConditionRecordsPatternKind()
    {
        var (_, ranges) = Print(
            typeof(CfgSampleClass),
            nameof(CfgSampleClass.LenOrZero));
        var map = PrintedBodyMap.Create(ranges);

        Assert.Contains(
            map.Nodes,
            node => node.Kind == "PatternExpression"
                && Text(map, node.Extent) == "o is string s");
    }

    [Fact]
    public void InvertedNullConditionRecordsPatternKind()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var condition = new LogicalNot(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(0, "o", objectType),
                new Constant(null, objectType)));
        var thenBlock = new Block();
        thenBlock.Add(new Return(null));
        var block = new Block(0);
        block.Add(new IfStatement(condition, thenBlock, null));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("o", objectType)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        CSharpPrinter.Print(function, out var ranges);
        var map = PrintedBodyMap.Create(ranges);

        Assert.Contains(
            map.Nodes,
            node => node.Kind == "PatternExpression"
                && Text(map, node.Extent) == "o is not null");
        Assert.DoesNotContain(
            map.Nodes,
            node => node.Kind == "BinaryExpression"
                && Text(map, node.Extent) == "o is not null");
    }

    [Fact]
    public void SynthesizedDiscardRecordsAssignmentKind()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var statement = new ExpressionStatement(
            new ArrayLength(
                new NewArray(
                    byteType,
                    new Constant(4, intType))));
        var block = new Block(0);
        block.Add(statement);
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        CSharpPrinter.Print(function, out var ranges);

        AssertSurfaceKind(
            ranges,
            statement,
            "_ = (new byte[4]).Length;\n",
            "AssignmentStatement");
        var map = PrintedBodyMap.Create(ranges);
        Assert.DoesNotContain(
            map.Nodes,
            node => node.Kind == "ExpressionStatement"
                && Text(map, node.Extent) == "_ = (new byte[4]).Length;");
    }

    [Fact]
    public void TargetCoercedConditionalRecordsVisibleRootKind()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var diamond = new Conditional(
            new LoadArgument(0, "exists", boolType),
            new Constant(true, boolType),
            new Constant(false, boolType))
        {
            MergedType = boolType,
        };
        var block = new Block(0);
        block.Add(new StoreLocal(0, intType, diamond));
        block.Add(new Return(new LoadLocal(0, intType)));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                intType,
                [new Parameter("exists", boolType)],
                HasThis: false,
                GenericParameterCount: 0),
            [intType],
            container);

        var result = CSharpPrinter.Print(function, out var ranges);
        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [diamond] = [new Annotation(Alloc, 0)],
            });

        Assert.NotNull(result.Output);
        Assert.True(ranges.TryGetRange(diamond, out var range));
        Assert.Equal("exists && true", ranges.Output[range]);
        Assert.True(ranges.TryGetNodeKind(diamond, out string? kind));
        Assert.Equal("BinaryExpression", kind);
        var wrapper = Assert.Single(
            map.Nodes,
            node => node.Kind == "ConditionalExpression"
                && Text(map, node.Extent) == "exists && true ? 1 : 0");
        var operand = Assert.Single(
            map.Nodes,
            node => node.Kind == "BinaryExpression"
                && Text(map, node.Extent) == "exists && true");
        Assert.True(
            SlotOf(ranges, diamond)
            < SlotOf(ranges, ContextualRoot(
                ranges,
                "exists && true ? 1 : 0",
                "ConditionalExpression")));
        var fact = Assert.Single(map.Annotations);
        Assert.Equal("BinaryExpression", fact.Kind);
        Assert.True(fact.Extent.HasValue);
        Assert.Equal("exists && true", Text(map, fact.Extent.Value));
    }

    [Fact]
    public void ContextualTruthinessPreservesOperandAndWrapperKinds()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var argument = new LoadArgument(0, "value", objectType);
        var thenBlock = new Block();
        thenBlock.Add(new Return(null));
        var block = new Block(0);
        block.Add(new IfStatement(argument, thenBlock, null));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("value", objectType)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        CSharpPrinter.Print(function, out var ranges);
        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [argument] = [new Annotation(Alloc, 0)],
            });

        var operand = Assert.Single(
            map.Nodes,
            node => node.Kind == "NameExpression"
                && Text(map, node.Extent) == "value");
        var wrapper = Assert.Single(
            map.Nodes,
            node => node.Kind == "PatternExpression"
                && Text(map, node.Extent) == "value is not null");
        Assert.True(
            SlotOf(ranges, argument)
            < SlotOf(ranges, ContextualRoot(
                ranges,
                "value is not null",
                "PatternExpression")));
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(operand.Id, fact.NodeId);
        Assert.Equal("NameExpression", fact.Kind);
    }

    [Fact]
    public void AddressStrippedOperatorTruthinessRecordsNameKind()
    {
        using var source = MetadataSource.Open(typeof(BoolBoxProbe).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(BoolBoxProbe).FullName!,
            nameof(BoolBoxProbe.Branch));
        Assert.NotNull(function);
        CSharpPrinter.PrintRaised(function!, out var ranges);
        var address = Assert.Single(
            function!.Descendants.OfType<LoadArgumentAddress>());
        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [address] = [new Annotation(Alloc, 0)],
            });

        var name = Assert.Single(
            map.Nodes,
            node => node.Kind == "NameExpression"
                && Text(map, node.Extent) == "value");
        Assert.DoesNotContain(
            map.Nodes,
            node => node.Kind == "AddressExpression"
                && Text(map, node.Extent) == "value");
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(name.Id, fact.NodeId);
        Assert.Equal("NameExpression", fact.Kind);
    }

    [Fact]
    public void ContextualStackallocConversionPreservesOperandAndWrapperKinds()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var intPointer = TypeRef.Pointer(intType);
        var voidPointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Void"));
        var allocation = new StackAllocArray(
            intType,
            new Constant(1, intType),
            intPointer);
        var block = new Block(0);
        block.Add(new Return(allocation));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                voidPointer,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        CSharpPrinter.Print(function, out var ranges);
        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [allocation] = [new Annotation(Alloc, 0)],
            });

        var operand = Assert.Single(
            map.Nodes,
            node => node.Kind == "StackAllocationExpression");
        Assert.Equal("stackalloc int[1]", Text(map, operand.Extent));
        var wrapper = Assert.Single(
            map.Nodes,
            node => node.Kind == "ConversionExpression"
                && Text(map, node.Extent) == "(void*)(stackalloc int[1])");
        Assert.True(
            SlotOf(ranges, allocation)
            < SlotOf(ranges, ContextualRoot(
                ranges,
                "(void*)(stackalloc int[1])",
                "ConversionExpression")));
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(operand.Id, fact.NodeId);
        Assert.Equal("StackAllocationExpression", fact.Kind);
    }

    [Fact]
    public void CoercedJoinArmPreservesLiteralAndConversionKinds()
    {
        using var source = MetadataSource.Open(typeof(EnumCastSamples).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(EnumCastSamples).FullName!,
            nameof(EnumCastSamples.CrossSignCoalesceConstant));
        Assert.NotNull(function);
        CSharpPrinter.PrintRaised(function!, out var ranges);
        var fallback = Assert.Single(
            function!.Descendants.OfType<Constant>(),
            constant => constant.Value is int value && value == -1);
        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [fallback] = [new Annotation(Alloc, 0)],
            });

        var literal = Assert.Single(
            map.Nodes,
            node => node.Kind == "LiteralExpression"
                && Text(map, node.Extent) == "-1");
        var conversion = Assert.Single(
            map.Nodes,
            node => node.Kind == "ConversionExpression"
                && Text(map, node.Extent) == "unchecked((uint)(-1))");
        Assert.True(
            SlotOf(ranges, fallback)
            < SlotOf(ranges, ContextualRoot(
                ranges,
                "unchecked((uint)(-1))",
                "ConversionExpression")));
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(literal.Id, fact.NodeId);
        Assert.Equal("LiteralExpression", fact.Kind);
    }

    [Fact]
    public void ContextualUnsignedComparisonCastPreservesOperandAndWrapperKinds()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var intType = TypeRef.CoreLib("System", "Int32");
        var zero = new Constant(0, intType);
        var comparison = new Comparison(
            ComparisonKind.GreaterThan,
            isUnsigned: true,
            new LoadArgument(0, "value", byteType),
            zero);
        var block = new Block(0);
        block.Add(new Return(comparison));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                boolType,
                [new Parameter("value", byteType)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        CSharpPrinter.Print(function, out var ranges);
        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [zero] = [new Annotation(Alloc, 0)],
            });

        var literal = Assert.Single(
            map.Nodes,
            node => node.Kind == "LiteralExpression"
                && Text(map, node.Extent) == "0");
        var conversion = Assert.Single(
            map.Nodes,
            node => node.Kind == "ConversionExpression"
                && Text(map, node.Extent) == "(uint)0");
        Assert.True(
            SlotOf(ranges, zero)
            < SlotOf(ranges, ContextualRoot(
                ranges,
                "(uint)0",
                "ConversionExpression")));
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(literal.Id, fact.NodeId);
        Assert.Equal("LiteralExpression", fact.Kind);
    }

    [Fact]
    public void FixedBufferPointerAddressPreservesElementAndAddressKinds()
    {
        using var source = MetadataSource.Open(
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals).FullName!,
            nameof(ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals.PointerReturn));
        Assert.NotNull(function);
        CSharpPrinter.PrintRaised(function!, out var ranges);
        var address = Assert.Single(
            function!.Descendants.OfType<FixedBufferElementAddress>());
        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [address] = [new Annotation(Alloc, 0)],
            });

        var element = Assert.Single(
            map.Nodes,
            node => node.Kind == "ElementAccessExpression"
                && Text(map, node.Extent) == "value.Data[index]");
        var addressOf = Assert.Single(
            map.Nodes,
            node => node.Kind == "AddressExpression"
                && Text(map, node.Extent) == "&value.Data[index]");
        Assert.True(element.Extent.StartColumn > addressOf.Extent.StartColumn);
        Assert.Equal(element.Extent.EndColumn, addressOf.Extent.EndColumn);
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(element.Id, fact.NodeId);
        Assert.Equal("ElementAccessExpression", fact.Kind);
    }

    [Fact]
    public void IntegerTruthinessContainingPatternTextRecordsBinaryKind()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var objectType = TypeRef.CoreLib("System", "Object");
        var stringType = TypeRef.CoreLib("System", "String");
        var typeTest = new Comparison(
            ComparisonKind.NotEqual,
            isUnsigned: false,
            new IsInstance(
                stringType,
                new LoadArgument(0, "value", objectType)),
            new Constant(null, objectType));
        var integerConditional = new Conditional(
            typeTest,
            new Constant(1, intType),
            new Constant(0, intType))
        {
            MergedType = intType,
        };
        var negated = new LogicalNot(integerConditional);
        var block = new Block(0);
        block.Add(new Return(negated));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                boolType,
                [new Parameter("value", objectType)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        var result = CSharpPrinter.Print(function, out var ranges);

        Assert.NotNull(result.Output);
        Assert.True(ranges.TryGetRange(negated, out var range));
        Assert.Equal("(value is string ? 1 : 0) == 0", ranges.Output[range]);
        Assert.True(ranges.TryGetNodeKind(negated, out string? kind));
        Assert.Equal("BinaryExpression", kind);
    }

    [Fact]
    public void TransparentWrappersRecordTheSyntaxTheyExpose()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var objectType = TypeRef.CoreLib("System", "Object");
        var stringType = TypeRef.CoreLib("System", "String");
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var pointerType = TypeRef.Pointer(intType);
        var holderType = TypeRef.Definition("synthetic", "", "Holder");
        var enumType = TypeRef.Definition("synthetic", "", "Mode");
        var nullableIntType = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Nullable`1"),
            [intType]);
        var boxed = new Box(
            intType,
            new LoadArgument(0, "value", intType));
        var coerced = new Coerce(
            intType,
            new LoadArgument(0, "value", intType));
        var managedRead = new LoadIndirect(
            intType,
            new LoadArgument(1, "managed", TypeRef.ByRef(intType)));
        var pointerRead = new LoadIndirect(
            intType,
            new LoadArgument(2, "pointer", TypeRef.Pointer(intType)));
        var conditional = new Coerce(
            intType,
            new Conditional(
                new LoadArgument(3, "flag", boolType),
                new Constant(1, intType),
                new Constant(0, intType))
            {
                MergedType = intType,
            });
        var typeTest = new IsInstance(
            intType,
            new LoadArgument(4, "subject", objectType));
        var coalesced = new Coerce(
            intType,
            new Coalesce(
                new LoadArgument(5, "optional", nullableIntType),
                new Constant(4, intType)));
        var collapsedConstant = new Coerce(
            uintType,
            new ILInspector.Decompiler.Pipeline.Convert(
                intType,
                isChecked: false,
                isUnsigned: false,
                new Constant(1, intType)));
        var binary = new Coerce(
            uintType,
            new Binary(
                BinaryKind.Remainder,
                isChecked: false,
                isUnsigned: true,
                new ILInspector.Decompiler.Pipeline.Convert(
                    uintType,
                    isChecked: false,
                    isUnsigned: false,
                    new LoadArgument(6, "signed", intType)),
                new ILInspector.Decompiler.Pipeline.Convert(
                    uintType,
                    isChecked: false,
                    isUnsigned: false,
                    new Constant(32, intType))));
        var pointerArithmetic = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(2, "pointer", pointerType),
            new LoadArgument(0, "value", intType));
        var enumConstant = new Constant(3, enumType);
        var namedEnumSink = new Coerce(
            enumType,
            new Constant(1, intType));
        var namedEnumOperand = new Constant(1, enumType);
        var enumComparison = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadLocal(11, enumType),
            namedEnumOperand);
        var increment = new StoreLocal(
            1,
            intType,
            new Binary(
                BinaryKind.Add,
                isChecked: false,
                isUnsigned: false,
                new LoadLocal(1, intType),
                new Constant(1, intType)));
        var checkedIncrement = new StoreLocal(
            1,
            intType,
            new Binary(
                BinaryKind.Add,
                isChecked: true,
                isUnsigned: false,
                new LoadLocal(1, intType),
                new Constant(1, intType)));
        var checkedOperator = new IncrementDecrement(
            new LoadLocal(1, intType),
            isIncrement: true,
            isPrefix: false,
            isUserDefined: true,
            isChecked: true);
        var checkedOperatorStatement = new ExpressionStatement(checkedOperator);
        var fieldLoad = new LoadField(
            new FieldRef(holderType, "Count", intType),
            new LoadArgument(0, "this", holderType));
        var fieldAddressRead = new LoadIndirect(
            intType,
            new LoadFieldAddress(
                new FieldRef(holderType, "Count", intType),
                new LoadArgument(0, "this", holderType)));
        var propertyLoad = new LoadProperty(
            new MethodRef(holderType, "get_Total", intType, [], HasThis: true),
            new LoadArgument(0, "this", holderType),
            []);
        var functionPointer = new LoadFunctionPointer(
            new MethodRef(holderType, "Target", intType, [], HasThis: false),
            isVirtual: false,
            instance: null);
        var pattern = new IsPattern(
            new LoadArgument(4, "subject", objectType),
            stringType,
            localIndex: 8);
        var lengthGetter = new MethodRef(
            stringType,
            "get_Length",
            intType,
            [],
            HasThis: true);
        var foldedPattern = new Conditional(
            pattern,
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadProperty(
                    lengthGetter,
                    new LoadLocal(8, stringType),
                    []),
                new Constant(5, intType)),
            new Constant(false, boolType))
        {
            MergedType = boolType,
        };
        var block = new Block(0);
        block.Add(new StoreLocal(0, objectType, boxed));
        block.Add(new StoreLocal(1, intType, coerced));
        block.Add(new StoreLocal(2, intType, pointerRead));
        block.Add(new StoreLocal(3, intType, conditional));
        block.Add(new StoreLocal(4, boolType, typeTest));
        block.Add(new StoreLocal(5, intType, coalesced));
        block.Add(new StoreLocal(6, uintType, collapsedConstant));
        block.Add(new StoreLocal(7, uintType, binary));
        block.Add(new StoreLocal(9, boolType, foldedPattern));
        block.Add(new StoreLocal(10, pointerType, pointerArithmetic));
        block.Add(new StoreLocal(11, enumType, enumConstant));
        block.Add(new StoreLocal(12, enumType, namedEnumSink));
        block.Add(new StoreLocal(13, boolType, enumComparison));
        block.Add(increment);
        block.Add(checkedIncrement);
        block.Add(checkedOperatorStatement);
        block.Add(new StoreLocal(14, intType, fieldLoad));
        block.Add(new StoreLocal(15, intType, propertyLoad));
        block.Add(new StoreLocal(16, intType, fieldAddressRead));
        block.Add(new ExpressionStatement(functionPointer));
        block.Add(new Return(managedRead));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            holderType,
            new MethodSignature(
                intType,
                [
                    new Parameter("value", intType),
                    new Parameter("managed", TypeRef.ByRef(intType)),
                    new Parameter("pointer", pointerType),
                    new Parameter("flag", boolType),
                    new Parameter("subject", objectType),
                    new Parameter("optional", nullableIntType),
                    new Parameter("signed", intType),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [
                objectType,
                intType,
                intType,
                intType,
                boolType,
                intType,
                uintType,
                uintType,
                stringType,
                boolType,
                pointerType,
                enumType,
                enumType,
                boolType,
                intType,
                intType,
                intType,
            ],
            container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape>
            {
                [enumType] = TypeShape.Enum,
            },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef>
            {
                [enumType] = intType,
            },
            EnumMembers = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
            {
                [enumType] = new Dictionary<long, string>
                {
                    [1] = "Enabled",
                },
            },
        };

        var result = CSharpPrinter.Print(function, out var ranges);

        Assert.NotNull(result.Output);
        AssertSurfaceKind(ranges, boxed, "value", "NameExpression");
        AssertSurfaceKind(ranges, coerced, "value", "NameExpression");
        AssertSurfaceKind(ranges, managedRead, "managed", "NameExpression");
        AssertSurfaceKind(ranges, pointerRead, "*pointer", "IndirectAccessExpression");
        AssertSurfaceKind(ranges, conditional, "flag ? 1 : 0", "ConditionalExpression");
        AssertSurfaceKind(ranges, typeTest, "subject is int", "PatternExpression");
        AssertSurfaceKind(ranges, coalesced, "optional ?? 4", "CoalesceExpression");
        AssertSurfaceKind(ranges, collapsedConstant, "1", "LiteralExpression");
        AssertSurfaceKind(ranges, binary, "((uint)signed) % ((uint)32)", "BinaryExpression");
        AssertSurfaceKind(ranges, foldedPattern, "subject is string { Length: 5 }", "PatternExpression");
        AssertSurfaceKind(ranges, pointerArithmetic, "(int*)((byte*)pointer + value)", "ConversionExpression");
        AssertSurfaceKind(ranges, enumConstant, "(Mode)3", "ConversionExpression");
        AssertSurfaceKind(ranges, namedEnumSink, "Mode.Enabled", "MemberAccessExpression");
        AssertSurfaceKind(ranges, namedEnumOperand, "Mode.Enabled", "MemberAccessExpression");
        AssertSurfaceKind(ranges, increment, "V_1++;\n", "IncrementOrDecrementExpression");
        AssertSurfaceKind(ranges, checkedIncrement, "checked { V_1++; }\n", "CheckedStatement");
        AssertSurfaceKind(ranges, checkedOperatorStatement, "checked { V_1++; }\n", "CheckedStatement");
        AssertSurfaceKind(ranges, checkedOperator, "V_1++", "IncrementOrDecrementExpression");
        AssertSurfaceKind(ranges, fieldLoad, "Count", "NameExpression");
        AssertSurfaceKind(ranges, propertyLoad, "Total", "NameExpression");
        AssertSurfaceKind(ranges, fieldAddressRead, "Count", "NameExpression");
        Assert.True(ranges.TryGetRange(functionPointer, out var functionPointerRange));
        Assert.StartsWith("/* LoadFunctionPointer", ranges.Output[functionPointerRange]);
        Assert.True(ranges.TryGetNodeKind(functionPointer, out string? functionPointerKind));
        Assert.Equal("UnsupportedExpression", functionPointerKind);
    }

    [Fact]
    public void UnplacedFactKeepsPrinterSelectedKind()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var node = new Box(
            intType,
            new LoadArgument(0, "value", intType));
        var ranges = new PrintedRangeMap();
        ranges.SetNodeKind(node, "NameExpression");
        ranges.Complete("");

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [node] = [new Annotation(Alloc, 0)],
            });

        var fact = Assert.Single(map.Annotations);
        Assert.Null(fact.NodeId);
        Assert.Null(fact.Extent);
        Assert.Equal("NameExpression", fact.Kind);
    }

    [Fact]
    public void SynthesizedStackallocDeclarationsStayOutsideStatementRanges()
    {
        static int SlotOf(PrintedRangeMap map, IrNode node)
            => map.Select((range, index) => (range.Node, index))
                .Single(x => ReferenceEquals(x.Node, node))
                .index;

        var intType = TypeRef.CoreLib("System", "Int32");
        var pointerType = TypeRef.Pointer(intType);
        var allocation = new StackAllocate(new Constant(16, intType));
        var store = new StoreLocal(0, pointerType, allocation);
        var block = new Block(0);
        block.Add(store);
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [pointerType],
            container);

        var result = CSharpPrinter.Print(function, out var ranges);

        Assert.NotNull(result.Output);
        Assert.True(ranges.TryGetRange(store, out var storeRange));
        Assert.Equal("int* V_0 = (int*)__stackalloc;\n", ranges.Output[storeRange]);
        Assert.True(ranges.TryGetRange(allocation, out var allocationRange));
        Assert.Equal("stackalloc byte[16]", ranges.Output[allocationRange]);
        Assert.True(SlotOf(ranges, allocation) < SlotOf(ranges, store));
        Assert.True(ranges.TryGetLine(allocation, out int allocationLine));
        Assert.True(ranges.TryGetLine(store, out int storeAnchorLine));
        Assert.True(ranges.TryGetLineColumn(store, out int storeSyntaxLine, out _, out _));
        Assert.Equal(allocationLine, storeAnchorLine);
        Assert.NotEqual(storeAnchorLine, storeSyntaxLine);

        using var source = MetadataSource.Open(typeof(UnsafeSampleClass).Assembly.Location);
        var imported = IrImporter.Import(
            source,
            typeof(UnsafeSampleClass).FullName!,
            nameof(UnsafeSampleClass.StackScratch));
        Assert.NotNull(imported);
        CSharpPrinter.PrintRaised(imported!, out var importedRanges);
        var slotStore = Assert.Single(
            imported!.Descendants.OfType<StoreStackSlot>(),
            candidate => candidate.Value is StackAllocate);
        var slotAllocation = Assert.IsType<StackAllocate>(slotStore.Value);

        Assert.True(importedRanges.TryGetRange(slotStore, out var slotRange));
        Assert.Equal("byte* S_256 = __stackalloc;\n", importedRanges.Output[slotRange]);
        Assert.True(SlotOf(importedRanges, slotAllocation) < SlotOf(importedRanges, slotStore));
        Assert.True(importedRanges.TryGetLine(slotAllocation, out int slotAllocationLine));
        Assert.True(importedRanges.TryGetLine(slotStore, out int slotAnchorLine));
        Assert.True(importedRanges.TryGetLineColumn(slotStore, out int slotSyntaxLine, out _, out _));
        Assert.Equal(slotAllocationLine, slotAnchorLine);
        Assert.NotEqual(slotAnchorLine, slotSyntaxLine);

        using var returnSource = MetadataSource.Open(typeof(LifetimeSampleClass).Assembly.Location);
        var returnFunction = IrImporter.Import(
            returnSource,
            typeof(LifetimeSampleClass).FullName!,
            nameof(LifetimeSampleClass.EscapingStackPointer));
        Assert.NotNull(returnFunction);
        CSharpPrinter.PrintRaised(returnFunction!, out var returnRanges);
        var returnStatement = Assert.Single(
            returnFunction!.Descendants.OfType<Return>(),
            candidate => candidate.Value is StackAllocate);
        var returnAllocation = Assert.IsType<StackAllocate>(returnStatement.Value);

        Assert.True(returnRanges.TryGetRange(returnStatement, out var returnRange));
        Assert.Equal("return (int*)__stackalloc;\n", returnRanges.Output[returnRange]);
        Assert.True(SlotOf(returnRanges, returnAllocation) < SlotOf(returnRanges, returnStatement));
        Assert.True(returnRanges.TryGetLine(returnAllocation, out int returnAllocationLine));
        Assert.True(returnRanges.TryGetLine(returnStatement, out int returnAnchorLine));
        Assert.True(returnRanges.TryGetLineColumn(returnStatement, out int returnSyntaxLine, out _, out _));
        Assert.Equal(returnAllocationLine, returnAnchorLine);
        Assert.NotEqual(returnAnchorLine, returnSyntaxLine);
    }

    [Fact]
    public void RenderSpecializationKeepsPlacedFactAndNodeKindsEqual()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(CfgSampleClass).FullName!,
            nameof(CfgSampleClass.NegateSum));
        Assert.NotNull(function);
        CSharpPrinter.PrintRaised(function!, out var ranges);
        var addition = Assert.Single(
            function!.Descendants.OfType<Call>(),
            call => AnnotatedSourceNodeKindProjection.OperatorKind(call) == "BinaryExpression");

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [addition] = [new Annotation(Alloc, addition.SourceOffset)],
            });

        var fact = Assert.Single(map.Annotations);
        var node = map.Nodes[Assert.IsType<int>(fact.NodeId)];
        Assert.Equal("BinaryExpression", fact.Kind);
        Assert.Equal(node.Kind, fact.Kind);
        Assert.Equal(node.Extent, fact.Extent);
    }

    [Fact]
    public void NodeIdsAreContiguousAndCanonicallyOrdered()
    {
        // Ids are the whole join. PrintedRangeMap only promises descendants
        // before ancestors, so ids cut from emission order would be reproducible
        // by accident; the canonical order is what makes them a contract.
        var (_, ranges) = Print(nameof(AllocSampleClass.SumList));
        var map = PrintedBodyMap.Create(ranges);

        Assert.NotEmpty(map.Nodes);
        Assert.Equal(Enumerable.Range(0, map.Nodes.Count), map.Nodes.Select(node => node.Id));
        for (int i = 1; i < map.Nodes.Count; i++)
        {
            var previous = map.Nodes[i - 1].Extent;
            var current = map.Nodes[i].Extent;
            Assert.True(
                ComparePosition(previous.StartLine, previous.StartColumn, current.StartLine, current.StartColumn) <= 0,
                "Node extents must be ordered by start position.");
        }
    }

    [Fact]
    public void AFactOnARefusedNodeRemainsPresentAndExplicitlyUnplaced()
    {
        // TwiceTheSameOnALaterLine prints "return y + y;" on a line after the
        // first, so the LoadLocal spelling is ambiguous and the printer
        // deliberately records no range for it. Facts are positive-only --
        // always shown somewhere -- so a fact keyed to that node must still be
        // present, but inheriting an ancestor coordinate would claim characters
        // the fact's node did not establish.
        var source = MetadataSource.Open(typeof(PrintedRangeExpressionFixture).Assembly.Location);
        var fn = IrImporter.Import(source, typeof(PrintedRangeExpressionFixture).FullName!, nameof(PrintedRangeExpressionFixture.TwiceTheSameOnALaterLine))!;
        CSharpPrinter.PrintRaised(fn, out var ranges);

        var refused = fn.Body.Descendants.OfType<LoadLocal>()
            .FirstOrDefault(n => !ranges.TryGetRange(n, out _));
        Assert.NotNull(refused);

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>> { [refused!] = [new Annotation(Alloc, 0, "kept")] });

        var fact = Assert.Single(map.Annotations);
        Assert.Equal("kept", fact.Detail);

        Assert.Null(fact.Extent);
        Assert.Null(fact.NodeId);
    }

    [Fact]
    public void ConditionalityReachesTheEnvelope_SoAReplayRendersTheSameLabel()
    {
        // AnnotationText appends "cached-once" / "per-iteration" to the rendered
        // label, so a payload that dropped conditionality would render a
        // *different* annotation than the in-process renderer -- silently
        // promoting a cached allocation to an unconditional one.
        var (_, ranges) = Print(nameof(AllocSampleClass.SumList));
        var statement = ranges[^1].Node;

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
            {
                [statement] = [new Annotation(Alloc, 0, "cached", AnnotationConditionality.CachedOnce)],
            });

        var fact = Assert.Single(map.Annotations);
        Assert.Equal(AnnotationConditionality.CachedOnce, fact.Conditionality);

        string json = JsonSerializer.Serialize(map);
        var replayed = JsonSerializer.Deserialize<PrintedBodyMap>(json);
        Assert.Equal(
            AnnotationConditionality.CachedOnce,
            Assert.Single(replayed!.Annotations).Conditionality);
    }

    [Fact]
    public void MapCarriesNoReferenceIntoTheIr()
    {
        // If any member could hand back an IrNode the payload would silently
        // re-acquire the lifetime it exists to shed.
        var (_, ranges) = Print(nameof(AllocSampleClass.SumList));
        var map = PrintedBodyMap.Create(ranges);

        foreach (var property in typeof(PrintedNodeSpan).GetProperties())
            Assert.True(
                property.PropertyType == typeof(string)
                    || property.PropertyType == typeof(int)
                    || property.PropertyType == typeof(PrintedExtent)
                    || property.PropertyType == typeof(AnnotatedSourceNodeProvenance));

        // Enums are permitted: they carry no reference and serialise by value.
        foreach (var property in typeof(PrintedAnnotationSpan).GetProperties())
            Assert.True(
                property.PropertyType == typeof(string)
                    || property.PropertyType == typeof(int)
                    || property.PropertyType == typeof(int?)
                    || property.PropertyType == typeof(PrintedExtent?)
                    || property.PropertyType.IsEnum,
                $"{property.Name} is {property.PropertyType}, which can carry a reference into the IR");

        Assert.NotEmpty(map.Nodes);
    }

    [Fact]
    public void PlacedFactsNameTheExactNodeTheyWereAnchoredTo()
    {
        // Two implementation nodes print one identical surface-syntax element.
        // They normalize to one portable node while identity is still alive, so
        // either implementation node resolves to the same unambiguous id.
        var first = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var second = new LoadLocal(1, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(first, 3, 7);
        ranges.Record(second, 3, 7);
        ranges.Complete("ab\nefgh\nmn");

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>> { [second] = [new Annotation(Alloc, 12)] });

        var node = Assert.Single(map.Nodes);
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(node.Id, fact.NodeId);
        Assert.Equal(node.Extent, fact.Extent);
    }

    [Fact]
    public void ConstructorRejectsBrokenNodeJoins()
    {
        var node = new PrintedNodeSpan(0, "LoadLocal", new PrintedExtent(0, 0, 0, 3));
        var placed = new PrintedAnnotationSpan(
            "alloc.new",
            "Allocation",
            AnnotationConditionality.Always,
            "LoadLocal",
            new PrintedExtent(0, 0, 0, 3),
            null,
            4,
            0);

        // Ids that are not contiguous from 0 in list order make "node 2" mean
        // two different rows depending on how a consumer looks it up.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["abc"],
            [node with { Id = 1 }],
            [],
            []));

        // A placed fact with no node id leaves the join to a coordinate re-match.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["abc"],
            [node],
            [],
            [placed with { NodeId = null }]));

        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["abc"],
            [node],
            [],
            [placed with { NodeId = 7 }]));

        // The id resolves, but not to the thing the fact claims it is.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["abc"],
            [node],
            [],
            [placed with { Kind = "NewObject" }]));

        // An unplaced fact naming a node asserts a placement it does not have.
        Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["abc"],
            [node],
            [],
            [placed with { Extent = null }]));

        var map = new PrintedBodyMap(["abc"], [node], [], [placed]);
        Assert.Equal(0, Assert.Single(map.Annotations).NodeId);
    }

    // The portable document is a text buffer plus overlays: one string, and
    // absolute UTF-16 spans into it. These pin the buffer's invariants, since a
    // consumer holding only this payload slices text by those spans and has
    // nothing else to check them against.
    const string DocumentInstruction = "IL_0000: newobj instance void object::.ctor()";

    static readonly string DocumentText = $"return new object();\n{DocumentInstruction}";

    static AnnotatedSourceNode AllocationNode() =>
        new(0, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(7, 12)]);

    static AnnotatedSourceNode InstructionNode() => new(
        1,
        "Instruction",
        SourceLineKind.Il,
        [new AnnotatedSourceSpan(21, DocumentInstruction.Length)],
        IlOffset: 0);

    static AnnotatedSourceFact AllocationFact() => new(
        0,
        "alloc.new",
        "Allocation",
        AnnotationConditionality.Always,
        "object",
        0,
        AnnotatedSourceFactOrigin.Body);

    [Fact]
    public void AnnotatedSourceDocumentSnapshotsValidatesAndReplays()
    {
        var nodes = new List<AnnotatedSourceNode> { AllocationNode(), InstructionNode() };
        var regions = new List<AnnotatedSourceRegion>
        {
            new(PrintedRegionRole.Body, [new AnnotatedSourceSpan(0, 20)]),
        };
        var facts = new List<AnnotatedSourceFact> { AllocationFact() };
        var targets = new List<AnnotatedSourceTarget> { new(0, 0), new(0, 1) };
        var document = new AnnotatedSourceDocument(DocumentText, nodes, regions, facts, targets);

        nodes.Clear();
        regions.Clear();
        facts.Clear();
        targets.Clear();
        Assert.Equal(2, document.Nodes.Count);
        Assert.Single(document.Regions);

        // Fact -> target -> node -> span -> text is the only join, and it is the
        // same walk in both media.
        var fact = Assert.Single(document.Facts);
        Assert.Equal(2, document.Targets.Count);
        Assert.All(document.Targets, target => Assert.Equal(fact.Id, target.FactId));
        Assert.Equal(
            ["new object()", DocumentInstruction],
            document.Targets.Select(target => Selected(document, document.Nodes[target.NodeId])));

        string json = JsonSerializer.Serialize(document);
        var replayed = JsonSerializer.Deserialize<AnnotatedSourceDocument>(json);
        Assert.NotNull(replayed);
        Assert.Equal(document, replayed);
        Assert.Equal(document.GetHashCode(), replayed!.GetHashCode());
        Assert.Equal(document.Text, replayed.Text);
        Assert.Equal(document.Nodes, replayed.Nodes);
        Assert.Equal(document.Regions, replayed.Regions);
        Assert.Equal(document.Facts, replayed.Facts);
        Assert.Equal(document.Targets, replayed.Targets);

        // Structural equality reaches into the span lists, so a replayed node
        // that selects different characters is a different node.
        Assert.NotEqual(
            document.Nodes[0],
            new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(7, 11)]));
    }

    [Fact]
    public void AnnotatedSourceDocumentAcceptsStructureWithNoFacts()
    {
        // Nodes are text structure, not evidence of an observation. A body with
        // no facts is the ordinary case, and future syntax, comment, and XML-doc
        // producers will only ever add nodes.
        var document = new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode(), InstructionNode()],
            [],
            [],
            []);

        Assert.Equal(2, document.Nodes.Count);
        Assert.Empty(document.Facts);
        Assert.Empty(document.Targets);
    }

    [Fact]
    public void AnnotatedSourceDocumentKeepsFactsThatTargetNothing()
    {
        // A fact with no target is the explicit unanchored case: the observation
        // is real, and nothing in the text was the right thing to point at.
        // Dropping it would lose the observation; inventing a span would turn
        // absence of evidence into a confident, wrong coordinate.
        var header = AllocationFact() with
        {
            Id = 1,
            Descriptor = "cost.method",
            Category = "Cost",
            Detail = null,
            SourceOffset = -1,
            Origin = AnnotatedSourceFactOrigin.MemberHeader,
        };
        var document = new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode()],
            [],
            [AllocationFact() with { SourceOffset = -1 }, header],
            []);

        Assert.Empty(document.Targets);
        Assert.Equal(
            [AnnotatedSourceFactOrigin.Body, AnnotatedSourceFactOrigin.MemberHeader],
            document.Facts.Select(fact => fact.Origin));
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsBrokenIdentity()
    {
        var node = AllocationNode();
        var fact = AllocationFact();

        // Contiguous ids in list order, on both planes.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [new AnnotatedSourceNode(3, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(7, 12)])],
            [],
            [],
            []));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [node],
            [],
            [fact with { Id = 5 }],
            [new AnnotatedSourceTarget(5, 0)]));

        // ... and so is restating a target.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [node],
            [],
            [fact],
            [new AnnotatedSourceTarget(0, 0), new AnnotatedSourceTarget(0, 0)]));

        // Dangling ids on either side of the join.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [node],
            [],
            [fact],
            [new AnnotatedSourceTarget(4, 0)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [node],
            [],
            [fact],
            [new AnnotatedSourceTarget(0, 9)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [node],
            [],
            [fact],
            [new AnnotatedSourceTarget(0, -1)]));
    }

    [Fact]
    public void AnnotatedSourceDocumentPreservesDisplayIdenticalFactMultiplicity()
    {
        var fact = AllocationFact();
        var document = new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode()],
            [],
            [fact, fact with { Id = 1 }],
            []);

        Assert.Equal(2, document.Facts.Count);
        Assert.Equal(document.Facts[0] with { Id = 1 }, document.Facts[1]);
    }

    [Fact]
    public void PrintedBodyMapPreservesCallerIssuedFindingInstanceKey()
    {
        using var source = MetadataSource.Open(
            typeof(AllocSampleClass).Assembly.Location);
        var function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            typeof(AllocSampleClass).FullName!,
            nameof(AllocSampleClass.MakeArray)));
        var result = CSharpPrinter.PrintRaised(function, out var ranges);
        Assert.NotNull(result.Output);

        IAnnotation annotation = new Annotation(Alloc, 0, "array");
        FindingCensus<IAnnotation> census = FindingCensus<IAnnotation>.Seal(
        [
            new Finding<IAnnotation>(
                new FindingSubject("test", "test"),
                new FindingDescriptor("alloc.new", "allocation"),
                new FindingKey("one"),
                annotation),
        ]);
        FindingInstanceKey key = Assert.Single(census.Entries).Key;

        PrintedBodyMap map = PrintedBodyMap.Create(
            ranges,
            function,
            [annotation],
            provenanceOffsetAllowList: null,
            instanceKey: _ => key);

        Assert.Equal(key, Assert.Single(map.Annotations).InstanceKey);
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsFalseTargetClaims()
    {
        var fact = AllocationFact();

        // Targeting an instruction claims the fact is about that instruction, so
        // the offsets have to agree.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode(), InstructionNode()],
            [],
            [fact with { SourceOffset = 7 }],
            [new AnnotatedSourceTarget(0, 1)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode(), InstructionNode()],
            [],
            [fact with { SourceOffset = -1 }],
            [new AnnotatedSourceTarget(0, 1)]));

        // A C# node carries no offset to agree with, so a body fact may target
        // it whatever its own offset is.
        var document = new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode(), InstructionNode()],
            [],
            [fact with { SourceOffset = -1 }],
            [new AnnotatedSourceTarget(0, 0)]);
        Assert.Equal(0, Assert.Single(document.Targets).NodeId);

        // A member-header fact is about the member, not a part of its body.
        var header = fact with
        {
            Descriptor = "cost.method",
            SourceOffset = -1,
            Origin = AnnotatedSourceFactOrigin.MemberHeader,
        };
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode()],
            [],
            [header],
            [new AnnotatedSourceTarget(0, 0)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [AllocationNode()],
            [],
            [header with { SourceOffset = 0 }],
            []));

        var headerOnly = new AnnotatedSourceDocument(DocumentText, [AllocationNode()], [], [header], []);
        Assert.Equal(AnnotatedSourceFactOrigin.MemberHeader, Assert.Single(headerOnly.Facts).Origin);
        Assert.Empty(headerOnly.Targets);
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsSpansThatAreNotCoordinates()
    {
        // A span that selects nothing, runs backwards, doubles back over its
        // predecessor, or leaves the buffer is not a coordinate: a consumer
        // slicing text by it would throw, or worse, select the wrong characters.
        Assert.Throws<ArgumentException>(
            () => new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, []));
        Assert.Throws<ArgumentException>(
            () => new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(7, 0)]));
        Assert.Throws<ArgumentException>(
            () => new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(7, -3)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AnnotatedSourceNode(0, "NewObject", SourceLineKind.CSharp, [new AnnotatedSourceSpan(-1, 4)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "NewObject",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(7, 12), new AnnotatedSourceSpan(0, 3)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "NewObject",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(0, 12), new AnnotatedSourceSpan(7, 3)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "NewObject",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(0, 7), new AnnotatedSourceSpan(7, 3)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceRegion(PrintedRegionRole.Body, []));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceRegion(
            PrintedRegionRole.Body,
            [new AnnotatedSourceSpan(0, 7), new AnnotatedSourceSpan(7, 3)]));

        // Bounds are the document's, because only the document holds the text.
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [new AnnotatedSourceNode(
                0,
                "NewObject",
                SourceLineKind.CSharp,
                [new AnnotatedSourceSpan(DocumentText.Length - 2, 8)])],
            [],
            [],
            []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            "",
            [],
            [new AnnotatedSourceRegion(PrintedRegionRole.Body, [new AnnotatedSourceSpan(0, 4)])],
            [],
            []));

        // A span whose end overflows int is the hostile case: computed as
        // Start + Length it wraps negative and reads as comfortably inside the
        // buffer, so the document would be accepted and the failure deferred to
        // whichever consumer sliced by it. Bounds are checked by subtraction, so
        // it is rejected here.
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [new AnnotatedSourceNode(
                0,
                "NewObject",
                SourceLineKind.CSharp,
                [new AnnotatedSourceSpan(int.MaxValue, 1)])],
            [],
            [],
            []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [],
            [new AnnotatedSourceRegion(
                PrintedRegionRole.Body,
                [new AnnotatedSourceSpan(0, int.MaxValue)])],
            [],
            []));

        // Ordering is decided against the same widened end, so a wrapped
        // predecessor cannot make an overlapping successor look ordered.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "NewObject",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(int.MaxValue - 1, 2), new AnnotatedSourceSpan(0, 3)]));
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsTextThatIsNotWellFormedUtf16()
    {
        // A lone surrogate has no UTF-8 form, so System.Text.Json writes U+FFFD
        // for it: the document that replays is a different string, and every
        // absolute span past the substitution names characters it was not minted
        // for. Producers contain the hazard as a visible ASCII \uXXXX before a
        // document exists, so a raw unpaired code unit here is a producer bug --
        // rejected, never repaired, because repairing it would silently move the
        // coordinates the caller already computed.
        static AnnotatedSourceDocument Make(string text) => new(text, [], [], [], []);

        var lone = Assert.Throws<ArgumentException>(() => Make("return \ud800;"));
        Assert.Equal("Text", lone.ParamName);
        Assert.Contains("index 7", lone.Message, StringComparison.Ordinal);
        Assert.Contains("U+D800", lone.Message, StringComparison.Ordinal);

        var low = Assert.Throws<ArgumentException>(() => Make("return \udc00;"));
        Assert.Equal("Text", low.ParamName);
        Assert.Contains("index 7", low.Message, StringComparison.Ordinal);
        Assert.Contains("U+DC00", low.Message, StringComparison.Ordinal);

        // A high surrogate in the last slot has nothing after it to pair with,
        // which is the case a lookahead written without a bounds check misses.
        var terminal = Assert.Throws<ArgumentException>(() => Make("return;\ud83d"));
        Assert.Equal("Text", terminal.ParamName);
        Assert.Contains("index 7", terminal.Message, StringComparison.Ordinal);

        // A pair in the wrong order is two lone halves, not a scalar.
        Assert.Throws<ArgumentException>(() => Make("\udc00\ud800"));

        // The rejection is the buffer's, not the span's: the text is refused
        // before any overlay is even consulted.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            $"{DocumentText}\ud800",
            [AllocationNode(), InstructionNode()],
            [],
            [],
            []));
    }

    [Fact]
    public void AnnotatedSourceDocumentSourceRejectsTextThatCannotHashOrReplayExactly()
    {
        static AnnotatedSourceDocumentSource Make(string assemblyName, string subject)
            => new(
                assemblyName,
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                0x06000001,
                new string('A', 64),
                subject);

        var assembly = Assert.Throws<ArgumentException>(() => Make("\ud800", "Fixture.M"));
        Assert.Equal("AssemblyName", assembly.ParamName);
        Assert.Contains("U+D800", assembly.Message, StringComparison.Ordinal);

        var subject = Assert.Throws<ArgumentException>(() => Make("Fixture", "\udc00"));
        Assert.Equal("Subject", subject.ParamName);
        Assert.Contains("U+DC00", subject.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnnotatedSourceDocumentSourceRejectsMissingPhysicalModuleIdentity()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new AnnotatedSourceDocumentSource(
                "Fixture",
                Guid.Empty,
                0x06000001,
                new string('A', 64),
                "Fixture.M"));

        Assert.Equal("ModuleVersionId", error.ParamName);
        Assert.Contains("non-empty MVID", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsOverlayTextThatIsNotWellFormedUtf16()
    {
        static AnnotatedSourceDocument Make(
            AnnotatedSourceNode node,
            AnnotatedSourceFact fact) => new(
                DocumentText,
                [node],
                [],
                [fact],
                []);

        var kind = Assert.Throws<ArgumentException>(
            () => Make(
                new AnnotatedSourceNode(
                    0,
                    "New\ud800Object",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(7, 12)]),
                AllocationFact()));
        Assert.Equal("Nodes", kind.ParamName);
        Assert.Contains("Node 0 kind", kind.Message, StringComparison.Ordinal);

        var descriptor = Assert.Throws<ArgumentException>(
            () => Make(AllocationNode(), AllocationFact() with { Descriptor = "alloc.\ud800" }));
        Assert.Equal("Facts", descriptor.ParamName);
        Assert.Contains("Fact 0 descriptor", descriptor.Message, StringComparison.Ordinal);

        var category = Assert.Throws<ArgumentException>(
            () => Make(AllocationNode(), AllocationFact() with { Category = "Alloc\udc00ation" }));
        Assert.Equal("Facts", category.ParamName);
        Assert.Contains("Fact 0 category", category.Message, StringComparison.Ordinal);

        var detail = Assert.Throws<ArgumentException>(
            () => Make(AllocationNode(), AllocationFact() with { Detail = "obj\ud800ect" }));
        Assert.Equal("Facts", detail.ParamName);
        Assert.Contains("Fact 0 detail", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnnotatedSourceDocumentKeepsSupplementaryCharactersExact()
    {
        // Well-formed is the rule, not "ASCII only": a paired surrogate is one
        // scalar the encode round-trips, so it stays raw. It still costs two
        // code units, and the span currency counts code units, so the
        // coordinates on either side of it must account for both.
        const string Emoji = "\U0001F600";
        string text = $"return \"{Emoji}\";\n{DocumentInstruction}";
        int literalStart = text.IndexOf('"');
        int instructionStart = text.IndexOf('\n') + 1;
        Assert.Equal(2, Emoji.Length);

        var document = new AnnotatedSourceDocument(
            text,
            [
                new AnnotatedSourceNode(0, "String", SourceLineKind.CSharp, [new AnnotatedSourceSpan(literalStart, Emoji.Length + 2)]),
                new AnnotatedSourceNode(
                    1,
                    "Instruction",
                    SourceLineKind.Il,
                    [new AnnotatedSourceSpan(instructionStart, DocumentInstruction.Length)],
                    IlOffset: 0),
            ],
            [new AnnotatedSourceRegion(PrintedRegionRole.Body, [new AnnotatedSourceSpan(0, text.Length)])],
            [AllocationFact()],
            [new AnnotatedSourceTarget(0, 0), new AnnotatedSourceTarget(0, 1)]);

        Assert.Equal($"\"{Emoji}\"", Selected(document, document.Nodes[0]));
        Assert.Equal(DocumentInstruction, Selected(document, document.Nodes[1]));

        // The instruction span sits after the pair, so it is only right if both
        // of its code units were counted: the literal's four, then `;` and the
        // line break.
        Assert.Equal(instructionStart, literalStart + Emoji.Length + 2 + 2);

        string json = JsonSerializer.Serialize(document);
        var replayed = JsonSerializer.Deserialize<AnnotatedSourceDocument>(json);
        Assert.NotNull(replayed);
        Assert.Equal(document, replayed);
        Assert.Equal(text, replayed!.Text);
        Assert.DoesNotContain('\uFFFD', replayed.Text);
        Assert.Equal(
            [$"\"{Emoji}\"", DocumentInstruction],
            replayed.Nodes.Select(node => Selected(replayed, node)));
    }

    [Fact]
    public void AnnotatedSourceDocumentRejectsMisplacedIlOffsets()
    {
        // The offset is what makes an instruction node addressable by a fact, so
        // it belongs to IL text only and orders the instruction stream.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "NewObject",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(7, 12)],
            IlOffset: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotatedSourceNode(
            0,
            "Instruction",
            SourceLineKind.Il,
            [new AnnotatedSourceSpan(21, 4)],
            IlOffset: -1));

        // "Instruction" is a claim, not a label: it holds exactly when the node
        // is IL text carrying the offset it disassembles. An offset-bearing
        // Block would let a fact anchor to something that is not one
        // instruction; an offsetless Instruction claims to be one and gives a
        // consumer nothing to resolve; and a C# Instruction claims C# text
        // disassembles.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "Block",
            SourceLineKind.Il,
            [new AnnotatedSourceSpan(21, 4)],
            IlOffset: 0));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "Instruction",
            SourceLineKind.Il,
            [new AnnotatedSourceSpan(21, 4)]));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "Instruction",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(7, 12)],
            IlOffset: 0));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "Instruction",
            SourceLineKind.CSharp,
            [new AnnotatedSourceSpan(7, 12)]));

        // The kind is matched ordinally, so a case variant is a different kind
        // and follows the ordinary offsetless rule.
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceNode(
            0,
            "instruction",
            SourceLineKind.Il,
            [new AnnotatedSourceSpan(21, 4)],
            IlOffset: 0));

        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [
                new AnnotatedSourceNode(0, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(21, 8)], 4),
                new AnnotatedSourceNode(1, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(30, 8)], 4),
            ],
            [],
            [],
            []));
        Assert.Throws<ArgumentException>(() => new AnnotatedSourceDocument(
            DocumentText,
            [
                new AnnotatedSourceNode(0, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(21, 8)], 4),
                new AnnotatedSourceNode(1, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(30, 8)], 2),
            ],
            [],
            [],
            []));

        // A future structural IL node carries no offset, and must not have to
        // invent one to sit between two instructions.
        var document = new AnnotatedSourceDocument(
            DocumentText,
            [
                new AnnotatedSourceNode(0, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(21, 8)], 0),
                new AnnotatedSourceNode(1, "Block", SourceLineKind.Il, [new AnnotatedSourceSpan(30, 8)]),
                new AnnotatedSourceNode(2, "Instruction", SourceLineKind.Il, [new AnnotatedSourceSpan(39, 5)], 5),
            ],
            [],
            [],
            []);
        Assert.Equal([0, null, 5], document.Nodes.Select(node => node.IlOffset));
        Assert.Equal("Instruction", AnnotatedSourceNode.InstructionKind);
    }

    [Fact]
    public void AnnotatedSourceDocumentSplitsStructureAroundInterleavedIl()
    {
        // The reason spans are a list. This C# construct is printed across two
        // lines with an IL line woven between them, so its exact characters are
        // two runs of the buffer. One span from the first character to the last
        // would swallow the instruction, which is text the construct does not
        // contain.
        const string text = "int x = 1;\nIL_0000: ldc.i4.1\nreturn x;";
        var document = new AnnotatedSourceDocument(
            text,
            [
                new AnnotatedSourceNode(
                    0,
                    "Block",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 10), new AnnotatedSourceSpan(29, 9)]),
                new AnnotatedSourceNode(
                    1,
                    "Instruction",
                    SourceLineKind.Il,
                    [new AnnotatedSourceSpan(11, 17)],
                    IlOffset: 0),
            ],
            [
                new AnnotatedSourceRegion(
                    PrintedRegionRole.Body,
                    [new AnnotatedSourceSpan(0, 10), new AnnotatedSourceSpan(29, 9)]),
            ],
            [],
            []);

        var block = document.Nodes[0];
        Assert.Equal(2, block.Spans.Count);
        Assert.Equal("int x = 1;return x;", Selected(document, block));
        Assert.DoesNotContain("IL_0000", Selected(document, block), StringComparison.Ordinal);
        Assert.Equal("IL_0000: ldc.i4.1", Selected(document, document.Nodes[1]));
        Assert.Equal(block.Spans, Assert.Single(document.Regions).Spans);
    }

    static string Selected(AnnotatedSourceDocument document, AnnotatedSourceNode node) => string.Concat(
        node.Spans.Select(span => document.Text.Substring(span.Start, span.Length)));

    [Fact]
    public void SurvivesSerialisationAndReplays()
    {
        var (_, ranges) = Print(nameof(AllocSampleClass.SumList));
        var map = PrintedBodyMap.Create(ranges);

        string json = JsonSerializer.Serialize(map);
        var replayed = JsonSerializer.Deserialize<PrintedBodyMap>(json);

        Assert.NotNull(replayed);
        Assert.NotEmpty(map.Nodes);
        Assert.NotEmpty(replayed!.Nodes);
        Assert.Equal(map.Lines, replayed!.Lines);
        Assert.Equal(map.Nodes, replayed.Nodes);
        Assert.Equal(map.Regions, replayed.Regions);
        Assert.Equal(map.Annotations, replayed.Annotations);

        // Replay proper: the round-tripped payload alone still selects the same
        // characters, with nothing from the decompiler in scope.
        foreach (var span in replayed.Nodes)
            Assert.NotEmpty(Text(replayed, span.Extent));
    }

    [Fact]
    public void TwoIndependentPrintsProduceIdenticalPayloads()
    {
        // Dictionary enumeration order is not a contract and List.Sort is not
        // stable, so a partial comparator would make the payload differ between
        // runs -- which would later read as a real change. Node ids are cut from
        // that same order, so they inherit the requirement.
        var (_, first) = Print(nameof(AllocSampleClass.SumList));
        var (_, second) = Print(nameof(AllocSampleClass.SumList));

        Assert.Equal(
            JsonSerializer.Serialize(PrintedBodyMap.Create(first)),
            JsonSerializer.Serialize(PrintedBodyMap.Create(second)));
    }

    static readonly AnnotationDescriptor Alloc =
        new("alloc.new", AnnotationCategory.Allocation, "Allocation");

    static readonly AnnotationDescriptor Box =
        new("alloc.box", AnnotationCategory.Allocation, "Boxing");

    static (PrintedRangeMap Ranges, LoadLocal First, LoadLocal Second) TwoNodesOnOneLine()
    {
        var first = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var second = new LoadLocal(1, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(first, 3, 7);
        ranges.Record(second, 9, 13);
        ranges.Complete("ab\nefgh__ijkl\nmn");
        return (ranges, first, second);
    }

    [Fact]
    public void FactsArePositionedAtTheNodeTheyWereFoundOn()
    {
        var (ranges, first, second) = TwoNodesOnOneLine();
        var annotations = new Dictionary<IrNode, IReadOnlyList<IAnnotation>>
        {
            [first] = [new Annotation(Alloc, 12, "List<int>")],
            [second] = [new Annotation(Box, 34, "int")],
        };

        var map = PrintedBodyMap.Create(ranges, annotations);

        Assert.Equal(2, map.Annotations.Count);
        var a = map.Annotations[0];
        var b = map.Annotations[1];

        Assert.Equal("alloc.new", a.Descriptor);
        Assert.Equal("Allocation", a.Category);
        Assert.Equal("List<int>", a.Detail);
        Assert.Equal(12, a.SourceOffset);
        Assert.Equal("efgh", Text(map, a.Extent!.Value));
        Assert.Equal(0, a.NodeId);
        Assert.Equal(map.Nodes[0].Extent, a.Extent);

        Assert.Equal("alloc.box", b.Descriptor);
        Assert.Equal("ijkl", Text(map, b.Extent!.Value));
        Assert.Equal(1, b.NodeId);
        Assert.Equal(map.Nodes[1].Extent, b.Extent);
    }

    [Fact]
    public void OrderingDistinguishesEveryFieldThatCanDiffer()
    {
        // Any pair the comparison calls equal may come out in either order, so a
        // comparison that stops short of a total order makes the serialised
        // payload differ between runs over identical input. Each pair below
        // differs in exactly one field.
        var baseline = new PrintedAnnotationSpan(
            "alloc.new",
            "Allocation",
            AnnotationConditionality.Always,
            "NewObject",
            new PrintedExtent(3, 7, 3, 19),
            "List<int>",
            40,
            2);

        Assert.NotEqual(0, PrintedBodyMap.Compare(
            baseline,
            baseline with { Extent = baseline.Extent!.Value with { StartLine = 4 } }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(
            baseline,
            baseline with { Extent = baseline.Extent!.Value with { StartColumn = 8 } }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(
            baseline,
            baseline with { Extent = baseline.Extent!.Value with { EndLine = 4 } }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(
            baseline,
            baseline with { Extent = baseline.Extent!.Value with { EndColumn = 20 } }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Extent = null }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Descriptor = "alloc.box" }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Category = "Unsafety" }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { SourceOffset = 41 }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Kind = "Box" }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Conditionality = AnnotationConditionality.PerIteration }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { Detail = "int" }));
        Assert.NotEqual(0, PrintedBodyMap.Compare(baseline, baseline with { NodeId = 3 }));

        Assert.Equal(0, PrintedBodyMap.Compare(baseline, baseline));
    }

    [Fact]
    public void FactOrderingDoesNotDependOnDictionaryOrder()
    {
        // List.Sort is unstable and dictionary enumeration order is not a
        // contract, so a comparator that stops short of a total order would let
        // the payload differ between two runs over identical input.
        var (ranges, first, second) = TwoNodesOnOneLine();

        Dictionary<IrNode, IReadOnlyList<IAnnotation>> forward = new()
        {
            [first] = [new Annotation(Alloc, 12, "a"), new Annotation(Box, 12, "b")],
            [second] = [new Annotation(Alloc, 34, "c")],
        };
        Dictionary<IrNode, IReadOnlyList<IAnnotation>> reversed = new()
        {
            [second] = [new Annotation(Alloc, 34, "c")],
            [first] = [new Annotation(Box, 12, "b"), new Annotation(Alloc, 12, "a")],
        };

        Assert.Equal(
            PrintedBodyMap.Create(ranges, forward).Annotations,
            PrintedBodyMap.Create(ranges, reversed).Annotations);
    }

    [Fact]
    public void AFactOnAStraddlingNodeKeepsItsExactMultiLineExtent()
    {
        // The old line/column/length shape could only report this as unknown.
        // End coordinates preserve the exact characters instead.
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 2, 12);
        ranges.Complete("ab\ncdefgh\nij");

        var map = PrintedBodyMap.Create(
            ranges,
            new Dictionary<IrNode, IReadOnlyList<IAnnotation>> { [node] = [new Annotation(Alloc, 4)] });

        var span = Assert.Single(map.Nodes);
        var fact = Assert.Single(map.Annotations);
        Assert.Equal(span.Extent, fact.Extent);
        Assert.Equal(span.Id, fact.NodeId);
        Assert.Equal("\ncdefgh\nij", Text(map, span.Extent));
    }

    [Fact]
    public void ARangeEndingWithItsLineBreakIsPlacedRatherThanRefused()
    {
        // A statement's range runs to the end of the line it printed, newline
        // included. Treating that as crossing a line break would refuse every
        // statement in the body, and every statement-anchored fact with it.
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 3, 10);
        ranges.Complete("ab\ncdefgh\nij");

        Assert.True(ranges.TryGetLineColumn(node, out int line, out int column, out int length));
        Assert.Equal(1, line);
        Assert.Equal(0, column);
        Assert.Equal(6, length);

        var map = PrintedBodyMap.Create(ranges);
        var span = Assert.Single(map.Nodes);
        Assert.Equal("cdefgh", Text(map, span.Extent));
    }

    [Fact]
    public void ARangeOfNothingButALineBreakIsRefused()
    {
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 2, 3);
        ranges.Complete("ab\ncdefgh\nij");

        Assert.False(ranges.TryGetLineColumn(node, out _, out _, out _));
    }

    [Fact]
    public void ARangeThatCrossesALineBreakKeepsItsExactExtent()
    {
        // Reporting only its first line would understate the extent. The
        // portable map now carries both endpoints instead.
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 2, 12);
        ranges.Complete("ab\ncdefgh\nij");

        Assert.False(ranges.TryGetLineColumn(node, out _, out _, out _));
        Assert.True(ranges.TryGetExtent(node, out var extent));
        Assert.Equal(new PrintedExtent(0, 2, 2, 2), extent);
        var map = PrintedBodyMap.Create(ranges);
        Assert.Equal("\ncdefgh\nij", Text(map, Assert.Single(map.Nodes).Extent));
    }

    [Fact]
    public void ASingleLineRangeIsPlacedAtItsOwnColumn()
    {
        var node = new LoadLocal(0, TypeRef.CoreLib("System", "Int32"));
        var ranges = new PrintedRangeMap();
        ranges.Record(node, 5, 9);
        ranges.Complete("ab\ncdefgh\nij");

        Assert.True(ranges.TryGetLineColumn(node, out int line, out int column, out int length));
        Assert.Equal(1, line);
        Assert.Equal(2, column);
        Assert.Equal(4, length);

        var map = PrintedBodyMap.Create(ranges);
        var span = Assert.Single(map.Nodes);
        Assert.Equal("NameExpression", span.Kind);
        Assert.Equal("efgh", Text(map, span.Extent));
    }

    static void AssertSurfaceKind(
        PrintedRangeMap ranges,
        IrNode node,
        string expectedText,
        string expectedKind)
    {
        Assert.True(ranges.TryGetRange(node, out var range));
        Assert.Equal(expectedText, ranges.Output[range]);
        Assert.True(ranges.TryGetNodeKind(node, out string? kind));
        Assert.Equal(expectedKind, kind);
        Assert.True(AnnotatedSourceNodeKinds.IsKnown(kind));
    }

    static int ComparePosition(int line, int column, int otherLine, int otherColumn)
    {
        int c = line.CompareTo(otherLine);
        return c != 0 ? c : column.CompareTo(otherColumn);
    }

    static int SlotOf(PrintedRangeMap ranges, IrNode node)
        => ranges.Select((range, index) => (range.Node, index))
            .Single(item => ReferenceEquals(item.Node, node))
            .index;

    static IrNode ContextualRoot(
        PrintedRangeMap ranges,
        string text,
        string kind)
        => Assert.Single(
            ranges,
            printed => printed.Node is SynthesizedRenderedExpression
                && ranges.TryGetNodeKind(printed.Node, out string? renderedKind)
                && renderedKind == kind
                && ranges.Output[printed.Characters] == text).Node;

    static string Text(PrintedBodyMap map, PrintedExtent extent) => Text(map.Lines, extent);

    static string Text(IReadOnlyList<string> lines, PrintedExtent extent)
    {
        if (extent.StartLine == extent.EndLine)
        {
            return lines[extent.StartLine][extent.StartColumn..extent.EndColumn];
        }

        var selected = new List<string>
        {
            lines[extent.StartLine][extent.StartColumn..],
        };
        for (int line = extent.StartLine + 1; line < extent.EndLine; line++)
            selected.Add(lines[line]);
        selected.Add(lines[extent.EndLine][..extent.EndColumn]);
        return string.Join('\n', selected);
    }
}
