using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// A raising pass over the IR: one class, one job, registered in
/// <see cref="IrPasses.Default"/> — the ordered list IS the architecture
/// document (docs/decompiler-pipeline.md). Passes rewrite the tree via
/// <see cref="IrNode.ReplaceWith"/>; they communicate through the tree,
/// never side-channel state.
/// </summary>
public interface IIrPass
{
    string Name { get; }

    void Run(IrFunction function);
}

/// <summary>The pipeline's pass list and runner. Debug builds validate tree invariants after every pass — a violation is a pass bug, never input data.</summary>
public static class IrPasses
{
    public static ImmutableArray<IIrPass> Default { get; } =
    [
        new TypedConstantsPass(),
        // Drop identity conversions (the ldlen/conv.i4 array-length idiom)
        // before structuring so loop conditions match on clean lengths.
        new IdentityConvertPass(),
        new RedundantBranchEliminationPass(),
        // EH before inlining: the catch-entry store must still be the
        // handler's first statement when the pass folds it into the clause
        // header — inlining would dissolve it into its use site. Regions
        // become TryCatch/TryFinally shells whose body containers the
        // structuring pass later raises independently.
        new EhStructuringPass(),
        new ExpressionInliningPass(),
        // Inlining exposes new typed positions (a slot constant landing in a
        // bool return); typed constants run again to catch them.
        new TypedConstantsPass(),
        // Canonicalize spilled-this constructor receivers before sugar so the
        // base(...)/this(...) call is in its final shape for the printer.
        new ConstructorChainPass(),
        // Raise in-place struct .ctor calls (ldloca; call S::.ctor(args)) back
        // to `s = new S(args)`; left as-is they print as the illegal s..ctor(...)
        // (CS0201). Runs after the constructor-chain canonicalization so the
        // this/base receiver is already off the table.
        new StructConstructorPass(),
        new PropertySugarPass(),
        new TypeOfFoldingPass(),
        // Raise method-group delegate creation (ldftn + delegate ctor) once
        // inlining has placed the function-pointer load in the ctor's argument
        // slot — the inverse of the compiler's delegate lowering.
        new DelegateConstructionPass(),
        // Consume conditional back edges into do-while loops before
        // structuring, which leaves any back-edge container flat. The loop
        // body becomes a container the structuring pass then raises.
        new DoWhileLoopPass(),
        // Fold short-circuit OR guard chains (if (a || b || !c) { ... }) into a
        // single guard so the structuring pass — which only takes single-target
        // guard/diamond shapes — can raise them instead of leaving goto soup.
        new OrChainGuardPass(),
        // Raise IL jump tables into switch statements; the section bodies are
        // containers the structuring pass then raises.
        new SwitchRaisingPass(),
        new StructuringPass(),
        // After structuring the finally guard is an IfStatement, so the
        // Monitor lock lowering is matchable as lock (obj) { ... }.
        new LockSugarPass(),
        new ForLoopPass(),
        new BooleanFoldingPass(),
        // Raise the null-conditional lowering (receiver spill + null diamond)
        // into target?.Member before the second inlining run, so the receiver
        // spill collapses into the ?. target and the reused slot stops carrying
        // two unrelated types.
        new NullConditionalPass(),
        // Folding merges slot diamonds into single stores; a second inlining
        // run collapses those slots into their uses (ternaries inline).
        new ExpressionInliningPass(),
        // With structuring done, a spilled base/this constructor argument is a
        // folded expression (base(message ?? "default")); collapse it into the
        // chain call so the call lands as the body's first statement and the
        // printer lifts it to a signature initializer rather than an invalid
        // base(temp); body call (CS0175).
        new ConstructorChainArgumentPass(),
        // Raise any static function-pointer load still standing into &Method
        // (it feeds a calli, native callback, or delegate*-typed field — all of
        // which take a function pointer directly).
        new MethodAddressPass(),
        // Last: any function-pointer load still standing fed something other
        // than a delegate constructor — record the honest residual diagnostic.
        new FunctionPointerDiagnosticsPass(),
    ];

    public static void Run(IrFunction function) => Run(function, Default);

    public static void Run(IrFunction function, ImmutableArray<IIrPass> passes)
    {
        foreach (var pass in passes)
        {
            pass.Run(function);
            function.CheckInvariant();
        }
    }
}
