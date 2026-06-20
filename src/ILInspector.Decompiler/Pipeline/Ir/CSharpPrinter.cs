using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// First C# projection of the IR: honest lowered output. Structure that has
/// not been raised renders as what it is — flat blocks with labels and
/// gotos — never as guessed sugar. Formatting is deliberately plain (bare
/// this-members, V_N/S_N names, trimmed trailing return) so a diff over the
/// output measures structural distance, not whitespace noise. The raising
/// passes close the goto gap from here; this printer is where completeness
/// starts.
/// </summary>
public sealed class CSharpPrinter
{
    readonly IrFunction _function;

    /// <summary>
    /// True when the source module opts into the updated memory-safety rules
    /// (see <see cref="IrFunction.UsesUpdatedMemorySafetyRules"/>). When set, the
    /// printer wraps unsafe operations in explicit <c>unsafe { }</c> blocks.
    /// </summary>
    readonly bool _newMemorySafetyRules;

    /// <summary>
    /// True when the method body skips locals initialization (<see
    /// cref="IrFunction.SkipLocalsInit"/>) — the extra condition that makes a
    /// <c>stackalloc</c>-to-<c>Span</c> conversion unsafe under the new rules.
    /// </summary>
    readonly bool _skipLocalsInit;

    /// <summary>
    /// Nesting depth of emitted <c>unsafe { }</c> blocks. Non-zero means the
    /// current statements are already in an unsafe context, so inner operations
    /// are not wrapped again (no redundant nested blocks).
    /// </summary>
    int _unsafeDepth;

    CSharpPrinter(IrFunction function)
    {
        _function = function;
        _newMemorySafetyRules = function.UsesUpdatedMemorySafetyRules;
        _skipLocalsInit = function.SkipLocalsInit;
    }

    /// <summary>The product path: runs the default raising passes, then prints. <see cref="Print"/> alone renders whatever tree it is given — right for stage dumps, wrong for output paths.</summary>
    public static DecompilerResult PrintRaised(IrFunction function)
    {
        try
        {
            IrPasses.Run(function);
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
        return Print(function);
    }

    /// <summary>
    /// The product path with a statement line map: same output as
    /// <see cref="PrintRaised(IrFunction)"/>, plus a table from each top-level
    /// statement node to its 0-based start line. Line-anchored overlays (the
    /// annotated C# view) splice onto those lines; the printer itself stays
    /// annotation-agnostic. The map is empty on failure.
    /// </summary>
    public static DecompilerResult PrintRaised(IrFunction function, out IReadOnlyDictionary<IrNode, int> statementLines)
    {
        statementLines = new Dictionary<IrNode, int>();
        try
        {
            IrPasses.Run(function);
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var sink = new Dictionary<IrNode, int>();
            var printer = new CSharpPrinter(function) { _statementLines = sink };
            string output = printer.PrintBody(function);
            statementLines = sink;
            return new DecompilerResult(output, function.Fidelity, [.. function.Diagnostics])
            {
                ConstructorChain = printer._constructorChain,
                FieldInitializers = printer._fieldInitializers,
            };
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the <see cref="IrPasses.Lowered"/> pipeline (the default minus the
    /// cosmetic statement-sugar passes), then prints — the lowered-C# view
    /// (issue #636). Like <see cref="PrintRaised"/> this is an output path, so it
    /// owns the pass run; the result is valid, recompilable C# at a lower
    /// altitude than the shipped output.
    /// </summary>
    public static DecompilerResult PrintLowered(IrFunction function)
    {
        try
        {
            IrPasses.Run(function, IrPasses.Lowered);
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
        return Print(function);
    }

    /// <summary>
    /// Runs the print analysis on an already-raised tree purely to capture the
    /// definite-assignment dataflow facts — the per-block <c>in</c>/<c>out</c>
    /// sets that decide which locals keep <c>= default</c>. The same walk that
    /// produces the output fills the sink, so the facts are the shipped
    /// analysis, not a parallel model. The rendered C# is discarded.
    /// </summary>
    public static DataflowFacts CollectDataflowFacts(IrFunction function)
    {
        var facts = new DataflowFacts();
        var printer = new CSharpPrinter(function) { _facts = facts };
        printer.PrintBody(function);
        return facts;
    }

    public static DecompilerResult Print(IrFunction function)
    {
        try
        {
            var printer = new CSharpPrinter(function);
            string output = printer.PrintBody(function);
            return new DecompilerResult(output, function.Fidelity, [.. function.Diagnostics])
            {
                ConstructorChain = printer._constructorChain,
                FieldInitializers = printer._fieldInitializers,
            };
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Stores that double as declarations: the local's first program-order reference, at statement level in the entry block.</summary>
    readonly HashSet<IrNode> _declaringStores = [];

    /// <summary>Locals that may be read before they are definitely assigned, so their declaration must keep its `= default` zero-initializer (a bare declaration would be CS0165).</summary>
    HashSet<int> _readBeforeAssign = [];

    /// <summary>Optional sink for the definite-assignment dataflow facts; null on the shipped print path (the analysis records nothing then).</summary>
    DataflowFacts? _facts;

    /// <summary>Offsets some surviving goto targets — labels print wherever the block lives, top-level or inside a flat EH body.</summary>
    HashSet<int> _labelTargets = [];

    /// <summary>An explicit base/this chain call lifted out of a constructor body to its signature initializer (base/this calls are invalid as body statements).</summary>
    string? _constructorChain;
    IrNode? _chainStatement;

    /// <summary>Field initializers (<c>this.f = value</c> stores preceding the base call) lifted out of a constructor body to the field declarations, keyed in source order.</summary>
    readonly List<(string Field, string Value)> _fieldInitializers = [];
    readonly HashSet<IrNode> _fieldInitStores = [];

    /// <summary>Pinned local slots a <see cref="Fixed"/> statement owns: declared by the fixed header (skipped up front) and read as a pointer of the fixed's element type.</summary>
    readonly HashSet<int> _fixedLocals = [];

    /// <summary>Ref-struct locals whose hoisted declaration must spell <c>scoped</c>: a <c>stackalloc</c>-initialized span whose declaration was split from its assignment (out of the unsafe block) would otherwise warn CS9081. A stackalloc result is always scoped, so this is faithful, not a guess.</summary>
    readonly HashSet<int> _scopedLocals = [];

    /// <summary>Optional sink mapping each printed top-level statement node to its 0-based start line in the output; null on the shipped print path. Drives line-anchored overlays (annotated views) without the printer knowing what they are.</summary>
    Dictionary<IrNode, int>? _statementLines;

    string PrintBody(IrFunction function)
    {
        var sb = new StringBuilder();
        _labelTargets = CollectBranchTargets(function);
        foreach (var fixedNode in function.Descendants.OfType<Fixed>())
            _fixedLocals.Add(fixedNode.LocalIndex);
        CollectDeclaringStores(function);
        _readBeforeAssign = ComputeReadBeforeAssign(function);

        // A constructor prologue is leading field-initializer stores
        // (this.f = value) followed by the base(...)/this(...) chain call. C#
        // emits field initializers before the base call, so a this-field store
        // preceding the chain call is a field initializer — not a body
        // assignment — and the chain call is invalid as a body statement
        // (CS0175). Lift both out of the body so they render where they belong:
        // the initializers on the field declarations, the chain on the
        // signature.
        if (function.Body.Blocks is [{ } entry, ..]
            && ChainCallIndex(entry) is { } chainIndex
            && entry.Children.Take(chainIndex).All(IsFieldInitializerStore))
        {
            foreach (var store in entry.Children.Take(chainIndex).Cast<StoreField>())
            {
                _fieldInitStores.Add(store);
                _fieldInitializers.Add((store.Field.Name, Expression(store.Value)));
            }

            var chainCall = (Call)((ExpressionStatement)entry.Children[chainIndex]).Expression;
            if (ConstructorChainText(chainCall.Callee, chainCall) is { } chain)
            {
                _constructorChain = chain.TrimEnd(';');
                _chainStatement = entry.Children[chainIndex];
            }
        }

        // Remaining locals and slots declare up front, current-style.
        foreach (var declaration in CollectDeclarations(function))
            sb.AppendLine(declaration);
        if (sb.Length > 0)
            sb.AppendLine();

        AppendContainer(sb, function.Body, 0, topLevel: true);
        return sb.ToString().TrimEnd() is { Length: > 0 } text ? text + Environment.NewLine : "";
    }

    void AppendContainer(StringBuilder sb, BlockContainer container, int indent, bool topLevel = false)
    {
        string pad = new(' ', indent * 4);
        var blocks = container.Blocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (_labelTargets.Contains(block.StartOffset))
                sb.Append(pad).AppendLine($"IL_{block.StartOffset:X4}:");
            // The trailing 'return;' trims, current-style — unless it is a
            // labeled block's only statement, where trimming would strand
            // the label as invalid C#.
            bool labeledReturnOnly = _labelTargets.Contains(block.StartOffset) && block.Children.Count == 1;
            var emit = new List<IrNode>();
            foreach (var statement in block.Children)
            {
                if (ReferenceEquals(statement, _chainStatement) || _fieldInitStores.Contains(statement))
                    continue;   // lifted to the signature initializer / field declarations
                bool isLast = topLevel && i == blocks.Count - 1 && ReferenceEquals(statement, block.Children[^1]);
                if (isLast && !labeledReturnOnly && statement is Return { Value: null })
                    break;
                emit.Add(statement);
            }
            AppendStatements(sb, emit, indent);
        }
    }

    static HashSet<int> CollectBranchTargets(IrFunction function)
    {
        var targets = new HashSet<int>();
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case Branch branch: targets.Add(branch.TargetOffset); break;
                case ConditionalBranch conditional: targets.Add(conditional.TargetOffset); break;
                case Leave leave: targets.Add(leave.TargetOffset); break;
                case SwitchBranch sw: foreach (int t in sw.TargetOffsets) targets.Add(t); break;
            }
        }
        return targets;
    }

    IEnumerable<string> CollectDeclarations(IrFunction function)
    {
        var locals = new SortedSet<int>();
        var slots = new SortedDictionary<int, TypeRef?>();
        var slotLoadTypes = new Dictionary<int, TypeRef?>();
        var slotStoreTypes = new Dictionary<int, TypeRef?>();
        // Catch variables declare in their clause header, not up front.
        var clauseDeclared = function.Descendants.OfType<CatchClause>()
            .Where(clause => clause.VariableIndex is not null)
            .Select(clause => clause.VariableIndex!.Value)
            .ToHashSet();
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadLocal l: locals.Add(l.Index); break;
                case StoreLocal s: locals.Add(s.Index); break;
                case LoadLocalAddress a: locals.Add(a.Index); break;
                // A slot's declared type is the type it is loaded AS — the merged
                // join type every predecessor's store is assignable to. A store
                // value can be a subtype at a join (object slot fed a string),
                // so store types are only a fallback when the slot is never
                // loaded with a known type.
                case LoadStackSlot ls: slotLoadTypes.TryAdd(ls.Slot, ls.Type); break;
                case StoreStackSlot ss: slotStoreTypes.TryAdd(ss.Slot, ss.Value.ResultType); break;
            }
        }
        foreach (int slot in slotLoadTypes.Keys.Concat(slotStoreTypes.Keys).Distinct())
        {
            slots[slot] = slotLoadTypes.TryGetValue(slot, out var loaded) && loaded is not null
                ? loaded
                : slotStoreTypes.TryGetValue(slot, out var stored) ? stored : null;
        }
        foreach (int index in locals)
        {
            // A fixed-statement pinned local is declared by the fixed header
            // (fixed (T* V = &place)), not up front.
            if (_fixedLocals.Contains(index))
                continue;
            bool declaredAtStore = _declaringStores.Any(s =>
                s is StoreLocal store && store.Index == index
                || s is InitObject { Address: LoadLocalAddress init } && init.Index == index);
            if (!declaredAtStore && !clauseDeclared.Contains(index))
            {
                // An up-front local is referenced before a defining store, so
                // it relies on IL's zero-initialization of locals (localsinit).
                // Spell that as `= default` — both faithful and what C#'s
                // definite-assignment requires (a bare declaration is CS0165 on
                // any path that reads before assigning). When the local is
                // instead provably assigned on every path before each read, the
                // `= default` is a dead store the IL never had (it leans on
                // localsinit), so drop it and declare bare. A ref local takes
                // neither: `= default` is illegal and a bare declaration is
                // CS8174. IL zero-initializes a managed pointer to a null
                // reference, whose faithful C# spelling is Unsafe.NullRef<T>().
                // Fully qualified so the per-member view compiles without a
                // using; the whole-type hoister shortens it and adds the using.
                var type = function.Locals[index];
                string scoped = _scopedLocals.Contains(index) ? "scoped " : "";
                yield return type.Kind == TypeRefKind.ByRef
                    ? $"{TypeText(type)} {LocalName(index)} = ref System.Runtime.CompilerServices.Unsafe.NullRef<{TypeText(type.ElementType!)}>();"
                    : _readBeforeAssign.Contains(index)
                        ? $"{scoped}{TypeText(type)} {LocalName(index)} = default;"
                        : $"{scoped}{TypeText(type)} {LocalName(index)};";
            }
        }
        foreach (var (slot, type) in slots)
        {
            if (_declaringStores.OfType<StoreStackSlot>().Any(s => s.Slot == slot))
                continue;
            // A ref-typed slot, like a ref-typed local, can't be declared bare
            // (CS8174); spell IL's null-reference zero-init as Unsafe.NullRef<T>().
            yield return type is { Kind: TypeRefKind.ByRef }
                ? $"{TypeText(type)} S_{slot} = ref System.Runtime.CompilerServices.Unsafe.NullRef<{TypeText(type.ElementType!)}>();"
                : $"{(type is null ? "var" : TypeText(type))} S_{slot};";
        }
    }

    enum DefiniteFlow { FallThrough, Break, Return, Bail }

    /// <summary>
    /// Computes the locals that may be read before they are definitely assigned
    /// — the ones whose declaration must keep its <c>= default</c>. The rest are
    /// assigned on every path before each read, so their initializer is a dead
    /// store the IL never carried (locals lean on <c>.locals init</c>) and the
    /// local declares bare. A conservative structured walk: it gives up — marking
    /// every local read-before-assign — on any control flow it does not fully
    /// model (gotos/labels, leave, lock, an unmodeled node), so a bare
    /// declaration is emitted only when definite assignment is proven. The walk
    /// under-claims assignment everywhere it is unsure, which can only keep a
    /// redundant <c>= default</c>, never drop a required one.
    /// </summary>
    HashSet<int> ComputeReadBeforeAssign(IrFunction function)
    {
        var readEarly = new HashSet<int>();
        bool bailed = false;

        void BailAll()
        {
            bailed = true;
            if (_facts is not null)
                _facts.Bailed = true;
            for (int i = 0; i < function.Locals.Length; i++)
                readEarly.Add(i);
        }

        void CheckReads(IrNode? node, HashSet<int> assigned)
        {
            if (node is null)
                return;
            foreach (var n in node.Descendants.Prepend(node))
            {
                int? read = n switch
                {
                    LoadLocal l => l.Index,
                    LoadLocalAddress la => la.Index,
                    _ => null,
                };
                if (read is { } index && !assigned.Contains(index))
                    readEarly.Add(index);
            }
        }

        // assigned is the set definitely assigned on entry; it is mutated to the
        // fall-through post-state. Joins keep only locals assigned on every path
        // that reaches the merge (under-claiming when unsure).
        static void ApplyJoin(HashSet<int> assigned, List<HashSet<int>> reaching)
        {
            if (reaching.Count == 0)
                return;
            var intersection = new HashSet<int>(reaching[0]);
            for (int i = 1; i < reaching.Count; i++)
                intersection.IntersectWith(reaching[i]);
            assigned.UnionWith(intersection);
        }

        DefiniteFlow Sequence(IReadOnlyList<IrNode> statements, HashSet<int> assigned)
        {
            foreach (var statement in statements)
            {
                if (bailed)
                    return DefiniteFlow.Bail;
                var flow = Statement(statement, assigned);
                if (flow != DefiniteFlow.FallThrough)
                    return flow;
            }
            return DefiniteFlow.FallThrough;
        }

        // A container whose blocks branch among themselves (or are reached by
        // goto) cannot be read in program order. Rather than give up on every
        // local, analyze it as the control-flow graph it is.
        bool IsFlat(BlockContainer container) =>
            container.Blocks.Any(b =>
                _labelTargets.Contains(b.StartOffset)
                || (b.Children.Count > 0 && b.Children[^1] is Branch or ConditionalBranch or SwitchBranch));

        DefiniteFlow Container(BlockContainer container, HashSet<int> assigned)
        {
            if (IsFlat(container))
                return CfgContainer(container, assigned);
            foreach (var block in container.Blocks)
            {
                var flow = Sequence(block.Children, assigned);
                if (flow != DefiniteFlow.FallThrough)
                    return flow;
            }
            return DefiniteFlow.FallThrough;
        }

        // Forward must-dataflow over a goto-connected container: a local is
        // assigned on entry to a block only if assigned on every predecessor
        // edge. This is the definite-assignment the C# compiler itself runs, so
        // a local it proves bare-safe declares bare instead of flooding to
        // `= default`. Conservative on shapes it cannot model (an EH leave, a
        // branch out of the container): those still take the global bail.
        DefiniteFlow CfgContainer(BlockContainer container, HashSet<int> entryAssigned)
        {
            var blocks = container.Blocks;
            int n = blocks.Count;
            if (n == 0)
                return DefiniteFlow.FallThrough;

            // Successor edges. An unmodeled terminator (EH leave, or a branch
            // whose target is outside this container) leaves the graph
            // incomplete — fall back to the safe bail rather than reason from it.
            var edges = Cfg.Build(blocks);
            if (edges.Any(e => e.LeavesRegion || e.ExternalTargets.Count > 0))
            {
                BailAll();
                return DefiniteFlow.Bail;
            }
            var successors = new List<int>[n];
            for (int i = 0; i < n; i++)
                successors[i] = [.. edges[i].Successors];

            // gen[i]: locals block i assigns on every path through it (order
            // within the block does not matter for what holds at its exit).
            var gen = new HashSet<int>[n];
            for (int i = 0; i < n; i++)
            {
                gen[i] = [];
                foreach (var child in blocks[i].Children)
                {
                    if (child is StoreLocal store)
                        gen[i].Add(store.Index);
                    else if (child is InitObject { Address: LoadLocalAddress address })
                        gen[i].Add(address.Index);
                }
            }

            var preds = new List<int>[n];
            for (int i = 0; i < n; i++)
                preds[i] = [];
            for (int i = 0; i < n; i++)
                foreach (var s in successors[i])
                    preds[s].Add(i);

            // in[0] is the entry assignments: the external edge always supplies
            // them, and a local assigned before the container stays assigned
            // across any back edge. Other blocks start at the universe so the
            // first intersection narrows down; intersection only shrinks, so the
            // fixpoint terminates.
            var inSets = new HashSet<int>[n];
            inSets[0] = new HashSet<int>(entryAssigned);
            for (int i = 1; i < n; i++)
                inSets[i] = new HashSet<int>(Enumerable.Range(0, function.Locals.Length));

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int k = 1; k < n; k++)
                {
                    if (preds[k].Count == 0)
                        continue;  // unreachable: leaving it at the universe never narrows a reachable join
                    HashSet<int>? merged = null;
                    foreach (var p in preds[k])
                    {
                        var outP = new HashSet<int>(inSets[p]);
                        outP.UnionWith(gen[p]);
                        if (merged is null)
                            merged = outP;
                        else
                            merged.IntersectWith(outP);
                    }
                    if (merged is not null && !merged.SetEquals(inSets[k]))
                    {
                        inSets[k] = merged;
                        changed = true;
                    }
                }
            }

            // With the assignment-on-entry known per block, check reads in
            // program order within each block.
            if (_facts is not null)
            {
                var blockFacts = new List<DataflowFacts.BlockFacts>(n);
                for (int i = 0; i < n; i++)
                {
                    bool reachable = i == 0 || preds[i].Count > 0;
                    var outI = new HashSet<int>(inSets[i]);
                    outI.UnionWith(gen[i]);
                    blockFacts.Add(new DataflowFacts.BlockFacts(
                        blocks[i].StartOffset,
                        [.. preds[i].Select(p => blocks[p].StartOffset).Order()],
                        [.. successors[i].Select(s => blocks[s].StartOffset).Order()],
                        [.. gen[i].Order()],
                        reachable ? [.. inSets[i].Order()] : [],
                        reachable ? [.. outI.Order()] : [],
                        reachable));
                }
                _facts.AddContainer(new DataflowFacts.ContainerFacts(blockFacts));
            }

            for (int k = 0; k < n; k++)
            {
                var running = new HashSet<int>(inSets[k]);
                foreach (var child in blocks[k].Children)
                {
                    switch (child)
                    {
                        case StoreLocal store:
                            CheckReads(store.Value, running);
                            running.Add(store.Index);
                            break;
                        case InitObject { Address: LoadLocalAddress address }:
                            running.Add(address.Index);
                            break;
                        default:
                            CheckReads(child, running);
                            break;
                    }
                }
            }
            return DefiniteFlow.FallThrough;
        }

        DefiniteFlow Statement(IrNode node, HashSet<int> assigned)
        {
            switch (node)
            {
                case Block block:
                    if (_labelTargets.Contains(block.StartOffset))
                    {
                        BailAll();
                        return DefiniteFlow.Bail;
                    }
                    return Sequence(block.Children, assigned);
                case BlockContainer container:
                    return Container(container, assigned);
                case StoreLocal store:
                    CheckReads(store.Value, assigned);
                    assigned.Add(store.Index);
                    return DefiniteFlow.FallThrough;
                case InitObject { Address: LoadLocalAddress address }:
                    assigned.Add(address.Index);
                    return DefiniteFlow.FallThrough;
                case Return ret:
                    CheckReads(ret.Value, assigned);
                    return DefiniteFlow.Return;
                case Throw thrown:
                    CheckReads(thrown.Value, assigned);
                    return DefiniteFlow.Return;
                case Break:
                    return DefiniteFlow.Break;
                case IfStatement branch:
                    return If(branch, assigned);
                case Switch switchStatement:
                    return SwitchStatement(switchStatement, assigned);
                case TryCatch tryCatch:
                    return TryCatchStatement(tryCatch, assigned);
                case TryFinally tryFinally:
                    return TryFinallyStatement(tryFinally, assigned);
                case WhileLoop loop:
                    CheckReads(loop.Condition, assigned);
                    return Loop(loop.Body, assigned);
                case DoWhileLoop loop:
                    return DoWhile(loop, assigned);
                case ForLoop loop:
                    return For(loop, assigned);
                // A lock runs its body in program order — the monitor
                // enter/exit around it does not perturb assignment — so model it
                // as the lock object's read followed by the sequential body.
                case Lock lockNode:
                    CheckReads(lockNode.LockObject, assigned);
                    return Container(lockNode.Body, assigned);
                // A fixed statement evaluates its pin source, binds the pinned
                // local in the header, then runs its body in program order. Model
                // the source read, mark the pinned local assigned, and walk the
                // body so an inner derived-pointer store counts as an assignment
                // (otherwise it floods to `= default`, a dead store the IL lacks).
                case Fixed fixedNode:
                    CheckReads(fixedNode.PinSource, assigned);
                    assigned.Add(fixedNode.LocalIndex);
                    return Container(fixedNode.Body, assigned);
                // Unmodeled control flow: stop trusting the program order.
                case Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter:
                    BailAll();
                    return DefiniteFlow.Bail;
                // Any other node is a straight-line statement (a call, a field or
                // element store): its reads count, it assigns no local, it falls
                // through.
                default:
                    CheckReads(node, assigned);
                    return DefiniteFlow.FallThrough;
            }
        }

        DefiniteFlow If(IfStatement branch, HashSet<int> assigned)
        {
            CheckReads(branch.Condition, assigned);
            var thenSet = new HashSet<int>(assigned);
            var thenFlow = Statement(branch.Then, thenSet);
            if (!branch.HasElse)
                return DefiniteFlow.FallThrough;   // the then-arm may be skipped
            var elseSet = new HashSet<int>(assigned);
            var elseFlow = Statement(branch.Else!, elseSet);
            var reaching = new List<HashSet<int>>();
            if (thenFlow == DefiniteFlow.FallThrough)
                reaching.Add(thenSet);
            if (elseFlow == DefiniteFlow.FallThrough)
                reaching.Add(elseSet);
            ApplyJoin(assigned, reaching);
            if (thenFlow == DefiniteFlow.FallThrough || elseFlow == DefiniteFlow.FallThrough)
                return DefiniteFlow.FallThrough;
            if (thenFlow == DefiniteFlow.Bail || elseFlow == DefiniteFlow.Bail)
                return DefiniteFlow.Bail;
            // Neither arm falls through; treat a mixed leave as the one that may
            // reach an enclosing switch/loop (break), which is the safe choice.
            return thenFlow == DefiniteFlow.Break || elseFlow == DefiniteFlow.Break
                ? DefiniteFlow.Break
                : DefiniteFlow.Return;
        }

        DefiniteFlow SwitchStatement(Switch switchStatement, HashSet<int> assigned)
        {
            CheckReads(switchStatement.Value, assigned);
            bool hasDefault = false;
            var reaching = new List<HashSet<int>>();
            foreach (var section in switchStatement.Sections)
            {
                hasDefault |= section.IsDefault;
                var sectionSet = new HashSet<int>(assigned);
                var flow = Container(section.Body, sectionSet);
                if (flow == DefiniteFlow.Bail)
                    return DefiniteFlow.Bail;
                // Break or a fall-off both reach the code after the switch.
                if (flow is DefiniteFlow.FallThrough or DefiniteFlow.Break)
                    reaching.Add(sectionSet);
            }
            // Without a default the operand may match nothing and skip every
            // section, so only the entry assignments survive.
            if (hasDefault)
                ApplyJoin(assigned, reaching);
            return DefiniteFlow.FallThrough;
        }

        DefiniteFlow TryCatchStatement(TryCatch tryCatch, HashSet<int> assigned)
        {
            var trySet = new HashSet<int>(assigned);
            var tryFlow = Container(tryCatch.TryBody, trySet);
            if (tryFlow is DefiniteFlow.Bail or DefiniteFlow.Break)
            {
                BailAll();
                return DefiniteFlow.Bail;
            }
            var reaching = new List<HashSet<int>>();
            if (tryFlow == DefiniteFlow.FallThrough)
                reaching.Add(trySet);
            foreach (var clause in tryCatch.Clauses)
            {
                // A handler is entered after partial try execution, so it starts
                // from the entry assignments plus its bound exception variable.
                var catchSet = new HashSet<int>(assigned);
                if (clause.VariableIndex is { } variable)
                    catchSet.Add(variable);
                var catchFlow = Container(clause.Body, catchSet);
                if (catchFlow is DefiniteFlow.Bail or DefiniteFlow.Break)
                {
                    BailAll();
                    return DefiniteFlow.Bail;
                }
                if (catchFlow == DefiniteFlow.FallThrough)
                    reaching.Add(catchSet);
            }
            ApplyJoin(assigned, reaching);
            return reaching.Count > 0 ? DefiniteFlow.FallThrough : DefiniteFlow.Return;
        }

        DefiniteFlow TryFinallyStatement(TryFinally tryFinally, HashSet<int> assigned)
        {
            // The try sees only the entry assignments; the finally likewise (the
            // try may throw at any point). The finally always runs, so its
            // assignments hold afterward; on the normal path the try also ran.
            var trySet = new HashSet<int>(assigned);
            var tryFlow = Container(tryFinally.TryBody, trySet);
            var finallySet = new HashSet<int>(assigned);
            var finallyFlow = Container(tryFinally.FinallyBody, finallySet);
            if (tryFlow is DefiniteFlow.Bail or DefiniteFlow.Break || finallyFlow != DefiniteFlow.FallThrough)
            {
                BailAll();
                return DefiniteFlow.Bail;
            }
            assigned.UnionWith(finallySet);
            if (tryFlow == DefiniteFlow.FallThrough)
            {
                assigned.UnionWith(trySet);
                return DefiniteFlow.FallThrough;
            }
            return DefiniteFlow.Return;   // the try returns/throws past the finally
        }

        // A while/for body may run zero times and a back edge can read what a
        // later iteration assigns, so nothing the body assigns is guaranteed
        // afterward. Reads inside are still checked against the entry state.
        DefiniteFlow Loop(Block body, HashSet<int> assigned)
        {
            var bodySet = new HashSet<int>(assigned);
            if (Statement(body, bodySet) == DefiniteFlow.Bail)
                return DefiniteFlow.Bail;
            return DefiniteFlow.FallThrough;
        }

        DefiniteFlow DoWhile(DoWhileLoop loop, HashSet<int> assigned)
        {
            var bodySet = new HashSet<int>(assigned);
            if (Container(loop.Body, bodySet) == DefiniteFlow.Bail)
                return DefiniteFlow.Bail;
            CheckReads(loop.Condition, bodySet);
            return DefiniteFlow.FallThrough;
        }

        DefiniteFlow For(ForLoop loop, HashSet<int> assigned)
        {
            if (Statement(loop.Initializer, assigned) == DefiniteFlow.Bail)
                return DefiniteFlow.Bail;
            CheckReads(loop.Condition, assigned);
            var bodySet = new HashSet<int>(assigned);
            if (Statement(loop.Body, bodySet) == DefiniteFlow.Bail
                || Statement(loop.Increment, bodySet) == DefiniteFlow.Bail)
                return DefiniteFlow.Bail;
            return DefiniteFlow.FallThrough;
        }

        Container(function.Body, []);

        if (_facts is not null)
        {
            _facts.LocalNames = [.. Enumerable.Range(0, function.Locals.Length).Select(LocalName)];
            _facts.ReadBeforeAssign = [.. readEarly.Order()];
        }

        return readEarly;
    }

    /// <summary>
    /// A local declares at its store when that store is the local's first
    /// program-order reference and sits at statement level in the entry
    /// block — the current emitter's merged-declaration shape.
    /// </summary>
    void CollectDeclaringStores(IrFunction function)
    {
        if (function.Body.Blocks.Count == 0)
            return;
        var entryStatements = new HashSet<IrNode>(function.Body.Blocks[0].Children);
        var seenLocals = new HashSet<int>();
        var seenSlots = new HashSet<int>();
        // A slot stored on more than one path is a join slot: it must declare
        // once, up front, with its merged type — declaring it at one store would
        // type it from that branch's value and strand the other branch's store.
        var slotStoreCounts = new Dictionary<int, int>();
        foreach (var store in function.Descendants.OfType<StoreStackSlot>())
            slotStoreCounts[store.Slot] = slotStoreCounts.GetValueOrDefault(store.Slot) + 1;
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreLocal store when !seenLocals.Contains(store.Index):
                    seenLocals.Add(store.Index);
                    if (entryStatements.Contains(store))
                        _declaringStores.Add(store);
                    else if (store is { Parent: ForLoop forLoop, ChildIndex: 0 }
                        && LastReferenceIsInside(function, store.Index, forLoop))
                    {
                        // A for-initializer declares its variable only when
                        // every reference lives inside the loop — otherwise
                        // C# scoping demands the declaration stay outside.
                        _declaringStores.Add(store);
                    }
                    break;
                case InitObject { Address: LoadLocalAddress initTarget } init when !seenLocals.Contains(initTarget.Index):
                    // Descendants yields the InitObject before its address
                    // child, so this fires before the address marks the
                    // local as seen.
                    seenLocals.Add(initTarget.Index);
                    if (entryStatements.Contains(init))
                        _declaringStores.Add(init);
                    break;
                case LoadLocal load: seenLocals.Add(load.Index); break;
                case LoadLocalAddress address: seenLocals.Add(address.Index); break;
                case StoreStackSlot slotStore when !seenSlots.Contains(slotStore.Slot):
                    seenSlots.Add(slotStore.Slot);
                    if (entryStatements.Contains(slotStore) && slotStore.Value.ResultType is not null
                        && slotStoreCounts[slotStore.Slot] == 1)
                        _declaringStores.Add(slotStore);
                    break;
                case LoadStackSlot slotLoad: seenSlots.Add(slotLoad.Slot); break;
            }
        }

        // Under the updated memory-safety rules a declaring store whose value is
        // an unsafe operation gets wrapped in an `unsafe { }` block. If the local
        // is also read elsewhere, an inline `Type v = <unsafe>` declaration would
        // strand the variable inside that block (out of scope at its uses), so
        // demote the store: the local declares up front and the wrapped statement
        // becomes a plain `v = <unsafe>` assignment.
        if (_newMemorySafetyRules)
        {
            foreach (var store in _declaringStores.OfType<StoreLocal>().ToList())
            {
                if (!HasUnsafeOperation(store.Value) || !LocalIsRead(function, store.Index))
                    continue;
                _declaringStores.Remove(store);
                // A stackalloc-initialized span loses its inline `scoped`
                // inference when split from its declaration, so the hoisted
                // declaration must restore it (else CS9081). A stackalloc result
                // can never escape, so `scoped` is always correct here.
                if (store.Value is StackAllocArray)
                    _scopedLocals.Add(store.Index);
            }
        }
    }

    /// <summary>True when the local slot is read (loaded by value or address) anywhere in the body.</summary>
    static bool LocalIsRead(IrFunction function, int index)
        => function.Descendants.Any(n =>
            (n is LoadLocal load && load.Index == index)
            || (n is LoadLocalAddress address && address.Index == index));

    /// <summary>True when the local's last program-order reference sits inside the given subtree.</summary>
    static bool LastReferenceIsInside(IrFunction function, int localIndex, IrNode subtree)
    {
        IrNode? last = null;
        foreach (var node in function.Descendants)
        {
            if (node is LoadLocal load && load.Index == localIndex
                || node is StoreLocal store && store.Index == localIndex
                || node is LoadLocalAddress address && address.Index == localIndex)
            {
                last = node;
            }
        }
        for (var current = last; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, subtree))
                return true;
        }
        return false;
    }

    /// <summary>Recursive statement emission with indentation — structured nodes (IfStatement) nest, flat statements render through <see cref="Statement"/>.</summary>
    void AppendStatement(StringBuilder sb, IrNode node, int indent)
    {
        if (_statementLines is not null)
        {
            int startLine = 0;
            for (int c = 0; c < sb.Length; c++)
                if (sb[c] == '\n')
                    startLine++;
            _statementLines.TryAdd(node, startLine);
        }
        string pad = new(' ', indent * 4);
        if (node is Return { Value: SwitchExpression returnedSwitch })
        {
            // A switch expression returned spans several lines, one arm per line,
            // indented under the governing value — the statement context knows the
            // indent the inline Expression() form cannot.
            string inner = pad + "    ";
            sb.Append(pad).Append("return ").Append(Operand(returnedSwitch.Value)).AppendLine(" switch");
            sb.Append(pad).AppendLine("{");
            foreach (var arm in returnedSwitch.Arms)
                sb.Append(inner).Append(SwitchArmText(arm)).AppendLine(",");
            sb.Append(pad).AppendLine("};");
            return;
        }
        if (node is ForLoop forLoop)
        {
            string initializer = Statement(forLoop.Initializer)?.TrimEnd(';') ?? "";
            string increment = Statement(forLoop.Increment)?.TrimEnd(';') ?? "";
            sb.Append(pad).Append("for (").Append(initializer).Append("; ")
                .Append(Condition(forLoop.Condition)).Append("; ").Append(increment).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            AppendStatements(sb, forLoop.Body.Children, indent + 1);
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is WhileLoop whileLoop)
        {
            sb.Append(pad).Append("while (").Append(Condition(whileLoop.Condition)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            AppendStatements(sb, whileLoop.Body.Children, indent + 1);
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is DoWhileLoop doWhile)
        {
            sb.Append(pad).AppendLine("do");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, doWhile.Body, indent + 1);
            sb.Append(pad).Append("}").Append(Environment.NewLine).Append(pad)
                .Append("while (").Append(Condition(doWhile.Condition)).AppendLine(");");
            return;
        }
        if (node is TryCatch tryCatch)
        {
            sb.Append(pad).AppendLine("try");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, tryCatch.TryBody, indent + 1);
            sb.Append(pad).AppendLine("}");
            foreach (var clause in tryCatch.Clauses)
            {
                sb.Append(pad).AppendLine(CatchHeader(clause));
                sb.Append(pad).AppendLine("{");
                AppendContainer(sb, clause.Body, indent + 1);
                sb.Append(pad).AppendLine("}");
            }
            return;
        }
        if (node is Lock lockStatement)
        {
            sb.Append(pad).Append("lock (").Append(Expression(lockStatement.LockObject)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, lockStatement.Body, indent + 1);
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is Fixed fixedStatement)
        {
            sb.Append(pad)
                .Append("fixed (").Append(TypeText(fixedStatement.ElementType)).Append("* ")
                .Append(LocalName(fixedStatement.LocalIndex)).Append(" = &")
                .Append(Deref(fixedStatement.PinSource)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, fixedStatement.Body, indent + 1);
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is TryFinally tryFinally)
        {
            sb.Append(pad).AppendLine("try");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, tryFinally.TryBody, indent + 1);
            sb.Append(pad).AppendLine("}");
            sb.Append(pad).AppendLine("finally");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, tryFinally.FinallyBody, indent + 1);
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is IfStatement ifStatement)
        {
            sb.Append(pad).Append("if (").Append(Condition(ifStatement.Condition)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            AppendStatements(sb, ifStatement.Then.Children, indent + 1);
            sb.Append(pad).AppendLine("}");
            if (ifStatement.Else is { } elseArm)
            {
                sb.Append(pad).AppendLine("else");
                sb.Append(pad).AppendLine("{");
                AppendStatements(sb, elseArm.Children, indent + 1);
                sb.Append(pad).AppendLine("}");
            }
            return;
        }
        if (node is Switch switchNode)
        {
            sb.Append(pad).Append("switch (").Append(Expression(switchNode.Value)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            string labelPad = pad + "    ";
            foreach (var section in switchNode.Sections)
            {
                foreach (int label in section.Labels)
                    sb.Append(labelPad).Append("case ").Append(label).AppendLine(":");
                if (section.IsDefault)
                    sb.Append(labelPad).AppendLine("default:");
                AppendContainer(sb, section.Body, indent + 2);
            }
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (Statement(node) is { } line)
            sb.Append(pad).AppendLine(line);
    }

    /// <summary>
    /// Emits a sibling statement sequence, wrapping unsafe operations in
    /// explicit <c>unsafe { }</c> blocks when the source module uses the updated
    /// memory-safety rules. Maximal runs of adjacent statements that need an
    /// unsafe context coalesce into a single block, and the block is scoped to
    /// the smallest enclosing statement: a loop or <c>if</c> whose body (not its
    /// header) holds the unsafe op is left unwrapped so the recursion wraps the
    /// inner statement instead. When the module uses legacy rules — or the
    /// statements are already inside an emitted block — this is a plain
    /// per-statement emit, identical to the pre-feature output.
    /// </summary>
    void AppendStatements(StringBuilder sb, IReadOnlyList<IrNode> statements, int indent)
    {
        int i = 0;
        while (i < statements.Count)
        {
            if (_newMemorySafetyRules && _unsafeDepth == 0 && NeedsUnsafeContext(statements[i]))
            {
                int j = i + 1;
                while (j < statements.Count && NeedsUnsafeContext(statements[j]))
                    j++;
                string pad = new(' ', indent * 4);
                sb.Append(pad).AppendLine("unsafe");
                sb.Append(pad).AppendLine("{");
                _unsafeDepth++;
                for (int k = i; k < j; k++)
                    AppendStatement(sb, statements[k], indent + 1);
                _unsafeDepth--;
                sb.Append(pad).AppendLine("}");
                i = j;
            }
            else
            {
                AppendStatement(sb, statements[i], indent);
                i++;
            }
        }
    }

    /// <summary>
    /// Whether a statement must itself sit in an unsafe context. For a compound
    /// statement only its own header expressions are considered — the body is a
    /// separate statement sequence the recursion wraps independently, keeping the
    /// block minimal. A simple statement is tested whole.
    /// </summary>
    bool NeedsUnsafeContext(IrNode node) => node switch
    {
        ForLoop f => HasUnsafeOperation(f.Initializer) || HasUnsafeOperation(f.Condition) || HasUnsafeOperation(f.Increment),
        WhileLoop w => HasUnsafeOperation(w.Condition),
        DoWhileLoop d => HasUnsafeOperation(d.Condition),
        IfStatement s => HasUnsafeOperation(s.Condition),
        Switch s => HasUnsafeOperation(s.Value),
        Lock l => HasUnsafeOperation(l.LockObject),
        Fixed fx => HasUnsafeOperation(fx.PinSource),
        TryCatch or TryFinally => false,
        _ => HasUnsafeOperation(node),
    };

    bool HasUnsafeOperation(IrNode? node)
        => node is not null && (IsUnsafeOperation(node) || node.Descendants.Any(IsUnsafeOperation));

    /// <summary>
    /// A single IR operation that requires an unsafe context under the updated
    /// rules: a function-pointer invocation (<c>calli</c>), a read/write through
    /// an unmanaged pointer, a call to a <em>requires-unsafe</em> member (one
    /// stamped with <c>RequiresUnsafeAttribute</c> — declared <c>unsafe</c>/
    /// <c>extern</c> — or, by the compat heuristic, one with a pointer in its
    /// signature), or a <c>stackalloc</c> converted to a <c>Span</c> with no
    /// initializer in a <c>[SkipLocalsInit]</c> body. Dereferencing a managed
    /// reference (<c>ByRef</c>) is safe and excluded. Creating pointers, the
    /// <c>fixed</c> statement, and <c>sizeof</c> are safe under the new rules.
    /// </summary>
    bool IsUnsafeOperation(IrNode node) => node switch
    {
        CallIndirect => true,
        // A stackalloc-backed Span (raised to `stackalloc T[n]` by
        // StackAllocSpanPass) is governed by the stackalloc rule — unsafe only
        // under [SkipLocalsInit], where the stack space is uninitialized.
        StackAllocArray => _skipLocalsInit,
        Call c => c.Callee.RequiresUnsafe || SignatureRequiresUnsafe(c.Callee),
        NewObject n => n.Constructor.RequiresUnsafe || SignatureRequiresUnsafe(n.Constructor),
        LoadIndirect l => RendersAsPointerDeref(l.Address),
        StoreIndirect s => RendersAsPointerDeref(s.Address),
        InitObject o => RendersAsPointerDeref(o.Address),
        _ => false,
    };

    /// <summary>
    /// Compat-mode requires-unsafe heuristic for a callee whose
    /// <c>RequiresUnsafeAttribute</c> can't be read (a cross-assembly
    /// MemberRef): the member is requires-unsafe if a pointer or function-pointer
    /// type appears anywhere among its parameter or return types — possibly
    /// nested in a non-pointer type such as <c>int*[]</c>. Mirrors the spec's
    /// compat fallback, which keeps such calls unsafe during the migration window
    /// even for callers that haven't opted into the new rules.
    /// </summary>
    static bool SignatureRequiresUnsafe(MethodRef callee)
        => ContainsPointer(callee.ReturnType) || callee.ParameterTypes.Any(ContainsPointer);

    static bool ContainsPointer(TypeRef? type)
        => type is not null
            && (type.Kind is TypeRefKind.Pointer or TypeRefKind.FunctionPointer
                || ContainsPointer(type.ElementType)
                || type.TypeArguments.Any(ContainsPointer));

    /// <summary>
    /// Whether <see cref="Deref"/> renders this load/store-indirect address with
    /// a leading <c>*</c> — i.e. it is a read/write through an unmanaged pointer
    /// rather than a managed reference. Mirrors the managed-reference cases of
    /// <see cref="Deref"/> exactly: anything not spelled as a place or a
    /// <c>ByRef</c> is a pointer dereference, which requires an unsafe context.
    /// </summary>
    static bool RendersAsPointerDeref(IrExpression address) => address switch
    {
        LoadArgument { Index: 0, Name: "this" } => false,
        LoadLocalAddress => false,
        LoadArgumentAddress => false,
        LoadFieldAddress => false,
        LoadElementAddress => false,
        Conditional { ResultType.Kind: TypeRefKind.ByRef } c
            when c.WhenTrue.ResultType?.Kind == TypeRefKind.ByRef
                && c.WhenFalse.ResultType?.Kind == TypeRefKind.ByRef => false,
        { ResultType.Kind: TypeRefKind.ByRef } => false,
        _ => true,
    };

    /// <summary>
    /// A constructor-chain call renders as a <c>base(args)</c> / <c>this(args)</c>
    /// body statement (the current emitter's placement, not a header
    /// initializer). The implicit parameterless base call — every default
    /// chain — is suppressed.
    /// </summary>
    string? ConstructorChainText(MethodRef callee, Call call)
    {
        bool isThis = Equals(callee.DeclaringType, _function.DeclaringType);
        var arguments = call.Arguments.Skip(1).ToList();
        if (!isThis && arguments.Count == 0)
            return null;  // implicit base()
        return $"{(isThis ? "this" : "base")}({Arguments(arguments, callee.ParameterTypes, callee.ParameterRefKinds)});";
    }

    /// <summary>The index of the base/this <c>.ctor</c> chain call in the entry block, or null when the body has none (a struct ctor, a static method, a body that never chains).</summary>
    static int? ChainCallIndex(Block entry)
    {
        for (int i = 0; i < entry.Children.Count; i++)
        {
            if (entry.Children[i] is ExpressionStatement { Expression: Call { Callee: { Name: ".ctor", HasThis: true } } call }
                && call.Arguments is [LoadArgument { Index: 0 }, ..])
            {
                return i;
            }
        }
        return null;
    }

    /// <summary>
    /// A <c>this.field = value</c> store whose value is a field initializer:
    /// self-contained (no <c>this</c>, parameter, local, or slot load), so it is
    /// legal in field-declaration context. C# field initializers cannot read the
    /// instance or constructor parameters, which is exactly the place-load ban.
    /// </summary>
    static bool IsFieldInitializerStore(IrNode node)
        => node is StoreField { HasInstance: true, Instance: LoadArgument { Index: 0 } } store
            && !ReferencesPlace(store.Value);

    static bool ReferencesPlace(IrExpression value)
    {
        foreach (var node in (IEnumerable<IrNode>)[value, .. value.Descendants])
        {
            if (node is LoadArgument or LoadLocal or LoadStackSlot or LoadLocalAddress or LoadArgumentAddress)
                return true;
        }
        return false;
    }

    /// <summary>Baseline-style clause headers: bare <c>catch</c> for object (the catch-all), the variable form when the entry store folded into the clause.</summary>
    string CatchHeader(CatchClause clause)
        => clause.ExceptionType is { Namespace: "System", Name: "Object" }
            ? "catch"
            : clause.VariableIndex is { } index
                ? $"catch ({TypeText(clause.ExceptionType)} {LocalName(index)})"
                : $"catch ({TypeText(clause.ExceptionType)})";

    /// <summary>Null means the statement has no body spelling: a no-argument base-constructor call is implicit in C#.</summary>
    string? Statement(IrNode node) => node switch
    {
        ExpressionStatement
        {
            Expression: Call { Callee: { Name: ".ctor", HasThis: true } callee } call,
        } when call.Arguments is [LoadArgument { Index: 0 }, ..]
            => ConstructorChainText(callee, call),
        ExpressionStatement e => e.Expression is UnsupportedNode u
            ? $"/* {u.Describe()} */"
            : $"{Expression(e.Expression)};",
        // Storing into a ref-typed local rebinds the reference itself (stloc of
        // a managed pointer), not a write-through — that is C#'s ref
        // (re)assignment, which takes `= ref <place>` on both the initial
        // declaration (CS8172) and any later rebind (CS8173). Deref renders the
        // address value as the place it refers to.
        StoreLocal { Type.Kind: TypeRefKind.ByRef } s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Type)} {LocalName(s.Index)} = ref {Deref(s.Value)};"
            : $"{LocalName(s.Index)} = ref {Deref(s.Value)};",
        StoreLocal s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Type)} {LocalName(s.Index)} = {CastValue(s.Value, s.Type)};"
            : AssignmentText($"{LocalName(s.Index)}", s.Value, left => left is LoadLocal load && load.Index == s.Index, s.Type),
        StoreArgument s => AssignmentText(s.Name, s.Value, left => left is LoadArgument load && load.Index == s.Index, s.Type),
        // A ref-typed slot stores by rebinding the reference — C#'s ref
        // (re)assignment, exactly as for ref locals above.
        StoreStackSlot { Value.ResultType.Kind: TypeRefKind.ByRef } s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Value.ResultType!)} S_{s.Slot} = ref {Deref(s.Value)};"
            : $"S_{s.Slot} = ref {Deref(s.Value)};",
        StoreStackSlot s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Value.ResultType!)} S_{s.Slot} = {Expression(s.Value)};"
            : AssignmentText($"S_{s.Slot}", s.Value, left => left is LoadStackSlot load && load.Slot == s.Slot),
        StoreField s => AssignmentText(
            FieldTarget(s.Field, s.Instance), s.Value,
            left => left is LoadField load
                && load.Field.Name == s.Field.Name
                && Equals(load.Field.DeclaringType, s.Field.DeclaringType)
                && SamePlace(load.Instance, s.Instance),
            s.Field.Type),
        StoreProperty s => $"{PropertyTarget(s.Accessor, s.HasInstance ? s.Instance : null, s.IndexArguments, s.PropertyName, s.IsVirtual)} = {Expression(s.Value)};",
        StoreElement s => $"{Expression(s.Array)}[{Expression(s.Index)}] = {CastValue(s.Value, s.ElementType)};",
        StoreIndirect s => $"{Deref(s.Address)} = {CastValue(s.Value, IndirectStoreType(s.Address, s.Type))};",
        // default-initialization of a named place spells through the place,
        // not its address.
        InitObject { Address: LoadLocalAddress local } init => _declaringStores.Contains(init)
            ? $"{TypeText(init.Type)} {LocalName(local.Index)} = default;"
            : $"{LocalName(local.Index)} = default;",
        InitObject { Address: LoadArgumentAddress argument } => $"{argument.Name} = default;",
        InitObject { Address: LoadFieldAddress field } o2 => $"{FieldTarget(field.Field, field.Instance)} = default;",
        InitObject o => $"{Deref(o.Address)} = default({TypeText(o.Type)});",
        Return { Value: { } value } => $"return {CastValue(value, _function.Signature.ReturnType)};",
        Return => "return;",
        // The rethrow: the raw caught value thrown back is C#'s bare throw.
        Throw { Value: CaughtException } => "throw;",
        Throw t => $"throw {Expression(t.Value)};",
        Break => "break;",
        Branch b => $"goto IL_{b.TargetOffset:X4};",
        ConditionalBranch c => $"if ({Condition(c.Condition)}) goto IL_{c.TargetOffset:X4};",
        SwitchBranch s => $"switch ({Expression(s.Value)}) goto [{string.Join(", ", s.TargetOffsets.Select(t => $"IL_{t:X4}"))}];",
        Leave l => $"goto IL_{l.TargetOffset:X4}; // leave",
        EndFinally => "// endfinally",
        EndFilter f => $"// endfilter({Expression(f.Value)})",
        _ => $"/* {node.Describe()} */",
    };

    string Expression(IrExpression node) => node switch
    {
        LoadArgument a => a.Name,
        LoadLocal l => $"{LocalName(l.Index)}",
        LoadStackSlot s => $"S_{s.Slot}",
        Constant { Value: int } c when EnumMemberName(c) is { } named => named,
        // A retyped enum constant with no single named member (a composite flag
        // value, or one outside the resolved member map) is still that enum — a
        // bare int is CS0266. Cast it; naming flag combinations is a later slice.
        // A negative value must be parenthesized after the cast (else CS0075).
        Constant { Value: int value, Type: { } enumType } when _function.TypeShapes.GetValueOrDefault(enumType) == TypeShape.Enum
            => $"({TypeText(enumType)}){(value < 0 ? $"({value})" : value.ToString(CultureInfo.InvariantCulture))}",
        Constant c => ConstantText(c),
        LoadField f => FieldTarget(f.Field, f.Instance),
        Binary b => BinaryText(b),
        Comparison c => ComparisonText(c),
        LogicalNot n => $"!{Operand(n.Operand)}",
        LogicalBinary l => LogicalText(l),
        Conditional t => $"{Condition(t.Condition)} ? {Operand(t.WhenTrue)} : {Operand(t.WhenFalse)}",
        SwitchExpression se => SwitchExpressionInline(se),
        Coalesce co => $"{Operand(co.Left)} ?? {Operand(co.Right)}",
        NullConditional nc => NullConditionalText(nc),
        Unary { Kind: UnaryKind.Negate } u => $"-{Operand(u.Operand)}",
        Unary u => $"~{Operand(u.Operand)}",
        IncrementDecrement id => id.IsPrefix
            ? $"{(id.IsIncrement ? "++" : "--")}{Operand(id.Target)}"
            : $"{Operand(id.Target)}{(id.IsIncrement ? "++" : "--")}",
        Convert v => ConvertText(v),
        Call c => CallText(c),
        CallIndirect ci => $"{Operand(ci.Pointer)}({Arguments(ci.Arguments)})",
        DelegateCreation d => $"new {TypeText(d.DelegateType)}({MethodGroupText(d.Method, d.Target)})",
        AddressOfMethod m => AddressOfMethodText(m),
        LoadFunctionPointer p => $"/* {p.Describe()} */",
        LoadProperty p => PropertyTarget(p.Accessor, p.HasInstance ? p.Instance : null, p.IndexArguments, p.PropertyName, p.IsVirtual),
        NewObject n => $"new {TypeText(n.Constructor.DeclaringType)}({Arguments(n.Arguments, n.Constructor.ParameterTypes, n.Constructor.ParameterRefKinds)})",
        ArrayLength l => $"{Operand(l.Array)}.Length",
        LoadElement e => $"{Operand(e.Array)}[{Expression(e.Index)}]",
        NewArray n => $"new {TypeText(n.ElementType)}[{Expression(n.Length)}]",
        SpanLiteral s => $"new {TypeText(s.ElementType)}[] {{ {string.Join(", ", s.Elements.Select(Expression))} }}",
        CollectionExpression c => $"[{string.Join(", ", c.Elements.Select(Expression))}]",
        StackAllocate s => $"stackalloc byte[{Expression(s.Size)}]",
        StackAllocArray s => $"stackalloc {TypeText(s.ElementType)}[{Expression(s.Count)}]",
        Box b => Expression(b.Operand),
        IsInstance i => $"{Operand(i.Operand)} {(IsValueTypeTarget(i.Type) ? "is" : "as")} {TypeText(i.Type)}",
        CastClass c => $"({TypeText(c.Type)}){Operand(c.Operand)}",
        UnboxAny u => $"({TypeText(u.Type)}){Operand(u.Operand)}",
        Unbox u => $"ref ({TypeText(u.Type)}){Operand(u.Operand)}",
        LoadLocalAddress a => $"ref {LocalName(a.Index)}",
        LoadArgumentAddress a => $"ref {a.Name}",
        LoadFieldAddress f => $"ref {FieldTarget(f.Field, f.Instance)}",
        LoadElementAddress e => $"ref {Operand(e.Array)}[{Expression(e.Index)}]",
        LoadIndirect l => Deref(l.Address),
        SizeOf s => $"sizeof({TypeText(s.Type)})",
        TypeOf t => $"typeof({TypeText(t.Type)})",
        LoadToken t => t.Kind == RuntimeTokenKind.Type && t.Type is not null
            ? $"typeof({TypeText(t.Type)})"
            : $"/* {t.Describe()} */",
        CaughtException => "__exception",
        UnsupportedNode u => $"/* {u.Describe()} */",
        _ => $"/* {node.Describe()} */",
    };

    string BinaryText(Binary binary)
    {
        // div.un/rem.un compute on unsigned operands; shr.un shifts an
        // unsigned left operand. Operands that are already unsigned (or
        // float, where .un means unordered, not unsigned) print plain.
        bool castBoth = binary.IsUnsigned && binary.Kind is BinaryKind.Divide or BinaryKind.Remainder;
        bool castLeft = castBoth || (binary.IsUnsigned && binary.Kind is BinaryKind.ShiftRight);
        string left = castLeft ? UnsignedOperand(binary.Left) : Operand(binary.Left);
        bool isShift = binary.Kind is BinaryKind.ShiftLeft or BinaryKind.ShiftRight;
        string right = isShift ? ShiftCount(binary)
            : castBoth ? UnsignedOperand(binary.Right)
            : Operand(binary.Right);
        string text = $"{left} {BinaryOperator(binary)} {right}";
        // add.ovf/sub.ovf/mul.ovf (and their .un forms) carry an overflow check
        // the default (unchecked) C# context would drop — spell it explicitly so
        // the recompiled IL keeps the .ovf opcode. A nested checked binary
        // re-wraps redundantly but emits the same opcode stream.
        return binary.IsChecked ? $"checked({text})" : text;
    }

    /// <summary>
    /// Renders a shift's count operand, stripping a redundant width mask. C#
    /// masks a shift count by the left operand's width — int/uint by 31, long/
    /// ulong by 63 — and the compiler bakes that mask into the IL. Reading it
    /// back and spelling it explicitly (<c>n &amp; 31</c>) would double-mask on
    /// recompile, since the compiler re-applies its own mask. Dropping a count
    /// mask that exactly matches the implicit width mask keeps the opcode stream
    /// faithful (and the masks are idempotent, so the value is unchanged).
    /// </summary>
    string ShiftCount(Binary shift)
    {
        if (shift.Right is Binary { Kind: BinaryKind.And, Right: Constant { Value: int mask } } masked
            && ShiftWidthMask(EffectiveType(shift.Left)) is { } width && mask == width)
            return Operand(masked.Left);
        return Operand(shift.Right);
    }

    static int? ShiftWidthMask(TypeRef? leftOperand) => TypeFamilies.Of(leftOperand) switch
    {
        StackFamily.I4 => 31,
        StackFamily.I8 => 63,
        _ => null,
    };

    string ComparisonText(Comparison comparison)
        => ComparisonText(comparison.Kind, comparison.IsUnsigned, comparison.Left, comparison.Right);

    string ComparisonText(ComparisonKind kind, bool isUnsigned, IrExpression left, IrExpression right)
    {
        // On floats .un means UNORDERED, and C#'s ordering operators are
        // ordered — 'a >= b unordered' must print as !(a < b) or NaN inputs
        // execute the wrong path. Equality needs no special form: C#'s ==
        // is beq and != is bne.un already.
        if (isUnsigned && IsFloatComparison(left, right)
            && kind is ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
                or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual)
        {
            return $"!({Operand(left)} {ComparisonOperator(Conditions.Inverse(kind))} {Operand(right)})";
        }
        // Null tests render the is-form (taste doc): it is always the
        // reference test the IL performs, where == could round-trip to an
        // op_Equality call under operator overloads.
        if (kind is ComparisonKind.Equal or ComparisonKind.NotEqual
            && (left is Constant { Value: null } || right is Constant { Value: null }))
        {
            var operand = right is Constant { Value: null } ? left : right;
            return kind == ComparisonKind.Equal
                ? $"{Operand(operand)} is null"
                : $"{Operand(operand)} is not null";
        }
        return isUnsigned
            ? $"{UnsignedOperand(left)} {ComparisonOperator(kind)} {UnsignedOperand(right)}"
            : $"{Operand(left)} {ComparisonOperator(kind)} {Operand(right)}";
    }

    static bool IsFloatComparison(IrExpression left, IrExpression right)
        => TypeFamilies.IsFloat(left.ResultType) || TypeFamilies.IsFloat(right.ResultType);

    /// <summary>Casts a signed-integer operand to its unsigned counterpart; already-unsigned, float (.un = unordered), and unknown-typed operands print plain.</summary>
    string UnsignedOperand(IrExpression operand)
    {
        string? cast = TypeFamilies.UnsignedCastKeyword(operand.ResultType);
        return cast is null ? Operand(operand) : $"({cast}){Operand(operand)}";
    }

    /// <summary>Conditions render brtrue's raw value as-is; LogicalNot over a comparison folds via the shared type-aware duals (float folds flip the unordered flag).</summary>
    string Condition(IrExpression condition) => condition switch
    {
        LogicalNot { Operand: Comparison c } => ComparisonText(
            Conditions.Inverse(c.Kind),
            IsFloatComparison(c.Left, c.Right) ? !c.IsUnsigned : c.IsUnsigned,
            c.Left, c.Right),
        // brtrue/brfalse test any I4/ref value; C# conditions need bool —
        // non-bool operands spell the comparison the branch performs.
        LogicalNot { Operand: { } operand } when Truthiness(operand) is { } negated => negated.Inverted,
        LogicalNot n => $"!{Operand(n.Operand)}",
        _ when Truthiness(condition) is { } truthy => truthy.Direct,
        _ => Expression(condition),
    };

    /// <summary>
    /// Spellings for a non-bool branch operand: <c>!= 0</c> for integers and
    /// enums, <c>is null</c>/<c>is not null</c> for reference shapes. The
    /// operand is a <c>brfalse</c>/<c>brtrue</c> value, so the CLI constrains
    /// it to int, native int, object reference, or managed pointer — never a
    /// struct value. A generic instance is therefore always a reference type
    /// (generic value types cannot be branch operands, and enums are never
    /// generic), so it null-tests with no resolution. A bare definition is
    /// reference-or-enum; the importer's same-assembly shape resolution tells
    /// them apart where it can, and an unresolved (cross-assembly) definition
    /// still prints raw rather than guess.
    /// </summary>
    (string Direct, string Inverted)? Truthiness(IrExpression operand)
    {
        // A value-type `isinst` already renders as the boolean `obj is T`, so it
        // is its own truth value — wrapping it in `!= 0` would be `bool != int`
        // (CS0019). The inverse spells the negated pattern.
        if (operand is IsInstance ii && IsValueTypeTarget(ii.Type))
            return ($"{Operand(operand)}", $"!({Operand(operand)})");

        var type = operand.ResultType;
        if (type is null || type is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary })
            return null;

        string text = Operand(operand);
        (string, string) reference = ($"{text} is not null", $"{text} is null");
        (string, string) integer = ($"{text} != 0", $"{text} == 0");

        switch (TypeFamilies.Of(type))
        {
            // Boolean was filtered above, so an I4 family here is a real integer (or char).
            case StackFamily.I4 or StackFamily.I8 or StackFamily.I:
                return integer;
            case StackFamily.O:
                return reference;
            case StackFamily.F:
                return null;   // a float is never a branch operand
        }

        // No primitive family. A generic instance is provably a reference; a
        // bare definition resolves by its same-assembly shape.
        if (type.Kind == TypeRefKind.GenericInstance)
            return reference;

        return _function.TypeShapes.GetValueOrDefault(type) switch
        {
            TypeShape.Reference => reference,
            TypeShape.Enum => integer,
            _ => null,   // a struct cannot be a branch operand; unknown stays raw
        };
    }

    /// <summary>The text of one switch-expression arm: its labels (or <c>_</c>) and the value it yields.</summary>
    string SwitchArmText(SwitchExpressionArm arm)
        => $"{(arm.IsDefault ? "_" : string.Join(" or ", arm.Labels))} => {Expression(arm.Value)}";

    /// <summary>The single-line form of a switch expression, used when it is nested inside another expression.</summary>
    string SwitchExpressionInline(SwitchExpression node)
        => $"{Operand(node.Value)} switch {{ {string.Join(", ", node.Arms.Select(SwitchArmText))} }}";

    /// <summary>Parenthesizes compound operands; leaves atoms bare. Conservative until the precedence visitor exists.</summary>
    string Operand(IrExpression node)
    {
        string text = Expression(node);
        // A Call is an atom only when it spells as a method invocation; an
        // operator-spelled call (op_Inequality → `a != b`, op_UnaryNegation →
        // `-x`) renders as a compound expression, so it must parenthesize like
        // any other binary/unary — otherwise an enclosing `!`/`-`/binary
        // misbinds to its first operand (e.g. `!a != b`, CS0023).
        bool atomic = node is LoadArgument or LoadLocal or LoadStackSlot or Constant or LoadField
            or NewObject or ArrayLength or LoadElement or CaughtException or SizeOf or LoadToken
            or LoadProperty or TypeOf or DelegateCreation or CallIndirect or AddressOfMethod or NullConditional
            or IncrementDecrement or SpanLiteral or CollectionExpression
            || node is Call call && !IsOperatorCall(call);
        return atomic ? text : $"({text})";
    }

    /// <summary>
    /// The C# place a load/store-indirect reads or writes. Dereferencing a
    /// managed reference is implicit in C#: the address of a place (ref local,
    /// ref argument, ref field, ref element) reads back as the place, and a
    /// ref/out parameter or ref local reads as itself — no <c>*</c>. Only a
    /// genuine unmanaged pointer takes the <c>*</c>; an unknown reference keeps
    /// it rather than guess.
    /// </summary>
    string Deref(IrExpression address) => address switch
    {
        // `this` in a value-type instance method is a managed pointer to the
        // value (IL's `ldarg.0` pushes `T&`), but C#'s `this` already denotes
        // the value, so reading through it is just `this` — never `*this`,
        // which is CS0193 (DeclaringType is never an unmanaged pointer).
        LoadArgument { Index: 0, Name: "this" } => "this",
        LoadLocalAddress a => $"{LocalName(a.Index)}",
        LoadArgumentAddress a => a.Name,
        LoadFieldAddress f => FieldTarget(f.Field, f.Instance),
        LoadElementAddress e => $"{Operand(e.Array)}[{Expression(e.Index)}]",
        { ResultType.Kind: TypeRefKind.Pointer } => $"*{Operand(address)}",
        // A ref-typed conditional is a ref ternary: the `ref` binds each arm
        // (`cond ? ref a : ref b`), not the expression as a whole — placing it
        // outside is CS8173 in a `= ref` position. This spelling applies only
        // when both arms are themselves references. A conditional typed ref by
        // an upstream merge that carries a non-reference arm is an inexpressible
        // merge — no valid `= ref` form exists for it — so it falls to the
        // generic spelling as a best effort, not a correctness guarantee. Only
        // BooleanFoldingPass.FoldSlotDiamond produces these, and an asymmetric
        // ref/value slot merge is not seen in non-synthetic IL.
        Conditional { ResultType.Kind: TypeRefKind.ByRef } c
            when c.WhenTrue.ResultType?.Kind == TypeRefKind.ByRef
                && c.WhenFalse.ResultType?.Kind == TypeRefKind.ByRef
            => $"({Condition(c.Condition)} ? ref {Deref(c.WhenTrue)} : ref {Deref(c.WhenFalse)})",
        { ResultType.Kind: TypeRefKind.ByRef } => Operand(address),
        _ => $"*{Operand(address)}",
    };

    /// <summary>
    /// The C# type a store-indirect writes through. A primitive <c>stind</c>
    /// opcode carries its own signed type (<c>stind.i4</c> → <c>int</c>) which
    /// can contradict the pointer it writes through: <c>*(uint*)p = (int)x</c>
    /// is int→uint (CS0266), since the C# lvalue <c>*p</c> is typed by the
    /// pointer (<c>uint</c>), not the opcode. Prefer the element type rooted in
    /// the address — the faithful target — falling back to the opcode type when
    /// the address carries no pointer/managed-ref type (an untyped <c>stind</c>).
    /// </summary>
    TypeRef? IndirectStoreType(IrExpression address, TypeRef? opcodeType) => PointeeType(address) ?? opcodeType;

    /// <summary>The element a pointer/managed-ref address points at, seen through the additive pointer arithmetic (<c>p + i</c>) and conversions that an indexed store roots in.</summary>
    static TypeRef? PointeeType(IrExpression address) => address.ResultType switch
    {
        { Kind: TypeRefKind.Pointer or TypeRefKind.ByRef, ElementType: { } element } => element,
        _ => address switch
        {
            Binary { Kind: BinaryKind.Add or BinaryKind.Subtract } b => PointeeType(b.Left) ?? PointeeType(b.Right),
            Convert c => PointeeType(c.Operand),
            _ => null,
        },
    };

    /// <summary>
    /// Short-circuit composition prints comparisons and nots bare (they bind
    /// tighter than &amp;&amp;/||); same-kind chains associate without parens;
    /// mixed kinds parenthesize.
    /// </summary>
    string LogicalText(LogicalBinary logical)
    {
        // Sides are condition positions: Condition() owns truthiness (a
        // string operand spells 'is not null', never '!value') and the
        // negation folds. Same-kind chains associate bare; mixed kinds
        // and ternaries parenthesize.
        string Side(IrExpression side) => side switch
        {
            LogicalBinary nested when nested.Kind == logical.Kind => LogicalText(nested),
            LogicalBinary nested => $"({LogicalText(nested)})",
            Conditional => $"({Expression(side)})",
            _ => Condition(side),
        };
        string op = logical.Kind == LogicalKind.And ? "&&" : "||";
        return $"{Side(logical.Left)} {op} {Side(logical.Right)}";
    }

    /// <summary>
    /// Assignment spelling with compound/increment sugar: when the value is
    /// an unchecked binary whose left operand reads the assignment target,
    /// the runtime style is x++/x-- for ±1 and x op= rest otherwise.
    /// </summary>
    string AssignmentText(string target, IrExpression value, Func<IrExpression, bool> readsTarget, TypeRef? targetType = null)
    {
        if (value is Binary { IsChecked: false } binary && readsTarget(binary.Left))
        {
            // A compound assignment only forms when the value reads the target
            // in same-type arithmetic, so the result already matches the target
            // — no conversion is involved on this path.
            if (binary.Kind is BinaryKind.Add or BinaryKind.Subtract && binary.Right is Constant { Value: 1 })
                return $"{target}{(binary.Kind == BinaryKind.Add ? "++" : "--")};";
            return $"{target} {BinaryOperator(binary)}= {Operand(binary.Right)};";
        }
        return $"{target} = {CastValue(value, targetType)};";
    }

    /// <summary>
    /// Renders <paramref name="value"/> for a position typed <paramref name="target"/>,
    /// inserting the explicit numeric cast C# requires when the value's type
    /// would not implicitly convert (the missing-cast case behind CS0266). The
    /// cast reinterprets bits the evaluation stack already carries (same family),
    /// so it is faithful to the IL — see <see cref="TypeFamilies.NeedsNumericCast"/>.
    /// </summary>
    string CastValue(IrExpression value, TypeRef? target)
    {
        // A fixed-statement pinned local reads as a pointer (the fixed variable),
        // so the IL's conv.u/conv.i deriving an unmanaged pointer from it
        // (Convert over the pinned load) is a pointer reinterpret. Into a pointer
        // target that is the explicit pointer cast — (uint*)V_0 — not the
        // managed-reference-to-nuint conversion the conv would otherwise print.
        if (target is { Kind: TypeRefKind.Pointer }
            && value is Convert { Operand: LoadLocal pinnedLoad }
            && _fixedLocals.Contains(pinnedLoad.Index))
        {
            return $"({TypeText(target)}){LocalName(pinnedLoad.Index)}";
        }
        // An integer flowing into an enum-typed position — a comparison kind, a
        // flags value computed at run time — needs an explicit (Enum)x cast: C#
        // converts int→enum implicitly only for the literal 0. The cast is always
        // legal off any integer and is faithful, since IL carries an enum as its
        // underlying integer (TypedConstantsPass already retypes the constant
        // operands, so only the genuinely non-constant boundaries reach here).
        // This runs before the merge-node bail below: a ternary of integer arms
        // into an enum (`flag ? 1 : 0`) is a real CS0266, and the cast wraps the
        // whole merge — `(StringComparison)(flag ? 1 : 0)` — which is legal off a
        // concrete enum target (the bail's CS0030 risk is type-parameter-only).
        if (target is { } enumTarget
            && _function.TypeShapes.GetValueOrDefault(enumTarget) == TypeShape.Enum
            && EffectiveType(value) is { } enumSource && !enumTarget.Equals(enumSource)
            && TypeFamilies.IsIntegerLike(enumSource))
        {
            // A negative literal must be parenthesized after the cast (CS0075),
            // as the enum-constant path above (line ~482) already does.
            bool negativeLiteral = value is Constant { Value: int iv } && iv < 0
                || value is Constant { Value: long lv } && lv < 0;
            return $"({TypeText(enumTarget)}){(negativeLiteral ? $"({Operand(value)})" : Operand(value))}";
        }
        // Cast only off a value whose rendered C# type reliably equals its IR
        // result type. A merge node (ternary/coalesce) reports a merged type the
        // arms may not actually share, and a stack slot's type is the join of
        // every store — both diverge from the rendered type in slot-confused or
        // generic bodies, where a keyed cast would be illegal (CS0030). Leave
        // those to render as-is (the enum cast above is the one safe exception).
        if (value is Conditional or Coalesce or LoadStackSlot)
            return Expression(value);
        // A constant carries an exact value: C# converts an in-range one to the
        // target type implicitly (render bare), while an out-of-range one — a
        // negative into unsigned, a bitmask wider than the target — does not
        // convert bare and is CS0266/CS0221, so reinterpret its bits with an
        // unchecked cast (uint.MaxValue's `ldc.i4.m1` → unchecked((uint)(-1))).
        if (value is Constant { Value: int or long } konst && target is { } t && TypeFamilies.IsNumericPrimitive(t))
            return NumericConstant(konst, t);
        if (!TypeFamilies.NeedsNumericCast(EffectiveType(value), target))
            return Expression(value);
        // A plain conversion to a same-width sibling (conv.u2 → ushort feeding a
        // char slot) is subsumed by the boundary cast: emit one cast to the
        // target on the conversion's operand, not (char)((ushort)x). An
        // out-of-range constant operand still needs the unchecked spelling.
        if (value is Convert { IsChecked: false, IsUnsigned: false } conv && TypeFamilies.SameWidth(conv.Target, target))
            return conv.Operand is Constant { Value: int or long } convConst
                ? NumericConstant(convConst, target!)
                : $"({TypeText(target!)}){Operand(conv.Operand)}";
        return $"({TypeText(target!)}){Operand(value)}";
    }

    /// <summary>
    /// An integer constant rendered for a numeric target: bare when in range (C#
    /// converts it implicitly), reinterpreted with an unchecked cast when out of
    /// range (a negative into unsigned, a mask wider than the target — CS0031/
    /// CS0221 as a bare or plain-cast literal).
    /// </summary>
    string NumericConstant(Constant konst, TypeRef target)
    {
        long literal = konst.Value is int i ? i : (long)konst.Value!;
        return TypeFamilies.ConstantFits(literal, target)
            ? Expression(konst)
            : $"unchecked(({TypeText(target)})({Expression(konst)}))";
    }

    /// <summary>
    /// The C# type the rendered expression actually has. For an unsigned
    /// div/rem/shr the printer casts the operands to their unsigned type, so the
    /// rendered result is unsigned even though the node's ECMA binary-promotion
    /// ResultType keeps the signed operand type; reflect that so a boundary into
    /// the matching unsigned type does not redundantly re-cast.
    /// </summary>
    static TypeRef? EffectiveType(IrExpression value)
        => value is Binary { IsUnsigned: true, Kind: BinaryKind.Divide or BinaryKind.Remainder or BinaryKind.ShiftRight }
            && TypeFamilies.UnsignedCounterpart(value.ResultType) is { } unsigned
            ? unsigned
            : value.ResultType;

    /// <summary>Structural same-place check for compound-assignment receivers; conservative (this/locals/arguments/static only).</summary>
    static bool SamePlace(IrExpression? a, IrExpression? b) => (a, b) switch
    {
        (null, null) => true,
        (LoadArgument x, LoadArgument y) => x.Index == y.Index,
        (LoadLocal x, LoadLocal y) => x.Index == y.Index,
        _ => false,
    };

    /// <summary>True when a non-instance call renders as a C# operator (`a != b`, `-x`) rather than a method invocation — the compound form that must parenthesize as an operand.</summary>
    bool IsOperatorCall(Call call) => !call.Callee.HasThis && call.Callee.IsSpecialName && OperatorSpelling(call) is not null;

    /// <summary>
    /// True when <paramref name="type"/> is a value type, so an <c>isinst</c>
    /// type-test must spell <c>obj is T</c> — <c>obj as T</c> is CS0077 on a
    /// non-nullable value type. Primitives are value types intrinsically;
    /// other definitions resolve through the shape map (enums included).
    /// </summary>
    bool IsValueTypeTarget(TypeRef type)
        => TypeFamilies.IsNumericPrimitive(type)
            || type is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary }
            || _function.TypeShapes.GetValueOrDefault(type) is TypeShape.ValueType or TypeShape.Enum;

    /// <summary>The operator form of an op_* call, or null when the name has no spelling (op_True/op_False and friends stay as calls).</summary>
    string? OperatorSpelling(Call call)
    {
        var arguments = call.Arguments;
        if (arguments.Count == 2)
        {
            string? op = call.Callee.Name switch
            {
                "op_Equality" => "==", "op_Inequality" => "!=",
                "op_LessThan" => "<", "op_LessThanOrEqual" => "<=",
                "op_GreaterThan" => ">", "op_GreaterThanOrEqual" => ">=",
                "op_Addition" => "+", "op_Subtraction" => "-",
                "op_Multiply" => "*", "op_Division" => "/", "op_Modulus" => "%",
                "op_BitwiseAnd" => "&", "op_BitwiseOr" => "|", "op_ExclusiveOr" => "^",
                "op_LeftShift" => "<<", "op_RightShift" => ">>",
                _ => null,
            };
            return op is null ? null : $"{Operand(arguments[0])} {op} {Operand(arguments[1])}";
        }
        if (arguments.Count == 1)
        {
            return call.Callee.Name switch
            {
                "op_UnaryNegation" => $"-{Operand(arguments[0])}",
                "op_UnaryPlus" => $"+{Operand(arguments[0])}",
                "op_LogicalNot" => $"!{Operand(arguments[0])}",
                "op_OnesComplement" => $"~{Operand(arguments[0])}",
                "op_Implicit" or "op_Explicit" => $"({TypeText(call.Callee.ReturnType)}){Operand(arguments[0])}",
                _ => null,
            };
        }
        return null;
    }

    string FieldTarget(FieldRef field, IrExpression? instance)
    {
        // An auto-property backing field, <Prop>k__BackingField, has no spellable
        // C# name; render it as the property it backs. `this.` qualifies the
        // instance form so a constructor assignment whose parameter shadows the
        // property still binds to it (and is legal even for a get-only property).
        if (BackingFieldProperty(field.Name) is { } property)
            return instance switch
            {
                null => $"{TypeText(field.DeclaringType)}.{property}",
                LoadArgument { Index: 0, Name: "this" } => $"this.{property}",
                _ => $"{ReceiverText(instance)}.{property}",
            };
        return instance switch
        {
            null => $"{TypeText(field.DeclaringType)}.{field.Name}",
            // A parameter or local with the same name shadows the field, so the
            // bare name binds to it, not the field (e.g. int Foo(int _x) =>
            // this._x + _x). Qualify with this. to reach the field; an
            // unshadowed instance field stays bare per the taste convention.
            LoadArgument { Index: 0, Name: "this" } => IsShadowedByLocal(field.Name) ? $"this.{field.Name}" : field.Name,
            _ => $"{ReceiverText(instance)}.{field.Name}",
        };
    }

    HashSet<string>? _localScopeNames;

    string[]? _localDisplayNames;

    /// <summary>
    /// The display name for local slot <paramref name="index"/>: the PDB source
    /// name when present, usable as a C# identifier, and not already taken by a
    /// parameter or an earlier-named local; otherwise the synthetic
    /// <c>V_index</c>. Resolved once per function so every reference to a slot —
    /// declaration, load, address, shadow test — spells it identically.
    /// </summary>
    string LocalName(int index)
    {
        if (_localDisplayNames is null)
        {
            int count = _function.Locals.Length;
            var display = new string[count];
            for (int i = 0; i < count; i++)
                display[i] = $"V_{i}";

            var names = _function.LocalNames;
            if (!names.IsDefaultOrEmpty)
            {
                var taken = new HashSet<string>(StringComparer.Ordinal);
                foreach (var parameter in _function.Signature.Parameters)
                    taken.Add(parameter.Name);
                for (int i = 0; i < count && i < names.Length; i++)
                {
                    if (names[i] is { } name && IsUsableIdentifier(name) && taken.Add(name))
                        display[i] = name;
                }
            }
            _localDisplayNames = display;
        }
        return index >= 0 && index < _localDisplayNames.Length ? _localDisplayNames[index] : $"V_{index}";
    }

    /// <summary>A name safe to emit bare as a C# identifier: letters/digits/underscore, no leading digit, and not a reserved keyword (which would need an <c>@</c> escape).</summary>
    static bool IsUsableIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_'))
            return false;
        foreach (char c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return false;
        }
        return !ReservedKeywords.Contains(name);
    }

    static readonly HashSet<string> ReservedKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    /// <summary>
    /// True when an instance-method parameter or local would shadow a field of
    /// this name, so a bare reference binds to the local rather than the field.
    /// Locals print with their resolved display name; parameters carry their
    /// metadata names.
    /// </summary>
    bool IsShadowedByLocal(string fieldName)
    {
        if (_localScopeNames is null)
        {
            _localScopeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in _function.Signature.Parameters)
                _localScopeNames.Add(parameter.Name);
            for (int i = 0; i < _function.Locals.Length; i++)
                _localScopeNames.Add(LocalName(i));
        }
        return _localScopeNames.Contains(fieldName);
    }

    /// <summary>The property name an auto-property backing field <c>&lt;Prop&gt;k__BackingField</c> backs, or null for an ordinary field.</summary>
    static string? BackingFieldProperty(string fieldName)
    {
        const string suffix = ">k__BackingField";
        return fieldName.Length > suffix.Length + 1 && fieldName[0] == '<' && fieldName.EndsWith(suffix, StringComparison.Ordinal)
            ? fieldName[1..^suffix.Length]
            : null;
    }

    string PropertyTarget(MethodRef accessor, IrExpression? instance, IReadOnlyList<IrExpression> indexArguments, string name, bool isVirtual = true)
    {
        string receiver = instance switch
        {
            // A NON-virtual this-receiver access to a base-declared member is
            // C#'s base. — the call opcode deliberately skips dispatch.
            LoadArgument { Index: 0, Name: "this" } when !isVirtual && IsCrossType(accessor.DeclaringType) => "base",
            null => TypeText(accessor.DeclaringType),
            LoadArgument { Index: 0, Name: "this" } => "",
            _ => ReceiverText(instance),
        };
        // An instance property accessor with index arguments IS an indexer,
        // whatever its metadata name (String's is Chars, not Item).
        if (instance is not null && indexArguments.Count > 0)
            return $"{(receiver.Length == 0 ? "this" : receiver)}[{Arguments(indexArguments)}]";
        string dotted = receiver.Length == 0 ? name : $"{receiver}.{name}";
        return indexArguments.Count == 0 ? dotted : $"{dotted}[{Arguments(indexArguments)}]";
    }

    /// <summary>True when the member's declaring DEFINITION differs from the function's — self-calls in generic types arrive as instantiations (List&lt;!0&gt;) and must not count as cross-type.</summary>
    bool IsCrossType(TypeRef memberDeclaringType)
    {
        static TypeRef Definition(TypeRef type)
            => type is { Kind: TypeRefKind.GenericInstance, ElementType: { } definition } ? definition : type;
        return !Equals(Definition(memberDeclaringType), Definition(_function.DeclaringType));
    }

    /// <summary>Member-access receivers: value-type receivers arrive by address in IL; C# spells the place itself, not its address.</summary>
    string ReceiverText(IrExpression receiver) => receiver switch
    {
        LoadLocalAddress a => $"{LocalName(a.Index)}",
        LoadArgumentAddress a => a.Name,
        LoadFieldAddress f => FieldTarget(f.Field, f.Instance),
        _ => Operand(receiver),
    };

    /// <summary>
    /// A method group for a delegate creation: a null target is a static
    /// method group (Type.Method); a this-receiver drops the qualifier to match
    /// instance-call spelling; any other receiver qualifies the name.
    /// </summary>
    string MethodGroupText(MethodRef method, IrExpression target)
    {
        string name = MethodName(method.Name);
        if (target is Constant { Value: null })
            return $"{TypeText(method.DeclaringType)}.{name}";
        if (target is LoadArgument { Index: 0, Name: "this" })
            return name;
        return $"{ReceiverText(target)}.{name}";
    }

    /// <summary>
    /// <c>&amp;Method</c> for a static method group. A same-type target — every
    /// local function, and members of the function's own type — needs no
    /// qualifier; a cross-type static method is qualified by its declaring type.
    /// Generic methods carry their type arguments (<c>&amp;Method&lt;int&gt;</c>).
    /// </summary>
    string AddressOfMethodText(AddressOfMethod node)
    {
        var method = node.Method;
        string typeArguments = method.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", method.TypeArguments.Select(TypeText))}>";
        string name = $"{MethodName(method.Name)}{typeArguments}";
        return IsCrossType(method.DeclaringType)
            ? $"&{TypeText(method.DeclaringType)}.{name}"
            : $"&{name}";
    }

    /// <summary>
    /// The source name of a call target. A compiler-generated local function
    /// carries the metadata name <c>&lt;Enclosing&gt;g__Local|N_M</c>, which is
    /// not a valid C# identifier; the source name is the segment between
    /// <c>g__</c> and the <c>|</c> ordinal suffix.
    /// </summary>
    static string MethodName(string name)
    {
        if (!name.StartsWith('<'))
            return name;
        int marker = name.IndexOf("g__", StringComparison.Ordinal);
        if (marker < 0)
            return name;
        int start = marker + 3;
        int bar = name.IndexOf('|', start);
        return bar > start ? name[start..bar] : name[start..];
    }

    string CallText(Call call)
    {
        var arguments = call.Arguments;
        string typeArguments = call.Callee.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", call.Callee.TypeArguments.Select(TypeText))}>";
        if (!call.Callee.HasThis)
        {
            // C# compiles user-defined operators TO these calls; the
            // operator spelling is the faithful inverse.
            if (IsOperatorCall(call))
                return OperatorSpelling(call)!;
            return $"{TypeText(call.Callee.DeclaringType)}.{MethodName(call.Callee.Name)}{typeArguments}({Arguments(arguments, call.Callee.ParameterTypes, call.Callee.ParameterRefKinds)})";
        }
        var receiver = arguments[0];
        string rest = Arguments(arguments.Skip(1), call.Callee.ParameterTypes, call.Callee.ParameterRefKinds);
        if (call.Callee.Name == ".ctor" && receiver is LoadArgument { Index: 0, Name: "this" })
        {
            // A this-receiver constructor call is C#'s base(...)/this(...).
            string keyword = Equals(call.Callee.DeclaringType, _function.DeclaringType) ? "this" : "base";
            return $"{keyword}({rest})";
        }
        if (receiver is LoadArgument { Index: 0, Name: "this" })
        {
            // Non-virtual this-receiver call to a base-declared method is
            // C#'s base.M() — the call opcode deliberately skips dispatch.
            return !call.IsVirtual && IsCrossType(call.Callee.DeclaringType)
                ? $"base.{MethodName(call.Callee.Name)}{typeArguments}({rest})"
                : $"{MethodName(call.Callee.Name)}{typeArguments}({rest})";
        }
        return $"{ReceiverText(receiver)}.{MethodName(call.Callee.Name)}{typeArguments}({rest})";
    }

    string Arguments(IEnumerable<IrExpression> arguments)
        => string.Join(", ", arguments.Select(Expression));

    /// <summary>
    /// Arguments paired positionally with the callee's parameter types, casting
    /// each to its parameter type where C# needs it (CS0266) — the call-site
    /// counterpart of the return/store boundary casts. Callers pass arguments
    /// that already align 1:1 with the parameters (the receiver of an instance
    /// call is dropped first), so index i maps to parameterTypes[i].
    /// </summary>
    string Arguments(IEnumerable<IrExpression> arguments, IReadOnlyList<TypeRef> parameterTypes, ImmutableArray<ArgumentRefKind> refKinds)
    {
        var parts = new List<string>();
        int i = 0;
        foreach (var argument in arguments)
        {
            var parameter = i < parameterTypes.Count ? parameterTypes[i] : null;
            var refKind = i < refKinds.Length ? refKinds[i] : ArgumentRefKind.Value;
            parts.Add(RefArgument(argument, parameter, refKind)
                ?? (parameter is not null ? CastValue(argument, parameter) : Expression(argument)));
            i++;
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Spells a by-ref argument with the keyword its parameter demands:
    /// <c>out</c>, <c>in</c> (no keyword — the readonly ref is implicit), or
    /// <c>ref</c>. A managed pointer forwarded to a <c>ref</c>/<c>out</c>
    /// parameter needs the keyword at the call site (CS1620); spelling it on an
    /// <c>in</c> parameter is the inverse error (CS1615), so the address-of
    /// node's own <c>ref</c> is dropped there. Null when the kind is unknown (a
    /// cross-assembly MemberRef carries no parameter rows) or the argument is not
    /// a simple place — both leave the existing spelling untouched.
    /// </summary>
    string? RefArgument(IrExpression argument, TypeRef? parameter, ArgumentRefKind refKind)
    {
        if (parameter is not { Kind: TypeRefKind.ByRef } || refKind == ArgumentRefKind.Value)
            return null;
        // `in` accepts a value argument (the compiler introduces a temporary), so
        // any place- or value-spelling works and the keyword stays implicit.
        if (refKind == ArgumentRefKind.In)
            return ArgumentPlace(argument);
        // `out`/`ref` require a genuine assignable lvalue; a cast (unbox) is not
        // one (`out (T)x` is CS0206), so leave those to the default spelling.
        if (ArgumentLvalue(argument) is not { } place)
            return null;
        return refKind == ArgumentRefKind.Out ? $"out {place}" : $"ref {place}";
    }

    /// <summary>
    /// The bare place of a by-ref argument — without any <c>ref</c> the keyword
    /// renderer adds itself. Address-of nodes read back as their place; a by-ref
    /// value (ref local/parameter, ref-returning call) already renders as a bare
    /// place. Null for forms that are not a single place (a ref ternary binds
    /// <c>ref</c> per arm), leaving them to the default spelling.
    /// </summary>
    string? ArgumentPlace(IrExpression argument) => argument switch
    {
        LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or LoadElementAddress => Deref(argument),
        Unbox u => $"({TypeText(u.Type)}){Operand(u.Operand)}",
        LoadLocal or LoadArgument or LoadIndirect or Call or CallIndirect => Expression(argument),
        _ => null,
    };

    /// <summary>
    /// The subset of <see cref="ArgumentPlace"/> that is a genuine assignable
    /// lvalue — what <c>out</c>/<c>ref</c> demand. Excludes the <see cref="Unbox"/>
    /// cast form (an lvalue only `in` can accept, as a value).
    /// </summary>
    string? ArgumentLvalue(IrExpression argument) => argument switch
    {
        LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or LoadElementAddress => Deref(argument),
        LoadLocal or LoadArgument or LoadIndirect or Call or CallIndirect => Expression(argument),
        _ => null,
    };

    /// <summary>
    /// <c>target?.Member</c>: the member's receiver child is the target, and the
    /// member's name/arguments form the suffix after <c>?</c>. Mirrors the
    /// instance spellings of <see cref="CallText"/>, <see cref="PropertyTarget"/>,
    /// and <see cref="FieldTarget"/>, minus their receiver — the <c>?.</c> owns it.
    /// </summary>
    string NullConditionalText(NullConditional node)
    {
        var member = node.Member;
        var receiver = NullConditionalReceiver(member);
        return $"{ReceiverText(receiver)}?{NullConditionalSuffix(member)}";
    }

    static IrExpression NullConditionalReceiver(IrExpression member) => member switch
    {
        Call call => call.Arguments[0],
        LoadProperty property => property.Instance!,
        LoadField field => field.Instance!,
        _ => member,
    };

    string NullConditionalSuffix(IrExpression member) => member switch
    {
        LoadField field => $".{field.Field.Name}",
        LoadProperty property when property.IndexArguments.Count > 0 => $"[{Arguments(property.IndexArguments)}]",
        LoadProperty property => $".{property.PropertyName}",
        Call call => NullConditionalCallSuffix(call),
        _ => $".{member.Describe()}",
    };

    string NullConditionalCallSuffix(Call call)
    {
        string typeArguments = call.Callee.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", call.Callee.TypeArguments.Select(TypeText))}>";
        return $".{MethodName(call.Callee.Name)}{typeArguments}({Arguments(call.Arguments.Skip(1), call.Callee.ParameterTypes, call.Callee.ParameterRefKinds)})";
    }

    string ConvertText(Convert convert)
    {
        // Converting an out-of-range integer constant (conv.u8 of ldc.i4.m1 for
        // ulong.MaxValue) is CS0221 as a plain cast; reinterpret its bits with
        // unchecked, matching the constant handling at value boundaries.
        if (!convert.IsChecked && convert.Operand is Constant { Value: int or long } c
            && TypeFamilies.IsNumericPrimitive(convert.Target))
        {
            long literal = c.Value is int i ? i : (long)c.Value!;
            if (!TypeFamilies.ConstantFits(literal, convert.Target))
                return $"unchecked(({TypeText(convert.Target)})({Expression(convert.Operand)}))";
        }
        // conv.r.un and conv.ovf.*.un interpret the SOURCE as unsigned —
        // a signed operand needs its unsigned cast or the value is wrong.
        string operand = convert.IsUnsigned ? UnsignedOperand(convert.Operand) : Operand(convert.Operand);
        string cast = $"({TypeText(convert.Target)}){operand}";
        return convert.IsChecked ? $"checked({cast})" : cast;
    }

    /// <summary>
    /// A retyped enum constant renders <c>EnumType.Member</c> when its value
    /// names exactly one member of the resolved (same-assembly) enum. Composite
    /// flag values and unnamed casts have no exact member and fall through to
    /// the raw integer — naming those is a later slice.
    /// </summary>
    string? EnumMemberName(Constant constant)
        => constant.Value is int value
            && _function.EnumMembers.TryGetValue(constant.Type, out var members)
            && members.TryGetValue(value, out var name)
            ? $"{TypeText(constant.Type)}.{name}"
            : null;

    static string ConstantText(Constant constant) => constant.Value switch
    {
        null => "null",
        string s => StringText(s),
        bool b => b ? "true" : "false",
        char c => CharText(c),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        float f => $"{f.ToString("R", CultureInfo.InvariantCulture)}f",
        double d => $"{d.ToString("R", CultureInfo.InvariantCulture)}d",
        _ => constant.Value.ToString() ?? "?",
    };

    static string CharText(char c) => $"'{EscapeChar(c, inString: false)}'";

    /// <summary>A C# string literal with every char that needs escaping escaped — control chars, quotes, backslashes — so the output always compiles.</summary>
    static string StringText(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (char c in value)
            sb.Append(EscapeChar(c, inString: true));
        return sb.Append('"').ToString();
    }

    /// <summary>
    /// The single home for C# character escaping, shared by char and string
    /// literals. The active delimiter is escaped (<c>"</c> in a string,
    /// <c>'</c> in a char); every control character gets a recognized escape
    /// or a <c>\u</c> sequence so a raw newline or tab never reaches the output.
    /// </summary>
    static string EscapeChar(char c, bool inString) => c switch
    {
        '\\' => "\\\\",
        '"' when inString => "\\\"",
        '\'' when !inString => "\\'",
        '\0' => "\\0",
        '\a' => "\\a",
        '\b' => "\\b",
        '\f' => "\\f",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\v' => "\\v",
        _ when char.IsControl(c) => $"\\u{(int)c:x4}",
        _ => c.ToString(),
    };

    static string BinaryOperator(Binary binary) => binary.Kind switch
    {
        BinaryKind.Add => "+",
        BinaryKind.Subtract => "-",
        BinaryKind.Multiply => "*",
        BinaryKind.Divide => "/",
        BinaryKind.Remainder => "%",
        BinaryKind.And => "&",
        BinaryKind.Or => "|",
        BinaryKind.Xor => "^",
        BinaryKind.ShiftLeft => "<<",
        _ => ">>",
    };

    static string ComparisonOperator(ComparisonKind kind) => kind switch
    {
        ComparisonKind.Equal => "==",
        ComparisonKind.NotEqual => "!=",
        ComparisonKind.LessThan => "<",
        ComparisonKind.LessThanOrEqual => "<=",
        ComparisonKind.GreaterThan => ">",
        _ => ">=",
    };

    static string TypeText(TypeRef type)
    {
        string text = type.ToDisplayString();
        int tick = text.IndexOf('`');
        return tick < 0 ? text : text[..tick];
    }
}
