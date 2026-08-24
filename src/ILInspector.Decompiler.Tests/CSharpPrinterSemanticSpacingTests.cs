using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

static class SemanticSpacingFixture
{
    public static int Grouped(string first, string second, int kind)
    {
        if (first is null)
            throw new ArgumentNullException(nameof(first));

        first = first.Trim();
        second = second.Trim();
        int length = first.Length + second.Length;
        if (length == 0)
            return -1;

        switch (kind)
        {
            case 0:
                GC.KeepAlive(first);
                break;
            case 1:
                GC.KeepAlive(second);
                break;
            case 2:
                GC.KeepAlive(length);
                break;
            case 3:
                GC.KeepAlive(kind);
                break;
            default:
                GC.KeepAlive(null);
                break;
        }
        return length;
    }

    public static int Compact(int value)
    {
        if (value < 0)
            return -1;
        return value + 1;
    }

    public static int SiblingControlFlow(string first, string second, int kind)
    {
        first = first.Trim();
        second = second.Trim();
        int length = first.Length + second.Length;
        if (length > 10)
            GC.KeepAlive(length);
        switch (kind)
        {
            case 0:
                GC.KeepAlive(first);
                break;
            default:
                GC.KeepAlive(second);
                break;
        }
        return length;
    }
}

sealed class FourVisibleConstructorFixture
{
    public int A;
    public int B;
    public int C;

    public FourVisibleConstructorFixture(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        A = value;
        B = value + 1;
        C = value + 2;
    }
}

sealed class FiveVisibleConstructorFixture
{
    public int A;
    public int B;
    public int C;
    public int D;

    public FiveVisibleConstructorFixture(int value)
    {
        if (value < 0)
            GC.KeepAlive(value);
        A = value;
        B = value + 1;
        C = value + 2;
        D = value + 3;
    }
}

[Trait("Area", "Printer")]
public class CSharpPrinterSemanticSpacingTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    static (string Output, PrintedRangeMap Ranges) Print(string methodName)
        => Print(typeof(SemanticSpacingFixture), methodName);

    static (string Output, PrintedRangeMap Ranges) Print(
        Type declaringType,
        string methodName)
    {
        using var source = MetadataSource.Open(declaringType.Assembly.Location);
        var function = IrImporter.Import(
            source,
            declaringType.FullName!,
            methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, out var ranges);
        Assert.NotNull(result.Output);
        return (result.Output!, ranges);
    }

    [Fact]
    public void LongMethod_SeparatesCompletedConditionalGroupsButKeepsSetupCompact()
    {
        var (output, _) = Print(nameof(SemanticSpacingFixture.Grouped));

        Assert.Contains(
            "throw new ArgumentNullException(\"first\");\n" +
            "}\n\n" +
            "first = first.Trim();",
            output);
        Assert.Contains(
            "int length = first.Length + second.Length;\n" +
            "if (length == 0)",
            output);
        Assert.Contains(
            "return -1;\n" +
            "}\n\n" +
            "switch (kind)",
            output);
        Assert.Equal(2, output.Split("\n\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void LongMethod_SeparatesSiblingControlFlowGroups()
    {
        var (output, _) = Print(nameof(SemanticSpacingFixture.SiblingControlFlow));

        Assert.Contains(
            "int length = first.Length + second.Length;\n" +
            "if (length > 10)",
            output);
        Assert.Contains(
            "GC.KeepAlive(length);\n" +
            "}\n\n" +
            "if (kind == 0)",
            output);
        Assert.Equal(1, output.Split("\n\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void FourVisibleConstructor_DoesNotCountSuppressedBaseCall()
    {
        var (output, _) = Print(typeof(FourVisibleConstructorFixture), ".ctor");

        Assert.Contains(
            "throw new ArgumentOutOfRangeException(\"value\");\n" +
            "}\n" +
            "A = value;",
            output);
        Assert.DoesNotContain("\n\n", output);
    }

    [Fact]
    public void FiveVisibleConstructor_SeparatesNonTerminatingLeadingConditional()
    {
        var (output, _) = Print(typeof(FiveVisibleConstructorFixture), ".ctor");

        Assert.Contains(
            "GC.KeepAlive(value);\n" +
            "}\n\n" +
            "A = value;",
            output);
        Assert.Equal(1, output.Split("\n\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void CompactMethod_KeepsAdjacentStatementsCompact()
    {
        var (output, _) = Print(nameof(SemanticSpacingFixture.Compact));

        Assert.Contains(
            "return -1;\n" +
            "}\n" +
            "return value + 1;",
            output);
        Assert.DoesNotContain("}\n\nreturn value + 1;", output);
    }

    [Fact]
    public void LabeledSequence_DeclinesSemanticSpacing()
    {
        var entry = new Block(0);
        entry.Add(new Branch(0x10));
        var labeled = new Block(0x10);
        labeled.Add(ReturningIf("first", 0, 1));
        labeled.Add(ReturningIf("second", 1, 2));
        labeled.Add(new ExpressionStatement(new Constant(3, Int32)));
        labeled.Add(new ExpressionStatement(new Constant(4, Int32)));
        labeled.Add(new Return(new Constant(0, Int32)));
        var body = new BlockContainer();
        body.Add(entry);
        body.Add(labeled);
        var function = new IrFunction(
            "Labeled",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Int32,
                [
                    new Parameter("first", Boolean),
                    new Parameter("second", Boolean),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Contains("IL_0010:", output);
        Assert.Contains("return 1;\n}\nif (second)", output);
        Assert.DoesNotContain("return 1;\n}\n\nif (second)", output);
    }

    [Fact]
    public void StatementOwnedLabel_DeclinesSemanticSpacingIndependently()
    {
        var guardBody = new Block();
        guardBody.Add(new Branch(0x50));
        var entry = new Block(0);
        entry.Add(new IfStatement(
            new LoadArgument(0, "flag", Boolean),
            guardBody,
            elseArm: null));
        entry.Add(new ExpressionStatement(new Constant(1, Int32)));
        entry.Add(new ExpressionStatement(new Constant(2, Int32)));
        var labeledStatement = new ExpressionStatement(new Constant(3, Int32));
        labeledStatement.SetSourceOffset(0x50);
        entry.Add(labeledStatement);
        entry.Add(new ExpressionStatement(new Constant(4, Int32)));
        entry.Add(new Return(new Constant(0, Int32)));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "StatementOwnedLabel",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Int32,
                [new Parameter("flag", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Contains("IL_0050:", output);
        Assert.Contains("goto IL_0050;\n}\n_ = 1;", output);
        Assert.DoesNotContain("goto IL_0050;\n}\n\n_ = 1;", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoopBody_SeparatesBreakAndContinueGuards(bool useContinue)
    {
        IrNode terminal = useContinue ? new Continue() : new Break();
        string output = PrintLoopConditional(terminal);
        string keyword = useContinue ? "continue" : "break";

        Assert.Contains(
            $"{keyword};\n" +
            "    }\n\n" +
            "    _ = 3;",
            output);
        Assert.Equal(1, output.Split("\n\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void LoopBody_KeepsNonTerminatingConditionalCompact()
    {
        string output = PrintLoopConditional(
            new ExpressionStatement(new Constant(99, Int32)));

        Assert.Contains(
            "_ = 99;\n" +
            "    }\n" +
            "    _ = 3;",
            output);
        Assert.DoesNotContain(
            "_ = 99;\n" +
            "    }\n\n" +
            "    _ = 3;",
            output);
    }

    [Fact]
    public void NestedFiveStatementSequence_AppliesSpacingIndependently()
    {
        var nested = new Block();
        var innerThen = new Block();
        innerThen.Add(new ExpressionStatement(new Constant(1, Int32)));
        nested.Add(new IfStatement(
            new LoadArgument(1, "inner", Boolean),
            innerThen,
            elseArm: null));
        nested.Add(new ExpressionStatement(new Constant(2, Int32)));
        nested.Add(new ExpressionStatement(new Constant(3, Int32)));
        nested.Add(new ExpressionStatement(new Constant(4, Int32)));
        nested.Add(new Return(new Constant(5, Int32)));
        var entry = new Block(0);
        entry.Add(new IfStatement(
            new LoadArgument(0, "outer", Boolean),
            nested,
            elseArm: null));
        entry.Add(new Return(new Constant(0, Int32)));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "Nested",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Int32,
                [
                    new Parameter("outer", Boolean),
                    new Parameter("inner", Boolean),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Contains(
            "_ = 1;\n" +
            "    }\n\n" +
            "    _ = 2;",
            output);
        Assert.Equal(1, output.Split("\n\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void GeneratedUnsafeRun_CarriesSpacingStateBetweenOriginalStatements()
    {
        var pointer = TypeRef.Pointer(Int32);
        var entry = new Block(0);
        entry.Add(new ExpressionStatement(new Constant(1, Int32)));
        entry.Add(new ExpressionStatement(new Constant(2, Int32)));
        entry.Add(UnsafeReturningIf(pointer, 1));
        entry.Add(UnsafeReturningIf(pointer, 2));
        entry.Add(new Return(new Constant(0, Int32)));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "UnsafeRun",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Int32,
                [new Parameter("pointer", pointer)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Contains(
            "return 1;\n" +
            "    }\n\n" +
            "    if ((*pointer) != 0)",
            output);
        Assert.Contains("    }\n}\n\nreturn 0;", output);
        Assert.Equal(2, output.Split("\n\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void GeneratedUnsafeRun_PlacesLeadingSeparatorBeforeBlock()
    {
        var pointer = TypeRef.Pointer(Int32);
        var entry = new Block(0);
        entry.Add(ReturningIf("flag", 0, 7));
        entry.Add(UnsafeReturningIf(pointer, 2, parameterIndex: 1));
        entry.Add(new ExpressionStatement(new Constant(3, Int32)));
        entry.Add(new ExpressionStatement(new Constant(4, Int32)));
        entry.Add(new Return(new Constant(0, Int32)));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "UnsafeRunLeadingSeparator",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Int32,
                [
                    new Parameter("flag", Boolean),
                    new Parameter("pointer", pointer),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Contains("return 7;\n}\n\nunsafe\n{", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiveStatementBlockLambda_PreservesSemanticSpacing(bool hasLocal)
    {
        string output = PrintFiveStatementBlockLambda(hasLocal);

        Assert.Contains(
            "return -1;\n" +
            "    }\n\n" +
            "    _ = 1;",
            output);
        Assert.DoesNotContain("/* IfStatement */", output);
        Assert.Equal(1, output.Split("\n\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void InsertedBlankLines_StayOutsideStatementRangesAndPortableCoordinatesRemainExact()
    {
        var (output, ranges) = Print(nameof(SemanticSpacingFixture.Grouped));
        var map = PrintedBodyMap.Create(ranges);
        var switchStatement = Assert.Single(
            ranges,
            range => range.Node is Switch);
        var precedingConditional = Assert.Single(
            ranges,
            range => range.Node is IfStatement
                && output[range.Characters].Contains(
                    "if (length == 0)",
                    StringComparison.Ordinal));

        int start = switchStatement.Characters.Start.GetOffset(output.Length);
        int precedingEnd =
            precedingConditional.Characters.End.GetOffset(output.Length);
        Assert.Equal("switch (kind)", output[start..].Split('\n')[0].TrimStart());
        Assert.NotEqual('\n', output[start]);
        Assert.Equal(start - 1, precedingEnd);
        Assert.Equal('\n', output[precedingEnd - 1]);
        Assert.Equal('\n', output[precedingEnd]);

        Assert.True(ranges.TryGetExtent(switchStatement.Node, out var extent));
        Assert.Equal(output[..start].Count(character => character == '\n'), extent.StartLine);
        Assert.Equal("switch (kind)", map.Lines[extent.StartLine].TrimStart());
        Assert.Equal("", map.Lines[extent.StartLine - 1]);
    }

    [Fact]
    public void InsertedBlankLines_RebaseAnnotatedSourceDocumentSpans()
    {
        using var source = MetadataSource.Open(typeof(SemanticSpacingFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(SemanticSpacingFixture).FullName!,
            nameof(SemanticSpacingFixture.Grouped),
            SourceDocument: true));
        Assert.Null(projection.SourceDocumentFailure);
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);
        var switchNode = Assert.Single(
            document.Nodes,
            node => node.Medium == SourceLineKind.CSharp
                && node.Kind == "SwitchStatement");

        string selected = string.Concat(
            switchNode.Spans.Select(
                span => document.Text.Substring(span.Start, span.Length)));
        Assert.StartsWith("switch (kind)\n{", selected, StringComparison.Ordinal);
        Assert.Equal(
            document.Text.IndexOf("switch (kind)", StringComparison.Ordinal),
            switchNode.Spans[0].Start);
    }

    static IfStatement ReturningIf(
        string parameterName,
        int parameterIndex,
        int value)
    {
        var then = new Block();
        then.Add(new Return(new Constant(value, Int32)));
        return new IfStatement(
            new LoadArgument(parameterIndex, parameterName, Boolean),
            then,
            elseArm: null);
    }

    static IfStatement UnsafeReturningIf(
        TypeRef pointer,
        int value,
        int parameterIndex = 0)
    {
        var then = new Block();
        then.Add(new Return(new Constant(value, Int32)));
        return new IfStatement(
            new Comparison(
                ComparisonKind.NotEqual,
                isUnsigned: false,
                new LoadIndirect(
                    Int32,
                    new LoadArgument(parameterIndex, "pointer", pointer)),
                new Constant(0, Int32)),
            then,
            elseArm: null);
    }

    static string PrintFiveStatementBlockLambda(bool hasLocal)
    {
        var delegateType = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Func`2"),
            [Int32, Int32]);
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(ReturningIf("value", 0, -1));
        lambdaBlock.Add(new ExpressionStatement(new Constant(1, Int32)));
        lambdaBlock.Add(new ExpressionStatement(new Constant(2, Int32)));
        lambdaBlock.Add(new ExpressionStatement(new Constant(3, Int32)));
        lambdaBlock.Add(new Return(new LoadArgument(0, "value", Int32)));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            delegateType,
            [new Parameter("value", Int32)],
            hasLocal ? [Int32] : [],
            hasLocal ? [null] : [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);

        var outerBlock = new Block(0);
        outerBlock.Add(new Return(lambda));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var function = new IrFunction(
            "Lambda",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                delegateType,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            outerBody);

        return Assert.IsType<string>(CSharpPrinter.Print(function).Output);
    }

    static string PrintLoopConditional(IrNode thenStatement)
    {
        var then = new Block();
        then.Add(thenStatement);
        var loopBody = new Block();
        loopBody.Add(new ExpressionStatement(new Constant(1, Int32)));
        loopBody.Add(new ExpressionStatement(new Constant(2, Int32)));
        loopBody.Add(new IfStatement(
            new LoadArgument(0, "flag", Boolean),
            then,
            elseArm: null));
        loopBody.Add(new ExpressionStatement(new Constant(3, Int32)));
        loopBody.Add(new ExpressionStatement(new Constant(4, Int32)));
        var entry = new Block(0);
        entry.Add(new WhileLoop(
            new Constant(true, Boolean),
            loopBody));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "LoopConditional",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Void,
                [new Parameter("flag", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        return Assert.IsType<string>(CSharpPrinter.Print(function).Output);
    }
}
