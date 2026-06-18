namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Thrown internally when a staged run reaches its <see cref="Stepper.StepLimit"/>,
/// unwinding the pipeline so the tree is left in the state right before that
/// step's rewrite. Caught by the staged runner; never escapes the library.
/// </summary>
public sealed class StepLimitReachedException : Exception
{
    public StepLimitReachedException()
        : base("Pipeline stopped at the configured step limit.") { }
}

/// <summary>
/// One recorded fine-grained rewrite inside a pass: its global ordinal, a human
/// description, the tree position it happened near (the <see cref="IrNode.Describe"/>
/// of the nearby node), and any nested sub-steps. The recorded steps of a run
/// form a hierarchy — a pass groups its work, each rewrite is a leaf.
/// </summary>
public sealed class Step
{
    public Step(int index, string description, string? position)
    {
        Index = index;
        Description = description;
        Position = position;
    }

    /// <summary>Global ordinal across the whole pipeline run; the value to pass as a step limit to stop right before this rewrite.</summary>
    public int Index { get; }

    public string Description { get; }

    /// <summary>The nearby node's one-line description when the step was recorded, or null if none was supplied.</summary>
    public string? Position { get; }

    public List<Step> Children { get; } = [];
}

/// <summary>
/// Records the fine-grained rewrites a pass performs — ILSpy's
/// <c>Stepper</c> (IL/Transforms/Stepper.cs) brought into the shipping tool
/// rather than gated behind a debug GUI. Passes call
/// <see cref="StepInto"/>/<see cref="StepOver"/> at each interesting rewrite;
/// recorded steps form a hierarchy. Setting <see cref="StepLimit"/> and
/// replaying re-runs the pipeline deterministically and stops at step N, which
/// is what makes "show me the tree right before this rewrite went wrong" a
/// one-flag operation (docs/decompiler-ir.md).
/// </summary>
public sealed class Stepper
{
    readonly Stack<Step> _groups = new();
    int _index;

    /// <summary>When false, every step call is a no-op — the disabled stepper threaded through normal runs.</summary>
    public bool IsEnabled { get; }

    /// <summary>The run stops right before the step with this ordinal (replay-to-limit). Default: no limit.</summary>
    public int StepLimit { get; set; } = int.MaxValue;

    /// <summary>The recorded top-level steps, in order.</summary>
    public List<Step> Steps { get; } = [];

    /// <summary>Total steps recorded so far across the run.</summary>
    public int Count => _index;

    public Stepper(bool enabled) => IsEnabled = enabled;

    /// <summary>Records a leaf rewrite. No-op on a disabled stepper; throws <see cref="StepLimitReachedException"/> at the limit.</summary>
    public void StepOver(string description, IrNode? near = null)
    {
        var step = Record(description, near);
        if (step is not null)
            CurrentChildren.Add(step);
    }

    /// <summary>
    /// Records a step and opens it as the current group: subsequent steps nest
    /// under it until <see cref="EndGroup"/>. Returns the disposable scope so a
    /// pass can <c>using</c> it.
    /// </summary>
    public GroupScope StepInto(string description, IrNode? near = null)
    {
        var step = Record(description, near);
        if (step is null)
            return new GroupScope(this, opened: false);
        CurrentChildren.Add(step);
        _groups.Push(step);
        return new GroupScope(this, opened: true);
    }

    void EndGroup()
    {
        if (_groups.Count > 0)
            _groups.Pop();
    }

    List<Step> CurrentChildren => _groups.Count > 0 ? _groups.Peek().Children : Steps;

    Step? Record(string description, IrNode? near)
    {
        if (!IsEnabled)
            return null;
        if (_index >= StepLimit)
            throw new StepLimitReachedException();
        return new Step(_index++, description, near?.Describe());
    }

    /// <summary>The scope returned by <see cref="StepInto"/>; closing it ends the group.</summary>
    public readonly struct GroupScope : IDisposable
    {
        readonly Stepper? _stepper;
        readonly bool _opened;

        internal GroupScope(Stepper stepper, bool opened)
        {
            _stepper = stepper;
            _opened = opened;
        }

        public void Dispose()
        {
            if (_opened)
                _stepper?.EndGroup();
        }
    }
}
