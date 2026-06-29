namespace ILInspector.ControlFlow;

/// <summary>
/// One block's outgoing structural control-flow edges. <see cref="Successors"/> are indices
/// into the same container; <see cref="ExternalTargets"/> are branch target offsets that do not
/// name a block in this container. A method exit, external target, or EH-survivor terminator is
/// reported as a flag rather than resolved, so analyses can either bail or route them to a
/// virtual exit. Shared, IL-representation-agnostic so both the decompiler and the analyzer can
/// produce edges from their own block models and feed one dominance/dataflow kernel.
/// </summary>
public sealed record BlockEdges(
    IReadOnlyList<int> Successors,
    IReadOnlyList<int> ExternalTargets,
    bool ExitsMethod,
    bool LeavesRegion);
