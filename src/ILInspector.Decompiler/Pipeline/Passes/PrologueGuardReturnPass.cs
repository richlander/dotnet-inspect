namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds a leading prologue guard — <c>if (c) goto L; …return/throw arm…; L:</c>
/// at the very start of a method body — into the structured guard
/// <c>if (!c) { …arm… }</c> when the rest of the container stays flat only
/// because it is EH-entangled.
///
/// <para>
/// <see cref="StructuringPass"/> is two-phase and all-or-nothing per container:
/// a container holding the target of a surviving <c>Leave</c> (the
/// <c>leave-target-in-container</c> stop) keeps its <em>entire</em> flat form,
/// so an otherwise-trivial prologue guard sitting in front of an EH/loop residue
/// never folds (issue #1089, slice 4; specimen <c>Interop.Sys::GetCwd()</c>).
/// This pass recovers just that pristine prologue without touching the recovered
/// <c>try</c>/<c>catch</c>/<c>finally</c> nodes or relocating any block across an
/// EH boundary.
/// </para>
///
/// <para>
/// The fold is gated to be EH-safe by construction: it fires only on a container
/// that <see cref="StructuringPass"/> declines for <c>leave-target-in-container</c>,
/// and only when the guard's fallthrough arm is a straight-line block sequence
/// terminating in <c>return</c>/<c>throw</c> that lies <em>entirely before</em> the
/// first leave-target — i.e. in the prologue, never crossing into a region. The
/// negation mirrors <see cref="StructuringPass"/>'s blessed fallthrough-first
/// guard shape (<see cref="Conditions.Negate"/>), so a folded prologue is
/// opcode-identical to the same guard structured in a non-EH method.
/// </para>
/// </summary>
public sealed class PrologueGuardReturnPass : IIrPass
{
    public string Name => "prologue-guard-return";

    public void Run(IrFunction function, PassContext context)
    {
        // Surviving leaves are the cross-container early exits the EH pass left
        // flat; their target blocks are why StructuringPass keeps the container
        // flat. No leaves means StructuringPass already owns this method.
        var leaveTargets = function.Descendants.OfType<Leave>()
            .Select(leave => leave.TargetOffset)
            .ToHashSet();
        if (leaveTargets.Count == 0)
            return;

        var container = function.Body;
        var blocks = container.Blocks;
        if (blocks.Count < 3)
            return;

        // Only fire when StructuringPass declines this container for the exact
        // leave-target-in-container reason — otherwise the prologue would fold
        // through the normal structurer and this pass must not double-handle it.
        if (!blocks.Any(b => leaveTargets.Contains(b.StartOffset)))
            return;

        var entry = blocks[0];
        if (entry.Children.Count == 0 || entry.Children[^1] is not ConditionalBranch guard)
            return;

        int targetIndex = container.IndexOfOffset(guard.TargetOffset);
        if (targetIndex < 2)
            return;  // need at least one arm block strictly before the target

        // The arm is every block between the guard and its target. It must be a
        // straight-line return/throw island that no other edge enters and that
        // ends before the first leave-target — so folding it relocates nothing
        // across an EH boundary and erases no label a goto still needs.
        var branchTargets = CollectBranchTargets(function);
        for (int i = 1; i < targetIndex; i++)
        {
            var armBlock = blocks[i];
            if (leaveTargets.Contains(armBlock.StartOffset))
                return;  // the arm reaches into the EH residue — not a prologue
            if (branchTargets.Contains(armBlock.StartOffset))
                return;  // another edge enters the arm — dropping it is unsafe
            foreach (var child in armBlock.Children)
                if (child is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                    return;  // control flow inside the arm is out of this slice
        }

        if (blocks[targetIndex - 1].Children is not [.., Return or Throw])
            return;  // the arm must terminate, else it would fall into the target

        // The entry's own statements before the guard must be straight-line: a
        // branch there would mean the entry is itself a structuring region.
        for (int s = 0; s < entry.Children.Count - 1; s++)
            if (entry.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return;

        var condition = guard.Condition;
        condition.Detach();
        guard.Detach();

        var armBody = new Block(blocks[1].StartOffset);
        for (int i = 1; i < targetIndex; i++)
            foreach (var child in blocks[i].DetachChildren())
                armBody.Add(child);

        var ifStatement = new IfStatement(Conditions.Negate(condition), armBody, null);
        ifStatement.InheritSourceOffset(guard);
        entry.Add(ifStatement);

        for (int i = targetIndex - 1; i >= 1; i--)
            blocks[i].Detach();

        context.Stepper.StepOver(
            $"fold prologue guard at IL_{entry.StartOffset:X4} (EH-entangled body stays flat)",
            container);
    }

    static HashSet<int> CollectBranchTargets(IrFunction function)
    {
        var targets = new HashSet<int>();
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case Branch branch:
                    targets.Add(branch.TargetOffset);
                    break;
                case ConditionalBranch conditional:
                    targets.Add(conditional.TargetOffset);
                    break;
                case Leave leave:
                    targets.Add(leave.TargetOffset);
                    break;
                case SwitchBranch switchBranch:
                    foreach (int target in switchBranch.TargetOffsets)
                        targets.Add(target);
                    break;
            }
        }
        return targets;
    }
}
