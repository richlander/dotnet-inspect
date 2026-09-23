using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ExactPointerSlotMaterializationTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Pointer = TypeRef.Pointer(Int32);

    [Theory]
    [InlineData("Byte")]
    [InlineData("Int32")]
    [InlineData("UInt64")]
    [InlineData("Void")]
    [InlineData("pointer")]
    public void ExactPointerTypesPreserveTheWholeProducer(string element)
    {
        var type = TypeRef.Pointer(element == "pointer"
            ? Pointer : TypeRef.CoreLib("System", element));
        var value = new Constant(null, type);
        var function = Function(type,
            new StoreStackSlot(0, value),
            new Return(new LoadStackSlot(0, type: null)));
        var invariant = SlotMaterializationInvariant.Capture(function);

        Assert.False(CoercionDomain.InDomain(type, function.TypeShapes));
        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        new CoercionInsertionPass().Run(function, PassContext.None);

        Assert.Equal(type, Assert.Single(function.Locals));
        Assert.Same(value, Assert.Single(function.Descendants.OfType<StoreLocal>()).Value);
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData("byref-element")]
    [InlineData("pinned-element")]
    [InlineData("name")]
    [InlineData("unbound-generic")]
    [InlineData("function-pointer")]
    public void UnspellablePointersAndOtherStorageKindsRemainDeferred(string shape)
    {
        var type = shape switch
        {
            "byref-element" => TypeRef.Pointer(TypeRef.ByRef(Int32)),
            "pinned-element" => TypeRef.Pointer(TypeRef.Pinned(Int32)),
            "name" => TypeRef.Pointer(TypeRef.Definition("Samples", "Samples", "<Invalid>")),
            "unbound-generic" => TypeRef.Pointer(TypeRef.MethodGenericParameter(0, "T")),
            "function-pointer" => TypeRef.FunctionPointer(Int32, [], ""),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var function = Function(type,
            new StoreStackSlot(0, new Constant(null, type)),
            new Return(new LoadStackSlot(0, type)));
        var invariant = SlotMaterializationInvariant.Capture(function);

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes
            .HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
        invariant.Check();
    }

    [Theory]
    [InlineData("element")]
    [InlineData("depth")]
    [InlineData("object-null")]
    [InlineData("native-integer")]
    public void EveryProducerMustHaveTheExactPointerType(string mismatch)
    {
        var producer = mismatch switch
        {
            "element" => TypeRef.Pointer(TypeRef.CoreLib("System", "Byte")),
            "depth" => TypeRef.Pointer(Pointer),
            "object-null" => TypeRef.CoreLib("System", "Object"),
            "native-integer" => TypeRef.CoreLib("System", "IntPtr"),
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch)),
        };
        var function = Function(Pointer,
            new StoreStackSlot(0, new Constant(null, Pointer)),
            new ExpressionStatement(new Call(
                new MethodRef(Owner, "Observe", TypeRef.CoreLib("System", "Void"), [Pointer], HasThis: false),
                isVirtual: false, [new LoadStackSlot(0, Pointer)])),
            new StoreStackSlot(0, new Constant(null, producer)),
            new Return(new LoadStackSlot(0, Pointer)));

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes
            .HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointerCopyComponentsRemainAtomic(bool incomplete)
    {
        var function = Function(Pointer,
            new StoreStackSlot(0, new Constant(null, Pointer)),
            new StoreStackSlot(1, new LoadStackSlot(0, Pointer)),
            incomplete
                ? new ExpressionStatement(new LoadStackSlot(1, type: null))
                : new Return(new LoadStackSlot(1, Pointer)));
        var invariant = SlotMaterializationInvariant.Capture(function);
        var decisions = SlotMaterializationPass.Analyze(function);

        Assert.Equal(2, decisions.Count);
        if (incomplete)
        {
            Assert.All(decisions, decision => Assert.True(
                decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent)));
            AssertRetained(function);
        }
        else
        {
            Assert.All(decisions, decision => Assert.True(decision.WillMaterialize));
            new SlotMaterializationPass().Run(function, PassContext.None);
            Assert.Equal(2, function.Locals.Length);
            Assert.All(function.Locals, type => Assert.Equal(Pointer, type));
            Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
            Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
            function.CheckInvariant(includeSemantics: true);
        }
        invariant.Check();
    }

    [Theory]
    [InlineData(nameof(ExactPointerSlotMaterializationSamples.ReadAndObserve))]
    [InlineData(nameof(ExactPointerSlotMaterializationSamples.CopyThenReplace))]
    [InlineData(nameof(ExactPointerSlotMaterializationSamples.MutateAndObserve))]
    public void CompilerProducedPointerStorageMaterializes(string method)
    {
        using var source = MetadataSource.Open(typeof(ExactPointerSlotMaterializationSamples).Assembly.Location);
        AssertCompiledStorage(source, typeof(ExactPointerSlotMaterializationSamples).FullName!, method);
    }

    [Fact]
    public void RealRoslynHashStackAllocationMaterializes()
    {
        using var source = MetadataSource.Open(typeof(Compilation).Assembly.Location);
        AssertCompiledStorage(source, "System.IO.Hashing.XxHash128", "HashLengthOver240");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public async Task CompilerProducedPointerStorageRecompilesExactly()
    {
        string[] methods =
        [
            nameof(ExactPointerSlotMaterializationSamples.ReadAndObserve),
            nameof(ExactPointerSlotMaterializationSamples.CopyThenReplace),
            nameof(ExactPointerSlotMaterializationSamples.MutateAndObserve),
            nameof(ExactPointerSlotMaterializationSamples.StackAllocated),
            nameof(ExactPointerSlotMaterializationSamples.Accumulate),
            nameof(ExactPointerSlotMaterializationSamples.ReturnAsVoid),
            nameof(ExactPointerSlotMaterializationSamples.SwapPointers),
        ];
        var targets = methods.Select(method => new ReturnToSender.RequestedTarget(
            typeof(ExactPointerSlotMaterializationSamples).FullName!, method, 0)).ToArray();
        var results = await ReturnToSender.CompileBackTargets(
            typeof(ExactPointerSlotMaterializationSamples).Assembly.Location, targets,
            sourceIndex: null, applyCompileBackFloor: false);

        Assert.Equal(targets.Length, results.Count);
        Assert.All(results, result =>
        {
            Assert.False(result.UsedCompileBackFloor);
            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.MemberAnchor}: {result.Status}: {result.Detail}");
        });
    }

    static void AssertCompiledStorage(MetadataSource source, string type, string method)
    {
        var function = RaiseToMaterialization(source, type, method);
        var decisions = SlotMaterializationPass.Analyze(function)
            .Where(decision => decision.Type?.Kind == TypeRefKind.Pointer).ToArray();
        Assert.NotEmpty(decisions);
        Assert.All(decisions, decision => Assert.True(decision.WillMaterialize));
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();

        Assert.All(decisions, decision => Assert.Contains(decision.Type!, function.Locals));
        Assert.DoesNotContain(function.Descendants, node =>
            node is LoadStackSlot load && decisions.Any(decision => decision.Slot == load.Slot)
            || node is StoreStackSlot store && decisions.Any(decision => decision.Slot == store.Slot));
        function.CheckInvariant(includeSemantics: true);
    }
}
