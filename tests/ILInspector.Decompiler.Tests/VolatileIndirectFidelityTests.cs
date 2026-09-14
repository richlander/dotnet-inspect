using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Over-raise honesty gate for issue #1434: a <c>volatile.</c>-prefixed indirect
/// access (<c>volatile. ldind</c>/<c>volatile. stind</c>) decompiles to a bare
/// <c>*p</c> that drops the acquire/release ordering, and must not be reported
/// <see cref="DecompilationFidelity.Full"/>. csc never emits <c>volatile.</c> on a
/// bare deref, so the counterexample is a synthetic IR fixture; the non-volatile
/// twin is the built-in near-miss negative — identical <c>*p</c> output, but it
/// stays <c>Full</c>.
/// </summary>
public class VolatileIndirectFidelityTests
{
    static readonly TypeRef IntType = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef IntPtr = TypeRef.Pointer(IntType);
    static readonly TypeRef VoidType = TypeRef.CoreLib("System", "Void");

    static IrFunction ReadVolatile(bool isVolatile)
    {
        var load = new LoadIndirect(IntType, new LoadArgument(0, "p", IntPtr)) { IsVolatile = isVolatile };
        var container = new BlockContainer();
        var entry = new Block(0x00);
        entry.Add(new Return(load));
        container.Add(entry);
        var signature = new MethodSignature(IntType, [new Parameter("p", IntPtr)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("ReadVolatile", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);
    }

    static IrFunction WriteVolatile(bool isVolatile)
    {
        var store = new StoreIndirect(IntType, new LoadArgument(0, "p", IntPtr), new Constant(1, IntType)) { IsVolatile = isVolatile };
        var container = new BlockContainer();
        var entry = new Block(0x00);
        entry.Add(store);
        entry.Add(new Return(null));
        container.Add(entry);
        var signature = new MethodSignature(VoidType, [new Parameter("p", IntPtr)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("WriteVolatile", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);
    }

    [Fact]
    public void VolatileLoadIndirect_IsPartial_WithRemark()
    {
        var function = ReadVolatile(isVolatile: true);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var remark = Assert.Single(FidelityRemarks.Collect(function));
        Assert.Equal(DiagnosticIds.VolatileIndirectAccess, remark.Code);  // DEC0012
    }

    [Fact]
    public void VolatileStoreIndirect_IsPartial_WithRemark()
    {
        var function = WriteVolatile(isVolatile: true);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var remark = Assert.Single(FidelityRemarks.Collect(function));
        Assert.Equal(DiagnosticIds.VolatileIndirectAccess, remark.Code);  // DEC0012
    }

    [Fact]
    public void PlainLoadIndirect_StaysFull_NoRemark()
    {
        var function = ReadVolatile(isVolatile: false);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(function));
    }

    [Fact]
    public void PlainStoreIndirect_StaysFull_NoRemark()
    {
        var function = WriteVolatile(isVolatile: false);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(function));
    }

    [Fact]
    public void VolatileAndPlainLoad_PrintIdentically_SoFidelityIsTheOnlyDiscriminator()
    {
        // The near-miss negative: the printer still emits a bare `*p` for both, so
        // the only thing distinguishing the volatile read is the lowered fidelity.
        var volatileOutput = CSharpPrinter.Print(ReadVolatile(isVolatile: true)).Output;
        var plainOutput = CSharpPrinter.Print(ReadVolatile(isVolatile: false)).Output;

        Assert.Contains("*p", volatileOutput);
        Assert.Equal(plainOutput, volatileOutput);
    }

    // --- Pass-erasure guards: a volatile indirect node must not be folded away by
    // a raising pass before the final-tree fidelity check runs (else the cap is
    // bypassed). The fold must decline, leaving the volatile node flat. ---

    static readonly TypeRef InlineArray2 =
        TypeRef.Definition("UserAssembly", "Samples", "InlineArray2", ValueTypeHint.ValueType, MetadataFactState.Yes);
    static readonly TypeRef SpanInt = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Span`1"), [IntType]);

    static MethodRef PrivateImplHelper(string name, TypeRef returnType)
        => new(
            TypeRef.Definition(TypeRef.CoreLibrary, "", "<PrivateImplementationDetails>"),
            name,
            returnType,
            [TypeRef.ByRef(InlineArray2), IntType],
            HasThis: false)
        {
            TypeArguments = [InlineArray2, IntType],
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };

    // csc lowers `[a, b]` to: default-init a synthesized InlineArray2 local, store
    // each slot through <PrivateImplementationDetails>.InlineArrayElementRef, then
    // expose it via InlineArrayAsReadOnlySpan. InlineArrayCollectionPass folds that
    // into `[a, b]`, detaching the element stores. With volatile stores it must not.
    static IrFunction InlineArrayCollection(bool isVolatile)
    {
        var block = new Block();
        block.Add(new InitObject(InlineArray2, new LoadLocalAddress(0, InlineArray2)));
        block.Add(new StoreIndirect(
            IntType,
            new Call(PrivateImplHelper("InlineArrayElementRef", TypeRef.ByRef(IntType)), isVirtual: false,
                [new LoadLocalAddress(0, InlineArray2), new Constant(0, IntType)]),
            new LoadArgument(0, "a", IntType)) { IsVolatile = isVolatile });
        block.Add(new StoreIndirect(
            IntType,
            new Call(PrivateImplHelper("InlineArrayElementRef", TypeRef.ByRef(IntType)), isVirtual: false,
                [new LoadLocalAddress(0, InlineArray2), new Constant(1, IntType)]),
            new LoadArgument(1, "b", IntType)) { IsVolatile = isVolatile });
        block.Add(new StoreLocal(1, SpanInt, new Call(
            PrivateImplHelper("InlineArrayAsReadOnlySpan", SpanInt), isVirtual: false,
            [new LoadLocalAddress(0, InlineArray2), new Constant(2, IntType)])));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(
            VoidType,
            [new Parameter("a", IntType), new Parameter("b", IntType)],
            HasThis: false,
            GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [InlineArray2, SpanInt], body);
    }

    [Fact]
    public void PlainInlineArrayStores_FoldToCollectionExpression()
    {
        // Positive canary: without the volatile prefix the pass raises the literal,
        // so the test below proves the volatile prefix is the only discriminator.
        var function = InlineArrayCollection(isVolatile: false);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.Empty(function.Descendants.OfType<StoreIndirect>());
        function.CheckInvariant();
    }

    [Fact]
    public void VolatileInlineArrayStores_StayFlat_AndCapFidelity()
    {
        var function = InlineArrayCollection(isVolatile: true);

        new InlineArrayCollectionPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Equal(2, function.Descendants.OfType<StoreIndirect>().Count(s => s.IsVolatile));
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        function.CheckInvariant();
    }
}
