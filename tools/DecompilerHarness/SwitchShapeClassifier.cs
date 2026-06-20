using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Sub-classifies a method in the <c>structuring: switch-branch</c> bucket by the
/// structural shape of its first residual <see cref="SwitchBranch"/>. Backs
/// <c>--gaps --by-shape</c>: turning a flat count into a shape histogram is what
/// scopes the next SwitchRaisingPass slice (issue #682).
///
/// <para>Reads the IMPORTED (pre-pass) tree — the switch is a block terminator
/// there, with one block per IL leader. (The passes flatten a switch they fail to
/// raise into a single goto-table block, which destroys the section structure, so
/// the post-pass tree cannot be classified.)</para>
///
/// <para>The buckets are checked in priority order, most-blocking first, and each
/// was verified against a <c>--dump</c> of a representative method:</para>
/// <list type="bullet">
/// <item><c>loop-back-edge</c> — a region block branches backward (a loop/iterator
///   state machine, e.g. TimerQueue.MoveNext); out of scope for forward
///   structuring.</item>
/// <item><c>nested-switch</c> — a case body is itself a switch, or the method has
///   more than one switch (a multi-level dispatch table, e.g.
///   InvokeUtils::PrimitiveWiden).</item>
/// <item><c>default-routes-into-cases</c> — the default block is a case target, or
///   the default section branches into case bodies (e.g.
///   AssemblyNameParser::IsWhiteSpace, SpinLock::IsEnterDeprioritized).</item>
/// <item><c>external-entry-into-cases</c> — a block before the switch jumps into
///   the case-body span, bypassing the table (a prologue guard sharing a case
///   body, e.g. HebrewCalendar::GetLunarMonthDay).</item>
/// <item><c>multi-block-case-section</c> — a case/default section spans several
///   blocks with interior control flow (an <c>if</c>/<c>?:</c> inside the case,
///   e.g. ValueType::GetHashCode); the section-as-region relaxation clears it.</item>
/// <item><c>single-block-clean</c> — none of the above; a clean single-block-section
///   switch that nonetheless did not raise (unexpected — a SwitchRaisingPass bug
///   rather than a known unhandled shape).</item>
/// </list>
/// </summary>
static class SwitchShapeClassifier
{
    public static string Classify(IrFunction function)
    {
        // The container whose block ends in the first residual switch. In the
        // imported tree a switch always terminates its block.
        SwitchBranch? switchBranch = null;
        BlockContainer? container = null;
        foreach (var c in function.Descendants.OfType<BlockContainer>())
        {
            foreach (var block in c.Blocks)
            {
                if (block.Children.Count > 0 && block.Children[^1] is SwitchBranch sw)
                {
                    switchBranch = sw;
                    container = c;
                    break;
                }
            }
            if (switchBranch is not null)
                break;
        }
        if (switchBranch is null || container is null)
            return "no-residual-switch";   // defensive: caller filtered to the switch bucket

        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        int switchIndex = -1;
        for (int i = 0; i < blocks.Count; i++)
            if (blocks[i].Children.Count > 0 && ReferenceEquals(blocks[i].Children[^1], switchBranch))
            {
                switchIndex = i;
                break;
            }

        // Case targets, as block indices. An unresolved or backward (<= switch)
        // table target is a loop table — a back-edge by another name.
        var caseIndices = new List<int>();
        foreach (int offset in switchBranch.TargetOffsets)
        {
            if (!offsetToIndex.TryGetValue(offset, out int ti) || ti <= switchIndex)
                return "loop-back-edge";
            caseIndices.Add(ti);
        }
        int maxCaseIndex = caseIndices.Max();
        var caseSet = caseIndices.ToHashSet();

        // 1. Loop: a block at or after the switch branches backward (target block
        // index strictly less than the branching block), the signature of a loop
        // wrapped around or inside the switch.
        for (int i = switchIndex; i < blocks.Count; i++)
            foreach (int t in BranchTargets(blocks[i]))
                if (offsetToIndex.TryGetValue(t, out int ti) && ti < i)
                    return "loop-back-edge";

        // 2. Nested / multi-level switch: more than one switch in the method, or a
        // case body that is itself a switch.
        if (function.Descendants.OfType<SwitchBranch>().Skip(1).Any())
            return "nested-switch";

        // 3. Default routes into the cases: the block right after the switch (the
        // fall-through default) is itself a case body, or it branches into one.
        int defaultIndex = switchIndex + 1;
        if (defaultIndex < blocks.Count)
        {
            if (caseSet.Contains(defaultIndex))
                return "default-routes-into-cases";
            foreach (int t in BranchTargets(blocks[defaultIndex]))
                if (offsetToIndex.TryGetValue(t, out int ti) && caseSet.Contains(ti))
                    return "default-routes-into-cases";
        }

        // 4. External entry: a block before the switch jumps into the case-body
        // span, bypassing the table (a prologue guard sharing a case body).
        for (int i = 0; i < switchIndex; i++)
            foreach (int t in BranchTargets(blocks[i]))
                if (offsetToIndex.TryGetValue(t, out int ti) && ti > switchIndex && ti <= maxCaseIndex)
                    return "external-entry-into-cases";

        // 5. Multi-block section: a case (or the fall-through default) starts an
        // interior decision — its block ends in a ConditionalBranch whose arms stay
        // inside the section (an `if`/`?:` in the case body, e.g. GetHashCode). A
        // block-spanning end-of-container heuristic would over-count the last
        // section (sweeping in the default body and join), so key on the branch.
        foreach (int idx in caseIndices.Append(defaultIndex).Distinct())
            if (idx < blocks.Count && blocks[idx].Children.Count > 0
                && blocks[idx].Children[^1] is ConditionalBranch)
                return "multi-block-case-section";

        return "single-block-clean";
    }

    static IEnumerable<int> BranchTargets(Block block)
    {
        foreach (var node in block.Children)
        {
            switch (node)
            {
                case Branch b: yield return b.TargetOffset; break;
                case ConditionalBranch c: yield return c.TargetOffset; break;
                case SwitchBranch sw:
                    foreach (int t in sw.TargetOffsets) yield return t;
                    break;
                case Leave lv: yield return lv.TargetOffset; break;
            }
        }
    }
}
