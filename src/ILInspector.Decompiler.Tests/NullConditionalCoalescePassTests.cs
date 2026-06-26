using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class NullConditionalCoalescePassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_tk = TypeRef.GenericParameter(0, "T");

    /// <summary>
    /// Builds csc's generic null-conditional invocation lowering with the given
    /// fallback as the false arm:
    ///   A: Vk = default(T); Sj = &amp;value; if ((object)Vk != null) goto TRUE
    ///   B: Vk = *Sj; Sj = &amp;Vk;          if ((object)Vk != null) goto TRUE
    ///   FALSE: Sr = fallback; goto MERGE
    ///   TRUE:  Sr = Sj->GetHashCode()
    ///   MERGE: return Sr
    /// Knobs let each adversarial test toggle exactly one discriminator.
    /// </summary>
    static IrFunction Build(
        IrExpression fallback,
        TypeRef resultType,
        IrExpression? receiver = null,
        int guardBTarget = 30,
        bool entryIntoGuardB = false,
        bool tempUsedAfterMerge = false,
        bool volatileReload = false)
    {
        const int vk = 0, sj = 1, sr = 2;
        receiver ??= new LoadArgumentAddress(0, "value", s_tk);
        var ghc = new MethodRef(s_object, "GetHashCode", resultType, [], HasThis: true);

        var a = new Block(0);
        a.Add(new InitObject(s_tk, new LoadLocalAddress(vk, s_tk)));
        a.Add(new StoreStackSlot(sj, receiver));
        a.Add(new ConditionalBranch(new Box(s_tk, new LoadLocal(vk, s_tk)), 30));

        var b = new Block(10);
        b.Add(new StoreLocal(vk, s_tk, new LoadIndirect(s_tk, new LoadStackSlot(sj, TypeRef.ByRef(s_tk))) { IsVolatile = volatileReload }));
        b.Add(new StoreStackSlot(sj, new LoadLocalAddress(vk, s_tk)));
        b.Add(new ConditionalBranch(new Box(s_tk, new LoadLocal(vk, s_tk)), guardBTarget));

        var falseArm = new Block(20);
        falseArm.Add(new StoreStackSlot(sr, fallback));
        falseArm.Add(new Branch(40));

        var trueArm = new Block(30);
        trueArm.Add(new StoreStackSlot(sr, new Call(ghc, isVirtual: true, [new LoadStackSlot(sj, TypeRef.ByRef(s_tk))]) { ConstrainedTo = s_tk }));

        var merge = new Block(40);
        // tempUsedAfterMerge references the generic-default temp Vk after the merge,
        // so it is not dead outside the matched blocks and the fold must refuse.
        merge.Add(new Return(tempUsedAfterMerge
            ? new LoadLocal(vk, resultType)
            : new LoadStackSlot(sr, resultType)));

        var blocks = new List<Block> { a, b, falseArm, trueArm, merge };

        if (entryIntoGuardB)
        {
            // A block before the diamond that conditionally jumps INTO the reload
            // guard (block B at offset 10) — an external entry the fold must refuse.
            var entry = new Block(-10);
            entry.Add(new ConditionalBranch(new Constant(true, TypeRef.CoreLib("System", "Boolean")), 10));
            blocks.Insert(0, entry);
        }

        var container = new BlockContainer();
        foreach (var block in blocks)
            container.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "Host"),
            new MethodSignature(resultType, [new Parameter("value", s_tk)], HasThis: false, GenericParameterCount: 1),
            [s_tk],
            container);
    }

    [Fact]
    public void GenericNullConditional_WithIntFallback_RaisesCoalesce()
    {
        var function = Build(new Constant(0, s_int), s_int);

        new NullConditionalCoalescePass().Run(function, PassContext.None);
        IrPasses.Run(function);
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("value?.GetHashCode() ?? 0", output);
        Assert.DoesNotContain("goto", output);
        Assert.Empty(function.Descendants.OfType<Box>());
        function.CheckInvariant();
    }

    [Fact]
    public void GenericNullConditional_LiteralNullFallback_RaisesBareNullConditional()
    {
        // A reference-typed member result has a literal-null false arm, so the shape
        // is the bare recv?.M() with no coalesce.
        var function = Build(new Constant(null, s_string), s_string);

        new NullConditionalCoalescePass().Run(function, PassContext.None);
        var nc = Assert.Single(function.Descendants.OfType<NullConditional>());
        Assert.Empty(function.Descendants.OfType<Coalesce>());
        function.CheckInvariant();
    }

    [Fact]
    public void VolatileReload_StaysFlat_AndCapsFidelity()
    {
        // The reload `Vk = *Sj` carries a `volatile.` prefix. Folding it into a
        // null-conditional would erase the LoadIndirect — dropping the
        // acquire/release ordering and bypassing the fidelity cap (no faithful
        // spelling, #1434). The fold must refuse so the volatile load survives and
        // fidelity stays Partial.
        var function = Build(new Constant(0, s_int), s_int, volatileReload: true);

        new NullConditionalCoalescePass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
        Assert.Empty(function.Descendants.OfType<Coalesce>());
        var load = Assert.Single(function.Descendants.OfType<LoadIndirect>());
        Assert.True(load.IsVolatile);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void ImpureReceiver_StaysFlat()
    {
        // The receiver is a call result, not a re-evaluable address: evaluating it
        // once in recv?.M() would change how many times the side effect runs, so the
        // fold must refuse.
        var sideEffect = new Call(
            new MethodRef(s_object, "GetThing", TypeRef.ByRef(s_tk), [], HasThis: false),
            isVirtual: false, []);
        var function = Build(new Constant(0, s_int), s_int, receiver: sideEffect);

        new NullConditionalCoalescePass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
        Assert.NotEmpty(function.Descendants.OfType<Box>());
        function.CheckInvariant();
    }

    [Fact]
    public void GuardsToDifferentArms_StaysFlat()
    {
        // The two null tests branch to different targets — not the shared single
        // call arm of a null-conditional, so the fold must refuse.
        var function = Build(new Constant(0, s_int), s_int, guardBTarget: 40);

        new NullConditionalCoalescePass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
        function.CheckInvariant();
    }

    [Fact]
    public void ExternalEntryIntoReloadGuard_StaysFlat()
    {
        // A branch from outside lands in the reload guard the fold would drop, so
        // collapsing the blocks would orphan that edge — the fold must refuse.
        var function = Build(new Constant(0, s_int), s_int, entryIntoGuardB: true);

        new NullConditionalCoalescePass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
        function.CheckInvariant();
    }

    [Fact]
    public void TempLiveAfterMerge_StaysFlat()
    {
        // The generic-default temp is read after the merge, so it is not dead
        // outside the matched blocks; folding them away would lose that definition.
        var function = Build(new Constant(0, s_int), s_int, tempUsedAfterMerge: true);

        new NullConditionalCoalescePass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
        function.CheckInvariant();
    }
}
