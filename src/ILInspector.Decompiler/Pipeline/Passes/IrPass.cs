using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// A raising pass over the IR: one class, one job, registered in
/// <see cref="IrPasses.Default"/> — the ordered list IS the architecture
/// document (docs/decompiler.md). Passes rewrite the tree via
/// <see cref="IrNode.ReplaceWith"/>; they communicate through the tree,
/// never side-channel state, and record fine-grained rewrites through
/// <see cref="PassContext.Stepper"/>.
/// </summary>
public interface IIrPass
{
    string Name { get; }

    void Run(IrFunction function, PassContext context);
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
        // Raise simple DefaultInterpolatedStringHandler append sequences back
        // into $"..." before later passes have a chance to inline or reshape
        // the handler local.
        new StringInterpolationPass(),
        // Raise direct ValueTuple constructor calls back into tuple literals.
        // Runs after struct-constructor so in-place struct .ctor calls have
        // already been normalized to NewObject when they are spellable.
        new TupleCreationPass(),
        // Raise an anonymous-type constructor (new <>f__AnonymousTypeN(...)) back
        // into a new { Name = value, ... } literal, using the property names the
        // importer captured on the NewObject. Runs in the expression-sugar band
        // next to tuple-creation; the unspeakable generated type name means the
        // flat form is invalid C#, so this also lifts fidelity to valid source.
        new AnonymousObjectPass(),
        new PropertySugarPass(),
        // Raise the object/collection-initializer lowering (a NewObject threaded
        // through a dup chain and mutated by a run of member stores or Add calls)
        // back into new T { X = a, ... }. Runs after property-sugar so the member
        // setters are already StoreProperty nodes, uniform with field stores.
        new ObjectInitializerPass(),
        // Raise array range-slice lowering (RuntimeHelpers.GetSubArray(a, range))
        // back into a[range]. GetSubArray is a compiler-only helper, so the match
        // is unambiguous and the round-trip is opcode-exact.
        new RangeFromGetSubArrayPass(),
        // Raise array/string receiver.Length - n index operands into ^n. This
        // removes the duplicate receiver use so the later inlining pass can
        // collapse the compiler's dup spill back into the element receiver.
        new IndexFromEndPass(),
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
        // Inline a shared multi-way return-merge into its arms, so the comparison
        // tree csc emits for a sparse switch (each arm goto-ing one ldloc; ret
        // tail) nests as guard clauses instead of staying flat goto soup.
        new ReturnMergePass(),
        new StructuringPass(),
        // After structuring the finally guard is an IfStatement, so the
        // Monitor lock lowering is matchable as lock (obj) { ... }.
        new LockSugarPass(),
        new ForLoopPass(),
        new BooleanFoldingPass(),
        // Raise local `if (V is null) V = fallback;` diamonds into `V ??= fallback`.
        // Runs after structuring/boolean folding so the null test is a shaped
        // IfStatement, before later expression inlining reshapes local uses.
        new NullCoalescingAssignmentPass(),
        // Raise the null-conditional lowering (receiver spill + null diamond)
        // into target?.Member before the second inlining run, so the receiver
        // spill collapses into the ?. target and the reused slot stops carrying
        // two unrelated types.
        new NullConditionalPass(),
        // Folding merges slot diamonds into single stores; a second inlining
        // run collapses those slots into their uses (ternaries inline).
        new ExpressionInliningPass(),
        // Raise the csc type-pattern lowering (a `value as T` store gating a
        // null test that scopes the narrowed local) into `value is T t`. Runs
        // after structuring and boolean folding so the `if` guard and the
        // `&&` short-circuit operand are both formed; left flat it renders as
        // a separate `T t = value as T; if (t is not null)`.
        new IsPatternPass(),
        // Fold the compiler's dup-based ++/-- idiom (a value-carrying increment
        // spilled to a single-use slot beside the local update) back into the
        // operator at the use site, so a[--i] = src[j++] recompiles to the same
        // dup rather than spilling the captured value to extra locals.
        new IncrementDecrementPass(),
        // Raise the compiler's bool→int normalization (cgt.un(boolExpr, 0), the
        // [LibraryImport] bool-marshalling shape) back into `b ? 1 : 0`. Left
        // flat it renders `b > false` (CS0019) and the method never binds. Runs
        // after inlining so the bool operand is in its final position.
        new BoolToIntNormalizationPass(),
        // With structuring done, a spilled base/this constructor argument is a
        // folded expression (base(message ?? "default")); collapse it into the
        // chain call so the call lands as the body's first statement and the
        // printer lifts it to a signature initializer rather than an invalid
        // base(temp); body call (CS0175).
        new ConstructorChainArgumentPass(),
        // Eliminate return-accumulator temporaries the compiler spilled across
        // an EH region or lock (try { V = e; } finally { } return V; back to
        // try { return e; }). Runs after structuring and the second inlining so
        // the try/catch/finally/lock shells and their tail stores are fully
        // formed; expression inlining leaves these temps standing because it
        // refuses to move a value across a leave/region edge.
        new ReturnSinkingPass(),
        // Raise the csc reference-type using lowering (resource local +
        // try/finally with an IDisposable.Dispose null guard) back into a
        // using statement. Runs after return sinking so a `return` from inside
        // the protected body stays inside the using body.
        new UsingStatementPass(),
        // Raise the csc pin lowering (a pinned managed-ref local + derived
        // pointer + optional unpin store) into fixed (T* p = &place) { ... }.
        // Runs after structuring and the second inlining so the pinned region's
        // body is fully raised before it is wrapped; left flat the pinned local
        // renders as the non-C# `pinned ref T` and the method never compiles.
        new FixedStatementPass(),
        // Raise the csc constant-array/span initializer lowering
        // (RuntimeHelpers.CreateSpan<T>(ldtoken <PrivateImplementationDetails>.blob))
        // back into a new T[] { ... } span literal, decoding the field's mapped
        // RVA bytes. Left flat it renders the unspellable ldtoken of a
        // compiler-internal field name and never compiles. Kept in Lowered
        // (the CreateSpan call has no valid C# spelling).
        new RvaSpanPass(),
        // Raise the csc lowering of `Span<T> s = stackalloc T[n]` (a localloc fed
        // to the Span<T>(void*, int) ctor) back into `stackalloc T[n]`. Left flat
        // it renders `new Span<T>(stackalloc byte[...], n)`, which never compiles
        // (a stackalloc in argument position is a Span<byte>, not void*). Kept in
        // Lowered (the ctor shape has no valid C# spelling).
        new StackAllocSpanPass(),
        // Raise the csc inline-array lowering of a span collection expression
        // (a <>y__InlineArrayN<T> temp written slot-by-slot through
        // <PrivateImplementationDetails>.InlineArrayElementRef and exposed by
        // InlineArrayAsReadOnlySpan) back into a C# 12 collection expression
        // [e0, e1, ...]. Left flat the angle-bracketed compiler-internal names
        // never parse. Kept in Lowered (no valid C# spelling otherwise).
        new InlineArrayCollectionPass(),
        // Raise any static function-pointer load still standing into &Method
        // (it feeds a calli, native callback, or delegate*-typed field — all of
        // which take a function pointer directly).
        new MethodAddressPass(),
        // Last: any function-pointer load still standing fed something other
        // than a delegate constructor — record the honest residual diagnostic.
        new FunctionPointerDiagnosticsPass(),
        new RefKindDiagnosticsPass(),
    ];

    /// <summary>
    /// The "lowered" pipeline — <see cref="Default"/> with the cosmetic
    /// statement-sugar passes removed, so the C# renders at a lower altitude
    /// while staying valid, recompilable code (issue #636). Only passes whose
    /// removal leaves spellable C# are dropped: <see cref="ForLoopPass"/>
    /// (leaves the <c>while</c> it would raise), <see cref="IncrementDecrementPass"/>
    /// (leaves the explicit value-carrying temp), and <see cref="LockSugarPass"/>
    /// (leaves the explicit <c>Monitor.Enter</c>/<c>try…finally</c>). Property,
    /// delegate-construction, null-conditional, and folding passes are kept:
    /// removing them would emit constructs C# cannot spell (a direct
    /// <c>get_X()</c>/<c>set_X()</c> accessor call is CS0571, a bare
    /// <c>ldftn</c> has no syntax), which would not recompile — and lowered
    /// output, like SharpLab's, must always be valid C#.
    /// </summary>
    public static ImmutableArray<IIrPass> Lowered { get; } =
        [.. Default.Where(p => p is not (ForLoopPass or IncrementDecrementPass or LockSugarPass))];

    public static void Run(IrFunction function) => Run(function, Default);

    public static void Run(IrFunction function, ImmutableArray<IIrPass> passes)
        => Run(function, passes, PassContext.None);

    public static void Run(IrFunction function, ImmutableArray<IIrPass> passes, PassContext context)
    {
        foreach (var pass in passes)
        {
            pass.Run(function, context);
            function.CheckInvariant();
        }
    }

    /// <summary>The synthetic stage name for the importer output — the pre-transform tree, before any pass runs.</summary>
    public const string ImportStageName = "(import)";

    /// <summary>
    /// Runs the default pipeline, capturing the IR-tree projection at every
    /// stage boundary (the importer output, then after each pass). This is the
    /// library backing for <c>--dump-stages</c>: one projection function applied
    /// per stage, so the harness and the CLI share identical boundaries rather
    /// than each re-deriving them (docs/decompiler.md).
    /// </summary>
    public static IReadOnlyList<PipelineStage> RunWithStages(IrFunction function)
        => RunWithStages(function, Default, IrPrinter.Dump);

    /// <summary>
    /// Runs <paramref name="passes"/>, capturing <paramref name="project"/>'s
    /// output at the importer boundary and after each pass. The projection runs
    /// between mutations, so each captured string is the tree as that stage left
    /// it. Debug builds validate invariants after every pass, exactly as
    /// <see cref="Run(IrFunction, ImmutableArray{IIrPass})"/> does.
    /// </summary>
    public static IReadOnlyList<PipelineStage> RunWithStages(
        IrFunction function, ImmutableArray<IIrPass> passes, Func<IrFunction, string> project)
    {
        var stages = new List<PipelineStage>(passes.Length + 1)
        {
            new(ImportStageName, project(function), function.Fidelity),
        };
        foreach (var pass in passes)
        {
            pass.Run(function, PassContext.None);
            function.CheckInvariant();
            stages.Add(new(pass.Name, project(function), function.Fidelity));
        }
        return stages;
    }

    /// <summary>
    /// Runs the default pipeline with the stepper enabled, replaying to
    /// <paramref name="stepLimit"/>: passes record their fine-grained rewrites,
    /// and the run stops right before the step with that ordinal so the returned
    /// stepper's tree position is the "about to go wrong" state. Pass
    /// <see cref="int.MaxValue"/> to record every step without stopping. The
    /// <see cref="StepLimitReachedException"/> is caught here — callers see a
    /// normal return with the partially-transformed <paramref name="function"/>.
    /// </summary>
    public static Stepper RunWithSteps(IrFunction function, int stepLimit = int.MaxValue)
    {
        var stepper = new Stepper(enabled: true) { StepLimit = stepLimit };
        var context = new PassContext(stepper);
        try
        {
            foreach (var pass in Default)
            {
                pass.Run(function, context);
                function.CheckInvariant();
            }
        }
        catch (StepLimitReachedException)
        {
            // Expected: the run was asked to stop right before this step. The
            // tree is left mid-rewrite, which is exactly what the caller wants
            // to inspect.
        }
        return stepper;
    }
}
