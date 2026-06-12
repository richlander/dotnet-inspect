using System.Diagnostics;

namespace DotnetInspector.Decompiler.Pipeline;

/// <summary>
/// Base node of the replacement pipeline's typed IR (docs/decompiler-ir.md):
/// a mutable tree with parent pointers and child slots, in the ILSpy
/// <c>ILInstruction</c> tradition. <see cref="ReplaceWith"/> is the primitive
/// rewrite; <see cref="CheckInvariant"/> validates parent/child consistency
/// after every pass in debug builds.
/// </summary>
public abstract class IrNode
{
    readonly List<IrNode> _children = [];

    public IrNode? Parent { get; private set; }

    /// <summary>Slot index in the parent; -1 for detached nodes and the root.</summary>
    public int ChildIndex { get; private set; } = -1;

    public IReadOnlyList<IrNode> Children => _children;

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

    public IEnumerable<IrNode> Descendants
    {
        get
        {
            foreach (var child in _children)
            {
                yield return child;
                foreach (var descendant in child.Descendants)
                    yield return descendant;
            }
        }
    }

    /// <summary>
    /// Validates parent/child consistency for this subtree. Debug builds run
    /// this after every pass; a violation is a pipeline bug, never input data.
    /// </summary>
    [Conditional("DEBUG")]
    public void CheckInvariant()
    {
        for (int i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            if (!ReferenceEquals(child.Parent, this))
                throw new InvalidOperationException($"Invariant violated: child '{child.Describe()}' of '{Describe()}' has wrong parent.");
            if (child.ChildIndex != i)
                throw new InvalidOperationException($"Invariant violated: child '{child.Describe()}' of '{Describe()}' has slot {child.ChildIndex}, expected {i}.");
            child.CheckInvariant();
        }
    }

    public override string ToString() => Describe();
}

/// <summary>An IR node that produces a value, typed by <see cref="TypeRef"/>.</summary>
public abstract class IrExpression : IrNode
{
    /// <summary>The expression's result type; null when the pipeline does not know (itself a fidelity signal).</summary>
    public abstract TypeRef? ResultType { get; }
}
