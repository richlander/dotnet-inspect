namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Base node of the pipeline's typed IR (docs/decompiler-ir.md):
/// a mutable tree with parent pointers and child slots, in the ILSpy
/// <c>ILInstruction</c> tradition. <see cref="ReplaceWith"/> is the primitive
/// rewrite; <see cref="CheckInvariant"/> validates parent/child consistency
/// after every pass when <see cref="IrInvariants.Enabled"/> is set.
/// </summary>
public abstract class IrNode
{
    List<IrNode> _children = [];

    public IrNode? Parent { get; private set; }

    /// <summary>Slot index in the parent; -1 for detached nodes and the root.</summary>
    public int ChildIndex { get; private set; } = -1;

    public IReadOnlyList<IrNode> Children => _children;

    /// <summary>
    /// IL offset of the instruction this node was imported from, or -1 when the
    /// node has no provenance (a spill/join artifact, or a node a pass
    /// synthesized without inheriting one). Provenance, not identity: it is
    /// copied by <see cref="Clone"/> (via <c>MemberwiseClone</c>) and untouched
    /// by re-parenting, so a stamped offset survives most passes for free. This
    /// is the bridge that lets a single annotation set key both the IL view (by
    /// offset) and the C# view (via the node that printed a line).
    /// </summary>
    public int SourceOffset { get; private set; } = -1;

    /// <summary>Records the originating IL offset. Stamped at import; idempotent.</summary>
    public void SetSourceOffset(int offset) => SourceOffset = offset;

    /// <summary>
    /// Best-effort provenance for a node synthesized by a pass: adopt the source
    /// offset of an existing node it derives from, but only when this node has
    /// none of its own.
    /// </summary>
    public void InheritSourceOffset(IrNode from)
    {
        if (SourceOffset < 0)
            SourceOffset = from.SourceOffset;
    }

    /// <summary>One-line description for tree dumps; no recursion into children.</summary>
    public abstract string Describe();

    /// <summary>Types this node references directly (not via children) — the fidelity computation scans these, so a node holding an unsupported type can never report Full.</summary>
    public virtual IEnumerable<TypeRef> DirectTypes => [];

    protected void AddChild(IrNode child)
    {
        Adopt(child, _children.Count);
        _children.Add(child);
    }

    public void SetChild(int index, IrNode child)
    {
        var old = _children[index];
        old.Parent = null;
        old.ChildIndex = -1;
        Adopt(child, index);
        _children[index] = child;
    }

    void Adopt(IrNode child, int index)
    {
        if (child.Parent is not null)
            throw new InvalidOperationException($"Node '{child.Describe()}' already has a parent; detach it first.");
        if (ReferenceEquals(child, this))
            throw new InvalidOperationException("A node cannot be its own child.");
        child.Parent = this;
        child.ChildIndex = index;
    }

    /// <summary>
    /// Detaches and returns all children, in slot order — the re-parenting
    /// primitive for restructuring passes (detach, build the replacement
    /// around them, then <see cref="ReplaceWith"/>).
    /// </summary>
    public IReadOnlyList<IrNode> DetachChildren()
    {
        var detached = new List<IrNode>(_children);
        foreach (var child in detached)
        {
            child.Parent = null;
            child.ChildIndex = -1;
        }
        _children.Clear();
        return detached;
    }

    /// <summary>Detaches this node from its parent; later siblings shift down one slot.</summary>
    public void Detach()
    {
        if (Parent is null)
            throw new InvalidOperationException("Node is already detached.");
        var siblings = Parent._children;
        siblings.RemoveAt(ChildIndex);
        for (int i = ChildIndex; i < siblings.Count; i++)
            siblings[i].ChildIndex = i;
        Parent = null;
        ChildIndex = -1;
    }

    /// <summary>Replaces this node at its slot in the parent. The primitive rewrite — what makes "this node was consumed" a tree edit instead of side-channel state.</summary>
    public void ReplaceWith(IrNode replacement)
    {
        if (Parent is null)
            throw new InvalidOperationException("Cannot replace a detached or root node.");
        Parent.SetChild(ChildIndex, replacement);
    }

    /// <summary>
    /// Pre-order (self-excluded) descendants of this node. Iterative DFS: a recursive
    /// <c>yield return</c> over children allocates one nested iterator per node (O(N)
    /// state machines per traversal) and re-yields every value up through its ancestors
    /// (O(N*depth) time). This walks the same order with a single work stack rented from
    /// a per-thread pool, so a traversal allocates only its iterator state machine — the
    /// stack and its backing array are reused across traversals. Children are pushed in
    /// reverse so they pop in source order, reproducing the recursive pre-order.
    /// </summary>
    public IEnumerable<IrNode> Descendants
    {
        get
        {
            if (_children.Count == 0)
                yield break;
            var stack = DescendantStackPool.Rent();
            try
            {
                for (int i = _children.Count - 1; i >= 0; i--)
                    stack.Push(_children[i]);
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    yield return node;
                    var children = node._children;
                    for (int i = children.Count - 1; i >= 0; i--)
                        stack.Push(children[i]);
                }
            }
            finally
            {
                // Runs when the iterator is disposed — foreach and LINQ both dispose,
                // including on an early break — so the stack returns to the pool even
                // when enumeration stops early.
                DescendantStackPool.Return(stack);
            }
        }
    }

    /// <summary>
    /// A detached deep copy of this subtree. The node's payload is shared
    /// (the model types it references — <see cref="TypeRef"/>, method/field
    /// handles, constants — are immutable), its children are cloned
    /// recursively, and the copy has no parent. This is the duplication
    /// primitive for passes that must materialize one node at more than one
    /// site (e.g. inlining a shared terminator block into each guard).
    /// </summary>
    public IrNode Clone()
    {
        var copy = (IrNode)MemberwiseClone();
        copy.Parent = null;
        copy.ChildIndex = -1;
        copy._children = [];
        foreach (var child in _children)
            copy.AddChild(child.Clone());
        return copy;
    }

    /// <summary>
    /// Validates structural invariants for this subtree: each child's
    /// <see cref="Parent"/> is this node and its <see cref="ChildIndex"/> matches
    /// its slot. Runs after every pass when <see cref="IrInvariants.Enabled"/> is
    /// set, and on every explicit call; a violation is a pipeline bug, never input
    /// data. Always compiled (no <c>[Conditional]</c>) so it runs in the optimized
    /// Release build the test suite and corpus sweeps use.
    /// <para>
    /// This parameterless overload checks structure only — the invariant every
    /// well-formed <em>and</em> minimally hand-built subtree satisfies, so it is
    /// safe to run across the whole unit-test suite. Semantic invariants that
    /// require a fully-formed function (e.g. local-slot range) live in the
    /// <see cref="CheckInvariant(bool)"/> overload and run over real importer
    /// output; see that overload.
    /// </para>
    /// </summary>
    public void CheckInvariant() => CheckInvariant(includeSemantics: false);

    /// <summary>
    /// Validates structural invariants (always) and, when
    /// <paramref name="includeSemantics"/> is true, semantic invariants that only
    /// hold for a fully-formed function tree:
    /// <list type="bullet">
    /// <item>local-slot range — every <c>ldloc</c>/<c>stloc</c>/<c>ldloca</c>
    /// (<see cref="LoadLocal"/>, <see cref="StoreLocal"/>,
    /// <see cref="LoadLocalAddress"/>) references a slot that exists in the
    /// nearest enclosing local scope. The containing <see cref="IrFunction"/>
    /// always opens a scope; a <em>static</em> <see cref="LocalFunctionStatement"/>
    /// also always opens its own scope (it cannot capture); a capturing
    /// (non-static) <see cref="LocalFunctionStatement"/> or a <see cref="Lambda"/>
    /// opens its own scope only when it declares a non-empty local table — an
    /// empty-<c>Locals</c> capturing body <em>shares</em> the host's scope and
    /// references the outer function's locals by their outer index, matching how
    /// the C# printer scopes it
    /// (<c>NeedsNestedLambdaScope</c>/<c>NeedsNestedLocalFunctionScope</c>). A
    /// pass that drops a local without repointing its readers, or fabricates a
    /// dangling slot, trips this instead of surfacing as a downstream
    /// miscompile.</item>
    /// </list>
    /// This overload's level is fixed by its argument, not by
    /// <see cref="IrInvariants.CheckSemantics"/>: a call site asks for exactly
    /// what it wants, which is what keeps per-test coverage hermetic under
    /// xUnit's parallel collections. Since #3302 the per-pass hooks thread the
    /// globally armed level instead, so semantic checks are on by default there.
    /// Hand-built unit-test fixtures that omit the local table are therefore
    /// still fine when a test calls a pass directly, but not when they are
    /// routed through <c>IrPasses.Run</c> — see #3317.
    /// </summary>
    public void CheckInvariant(bool includeSemantics) =>
        CheckSubtree(includeSemantics ? EnclosingLocalScope() : -1, includeSemantics);

    /// <summary>
    /// Recursive core. <paramref name="localScope"/> is the number of local slots
    /// declared by the nearest enclosing local scope, or -1 when local-slot checks
    /// are disabled or no enclosing scope is known (a detached subtree) — in which
    /// case those checks are skipped, since the scope that would bound them is not
    /// present.
    /// </summary>
    void CheckSubtree(int localScope, bool includeSemantics)
    {
        if (includeSemantics)
            ValidateLocalSlot(localScope);

        // A nested function scope (lambda / local function) rebinds the local
        // table for its own subtree; the function root establishes the top scope.
        int childScope = includeSemantics ? (OwnLocalScope() ?? localScope) : -1;

        for (int i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            if (!ReferenceEquals(child.Parent, this))
                throw new InvalidOperationException($"Invariant violated: child '{child.Describe()}' of '{Describe()}' has wrong parent.");
            if (child.ChildIndex != i)
                throw new InvalidOperationException($"Invariant violated: child '{child.Describe()}' of '{Describe()}' has slot {child.ChildIndex}, expected {i}.");
            child.CheckSubtree(childScope, includeSemantics);
        }
    }

    /// <summary>
    /// Local-slot count of the scope this node itself opens, or null if it opens
    /// none. An <see cref="IrFunction"/> always opens a scope (even with zero
    /// locals). A <em>static</em> <see cref="LocalFunctionStatement"/> also always
    /// opens its own scope: it cannot capture, so its local references bind to its
    /// own table and an empty table is a genuine zero-slot scope. A capturing
    /// (non-static) <see cref="LocalFunctionStatement"/> or a <see cref="Lambda"/>
    /// opens its own scope only when it declares locals; an empty-<c>Locals</c>
    /// capturing body shares the enclosing scope, so returning null here lets its
    /// outer-indexed local references validate against the host — mirroring the
    /// printer's shared-vs-nested distinction.
    /// <para>
    /// The <see cref="Lambda"/> node carries no static/instance flag, so a
    /// hypothetical static lambda with an empty local table is treated as shared
    /// (a bounded, no-false-positive under-approximation) rather than tightened
    /// the way a static local function is.
    /// </para>
    /// </summary>
    int? OwnLocalScope() => this switch
    {
        IrFunction f => f.Locals.Length,
        LocalFunctionStatement { IsStatic: true } lf => lf.Locals.Length,
        LocalFunctionStatement lf => lf.Locals.IsEmpty ? null : lf.Locals.Length,
        Lambda l => l.Locals.IsEmpty ? null : l.Locals.Length,
        _ => null,
    };

    /// <summary>Local-slot count of the nearest scope-defining ancestor, or -1 if none.</summary>
    int EnclosingLocalScope()
    {
        for (var node = Parent; node is not null; node = node.Parent)
            if (node.OwnLocalScope() is int count)
                return count;
        return -1;
    }

    void ValidateLocalSlot(int localScope)
    {
        if (localScope < 0)
            return;

        int? slot = this switch
        {
            LoadLocal l => l.Index,
            StoreLocal s => s.Index,
            LoadLocalAddress a => a.Index,
            _ => null,
        };

        if (slot is int index && (index < 0 || index >= localScope))
            throw new InvalidOperationException(
                $"Invariant violated: '{Describe()}' references local slot {index}, out of range for the enclosing scope's {localScope} local slot(s).");
    }

    public override string ToString() => Describe();
}

/// <summary>
/// Per-thread pool of work stacks for <see cref="IrNode.Descendants"/>. A
/// traversal rents a stack and returns it on <c>Dispose</c>, so steady-state
/// traversal allocates nothing. Reentrancy-safe: nested/interleaved traversals rent
/// distinct stacks, and a skipped return only falls back to a fresh allocation. The
/// return path is idempotent against a double-return (a copied-then-doubly-disposed
/// enumerator) so a stack cannot be handed out twice.
/// </summary>
static class DescendantStackPool
{
    const int MaxPooledPerThread = 8;

    [ThreadStatic]
    static Stack<IrNode>?[]? _free;

    [ThreadStatic]
    static int _count;

    public static Stack<IrNode> Rent()
    {
        var free = _free;
        if (free is not null && _count > 0)
        {
            int index = --_count;
            var stack = free[index]!;
            free[index] = null;
            stack.Clear();
            return stack;
        }
        return new Stack<IrNode>();
    }

    public static void Return(Stack<IrNode> stack)
    {
        var free = _free ??= new Stack<IrNode>?[MaxPooledPerThread];
        // Idempotent: never pool the same instance twice (guards a double-return from
        // a copied-then-doubly-disposed struct enumerator).
        for (int i = 0; i < _count; i++)
        {
            if (ReferenceEquals(free[i], stack))
                return;
        }
        if (_count < free.Length)
        {
            stack.Clear();
            free[_count++] = stack;
        }
    }
}

/// <summary>An IR node that produces a value, typed by <see cref="TypeRef"/>.</summary>
public abstract class IrExpression : IrNode
{
    /// <summary>The expression's result type; null when the pipeline does not know (itself a fidelity signal).</summary>
    public abstract TypeRef? ResultType { get; }
}
