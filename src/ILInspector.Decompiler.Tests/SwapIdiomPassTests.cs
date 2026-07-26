using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class SwapIdiomPassTests
{
    static IrFunction Raised(string methodName, Type? type = null)
    {
        type ??= typeof(CfgSampleClass);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    // --- Positive: the real compiler shape (CfgSampleClass.SwapStructPair) ---

    [Fact]
    public void ValueSwap_RaisesToTupleDeconstruction()
    {
        var function = Raised(nameof(CfgSampleClass.SwapStructPair));

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Equal(2, deconstruction.Targets.Length);
        Assert.All(deconstruction.Targets, t => Assert.Equal(DeconstructionTargetKind.Argument, t.Kind));
        Assert.IsType<TupleExpression>(deconstruction.Source);
        // The swap carrier is consumed: no surviving stack-slot store/load remains.
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
    }

    [Fact]
    public void PrintRaised_RendersRecognizedSwapForm()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.SwapStructPair))).Output;

        Assert.NotNull(output);
        Assert.Contains("(a, b) = (b, a);", output);
        Assert.DoesNotContain("S_", output);
    }

    // --- Negative: place types that forbid a tuple swap (byref / pointer / ref struct) ---

    public static TheoryData<string, TypeRef> UnspellableSwapPlaceTypes() => new()
    {
        { "byref (a `ref` reseat, not a value swap)", TypeRef.ByRef(Int) },
        { "pointer (illegal ValueTuple element, CS0306)", TypeRef.Pointer(Int) },
        { "ref struct (illegal ValueTuple element, CS9244)",
            TypeRef.GenericInstance(TypeRef.CoreLib("System", "Span"), [Int]) },
    };

    [Theory]
    [MemberData(nameof(UnspellableSwapPlaceTypes))]
    public void SwapOfUnspellablePlaceType_NotRaised(string _, TypeRef placeType)
    {
        // S = a; a = b; b = S;  over two places of a type that cannot appear as
        // a ValueTuple element (or would reseat rather than assign): decline.
        var function = BuildBlock(
            new StoreStackSlot(0, new LoadArgument(0, "p0", placeType)),
            new StoreArgument(0, "p0", placeType, new LoadArgument(1, "p1", placeType)),
            new StoreArgument(1, "p1", placeType, new LoadStackSlot(0, placeType)));

        new SwapIdiomPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Empty(function.Descendants.OfType<TupleExpression>());
    }

    [Fact]
    public void SwapOfReferenceTypePlaces_Raises()
    {
        // Reference-type swaps are byte-exact and legal ValueTuple elements, so
        // the guard must not over-reject them: `S = a; a = b; b = S;` still raises.
        var stringType = TypeRef.CoreLib("System", "String");
        var function = BuildBlock(
            new StoreStackSlot(0, new LoadArgument(0, "p0", stringType)),
            new StoreArgument(0, "p0", stringType, new LoadArgument(1, "p1", stringType)),
            new StoreArgument(1, "p1", stringType, new LoadStackSlot(0, stringType)));

        new SwapIdiomPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
    }

    // --- Negative: synthesized IR exercising each decline gate directly ---

    [Fact]
    public void SwapOfTwoArguments_Raises()
    {
        // S = a; a = b; b = S;  ->  (b, a) = (a, b);
        var function = BuildBlock(
            SaveSlot(0, Arg(0)),
            StoreArg(0, Arg(1)),
            StoreArg(1, LoadSlot(0)));

        new SwapIdiomPass().Run(function, PassContext.None);

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Equal([1, 0], deconstruction.Targets.Select(t => t.ArgumentIndex).ToArray());
        var tuple = Assert.IsType<TupleExpression>(deconstruction.Source);
        Assert.Equal([0, 1], tuple.Elements.Cast<LoadArgument>().Select(l => l.Index).ToArray());
    }

    [Fact]
    public void SavedValueIsComputed_NotRaised()
    {
        // S = f(a); a = b; b = S;  — the saved value is a call result, not a
        // simple place, so this is not a swap (b := f(old a), a := b).
        var function = BuildBlock(
            new StoreStackSlot(0, ComputeCall(Arg(0))),
            StoreArg(0, Arg(1)),
            StoreArg(1, LoadSlot(0)));

        new SwapIdiomPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DeconstructionAssignment>());
    }

    [Fact]
    public void RestoreWritesDifferentPlace_NotRaised()
    {
        // S = a; a = b; c = S;  — a three-place rotation, not a two-place swap:
        // the saved value lands in c, not back into the place b was read from.
        var function = BuildBlock(
            SaveSlot(0, Arg(0)),
            StoreArg(0, Arg(1)),
            StoreArg(2, LoadSlot(0)));

        new SwapIdiomPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DeconstructionAssignment>());
    }

    [Fact]
    public void CrossAssignsIntoSamePlace_NotRaised()
    {
        // S = a; a = a; a = S;  — both cross-stores target the same place, so
        // the two "exchanged" places are identical: not a swap.
        var function = BuildBlock(
            SaveSlot(0, Arg(0)),
            StoreArg(0, Arg(0)),
            StoreArg(0, LoadSlot(0)));

        new SwapIdiomPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DeconstructionAssignment>());
    }

    [Fact]
    public void CarrierReusedElsewhere_NotRaised()
    {
        // S = a; a = b; b = S; c = S;  — the carrier is read a second time, so
        // it is not a throwaway swap temp and must survive.
        var function = BuildBlock(
            SaveSlot(0, Arg(0)),
            StoreArg(0, Arg(1)),
            StoreArg(1, LoadSlot(0)),
            StoreArg(2, LoadSlot(0)));

        new SwapIdiomPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<DeconstructionAssignment>());
    }

    // --- Synthesis helpers ---

    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");

    static LoadArgument Arg(int index) => new(index, $"p{index}", Int);
    static StoreArgument StoreArg(int index, IrExpression value) => new(index, $"p{index}", Int, value);
    static StoreStackSlot SaveSlot(int slot, IrExpression value) => new(slot, value);
    static LoadStackSlot LoadSlot(int slot) => new(slot, Int);

    static Call ComputeCall(IrExpression argument)
    {
        var method = new MethodRef(
            TypeRef.Definition("UserAssembly", "Samples", "Factory"),
            "Compute", Int, [Int], HasThis: false);
        return new Call(method, isVirtual: false, [argument]);
    }

    static IrFunction BuildBlock(params IrNode[] statements)
    {
        var block = new Block();
        foreach (var statement in statements)
            block.Add(statement);
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("p0", Int), new Parameter("p1", Int), new Parameter("p2", Int)],
                HasThis: false,
                GenericParameterCount: 0),
            ImmutableArray<TypeRef>.Empty,
            body);
    }
}
