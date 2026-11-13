namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Separates compiler pattern-test storage from the exact PDB local that begins
/// at the successful branch target.
/// </summary>
public sealed class PdbScopeEntryLocalPass : IIrPass
{
    public string Name => "pdb-scope-entry-locals";

    public PassAnalysisKind RequiredAnalyses
        => PassAnalysisKind.BranchTargets;

    public PassAnalysisKind PreservedAnalyses
        => PassAnalysisKind.BranchTargets;

    public void Run(IrFunction function, PassContext context)
    {
        IReadOnlySet<int> branchTargets =
            context.BranchTargets(function);
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
                    .Where(index => CanMaterialize(
                        function,
                        index,
                        branchTargets))
                    .ToArray()))
            .Where(plan => plan.Candidates.Length != 0)
            .ToArray();
        if (plannedGroups.Length == 0)
            return;

        int[] candidates = [.. plannedGroups.SelectMany(plan => plan.Candidates)];
        int[] patternCarriers = [.. candidates.Where(index =>
            IrFunction.LocalSlotReferencesInScope(function.Body, index)
                .Any(reference => reference is IsPattern))];
        int[] requireReachingWrite = [.. candidates.Where(index =>
        {
            int offset = ExactBinding(function, index)!.Scope.StartOffset;
            var entry = FindEntry(function, index, offset, branchTargets)!.Value;
            return entry.Statement is not null
                || !branchTargets.Contains(offset)
                || function.DescendantsOutsideNestedFunctions
                    .OfType<Block>()
                    .Count(block => block.StartOffset == offset) > 1;
        })];
        var trial = (IrFunction)function.Clone();
        int[] projected = Materialize(trial, candidates, PassContext.None);
        if (!TrialPreservesExactNames(
                trial,
                [.. plannedGroups.SelectMany(plan => plan.Group).Distinct()],
                candidates,
                projected,
                requireReachingWrite,
                patternCarriers))
        {
            return;
        }

        Materialize(function, candidates, context);
    }

    static bool CanMaterialize(
        IrFunction function,
        int index,
        IReadOnlySet<int> branchTargets)
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
            || outside.Any(reference =>
                reference is not (LoadLocal or StoreLocal or IsPattern)
                || reference.SourceOffset < 0 && reference is not IsPattern))
        {
            return false;
        }

        StoreLocal[] stores = references.OfType<StoreLocal>().ToArray();
        IsPattern[] patterns = references.OfType<IsPattern>().ToArray();
        if (stores.Any(store =>
                store.SourceOffset < 0
                || store.SourceOffset >= binding.Scope.StartOffset))
        {
            return false;
        }

        var entry = FindEntry(function, index, binding.Scope.StartOffset, branchTargets);
        if (entry is null
            || (patterns.Length == 0 && stores.Length == 0)
            || (patterns.Length != 0 && (stores.Length != 0
                || patterns.Length != 1
                || !PatternSuccessDominates(function, patterns[0], entry.Value))))
            return false;

        return entry.Value.Statement is null
                || !entry.Value.Block.Children
                    .Take(entry.Value.Statement.ChildIndex)
                    .SelectMany(statement => statement
                        .DescendantsOutsideNestedFunctions.Prepend(statement))
                    .Any(node => node is LoadLocal load
                        && load.Index == index
                        && Contains(binding.Scope, load.SourceOffset));
    }

    static bool PatternSuccessDominates(
        IrFunction function,
        IsPattern pattern,
        (Block Block, IrNode? Statement) entry)
    {
        return pattern.Parent is IfStatement guard
            && ReferenceEquals(guard.Condition, pattern)
            && !ReferenceOwnership.SubtreeReferencesOrBindsLocal(
                pattern.Value, pattern.LocalIndex)
            && ReferenceOwnership.IsInside(entry.Block, guard.Then)
            && IrFunction.LocalSlotReferencesInScope(
                    function.Body, pattern.LocalIndex)
                .OfType<LoadLocal>()
                .All(load => ReferenceOwnership.IsInside(load, guard.Then))
            && !ReferenceOwnership.RewriteWouldInvalidateLabels(
                function, [guard.Then], []);
    }

    static (Block Block, IrNode? Statement)? FindEntry(
        IrFunction function,
        int index,
        int offset,
        IReadOnlySet<int> branchTargets)
    {
        Block[] blocks = function.DescendantsOutsideNestedFunctions
            .OfType<Block>()
            .Where(block => block.StartOffset == offset)
            .ToArray();
        if (blocks.Length != 0)
        {
            Block? owner = blocks.Length == 1 ? blocks[0] : null;
            if (blocks.Length > 1)
            {
                // A nested arm can inherit the same offset as its enclosing
                // arm; only the unique arm owning the scoped read is an entry.
                Block[] readers = blocks.Where(block =>
                    block.DescendantsOutsideNestedFunctions
                        .OfType<LoadLocal>()
                        .Any(load => load.Index == index
                            && load.SourceOffset == offset)).ToArray();
                if (readers.Length == 1
                    && blocks.All(block => ReferenceEquals(block, readers[0])
                        || ExactLocalNameAllocation.Contains(readers[0], block)))
                    owner = readers[0];
            }
            return owner is not null
                && (branchTargets.Contains(offset)
                    || owner.Parent is not BlockContainer)
                ? (owner, null)
                : null;
        }

        // A dissolved label can survive on a nested operation. A local
        // snapshot can precede earlier work in the statement only when none
        // of that work reads or writes the carrier and the statement does not
        // bind or write it later.
        var statements = function.DescendantsOutsideNestedFunctions
            .OfType<Block>()
            .SelectMany(block => block.Children
                .Where(statement => statement is not LabelAnchor
                    && statement.DescendantsOutsideNestedFunctions
                        .Prepend(statement)
                        .Any(node => node.SourceOffset == offset
                            && node.OwnsSourceLabel)
                    && !IrFunction.LocalSlotReferencesInScope(
                            statement, index)
                        .Any(node => node is not LoadLocal
                            || node.SourceOffset >= 0
                                && node.SourceOffset < offset))
                .Select(statement => (Block: block, Statement: statement)))
            .ToArray();
        return statements.Length == 1 ? statements[0] : null;
    }

    static bool TrialPreservesExactNames(
        IrFunction trial,
        int[] originalGroup,
        int[] carriers,
        int[] projected,
        int[] requireReachingWrite,
        int[] patternCarriers)
    {
        var facts = new DataflowFacts();
        var readBeforeAssign = DefiniteAssignment.Compute(
            trial,
            ReferenceOwnership.CollectBranchTargets(trial),
            facts);
        var projectedBeforeEntry = carriers
            .Select((carrier, position) => (carrier, position))
            .Where(item => requireReachingWrite.Contains(item.carrier))
            .Select(item => projected[item.position]);
        if (facts.Bailed
            || trial.SkipLocalsInit && carriers.Any(readBeforeAssign.Contains)
            || requireReachingWrite.Except(patternCarriers)
                .Any(readBeforeAssign.Contains)
            || projectedBeforeEntry.Any(readBeforeAssign.Contains))
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
        IReadOnlySet<int> branchTargets = context.BranchTargets(function);
        var entries = candidates.ToDictionary(
            carrier => carrier,
            carrier => FindEntry(
                function,
                carrier,
                ExactBinding(function, carrier)!.Scope.StartOffset,
                branchTargets)!.Value);
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

            var (entry, statementAtEntry) = entries[carrier];
            int positionAtEntry = statementAtEntry?.ChildIndex ?? 0;
            var statements = entry.DetachChildren();
            var anchor = new LabelAnchor { RetainsPdbLocalScope = true };
            anchor.SetSourceOffset(binding.Scope.StartOffset);

            var value = new LoadLocal(carrier, type);
            value.SetSourceOffset(binding.Scope.StartOffset);
            var declaration = new StoreLocal(local, type, value)
            {
                PdbScopeEntryProjection = new(carrier, binding),
            };
            declaration.SetSourceOffset(binding.Scope.StartOffset);
            for (int statementIndex = 0; statementIndex <= statements.Count; statementIndex++)
            {
                if (statementIndex == positionAtEntry)
                {
                    entry.Add(anchor);
                    entry.Add(declaration);
                }
                if (statementIndex < statements.Count)
                    entry.Add(statements[statementIndex]);
            }

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
