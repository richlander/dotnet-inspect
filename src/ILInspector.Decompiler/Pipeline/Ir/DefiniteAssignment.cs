using ILInspector.ControlFlow;
namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The definite-assignment analysis behind the printer's <c>= default</c>
/// elision: the locals that may be read before they are definitely assigned
/// keep their zero-initializer (a bare declaration would be CS0165), and the
/// rest declare bare because their initializer is a dead store the IL never
/// carried (locals lean on <c>.locals init</c>). This is the same definite
/// assignment the C# compiler itself runs, lifted out of <see cref="CSharpPrinter"/>
/// into its own unit: the printer consumes the fact (a read-before-assign set)
/// rather than computing it inline. It is fact-producing, not tree-rewriting, so
/// it is an analyzer the printer calls — not an <see cref="IIrPass"/>, whose
/// contract is to rewrite the tree and never side-channel state.
///
/// A conservative structured walk: it gives up — marking every local
/// read-before-assign — on any control flow it does not fully model
/// (gotos/labels it cannot read in program order are handed to the CFG fixpoint
/// in <c>CfgContainer</c>; an EH leave or a branch leaving a container takes the
/// global bail), so a bare declaration is emitted only when definite assignment
/// is proven. The walk under-claims assignment everywhere it is unsure, which
/// can only keep a redundant <c>= default</c>, never drop a required one. An
/// optional <see cref="DataflowFacts"/> sink records the CFG in/out sets for the
/// shipped analysis to be read directly.
/// </summary>
static class DefiniteAssignment
{
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
    public static HashSet<int> Compute(IrFunction function, IReadOnlySet<int> labelTargets, DataflowFacts? facts)
    {
        var readEarly = new HashSet<int>();
        bool bailed = false;

        void BailAll()
        {
            bailed = true;
            if (facts is not null)
                facts.Bailed = true;
            for (int i = 0; i < function.Locals.Length; i++)
                readEarly.Add(i);
        }

        void CheckReads(IrNode? node, HashSet<int> assigned)
        {
            if (node is null)
                return;
            switch (node)
            {
                case Lambda or LocalFunctionStatement:
                    return;
                case LoadLocal l:
                    if (!assigned.Contains(l.Index))
                        readEarly.Add(l.Index);
                    return;
                case LoadLocalAddress la:
                    if (!assigned.Contains(la.Index))
                        readEarly.Add(la.Index);
                    return;
                case Conditional conditional:
                {
                    CheckReads(conditional.Condition, assigned);
                    var trueSet = new HashSet<int>(assigned);
                    CheckReads(conditional.WhenTrue, trueSet);
                    var falseSet = new HashSet<int>(assigned);
                    CheckReads(conditional.WhenFalse, falseSet);
                    trueSet.IntersectWith(falseSet);
                    assigned.Clear();
                    assigned.UnionWith(trueSet);
                    return;
                }
                case LogicalBinary logical:
                {
                    CheckReads(logical.Left, assigned);
                    var rightSet = new HashSet<int>(assigned);
                    CheckReads(logical.Right, rightSet);
                    return;
                }
                case Coalesce coalesce:
                {
                    CheckReads(coalesce.Left, assigned);
                    var rightSet = new HashSet<int>(assigned);
                    CheckReads(coalesce.Right, rightSet);
                    return;
                }
                case SwitchExpression switchExpression:
                {
                    CheckReads(switchExpression.Value, assigned);
                    foreach (var arm in switchExpression.Arms)
                    {
                        var armSet = new HashSet<int>(assigned);
                        CheckReads(arm.Value, armSet);
                    }
                    return;
                }
                case UnionSwitchExpression unionSwitch:
                {
                    CheckReads(unionSwitch.Value, assigned);
                    foreach (var arm in unionSwitch.Arms)
                    {
                        var armSet = new HashSet<int>(assigned);
                        if (arm.LocalIndex is { } local)
                            armSet.Add(local);
                        CheckReads(arm.Guard, armSet);
                        CheckReads(arm.Value, armSet);
                    }
                    if (unionSwitch.NullValue is { } nullValue)
                    {
                        var nullSet = new HashSet<int>(assigned);
                        CheckReads(nullValue, nullSet);
                    }
                    if (unionSwitch.DefaultValue is { } defaultValue)
                    {
                        var defaultSet = new HashSet<int>(assigned);
                        CheckReads(defaultValue, defaultSet);
                    }
                    return;
                }
                case NullConditional nullConditional:
                {
                    var memberSet = new HashSet<int>(assigned);
                    CheckReads(nullConditional.Member, memberSet);
                    return;
                }
                case Call call:
                    CheckCall(call.Callee, call.Arguments, call.Callee.HasThis ? 1 : 0, assigned);
                    return;
                case NewObject newObject:
                    CheckCall(newObject.Constructor, newObject.Arguments, 0, assigned);
                    return;
            }

            foreach (var child in node.Children)
                CheckReads(child, assigned);
        }

        void CheckCall(MethodRef callee, IReadOnlyList<IrExpression> arguments, int parameterStart, HashSet<int> assigned)
        {
            if (parameterStart == 1 && arguments.Count > 0)
                CheckReads(arguments[0], assigned);

            var outAssigned = new List<int>();
            for (int argumentIndex = parameterStart; argumentIndex < arguments.Count; argumentIndex++)
            {
                var argument = arguments[argumentIndex];
                int parameterIndex = argumentIndex - parameterStart;
                if (IsVerifiedOutLocal(callee, parameterIndex, argument, out int local))
                {
                    outAssigned.Add(local);
                    continue;
                }

                CheckReads(argument, assigned);
            }

            foreach (int local in outAssigned)
                assigned.Add(local);
        }

        static bool IsVerifiedOutLocal(MethodRef callee, int parameterIndex, IrExpression argument, out int local)
        {
            local = -1;
            if (callee.ParameterRefKindsFacts != ParameterRefKindFacts.Known
                || parameterIndex < 0
                || parameterIndex >= callee.ParameterRefKinds.Length
                || callee.ParameterRefKinds[parameterIndex] != ArgumentRefKind.Out
                || argument is not LoadLocalAddress address)
            {
                return false;
            }

            local = address.Index;
            return true;
        }

        static void AddVerifiedOutLocals(IrExpression expression, HashSet<int> assigned)
        {
            switch (expression)
            {
                case Call call:
                    if (call.Callee.HasThis && call.Arguments.Count > 0)
                        AddVerifiedOutLocals(call.Arguments[0], assigned);
                    foreach (var argument in call.Arguments.Skip(call.Callee.HasThis ? 1 : 0))
                        AddVerifiedOutLocals(argument, assigned);
                    AddCallOutLocals(call.Callee, call.Arguments, call.Callee.HasThis ? 1 : 0, assigned);
                    return;
                case NewObject newObject:
                    foreach (var argument in newObject.Arguments)
                        AddVerifiedOutLocals(argument, assigned);
                    AddCallOutLocals(newObject.Constructor, newObject.Arguments, 0, assigned);
                    return;
                case Conditional conditional:
                    AddVerifiedOutLocals(conditional.Condition, assigned);
                    return;
                case LogicalBinary logical:
                    AddVerifiedOutLocals(logical.Left, assigned);
                    return;
                case Coalesce coalesce:
                    AddVerifiedOutLocals(coalesce.Left, assigned);
                    return;
                case SwitchExpression switchExpression:
                    AddVerifiedOutLocals(switchExpression.Value, assigned);
                    return;
                case UnionSwitchExpression unionSwitch:
                    AddVerifiedOutLocals(unionSwitch.Value, assigned);
                    return;
                case NullConditional:
                    return;
            }

            foreach (var child in expression.Children.OfType<IrExpression>())
                AddVerifiedOutLocals(child, assigned);
        }

        static void AddCallOutLocals(MethodRef callee, IReadOnlyList<IrExpression> arguments, int parameterStart, HashSet<int> assigned)
        {
            for (int argumentIndex = parameterStart; argumentIndex < arguments.Count; argumentIndex++)
            {
                int parameterIndex = argumentIndex - parameterStart;
                if (IsVerifiedOutLocal(callee, parameterIndex, arguments[argumentIndex], out int local))
                    assigned.Add(local);
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
                labelTargets.Contains(b.StartOffset)
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
            // gen[i]: locals block i assigns on every path through it (order
            // within the block does not matter for what holds at its exit).
            var transfers = new GenKillSet[n];
            for (int i = 0; i < n; i++)
            {
                var gen = new HashSet<int>();
                foreach (var child in blocks[i].Children)
                {
                    if (child is StoreLocal store)
                        gen.Add(store.Index);
                    else if (child is InitObject { Address: LoadLocalAddress address })
                        gen.Add(address.Index);
                    else if (child is ConditionalBranch conditional)
                        AddVerifiedOutLocals(conditional.Condition, gen);
                    else if (child is IrExpression expression)
                        AddVerifiedOutLocals(expression, gen);
                }
                transfers[i] = new GenKillSet(gen, new HashSet<int>());
            }

            // in[0] is the fixed entry assignment set: the external edge always
            // supplies it, and a local assigned before the container stays
            // assigned across any back edge. Other blocks start at the universe
            // for the must/intersection fixpoint and narrow down.
            var flow = ForwardDataflow.Solve(
                edges,
                transfers,
                entryAssigned,
                new HashSet<int>(Enumerable.Range(0, function.Locals.Length)),
                DataflowMerge.Intersection);

            // With the assignment-on-entry known per block, check reads in
            // program order within each block.
            if (facts is not null)
            {
                var blockFacts = new List<DataflowFacts.BlockFacts>(n);
                for (int i = 0; i < n; i++)
                {
                    var state = flow.Blocks[i];
                    bool legacyReachable = i == 0 || state.Predecessors.Count > 0;
                    blockFacts.Add(new DataflowFacts.BlockFacts(
                        blocks[i].StartOffset,
                        [.. state.Predecessors.Select(p => blocks[p].StartOffset).Order()],
                        [.. edges[i].Successors.Select(s => blocks[s].StartOffset).Order()],
                        [.. transfers[i].Gen.Order()],
                        legacyReachable ? [.. state.In.Order()] : [],
                        legacyReachable ? [.. state.Out.Order()] : [],
                        legacyReachable));
                }
                facts.AddContainer(new DataflowFacts.ContainerFacts(blockFacts));
            }

            for (int k = 0; k < n; k++)
            {
                var running = new HashSet<int>(flow.Blocks[k].In);
                foreach (var child in blocks[k].Children)
                {
                    switch (child)
                    {
                        case StoreLocal store:
                            CheckReads(store.Value, running);
                            running.Add(store.Index);
                            break;
                        case NullCoalescingAssignment assignment:
                            CheckReads(new LoadLocal(assignment.LocalIndex, assignment.LocalType), running);
                            var cfgCoalesceSet = new HashSet<int>(running);
                            CheckReads(assignment.Value, cfgCoalesceSet);
                            running.Add(assignment.LocalIndex);
                            break;
                        case NullCoalescingFieldAssignment assignment:
                            CheckReads(assignment.Instance, running);
                            var cfgFieldCoalesceSet = new HashSet<int>(running);
                            CheckReads(assignment.Value, cfgFieldCoalesceSet);
                            break;
                        case DeconstructionAssignment deconstruction:
                            CheckReads(deconstruction.Source, running);
                            foreach (var target in deconstruction.Targets)
                            {
                                CheckReads(target, running);
                                if (target.Kind == DeconstructionTargetKind.Local)
                                    running.Add(target.LocalIndex);
                            }
                            break;
                        case ForeachStatement foreachStatement:
                            CheckReads(foreachStatement.Collection, running);
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
                    if (labelTargets.Contains(block.StartOffset))
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
                case NullCoalescingAssignment assignment:
                    CheckReads(new LoadLocal(assignment.LocalIndex, assignment.LocalType), assigned);
                    var coalesceSet = new HashSet<int>(assigned);
                    CheckReads(assignment.Value, coalesceSet);
                    assigned.Add(assignment.LocalIndex);
                    return DefiniteFlow.FallThrough;
                case NullCoalescingFieldAssignment assignment:
                    CheckReads(assignment.Instance, assigned);
                    var fieldCoalesceSet = new HashSet<int>(assigned);
                    CheckReads(assignment.Value, fieldCoalesceSet);
                    return DefiniteFlow.FallThrough;
                case DeconstructionAssignment deconstruction:
                    CheckReads(deconstruction.Source, assigned);
                    foreach (var target in deconstruction.Targets)
                    {
                        CheckReads(target, assigned);
                        if (target.Kind == DeconstructionTargetKind.Local)
                            assigned.Add(target.LocalIndex);
                    }
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
                case ForeachStatement foreachStatement:
                    CheckReads(foreachStatement.Collection, assigned);
                    var foreachSet = new HashSet<int>(assigned) { foreachStatement.LocalIndex };
                    if (Statement(foreachStatement.Body, foreachSet) == DefiniteFlow.Bail)
                        return DefiniteFlow.Bail;
                    return DefiniteFlow.FallThrough;
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
                case UsingStatement usingNode:
                    CheckReads(usingNode.Resource, assigned);
                    assigned.Add(usingNode.LocalIndex);
                    return Container(usingNode.Body, assigned);
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
                CheckReads(clause.Filter, catchSet);
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

        if (facts is not null)
            facts.ReadBeforeAssign = [.. readEarly.Order()];

        return readEarly;
    }
}
