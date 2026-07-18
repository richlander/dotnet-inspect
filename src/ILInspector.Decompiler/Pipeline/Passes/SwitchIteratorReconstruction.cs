using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs the compiler-produced "switch with shared continuation" iterator
/// shape pinned by #2868. The match is deliberately narrow: an outer iterator
/// state dispatch, a state-0 integer switch over one kickoff parameter, two
/// explicit case arms plus default, and one shared post-switch yield reached by
/// each arm's resume state.
/// </summary>
internal static class SwitchIteratorReconstruction
{
    public static bool TryReconstruct(IrFunction work, IrFunction kickoff, NewObject handoff, out List<IrNode> statements)
    {
        statements = [];
        if (!work.Regions.IsEmpty)
            return false;

        var blocks = work.Body.Blocks;
        if (blocks.Count == 0 || blocks[0].Children is not [StoreLocal stateStore, SwitchBranch dispatch])
            return false;
        if (stateStore.Value is not LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" }
            || dispatch.Value is not LoadLocal dispatchLocal
            || dispatchLocal.Index != stateStore.Index
            || dispatch.TargetOffsets.Length != 6)
        {
            return false;
        }

        var graph = new BlockGraph(blocks);
        var stateBlocks = new Dictionary<int, Block>();
        for (var state = 0; state < dispatch.TargetOffsets.Length; state++)
        {
            if (!graph.TryResolve(dispatch.TargetOffsets[state], out var target))
                return false;
            stateBlocks[state] = target;
        }

        var entry = stateBlocks[0];
        if (!TryReadSwitchEntry(entry, graph, kickoff, out var switchValue, out var switchLocal, out var case0Offset, out var case1Offset, out var defaultOffset))
            return false;
        if (!graph.TryResolve(case0Offset, out var case0Block)
            || !graph.TryResolve(case1Offset, out var case1Block)
            || !graph.TryResolve(defaultOffset, out var defaultBlock))
        {
            return false;
        }

        if (!TryReadYield(case0Block, kickoff, out var case0Value, out var stateAfterCase0)
            || stateAfterCase0 != 1
            || !TryReadResumeToSharedPost(stateBlocks[1], graph, out var postOffset))
        {
            return false;
        }

        if (!TryReadYield(case1Block, kickoff, out var case1FirstValue, out var stateAfterCase1First)
            || stateAfterCase1First != 2
            || !TryReadYield(stateBlocks[2], kickoff, out var case1SecondValue, out var stateAfterCase1Second)
            || stateAfterCase1Second != 3
            || !TryReadResumeToSharedPost(stateBlocks[3], graph, out var case1PostOffset)
            || case1PostOffset != postOffset)
        {
            return false;
        }

        if (!TryReadYield(defaultBlock, kickoff, out var defaultValue, out var stateAfterDefault)
            || stateAfterDefault != 4
            || !TryReadResumeToSharedPost(stateBlocks[4], graph, out var defaultPostOffset)
            || defaultPostOffset != postOffset)
        {
            return false;
        }

        if (!graph.TryResolve(postOffset, out var postBlock)
            || !TryReadYield(postBlock, kickoff, out var postValue, out var finalState)
            || finalState != 5
            || !IsTerminalState(stateBlocks[5]))
        {
            return false;
        }

        if (work.Descendants.OfType<StoreField>()
                .Count(store => store is { Instance: LoadArgument { Index: 0 }, Field.Name: "<>2__current" }) != 5)
        {
            return false;
        }

        var switchNode = new Switch(
            switchValue,
            [
                Section(0, case0Value),
                Section(1, case1FirstValue, case1SecondValue),
                DefaultSection(defaultValue),
            ]);

        Reanchor(switchNode, handoff.SourceOffset);
        var postYield = new YieldReturn(postValue);
        Reanchor(postYield, handoff.SourceOffset);

        statements.Add(switchNode);
        statements.Add(postYield);
        return true;

        static SwitchSection Section(int label, params IrExpression[] yields)
            => new(ImmutableArray.Create(new Constant(label, TypeRef.CoreLib("System", "Int32"))), isDefault: false, Body(yields));

        static SwitchSection DefaultSection(params IrExpression[] yields)
            => new(ImmutableArray<Constant>.Empty, isDefault: true, Body(yields));

        static BlockContainer Body(params IrExpression[] yields)
        {
            var container = new BlockContainer();
            var block = new Block(0);
            foreach (var value in yields)
                block.Add(new YieldReturn(value));
            block.Add(new Break());
            container.Add(block);
            return container;
        }
    }

    static bool TryReadSwitchEntry(Block block, BlockGraph graph, IrFunction kickoff, out IrExpression switchValue, out int switchLocal, out int case0Offset, out int case1Offset, out int defaultOffset)
    {
        switchValue = null!;
        switchLocal = -1;
        case0Offset = case1Offset = defaultOffset = -1;

        var children = block.Children;
        var index = 0;
        if (index >= children.Count || !IsStateStore(children[index++], -1))
            return false;

        if (index >= children.Count || children[index++] is not StoreLocal valueStore)
            return false;
        if (Remap(valueStore.Value, kickoff, EmptyHoisted) is not { } remapped)
            return false;

        switchValue = remapped;
        switchLocal = valueStore.Index;

        if (index + 1 < children.Count
            && children[index] is StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: var spillName, Value: LoadLocal spillSource }
            && GeneratedCodeIdentity.IsGeneratedFieldName(spillName)
            && spillSource.Index == valueStore.Index
            && children[index + 1] is StoreLocal switchTemp
            && switchTemp.Value is LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: var reloadName }
            && reloadName == spillName)
        {
            switchLocal = switchTemp.Index;
            index += 2;
        }

        if (index >= children.Count
            || children[index++] is not ConditionalBranch first
            || !IsZeroCase(first.Condition, switchLocal))
        {
            return false;
        }
        case0Offset = first.TargetOffset;

        if (index != children.Count)
            return false;

        if (NextMeaningfulBlock(block, graph) is not { } secondBlock
            || secondBlock.Children is not [ConditionalBranch second]
            || !IsCase(second.Condition, switchLocal, 1))
        {
            return false;
        }
        case1Offset = second.TargetOffset;

        if (graph.NextBlock(secondBlock) is not { Children: [Branch defaultBranch] })
            return false;
        defaultOffset = defaultBranch.TargetOffset;

        return defaultOffset >= 0;
    }

    static Block? NextMeaningfulBlock(Block block, BlockGraph graph)
    {
        var next = graph.NextBlock(block);
        while (next is { Children: [Branch branch] })
        {
            if (!graph.TryResolve(branch.TargetOffset, out next))
                return null;
        }
        return next;
    }

    static bool TryReadYield(Block block, IrFunction kickoff, out IrExpression value, out int nextState)
    {
        value = null!;
        nextState = -1;

        var children = block.Children;
        var index = 0;
        if (index < children.Count && IsStateStore(children[index], -1))
            index++;
        if (index + 3 != children.Count)
            return false;
        if (children[index++] is not StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>2__current" } current)
            return false;
        if (children[index++] is not StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state", Value: Constant { Value: int state } })
            return false;
        if (children[index] is not Return { Value: Constant { Value: true or 1 } })
            return false;

        if (Remap(current.Value, kickoff, EmptyHoisted) is not { } remapped)
            return false;
        value = remapped;
        nextState = state;
        return true;
    }

    static bool TryReadResumeToSharedPost(Block block, BlockGraph graph, out int postOffset)
    {
        postOffset = -1;
        var children = block.Children;
        if (children.Count == 0 || !IsStateStore(children[0], -1))
            return false;
        if (children.Count == 2 && children[1] is Branch branch)
        {
            postOffset = graph.ResolveOffset(branch.TargetOffset);
            return postOffset >= 0;
        }
        if (children.Count == 1 && graph.NextBlock(block) is { } next)
        {
            postOffset = next.StartOffset;
            return true;
        }
        return false;
    }

    static bool IsTerminalState(Block block)
        => block.Children is [var stateStore, Return { Value: Constant { Value: false or 0 } }]
            && IsStateStore(stateStore, -1);

    static bool IsStateStore(IrNode node, int value)
        => node is StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state", Value: Constant { Value: int state } }
            && state == value;

    static bool IsZeroCase(IrExpression condition, int switchLocal)
        => condition is LogicalNot { Operand: LoadLocal load } && load.Index == switchLocal
            || IsCase(condition, switchLocal, 0);

    static bool IsCase(IrExpression condition, int switchLocal, int value)
        => condition is Comparison { Kind: ComparisonKind.Equal, Left: LoadLocal load, Right: Constant { Value: int constant } }
            && load.Index == switchLocal
            && constant == value;

    static IrExpression? Remap(IrExpression expression, IrFunction kickoff, IReadOnlyDictionary<string, (int Index, TypeRef Type)> hoisted)
    {
        var clone = (IrExpression)expression.Clone();
        var ok = true;
        var swaps = new List<(IrNode Old, IrNode New)>();
        Visit(clone);
        if (!ok)
            return null;

        IrExpression result = clone;
        foreach (var (old, replacement) in swaps)
        {
            if (ReferenceEquals(old, clone))
                result = (IrExpression)replacement;
            else
                old.ReplaceWith(replacement);
        }

        return result;

        void Visit(IrNode node)
        {
            if (!ok)
                return;
            switch (node)
            {
                case LoadField { Instance: LoadArgument { Index: 0 }, Field: var field }:
                    if (hoisted.TryGetValue(field.Name, out var slot))
                        swaps.Add((node, new LoadLocal(slot.Index, field.Type)));
                    else if (TryGetParameter(kickoff, field.Name, out var index, out var parameter))
                        swaps.Add((node, new LoadArgument(index, parameter.Name, parameter.Type)));
                    else
                        ok = false;
                    return;
                case LoadField { Field.Name: var name } when GeneratedCodeIdentity.IsGeneratedFieldName(name):
                case LoadArgument:
                case LoadStackSlot:
                    ok = false;
                    return;
                default:
                    foreach (var child in node.Children)
                        Visit(child);
                    return;
            }
        }
    }

    static readonly IReadOnlyDictionary<string, (int Index, TypeRef Type)> EmptyHoisted =
        new Dictionary<string, (int Index, TypeRef Type)>(StringComparer.Ordinal);

    static bool TryGetParameter(IrFunction kickoff, string name, out int index, out Parameter parameter)
    {
        var parameters = kickoff.Signature.Parameters;
        var argumentBase = kickoff.Signature.HasThis ? 1 : 0;
        for (var i = 0; i < parameters.Length; i++)
            if (parameters[i].Name == name)
            {
                index = argumentBase + i;
                parameter = parameters[i];
                return true;
            }

        index = -1;
        parameter = null!;
        return false;
    }

    static void Reanchor(IrNode node, int offset)
    {
        foreach (var descendant in node.Descendants)
            descendant.SetSourceOffset(-1);
        node.SetSourceOffset(offset >= 0 ? offset : -1);
    }

    sealed class BlockGraph(IReadOnlyList<Block> blocks)
    {
        readonly Dictionary<int, Block> _byOffset = blocks.ToDictionary(static b => b.StartOffset);

        public bool TryResolve(int offset, out Block block)
        {
            var resolved = ResolveOffset(offset);
            if (resolved < 0)
            {
                block = null!;
                return false;
            }
            block = _byOffset[resolved];
            return true;
        }

        public int ResolveOffset(int offset)
        {
            var seen = new HashSet<int>();
            while (_byOffset.TryGetValue(offset, out var block))
            {
                if (!seen.Add(offset))
                    return -1;
                if (block.Children is [Branch branch])
                {
                    offset = branch.TargetOffset;
                    continue;
                }
                return offset;
            }
            return -1;
        }

        public Block? NextBlock(Block block)
        {
            for (var i = 0; i + 1 < blocks.Count; i++)
                if (ReferenceEquals(blocks[i], block))
                    return blocks[i + 1];
            return null;
        }
    }
}
