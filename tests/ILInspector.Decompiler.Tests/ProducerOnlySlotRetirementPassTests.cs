using System.Collections.Immutable;
using System.Security.Cryptography;
using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ProducerOnlySlotRetirementPassTests
{
    static readonly TypeRef Action = TypeRef.CoreLib("System", "Action");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Owner =
        TypeRef.Definition("Synthetic", "Samples", "Owner");

    [Fact]
    public void PureProducerDisappearsWithItsStorage()
    {
        var function = Function(
            new StoreStackSlot(0, new LoadArgument(0, "value", Int32)),
            new Return(null));

        new ProducerOnlySlotRetirementPass().Run(
            function,
            PassContext.None);

        Assert.DoesNotContain(
            function.Descendants,
            node => node is StoreStackSlot or LoadStackSlot);
        Assert.IsType<Return>(Assert.Single(Entry(function).Children));
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void EffectfulProducerBecomesExactTypedDiscard()
    {
        var effect = new MethodRef(
            Owner,
            "Effect",
            Int32,
            [],
            HasThis: false);
        var call = new Call(effect, isVirtual: false, []);
        var function = Function(
            new StoreStackSlot(0, call),
            new Return(null));

        new ProducerOnlySlotRetirementPass().Run(
            function,
            PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(
            Entry(function).Children[0]);
        Assert.Same(call, statement.Expression);
        Assert.Equal(Int32, statement.DiscardTargetType);
        string output = DecidedPrint.Print(function).Output!;
        Assert.Contains("_ = (int)(", output);
        Assert.Contains("Effect()", output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void TargetTypedLambdaKeepsItsDelegateContext()
    {
        var lambdaBody = new BlockContainer();
        var lambdaEntry = new Block();
        lambdaEntry.Add(new Return(null));
        lambdaBody.Add(lambdaEntry);
        var lambda = new Lambda(
            Action,
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody)
        {
            ReturnsVoid = true,
        };
        var function = Function(
            new StoreStackSlot(0, lambda),
            new Return(null));

        new ProducerOnlySlotRetirementPass().Run(
            function,
            PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(
            Entry(function).Children[0]);
        Assert.Equal(Action, statement.DiscardTargetType);
        string output = DecidedPrint.Print(function).Output!;
        Assert.Contains("_ = (Action)(", output);
        Assert.Contains("() =>", output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void WiderReferenceCoalesceKeepsItsAssignmentType()
    {
        var coalesce = new Coalesce(
            new LoadArgument(0, "text", String),
            new LoadArgument(1, "fallback", Object));
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                Void,
                [
                    new Parameter("text", String),
                    new Parameter("fallback", Object),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            Body(
                new StoreStackSlot(0, coalesce),
                new Return(null)));

        new ReferenceSlotTargetBindingPass().Run(
            function,
            PassContext.None);
        Assert.Equal(String, coalesce.ResultType);
        Assert.Equal(Object, coalesce.AssignmentType);

        new ProducerOnlySlotRetirementPass().Run(
            function,
            PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(
            Entry(function).Children[0]);
        Assert.Equal(Object, statement.DiscardTargetType);
        string output = DecidedPrint.Print(function).Output!;
        Assert.Contains("_ = (object)(text ?? fallback);", output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void PointerStackAllocateRemainsForDedicatedStorageRendering()
    {
        var store = new StoreStackSlot(
            0,
            new StackAllocate(new Constant(8, Int32)));
        var function = Function(
            store,
            new Return(null));

        new ProducerOnlySlotRetirementPass().Run(
            function,
            PassContext.None);

        Assert.Same(store, Entry(function).Children[0]);
        Assert.Empty(function.Descendants.OfType<ExpressionStatement>());
        Assert.Single(function.Descendants.OfType<StackAllocate>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void DirectCopyComponentRetiresWithoutOrphaningLoads()
    {
        var effect = new MethodRef(
            Owner,
            "Effect",
            Int32,
            [],
            HasThis: false);
        var function = Function(
            new StoreStackSlot(
                0,
                new Call(effect, isVirtual: false, [])),
            new StoreStackSlot(
                1,
                new LoadStackSlot(0, Int32)),
            new Return(null));

        new ProducerOnlySlotRetirementPass().Run(
            function,
            PassContext.None);

        Assert.DoesNotContain(
            function.Descendants,
            node => node is StoreStackSlot or LoadStackSlot);
        Assert.Single(
            Entry(function).Children,
            node => node is ExpressionStatement);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void ExternalObserverKeepsTheWholeProducerChain()
    {
        var function = Function(
            new StoreStackSlot(
                0,
                new LoadArgument(0, "value", Int32)),
            new StoreStackSlot(
                1,
                new LoadStackSlot(0, Int32)),
            new Return(new LoadStackSlot(1, Int32)));

        new ProducerOnlySlotRetirementPass().Run(
            function,
            PassContext.None);

        Assert.Equal(
            2,
            function.Descendants.OfType<StoreStackSlot>().Count());
        Assert.Equal(
            2,
            function.Descendants.OfType<LoadStackSlot>().Count());
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void ManagedReferenceProducerRemainsForExactStorage()
    {
        TypeRef byRef = TypeRef.ByRef(Int32);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                Void,
                [new Parameter("value", byRef)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            Body(
                new StoreStackSlot(
                    0,
                    new LoadArgument(0, "value", byRef)),
                new Return(null)));

        new ProducerOnlySlotRetirementPass().Run(
            function,
            PassContext.None);

        var store = Assert.Single(
            function.Descendants.OfType<StoreStackSlot>());
        Assert.Equal(byRef, store.Value.ResultType);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void NestedProducerOnlyWebRemainsInItsOwnScope()
    {
        var lambdaBody = Body(
            new StoreStackSlot(
                0,
                new LoadArgument(0, "value", Int32)),
            new Return(null));
        var lambda = new Lambda(
            Action,
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody)
        {
            ReturnsVoid = true,
        };
        var function = Function(
            [Action],
            new StoreLocal(0, Action, lambda),
            new Return(null));

        new ProducerOnlySlotRetirementPass().Run(
            function,
            PassContext.None);

        Assert.Single(lambda.Descendants.OfType<StoreStackSlot>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void RoslynAnalyzerLoaderDeadArraySpillBecomesDiscard()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PrimitiveJoin",
            "Microsoft.CodeAnalysis.dll");
        Assert.Equal(
            "10F489DB67B8AC7489E58D392166C928302BA5698506DD652311DA5D89F0A0F8",
            System.Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(path))));
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = IrImporter.Import(
            source,
            "Microsoft.CodeAnalysis.AnalyzerAssemblyLoader",
            ".ctor",
            overloadIndex: 1);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(
            function!,
            method => IrImporter.Import(source, method));

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.NotNull(result.Output);
        Assert.DoesNotContain(
            "IAnalyzerAssemblyResolver[] S_256",
            result.Output);
        Assert.Contains(
            "_ = (IAnalyzerAssemblyResolver[])(",
            result.Output);
    }

    static IrFunction Function(params IrNode[] statements)
        => Function([], statements);

    static IrFunction Function(
        ImmutableArray<TypeRef> locals,
        params IrNode[] statements)
        => new(
            "M",
            Owner,
            new MethodSignature(
                Void,
                [new Parameter("value", Int32)],
                HasThis: false,
                GenericParameterCount: 0),
            locals,
            Body(statements));

    static BlockContainer Body(params IrNode[] statements)
    {
        var body = new BlockContainer();
        var block = new Block();
        foreach (IrNode statement in statements)
            block.Add(statement);
        body.Add(block);
        return body;
    }

    static Block Entry(IrFunction function)
        => Assert.IsType<Block>(
            Assert.IsType<BlockContainer>(function.Body).Children[0]);
}
