namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds a leading prologue guard — <c>if (c) goto L; …return/throw arm…; L:</c>
/// at the very start of a method body — into the structured guard
/// <c>if (!c) { …arm… }</c>.
///
/// <para>
/// <see cref="StructuringPass"/> is two-phase and all-or-nothing per container:
/// if any later region remains unstructureable, an otherwise-trivial guard at
/// the start of the method also stays flat. This pass independently owns that
/// pristine leading slice, so later irreducible control flow does not prevent a
/// source-like guard return (issue #1089, slice 4; issue #8498).
/// </para>
///
/// <para>
/// The fold owns only a straight-line, externally unentered fallthrough arm
/// ending in <c>return</c>/<c>throw</c>. It never crosses a surviving
/// <c>leave</c> target or consumes internal control flow. The negation mirrors
/// <see cref="StructuringPass"/>'s blessed fallthrough-first guard shape
/// (<see cref="Conditions.Negate"/>), so the fold is opcode-identical to the
/// same guard structured as part of a fully reducible container.
/// </para>
/// </summary>
public sealed class PrologueGuardReturnPass : IIrPass
{
    public string Name => "prologue-guard-return";

    public void Run(IrFunction function, PassContext context)
    {
        var leaveTargets = function.Descendants.OfType<Leave>()
            .Select(leave => leave.TargetOffset)
            .ToHashSet();

        var container = function.Body;
        var blocks = container.Blocks;
        if (blocks.Count < 3)
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
            $"fold independently owned prologue guard at IL_{entry.StartOffset:X4}",
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
