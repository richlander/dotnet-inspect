using System.Collections.Immutable;
using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis.CSharp;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ExactMdArraySlotMaterializationTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Matrix = TypeRef.MdArray(Int32, 2);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(32)]
    public void ExactArrayRanksPreserveTheWholeProducer(int rank)
        => AssertExactArray(TypeRef.MdArray(Int32, rank));

    [Theory]
    [InlineData("reference")]
    [InlineData("enum")]
    [InlineData("value")]
    [InlineData("generic")]
    [InlineData("jagged")]
    public void ExactArrayElementFamiliesMaterialize(string family)
    {
        var element = family switch
        {
            "reference" => String,
            "enum" => TypeRef.CoreLib("System", "DayOfWeek"),
            "value" => TypeRef.CoreLib("System", "DateTime"),
            "generic" => TypeRef.GenericInstance(
                TypeRef.CoreLib("System.Collections.Generic", "List`1"), [Int32]),
            "jagged" => TypeRef.SzArray(Int32),
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
        AssertExactArray(TypeRef.MdArray(element, 2));
    }

    [Theory]
    [InlineData("rank-zero")]
    [InlineData("rank-one")]
    [InlineData("rank-too-large")]
    [InlineData("bounds")]
    [InlineData("void")]
    [InlineData("byref")]
    [InlineData("ref-struct")]
    [InlineData("name")]
    [InlineData("unbound-generic")]
    public void UnspellableShapesRemainDeferred(string shape)
    {
        var refStruct = TypeRef.Definition("Samples", "Samples", "RefStruct");
        var type = shape switch
        {
            "rank-zero" => TypeRef.MdArray(Int32, 0),
            "rank-one" => TypeRef.MdArray(Int32, 1),
            "rank-too-large" => TypeRef.MdArray(Int32, 33),
            "bounds" => TypeRef.MdArray(Int32, 2, arrayShapeIsExact: false),
            "void" => TypeRef.MdArray(TypeRef.CoreLib("System", "Void"), 2),
            "byref" => TypeRef.MdArray(TypeRef.ByRef(Int32), 2),
            "ref-struct" => TypeRef.MdArray(refStruct, 2),
            "name" => TypeRef.MdArray(TypeRef.Definition("Samples", "Samples", "<Invalid>"), 2),
            "unbound-generic" => TypeRef.MdArray(TypeRef.MethodGenericParameter(0, "T"), 2),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var function = Function(type,
            new StoreStackSlot(0, new Constant(null, type)),
            new Return(new LoadStackSlot(0, type)));
        function.ByRefLikeTypes = ImmutableHashSet.Create(refStruct);
        var invariant = SlotMaterializationInvariant.Capture(function);

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes
            .HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
        invariant.Check();
    }

    [Theory]
    [InlineData("object-null")]
    [InlineData("covariance")]
    [InlineData("rank")]
    [InlineData("vector")]
    public void EveryProducerMustHaveTheExactArrayType(string mismatch)
    {
        var target = TypeRef.MdArray(Object, 2);
        var producer = mismatch switch
        {
            "object-null" => Object,
            "covariance" => TypeRef.MdArray(String, 2),
            "rank" => TypeRef.MdArray(Object, 3),
            "vector" => TypeRef.SzArray(Object),
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch)),
        };
        var function = Function(target,
            new StoreStackSlot(0, new Constant(null, target)),
            new ExpressionStatement(new Call(
                new MethodRef(Owner, "Observe", TypeRef.CoreLib("System", "Void"), [target], HasThis: false),
                isVirtual: false, [new LoadStackSlot(0, target)])),
            new StoreStackSlot(0, new Constant(null, producer)),
            new Return(new LoadStackSlot(0, target)));

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes
            .HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrayCopyComponentsRemainAtomic(bool incomplete)
    {
        var function = Function(Matrix,
            new StoreStackSlot(0, new Constant(null, Matrix)),
            new StoreStackSlot(1, new LoadStackSlot(0, Matrix)),
            incomplete
                ? new ExpressionStatement(new LoadStackSlot(1, type: null))
                : new Return(new LoadStackSlot(1, Matrix)));
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
            Assert.All(function.Locals, type => Assert.Equal(Matrix, type));
            Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
            Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
            function.CheckInvariant(includeSemantics: true);
        }
        invariant.Check();
    }

    [Theory]
    [InlineData(nameof(ExactMdArraySlotMaterializationSamples.ReadIntegers))]
    [InlineData(nameof(ExactMdArraySlotMaterializationSamples.ReadRankThree))]
    [InlineData(nameof(ExactMdArraySlotMaterializationSamples.ReadGeneric))]
    [InlineData(nameof(ExactMdArraySlotMaterializationSamples.AllocateAndObserve))]
    [InlineData(nameof(ExactMdArraySlotMaterializationSamples.InitializeAndObserve))]
    [InlineData(nameof(ExactMdArraySlotMaterializationSamples.CopyThenReplace))]
    [InlineData(nameof(ExactMdArraySlotMaterializationSamples.MutateAndObserve))]
    [InlineData(nameof(ExactMdArraySlotMaterializationSamples.CovariantReturn))]
    public void CompilerProducedArrayStorageMaterializes(string method)
    {
        using var source = MetadataSource.Open(typeof(ExactMdArraySlotMaterializationSamples).Assembly.Location);
        AssertCompiledStorage(source, typeof(ExactMdArraySlotMaterializationSamples).FullName!, method);
    }

    [Fact]
    public void RealRoslynRectangularTableMaterializes()
    {
        using var source = MetadataSource.Open(typeof(CSharpSyntaxTree).Assembly.Location);
        AssertCompiledStorage(source, "Microsoft.CodeAnalysis.CSharp.ConversionsBase.ConversionEasyOut", ".cctor");
    }

    [Fact]
    public void ArraySwapKeepsTheExistingRaise()
    {
        using var source = MetadataSource.Open(typeof(ExactMdArraySlotMaterializationSamples).Assembly.Location);
        var function = RaiseToMaterialization(source, typeof(ExactMdArraySlotMaterializationSamples).FullName!,
            nameof(ExactMdArraySlotMaterializationSamples.SwapArrays));
        var pending = Assert.Single(SlotMaterializationPass.Analyze(function),
            decision => decision.Vetoes == SlotMaterializationVeto.PendingStorageSwap);
        var invariant = SlotMaterializationInvariant.Capture(function);

        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == pending.Slot);
        new SwapIdiomPass().Run(function, PassContext.None);
        Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public async Task CompilerProducedArrayStorageRecompilesExactly()
    {
        string[] methods =
        [
            nameof(ExactMdArraySlotMaterializationSamples.ReadIntegers),
            nameof(ExactMdArraySlotMaterializationSamples.ReadRankThree),
            nameof(ExactMdArraySlotMaterializationSamples.ReadGeneric),
            nameof(ExactMdArraySlotMaterializationSamples.AllocateAndObserve),
            nameof(ExactMdArraySlotMaterializationSamples.InitializeAndObserve),
            nameof(ExactMdArraySlotMaterializationSamples.CopyThenReplace),
            nameof(ExactMdArraySlotMaterializationSamples.MutateAndObserve),
            nameof(ExactMdArraySlotMaterializationSamples.CovariantReturn),
            nameof(ExactMdArraySlotMaterializationSamples.SwapArrays),
        ];
        var targets = methods.Select(method => new ReturnToSender.RequestedTarget(
            typeof(ExactMdArraySlotMaterializationSamples).FullName!, method, 0)).ToArray();
        var results = await ReturnToSender.CompileBackTargets(
            typeof(ExactMdArraySlotMaterializationSamples).Assembly.Location, targets,
            sourceIndex: null, applyCompileBackFloor: false);

        Assert.Equal(targets.Length, results.Count);
        Assert.All(results, result =>
        {
            Assert.False(result.UsedCompileBackFloor);
            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.MemberAnchor}: {result.Status}: {result.Detail}");
        });
    }

    static void AssertExactArray(TypeRef type)
    {
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

    static void AssertCompiledStorage(MetadataSource source, string type, string method)
    {
        var function = RaiseToMaterialization(source, type, method);
        var decisions = SlotMaterializationPass.Analyze(function)
            .Where(decision => decision.Type?.Kind == TypeRefKind.Array).ToArray();
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
