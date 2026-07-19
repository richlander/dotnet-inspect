using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Regression coverage for two adversarial-review findings on #2877
// (PatternGuardedShortCircuitPass): a nested lambda/local-function local sharing
// an index with an arm default temp must not be mis-recovered, and a pattern
// nested under `||`/`!` (confined but not definitely assigned) must not fold.
public class PatternGuardedReviewReproTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");

    static IrFunction Function(Block body)
    {
        var container = new BlockContainer();
        container.Add(body);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Tests", "Owner"), signature, [Int, Int], container);
    }

    static IrExpression BoolCompare(IrExpression value)
        => new Comparison(ComparisonKind.GreaterThan, isUnsigned: false, value, new Constant(0, Int));

    // GPT finding: nested lambda local with the same index gets mis-recovered.
    [Fact]
    public void NestedLambdaSameIndex_MustNotMisRecover()
    {
        // Arm has `initobj V_2`; the ONLY read of local 2 in rhs lives inside a
        // nested lambda body (the lambda's own local 2). Recovery must not touch
        // the lambda's local — outer local 2 is never read at outer scope.
        var pattern = new IsPattern(new LoadArgument(0, "arg", ObjectType), Int, localIndex: 1);
        var then = new Block();
        then.Add(new InitObject(Int, new LoadLocalAddress(2, Int)));

        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block();
        lambdaBlock.Add(new Return(new LoadLocal(2, Int)));
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            TypeRef.CoreLib("System", "Func`1"),
            ImmutableArray<Parameter>.Empty,
            [Int],
            [null],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);

        // A bool rhs whose only LoadLocal(2) descendant is inside the lambda.
        var rhs = new Comparison(ComparisonKind.GreaterThan, isUnsigned: false, lambda, new Constant(0, Int));
        then.Add(new StoreStackSlot(256, rhs));

        var elseArm = new Block();
        elseArm.Add(new StoreStackSlot(256, new Constant(false, Bool)));
        var ifs = new IfStatement(pattern, then, elseArm);

        var body = new Block();
        body.Add(ifs);
        var function = Function(body);

        new PatternGuardedShortCircuitPass().Run(function, PassContext.None);

        var defaultsInLambda = function.Descendants.OfType<DefaultValue>()
            .Where(d => ReferenceOwnership.IsInsideNestedFunctionBody(d))
            .ToList();
        Assert.Empty(defaultsInLambda);
    }

    // Gemini finding 1: duplicate initobj for the same index crashes ReplaceWith.
    [Fact]
    public void DuplicateInitObjSameIndex_MustNotCrash()
    {
        var pattern = new IsPattern(new LoadArgument(0, "arg", ObjectType), Int, localIndex: 1);
        var then = new Block();
        then.Add(new InitObject(Int, new LoadLocalAddress(2, Int)));
        then.Add(new InitObject(Int, new LoadLocalAddress(2, Int)));
        then.Add(new StoreStackSlot(256, BoolCompare(new LoadLocal(2, Int))));
        var elseArm = new Block();
        elseArm.Add(new StoreStackSlot(256, new Constant(false, Bool)));
        var ifs = new IfStatement(pattern, then, elseArm);

        var body = new Block();
        body.Add(ifs);
        var function = Function(body);

        // Must not throw.
        new PatternGuardedShortCircuitPass().Run(function, PassContext.None);
    }

    // Gemini finding 2: pattern nested under || is confined but not definitely
    // assigned; folding to && emits CS0165. Must not fold.
    [Fact]
    public void PatternUnderOr_MustNotFold()
    {
        // if (arg0 != 0 || arg1 is int V_1) S_256 = V_1 > 0; else S_256 = false;
        var cond = new LogicalBinary(
            LogicalKind.Or,
            new Comparison(ComparisonKind.NotEqual, isUnsigned: false, new LoadArgument(0, "a", Int), new Constant(0, Int)),
            new IsPattern(new LoadArgument(1, "b", ObjectType), Int, localIndex: 1));
        var then = new Block();
        then.Add(new StoreStackSlot(256, BoolCompare(new LoadLocal(1, Int))));
        var elseArm = new Block();
        elseArm.Add(new StoreStackSlot(256, new Constant(false, Bool)));
        var ifs = new IfStatement(cond, then, elseArm);

        var body = new Block();
        body.Add(ifs);
        var function = Function(body);

        new PatternGuardedShortCircuitPass().Run(function, PassContext.None);

        // Must remain a diamond — folding would read V_1 unassigned (CS0165).
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }
}
