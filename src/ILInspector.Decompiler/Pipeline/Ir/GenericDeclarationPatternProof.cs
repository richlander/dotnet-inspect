using ILInspector.ControlFlow;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The shared proof that a csc generic declaration-pattern extraction
/// (<c>UnboxAny T(IsInstance T(x))</c>) sits on a path where the identical type
/// test has already succeeded — the dominance/aliasing boundary established by
/// #2831/#2856. Both the printer (which bridges the extraction through
/// <c>(object)</c>) and <see cref="IsPatternPass"/> (which raises the shape to a
/// <c>value is T t</c> binding) share this one rule so a widened or narrowed
/// proof cannot drift between the two contexts (the partial-sibling rule).
///
/// <para>The proof is narrow by design: the guard's test must be the same type
/// test (same target type, structurally identical operand) via
/// <see cref="SameTestedValue"/>, the site must be reachable only through the
/// guard's successful edge (never its failed arm), no loop may lie between the
/// guard and the site for the structured proof, no store between the guard and
/// the site may reassign a local the tested operand reads, and no managed
/// address of the tested value may exist. A nested lambda/local-function
/// boundary ends the structured search.</para>
/// </summary>
internal static class GenericDeclarationPatternProof
{
    /// <summary>
    /// True when <paramref name="site"/> (an <see cref="UnboxAny"/> whose operand
    /// is <paramref name="test"/>) sits inside the guaranteed-true arm of an
    /// <see cref="IfStatement"/> that already proved the identical test, or — when
    /// <see cref="StructuringPass"/> left the region flat — behind an equivalent
    /// <see cref="ConditionalBranch"/> guard reached only on the successful path
    /// (see <see cref="IsProvenByFlatGuard"/>).
    /// </summary>
    public static bool IsProvenSuccessfulTypeTest(IrNode site, IsInstance test)
    {
        if (!IsReadOnlyOperand(test.Operand))
            return false;
        if (HasAddressTaken(site, test.Operand))
            return false;

        for (var current = site.Parent; current is not null; current = current.Parent)
        {
            if (current is Lambda or LocalFunctionStatement)
                break;
            if (current is WhileLoop or DoWhileLoop or ForLoop or ForeachStatement)
                break;
            if (current is not IfStatement ifStatement)
                continue;
            if (ifStatement.Condition is not IsInstance guard
                || !guard.Type.Equals(test.Type)
                || !SameTestedValue(guard.Operand, test.Operand))
            {
                continue;
            }
            if (!ReferenceOwnership.IsInside(site, ifStatement.Then))
                return false;

            return !HasInterveningWriteAlongPath(ifStatement.Then, site, test.Operand);
        }
        return IsProvenByFlatGuard(site, test);
    }

    /// <summary>
    /// Whether <paramref name="site"/> is proven successful specifically by
    /// <paramref name="guard"/> — not merely by some nested same-value guard that
    /// tests a re-read of the value past a write. Raising binds the pattern local
    /// at <paramref name="guard"/>, so every rewritten extraction must read the
    /// value the guard tested: the site must lie in the guard's <c>Then</c> arm,
    /// with no loop or nested-function boundary and no reassignment of the tested
    /// value between the guard and the site. A site that is only valid because an
    /// inner guard re-tested a mutated value must not be rebound to this guard's
    /// binding.
    /// </summary>
    public static bool IsProvenBySpecificGuard(IfStatement guard, IrNode site, IsInstance test)
    {
        if (!IsReadOnlyOperand(test.Operand) || HasAddressTaken(site, test.Operand))
            return false;

        for (IrNode current = site; current.Parent is { } parent; current = parent)
        {
            // A loop could re-execute the site against a value mutated on the
            // backedge; a nested function changes value semantics (capture). Either
            // between the guard and the site breaks the "same tested value" identity.
            if (parent is WhileLoop or DoWhileLoop or ForLoop or ForeachStatement
                or Lambda or LocalFunctionStatement)
            {
                return false;
            }
            if (HasInterveningWrite(parent, current.ChildIndex, test.Operand))
                return false;
            if (ReferenceEquals(parent, guard.Then))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The flat-CFG counterpart of the structured proof above: real-world
    /// declaration-pattern extractions frequently sit in a ternary-shaped merge
    /// region <see cref="StructuringPass"/> left as raw blocks and a
    /// <see cref="ConditionalBranch"/> rather than an <see cref="IfStatement"/>
    /// (e.g. <c>T x = cond ? isinst-extract : default;</c>). The proof mirrors the
    /// structured one over the block graph instead of the statement tree: the
    /// site's enclosing <see cref="Block"/> must be reached, via <see cref="Cfg"/>
    /// edges, from exactly one predecessor — a block whose terminating
    /// <see cref="ConditionalBranch"/> tests the identical value and only reaches
    /// the site's block on the branch's successful arm (fall-through past a
    /// negated test, or the jump target of an unnegated one). An unmodeled edge
    /// (an EH leave, or a branch target outside the container) fails the proof
    /// rather than being reasoned about.
    /// </summary>
    public static bool IsProvenByFlatGuard(IrNode site, IsInstance test)
    {
        IrNode? current = site.Parent;
        while (current is not null && current is not Block)
            current = current.Parent;
        if (current is not Block siteBlock || siteBlock.Parent is not BlockContainer container)
            return false;

        var guardedStatement = TopLevelStatementWithin(site, siteBlock);
        if (guardedStatement is null
            || HasInterveningWrite(siteBlock, guardedStatement.ChildIndex, test.Operand))
        {
            return false;
        }

        var blocks = container.Blocks;
        int siteIndex = -1;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], siteBlock))
            {
                siteIndex = i;
                break;
            }
        }
        // The container's entry block also has an implicit predecessor from
        // outside the region. Cfg models only intra-container edges, so it
        // cannot prove that a backedge into block zero dominates the first
        // execution of that block.
        if (siteIndex <= 0)
            return false;

        var edges = Cfg.Build(blocks);
        if (edges.Any(e => e.LeavesRegion || e.ExternalTargets.Count > 0))
            return false;

        for (int i = 0; i < blocks.Count; i++)
        {
            if (i == siteIndex || blocks[i].Children.Count == 0)
                continue;
            if (blocks[i].Children[^1] is not ConditionalBranch branch)
                continue;

            bool reachesSiteOnSuccess = branch.Condition switch
            {
                // `if (x is not T) goto Fail;` — fall-through (i+1) is the true path.
                // The failure jump must land somewhere else: a target that (degenerately)
                // coincides with the fall-through would mean the guard reaches the site
                // on failure too, so the test would not actually gate the extraction.
                LogicalNot { Operand: IsInstance negatedGuard }
                    when negatedGuard.Type.Equals(test.Type) && SameTestedValue(negatedGuard.Operand, test.Operand)
                    => i + 1 == siteIndex && branch.TargetOffset != siteBlock.StartOffset,
                // `if (x is T) goto Success;` — the jump target is the true path. The
                // fall-through must land somewhere else for the same reason: a
                // fall-through that (degenerately) coincides with the jump target
                // would mean the guard reaches the site on failure too.
                IsInstance guard
                    when guard.Type.Equals(test.Type) && SameTestedValue(guard.Operand, test.Operand)
                    => branch.TargetOffset == siteBlock.StartOffset && i + 1 != siteIndex,
                _ => false,
            };
            if (!reachesSiteOnSuccess)
                continue;

            if (HasSinglePredecessor(edges, siteIndex, i))
                return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="targetIndex"/> is reached from exactly one block in the container, and that block is <paramref name="expectedSourceIndex"/> — the single-entry requirement for the flat-guard proof (no other path into the guarded block could skip the test).</summary>
    static bool HasSinglePredecessor(IReadOnlyList<BlockEdges> edges, int targetIndex, int expectedSourceIndex)
    {
        int count = 0;
        bool matchesExpected = false;
        for (int i = 0; i < edges.Count; i++)
        {
            if (!edges[i].Successors.Contains(targetIndex))
                continue;
            count++;
            if (i == expectedSourceIndex)
                matchesExpected = true;
        }
        return count == 1 && matchesExpected;
    }

    /// <summary>A read with no observable side effect: a local/argument load or a box over one. Matches the exact vocabulary csc emits for a cached declaration-pattern subject (never a call, allocation, or property getter).</summary>
    public static bool IsReadOnlyOperand(IrExpression value) => value switch
    {
        LoadLocal or LoadArgument or Constant => true,
        Box box => IsReadOnlyOperand(box.Operand),
        _ => false,
    };

    /// <summary>Structural equality over the narrow <see cref="IsReadOnlyOperand"/> vocabulary — enough to recognize csc's duplicated read of the one tested value, not a general expression comparer.</summary>
    public static bool SameTestedValue(IrExpression a, IrExpression b) => (a, b) switch
    {
        (LoadLocal x, LoadLocal y) => x.Index == y.Index && x.Type.Equals(y.Type),
        (LoadArgument x, LoadArgument y) => x.Index == y.Index && x.Type.Equals(y.Type),
        (Constant x, Constant y) => Equals(x.Value, y.Value) && x.Type.Equals(y.Type),
        (Box x, Box y) => x.Type.Equals(y.Type) && SameTestedValue(x.Operand, y.Operand),
        _ => false,
    };

    /// <summary>The direct child of <paramref name="scope"/> that contains <paramref name="node"/> — the statement whose position within the guarded block bounds the intervening-write scan.</summary>
    static IrNode? TopLevelStatementWithin(IrNode node, Block scope)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.Parent, scope))
                return current;
        }
        return null;
    }

    /// <summary>
    /// Whether any preceding sibling subtree on the path from
    /// <paramref name="guardedBlock"/> to <paramref name="site"/> could have
    /// reassigned a local or argument the tested value reads.
    /// </summary>
    static bool HasInterveningWriteAlongPath(Block guardedBlock, IrNode site, IrExpression value)
    {
        for (IrNode current = site; current.Parent is { } parent; current = parent)
        {
            if (HasInterveningWrite(parent, current.ChildIndex, value))
                return true;
            if (ReferenceEquals(parent, guardedBlock))
                return false;
        }
        return true;
    }

    /// <summary>Whether any child subtree in <paramref name="scope"/> before index <paramref name="beforeChildIndex"/> could have reassigned a local/argument the read-only <paramref name="value"/> depends on.</summary>
    static bool HasInterveningWrite(IrNode scope, int beforeChildIndex, IrExpression value)
    {
        var locals = new List<int>();
        var arguments = new List<int>();
        CollectReadOnlyReferences(value, locals, arguments);
        if (locals.Count == 0 && arguments.Count == 0)
            return false;

        for (int i = 0; i < beforeChildIndex && i < scope.Children.Count; i++)
        {
            foreach (var node in DescendantsAndSelfOutsideNestedFunctions(scope.Children[i]))
            {
                if (node is StoreLocal store && locals.Contains(store.Index))
                    return true;
                if (node is StoreArgument storeArgument && arguments.Contains(storeArgument.Index))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether the tested local or argument has any managed-address use in its
    /// function scope. Once an address exists, a later indirect write can
    /// mutate the value without a direct <see cref="StoreLocal"/> or
    /// <see cref="StoreArgument"/> between the guard and extraction.
    /// </summary>
    static bool HasAddressTaken(IrNode site, IrExpression value)
    {
        var locals = new List<int>();
        var arguments = new List<int>();
        CollectReadOnlyReferences(value, locals, arguments);
        if (locals.Count == 0 && arguments.Count == 0)
            return false;

        foreach (var node in DescendantsAndSelfOutsideNestedFunctions(EnclosingFunctionBody(site)))
        {
            if (node is LoadLocalAddress address && locals.Contains(address.Index))
                return true;
            if (node is LoadArgumentAddress argumentAddress && arguments.Contains(argumentAddress.Index))
                return true;
        }
        return false;
    }

    static IrNode EnclosingFunctionBody(IrNode site)
    {
        for (IrNode? current = site; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case Lambda lambda:
                    return lambda.Body;
                case LocalFunctionStatement localFunction:
                    return localFunction.Body;
                case IrFunction function:
                    return function.Body;
            }
        }
        return site;
    }

    public static void CollectReadOnlyReferences(IrExpression value, List<int> locals, List<int> arguments)
    {
        switch (value)
        {
            case LoadLocal load:
                locals.Add(load.Index);
                break;
            case LoadArgument argument:
                arguments.Add(argument.Index);
                break;
            case Box box:
                CollectReadOnlyReferences(box.Operand, locals, arguments);
                break;
        }
    }

    static IEnumerable<IrNode> DescendantsAndSelfOutsideNestedFunctions(IrNode node)
    {
        yield return node;
        if (node is Lambda or LocalFunctionStatement)
            yield break;
        foreach (var descendant in DescendantsOutsideNestedFunctions(node))
            yield return descendant;
    }

    // Pre-order descendants that do NOT cross a nested-function (lambda/local
    // function) boundary. Transforms that mint locals on the root IrFunction must
    // use this to skip guards inside nested scopes, whose locals live in a
    // separate pool the root printer never sees.
    public static IEnumerable<IrNode> DescendantsOutsideNestedFunctions(IrNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            if (child is Lambda or LocalFunctionStatement)
                continue;
            foreach (var descendant in DescendantsOutsideNestedFunctions(child))
                yield return descendant;
        }
    }
}
