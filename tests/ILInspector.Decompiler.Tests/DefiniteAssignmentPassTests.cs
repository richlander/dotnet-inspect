using System.Security.Cryptography;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Gates for value-typed-emission.md, Instance 3 (definite assignment decided
/// before printing): <see cref="DefiniteAssignmentPass"/> issues the up-front
/// locals that keep <c>= default</c> on the function and every raised nested
/// body, residual-bound locals are excluded as an input, the printer only
/// spells the issued set, an undecided body fails visibly, and a body
/// rewritten after the decision fails the freshness invariant instead of
/// printing a stale initializer.
/// </summary>
[Trait("Area", "Pass")]
public class DefiniteAssignmentPassTests
{
    static readonly TypeRef Holder = TypeRef.Definition("Synthetic", "Samples", "Holder", ValueTypeHint.ReferenceType);
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");

    [Fact]
    public void IssuesTheReadBeforeAssignSetForTheHostBody()
    {
        // V_1 is assigned before every read; V_0 is assigned only on one arm
        // and read after the join, so only V_0 keeps `= default`.
        var function = ConditionalAssignment();

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        Assert.Equal([0], function.ZeroInitializedLocals!.Order());
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("int V_0 = default;", output);
        Assert.DoesNotContain("V_1 = default", output);
    }

    [Fact]
    public void ResidualBoundLocalsAreExcludedFromTheIssuedSet()
    {
        // Store long, load int: the web splits and the int piece is read on a
        // path no store of that piece reaches. Definite assignment marks it
        // read-before-assign, but a residual piece is a binding gap that must
        // stay CS0165-visible, so the pass leaves it out.
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "l", Int64)));
        block.Add(new Return(new LoadStackSlot(0, Int32)));
        var function = Function(Int32, block, [new Parameter("l", Int64)]);
        new ResidualSlotBindingPass().Run(function, PassContext.None);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        int gap = Assert.Single(
            DefiniteAssignment.Compute(function, ReferenceOwnership.CollectBranchTargets(function), facts: null),
            index => function.ResidualSlotBindings.ContainsKey(index));
        Assert.DoesNotContain(gap, function.ZeroInitializedLocals!);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("int S_0_1;", output);
        Assert.DoesNotContain("= default", output);
    }

    [Fact]
    public void NestedBodiesCarryTheirOwnIssuedSet()
    {
        // A local function and a lambda whose own V_0 is assigned on one arm
        // and read after the join: the pass issues each body's set on its
        // node, and the nested printers restore it when they re-enter the body.
        var localFunction = new LocalFunctionStatement(
            "Inner", Int32, [new Parameter("choose", Bool)], isStatic: true, [Int32, Int32], [],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, ConditionalAssignmentBody());
        var func = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`2"), [Bool, Int32]);
        var lambda = new Lambda(
            func, [new Parameter("choose", Bool)], [Int32, Int32], [],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, ConditionalAssignmentBody());

        var block = new Block(0);
        block.Add(localFunction);
        block.Add(new StoreLocal(0, func, lambda));
        block.Add(new Return(new Constant(0, Int32)));
        var function = Function(Int32, block, [], locals: [func]);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        Assert.Equal([0], localFunction.ZeroInitializedLocals!.Order());
        Assert.Equal([0], lambda.ZeroInitializedLocals!.Order());
        Assert.Empty(function.ZeroInitializedLocals!);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(output, @"\bint V_0(_\d+)? = default;").Count);
    }

    [Fact]
    public void AnUndecidedBodyFailsVisibly()
    {
        var function = ConditionalAssignment();

        var result = CSharpPrinter.Print(function);

        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Message.Contains("definite assignment was not decided", StringComparison.Ordinal));
    }

    [Fact]
    public void ABodyRewrittenAfterTheDecisionFailsTheFreshnessInvariant()
    {
        Assert.True(IrInvariants.Enabled, "the freshness check runs under IrInvariants, on by default in tests");
        var function = ConditionalAssignment();
        new DefiniteAssignmentPass().Run(function, PassContext.None);

        // A later rewrite stops reading V_0 after the join: a fresh decision
        // is now empty, but the issued set still says V_0 keeps `= default`.
        var read = function.Descendants.OfType<Return>().Single().Value!;
        read.ReplaceWith(new LoadLocal(1, Int32));

        var result = CSharpPrinter.Print(function);

        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Message.Contains("differ from a fresh decision", StringComparison.Ordinal));
    }

    [Fact]
    public void RealResidualGapStaysBare()
    {
        // Newtonsoft.Json 13.0.4 JsonSerializer.PopulateInternal: a residual
        // split piece that definite assignment marks read-before-assign. The
        // pass excludes it, so the declaration stays bare.
        string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", "ReferenceConditional", "Newtonsoft.Json.dll");
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = IrImporter.Import(source, "Newtonsoft.Json.JsonSerializer", "PopulateInternal");
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));

        var readEarly = DefiniteAssignment.Compute(function, ReferenceOwnership.CollectBranchTargets(function), facts: null);
        var gaps = readEarly.Where(index => function.ResidualSlotBindings.ContainsKey(index)).ToList();
        Assert.NotEmpty(gaps);
        Assert.All(gaps, gap => Assert.DoesNotContain(gap, function.ZeroInitializedLocals!));
        Assert.NotNull(CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void NestedBodiesAreDecidedOnTheFinalTree()
    {
        // BinderFactory.MakeCrefBinder: host passes rewrite the local function
        // `getBinder` after it is raised (IsPatternPass changes its decided
        // set), so the set is taken at the host's tail. Every nested body's
        // issued set must equal a fresh decision over its final body.
        string path = typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = IrImporter.Import(source, "Microsoft.CodeAnalysis.CSharp.BinderFactory", "MakeCrefBinder");
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));

        var nested = function.Descendants.OfType<LocalFunctionStatement>().ToList();
        Assert.NotEmpty(nested);
        foreach (var localFunction in nested)
        {
            var fresh = DefiniteAssignment.Compute(localFunction.Body, localFunction.Locals.Length, ReferenceOwnership.CollectBranchTargets(localFunction.Body), facts: null)
                .Where(index => !localFunction.ResidualSlotBindings.ContainsKey(index));
            Assert.True(localFunction.ZeroInitializedLocals!.SetEquals(fresh));
        }
        Assert.NotNull(CSharpPrinter.Print(function).Output);
    }

    static IrFunction ConditionalAssignment()
    {
        var body = ConditionalAssignmentBody();
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [new Parameter("choose", Bool)], HasThis: false, GenericParameterCount: 0),
            [Int32, Int32],
            body);
    }

    static BlockContainer ConditionalAssignmentBody()
    {
        var block = new Block(0);
        block.Add(new StoreLocal(1, Int32, new Constant(2, Int32)));
        var then = new Block(1);
        then.Add(new StoreLocal(0, Int32, new LoadLocal(1, Int32)));
        block.Add(new IfStatement(new LoadArgument(0, "choose", Bool), then, null));
        block.Add(new Return(new LoadLocal(0, Int32)));
        var body = new BlockContainer();
        body.Add(block);
        return body;
    }

    static IrFunction Function(TypeRef returnType, Block block, IReadOnlyList<Parameter> parameters, IReadOnlyList<TypeRef>? locals = null)
    {
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(returnType, [.. parameters], HasThis: false, GenericParameterCount: 0),
            [.. locals ?? []],
            body);
    }
}
