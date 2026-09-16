using System.Collections.Immutable;
using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis.CSharp;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ExactSzArraySlotMaterializationTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef IntArray = TypeRef.SzArray(Int32);

    [Theory]
    [InlineData("object")]
    [InlineData("signed")]
    [InlineData("integer")]
    [InlineData("class")]
    [InlineData("interface")]
    [InlineData("enum")]
    [InlineData("struct")]
    [InlineData("generic")]
    [InlineData("jagged")]
    [InlineData("rectangular-element")]
    [InlineData("foreign-byte")]
    [InlineData("foreign-string")]
    [InlineData("pointer")]
    [InlineData("function-pointer")]
    public void ExactArrayFamiliesPreserveTheWholeProducer(string family)
    {
        var element = family switch
        {
            "object" => Object,
            "signed" => TypeRef.CoreLib("System", "SByte"),
            "integer" => Int32,
            "class" => TypeRef.Definition("Samples", "Samples", "Widget"),
            "interface" => TypeRef.CoreLib("System", "IDisposable"),
            "enum" => TypeRef.CoreLib("System", "DayOfWeek"),
            "struct" => TypeRef.CoreLib("System", "DateTime"),
            "generic" => TypeRef.GenericInstance(TypeRef.CoreLib("System.Collections.Generic", "List`1"), [Int32]),
            "jagged" => IntArray,
            "rectangular-element" => TypeRef.MdArray(Int32, 2),
            "foreign-byte" => TypeRef.Definition("Other", "System", "Byte"),
            "foreign-string" => TypeRef.Definition("Other", "System", "String"),
            "pointer" => TypeRef.Pointer(Int32),
            "function-pointer" => TypeRef.FunctionPointer(Int32, [], ""),
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
        var type = TypeRef.SzArray(element);
        var value = new NewArray(element, new Constant(2, Int32));
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
    [InlineData("void")]
    [InlineData("byref")]
    [InlineData("pinned")]
    [InlineData("unsupported")]
    [InlineData("open-generic")]
    [InlineData("wrong-arity")]
    [InlineData("rank-one-element")]
    [InlineData("bounded-element")]
    [InlineData("ref-struct")]
    [InlineData("name")]
    public void UnspellableArrayConstituentsRemainDeferred(string shape)
    {
        var refStruct = TypeRef.Definition("Samples", "Samples", "RefStruct");
        var element = shape switch
        {
            "void" => TypeRef.CoreLib("System", "Void"),
            "byref" => TypeRef.ByRef(Int32),
            "pinned" => TypeRef.Pinned(Int32),
            "unsupported" => TypeRef.Unsupported("test unsupported element"),
            "open-generic" => TypeRef.CoreLib("System.Collections.Generic", "List`1"),
            "wrong-arity" => TypeRef.GenericInstance(TypeRef.CoreLib("System.Collections.Generic", "List`1"), [Int32, Int32]),
            "rank-one-element" => TypeRef.MdArray(Int32, 1),
            "bounded-element" => TypeRef.MdArray(Int32, 2, arrayShapeIsExact: false),
            "ref-struct" => refStruct,
            "name" => TypeRef.Definition("Samples", "Samples", "<Invalid>"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var type = TypeRef.SzArray(element);
        var function = Function(type,
            new StoreStackSlot(0, new Constant(null, type)),
            new Return(new LoadStackSlot(0, type)));
        function.ByRefLikeTypes = ImmutableHashSet.Create(refStruct);

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes
            .HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GenericElementsRequireTheHostScope(bool methodParameter, bool inScope)
    {
        var parameter = methodParameter
            ? TypeRef.MethodGenericParameter(0, "T")
            : TypeRef.GenericParameter(0, "T");
        var type = TypeRef.SzArray(parameter);
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Constant(null, type)));
        block.Add(new Return(new LoadStackSlot(0, type)));
        body.Add(block);
        var function = new IrFunction("M", Owner,
            new MethodSignature(type, [], HasThis: false, GenericParameterCount: methodParameter ? 1 : 0)
            {
                GenericParameterNames = methodParameter && inScope ? ["T"] : [],
            }, [], body)
        {
            DeclaringTypeGenericParameterNames = !methodParameter && inScope ? ["T"] : [],
        };
        Assert.Equal(inScope, Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
        if (!inScope)
        {
            AssertRetained(function);
            return;
        }

        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Equal(type, Assert.Single(function.Locals));
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void PrimitiveArrayCopyComponentRemainsAtomic()
    {
        var function = Function(IntArray,
            new StoreStackSlot(0, new NewArray(Int32, new Constant(2, Int32))),
            new StoreStackSlot(1, new LoadStackSlot(0, IntArray)),
            new ExpressionStatement(new LoadStackSlot(1, type: null)),
            new Return(new LoadStackSlot(0, IntArray)));

        Assert.All(SlotMaterializationPass.Analyze(function), decision =>
            Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent)));
        AssertRetained(function);
    }

    [Theory]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.ReadObjects))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.ReadIntegers))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.ReadClasses))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.ReadInterfaces))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.ReadEnums))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.ReadGenericStructs))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.ReadGeneric))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.ReadJagged))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.ReadRectangularElements))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.AllocateAndObserve))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.InitializeAndObserve))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.MutateAndObserve))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.CovariantReturn))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.AllocatePointers))]
    [InlineData(nameof(ExactSzArraySlotMaterializationSamples.AllocateFunctionPointers))]
    public void CompilerProducedArrayCategoriesMaterialize(string method)
    {
        using var source = MetadataSource.Open(typeof(ExactSzArraySlotMaterializationSamples).Assembly.Location);
        var function = RaiseToMaterialization(source, typeof(ExactSzArraySlotMaterializationSamples).FullName!, method);
        var decisions = SlotMaterializationPass.Analyze(function)
            .Where(decision => decision.WillMaterialize && decision.Type?.Kind == TypeRefKind.SzArray).ToArray();
        Assert.NotEmpty(decisions);
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.All(decisions, decision => Assert.Contains(decision.Type!, function.Locals));
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void RealRoslynNodeArrayMaterializes()
    {
        using var source = MetadataSource.Open(typeof(CSharpSyntaxTree).Assembly.Location);
        var function = RaiseToMaterialization(source,
            "Microsoft.CodeAnalysis.CSharp.BoundTreeDumperNodeProducer", "VisitFieldEqualsValue");
        Assert.Contains(SlotMaterializationPass.Analyze(function),
            decision => decision.WillMaterialize
                && decision.Type is { Kind: TypeRefKind.SzArray, ElementType.Name: "TreeDumperNode" });
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void ObjectArraySwapKeepsTheExistingRaise()
    {
        using var source = MetadataSource.Open(typeof(ExactSzArraySlotMaterializationSamples).Assembly.Location);
        var function = RaiseToMaterialization(source, typeof(ExactSzArraySlotMaterializationSamples).FullName!,
            nameof(ExactSzArraySlotMaterializationSamples.SwapArrays));
        var pending = Assert.Single(SlotMaterializationPass.Analyze(function),
            decision => decision.Vetoes == SlotMaterializationVeto.PendingStorageSwap);

        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == pending.Slot);
        new SwapIdiomPass().Run(function, PassContext.None);
        Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void CompilerProducedArrayCategoriesRecompileExactly()
    {
        var results = FidelityCheck.Evaluate(
            typeof(ExactSzArraySlotMaterializationSamples).Assembly.Location,
            type => type == typeof(ExactSzArraySlotMaterializationSamples).FullName,
            // Mixed array ranks also fail with unchanged base tools (#7103).
            // Their materialization contract is covered above, not compile-back.
            method => method.Method != nameof(ExactSzArraySlotMaterializationSamples.ReadRectangularElements)).ToArray();
        Assert.Equal(15, results.Length);
        Assert.All(results, result => Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }
}
