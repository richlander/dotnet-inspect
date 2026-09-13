using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Printer-only regression coverage for the #2952 follow-up (PR #2987 adversarial
// review): _statementIndent is set unconditionally at AppendStatement entry with
// no general save/restore, which is only safe when a statement's own expression
// composition happens entirely *before* any nested child-block render. DoWhileLoop
// (condition evaluated after the body) and TryCatch (catch header/filter evaluated
// after the try body and each preceding catch body) are the two counterexamples —
// both are fixed by resetting _statementIndent right before the subsequent
// expression composes. These tests build the IR directly (bypassing
// LambdaRaisingPass, which does not raise a lambda passed as a delegate-cache
// argument inside a loop condition or catch filter in this environment) so the
// printer's indent bookkeeping is exercised in isolation.
[Trait("Area", "Pass")]
public class CSharpPrinterStatementIndentTests
{
    static readonly TypeRef Holder = TypeRef.Definition("synthetic", "", "Holder");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef ListOfInt = TypeRef.GenericInstance(TypeRef.Definition("System.Private.CoreLib", "System.Collections.Generic", "List`1"), [Int32]);

    static Lambda MultiStatementPredicate()
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new ExpressionStatement(new Call(new MethodRef(Holder, "Log", Void, [Int32], HasThis: false), isVirtual: false, [new LoadArgument(0, "x", Int32)])));
        block.Add(new Return(new Comparison(ComparisonKind.GreaterThan, isUnsigned: false, new LoadArgument(0, "x", Int32), new Constant(0, Int32))));
        body.Add(block);
        return new Lambda(
            TypeRef.GenericInstance(TypeRef.Definition("System.Private.CoreLib", "System", "Predicate`1"), [Int32]),
            [new Parameter("x", Int32)],
            [], [], usesUpdatedMemorySafetyRules: false, skipLocalsInit: false,
            body);
    }

    [Fact]
    public void MultiStatementLambda_InDoWhileCondition_AlignsBracesToLoopStatementIndent()
    {
        // The do/while's own body is the deeper (indent-1) container that
        // renders first; the condition (holding the multi-statement lambda)
        // renders after it, so a stale _statementIndent would misalign the
        // lambda's braces to the body's indent instead of the loop's own.
        var loopBody = new BlockContainer();
        var loopBlock = new Block(0);
        loopBlock.Add(new ExpressionStatement(new Call(new MethodRef(Holder, "Tick", Void, [], HasThis: false), isVirtual: false, [])));
        loopBody.Add(loopBlock);

        var condition = new Call(
            new MethodRef(ListOfInt, "Exists", Bool, [TypeRef.GenericInstance(TypeRef.Definition("System.Private.CoreLib", "System", "Predicate`1"), [Int32])], HasThis: true),
            isVirtual: false,
            [new LoadArgument(0, "items", ListOfInt), MultiStatementPredicate()]);

        var doWhile = new DoWhileLoop(loopBody, condition);
        var function = Function(SingleStatement(doWhile));

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.Contains(
            "while (items.Exists(x =>\n" +
            "{\n" +
            "    Log(x);\n" +
            "    return x > 0;\n" +
            "}));",
            output);
    }

    [Fact]
    public void MultiStatementLambda_InCatchFilter_AlignsBracesToCatchStatementIndent()
    {
        // The preceding try body renders before the catch clause's own header
        // (the exception type/`when` filter), so a stale _statementIndent left
        // over from the try body would misalign the filter's lambda braces.
        var tryBody = new BlockContainer();
        var tryBlock = new Block(0);
        tryBlock.Add(new ExpressionStatement(new Call(new MethodRef(Holder, "Tick", Void, [], HasThis: false), isVirtual: false, [])));
        tryBody.Add(tryBlock);

        var filter = new Call(
            new MethodRef(ListOfInt, "Exists", Bool, [TypeRef.GenericInstance(TypeRef.Definition("System.Private.CoreLib", "System", "Predicate`1"), [Int32])], HasThis: true),
            isVirtual: false,
            [new LoadArgument(0, "items", ListOfInt), MultiStatementPredicate()]);

        var catchBody = new BlockContainer();
        var catchBlock = new Block(0);
        catchBlock.Add(new ExpressionStatement(new Call(new MethodRef(Holder, "Handle", Void, [], HasThis: false), isVirtual: false, [])));
        catchBody.Add(catchBlock);

        var catchClause = new CatchClause(TypeRef.CoreLib("System", "Exception"), catchBody, filter);
        var tryCatch = new TryCatch(tryBody, [catchClause]);
        var function = Function(SingleStatement(tryCatch));

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.Contains(
            "catch (Exception) when (items.Exists(x =>\n" +
            "{\n" +
            "    Log(x);\n" +
            "    return x > 0;\n" +
            "}))",
            output);
    }

    static BlockContainer SingleStatement(IrNode statement)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(statement);
        container.Add(block);
        return container;
    }

    static IrFunction Function(BlockContainer body)
        => new(
            "M",
            Holder,
            new MethodSignature(Void, [new Parameter("items", ListOfInt)], HasThis: false, GenericParameterCount: 0),
            ImmutableArray<TypeRef>.Empty,
            body);
}
