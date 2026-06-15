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
        new PropertySugarPass(),
        new TypeOfFoldingPass(),
        // Consume conditional back edges into do-while loops before
        // structuring, which leaves any back-edge container flat. The loop
        // body becomes a container the structuring pass then raises.
        new DoWhileLoopPass(),
        new StructuringPass(),
        // After structuring the finally guard is an IfStatement, so the
        // Monitor lock lowering is matchable as lock (obj) { ... }.
        new LockSugarPass(),
        new ForLoopPass(),
        new BooleanFoldingPass(),
        // Folding merges slot diamonds into single stores; a second inlining
        // run collapses those slots into their uses (ternaries inline).
        new ExpressionInliningPass(),
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
