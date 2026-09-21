namespace ILInspector.Decompiler.Pipeline;

/// <summary>Retains lexical blocks needed to spell disjoint exact local names.</summary>
public sealed class PdbLocalScopePass : IIrPass
{
    public string Name => "pdb-local-scopes";

    public void Run(IrFunction function, PassContext context)
    {
        var duplicates = Enumerable.Range(0, function.LocalNames.Length)
            .Where(index => function.LocalNames[index] is { } name
                && CSharpNaming.IsUsableIdentifier(name))
            .GroupBy(index => function.LocalNames[index]!, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Where(index =>
                !function.EliminatedLocalSlots.Contains(index)
                && IrFunction.LocalSlotReferencesInScope(function, index).Any()).ToArray())
            .Where(group => group.Length > 1)
            .ToArray();
        if (duplicates.Length == 0)
            return;

        var nodeOrder = function.DescendantsOutsideNestedFunctions
            .Select((node, position) => (node, position))
            .ToDictionary(pair => pair.node, pair => pair.position);
        var candidates = duplicates
            .SelectMany(group => group.Select(index =>
            {
                var references =
                    IrFunction.LocalSlotReferencesInScope(function, index).ToArray();
                IrNode? declaration = DeclarationAnchor(references, index);
                return (
                    SameName: group,
                    Index: index,
                    DeclarationOrder: declaration is not null
                        ? nodeOrder[declaration]
                        : references.Select(reference => nodeOrder[reference])
                            .DefaultIfEmpty(-1)
                            .Min(),
                    LastReferenceOrder: references.Select(reference => nodeOrder[reference])
                        .DefaultIfEmpty(-1)
                        .Max());
            }))
            .ToArray();
        foreach (var scopeGroup in candidates
            .Select((candidate, position) => (candidate, position))
            .Where(item => ScopeRow(item.candidate.Index) > 0)
            .GroupBy(item => ScopeRow(item.candidate.Index))
            .Where(group => group.Skip(1).Any()))
        {
            int[] positions = [.. scopeGroup.Select(item => item.position)];
            var insideOut = scopeGroup
                .Select(item => item.candidate)
                .OrderByDescending(candidate => candidate.DeclarationOrder)
                .ThenByDescending(candidate => candidate.LastReferenceOrder)
                .ToArray();
            for (int position = 0; position < positions.Length; position++)
                candidates[positions[position]] = insideOut[position];
        }

        var reserved = ExactLocalNameAllocation.ReservedNames(
            function, function.Signature.Parameters, function.Signature.GenericParameterNames);
        foreach (var candidate in candidates)
        {
            int[] group = candidate.SameName;
            int index = candidate.Index;
            if (reserved.Contains(function.LocalNames[index]!))
                continue;
            if (!function.IsLocalDeclaredInNestedScope(index))
                continue;
            var scopes = LocalDeclarationPlan
                .Create(function, function.Locals.Length)
                .DeclarationScopes;
            if (!group.Any(other => other != index
                && ExactLocalNameAllocation.ScopesOverlap(scopes[index], scopes[other])))
            {
                continue;
            }
            TryRetainBlock(function, index, group, context);
        }

        int ScopeRow(int index)
            => index < function.LocalDeclarationBindings.Length
                && function.LocalDeclarationBindings[index] is { } binding
                ? binding.ScopeRowId
                : -1;
    }

    static void TryRetainBlock(IrFunction function, int index, int[] sameName, PassContext context)
    {
        var references = IrFunction.LocalSlotReferencesInScope(function.Body, index).ToArray();
        if (references.Length == 0)
            return;
        IrNode? declaration = DeclarationAnchor(references, index);
        if (declaration?.Parent is not Block block)
        {
            return;
        }
        int first = declaration.ChildIndex;
        int last = first;
        foreach (var reference in references)
        {
            IrNode? statement = reference;
            while (statement is not null && !ReferenceEquals(statement.Parent, block))
                statement = statement.Parent;
            if (statement is null)
            {
                TryRetainBasicBlockRange(
                    function, index, sameName, declaration, block, references, context);
                return;
            }
            if (statement.ChildIndex < first)
                return;
            last = Math.Max(last, statement.ChildIndex);
        }
        if (first == 0 && last == block.Children.Count - 1
            && block.Parent is not BlockContainer)
        {
            RetainExistingBlockLabels(
                function,
                index,
                sameName,
                declaration,
                block,
                context);
            return;
        }

        var range = block.Children.Skip(first).Take(last - first + 1).ToArray();
        if (!CanRetainRange(
                function,
                index,
                sameName,
                range,
                LocalDeclarationPlan.PdbLocalEntryLabelsPrintedOutside(
                    function,
                    declaration,
                    block)))
        {
            return;
        }

        var statements = block.DetachChildren();
        var lexical = new Block(declaration.SourceOffset);
        for (int position = 0; position < statements.Count; position++)
        {
            if (position == first)
                block.Add(lexical);
            if (position >= first && position <= last)
                lexical.Add(statements[position]);
            else
                block.Add(statements[position]);
        }
        context.Stepper.StepOver($"retain scope for local {index}", lexical);
    }

    static void TryRetainBasicBlockRange(
        IrFunction function,
        int index,
        int[] sameName,
        IrNode declaration,
        Block declarationBlock,
        IrNode[] references,
        PassContext context)
    {
        if (declarationBlock.Parent is not BlockContainer container)
            return;

        Block[] blocks = [.. container.Blocks];
        int firstBlock = declarationBlock.ChildIndex;
        int lastBlock = firstBlock;
        IrNode? lastStatement = null;
        foreach (IrNode reference in references)
        {
            Block? owner = TopLevelBlock(reference, container);
            if (owner is null || owner.ChildIndex < firstBlock)
                return;
            IrNode? statement = StatementInBlock(reference, owner);
            if (statement is null
                || owner.ChildIndex == firstBlock
                    && statement.ChildIndex < declaration.ChildIndex)
            {
                return;
            }
            if (owner.ChildIndex > lastBlock
                || owner.ChildIndex == lastBlock
                    && (lastStatement is null
                        || statement.ChildIndex > lastStatement.ChildIndex))
            {
                lastBlock = owner.ChildIndex;
                lastStatement = statement;
            }
        }
        if (lastBlock <= firstBlock || lastStatement is null)
            return;
        if (!TryCloseRangeWithinPdbScope(
                function,
                index,
                declaration,
                declarationBlock,
                container,
                blocks,
                firstBlock,
                ref lastBlock,
                ref lastStatement))
        {
            return;
        }

        var branchTargets = ReferenceOwnership.CollectBranchTargets(function);
        var retainedLabels = new Dictionary<int, LabelAnchor>();
        for (int blockIndex = firstBlock + 1; blockIndex <= lastBlock; blockIndex++)
        {
            if (!branchTargets.Contains(blocks[blockIndex].StartOffset))
                continue;
            var anchor = new LabelAnchor { RetainsPdbLocalScope = true };
            anchor.SetSourceOffset(blocks[blockIndex].StartOffset);
            retainedLabels.Add(blockIndex, anchor);
        }

        var range = new List<IrNode>();
        range.AddRange(declarationBlock.Children.Skip(declaration.ChildIndex));
        for (int blockIndex = firstBlock + 1; blockIndex < lastBlock; blockIndex++)
        {
            if (retainedLabels.TryGetValue(blockIndex, out LabelAnchor? anchor))
                range.Add(anchor);
            range.AddRange(blocks[blockIndex].Children);
        }
        if (retainedLabels.TryGetValue(lastBlock, out LabelAnchor? rangeLastAnchor))
            range.Add(rangeLastAnchor);
        range.AddRange(blocks[lastBlock].Children.Take(lastStatement.ChildIndex + 1));
        if (!CanRetainRange(
                function,
                index,
                sameName,
                range,
                LocalDeclarationPlan.PdbLocalEntryLabelsPrintedOutside(
                    function,
                    declaration,
                    declarationBlock)))
            return;

        int declarationPosition = declaration.ChildIndex;
        int lastPosition = lastStatement.ChildIndex;
        var statementsByBlock = blocks[firstBlock..(lastBlock + 1)]
            .Select(block => block.DetachChildren())
            .ToArray();
        var allBlocks = container.DetachChildren();

        var lexical = new Block(declaration.SourceOffset);
        var firstStatements = statementsByBlock[0];
        for (int position = declarationPosition; position < firstStatements.Count; position++)
            lexical.Add(firstStatements[position]);
        for (int blockIndex = firstBlock + 1; blockIndex < lastBlock; blockIndex++)
        {
            if (retainedLabels.TryGetValue(blockIndex, out LabelAnchor? anchor))
                lexical.Add(anchor);
            foreach (IrNode statement in statementsByBlock[blockIndex - firstBlock])
                lexical.Add(statement);
        }
        var lastStatements = statementsByBlock[^1];
        if (retainedLabels.TryGetValue(lastBlock, out LabelAnchor? lexicalLastAnchor))
            lexical.Add(lexicalLastAnchor);
        for (int position = 0; position <= lastPosition; position++)
            lexical.Add(lastStatements[position]);

        for (int position = 0; position < declarationPosition; position++)
            declarationBlock.Add(firstStatements[position]);
        declarationBlock.Add(lexical);
        for (int position = lastPosition + 1; position < lastStatements.Count; position++)
            declarationBlock.Add(lastStatements[position]);

        for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            if (blockIndex == firstBlock)
                container.Add(declarationBlock);
            else if (blockIndex > firstBlock && blockIndex <= lastBlock)
                continue;
            else
                container.Add((Block)allBlocks[blockIndex]);
        }
        context.Stepper.StepOver($"retain cross-block scope for local {index}", lexical);
    }

    static bool TryCloseRangeWithinPdbScope(
        IrFunction function,
        int index,
        IrNode declaration,
        Block declarationBlock,
        BlockContainer container,
        Block[] blocks,
        int firstBlock,
        ref int lastBlock,
        ref IrNode lastStatement)
    {
        IReadOnlySet<int>? labelsPrintedOutside =
            LocalDeclarationPlan.PdbLocalEntryLabelsPrintedOutside(
                function,
                declaration,
                declarationBlock);
        while (true)
        {
            List<IrNode> range = BasicBlockRange(
                blocks,
                firstBlock,
                declaration,
                lastBlock,
                lastStatement);
            var bodyLabels = range
                .SelectMany(statement =>
                    statement.DescendantsOutsideNestedFunctions.Prepend(statement))
                .Where(node => node.OwnsSourceLabel && node.SourceOffset >= 0)
                .Select(node => node.SourceOffset)
                .ToHashSet();
            for (int blockIndex = firstBlock + 1; blockIndex <= lastBlock; blockIndex++)
            {
                // An omitted leading instruction can leave the target identity
                // only on a basic block consumed by the lexical range.
                if (blocks[blockIndex].StartOffset >= 0)
                    bodyLabels.Add(blocks[blockIndex].StartOffset);
            }
            if (labelsPrintedOutside is not null)
                bodyLabels.ExceptWith(labelsPrintedOutside);

            var enteringTransfers = function.DescendantsOutsideNestedFunctions
                .Where(transfer => !range.Any(statement =>
                    ExactLocalNameAllocation.Contains(statement, transfer))
                    && ReferenceOwnership.TransferTargets(transfer)
                        .Any(bodyLabels.Contains))
                .ToArray();
            if (enteringTransfers.Length == 0)
                return true;
            if (index >= function.LocalDeclarationBindings.Length
                || function.LocalDeclarationBindings[index] is not { } binding)
            {
                return false;
            }

            int extendedLastBlock = lastBlock;
            IrNode extendedLastStatement = lastStatement;
            foreach (IrNode transfer in enteringTransfers)
            {
                if (transfer.SourceOffset < binding.Scope.StartOffset
                    || transfer.SourceOffset >= binding.Scope.EndOffset)
                {
                    return false;
                }
                Block? owner = TopLevelBlock(transfer, container);
                IrNode? statement = owner is null
                    ? null
                    : StatementInBlock(transfer, owner);
                if (owner is null
                    || statement is null
                    || owner.ChildIndex < lastBlock
                    || owner.ChildIndex == lastBlock
                        && statement.ChildIndex <= lastStatement.ChildIndex)
                {
                    return false;
                }
                if (owner.ChildIndex > extendedLastBlock
                    || owner.ChildIndex == extendedLastBlock
                        && statement.ChildIndex > extendedLastStatement.ChildIndex)
                {
                    extendedLastBlock = owner.ChildIndex;
                    extendedLastStatement = statement;
                }
            }
            if (!ExtensionStaysInsidePdbScope(
                    blocks,
                    lastBlock,
                    lastStatement,
                    extendedLastBlock,
                    extendedLastStatement,
                    binding.Scope))
            {
                return false;
            }
            lastBlock = extendedLastBlock;
            lastStatement = extendedLastStatement;
        }
    }

    static List<IrNode> BasicBlockRange(
        Block[] blocks,
        int firstBlock,
        IrNode declaration,
        int lastBlock,
        IrNode lastStatement)
    {
        var range = new List<IrNode>();
        range.AddRange(blocks[firstBlock].Children.Skip(declaration.ChildIndex));
        for (int blockIndex = firstBlock + 1; blockIndex < lastBlock; blockIndex++)
            range.AddRange(blocks[blockIndex].Children);
        range.AddRange(blocks[lastBlock].Children.Take(lastStatement.ChildIndex + 1));
        return range;
    }

    static bool ExtensionStaysInsidePdbScope(
        Block[] blocks,
        int lastBlock,
        IrNode lastStatement,
        int extendedLastBlock,
        IrNode extendedLastStatement,
        LocalSlotScope scope)
    {
        for (int blockIndex = lastBlock; blockIndex <= extendedLastBlock; blockIndex++)
        {
            int firstPosition = blockIndex == lastBlock
                ? lastStatement.ChildIndex + 1
                : 0;
            int lastPosition = blockIndex == extendedLastBlock
                ? extendedLastStatement.ChildIndex
                : blocks[blockIndex].Children.Count - 1;
            for (int position = firstPosition; position <= lastPosition; position++)
            {
                if (blocks[blockIndex].Children[position]
                    .DescendantsOutsideNestedFunctions
                    .Prepend(blocks[blockIndex].Children[position])
                    .Any(node => node.SourceOffset >= 0
                        && (node.SourceOffset < scope.StartOffset
                            || node.SourceOffset >= scope.EndOffset)))
                {
                    return false;
                }
            }
        }
        return true;
    }

    static void RetainExistingBlockLabels(
        IrFunction function,
        int index,
        int[] sameName,
        IrNode declaration,
        Block block,
        PassContext context)
    {
        IrNode[] range = [.. block.Children];
        if (!CanRetainRange(
                function,
                index,
                sameName,
                range,
                LocalDeclarationPlan.PdbLocalEntryLabelsPrintedOutside(
                    function,
                    declaration,
                    block)))
        {
            return;
        }

        HashSet<int> branchTargets = ReferenceOwnership.CollectBranchTargets(function);
        int[] targetedLabels = block.Children
            .Skip(declaration.ChildIndex + 1)
            .SelectMany(statement =>
                statement.DescendantsOutsideNestedFunctions.Prepend(statement))
            .Where(node => node.OwnsSourceLabel
                && node.SourceOffset >= 0
                && branchTargets.Contains(node.SourceOffset))
            .Select(node => node.SourceOffset)
            .Distinct()
            .ToArray();
        if (targetedLabels.Length == 0)
            return;

        var anchors = block.Children
            .OfType<LabelAnchor>()
            .Where(anchor => targetedLabels.Contains(anchor.SourceOffset))
            .ToDictionary(anchor => anchor.SourceOffset);
        if (targetedLabels.Any(target => !anchors.ContainsKey(target)))
            return;

        foreach (var anchor in anchors.Values)
            anchor.RetainsPdbLocalScope = true;
        context.Stepper.StepOver($"retain existing scope for local {index}", block);
    }

    static bool CanRetainRange(
        IrFunction function,
        int index,
        int[] sameName,
        IReadOnlyList<IrNode> range,
        IReadOnlySet<int>? bodyLabelsRetainedOutside)
    {
        if (ReferenceOwnership.RewriteWouldInvalidateLabels(
                function,
                range,
                [],
                bodyLabelsRetainedOutside))
            return false;
        bool Inside(IrNode node) => range.Any(
            statement => ExactLocalNameAllocation.Contains(statement, node));
        if (sameName.Any(other => other != index
            && IrFunction.LocalSlotReferencesInScope(function.Body, other).Any(Inside)))
        {
            return false;
        }
        // Header/pattern binders have an intrinsic declaration scope. A wrapper
        // must not strand one of their surviving uses outside that scope.
        for (int other = 0; other < function.Locals.Length; other++)
        {
            var uses = IrFunction.LocalSlotReferencesInScope(function.Body, other).ToArray();
            if (uses.Any(node => Inside(node)
                    && node is not (StoreLocal or LoadLocal or LoadLocalAddress))
                && uses.Any(node => !Inside(node)))
            {
                return false;
            }
        }
        return true;
    }

    static Block? TopLevelBlock(IrNode node, BlockContainer container)
    {
        while (node.Parent is not null && !ReferenceEquals(node.Parent, container))
            node = node.Parent;
        return node is Block block && ReferenceEquals(block.Parent, container)
            ? block
            : null;
    }

    static IrNode? StatementInBlock(IrNode node, Block block)
    {
        while (node.Parent is not null && !ReferenceEquals(node.Parent, block))
            node = node.Parent;
        return ReferenceEquals(node.Parent, block) ? node : null;
    }

    static IrNode? DeclarationAnchor(IrNode[] references, int index)
        => references[0] switch
        {
            StoreLocal store when !store.Value.DescendantsAndSelfOutsideNestedFunctions.Any(node =>
                node is LoadLocal load && load.Index == index
                || node is LoadLocalAddress address && address.Index == index) => store,
            LoadLocalAddress address when address.Parent is InitObject init
                && ReferenceEquals(init.Address, address) => init,
            LoadLocalAddress address => OutArgumentStatement(address, index),
            IsPattern pattern => PatternStatement(pattern, references),
            RecursivePropertyDeclarationPattern pattern => PatternStatement(pattern, references),
            _ => null,
        };

    static IrNode? OutArgumentStatement(LoadLocalAddress address, int index)
    {
        MethodRef? callee;
        int parameterIndex;
        switch (address.Parent)
        {
            case Call call:
                callee = call.Callee;
                parameterIndex = address.ChildIndex - (callee.HasThis ? 1 : 0);
                break;
            case NewObject creation:
                callee = creation.Constructor;
                parameterIndex = address.ChildIndex;
                break;
            default:
                return null;
        }
        return callee.TryGetVerifiedOutLocal(parameterIndex, address, out int local)
            && local == index
                ? StatementInBlock(address)
                : null;
    }

    static IrNode? StatementInBlock(IrNode node)
    {
        while (node.Parent is not null and not Block)
            node = node.Parent;
        return node.Parent is Block ? node : null;
    }

    static IrNode? PatternStatement(IrNode pattern, IrNode[] references)
    {
        IrNode? statement = StatementInBlock(pattern);
        return statement is not null
            && references.All(reference =>
                ExactLocalNameAllocation.Contains(statement, reference))
                ? statement
                : null;
    }

    internal static IReadOnlyList<IrNode> WithoutLexicalBlocks(IReadOnlyList<IrNode> statements)
    {
        if (!statements.Any(statement => statement is Block))
            return statements;
        var flattened = new List<IrNode>();
        foreach (var statement in statements)
        {
            if (statement is Block block)
                flattened.AddRange(WithoutLexicalBlocks(block.Children));
            else
                flattened.Add(statement);
        }
        return flattened;
    }
}
