using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Printer")]
public class PrintedRegionTests
{
    static readonly TypeRef Holder = TypeRef.Definition("synthetic", "", "Holder");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef ListOfInt = TypeRef.GenericInstance(
        TypeRef.Definition("System.Private.CoreLib", "System.Collections.Generic", "List`1"),
        [Int32]);

    public static TheoryData<string, string, PrintedRegionRole[]> CompoundConstructs => new()
    {
        {
            nameof(PrintedRegionFixture.ForLoop),
            nameof(ForLoop),
            [PrintedRegionRole.Header, PrintedRegionRole.Body]
        },
        {
            nameof(PrintedRegionFixture.WhileLoop),
            nameof(WhileLoop),
            [PrintedRegionRole.Header, PrintedRegionRole.Body]
        },
        {
            nameof(PrintedRegionFixture.DoWhileLoop),
            nameof(DoWhileLoop),
            [PrintedRegionRole.Header, PrintedRegionRole.Body]
        },
        {
            nameof(PrintedRegionFixture.TryCatch),
            nameof(TryCatch),
            [PrintedRegionRole.Body, PrintedRegionRole.Catch]
        },
        {
            nameof(PrintedRegionFixture.Lock),
            "Lock",
            [PrintedRegionRole.Header, PrintedRegionRole.Body]
        },
        {
            nameof(PrintedRegionFixture.Using),
            nameof(UsingStatement),
            [PrintedRegionRole.Header, PrintedRegionRole.Body]
        },
        {
            nameof(PrintedRegionFixture.Foreach),
            nameof(ForeachStatement),
            [PrintedRegionRole.Header, PrintedRegionRole.Body]
        },
        {
            nameof(PrintedRegionFixture.TryFinally),
            nameof(TryFinally),
            [PrintedRegionRole.Body, PrintedRegionRole.Finally]
        },
        {
            nameof(PrintedRegionFixture.Switch),
            nameof(Switch),
            [PrintedRegionRole.Header, PrintedRegionRole.Body, PrintedRegionRole.Case]
        },
    };

    [Theory]
    [MemberData(nameof(CompoundConstructs))]
    public void CompoundConstructs_RecordTheirLiteralSyntax(
        string method,
        string nodeKind,
        PrintedRegionRole[] expectedRoles)
    {
        var map = Map(typeof(PrintedRegionFixture), method, raised: true);

        var construct = Assert.Single(
            map.Regions,
            region => region.Role == PrintedRegionRole.Construct
                && map.Nodes.Any(node => node.Kind == nodeKind && node.Extent == region.Extent));
        Assert.StartsWith(
            ExpectedConstructPrefix(nodeKind),
            Text(map, construct.Extent).TrimStart());

        foreach (var role in expectedRoles)
            Assert.Contains(map.Regions, region => region.Role == role);

        foreach (var region in map.Regions)
        {
            string text = Text(map, region.Extent);
            Assert.NotEmpty(text);
            switch (region.Role)
            {
                case PrintedRegionRole.Header:
                    Assert.False(char.IsWhiteSpace(text[0]));
                    Assert.True(text.EndsWith(')') || text.EndsWith(");", StringComparison.Ordinal));
                    break;
                case PrintedRegionRole.Body:
                    Assert.StartsWith("{", text);
                    Assert.EndsWith("}", text);
                    break;
                case PrintedRegionRole.Else:
                    Assert.StartsWith("else\n{", text);
                    Assert.EndsWith("}", text);
                    break;
                case PrintedRegionRole.Catch:
                    Assert.StartsWith("catch", text);
                    Assert.EndsWith("}", text);
                    break;
                case PrintedRegionRole.Finally:
                    Assert.StartsWith("finally\n{", text);
                    Assert.EndsWith("}", text);
                    break;
                case PrintedRegionRole.Case:
                    Assert.True(
                        text.StartsWith("case ", StringComparison.Ordinal)
                            || text.StartsWith("default:", StringComparison.Ordinal)
                            || text.StartsWith("if (", StringComparison.Ordinal),
                        $"Unexpected case region: {text}");
                    break;
            }
        }
    }

    [Fact]
    public void LoweredSwitchBranch_RecordsOneCasePerTarget()
    {
        var map = Map(
            typeof(SwitchTableFixture),
            nameof(SwitchTableFixture.Classify),
            raised: false);

        var construct = Assert.Single(
            map.Regions,
            region => region.Role == PrintedRegionRole.Construct
                && map.Nodes.Any(node => node.Kind == nameof(SwitchBranch)
                    && node.Extent == region.Extent));
        Assert.Contains("__switchValue", Text(map, construct.Extent));

        var cases = map.Regions
            .Where(region => region.Role == PrintedRegionRole.Case
                && Contains(construct.Extent, region.Extent))
            .ToList();
        Assert.NotEmpty(cases);
        Assert.All(cases, region => Assert.StartsWith("if (", Text(map, region.Extent)));
    }

    [Fact]
    public void Fixed_RecordsHeaderAndBody()
    {
        var fixedBody = Container(new ExpressionStatement(new LoadLocal(0, Int32)));
        var body = Container(new Fixed(
            Int32,
            localIndex: 0,
            new Constant(0, Int32),
            fixedBody));
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [TypeRef.Pinned(TypeRef.ByRef(Int32))],
            body);

        var map = Map(function);

        var construct = Assert.Single(
            map.Regions,
            region => region.Role == PrintedRegionRole.Construct
                && map.Nodes.Any(node => node.Kind == nameof(Fixed)
                    && node.Extent == region.Extent));
        Assert.StartsWith("fixed (", Text(map, construct.Extent));
        Assert.Contains(map.Regions, region => region.Role == PrintedRegionRole.Header);
        Assert.Contains(map.Regions, region => region.Role == PrintedRegionRole.Body);
    }

    [Fact]
    public void IfWithElse_RecordsHeaderBodyAndElse()
    {
        var thenArm = new Block(0);
        thenArm.Add(new Return(new Constant(1, Int32)));
        var elseArm = new Block(1);
        elseArm.Add(new Return(new Constant(-1, Int32)));
        var body = Container(new IfStatement(new Constant(true, Bool), thenArm, elseArm));
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0),
            ImmutableArray<TypeRef>.Empty,
            body);

        var map = Map(function);

        Assert.Contains(map.Regions, region => region.Role == PrintedRegionRole.Header);
        Assert.Contains(map.Regions, region => region.Role == PrintedRegionRole.Body);
        var elseRegion = Assert.Single(
            map.Regions,
            region => region.Role == PrintedRegionRole.Else);
        Assert.StartsWith("else\n{", Text(map, elseRegion.Extent));
        Assert.EndsWith("}", Text(map, elseRegion.Extent));
    }

    [Fact]
    public void MultiLineHeader_ContainsTheCompleteHeader()
    {
        var loopBody = new BlockContainer();
        var loopBlock = new Block(0);
        loopBlock.Add(new ExpressionStatement(new Call(
            new MethodRef(Holder, "Tick", Void, [], HasThis: false),
            isVirtual: false,
            [])));
        loopBody.Add(loopBlock);

        var condition = new Call(
            new MethodRef(
                ListOfInt,
                "Exists",
                Bool,
                [TypeRef.GenericInstance(
                    TypeRef.Definition("System.Private.CoreLib", "System", "Predicate`1"),
                    [Int32])],
                HasThis: true),
            isVirtual: false,
            [new LoadArgument(0, "items", ListOfInt), MultiStatementPredicate()]);

        var function = Function(new DoWhileLoop(loopBody, condition));
        var result = CSharpPrinter.PrintRaised(function, out var ranges);
        Assert.NotNull(result.Output);
        var map = PrintedBodyMap.Create(ranges);

        var header = Assert.Single(
            map.Regions,
            region => region.Role == PrintedRegionRole.Header);
        Assert.True(header.Extent.EndLine > header.Extent.StartLine);
        string text = Text(map, header.Extent);
        Assert.StartsWith("while (items.Exists(x =>\n{", text);
        Assert.EndsWith("}));", text);
    }

    [Fact]
    public void NestedRegions_StartAtSyntaxAfterIndentation()
    {
        var map = Map(
            typeof(PrintedRegionFixture),
            nameof(PrintedRegionFixture.Nested),
            raised: true);

        var nestedHeader = Assert.Single(
            map.Regions,
            region => region.Role == PrintedRegionRole.Header
                && region.Extent.StartColumn > 0);
        var nestedBody = Assert.Single(
            map.Regions,
            region => region.Role == PrintedRegionRole.Body
                && region.Extent.StartColumn > 0);

        Assert.StartsWith("if (", Text(map, nestedHeader.Extent));
        Assert.StartsWith("{", Text(map, nestedBody.Extent));
    }

    [Fact]
    public void Constructor_RejectsPartialOverlap()
    {
        // This is the non-vacuity gate for the constructor-enforced laminarity
        // claim in PrintedBodyMap's remarks. The two valid extents overlap but
        // neither contains the other.
        var exception = Assert.Throws<ArgumentException>(() => new PrintedBodyMap(
            ["abcdefghij"],
            [new PrintedNodeSpan("Outer", new PrintedExtent(0, 0, 0, 6))],
            [new PrintedRegion(
                PrintedRegionRole.Body,
                new PrintedExtent(0, 4, 0, 8))],
            []));

        Assert.Contains("partially overlap", exception.Message);
    }

    [Fact]
    public void Constructor_AcceptsDuplicateNestedAndAdjacentExtents()
    {
        var outer = new PrintedExtent(0, 0, 0, 10);
        var map = new PrintedBodyMap(
            ["abcdefghij"],
            [new PrintedNodeSpan("IfStatement", outer)],
            [
                new PrintedRegion(PrintedRegionRole.Construct, outer),
                new PrintedRegion(PrintedRegionRole.Header, new PrintedExtent(0, 0, 0, 4)),
                new PrintedRegion(PrintedRegionRole.Body, new PrintedExtent(0, 4, 0, 10)),
            ],
            []);

        Assert.Equal(3, map.Regions.Count);
    }

    [Fact]
    public void Constructor_SnapshotsEveryCollection()
    {
        var lines = new List<string> { "abc" };
        var nodes = new List<PrintedNodeSpan>
        {
            new("LoadLocal", new PrintedExtent(0, 0, 0, 3)),
        };
        var regions = new List<PrintedRegion>();
        var annotations = new List<PrintedAnnotationSpan>();
        var map = new PrintedBodyMap(lines, nodes, regions, annotations);

        lines[0] = "changed";
        nodes.Clear();
        regions.Add(new PrintedRegion(
            PrintedRegionRole.Header,
            new PrintedExtent(0, 0, 0, 1)));

        Assert.Equal("abc", Assert.Single(map.Lines));
        Assert.Single(map.Nodes);
        Assert.Empty(map.Regions);
    }

    static PrintedBodyMap Map(Type type, string method, bool raised)
    {
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, method);
        Assert.NotNull(function);
        DecompilerResult result = raised
            ? CSharpPrinter.PrintRaised(function!, out var ranges)
            : CSharpPrinter.PrintLowered(function!, out ranges);
        Assert.NotNull(result.Output);
        return PrintedBodyMap.Create(ranges);
    }

    static PrintedBodyMap Map(IrFunction function)
    {
        var result = CSharpPrinter.PrintRaised(function, out var ranges);
        Assert.NotNull(result.Output);
        return PrintedBodyMap.Create(ranges);
    }

    static string ExpectedConstructPrefix(string nodeKind)
        => nodeKind switch
        {
            nameof(ForLoop) => "for (",
            nameof(WhileLoop) => "while (",
            nameof(DoWhileLoop) => "do\n",
            nameof(TryCatch) or nameof(TryFinally) => "try\n",
            "Lock" => "lock (",
            nameof(Fixed) => "fixed (",
            nameof(UsingStatement) => "using (",
            nameof(ForeachStatement) => "foreach (",
            nameof(IfStatement) => "if (",
            nameof(Switch) => "switch (",
            _ => throw new ArgumentOutOfRangeException(nameof(nodeKind)),
        };

    static bool Contains(PrintedExtent outer, PrintedExtent inner)
        => Compare(
                outer.StartLine, outer.StartColumn,
                inner.StartLine, inner.StartColumn) <= 0
            && Compare(
                inner.EndLine, inner.EndColumn,
                outer.EndLine, outer.EndColumn) <= 0;

    static int Compare(int line, int column, int otherLine, int otherColumn)
    {
        int c = line.CompareTo(otherLine);
        return c != 0 ? c : column.CompareTo(otherColumn);
    }

    static string Text(PrintedBodyMap map, PrintedExtent extent)
    {
        if (extent.StartLine == extent.EndLine)
            return map.Lines[extent.StartLine][extent.StartColumn..extent.EndColumn];

        var selected = new List<string>
        {
            map.Lines[extent.StartLine][extent.StartColumn..],
        };
        for (int line = extent.StartLine + 1; line < extent.EndLine; line++)
            selected.Add(map.Lines[line]);
        selected.Add(map.Lines[extent.EndLine][..extent.EndColumn]);
        return string.Join('\n', selected);
    }

    static Lambda MultiStatementPredicate()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(Holder, "Log", Void, [Int32], HasThis: false),
            isVirtual: false,
            [new LoadArgument(0, "x", Int32)])));
        block.Add(new Return(new Comparison(
            ComparisonKind.GreaterThan,
            isUnsigned: false,
            new LoadArgument(0, "x", Int32),
            new Constant(0, Int32))));
        body.Add(block);
        return new Lambda(
            TypeRef.GenericInstance(
                TypeRef.Definition("System.Private.CoreLib", "System", "Predicate`1"),
                [Int32]),
            [new Parameter("x", Int32)],
            ImmutableArray<TypeRef>.Empty,
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            body);
    }

    static IrFunction Function(IrNode statement)
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(statement);
        body.Add(block);
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(
                Void,
                [new Parameter("items", ListOfInt)],
                HasThis: false,
                GenericParameterCount: 0),
            ImmutableArray<TypeRef>.Empty,
            body);
    }

    static BlockContainer Container(params IrNode[] statements)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        container.Add(block);
        return container;
    }
}

public static class PrintedRegionFixture
{
    public static int ForLoop(int count)
    {
        int sum = 0;
        for (int i = 0; i < count; i++)
            sum += i;
        return sum;
    }

    public static int WhileLoop(int count)
    {
        int sum = 0;
        while (count > 0)
            sum += count--;
        return sum;
    }

    public static int DoWhileLoop(int count)
    {
        int sum = 0;
        do
        {
            sum += count--;
        }
        while (count > 0);
        return sum;
    }

    public static int TryCatch(int value)
    {
        try
        {
            return 10 / value;
        }
        catch (DivideByZeroException)
        {
            return 0;
        }
    }

    public static void Lock(object gate, Action action)
    {
        lock (gate)
            action();
    }

    public static int Using(MemoryStream stream)
    {
        using (stream)
            return stream.ReadByte();
    }

    public static int Foreach(int[] values)
    {
        int sum = 0;
        foreach (int value in values)
            sum += value;
        return sum;
    }

    public static void TryFinally(Action action)
    {
        try
        {
            action();
        }
        finally
        {
            action();
        }
    }

    public static int Switch(int value)
    {
        switch (value)
        {
            case 0:
                return 10;
            case 1:
                return 11;
            case 2:
                return 12;
            case 3:
                return 13;
            default:
                return -1;
        }
    }

    public static int Nested(int value)
    {
        while (value > 0)
        {
            if ((value & 1) == 0)
            {
                value--;
            }
            else
            {
                value -= 2;
            }
        }
        return value;
    }
}
