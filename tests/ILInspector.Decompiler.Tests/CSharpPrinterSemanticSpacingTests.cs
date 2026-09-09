using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
    public enum CommentOnlySiblingKind
    {
        UnsupportedExpression,
        UnsupportedStatement,
        CopyBlock,
        EndFinally,
        EndFilter,
    }

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
    public void LongMethod_SeparatesSetupAndCompletedConditionalGroups()
    {
        var (output, _) = Print(nameof(SemanticSpacingFixture.Grouped));

        Assert.Contains(
            "throw new ArgumentNullException(\"first\");\n" +
            "}\n\n" +
            "first = first.Trim();",
            output);
        Assert.Contains(
            "int length = first.Length + second.Length;\n" +
            "\n" +
            "if (length == 0)",
            output);
        Assert.Contains(
            "return -1;\n" +
            "}\n\n" +
            "switch (kind)",
            output);
        Assert.Equal(3, output.Split("\n\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void LongMethod_SeparatesSiblingControlFlowGroups()
    {
        var (output, _) = Print(nameof(SemanticSpacingFixture.SiblingControlFlow));

        Assert.Contains("int length = first.Length + second.Length;\n\nif (length > 10)", output);
        Assert.Contains(
            "GC.KeepAlive(length);\n" +
            "}\n\n" +
            "if (kind == 0)",
            output);
        Assert.Equal(2, output.Split("\n\n", StringSplitOptions.None).Length - 1);
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

    [Theory]
    [InlineData(CommentOnlySiblingKind.UnsupportedExpression)]
    [InlineData(CommentOnlySiblingKind.UnsupportedStatement)]
    [InlineData(CommentOnlySiblingKind.CopyBlock)]
    [InlineData(CommentOnlySiblingKind.EndFinally)]
    [InlineData(CommentOnlySiblingKind.EndFilter)]
    public void FourVisibleStatements_DoNotCountCommentOnlySibling(
        CommentOnlySiblingKind kind)
    {
        var (commentOnly, expectedComment) = CommentOnlySibling(kind);
        var then = new Block();
        then.Add(new ExpressionStatement(new Constant(1, Int32)));
        var entry = new Block(0);
        entry.Add(new IfStatement(
            new LoadArgument(0, "flag", Boolean),
            then,
            elseArm: null));
        entry.Add(new ExpressionStatement(new Constant(2, Int32)));
        entry.Add(new ExpressionStatement(new Constant(3, Int32)));
        entry.Add(new ExpressionStatement(new Constant(4, Int32)));
        entry.Add(commentOnly);
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "CommentOnlyThreshold",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Void,
                [new Parameter("flag", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Contains(
            "_ = 1;\n" +
            "}\n" +
            "_ = 2;",
            output);
        Assert.Contains(expectedComment, output);
        Assert.DoesNotContain("\n\n", output);
    }

    [Fact]
    public void CompilerProducedCopyBlock_SeparatesGeneratedUnsafeGroups()
    {
        var (output, _) = Print(
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.StackallocInitializerNegatives),
            nameof(ILInspector.Decompiler.Fixtures.NewUnsafe.StackallocInitializerNegatives.StackallocBooleanInitializer));

        Assert.Contains(
            "    values = (bool*)S_256;\n\n" +
            "    if (!(*values))",
            output);
        Assert.Contains("    }\n}\n\nreturn true;", output);
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
    public void LongMethod_KeepsSingleSetupBeforeControlFlowCompact()
    {
        var firstThen = new Block();
        firstThen.Add(new ExpressionStatement(new Constant(1, Int32)));
        var secondThen = new Block();
        secondThen.Add(new ExpressionStatement(new Constant(3, Int32)));
        var entry = new Block(0);
        entry.Add(new IfStatement(
            new LoadArgument(0, "first", Boolean),
            firstThen,
            elseArm: null));
        entry.Add(new ExpressionStatement(new Constant(2, Int32)));
        entry.Add(new IfStatement(
            new LoadArgument(1, "second", Boolean),
            secondThen,
            elseArm: null));
        entry.Add(new ExpressionStatement(new Constant(4, Int32)));
        entry.Add(new Return(new Constant(0, Int32)));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "SingleSetupBoundary",
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

        Assert.Contains("_ = 2;\nif (second)", output);
        Assert.DoesNotContain("_ = 2;\n\nif (second)", output);
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

        Assert.Contains("_ = 2;\n\n    if (flag)", output);
        Assert.Contains(
            $"{keyword};\n" +
            "    }\n\n" +
            "    _ = 3;",
            output);
        Assert.Equal(2, output.Split("\n\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void LoopBody_KeepsNonTerminatingConditionalCompact()
    {
        string output = PrintLoopConditional(
            new ExpressionStatement(new Constant(99, Int32)));

        Assert.Contains("_ = 2;\n\n    if (flag)", output);
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
        Assert.Equal(1, output.Split("\n\n", StringSplitOptions.None).Length - 1);
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

        Assert.Contains("_ = 2;\n\nunsafe\n{", output);
        Assert.Contains(
            "return 1;\n" +
            "    }\n\n" +
            "    if ((*pointer) != 0)",
            output);
        Assert.Contains("    }\n}\n\nreturn 0;", output);
        Assert.Equal(3, output.Split("\n\n", StringSplitOptions.None).Length - 1);
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

    [Fact]
    public void GeneratedUnsafeRun_UsesFirstVisibleMemberForSeparator()
    {
        var pointer = TypeRef.Pointer(Int32);
        var copy = new CopyBlock(
            new LoadIndirect(
                Int32,
                new LoadArgument(1, "pointer", pointer)),
            new LoadArgument(1, "pointer", pointer),
            new Constant(4, Int32));
        var entry = new Block(0);
        entry.Add(ReturningIf("flag", 0, 7));
        entry.Add(copy);
        entry.Add(UnsafeReturningIf(pointer, 2, parameterIndex: 1));
        entry.Add(new ExpressionStatement(new Constant(3, Int32)));
        entry.Add(new ExpressionStatement(new Constant(4, Int32)));
        entry.Add(new Return(new Constant(0, Int32)));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "UnsafeRunCommentLeader",
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
        Assert.Contains(
            "    /* unsupported cpblk */\n" +
            "    if ((*pointer) != 0)",
            output);
        Assert.DoesNotContain(
            "    /* unsupported cpblk */\n\n" +
            "    if ((*pointer) != 0)",
            output);
        Assert.Contains("    }\n}\n\n_ = 3;", output);
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
    public void SharedScopeBlockLambda_UsesLambdaReturnType()
    {
        var funcBool = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Func`1"),
            [Boolean]);
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new ExpressionStatement(new Constant(0, Int32)));
        lambdaBlock.Add(new Return(new Comparison(
            ComparisonKind.NotEqual,
            isUnsigned: false,
            new Constant(1, Int32),
            new Constant(0, Int32))));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            funcBool,
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);
        var outerBlock = new Block(0);
        outerBlock.Add(new StoreArgument(
            0,
            "predicate",
            funcBool,
            lambda));
        outerBlock.Add(new Return(new Constant(0, Int32)));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var function = new IrFunction(
            "LambdaReturnContext",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Int32,
                [new Parameter("predicate", funcBool)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            outerBody);

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Equal(
            "predicate = () =>\n" +
            "{\n" +
            "    _ = 0;\n" +
            "    return 1 != 0;\n" +
            "};\n" +
            "return 0;\n",
            output);
    }

    [Fact]
    public void SharedScopeBlockLambda_StructuredRegionsStayLaminar()
    {
        var funcInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Func`1"),
            [Int32]);
        var then = new Block();
        then.Add(new Return(new Constant(1, Int32)));
        var conditional = new IfStatement(
            new Constant(true, Boolean),
            then,
            elseArm: null);
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(conditional);
        lambdaBlock.Add(new Return(new Constant(0, Int32)));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            funcInt,
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);
        var outerBlock = new Block(0);
        outerBlock.Add(new Return(lambda));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var function = new IrFunction(
            "LambdaRanges",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                funcInt,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            outerBody);

        var result = CSharpPrinter.Print(function, out var ranges);

        Assert.IsType<string>(result.Output);
        Assert.Empty(ranges.PrintedRegions);
        Assert.True(ranges.TryGetRange(lambda, out _));
        _ = PrintedBodyMap.Create(ranges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlockLambda_UsesIndependentLabelScope(bool hasLocal)
    {
        var funcInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Func`1"),
            [Int32]);
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new Branch(0x10));
        var collidingStatement = new ExpressionStatement(new Constant(7, Int32));
        collidingStatement.SetSourceOffset(0x20);
        lambdaBlock.Add(collidingStatement);
        var lambdaTarget = new Return(new Constant(0, Int32));
        lambdaTarget.SetSourceOffset(0x10);
        lambdaBlock.Add(lambdaTarget);
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            funcInt,
            [],
            hasLocal ? [Int32] : [],
            hasLocal ? [null] : [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);
        var outerBlock = new Block(0);
        outerBlock.Add(new StoreArgument(
            0,
            "callback",
            funcInt,
            lambda));
        outerBlock.Add(new Branch(0x20));
        var outerTarget = new Return(new Constant(1, Int32));
        outerTarget.SetSourceOffset(0x20);
        outerBlock.Add(outerTarget);
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var function = new IrFunction(
            "LambdaLabelScopes",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Int32,
                [new Parameter("callback", funcInt)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            outerBody);

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Contains(
            "    goto IL_0010_scope1;\n" +
            "    _ = 7;\n" +
            "    IL_0010_scope1:\n" +
            "    return 0;",
            output);
        Assert.DoesNotContain("IL_0020_scope1", output);
        Assert.Contains(
            "};\n" +
            "goto IL_0020;\n" +
            "IL_0020:\n" +
            "return 1;",
            output);
        AssertBodyCompiles("public static int M(Func<int> callback)", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedLocalFunction_UsesIndependentLabelScope(bool hasLocal)
    {
        var localBlock = new Block(0);
        localBlock.Add(new Branch(0x10));
        var localTarget = new Return(new Constant(0, Int32));
        localTarget.SetSourceOffset(0x10);
        localBlock.Add(localTarget);
        var localBody = new BlockContainer();
        localBody.Add(localBlock);
        var localFunction = new LocalFunctionStatement(
            "Local",
            Int32,
            [],
            isStatic: false,
            hasLocal ? [Int32] : [],
            hasLocal ? [null] : [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localBody);
        var outerBlock = new Block(0);
        outerBlock.Add(localFunction);
        outerBlock.Add(new Branch(0x10));
        var outerTarget = new Return(new Constant(1, Int32));
        outerTarget.SetSourceOffset(0x10);
        outerBlock.Add(outerTarget);
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var function = new IrFunction(
            "LocalFunctionLabelScopes",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Int32,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            outerBody);

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Contains(
            "    goto IL_0010_scope1;\n" +
            "    IL_0010_scope1:\n" +
            "    return 0;",
            output);
        Assert.Contains(
            "}\n" +
            "goto IL_0010;\n" +
            "IL_0010:\n" +
            "return 1;",
            output);
        AssertBodyCompiles("public static int M()", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleStructuredLambda_WithLineCommentsUsesMultilineBlock(bool hasLocal)
    {
        var action = TypeRef.CoreLib("System", "Action");
        var tryBody = new BlockContainer();
        var leaveBlock = new Block(0);
        leaveBlock.Add(new Leave(0x30));
        var leaveTarget = new Block(0x30);
        leaveTarget.Add(new ExpressionStatement(new Constant(9, Int32)));
        tryBody.Add(leaveBlock);
        tryBody.Add(leaveTarget);
        var finallyBody = new BlockContainer();
        var finallyBlock = new Block(0x10);
        finallyBlock.Add(new ExpressionStatement(new Constant(3, Int32)));
        finallyBlock.Add(new EndFinally());
        finallyBody.Add(finallyBlock);
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new TryFinally(tryBody, finallyBody));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            action,
            [],
            hasLocal ? [Int32] : [],
            hasLocal ? [null] : [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);
        var outerBlock = new Block(0);
        outerBlock.Add(new StoreArgument(
            0,
            "callback",
            action,
            lambda));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var function = new IrFunction(
            "LambdaLineComments",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Void,
                [new Parameter("callback", action)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            outerBody);

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Contains(
            "callback = () =>\n" +
            "{\n" +
            "    try",
            output);
        Assert.DoesNotContain("() => { try", output);
        Assert.Contains("// leave\n", output);
        Assert.Contains("// endfinally\n", output);
        AssertBodyCompiles("public static void M(Action callback)", output);
    }

    [Fact]
    public void SharedScopeBlockLambda_RebasesInlineExpressionRanges()
    {
        var funcInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Func`2"),
            [Int32, Int32]);
        var returnedSwitch = new SwitchExpression(
            new LoadArgument(0, "value", Int32),
            [
                new SwitchExpressionArm(
                    [0],
                    isDefault: false,
                    new Constant(11, Int32)),
                new SwitchExpressionArm(
                    [],
                    isDefault: true,
                    new Constant(22, Int32)),
            ]);
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new ExpressionStatement(new Constant(9, Int32)));
        lambdaBlock.Add(new Return(returnedSwitch));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            funcInt,
            [new Parameter("value", Int32)],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);
        var outerBlock = new Block(0);
        outerBlock.Add(new Return(lambda));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var function = new IrFunction(
            "LambdaExpressionRanges",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                funcInt,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            outerBody);

        var result = CSharpPrinter.Print(function, out var ranges);
        string output = Assert.IsType<string>(result.Output);

        Assert.True(ranges.TryGetRange(returnedSwitch, out var range));
        Assert.Equal(
            "value switch { 0 => 11, _ => 22 }",
            output[range]);
        _ = PrintedBodyMap.Create(ranges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiblingBlockLambdas_RestoreEnclosingIndent(bool secondHasLocal)
    {
        var funcInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Func`2"),
            [Int32, Int32]);
        var register = new MethodRef(
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            "Register",
            Void,
            [funcInt, funcInt],
            HasThis: false);
        var then = new Block();
        then.Add(new ExpressionStatement(new Call(
            register,
            isVirtual: false,
            [
                ThreeStatementLambda(funcInt, first: 1, second: 2, hasLocal: false),
                ThreeStatementLambda(funcInt, first: 3, second: 4, secondHasLocal),
            ])));
        var entry = new Block(0);
        entry.Add(new IfStatement(
            new Constant(true, Boolean),
            then,
            elseArm: null));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "SiblingLambdas",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Void,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Contains(
            "    }, value =>\n" +
            "    {\n",
            output);
        Assert.DoesNotContain("}, value =>\n{", output);
    }

    [Fact]
    public void SharedScopeBlockLambda_NestedLocalFunctionUsesOwnReturnType()
    {
        var funcInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Func`1"),
            [Int32]);
        var localBlock = new Block(0);
        localBlock.Add(new ExpressionStatement(new Constant(0, Int32)));
        localBlock.Add(new Return(new Comparison(
            ComparisonKind.NotEqual,
            isUnsigned: false,
            new Constant(1, Int32),
            new Constant(0, Int32))));
        var localBody = new BlockContainer();
        localBody.Add(localBlock);
        var localFunction = new LocalFunctionStatement(
            "Local",
            Boolean,
            [],
            isStatic: false,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localBody);
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(localFunction);
        lambdaBlock.Add(new Return(new Constant(0, Int32)));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            funcInt,
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);
        var outerBlock = new Block(0);
        outerBlock.Add(new StoreArgument(
            0,
            "callback",
            funcInt,
            lambda));
        outerBlock.Add(new Return(new Constant(0, Int32)));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var function = new IrFunction(
            "LambdaLocalFunction",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Int32,
                [new Parameter("callback", funcInt)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            outerBody);

        var output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        Assert.Contains(
            "bool Local()\n" +
            "    {\n" +
            "        _ = 0;\n" +
            "        return 1 != 0;\n" +
            "    }",
            output);
        Assert.DoesNotContain("return 1 != 0 ? 1 : 0;", output);
    }

    [Fact]
    public void SharedScopeBlockLambda_PreservesPointerArithmeticKind()
    {
        var pointer = TypeRef.Pointer(Int32);
        var delegateType = TypeRef.Definition(
            "synthetic",
            "",
            "PointerCallback");
        var pointerArithmetic = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "pointer", pointer),
            new LoadArgument(1, "offset", Int32));
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new ExpressionStatement(new Constant(0, Int32)));
        lambdaBlock.Add(new Return(pointerArithmetic));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            delegateType,
            [
                new Parameter("pointer", pointer),
                new Parameter("offset", Int32),
            ],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);
        var outerBlock = new Block(0);
        outerBlock.Add(new StoreArgument(
            0,
            "callback",
            delegateType,
            lambda));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var function = new IrFunction(
            "LambdaPointerKind",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Void,
                [new Parameter("callback", delegateType)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            outerBody);

        AssertRangeAndKind(
            function,
            pointerArithmetic,
            "(int*)((byte*)pointer + offset)",
            "ConversionExpression");
    }

    [Fact]
    public void SharedScopeBlockLambda_PreservesArrayPseudoMemberKind()
    {
        var arrayType = TypeRef.MdArray(Int32, 2);
        var tupleType = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "ValueTuple`2"),
            [Int32, Int32]);
        var funcInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Func`1"),
            [Int32]);
        var load = new LoadElement(
            Int32,
            new LoadArgument(0, "array", arrayType),
            new TupleExpression(
                tupleType,
                [
                    new LoadLocal(0, Int32),
                    new LoadLocal(0, Int32),
                ]));
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new ExpressionStatement(new Constant(0, Int32)));
        lambdaBlock.Add(new Return(load));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            funcInt,
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);
        var outerBlock = new Block(0);
        outerBlock.Add(new StoreLocal(
            0,
            Int32,
            new Constant(0, Int32)));
        outerBlock.Add(new StoreArgument(
            1,
            "callback",
            funcInt,
            lambda));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);
        var function = new IrFunction(
            "LambdaArrayKind",
            TypeRef.CoreLib("Synthetic", "SemanticSpacing"),
            new MethodSignature(
                Void,
                [
                    new Parameter("array", arrayType),
                    new Parameter("callback", funcInt),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [Int32],
            outerBody);

        AssertRangeAndKind(
            function,
            load,
            "array.Get(V_0, V_0)",
            "InvocationExpression");
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

    static (IrNode Node, string ExpectedComment) CommentOnlySibling(
        CommentOnlySiblingKind kind)
        => kind switch
        {
            CommentOnlySiblingKind.UnsupportedExpression => (
                new UnsupportedNode(0x10, "probe", "comment-only sibling"),
                "/* Unsupported IL_0010 probe: comment-only sibling */"),
            CommentOnlySiblingKind.UnsupportedStatement => (
                new ExpressionStatement(
                    new UnsupportedNode(0x10, "probe", "comment-only sibling")),
                "/* Unsupported IL_0010 probe: comment-only sibling */"),
            CommentOnlySiblingKind.CopyBlock => (
                new CopyBlock(
                    new Constant(0, Int32),
                    new Constant(0, Int32),
                    new Constant(4, Int32)),
                "/* unsupported cpblk */"),
            CommentOnlySiblingKind.EndFinally => (
                new EndFinally(),
                "// endfinally"),
            CommentOnlySiblingKind.EndFilter => (
                new EndFilter(new Constant(0, Int32)),
                "// endfilter(0)"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

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

    static Lambda ThreeStatementLambda(
        TypeRef delegateType,
        int first,
        int second,
        bool hasLocal)
    {
        var block = new Block(0);
        block.Add(new ExpressionStatement(new Constant(first, Int32)));
        block.Add(new ExpressionStatement(new Constant(second, Int32)));
        block.Add(new Return(new LoadArgument(0, "value", Int32)));
        var body = new BlockContainer();
        body.Add(block);
        return new Lambda(
            delegateType,
            [new Parameter("value", Int32)],
            hasLocal ? [Int32] : [],
            hasLocal ? [null] : [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            body);
    }

    static void AssertRangeAndKind(
        IrFunction function,
        IrNode node,
        string expectedText,
        string expectedKind)
    {
        var result = CSharpPrinter.Print(function, out var ranges);
        string output = Assert.IsType<string>(result.Output);

        Assert.True(ranges.TryGetRange(node, out var range));
        Assert.Equal(expectedText, output[range]);
        Assert.True(ranges.TryGetNodeKind(node, out string? kind));
        Assert.Equal(expectedKind, kind);
        Assert.True(ranges.TryGetExtent(node, out var extent));
        var map = PrintedBodyMap.Create(ranges);
        Assert.Equal(
            expectedKind,
            Assert.Single(map.Nodes, candidate => candidate.Extent == extent).Kind);
    }

    static void AssertBodyCompiles(string methodHeader, string body)
    {
        string source = $$"""
            using System;
            static class Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "semantic-spacing-lambda-gate",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}")
            .ToArray();

        Assert.True(
            errors.Length == 0,
            "Rendered body must compile, got:\n  "
            + string.Join("\n  ", errors)
            + "\n--- body ---\n"
            + body);
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
