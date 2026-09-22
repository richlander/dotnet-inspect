namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Separates compiler pattern-test storage from the exact PDB local that begins
/// at the successful branch target.
/// </summary>
public sealed class PdbScopeEntryLocalPass : IIrPass
{
    public string Name => "pdb-scope-entry-locals";

    public void Run(IrFunction function, PassContext context)
    {
        var groups = Enumerable.Range(0, function.LocalNames.Length)
            .Where(index => ExactBinding(function, index) is not null
                && function.LocalNames[index] is { } name
                && CSharpNaming.IsUsableIdentifier(name)
                && !function.EliminatedLocalSlots.Contains(index)
                && IrFunction.LocalSlotReferencesInScope(function.Body, index).Any())
            .GroupBy(index => function.LocalNames[index]!, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.ToArray())
            .Where(group => PdbScopesAreDisjoint(function, group))
            .ToArray();

        var plannedGroups = groups
            .Select(group => (
                Group: group,
                Candidates: group
                    .Where(index => CanMaterialize(function, index))
                    .ToArray()))
            .Where(plan => plan.Candidates.Length != 0)
            .ToArray();
        if (plannedGroups.Length == 0)
            return;

        int[] candidates = [.. plannedGroups.SelectMany(plan => plan.Candidates)];
        var trial = (IrFunction)function.Clone();
        int[] projected = Materialize(trial, candidates, PassContext.None);
        if (!TrialPreservesExactNames(
                trial,
                [.. plannedGroups.SelectMany(plan => plan.Group).Distinct()],
                candidates,
                projected))
        {
            return;
        }

        Materialize(function, candidates, context);
    }

    static bool CanMaterialize(IrFunction function, int index)
    {
        if (!function.IsLocalDeclaredInNestedScope(index)
            || ExactBinding(function, index) is not { } binding)
        {
            return false;
        }

        TypeRef type = function.Locals[index];
        if (type.Kind is TypeRefKind.ByRef or TypeRefKind.Pinned
            || type.ContainsUnsupported)
        {
            return false;
        }

        IrNode[] references =
            IrFunction.LocalSlotReferencesInScope(function.Body, index).ToArray();
        IrNode[] inside = references
            .Where(reference => Contains(binding.Scope, reference.SourceOffset))
            .ToArray();
        IrNode[] outside = references
            .Where(reference => !Contains(binding.Scope, reference.SourceOffset))
            .ToArray();
        if (inside.Length == 0
            || inside.Any(reference => reference is not LoadLocal)
            || outside.Any(reference => reference.SourceOffset < 0
                || reference is not (LoadLocal or StoreLocal)))
        {
            return false;
        }

        StoreLocal[] stores = references.OfType<StoreLocal>().ToArray();
        if (stores.Length == 0
            || stores.Any(store =>
                store.SourceOffset < 0
                || store.SourceOffset >= binding.Scope.StartOffset))
        {
            return false;
        }

        Block[] entries = function.DescendantsOutsideNestedFunctions
            .OfType<Block>()
            .Where(block => block.StartOffset == binding.Scope.StartOffset)
            .ToArray();
        return entries.Length == 1
            && ReferenceOwnership.CollectBranchTargets(function)
                .Contains(binding.Scope.StartOffset);
    }

    static bool TrialPreservesExactNames(
        IrFunction trial,
        int[] originalGroup,
        int[] carriers,
        int[] projected)
    {
        var facts = new DataflowFacts();
        var readBeforeAssign = DefiniteAssignment.Compute(
            trial,
            ReferenceOwnership.CollectBranchTargets(trial),
            facts);
        if (facts.Bailed
            || trial.SkipLocalsInit && carriers.Any(readBeforeAssign.Contains))
            return false;

        new PdbLocalScopePass().Run(trial, PassContext.None);
        var retained = ExactLocalNameAllocation.RetainedLocalSlots(
            trial, trial.Locals.Length, trial.EliminatedLocalSlots);
        var allocation = ExactLocalNameAllocation.Allocate(
            trial,
            trial.Locals.Length,
            trial.LocalNames,
            ExactLocalNameAllocation.ReservedNames(
                trial,
                trial.Signature.Parameters,
                trial.Signature.GenericParameterNames),
            retained);
        var resultingGroup = originalGroup
            .Except(carriers)
            .Concat(projected);
        return resultingGroup.All(index =>
            allocation.Dispositions[index] == ExactLocalNameDisposition.Preserved);
    }

    static int[] Materialize(
        IrFunction function,
        int[] candidates,
        PassContext context)
    {
        var projected = new int[candidates.Length];
        for (int position = 0; position < candidates.Length; position++)
        {
            int carrier = candidates[position];
            PdbLocalDeclaration binding = ExactBinding(function, carrier)!;
            TypeRef type = function.Locals[carrier];
            int local = function.AddLocal(type, binding.Name);
            projected[position] = local;

            function.LocalNames = function.LocalNames.SetItem(carrier, null);
            function.LocalDeclarationBindings = function.LocalDeclarationBindings
                .SetItem(carrier, null)
                .SetItem(local, binding);
            function.LocalDeclaredInNestedScope = function.LocalDeclaredInNestedScope
                .SetItem(carrier, false)
                .SetItem(local, true);

            foreach (LoadLocal load in IrFunction.LocalSlotReferencesInScope(
                function.Body, carrier)
                .OfType<LoadLocal>()
                .Where(load => Contains(binding.Scope, load.SourceOffset))
                .ToArray())
            {
                var replacement = new LoadLocal(local, type);
                replacement.InheritSourceOffset(load);
                load.ReplaceWith(replacement);
            }

            Block entry = function.DescendantsOutsideNestedFunctions
                .OfType<Block>()
                .Single(block => block.StartOffset == binding.Scope.StartOffset);
            var statements = entry.DetachChildren();
            var anchor = new LabelAnchor { RetainsPdbLocalScope = true };
            anchor.SetSourceOffset(binding.Scope.StartOffset);
            entry.Add(anchor);

            var value = new LoadLocal(carrier, type);
            value.SetSourceOffset(binding.Scope.StartOffset);
            var declaration = new StoreLocal(local, type, value)
            {
                PdbScopeEntryProjection = new(carrier, binding),
            };
            declaration.SetSourceOffset(binding.Scope.StartOffset);
            entry.Add(declaration);
            foreach (IrNode statement in statements)
                entry.Add(statement);

            context.Stepper.StepOver(
                $"materialize exact PDB local {local} from carrier {carrier}",
                declaration);
        }
        return projected;
    }

    static bool PdbScopesAreDisjoint(IrFunction function, int[] group)
    {
        for (int left = 0; left < group.Length; left++)
        {
            for (int right = left + 1; right < group.Length; right++)
            {
                PdbLocalDeclaration? leftBinding =
                    ExactBinding(function, group[left]);
                PdbLocalDeclaration? rightBinding =
                    ExactBinding(function, group[right]);
                if (leftBinding is null
                    || rightBinding is null
                    || ScopesOverlap(leftBinding.Scope, rightBinding.Scope))
                {
                    return false;
                }
            }
        }
        return true;
    }

    static PdbLocalDeclaration? ExactBinding(IrFunction function, int index)
        => index < function.LocalDeclarationBindings.Length
            ? function.LocalDeclarationBindings[index]
            : null;

    static bool Contains(LocalSlotScope scope, int offset)
        => offset >= scope.StartOffset && offset < scope.EndOffset;

    static bool ScopesOverlap(LocalSlotScope left, LocalSlotScope right)
        => left.StartOffset < right.EndOffset
            && right.StartOffset < left.EndOffset;
}
