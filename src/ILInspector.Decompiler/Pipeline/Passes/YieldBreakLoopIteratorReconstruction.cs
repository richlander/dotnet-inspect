namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs the compiler-produced <c>while (true)</c> iterator loop with an
/// explicit conditional <c>yield break</c>. The discriminator is the loop header
/// that falls through to terminal <c>return false</c> before the yield block;
/// ordinary counting iterators keep their direct branch-to-condition shape and
/// remain owned by <see cref="CountingLoopReconstruction"/>.
/// </summary>
internal static class YieldBreakLoopIteratorReconstruction
{
    public static bool TryReconstruct(IrFunction work, IrFunction kickoff, NewObject handoff, out List<IrNode> statements)
    {
        statements = [];
        if (!work.Regions.IsEmpty)
            return false;

        var blocks = work.Body.Blocks;
        if (blocks.Count < 7
            || blocks[0].Children.Count < 2
            || blocks[0].Children[0] is not StoreLocal { Value: LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" } } stateStore)
        {
            return false;
        }

        var graph = new BlockGraph(blocks);
        if (!TryResolveStateTarget(blocks[0].Children[1], stateStore.Index, state: 0, graph, out var initBlock)
            || !TryFindStateDispatch(blocks, stateStore.Index, state: 1, graph, out var resumeBlock))
        {
            return false;
        }

        if (!TryReadInit(initBlock, graph, out var loopField, out var initValue, out var conditionBlock))
            return false;
        if (!TryReadCondition(conditionBlock, graph, loopField.Name, out var continueCondition, out var yieldBlock, out var terminalBlock))
            return false;
        if (!IsReturnFalse(terminalBlock))
            return false;
        if (!TryReadYield(yieldBlock, kickoff, loopField.Name, out var yieldValue, out var resumeState)
            || resumeState != 1)
        {
            return false;
        }
        if (!TryReadResume(resumeBlock, graph, loopField.Name, conditionBlock.StartOffset, out var increment))
            return false;

        var expectedYieldCount = work.Descendants.OfType<StoreField>()
            .Count(store => store is { Instance: LoadArgument { Index: 0 }, Field.Name: "<>2__current" });
        if (expectedYieldCount != 1)
            return false;

        var local = kickoff.Locals.Length;
        if (Remap(initValue, loopField.Name, local, loopField.Type, kickoff) is not { } init
            || Remap(continueCondition, loopField.Name, local, loopField.Type, kickoff) is not { } continueGuard
            || Remap(yieldValue, loopField.Name, local, loopField.Type, kickoff) is not { } element)
        {
            return false;
        }

        kickoff.AddLocal(loopField.Type, ExtractSourceName(loopField.Name));

        var declaration = new StoreLocal(local, loopField.Type, init);
        var body = new Block(0);
        var breakBody = new Block(0);
        breakBody.Add(new YieldBreak());
        body.Add(new IfStatement(Negate(continueGuard), breakBody, elseArm: null));
        body.Add(new YieldReturn(element));
        body.Add(new StoreLocal(local, loopField.Type,
            new Binary(increment.Kind, increment.IsChecked, increment.IsUnsigned,
                new LoadLocal(local, loopField.Type),
                new Constant(increment.Step.Value, increment.Step.Type))));
        var loop = new WhileLoop(new Constant(true, TypeRef.CoreLib("System", "Boolean")), body);

        Reanchor(declaration, handoff.SourceOffset);
        Reanchor(loop, handoff.SourceOffset);
        statements.Add(declaration);
        statements.Add(loop);
        return true;
    }

    static bool TryResolveStateTarget(IrNode node, int stateLocal, int state, BlockGraph graph, out Block target)
    {
        target = null!;
        if (node is ConditionalBranch branch && IsState(branch.Condition, stateLocal, state))
            return graph.TryResolve(branch.TargetOffset, out target);
        return false;
    }

    static bool TryFindStateDispatch(IReadOnlyList<Block> blocks, int stateLocal, int state, BlockGraph graph, out Block target)
    {
        target = null!;
        foreach (var block in blocks)
            foreach (var node in block.Children)
                if (node is ConditionalBranch branch
                    && IsState(branch.Condition, stateLocal, state)
                    && graph.TryResolve(branch.TargetOffset, out target))
                {
                    return true;
                }
        return false;
    }

    static bool TryReadInit(Block block, BlockGraph graph, out FieldRef loopField, out IrExpression initValue, out Block conditionBlock)
    {
        loopField = null!;
        initValue = null!;
        conditionBlock = null!;

        if (block.Children.Count < 2
            || !IsStateStore(block.Children[0], -1)
            || block.Children[1] is not StoreField { Instance: LoadArgument { Index: 0 }, Field: var field } initStore
            || !GeneratedCodeIdentity.IsHoistedLocalFieldName(field.Name))
        {
            return false;
        }

        loopField = field;
        initValue = initStore.Value;
        if (block.Children.Count == 3 && block.Children[2] is Branch branch)
            return graph.TryResolve(branch.TargetOffset, out conditionBlock);
        if (block.Children.Count == 2 && graph.NextBlock(block) is { } next)
        {
            conditionBlock = next;
            return true;
        }
        return false;
    }

    static bool TryReadCondition(Block start, BlockGraph graph, string loopFieldName,
        out IrExpression continueCondition, out Block yieldBlock, out Block terminalBlock)
    {
        continueCondition = null!;
        yieldBlock = null!;
        terminalBlock = null!;

        var block = start;
        var seen = new HashSet<int>();
        while (true)
        {
            if (!seen.Add(block.StartOffset))
                return false;

            if (TryReadConditionBranch(block, loopFieldName, out continueCondition, out var yieldOffset))
            {
                if (!graph.TryResolve(yieldOffset, out yieldBlock))
                    return false;
                terminalBlock = graph.NextBlock(block)!;
                return terminalBlock is not null;
            }

            if (block.Children is [StoreLocal { Value: Constant { Value: true or 1 } }, Branch nextBranch])
            {
                if (!graph.TryResolve(nextBranch.TargetOffset, out block))
                    return false;
                continue;
            }

            return false;
        }
    }

    static bool TryReadConditionBranch(Block block, string loopFieldName, out IrExpression continueCondition, out int yieldOffset)
    {
        continueCondition = null!;
        yieldOffset = -1;

        if (block.Children is [ConditionalBranch direct]
            && TryNormalizeContinueCondition(direct.Condition, loopFieldName, out continueCondition))
        {
            yieldOffset = direct.TargetOffset;
            return true;
        }

        if (block.Children is [StoreLocal conditionStore, ConditionalBranch branch]
            && branch.Condition is LogicalNot { Operand: LoadLocal load }
            && load.Index == conditionStore.Index
            && TryNormalizeBreakCondition(conditionStore.Value, loopFieldName, out var breakCondition))
        {
            continueCondition = Negate(breakCondition);
            yieldOffset = branch.TargetOffset;
            return true;
        }

        return false;
    }

    static bool TryNormalizeContinueCondition(IrExpression expression, string loopFieldName, out IrExpression condition)
    {
        condition = null!;
        return expression switch
        {
            Comparison comparison when ReadsLoopField(comparison.Left, loopFieldName) => Assign(comparison, out condition),
            LogicalNot { Operand: Comparison comparison } when ReadsLoopField(comparison.Left, loopFieldName)
                => Assign(Negate(comparison), out condition),
            _ => false,
        };
    }

    static bool TryNormalizeBreakCondition(IrExpression expression, string loopFieldName, out IrExpression condition)
    {
        condition = null!;
        if (expression is Comparison { Kind: ComparisonKind.Equal, Left: Comparison inner, Right: Constant { Value: false or 0 } }
            && ReadsLoopField(inner.Left, loopFieldName))
        {
            return Assign(Negate(inner), out condition);
        }
        if (expression is Comparison comparison && ReadsLoopField(comparison.Left, loopFieldName))
            return Assign(comparison, out condition);
        return false;
    }

    static bool TryReadYield(Block block, IrFunction kickoff, string loopFieldName, out IrExpression value, out int nextState)
    {
        value = null!;
        nextState = -1;
        if (block.Children is not [
                StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>2__current" } current,
                StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state", Value: Constant { Value: int state } },
                Return { Value: Constant { Value: true or 1 } }])
        {
            return false;
        }

        value = current.Value;
        nextState = state;
        return true;
    }

    static bool TryReadResume(Block block, BlockGraph graph, string loopFieldName, int conditionOffset, out Increment increment)
    {
        increment = default;
        var children = block.Children;
        if (children.Count < 2 || !IsStateStore(children[0], -1))
            return false;

        var store = children.OfType<StoreField>().LastOrDefault(s =>
            s is { Instance: LoadArgument { Index: 0 }, Field.Name: var name } && name == loopFieldName);
        if (store?.Value is not Binary binary || binary.Right is not Constant step)
            return false;
        if (!IncrementReadsLoopField(children, binary.Left, loopFieldName))
            return false;

        if (children[^1] is Branch branch)
            return graph.ResolveOffset(branch.TargetOffset) == conditionOffset
                && Assign(new Increment(binary.Kind, binary.IsChecked, binary.IsUnsigned, step), out increment);
        if (graph.NextBlock(block)?.StartOffset == conditionOffset)
            return Assign(new Increment(binary.Kind, binary.IsChecked, binary.IsUnsigned, step), out increment);
        return false;
    }

    static bool IncrementReadsLoopField(IReadOnlyList<IrNode> resumeChildren, IrExpression left, string loopFieldName)
    {
        if (IsLoopFieldLoad(left, loopFieldName))
            return true;

        if (left is not LoadLocal temp)
            return false;
        return resumeChildren.Any(node => node is StoreLocal { Index: var stored, Value: var value }
                && stored == temp.Index
                && IsLoopFieldLoad(value, loopFieldName));
    }

    static bool IsLoopFieldLoad(IrExpression expression, string loopFieldName)
        => expression is LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: var name }
            && name == loopFieldName;

    static bool ReadsLoopField(IrExpression expression, string loopFieldName)
        => expression.Descendants.Prepend(expression).Any(node => IsLoopFieldLoad((node as IrExpression)!, loopFieldName));

    static IrExpression? Remap(IrExpression expr, string loopFieldName, int local, TypeRef loopType, IrFunction kickoff)
    {
        switch (expr)
        {
            case LoadField { Instance: LoadArgument { Index: 0 }, Field: var field }:
                if (field.Name == loopFieldName)
                    return new LoadLocal(local, loopType);
                if (TryGetParameter(kickoff, field.Name, out var index, out var parameter))
                    return new LoadArgument(index, parameter.Name, parameter.Type);
                return null;
            case Constant constant:
                return new Constant(constant.Value, constant.Type);
            case Comparison comparison:
                if (Remap(comparison.Left, loopFieldName, local, loopType, kickoff) is not { } left
                    || Remap(comparison.Right, loopFieldName, local, loopType, kickoff) is not { } right)
                    return null;
                return new Comparison(comparison.Kind, comparison.IsUnsigned, left, right);
            case Binary binary:
                if (Remap(binary.Left, loopFieldName, local, loopType, kickoff) is not { } bl
                    || Remap(binary.Right, loopFieldName, local, loopType, kickoff) is not { } br)
                    return null;
                return new Binary(binary.Kind, binary.IsChecked, binary.IsUnsigned, bl, br);
            case LogicalNot not:
                return Remap(not.Operand, loopFieldName, local, loopType, kickoff) is { } operand
                    ? new LogicalNot(operand)
                    : null;
            default:
                return null;
        }
    }

    static IrExpression Negate(IrExpression expression)
        => expression is Comparison comparison
            ? new Comparison(Invert(comparison.Kind), comparison.IsUnsigned,
                (IrExpression)comparison.Left.Clone(), (IrExpression)comparison.Right.Clone())
            : new LogicalNot((IrExpression)expression.Clone());

    static ComparisonKind Invert(ComparisonKind kind) => kind switch
    {
        ComparisonKind.Equal => ComparisonKind.NotEqual,
        ComparisonKind.NotEqual => ComparisonKind.Equal,
        ComparisonKind.LessThan => ComparisonKind.GreaterThanOrEqual,
        ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThan,
        ComparisonKind.GreaterThan => ComparisonKind.LessThanOrEqual,
        ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThan,
        _ => kind,
    };

    static bool IsState(IrExpression condition, int stateLocal, int state)
    {
        if (state == 0)
        {
            return condition is LogicalNot { Operand: LoadLocal zeroLoad } && zeroLoad.Index == stateLocal
                || condition is Comparison { Kind: ComparisonKind.Equal, Left: LoadLocal explicitZeroLoad, Right: Constant { Value: 0 } }
                    && explicitZeroLoad.Index == stateLocal;
        }

        return condition is Comparison { Kind: ComparisonKind.Equal, Left: LoadLocal stateLoad, Right: Constant { Value: int value } }
            && stateLoad.Index == stateLocal
            && value == state;
    }

    static bool IsStateStore(IrNode node, int value)
        => node is StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state", Value: Constant { Value: int state } }
            && state == value;

    static bool IsReturnFalse(Block block)
        => block.Children is [Return { Value: Constant { Value: false or 0 } }];

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

    static string ExtractSourceName(string fieldName)
    {
        var close = fieldName.IndexOf('>');
        return close > 1 ? fieldName[1..close] : "i";
    }

    static bool Assign<T>(T value, out T target)
    {
        target = value;
        return true;
    }

    static void Reanchor(IrNode node, int offset)
    {
        foreach (var descendant in node.Descendants)
            descendant.SetSourceOffset(-1);
        node.SetSourceOffset(offset >= 0 ? offset : -1);
    }

    readonly record struct Increment(BinaryKind Kind, bool IsChecked, bool IsUnsigned, Constant Step);

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
