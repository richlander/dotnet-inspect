using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class SlotMaterializationInvariantTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    [Fact]
    public void ProductionRewritePreservesCoupledCallsAndExistingLocals()
    {
        var function = CoupledFunction();
        var invariant = SlotMaterializationInvariant.Capture(function);

        new SlotMaterializationPass().Run(function, PassContext.None);

        invariant.Check();
        Assert.Equal(3, function.Locals.Length);
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void CorrespondenceDoesNotDependOnPlanOrderOrGeneratedNames()
    {
        var function = CoupledFunction();
        var invariant = SlotMaterializationInvariant.Capture(function);
        int second = function.AddSynthesizedLocal(Int32, "second");
        int first = function.AddSynthesizedLocal(Int32, "first");

        RewriteSlot(function, 0, first, Int32);
        RewriteSlot(function, 1, second, Int32);

        invariant.Check();
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData("existing-local", "fresh local")]
    [InlineData("other-web", "inconsistent storage bindings")]
    [InlineData("split-web", "inconsistent storage bindings")]
    [InlineData("partial-web", "inconsistent storage bindings")]
    [InlineData("reference-type", "local table type")]
    [InlineData("clone-producer", "ordered node identity")]
    [InlineData("remove-statement", "child count")]
    [InlineData("reorder-effects", "child count")]
    [InlineData("extra-local", "one-to-one")]
    public void RejectsFaultyCompletedRewrites(string fault, string diagnostic)
    {
        var function = CoupledFunction();
        var oldRead = function.Descendants.OfType<LoadStackSlot>().First();
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        var read = function.Descendants.OfType<LoadLocal>().First();
        var block = Assert.Single(function.Body.Blocks);

        switch (fault)
        {
            case "existing-local":
                read.ReplaceWith(new LoadLocal(0, Int32));
                break;
            case "other-web":
                read.ReplaceWith(new LoadLocal(2, Int32));
                break;
            case "split-web":
                read.ReplaceWith(new LoadLocal(function.AddLocal(Int32), Int32));
                break;
            case "partial-web":
                read.ReplaceWith(oldRead);
                break;
            case "reference-type":
                read.ReplaceWith(new LoadLocal(read.Index, Int64));
                break;
            case "clone-producer":
                var producer = function.Descendants.OfType<Call>().First();
                producer.ReplaceWith(producer.Clone());
                break;
            case "remove-statement":
                block.Children[1].Detach();
                break;
            case "reorder-effects":
                var statements = block.DetachChildren().ToArray();
                (statements[1], statements[2]) = (statements[2], statements[1]);
                foreach (var statement in statements)
                    block.Add(statement);
                break;
            case "extra-local":
                function.AddLocal(Int32);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }

        var error = Assert.Throws<InvalidOperationException>(invariant.Check);
        Assert.Contains("Slot materialization invariant in M:", error.Message);
        Assert.Contains(diagnostic, error.Message);
    }

    [Fact]
    public void RejectsCoalescingDifferentWebs()
    {
        var function = CoupledFunction();
        var invariant = SlotMaterializationInvariant.Capture(function);
        int index = function.AddLocal(Int32);
        RewriteSlot(function, 0, index, Int32);
        RewriteSlot(function, 1, index, Int32);

        var error = Assert.Throws<InvalidOperationException>(invariant.Check);
        Assert.Contains("shared by different slots", error.Message);
    }

    [Fact]
    public void RejectsWrongTargetEvenWhenAllNewReferencesAgree()
    {
        var function = Function(Int32,
            new StoreStackSlot(0, new Constant(1, Int32)),
            new Return(new LoadStackSlot(0, Int32)));
        var invariant = SlotMaterializationInvariant.Capture(function);
        RewriteSlot(function, 0, function.AddLocal(Int64), Int64);

        var error = Assert.Throws<InvalidOperationException>(invariant.Check);
        Assert.Contains("pre-rewrite testimony", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectCopyComponentsMustConvertTogether(bool convertSource)
    {
        var function = Function(Int32,
            new StoreStackSlot(0, new Constant(1, Int32)),
            new StoreStackSlot(1, new LoadStackSlot(0, Int32)),
            new Return(new LoadStackSlot(1, Int32)));
        var invariant = SlotMaterializationInvariant.Capture(function);
        RewriteSlot(function, convertSource ? 0 : 1, function.AddLocal(Int32), Int32);

        var error = Assert.Throws<InvalidOperationException>(invariant.Check);
        Assert.Contains("direct-copy component was only partly materialized", error.Message);
    }

    [Fact]
    public void SameNumberInNestedScopeCannotJoinTheOuterRewrite()
    {
        var nestedBody = new BlockContainer();
        var nestedBlock = new Block(0);
        nestedBlock.Add(new StoreStackSlot(0, new Constant(2, Int32)));
        nestedBlock.Add(new Return(new LoadStackSlot(0, Int32)));
        nestedBody.Add(nestedBlock);
        var lambda = new Lambda(
            TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`1"), [Int32]),
            [], [Int32], [], false, false, nestedBody);
        var function = Function(Int32,
            new StoreStackSlot(0, new Constant(1, Int32)),
            new ExpressionStatement(lambda),
            new Return(new LoadStackSlot(0, Int32)));
        var invariant = SlotMaterializationInvariant.Capture(function);

        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        function.CheckInvariant(includeSemantics: true);

        var nestedRead = Assert.Single(nestedBody.Descendants.OfType<LoadStackSlot>());
        nestedRead.ReplaceWith(new LoadLocal(0, Int32));
        var error = Assert.Throws<InvalidOperationException>(invariant.Check);
        Assert.Contains("ordered node identity", error.Message);
    }

    [Fact]
    public void DeclinedWebCanRemainUnchanged()
    {
        var unknown = TypeRef.Definition("Other", "Samples", "Unknown");
        var function = Function(unknown,
            new StoreStackSlot(0, new Constant(null, unknown)),
            new Return(new LoadStackSlot(0, unknown)));
        var invariant = SlotMaterializationInvariant.Capture(function);

        new SlotMaterializationPass().Run(function, PassContext.None);

        invariant.Check();
        Assert.Empty(function.Locals);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void InterruptedSteppingIsNotCheckedAsACompletedRewrite(int limit)
    {
        var function = CoupledFunction();
        var stepper = new Stepper(enabled: true) { StepLimit = limit };

        Assert.Throws<StepLimitReachedException>(() =>
            new SlotMaterializationPass().Run(function, new PassContext(stepper)));

        Assert.Equal(limit, stepper.Count);
        Assert.NotEmpty(function.Descendants.OfType<StoreStackSlot>());
    }

    [Theory]
    [InlineData(nameof(StringArraySlotMaterializationSamples.ReadAndObserve))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.AllocateAndObserve))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.InitializeAndObserve))]
    [InlineData(nameof(StringArraySlotMaterializationSamples.MutateAndObserve))]
    public void CompilerProducedRetainedValuesPassValidation(string method)
    {
        using var source = MetadataSource.Open(typeof(StringArraySlotMaterializationSamples).Assembly.Location);
        CheckImportedRewrite(source, typeof(StringArraySlotMaterializationSamples).FullName!, method);
    }

    [Theory]
    [InlineData("Roslyn.Utilities.PathUtilities", "ExpandAbsolutePathWithRelativeParts")]
    [InlineData("Microsoft.CodeAnalysis.SyntaxDiffer", "RecordChange")]
    public void RealRoslynMethodsPassValidation(string type, string method)
    {
        using var source = MetadataSource.Open(typeof(SyntaxTree).Assembly.Location);
        CheckImportedRewrite(source, type, method);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Corpus")]
    public void StorageRewritesPreserveTreesAcrossCoreLib()
    {
        Assert.True(IrInvariants.Enabled);
        var prefix = IrPasses.Default.TakeWhile(static pass => pass is not SlotMaterializationPass)
            .ToImmutableArray();
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        int methods = 0, rewrittenMethods = 0, webs = 0;
        foreach (var (_, _, function) in IrImporter.ImportAssembly(source))
        {
            IrPasses.Run(function, prefix);
            int before = function.Locals.Length;
            new SlotMaterializationPass().Run(function, PassContext.None);
            function.CheckInvariant(includeSemantics: true);
            int converted = function.Locals.Length - before;
            methods++;
            if (converted > 0)
                rewrittenMethods++;
            webs += converted;
        }

        Assert.True(methods > 10_000, $"Only {methods} methods were inspected.");
        Assert.True(rewrittenMethods > 100, $"Only {rewrittenMethods} rewrites were exercised.");
        Assert.True(webs >= rewrittenMethods);
        Console.WriteLine($"STORAGE-REWRITE methods={methods} rewritten-methods={rewrittenMethods} webs={webs}");
    }

    static void CheckImportedRewrite(MetadataSource source, string type, string method)
    {
        var function = RaiseToMaterialization(source, type, method);
        int before = function.Locals.Length;
        var invariant = SlotMaterializationInvariant.Capture(function);

        new SlotMaterializationPass().Run(function, PassContext.None);

        invariant.Check();
        Assert.True(function.Locals.Length > before);
        function.CheckInvariant(includeSemantics: true);
    }

    static IrFunction CoupledFunction()
    {
        var function = Function(Int32,
            new StoreLocal(0, Int32, new Constant(0, Int32)),
            new StoreStackSlot(0, new Call(new MethodRef(Owner, "Produce", Int32, [], false), false, [])),
            new StoreStackSlot(1, new Binary(BinaryKind.Add, false, false,
                new LoadStackSlot(0, Int32), new Constant(1, Int32))),
            new ExpressionStatement(new Call(new MethodRef(Owner, "Observe", Void, [Int32], false),
                false, [new LoadStackSlot(0, Int32)])),
            new Return(new LoadStackSlot(1, Int32)));
        function.AddLocal(Int32);
        return function;
    }

    // Deliberately bypass admission to give the checker independent, faulty
    // rewrites. These IR controls are never compiled as product evidence.
    static void RewriteSlot(IrFunction function, int slot, int index, TypeRef type)
    {
        var nodes = CoercionSinks.ScopeNodes(function.Body).ToArray();
        foreach (var load in nodes.OfType<LoadStackSlot>().Where(load => load.Slot == slot))
            load.ReplaceWith(new LoadLocal(index, type));
        foreach (var store in nodes.OfType<StoreStackSlot>().Where(store => store.Slot == slot))
        {
            var value = (IrExpression)store.DetachChildren()[0];
            store.ReplaceWith(new StoreLocal(index, type, value));
        }
    }
}
