namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Optional recorder for the printer's definite-assignment dataflow — the
/// analysis inside <see cref="CSharpPrinter"/> that decides which locals keep
/// their <c>= default</c> zero-initializer. The facts (per-block predecessors,
/// gen, and the <c>in</c>/<c>out</c> sets of the CFG fixpoint) otherwise exist
/// only as transient sets inside <c>ComputeReadBeforeAssign</c>, so a
/// soundness question can only be answered by a slow validity A/B rather
/// than by reading the analysis directly.
///
/// Like <see cref="Stepper"/>, this is opt-in: the shipped print path passes no
/// sink and pays nothing. The recorder is filled by the <em>real</em> analysis
/// (the same walk that produces the output), so what it surfaces is what ships
/// — never a parallel reimplementation that could drift.
/// </summary>
public sealed class DataflowFacts
{
    /// <summary>
    /// One block's definite-assignment facts. Offsets identify blocks; locals
    /// are slot indices into <see cref="LocalNames"/>.
    /// </summary>
    public sealed record BlockFacts(
        int Offset,
        IReadOnlyList<int> Predecessors,
        IReadOnlyList<int> Successors,
        IReadOnlyList<int> Gen,
        IReadOnlyList<int> In,
        IReadOnlyList<int> Out,
        bool Reachable);

    /// <summary>The per-block facts for one goto-connected container analyzed as a CFG.</summary>
    public sealed record ContainerFacts(IReadOnlyList<BlockFacts> Blocks);

    readonly List<ContainerFacts> _containers = [];

    /// <summary>Each container the analysis solved as a control-flow graph, in walk order.</summary>
    public IReadOnlyList<ContainerFacts> Containers => _containers;

    /// <summary>Display names for each local slot, index-aligned (a source name or <c>V_N</c>).</summary>
    public IReadOnlyList<string> LocalNames { get; set; } = [];

    /// <summary>
    /// Locals the analysis proved may be read before they are definitely
    /// assigned — the ones whose declaration keeps <c>= default</c>. The
    /// complement declares bare.
    /// </summary>
    public IReadOnlyList<int> ReadBeforeAssign { get; set; } = [];

    /// <summary>
    /// True when the analysis hit a shape it does not model (an EH leave, a
    /// branch leaving the container) and conservatively bailed, marking every
    /// local read-before-assign.
    /// </summary>
    public bool Bailed { get; set; }

    internal void AddContainer(ContainerFacts container) => _containers.Add(container);
}
