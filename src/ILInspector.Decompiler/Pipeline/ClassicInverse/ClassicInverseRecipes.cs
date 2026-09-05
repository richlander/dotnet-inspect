using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The closed set of accepted classic-async recipes. Each is an inverse of one
/// named Roslyn lowering shell, and each publishes the claims, protocol
/// declarations, and control regions that
/// <see cref="ClassicInverseAccountant"/> re-checks. A recipe never licenses
/// its own result: matching only produces a candidate.
/// <para>
/// The preconditions below are mutually exclusive by construction (presence of
/// a <c>try/finally</c>, of the compiler's <c>&lt;&gt;7__wrap</c> foreach
/// triple, of a two-armed conditional temporary, and the arity of the
/// builder's completion call). The core still detects a multiple match and
/// declines, so registration order can never decide an outcome.
/// </para>
/// </summary>
internal static class ClassicInverseRecipes
{
    internal sealed class RecipeIndex
    {
        readonly record struct IndexedNode(int Position, IrNode Node);

        readonly List<IndexedNode> _nodes = [];
        readonly List<int> _subtreeEnds = [];
        readonly Dictionary<IrNode, int> _positions =
            new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<IrNode, Block?> _enclosingBlocks =
            new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<Call, IrExpression> _awaitedOperands =
            new(ReferenceEqualityComparer.Instance);
        readonly List<IndexedNode> _storeLocals = [];
        readonly List<IndexedNode> _storeFields = [];
        readonly List<IndexedNode> _expressionStatements = [];
        readonly List<IndexedNode> _conditionalBranches = [];
        readonly List<IndexedNode> _branches = [];
        readonly List<IndexedNode> _storeStackSlots = [];
        readonly List<IndexedNode> _loadStackSlots = [];
        readonly List<IndexedNode> _tryFinallys = [];
        readonly List<IndexedNode> _ifStatements = [];

        RecipeIndex() { }

        internal Call? SetResult { get; private set; }

        internal List<Call> GetResults { get; private set; } = [];

        internal bool HasTryFinally { get; private set; }

        internal bool HasForeachHoist { get; private set; }

        internal int NodeCount => _nodes.Count;

        /// <summary>
        /// Await operands discovered by the same charged snapshot. A consumer
        /// must still charge its lookup; <see cref="AwaitedOperand"/> does so.
        /// </summary>
        internal IReadOnlyDictionary<Call, IrExpression> AwaitedOperands =>
            _awaitedOperands;

        /// <summary>
        /// Captures the planning body once in preorder. Construction charges
        /// once when a node is admitted and once when its subtree interval is
        /// closed. A typed query charges its range lookup, every lower-bound
        /// probe, and every indexed entry it inspects; direct containment,
        /// enclosing-block, and await-operand questions charge one unit each.
        /// </summary>
        internal static RecipeIndex? Build(
            IrFunction execution,
            ClassicInverseShellFacts shell,
            ClassicInverseBudget budget)
        {
            Call? setResult = null;
            var getResults = new List<Call>();
            bool hasTryFinally = false;
            bool hasForeachHoist = false;
            var open = new Stack<int>();
            var awaiterBinds = new Dictionary<int, IrExpression>();
            var index = new RecipeIndex();

            foreach (IrNode node in execution.Body.Descendants.Prepend(execution.Body))
            {
                if (!budget.Charge())
                    return null;

                int position = index._nodes.Count;
                while (open.Count > 0
                    && !ReferenceEquals(
                        node.Parent,
                        index._nodes[open.Peek()].Node))
                {
                    if (!budget.Charge())
                        return null;
                    index._subtreeEnds[open.Pop()] = position;
                }

                index._positions[node] = position;
                index._nodes.Add(new IndexedNode(position, node));
                index._subtreeEnds.Add(-1);
                Block? enclosing = node as Block;
                if (enclosing is null && node.Parent is { } parent)
                    index._enclosingBlocks.TryGetValue(parent, out enclosing);
                index._enclosingBlocks[node] = enclosing;
                index.AddTyped(position, node);
                open.Push(position);

                switch (node)
                {
                    case Call call:
                        if (call.Callee.Name == "SetResult"
                            && ClassicInverseNodeFacts.IsAsyncMethodBuilder(
                                call.Callee.DeclaringType))
                        {
                            setResult = call;
                        }
                        if (ClassicInverseRealizationRules.IsAwaiterGetResult(
                                call,
                                shell))
                        {
                            getResults.Add(call);
                            if (call.Arguments is [LoadLocalAddress address]
                                && awaiterBinds.TryGetValue(
                                    address.Index,
                                    out IrExpression? awaitedOperand))
                            {
                                index._awaitedOperands[call] = awaitedOperand;
                            }
                        }
                        break;

                    case StoreLocal
                    {
                        Value: Call
                        {
                            Callee.Name: "GetAwaiter",
                            Arguments: [IrExpression boundOperand],
                        },
                    } bind:
                        awaiterBinds[bind.Index] = boundOperand;
                        break;

                    case TryFinally:
                        hasTryFinally = true;
                        break;

                    case LoadField { Field.Name: var loadFieldName }
                        when loadFieldName.StartsWith(
                            "<>7__wrap",
                            StringComparison.Ordinal):
                    case StoreField { Field.Name: var storeFieldName }
                        when storeFieldName.StartsWith(
                            "<>7__wrap",
                            StringComparison.Ordinal):
                        hasForeachHoist = true;
                        break;
                }
            }

            while (open.Count > 0)
            {
                if (!budget.Charge())
                    return null;
                index._subtreeEnds[open.Pop()] = index._nodes.Count;
            }

            index.SetResult = setResult;
            index.GetResults = getResults;
            index.HasTryFinally = hasTryFinally;
            index.HasForeachHoist = hasForeachHoist;
            return index;
        }

        internal bool TryFind<T>(
            IrNode root,
            Func<T, bool> predicate,
            ClassicInverseBudget budget,
            out List<T> matches)
            where T : IrNode
        {
            matches = [];
            if (!TryRange(root, budget, out int start, out int end))
                return false;

            List<IndexedNode> entries = EntriesFor<T>();
            int entryIndex = 0;
            if (start != 0
                && !TryLowerBound(entries, start, budget, out entryIndex))
            {
                return false;
            }

            for (; entryIndex < entries.Count; entryIndex++)
            {
                if (!budget.Charge())
                {
                    matches = [];
                    return false;
                }

                IndexedNode entry = entries[entryIndex];
                if (entry.Position >= end)
                    break;
                T node = (T)entry.Node;
                bool matched = predicate(node);
                if (budget.Exhausted)
                {
                    matches = [];
                    return false;
                }
                if (matched)
                    matches.Add(node);
            }
            return true;
        }

        internal bool Contains(
            IrNode root,
            IrNode target,
            ClassicInverseBudget budget)
        {
            if (!budget.Charge())
                return false;
            return _positions.TryGetValue(root, out int rootPosition)
                && _positions.TryGetValue(target, out int targetPosition)
                && targetPosition >= rootPosition
                && targetPosition < _subtreeEnds[rootPosition];
        }

        internal IrExpression? AwaitedOperand(
            Call getResult,
            ClassicInverseBudget budget)
            => budget.Charge()
                ? _awaitedOperands.GetValueOrDefault(getResult)
                : null;

        internal Block? EnclosingBlock(
            IrNode node,
            ClassicInverseBudget budget)
            => budget.Charge()
                ? _enclosingBlocks.GetValueOrDefault(node)
                : null;

        void AddTyped(int position, IrNode node)
        {
            var indexed = new IndexedNode(position, node);
            switch (node)
            {
                case StoreLocal:
                    _storeLocals.Add(indexed);
                    break;
                case StoreField:
                    _storeFields.Add(indexed);
                    break;
                case ExpressionStatement:
                    _expressionStatements.Add(indexed);
                    break;
                case ConditionalBranch:
                    _conditionalBranches.Add(indexed);
                    break;
                case Branch:
                    _branches.Add(indexed);
                    break;
                case StoreStackSlot:
                    _storeStackSlots.Add(indexed);
                    break;
                case LoadStackSlot:
                    _loadStackSlots.Add(indexed);
                    break;
                case TryFinally:
                    _tryFinallys.Add(indexed);
                    break;
                case IfStatement:
                    _ifStatements.Add(indexed);
                    break;
            }
        }

        List<IndexedNode> EntriesFor<T>()
            where T : IrNode
        {
            Type type = typeof(T);
            if (type == typeof(IrNode))
                return _nodes;
            if (type == typeof(StoreLocal))
                return _storeLocals;
            if (type == typeof(StoreField))
                return _storeFields;
            if (type == typeof(ExpressionStatement))
                return _expressionStatements;
            if (type == typeof(ConditionalBranch))
                return _conditionalBranches;
            if (type == typeof(Branch))
                return _branches;
            if (type == typeof(StoreStackSlot))
                return _storeStackSlots;
            if (type == typeof(LoadStackSlot))
                return _loadStackSlots;
            if (type == typeof(TryFinally))
                return _tryFinallys;
            if (type == typeof(IfStatement))
                return _ifStatements;
            throw new NotSupportedException(
                $"Recipe index does not contain nodes of type {type.Name}.");
        }

        bool TryRange(
            IrNode root,
            ClassicInverseBudget budget,
            out int start,
            out int end)
        {
            start = -1;
            end = -1;
            if (!budget.Charge()
                || !_positions.TryGetValue(root, out start))
            {
                return false;
            }
            end = _subtreeEnds[start];
            return true;
        }

        static bool TryLowerBound(
            IReadOnlyList<IndexedNode> entries,
            int position,
            ClassicInverseBudget budget,
            out int result)
        {
            int low = 0;
            int high = entries.Count;
            while (low < high)
            {
                if (!budget.Charge())
                {
                    result = -1;
                    return false;
                }
                int middle = low + ((high - low) / 2);
                if (entries[middle].Position < position)
                    low = middle + 1;
                else
                    high = middle;
            }
            result = low;
            return true;
        }
    }

    internal static List<ClassicInverseCandidate> Match(
        ClassicInverseRequest request,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        ClassicInverseBudget budget)
    {
        var candidates = new List<ClassicInverseCandidate>();
        IrFunction execution = planning.ExecutionBody;
        RecipeIndex? recipeIndex = RecipeIndex.Build(execution, shell, budget);
        if (recipeIndex is null)
            return candidates;

        Call? setResult = recipeIndex.SetResult;
        if (setResult is null)
            return candidates;
        if (!ClassicInverseNodeFacts.IsAsyncMethodBuilder(
                setResult.Callee.DeclaringType))
        {
            return candidates;
        }

        Add(candidates, TryTryFinally(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            recipeIndex.GetResults,
            recipeIndex,
            budget));
        if (budget.Exhausted)
            return candidates;
        Add(candidates, TryLoop(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            recipeIndex.GetResults,
            recipeIndex,
            budget));
        if (budget.Exhausted)
            return candidates;
        Add(candidates, TryConditional(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            recipeIndex.GetResults,
            recipeIndex,
            budget));
        if (budget.Exhausted)
            return candidates;
        Add(candidates, TrySequentialVoid(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            recipeIndex.GetResults,
            recipeIndex,
            budget));
        if (budget.Exhausted)
            return candidates;
        Add(candidates, TrySingleAwaitVoid(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            recipeIndex.GetResults,
            recipeIndex,
            budget));
        if (budget.Exhausted)
            return candidates;
        Add(candidates, TrySingleAwaitReturn(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            recipeIndex.GetResults,
            recipeIndex,
            budget));
        return candidates;
    }

    static void Add(
        List<ClassicInverseCandidate> candidates,
        ClassicInverseCandidate? candidate)
    {
        if (candidate is not null)
            candidates.Add(candidate);
    }

    static string SourceName(string fieldName)
    {
        int close = fieldName.IndexOf('>');
        return close > 1 ? fieldName[1..close] : "value";
    }

    static bool IsTaskLike(TypeRef type)
    {
        TypeRef definition = ClassicInverseNodeFacts.Definition(type);
        return definition is
        {
            Namespace: "System.Threading.Tasks",
            Name: "Task" or "Task`1" or "ValueTask" or "ValueTask`1",
        };
    }

    // ---- Recipe: return await -------------------------------------------

    static ClassicInverseCandidate? TrySingleAwaitReturn(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        RecipeIndex recipeIndex,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments is not [_, LoadLocal result]
            || getResults.Count != 1
            || recipeIndex.HasTryFinally
            || recipeIndex.HasForeachHoist)
        {
            return null;
        }

        if (!recipeIndex.TryFind<StoreLocal>(
                execution.Body,
                candidate =>
                    candidate.Index == result.Index
                    && recipeIndex.Contains(
                        candidate.Value,
                        getResults[0],
                        budget),
                budget,
                out List<StoreLocal> stores))
        {
            return null;
        }
        StoreLocal? store = stores.Count == 0 ? null : stores[^1];
        if (store is null)
        {
            return TryNamedAwaitReturn(rawExecution, planning, shell, setResult,
                result, getResults[0], recipeIndex, budget);
        }
        if (!ProvesCompletionTransfer(
                rawExecution,
                store,
                setResult,
                budget))
            return null;

        var candidate = new ClassicInverseCandidate("classic-await-return")
        {
            ResultLocal = result.Index,
        };
        if (IsMemberReceiver(getResults[0]) && store.Parent is Block continuation)
        {
            bool proven = TryRawAwaitReceiver(rawExecution, continuation, store, setResult,
                getResults[0], budget, out StoreLocal? rawReceiver,
                out StoreLocal rawProjection, out IrNode? rawUse);
            if (rawReceiver is not null)
            {
                if (!proven
                    || rawUse is not LoadLocalAddress rawAddress
                    || rawReceiver.Index < rawExecution.LocalNames.Length
                        && rawExecution.LocalNames[rawReceiver.Index] is not null
                    || !TypeFamilies.IsKnownNonNullableValueType(rawReceiver.Type, rawExecution.TypeShapes)
                    || !ClassicInverseExpressionRules.SameTree(rawReceiver.Value, getResults[0], budget)
                    || !ClassicInverseExpressionRules.SameTree(rawProjection.Value, store.Value, budget,
                        rawAddress, getResults[0]))
                {
                    return null;
                }
                candidate.InlinedAwaitReceiver = new(rawReceiver, rawAddress, getResults[0]);
            }
        }
        var rewriter = new ClassicInverseRewriter(
            planning,
            shell,
            candidate,
            budget,
            recipeIndex.AwaitedOperands);
        IrNode? value = rewriter.Rewrite(store.Value);
        if (value is not IrExpression expression)
            return null;

        var ret = new Return(expression);
        candidate.Statements.Add(ret);
        candidate.Claim(store, ret, ClassicInverseRealizationRule.ResultStore);
        return candidate;
    }

    static ClassicInverseCandidate? TryNamedAwaitReturn(
        IrFunction raw,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        LoadLocal completionResult,
        Call getResult,
        RecipeIndex index,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (getResult.Parent is not StoreLocal resultStore
            || !ReferenceEquals(resultStore.Value, getResult)
            || resultStore.Index == completionResult.Index
            || resultStore.Index < 0
            || resultStore.Index >= execution.LocalNames.Length
            || execution.LocalNames[resultStore.Index] is not { } name
            || resultStore.Index >= raw.LocalNames.Length
            || raw.LocalNames[resultStore.Index] != name
            || !Equals(resultStore.Type, getResult.Callee.ReturnType)
            || resultStore.Parent is not Block continuation
            || continuation.Children is not [var first, StoreLocal returnStore]
            || !ReferenceEquals(first, resultStore)
            || returnStore.Index != completionResult.Index
            || !ProvesCompletionTransfer(raw, returnStore, setResult, budget)
            || !index.TryFind<IrNode>(execution.Body,
                node => node is StoreLocal or LoadLocal or LoadLocalAddress,
                budget, out var localNodes)
            || !OwnsNamedResultLocals(localNodes, resultStore, returnStore,
                completionResult, budget, out _)
            || !TryRawAwaitReceiver(raw, continuation, returnStore, setResult,
                getResult, budget, out StoreLocal? rawResult, out _, out _)
            || rawResult is null
            || rawResult.Index != resultStore.Index
            || !Equals(rawResult.Type, resultStore.Type)
            || rawResult.SourceOffset != resultStore.SourceOffset)
        {
            return null;
        }

        var candidate = new ClassicInverseCandidate("classic-await-return")
        {
            ResultLocal = completionResult.Index,
        };
        var locals = new ClassicInverseLocalTable();
        int local = locals.Add(resultStore.Type, name);
        candidate.Locals = locals.Types;
        candidate.LocalNames = locals.Names;
        candidate.SynthesizedLocalNames = locals.SynthesizedNames;
        candidate.MapLocal(resultStore.Index, local);
        var rewriter = new ClassicInverseRewriter(planning, shell, candidate,
            budget, index.AwaitedOperands);
        if (rewriter.Rewrite(getResult) is not AwaitExpression awaited
            || rewriter.Rewrite(returnStore.Value) is not IrExpression projection)
        {
            return null;
        }

        var outputStore = new StoreLocal(local, resultStore.Type, awaited);
        var outputReturn = new Return(projection);
        candidate.Statements.Add(outputStore);
        candidate.Statements.Add(outputReturn);
        candidate.Claim(resultStore, outputStore, ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(returnStore, outputReturn, ClassicInverseRealizationRule.ResultStore);
        return candidate;
    }

    static bool TryRawAwaitReceiver(
        IrFunction raw,
        Block continuation,
        StoreLocal returnStore,
        Call setResult,
        Call getResult,
        ClassicInverseBudget budget,
        out StoreLocal? result,
        out StoreLocal projection,
        out IrNode? use)
    {
        result = null;
        projection = null!;
        use = null;
        Block? rawContinuation = null;
        Call? rawSetResult = null;
        var rawLocals = new List<IrNode>();
        foreach (IrNode node in raw.Body.Descendants)
        {
            if (!budget.Charge())
                return false;
            if (node is Block block && block.StartOffset == continuation.StartOffset)
            {
                if (rawContinuation is not null)
                    return false;
                rawContinuation = block;
            }
            if (node is Call call && call.SourceOffset == setResult.SourceOffset)
            {
                if (rawSetResult is not null || call.Callee != setResult.Callee)
                    return false;
                rawSetResult = call;
            }
            if (node is Call resultCall && resultCall.SourceOffset == getResult.SourceOffset
                && resultCall.Parent is StoreLocal temporary && temporary.Index != returnStore.Index)
            {
                result = temporary;
            }
            if (node is StoreLocal or LoadLocal or LoadLocalAddress)
                rawLocals.Add(node);
        }
        if (rawContinuation?.Children is not
                [StoreLocal rawResult, StoreLocal rawReturn, Leave]
            || rawSetResult?.Arguments is not [_, LoadLocal rawCompletionResult]
            || rawResult.Index == rawReturn.Index
            || rawResult.Index < 0
            || rawReturn.Index != returnStore.Index
            || !Equals(rawReturn.Type, returnStore.Type)
            || rawReturn.SourceOffset != returnStore.SourceOffset
            || rawResult.Value is not Call rawGetResult
            || !Equals(rawResult.Type, getResult.Callee.ReturnType)
            || rawGetResult.SourceOffset != getResult.SourceOffset
            || rawGetResult.Callee != getResult.Callee
            || !OwnsNamedResultLocals(rawLocals, rawResult, rawReturn,
                rawCompletionResult, budget, out use))
        {
            return false;
        }
        result = rawResult;
        projection = rawReturn;
        return true;
    }

    static bool OwnsNamedResultLocals(
        IReadOnlyList<IrNode> nodes,
        StoreLocal resultStore,
        StoreLocal returnStore,
        LoadLocal completionResult,
        ClassicInverseBudget budget,
        out IrNode? resultUse)
    {
        resultUse = null;
        if (completionResult.Index != returnStore.Index)
            return false;
        foreach (IrNode node in nodes)
        {
            if (!budget.Charge())
                return false;
            if (node is StoreLocal store)
            {
                if (store.Index == resultStore.Index && !ReferenceEquals(store, resultStore)
                    || store.Index == returnStore.Index && !ReferenceEquals(store, returnStore))
                {
                    return false;
                }
                continue;
            }

            int local = node is LoadLocal load ? load.Index : ((LoadLocalAddress)node).Index;
            TypeRef type = node is LoadLocal value ? value.Type : ((LoadLocalAddress)node).Type;
            bool inProjection = false;
            for (IrNode? current = node; current is not null; current = current.Parent)
            {
                if (!budget.Charge())
                    return false;
                if (ReferenceEquals(current, returnStore.Value))
                {
                    inProjection = true;
                    break;
                }
            }
            if (local == resultStore.Index)
            {
                if (resultUse is not null || !inProjection || !Equals(type, resultStore.Type))
                    return false;
                resultUse = node;
            }
            else if (inProjection)
            {
                return false;
            }
            if (local == returnStore.Index
                && (!ReferenceEquals(node, completionResult) || !Equals(type, returnStore.Type)))
            {
                return false;
            }
        }

        return resultUse is not null && IsMemberReceiver(resultUse);
    }

    static bool IsMemberReceiver(IrNode node)
        => node.Parent switch
        {
            LoadProperty property => ReferenceEquals(property.Instance, node),
            LoadField field => ReferenceEquals(field.Instance, node),
            Call { Callee.HasThis: true } call => call.Arguments.Count > 0
                && ReferenceEquals(call.Arguments[0], node),
            _ => false,
        };

    // ---- Recipe: await a void-returning operation -----------------------

    static ClassicInverseCandidate? TrySingleAwaitVoid(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        RecipeIndex recipeIndex,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments.Count != 1
            || getResults.Count != 1
            || recipeIndex.HasTryFinally
            || recipeIndex.HasForeachHoist)
        {
            return null;
        }

        if (getResults[0].Parent is not ExpressionStatement statement
            || !ProvesCompletionTransfer(
                rawExecution,
                statement,
                setResult,
                budget))
            return null;

        var candidate = new ClassicInverseCandidate("classic-await-void");
        var rewriter = new ClassicInverseRewriter(
            planning,
            shell,
            candidate,
            budget,
            recipeIndex.AwaitedOperands);
        IrNode? rewritten = rewriter.Rewrite(statement);
        if (rewritten is not ExpressionStatement output)
            return null;

        candidate.Statements.Add(output);
        candidate.Statements.Add(new Return(null));
        candidate.Claim(statement, output, ClassicInverseRealizationRule.Statement);
        return candidate;
    }

    // ---- Recipe: two sequential awaits, then one statement ---------------

    static ClassicInverseCandidate? TrySequentialVoid(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        RecipeIndex recipeIndex,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments.Count != 1
            || getResults.Count != 2
            || recipeIndex.HasTryFinally
            || recipeIndex.HasForeachHoist)
        {
            return null;
        }

        if (!recipeIndex.TryFind<StoreLocal>(
                execution.Body,
                store => recipeIndex.Contains(
                    store.Value,
                    getResults[0],
                    budget),
                budget,
                out List<StoreLocal> firstResultStores)
            || firstResultStores.Count == 0)
            return null;
        StoreLocal firstResultStore = firstResultStores[0];

        if (!recipeIndex.TryFind<StoreField>(
                execution.Body,
                store =>
                    ClassicInverseNodeFacts.IsHoistedLocalField(store.Field.Name)
                    && ClassicInverseNodeFacts.IsMachineField(
                        store.Field,
                        shell.Machine)
                    && store.Instance is LoadArgument { Index: 0 }
                    && store.Value is LoadLocal local
                    && local.Index == firstResultStore.Index,
                budget,
                out List<StoreField> hoists)
            || hoists.Count == 0)
        {
            return null;
        }
        StoreField hoist = hoists[0];
        if (hoist.Field.Type is not { } firstType)
            return null;

        if (!recipeIndex.TryFind<StoreLocal>(
                execution.Body,
                store => recipeIndex.Contains(
                    store.Value,
                    getResults[1],
                    budget),
                budget,
                out List<StoreLocal> secondStores)
            || secondStores.Count == 0)
            return null;
        StoreLocal secondStore = secondStores[0];

        if (!recipeIndex.TryFind<ExpressionStatement>(
                execution.Body,
                static statement =>
                    statement.Expression is Call { Callee.Name: "KeepAlive" },
                budget,
                out List<ExpressionStatement> tails)
            || tails.Count == 0)
        {
            return null;
        }
        ExpressionStatement tail = tails[0];
        if (!ProvesCompletionTransfer(
                rawExecution,
                tail,
                setResult,
                budget))
        {
            return null;
        }

        var candidate = new ClassicInverseCandidate("classic-sequential-await-void");
        var locals = new ClassicInverseLocalTable();
        int firstIndex = locals.Add(firstType, SourceName(hoist.Field.Name));
        string? secondName =
            secondStore.Index >= 0 && secondStore.Index < execution.LocalNames.Length
                ? execution.LocalNames[secondStore.Index]
                : null;
        int secondIndex = locals.Add(secondStore.Type, secondName);

        candidate.Locals = locals.Types;
        candidate.LocalNames = locals.Names;
        candidate.SynthesizedLocalNames = locals.SynthesizedNames;
        candidate.MapHoistedLocal(hoist.Field, firstIndex);
        candidate.MapLocal(firstResultStore.Index, firstIndex);
        candidate.MapLocal(secondStore.Index, secondIndex);

        var rewriter = new ClassicInverseRewriter(
            planning,
            shell,
            candidate,
            budget,
            recipeIndex.AwaitedOperands);

        IrNode? firstValue = rewriter.Rewrite(firstResultStore.Value);
        IrNode? secondValue = rewriter.Rewrite(secondStore.Value);
        IrNode? tailStatement = rewriter.Rewrite(tail);
        if (firstValue is not IrExpression first
            || secondValue is not IrExpression second
            || tailStatement is not ExpressionStatement tailOutput)
        {
            return null;
        }

        var firstOutput = new StoreLocal(firstIndex, firstType, first);
        var secondOutput = new StoreLocal(secondIndex, secondStore.Type, second);
        candidate.Statements.Add(firstOutput);
        candidate.Statements.Add(secondOutput);
        candidate.Statements.Add(tailOutput);
        candidate.Statements.Add(new Return(null));

        candidate.Claim(
            firstResultStore,
            firstOutput,
            ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(
            secondStore,
            secondOutput,
            ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(tail, tailOutput, ClassicInverseRealizationRule.Statement);

        // The compiler hoists the first user local into the state machine so it
        // survives the suspension point. The hoist writes the same value into
        // the same variable the output local already denotes.
        candidate.DeclareProtocol(hoist, "hoisted-local-transfer");
        return candidate;
    }

    // ---- Recipe: conditional await --------------------------------------

    static ClassicInverseCandidate? TryConditional(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        RecipeIndex recipeIndex,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments is not [_, LoadLocal result]
            || getResults.Count != 1
            || recipeIndex.HasTryFinally
            || recipeIndex.HasForeachHoist)
        {
            return null;
        }

        if (!recipeIndex.TryFind<StoreLocal>(
                execution.Body,
                store =>
                    store.Index != result.Index
                    && recipeIndex.Contains(
                        store.Value,
                        getResults[0],
                        budget),
                budget,
                out List<StoreLocal> awaitStores)
            || awaitStores is not [StoreLocal awaitStore])
        {
            return null;
        }
        if (!recipeIndex.TryFind<StoreLocal>(
                execution.Body,
                store => store.Index == awaitStore.Index
                    && store.Value is Constant { Value: 0 },
                budget,
                out List<StoreLocal> zeroStores)
            || zeroStores is not [StoreLocal zeroStore])
        {
            return null;
        }
        if (zeroStore.Parent is not Block zeroBlock)
            return null;

        if (!recipeIndex.TryFind<ConditionalBranch>(
                execution.Body,
                branch =>
                    branch.TargetOffset == zeroBlock.StartOffset
                    && branch.Condition is LogicalNot,
                budget,
                out List<ConditionalBranch> zeroBranches)
            || zeroBranches.Count == 0)
            return null;
        ConditionalBranch zeroBranch = zeroBranches[0];

        if (!recipeIndex.TryFind<StoreLocal>(
                execution.Body,
                store =>
                    store.Index == result.Index
                    && store.Value is LoadLocal load
                    && load.Index == awaitStore.Index,
                budget,
                out List<StoreLocal> finalStores))
        {
            return null;
        }
        if (finalStores is not [StoreLocal finalStore])
            return null;

        // The compiler's join branch after the awaited arm.
        if (finalStore.Parent is not Block finalBlock)
            return null;
        if (!recipeIndex.TryFind<Branch>(
                execution.Body,
                branch => branch.TargetOffset == finalBlock.StartOffset,
                budget,
                out List<Branch> conditionalJoins))
        {
            return null;
        }
        if (conditionalJoins is not [Branch conditionalJoin]
            || zeroBranch.Parent is not Block conditionBlock
            || awaitStore.Parent is not Block awaitContinuation
            || OperandBlock(recipeIndex, getResults[0], budget)
                is not Block awaitEntry
            || !TryGetConditionalControlIdentity(
                conditionBlock,
                zeroBranch,
                awaitEntry,
                awaitContinuation,
                zeroBlock,
                finalBlock,
                conditionalJoin,
                budget,
                out string planningControl)
            || !TryGetRawConditionalControlIdentity(
                rawExecution,
                zeroBranch.SourceOffset,
                awaitEntry.StartOffset,
                awaitContinuation.StartOffset,
                zeroBlock.StartOffset,
                finalBlock.StartOffset,
                conditionalJoin.SourceOffset,
                budget,
                out string rawControl)
            || planningControl != rawControl
            || !ProvesCompletionTransfer(
                rawExecution,
                finalStore,
                setResult,
                budget))
        {
            return null;
        }

        var candidate = new ClassicInverseCandidate("classic-await-conditional")
        {
            ResultLocal = result.Index,
        };
        var rewriter = new ClassicInverseRewriter(
            planning,
            shell,
            candidate,
            budget,
            recipeIndex.AwaitedOperands);
        rewriter.AttributeAwaitTo(getResults[0], awaitStore);

        IrNode? condition =
            rewriter.Rewrite(((LogicalNot)zeroBranch.Condition).Operand);
        IrNode? whenTrue = rewriter.Rewrite(awaitStore.Value);
        IrNode? whenFalse = rewriter.Rewrite(zeroStore.Value);
        if (condition is not IrExpression conditionOutput
            || whenTrue is not IrExpression thenOutput
            || whenFalse is not IrExpression elseOutput)
        {
            return null;
        }

        var conditional = new Conditional(conditionOutput, thenOutput, elseOutput);
        var ret = new Return(conditional);
        candidate.Statements.Add(ret);

        candidate.MapLocalValue(awaitStore.Index, conditional);
        candidate.Claim(
            zeroBranch,
            conditional,
            ClassicInverseRealizationRule.ControlCondition);
        candidate.Claim(
            zeroStore,
            elseOutput,
            ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(
            finalStore,
            ret,
            ClassicInverseRealizationRule.ResultStore);

        candidate.DeclareProtocol(conditionalJoin, "conditional-join");

        Block? thenBlock = recipeIndex.EnclosingBlock(awaitStore, budget);
        Block? operandBlock =
            recipeIndex.EnclosingBlock(getResults[0], budget) is not null
            ? OperandBlock(recipeIndex, getResults[0], budget)
            : null;
        if (budget.Exhausted)
            return null;
        var thenRoots = new List<IrNode>();
        if (thenBlock is not null)
            thenRoots.Add(thenBlock);
        if (operandBlock is not null && !ReferenceEquals(operandBlock, thenBlock))
            thenRoots.Add(operandBlock);
        if (thenRoots.Count > 0)
            candidate.DeclareControlRegion("conditional-then", thenRoots, thenOutput);
        candidate.DeclareControlRegion("conditional-else", [zeroBlock], elseOutput);

        return candidate;
    }

    static bool TryGetConditionalControlIdentity(
        Block condition,
        ConditionalBranch test,
        Block awaitEntry,
        Block awaitContinuation,
        Block whenFalse,
        Block merge,
        Branch join,
        ClassicInverseBudget budget,
        out string identity)
    {
        identity = "";
        if (condition.Parent is not BlockContainer container
            || !ReferenceEquals(awaitEntry.Parent, container)
            || !ReferenceEquals(awaitContinuation.Parent, container)
            || !ReferenceEquals(whenFalse.Parent, container)
            || !ReferenceEquals(merge.Parent, container)
            || !ReferenceEquals(test.Parent, condition)
            || !ReferenceEquals(condition.Children.LastOrDefault(), test)
            || !ReferenceEquals(join.Parent, awaitContinuation)
            || !ReferenceEquals(
                awaitContinuation.Children.LastOrDefault(),
                join))
        {
            return false;
        }

        IReadOnlyList<Block> blocks = container.Blocks;
        int conditionIndex = BlockIndex(blocks, condition, budget);
        int awaitEntryIndex = BlockIndex(blocks, awaitEntry, budget);
        int awaitContinuationIndex = BlockIndex(
            blocks,
            awaitContinuation,
            budget);
        int falseIndex = BlockIndex(blocks, whenFalse, budget);
        int mergeIndex = BlockIndex(blocks, merge, budget);
        if (conditionIndex < 0
            || awaitEntryIndex < 0
            || awaitContinuationIndex < 0
            || falseIndex < 0
            || mergeIndex < 0
            || test.TargetOffset != whenFalse.StartOffset
            || join.TargetOffset != merge.StartOffset
            || !ClassicInverseCfg.TryBuild(blocks, budget, out var edges)
            || !HasOnlySuccessors(
                edges[conditionIndex],
                budget,
                awaitEntryIndex,
                falseIndex)
            || !HasOnlySuccessors(
                edges[awaitContinuationIndex],
                budget,
                mergeIndex)
            || !HasOnlySuccessors(
                edges[falseIndex],
                budget,
                mergeIndex)
            || !HasNoSuccessors(edges[mergeIndex])
            || !HasOnlyPredecessors(
                edges,
                awaitEntryIndex,
                budget,
                conditionIndex)
            || !HasOnlyPredecessors(
                edges,
                falseIndex,
                budget,
                conditionIndex)
            || !HasOnlyPredecessors(
                edges,
                mergeIndex,
                budget,
                awaitContinuationIndex,
                falseIndex))
        {
            return false;
        }

        identity =
            $"condition:{condition.StartOffset}/{test.SourceOffset}"
            + $"->{awaitEntry.StartOffset}|{whenFalse.StartOffset};"
            + $"await:{awaitContinuation.StartOffset}/{join.SourceOffset}"
            + $"->{merge.StartOffset};"
            + $"false:{whenFalse.StartOffset}->{merge.StartOffset};"
            + $"merge:{merge.StartOffset}";
        return true;
    }

    static bool TryGetRawConditionalControlIdentity(
        IrFunction raw,
        int testSourceOffset,
        int awaitEntryOffset,
        int awaitContinuationOffset,
        int falseOffset,
        int mergeOffset,
        int joinSourceOffset,
        ClassicInverseBudget budget,
        out string identity)
    {
        identity = "";
        var tests = new List<ConditionalBranch>();
        var joins = new List<Branch>();
        var awaitEntries = new List<Block>();
        var awaitContinuations = new List<Block>();
        var falseBlocks = new List<Block>();
        var mergeBlocks = new List<Block>();
        foreach (IrNode node in raw.Body.Descendants)
        {
            if (!budget.Charge())
                return false;
            switch (node)
            {
                case ConditionalBranch candidateTest
                    when candidateTest.SourceOffset == testSourceOffset:
                    tests.Add(candidateTest);
                    break;
                case Branch candidateJoin
                    when candidateJoin.SourceOffset == joinSourceOffset:
                    joins.Add(candidateJoin);
                    break;
                case Block block when block.StartOffset == awaitEntryOffset:
                    awaitEntries.Add(block);
                    break;
                case Block block
                    when block.StartOffset == awaitContinuationOffset:
                    awaitContinuations.Add(block);
                    break;
                case Block block when block.StartOffset == falseOffset:
                    falseBlocks.Add(block);
                    break;
                case Block block when block.StartOffset == mergeOffset:
                    mergeBlocks.Add(block);
                    break;
            }
        }

        if (tests is not [ConditionalBranch test]
            || joins is not [Branch join]
            || awaitEntries is not [Block awaitEntry]
            || awaitContinuations is not [Block awaitContinuation]
            || falseBlocks is not [Block whenFalse]
            || mergeBlocks is not [Block merge]
            || test.Parent is not Block condition)
        {
            return false;
        }

        return TryGetConditionalControlIdentity(
            condition,
            test,
            awaitEntry,
            awaitContinuation,
            whenFalse,
            merge,
            join,
            budget,
            out identity);
    }

    static int BlockIndex(
        IReadOnlyList<Block> blocks,
        Block target,
        ClassicInverseBudget budget)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (!budget.Charge())
                return -1;
            if (ReferenceEquals(blocks[i], target))
                return i;
        }
        return -1;
    }

    static bool HasOnlySuccessors(
        ILInspector.ControlFlow.BlockEdges block,
        ClassicInverseBudget budget,
        params int[] expected)
    {
        if (block.ExitsMethod
            || block.LeavesRegion
            || block.ExternalTargets.Count != 0
            || block.Successors.Count != expected.Length)
        {
            return false;
        }
        foreach (int successor in block.Successors)
        {
            if (!budget.Charge()
                || !ContainsExpected(expected, successor, budget))
                return false;
        }
        return true;
    }

    static bool HasNoSuccessors(
        ILInspector.ControlFlow.BlockEdges block)
        => block.ExternalTargets.Count == 0
            && block.Successors.Count == 0;

    static bool HasOnlyPredecessors(
        IReadOnlyList<ILInspector.ControlFlow.BlockEdges> edges,
        int block,
        ClassicInverseBudget budget,
        params int[] expected)
    {
        var actual = new List<int>();
        for (int source = 0; source < edges.Count; source++)
        {
            if (!budget.Charge())
                return false;
            foreach (int successor in edges[source].Successors)
            {
                if (!budget.Charge())
                    return false;
                if (successor == block)
                    actual.Add(source);
            }
        }
        if (actual.Count != expected.Length)
            return false;
        foreach (int source in actual)
        {
            if (!budget.Charge()
                || !ContainsExpected(expected, source, budget))
            {
                return false;
            }
        }
        return true;
    }

    static bool ContainsExpected(
        IReadOnlyList<int> expected,
        int value,
        ClassicInverseBudget budget)
    {
        for (int i = 0; i < expected.Count; i++)
        {
            if (!budget.Charge())
                return false;
            if (expected[i] == value)
                return true;
        }
        return false;
    }

    static bool ProvesCompletionTransfer(
        IrFunction raw,
        IrNode planningEndpoint,
        Call planningSetResult,
        ClassicInverseBudget budget)
    {
        if (planningEndpoint.Parent is not Block planningBlock
            || planningBlock.Parent is not BlockContainer planningContainer
            || !ClassicInverseCfg.TryBuild(
                planningContainer.Blocks,
                budget,
                out var planningEdges))
        {
            return false;
        }

        int planningIndex = BlockIndex(
            planningContainer.Blocks,
            planningBlock,
            budget);
        if (planningIndex < 0
            || !HasNoSuccessors(planningEdges[planningIndex]))
        {
            return false;
        }

        var rawEndpoints = new List<Block>();
        var rawSetResults = new List<Call>();
        var rawLeaves = new List<Leave>();
        foreach (IrNode node in raw.Body.Descendants)
        {
            if (!budget.Charge())
                return false;
            switch (node)
            {
                case Block block
                    when block.StartOffset == planningBlock.StartOffset:
                    rawEndpoints.Add(block);
                    break;
                case Call call
                    when call.SourceOffset == planningSetResult.SourceOffset
                        && call.Callee.Name == "SetResult":
                    rawSetResults.Add(call);
                    break;
                case Leave leave:
                    rawLeaves.Add(leave);
                    break;
            }
        }

        if (rawEndpoints is not [Block rawEndpoint]
            || rawSetResults is not
                [Call { Parent.Parent: Block setResultBlock }])
        {
            return false;
        }

        int leavesToSetResult = 0;
        foreach (Leave leave in rawLeaves)
        {
            if (!budget.Charge())
                return false;
            if (leave.TargetOffset == setResultBlock.StartOffset)
                leavesToSetResult++;
        }

        return rawEndpoint.Children.LastOrDefault() is Leave success
            && success.TargetOffset == setResultBlock.StartOffset
            && leavesToSetResult == 1;
    }

    static Block? OperandBlock(
        RecipeIndex recipeIndex,
        Call getResult,
        ClassicInverseBudget budget)
    {
        IrExpression? operand = recipeIndex.AwaitedOperand(getResult, budget);
        if (operand is null)
            return null;
        return recipeIndex.EnclosingBlock(operand, budget);
    }

    // ---- Recipe: await inside a foreach over an array --------------------

    static ClassicInverseCandidate? TryLoop(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        RecipeIndex recipeIndex,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments is not [_, LoadLocal finalResult]
            || getResults is not [Call getResult]
            || recipeIndex.HasTryFinally)
        {
            return null;
        }

        if (!recipeIndex.TryFind<StoreField>(
                execution.Body,
                store =>
                store.Field.Name == "<>7__wrap1"
                && store.Instance is LoadArgument { Index: 0 }
                && ClassicInverseNodeFacts.IsMachineField(store.Field, shell.Machine)
                && store.Value is LoadField collection
                && collection.Instance is LoadArgument { Index: 0 }
                && ClassicInverseNodeFacts.IsMachineField(
                    collection.Field,
                    shell.Machine)
                && collection.Field.Type is
                    { Kind: TypeRefKind.SzArray, ElementType: { } element }
                && IsTaskLike(element),
                budget,
                out List<StoreField> collectionHoists))
        {
            return null;
        }
        if (collectionHoists is not
            [StoreField { Value: LoadField collectionField } collectionHoist]
            || collectionField.Field.Type.ElementType is not { } taskType)
        {
            return null;
        }
        FieldRef hoistedCollection = collectionHoist.Field;

        // The loop index is whatever machine field the bound test compares
        // against this exact hoisted array's length — never a field recognized
        // by the compiler's '<>7__wrap' name family.
        if (!recipeIndex.TryFind<ConditionalBranch>(
                execution.Body,
                branch => branch.Condition is Comparison
            {
                Kind: ComparisonKind.LessThan,
                Left: LoadField { Instance: LoadArgument { Index: 0 } } index,
                Right: ArrayLength
                {
                    Array: LoadField { Instance: LoadArgument { Index: 0 } } array,
                },
            }
                && array.Field == hoistedCollection
                && index.Field != hoistedCollection
                && ClassicInverseNodeFacts.IsMachineField(
                    index.Field,
                    shell.Machine),
                budget,
                out List<ConditionalBranch> boundTests))
        {
            return null;
        }
        if (boundTests is not
            [ConditionalBranch
            {
                Parent: Block boundTestBlock,
                Condition: Comparison { Left: LoadField boundIndex },
            } boundTest])
        {
            return null;
        }
        FieldRef loopIndex = boundIndex.Field;

        if (!recipeIndex.TryFind<StoreLocal>(
                execution.Body,
                store => recipeIndex.Contains(
                    store.Value,
                    getResult,
                    budget),
                budget,
                out List<StoreLocal> resultStores)
            || resultStores is not [StoreLocal resultStore])
        {
            return null;
        }
        if (!recipeIndex.TryFind<StoreLocal>(
                execution.Body,
                store => store.Value is Binary { Kind: BinaryKind.Add } add
                && IsAccumulatorRead(add.Left, shell, hoistedCollection, loopIndex)
                && add.Right is LoadLocal read
                && read.Index == resultStore.Index,
                budget,
                out List<StoreLocal> accumulatorStores))
        {
            return null;
        }
        if (accumulatorStores is not
            [StoreLocal
            {
                Value: Binary { Left: LoadField accumulatorRead },
            } accumulatorStore])
        {
            return null;
        }
        FieldRef loopAccumulator = accumulatorRead.Field;
        var storage = new ClassicInverseLoopStorage(
            hoistedCollection,
            loopIndex,
            loopAccumulator);
        if (!recipeIndex.TryFind<StoreField>(
                execution.Body,
                store => store.Field == loopAccumulator
                && store.Instance is LoadArgument { Index: 0 }
                && store.Value is LoadLocal load
                && load.Index == accumulatorStore.Index,
                budget,
                out List<StoreField> accumulatorHoists)
            || !recipeIndex.TryFind<StoreField>(
                execution.Body,
                store => store.Field == hoistedCollection
                && store.Instance is LoadArgument { Index: 0 }
                && store.Value is Constant { Value: null },
                budget,
                out List<StoreField> collectionReleases)
            || !recipeIndex.TryFind<IrNode>(
                execution.Body,
                node => IsMachineFieldWrite(
                    node,
                    hoistedCollection,
                    shell.Machine),
                budget,
                out List<IrNode> collectionWrites)
            || !recipeIndex.TryFind<IrNode>(
                execution.Body,
                node => IsMachineFieldWrite(
                    node,
                    loopAccumulator,
                    shell.Machine),
                budget,
                out List<IrNode> accumulatorWrites))
        {
            return null;
        }
        if (accumulatorHoists is not [StoreField accumulatorHoist]
            || collectionReleases is not [StoreField collectionRelease]
            || collectionWrites.Count != 2
            || accumulatorWrites.Count != 1)
        {
            return null;
        }

        IrExpression? operand = recipeIndex.AwaitedOperand(getResult, budget);
        if (operand is null
            || !recipeIndex.TryFind<LoadStackSlot>(
                operand,
                static _ => true,
                budget,
                out List<LoadStackSlot> spilledElements))
        {
            return null;
        }
        if (spilledElements is not [LoadStackSlot spilledElement])
            return null;
        if (!recipeIndex.TryFind<StoreStackSlot>(
                execution.Body,
                store => store.Slot == spilledElement.Slot,
                budget,
                out List<StoreStackSlot> elementSpills)
            || elementSpills is not [StoreStackSlot elementSpill])
        {
            return null;
        }
        if (!storage.IsElementLoad(elementSpill.Value, shell.Machine))
            return null;

        if (!recipeIndex.TryFind<StoreLocal>(
                execution.Body,
                store => store.Index == accumulatorStore.Index
                    && store.Value is Constant { Value: 0 },
                budget,
                out List<StoreLocal> seeds)
            || !recipeIndex.TryFind<StoreLocal>(
                execution.Body,
                store => store.Index == finalResult.Index
                    && store.Value is LoadLocal load
                    && load.Index == accumulatorStore.Index,
                budget,
                out List<StoreLocal> finalStores)
            || seeds.Count == 0
            || finalStores.Count == 0)
        {
            return null;
        }
        StoreLocal seed = seeds[0];
        StoreLocal finalStore = finalStores[0];

        if (!recipeIndex.TryFind<StoreField>(
                execution.Body,
                advance => advance is
            {
                Value: Binary
                {
                    Kind: BinaryKind.Add,
                    IsChecked: false,
                    IsUnsigned: false,
                    Left: LoadField advanceRead,
                    Right: Constant { Value: 1 },
                },
                Instance: LoadArgument { Index: 0 },
            }
                && advance.Field == loopIndex
                && advanceRead.Field == loopIndex
                && advanceRead.Instance is LoadArgument { Index: 0 },
                budget,
                out List<StoreField> advances)
            || !recipeIndex.TryFind<IrNode>(
                execution.Body,
                node => IsMachineFieldWrite(
                    node,
                    loopIndex,
                    shell.Machine),
                budget,
                out List<IrNode> indexWrites)
            || !recipeIndex.TryFind<StoreField>(
                execution.Body,
                store => IsMachineFieldWrite(
                        store,
                        loopIndex,
                        shell.Machine)
                    && store.Value is Constant { Value: 0 },
                budget,
                out List<StoreField> indexInitializers))
        {
            return null;
        }
        if (advances is not [StoreField advance]
            || indexInitializers is not [StoreField indexInitializer]
            || indexWrites.Count != 2
            || recipeIndex.EnclosingBlock(elementSpill, budget)
                is not { } bodyBlock
            || recipeIndex.EnclosingBlock(advance, budget)
                is not { } advanceBlock)
        {
            return null;
        }

        if (!recipeIndex.TryFind<Branch>(
                execution.Body,
                branch => branch.TargetOffset == boundTestBlock.StartOffset,
                budget,
                out List<Branch> entries))
        {
            return null;
        }
        if (entries is not [Branch entry]
            || entry.Parent is not Block entryBlock
            || !ReferenceEquals(indexInitializer.Parent, entryBlock)
            || indexInitializer.ChildIndex >= entry.ChildIndex
            || !TryGetForeachControlIdentity(
                boundTestBlock,
                boundTest,
                entryBlock,
                bodyBlock,
                advanceBlock,
                indexInitializer,
                collectionHoist,
                elementSpill,
                accumulatorHoist,
                resultStore,
                accumulatorStore,
                collectionRelease,
                finalStore,
                budget,
                out string planningControl)
            || !TryGetRawForeachControlIdentity(
                rawExecution,
                shell.Machine,
                storage,
                boundTest.SourceOffset,
                entry.SourceOffset,
                elementSpill.SourceOffset,
                advance.SourceOffset,
                indexInitializer.SourceOffset,
                collectionHoist.SourceOffset,
                accumulatorHoist.SourceOffset,
                resultStore.SourceOffset,
                accumulatorStore.SourceOffset,
                collectionRelease.SourceOffset,
                finalStore.SourceOffset,
                collectionField.Field,
                accumulatorStore.Index,
                finalResult.Index,
                budget,
                out string rawControl)
            || planningControl != rawControl)
        {
            return null;
        }
        if (!ProvesCompletionTransfer(
                rawExecution,
                finalStore,
                setResult,
                budget))
        {
            return null;
        }

        var candidate = new ClassicInverseCandidate("classic-await-foreach-array")
        {
            ResultLocal = finalResult.Index,
            LoopStorage = storage,
        };
        var locals = new ClassicInverseLocalTable();
        TypeRef sumType = accumulatorStore.Type;
        int sumIndex = locals.AddSynthesized(sumType, "sum");
        int taskIndex = locals.AddSynthesized(taskType, "task");

        candidate.Locals = locals.Types;
        candidate.LocalNames = locals.Names;
        candidate.SynthesizedLocalNames = locals.SynthesizedNames;
        candidate.MapHoistedLocal(loopAccumulator, sumIndex);
        candidate.MapLocal(accumulatorStore.Index, sumIndex);
        candidate.MapLocal(finalResult.Index, sumIndex);

        var rewriter = new ClassicInverseRewriter(
            planning,
            shell,
            candidate,
            budget,
            recipeIndex.AwaitedOperands);
        IrNode? collectionOutput = rewriter.Rewrite(collectionField);
        if (collectionOutput is not IrExpression collectionExpression)
            return null;

        var awaited = new AwaitExpression(
            new LoadLocal(taskIndex, taskType),
            getResult.Callee.ReturnType,
            getResult.Callee.ReturnIsDynamic);
        var accumulate = new StoreLocal(
            sumIndex,
            sumType,
            new Binary(
                BinaryKind.Add,
                isChecked: false,
                isUnsigned: false,
                new LoadLocal(sumIndex, sumType),
                awaited));
        var body = new Block(0);
        body.Add(accumulate);
        var loop = new ForeachStatement(
            taskIndex,
            taskType,
            collectionExpression,
            body);

        var seedOutput = new StoreLocal(sumIndex, sumType, new Constant(0, sumType));
        var ret = new Return(new LoadLocal(sumIndex, sumType));
        candidate.Statements.Add(seedOutput);
        candidate.Statements.Add(loop);
        candidate.Statements.Add(ret);

        candidate.MapLocalValue(resultStore.Index, awaited);
        candidate.Claim(seed, seedOutput, ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(
            collectionField,
            collectionExpression,
            ClassicInverseRealizationRule.LoopCollection);
        candidate.Claim(elementSpill, body, ClassicInverseRealizationRule.LoopElement);
        candidate.Claim(
            spilledElement,
            awaited.Operand,
            ClassicInverseRealizationRule.LoopElement);
        candidate.Claim(
            resultStore,
            awaited,
            ClassicInverseRealizationRule.AwaitResult);
        candidate.Claim(
            accumulatorStore,
            accumulate,
            ClassicInverseRealizationRule.LoopAccumulator);
        candidate.Claim(finalStore, ret, ClassicInverseRealizationRule.ResultStore);

        candidate.DeclareContainer(
            collectionHoist,
            ClassicInverseAncestorKind.Protocol,
            "foreach-collection-hoist",
            outputContext: null);

        candidate.DeclareProtocol(
            collectionRelease,
            "foreach-collection-release");
        candidate.DeclareProtocol(
            accumulatorHoist,
            "hoisted-local-transfer");
        candidate.DeclareProtocol(indexInitializer, "foreach-index-init");
        candidate.DeclareProtocol(advance, "foreach-index-advance");
        candidate.DeclareProtocol(boundTest, "foreach-bound-test");
        candidate.DeclareProtocol(entry, "foreach-entry");

        var loopRoots = new List<IrNode>();
        if (recipeIndex.EnclosingBlock(elementSpill, budget) is { } spillBlock)
            loopRoots.Add(spillBlock);
        if (budget.Exhausted)
            return null;
        if (recipeIndex.EnclosingBlock(resultStore, budget) is { } resultBlock
            && (loopRoots.Count == 0
                || !ReferenceEquals(loopRoots[0], resultBlock)))
        {
            loopRoots.Add(resultBlock);
        }
        if (budget.Exhausted)
            return null;
        candidate.DeclareControlRegion("foreach-body", loopRoots, body);
        return candidate;
    }

    /// <summary>
    /// Proves the exact loop skeleton represented by the source-level
    /// <c>foreach</c>: the entry and advance reach the bound test, whose taken
    /// edge enters the body and whose fall-through exits the loop. Predecessor
    /// closure prevents another edge from silently entering either arm.
    /// </summary>
    static bool TryGetForeachControlIdentity(
        Block bound,
        ConditionalBranch test,
        Block entry,
        Block body,
        Block advance,
        StoreField indexInitializer,
        StoreField collectionHoist,
        StoreStackSlot elementSpill,
        StoreField accumulatorHoist,
        StoreLocal awaitedResultStore,
        StoreLocal accumulatorStore,
        StoreField collectionRelease,
        StoreLocal finalStore,
        ClassicInverseBudget budget,
        out string identity)
    {
        identity = "";
        if (bound.Parent is not BlockContainer container
            || !ReferenceEquals(entry.Parent, container)
            || !ReferenceEquals(body.Parent, container)
            || !ReferenceEquals(advance.Parent, container)
            || !ReferenceEquals(indexInitializer.Parent, entry)
            || !ReferenceEquals(collectionHoist.Parent, entry)
            || !ReferenceEquals(elementSpill.Parent, body)
            || !ReferenceEquals(accumulatorHoist.Parent, body)
            || !ReferenceEquals(awaitedResultStore.Parent, advance)
            || !ReferenceEquals(accumulatorStore.Parent, advance)
            || finalStore.Parent is not Block finalBlock
            || !ReferenceEquals(finalBlock.Parent, container)
            || indexInitializer.ChildIndex < 0
            || indexInitializer.ChildIndex >= entry.Children.Count - 1
            || collectionHoist.ChildIndex < 0
            || collectionHoist.ChildIndex >= indexInitializer.ChildIndex
            || elementSpill.ChildIndex < 0
            || accumulatorHoist.ChildIndex <= elementSpill.ChildIndex
            || accumulatorHoist.ChildIndex >= body.Children.Count - 1
            || awaitedResultStore.ChildIndex < 0
            || accumulatorStore.ChildIndex <= awaitedResultStore.ChildIndex
            || accumulatorStore.ChildIndex >= advance.Children.Count - 1
            || bound.Children is not [.., var terminator]
            || !ReferenceEquals(terminator, test))
        {
            return false;
        }

        IReadOnlyList<Block> blocks = container.Blocks;
        int boundIndex = BlockIndex(blocks, bound, budget);
        int entryIndex = BlockIndex(blocks, entry, budget);
        int bodyIndex = BlockIndex(blocks, body, budget);
        int advanceIndex = BlockIndex(blocks, advance, budget);
        int finalIndex = BlockIndex(blocks, finalBlock, budget);
        if (boundIndex < 0
            || entryIndex < 0
            || bodyIndex < 0
            || advanceIndex < 0
            || finalIndex < 0)
        {
            return false;
        }

        if (!ClassicInverseCfg.TryBuild(blocks, budget, out var edges))
            return false;
        if (!HasOnlySuccessors(edges[entryIndex], budget, boundIndex)
            || !HasOnlySuccessors(edges[advanceIndex], budget, boundIndex)
            || !TryLoopExit(
                edges[boundIndex].Successors,
                bodyIndex,
                budget,
                out int exitIndex))
        {
            return false;
        }

        if (test.TargetOffset != body.StartOffset
            || !ReferenceEquals(
                collectionRelease.Parent,
                blocks[exitIndex])
            || collectionRelease.ChildIndex < 0
            || finalStore.ChildIndex < 0
            || (exitIndex == finalIndex
                && collectionRelease.ChildIndex >= finalStore.ChildIndex)
            || !AllPathsReachTarget(
                edges,
                exitIndex,
                finalIndex,
                budget)
            || !HasOnlyPredecessors(
                edges,
                bodyIndex,
                budget,
                boundIndex)
            || !HasOnlyPredecessors(
                edges,
                exitIndex,
                budget,
                boundIndex)
            || !HasOnlyPredecessors(
                edges,
                boundIndex,
                budget,
                entryIndex,
                advanceIndex))
        {
            return false;
        }

        identity =
            $"collection:{collectionHoist.SourceOffset}"
            + $"@{entry.StartOffset}:{collectionHoist.ChildIndex};"
            + $"init:{indexInitializer.SourceOffset}@{entry.StartOffset}"
            + $":{indexInitializer.ChildIndex};"
            + $"entry:{entry.StartOffset}->{bound.StartOffset};"
            + $"element:{elementSpill.SourceOffset}@{body.StartOffset}"
            + $":{elementSpill.ChildIndex};"
            + $"accumulator:{accumulatorHoist.SourceOffset}@{body.StartOffset}"
            + $":{accumulatorHoist.ChildIndex};"
            + $"result:{awaitedResultStore.SourceOffset}@{advance.StartOffset}"
            + $":{awaitedResultStore.ChildIndex};"
            + $"accumulate:{accumulatorStore.SourceOffset}"
            + $"@{advance.StartOffset}:{accumulatorStore.ChildIndex};"
            + $"advance:{advance.StartOffset}->{bound.StartOffset};"
            + $"bound:{bound.StartOffset}/{test.SourceOffset}"
            + $"->{body.StartOffset}|{blocks[exitIndex].StartOffset};"
            + $"body:{body.StartOffset};"
            + $"release:{collectionRelease.SourceOffset}"
            + $"@{blocks[exitIndex].StartOffset}:{collectionRelease.ChildIndex};"
            + $"final:{finalStore.SourceOffset}"
            + $"@{finalBlock.StartOffset}:{finalStore.ChildIndex};"
            + $"exit:{blocks[exitIndex].StartOffset}";
        return true;
    }

    static bool TryLoopExit(
        IReadOnlyList<int> successors,
        int body,
        ClassicInverseBudget budget,
        out int exit)
    {
        exit = -1;
        bool foundBody = false;
        for (int i = 0; i < successors.Count; i++)
        {
            if (!budget.Charge())
                return false;
            int successor = successors[i];
            if (successor == body)
            {
                if (foundBody)
                    return false;
                foundBody = true;
            }
            else
            {
                if (exit >= 0)
                    return false;
                exit = successor;
            }
        }
        return foundBody && exit >= 0 && successors.Count == 2;
    }

    static bool AllPathsReachTarget(
        IReadOnlyList<ILInspector.ControlFlow.BlockEdges> edges,
        int start,
        int target,
        ClassicInverseBudget budget)
    {
        var state = new byte[edges.Count];
        var stack = new Stack<(int Block, int NextSuccessor)>();
        stack.Push((start, 0));
        while (stack.Count > 0)
        {
            if (!budget.Charge())
                return false;
            var (block, nextSuccessor) = stack.Pop();
            if (block == target)
                continue;

            ILInspector.ControlFlow.BlockEdges edge = edges[block];
            if (nextSuccessor == 0)
            {
                if (state[block] == 1
                    || edge.ExitsMethod
                    || edge.LeavesRegion
                    || edge.ExternalTargets.Count != 0
                    || edge.Successors.Count == 0)
                {
                    return false;
                }
                if (state[block] == 2)
                    continue;
                state[block] = 1;
            }

            if (nextSuccessor < edge.Successors.Count)
            {
                if (!budget.Charge())
                    return false;
                stack.Push((block, nextSuccessor + 1));
                int successor = edge.Successors[nextSuccessor];
                if (successor != target)
                {
                    if (state[successor] == 1)
                        return false;
                    if (state[successor] == 0)
                        stack.Push((successor, 0));
                }
                continue;
            }

            state[block] = 2;
        }
        return true;
    }

    static bool TryGetRawForeachControlIdentity(
        IrFunction raw,
        TypeRef machine,
        ClassicInverseLoopStorage storage,
        int boundSourceOffset,
        int entrySourceOffset,
        int bodySourceOffset,
        int advanceSourceOffset,
        int initializerSourceOffset,
        int collectionHoistSourceOffset,
        int accumulatorHoistSourceOffset,
        int awaitedResultStoreSourceOffset,
        int accumulatorStoreSourceOffset,
        int collectionReleaseSourceOffset,
        int finalStoreSourceOffset,
        FieldRef sourceCollection,
        int accumulatorLocal,
        int finalResultLocal,
        ClassicInverseBudget budget,
        out string identity)
    {
        identity = "";
        if (boundSourceOffset < 0
            || entrySourceOffset < 0
            || bodySourceOffset < 0
            || advanceSourceOffset < 0
            || initializerSourceOffset < 0
            || collectionHoistSourceOffset < 0
            || accumulatorHoistSourceOffset < 0
            || awaitedResultStoreSourceOffset < 0
            || accumulatorStoreSourceOffset < 0
            || collectionReleaseSourceOffset < 0
            || finalStoreSourceOffset < 0)
        {
            return false;
        }

        var bounds = new List<ConditionalBranch>();
        var entries = new List<Branch>();
        var bodyAnchors = new List<StoreStackSlot>();
        var advances = new List<StoreField>();
        var initializers = new List<StoreField>();
        var indexWrites = new List<IrNode>();
        var collectionHoists = new List<StoreField>();
        var accumulatorHoists = new List<StoreField>();
        var collectionReleases = new List<StoreField>();
        var finalStores = new List<StoreLocal>();
        var awaitedResultStores = new List<StoreLocal>();
        var accumulatorStores = new List<StoreLocal>();
        var collectionWrites = new List<IrNode>();
        var accumulatorWrites = new List<IrNode>();
        foreach (IrNode node in raw.Body.Descendants)
        {
            if (!budget.Charge())
                return false;
            switch (node)
            {
                case ConditionalBranch branch
                    when branch.SourceOffset == boundSourceOffset
                        && IsExactLoopBound(branch.Condition, storage, machine):
                    bounds.Add(branch);
                    break;
                case Branch branch
                    when branch.SourceOffset == entrySourceOffset:
                    entries.Add(branch);
                    break;
                case StoreStackSlot store
                    when store.SourceOffset == bodySourceOffset
                        && storage.IsElementLoad(store.Value, machine):
                    bodyAnchors.Add(store);
                    break;
                case StoreField
                {
                    Field: var advanceField,
                    Instance: LoadArgument { Index: 0 },
                    Value: Binary
                    {
                        Kind: BinaryKind.Add,
                        IsChecked: false,
                        IsUnsigned: false,
                        Left: LoadField
                        {
                            Field: var advanceRead,
                            Instance: LoadArgument { Index: 0 },
                        },
                        Right: Constant { Value: 1 },
                    },
                } store
                    when store.SourceOffset == advanceSourceOffset
                        && advanceField == storage.Index
                        && advanceRead == storage.Index
                        && ClassicInverseNodeFacts.IsMachineField(
                            advanceField,
                            machine):
                    advances.Add(store);
                    break;
                case StoreField
                {
                    Field: var hoistedCollection,
                    Instance: LoadArgument { Index: 0 },
                    Value: LoadField
                    {
                        Field: var source,
                        Instance: LoadArgument { Index: 0 },
                    },
                } store
                    when store.SourceOffset == collectionHoistSourceOffset
                        && hoistedCollection == storage.Collection
                        && source == sourceCollection:
                    collectionHoists.Add(store);
                    break;
                case StoreField
                {
                    Field: var accumulator,
                    Instance: LoadArgument { Index: 0 },
                    Value: LoadLocal { Index: var local },
                } store
                    when store.SourceOffset == accumulatorHoistSourceOffset
                        && accumulator == storage.Accumulator
                        && local == accumulatorLocal:
                    accumulatorHoists.Add(store);
                    break;
                case StoreField
                {
                    Field: var collection,
                    Instance: LoadArgument { Index: 0 },
                    Value: Constant { Value: null },
                } store
                    when store.SourceOffset == collectionReleaseSourceOffset
                        && collection == storage.Collection:
                    collectionReleases.Add(store);
                    break;
                case StoreLocal
                {
                    Index: var target,
                    Value: LoadLocal { Index: var source },
                } store
                    when store.SourceOffset == finalStoreSourceOffset
                        && target == finalResultLocal
                        && source == accumulatorLocal:
                    finalStores.Add(store);
                    break;
                case StoreLocal store
                    when store.SourceOffset == awaitedResultStoreSourceOffset:
                    awaitedResultStores.Add(store);
                    break;
                case StoreLocal store
                    when store.SourceOffset == accumulatorStoreSourceOffset:
                    accumulatorStores.Add(store);
                    break;
            }
            if (IsMachineFieldWrite(node, storage.Index, machine))
            {
                indexWrites.Add(node);
                if (node is StoreField
                    {
                        Value: Constant { Value: 0 },
                    } indexInitializer
                    && indexInitializer.SourceOffset == initializerSourceOffset)
                {
                    initializers.Add(indexInitializer);
                }
            }
            if (IsMachineFieldWrite(node, storage.Collection, machine))
                collectionWrites.Add(node);
            if (IsMachineFieldWrite(node, storage.Accumulator, machine))
                accumulatorWrites.Add(node);
        }
        if (bounds is not [ConditionalBranch bound]
            || entries is not [Branch entry]
            || bodyAnchors is not [StoreStackSlot bodyAnchor]
            || advances is not [StoreField advance]
            || initializers is not [StoreField initializer]
            || indexWrites.Count != 2
            || collectionHoists is not [StoreField collectionHoist]
            || accumulatorHoists is not [StoreField accumulatorHoist]
            || collectionReleases is not [StoreField collectionRelease]
            || finalStores is not [StoreLocal finalStore]
            || awaitedResultStores is not
                [StoreLocal awaitedResultStore]
            || accumulatorStores is not [StoreLocal accumulatorStore]
            || collectionWrites.Count != 2
            || accumulatorWrites.Count != 1
            || bound.Parent is not Block boundBlock
            || entry.Parent is not Block entryBlock
            || bodyAnchor.Parent is not Block bodyBlock
            || advance.Parent is not Block advanceBlock)
        {
            return false;
        }

        return TryGetForeachControlIdentity(
            boundBlock,
            bound,
            entryBlock,
            bodyBlock,
            advanceBlock,
            initializer,
            collectionHoist,
            bodyAnchor,
            accumulatorHoist,
            awaitedResultStore,
            accumulatorStore,
            collectionRelease,
            finalStore,
            budget,
            out identity);
    }

    static bool IsMachineFieldWrite(
        IrNode node,
        FieldRef expectedField,
        TypeRef machine)
        => node switch
        {
            StoreField
            {
                Field: var field,
            } => field == expectedField
                && ClassicInverseNodeFacts.IsMachineField(field, machine),
            InitObject
            {
                Address: LoadFieldAddress
                {
                    Field: var field,
                },
            } => field == expectedField
                && ClassicInverseNodeFacts.IsMachineField(field, machine),
            _ => false,
        };

    static bool IsExactLoopBound(
        IrExpression expression,
        ClassicInverseLoopStorage storage,
        TypeRef machine)
    {
        if (expression is not Comparison
            {
                Kind: ComparisonKind.LessThan,
                IsUnsigned: false,
                Left: LoadField
                {
                    Field: var index,
                    Instance: LoadArgument { Index: 0 },
                },
            } comparison
            || index != storage.Index
            || !ClassicInverseNodeFacts.IsMachineField(index, machine))
        {
            return false;
        }

        IrExpression length = comparison.Right;
        if (length is Convert
            {
                Target: var target,
                IsChecked: false,
                IsUnsigned: false,
                Operand: var converted,
            })
        {
            if (!MemberIdentity.IsCoreLibraryType(
                    target,
                    "System",
                    "Int32"))
                return false;
            length = converted;
        }

        return length is ArrayLength
            {
                Array: LoadField
                {
                    Field: var collection,
                    Instance: LoadArgument { Index: 0 },
                },
            }
            && collection == storage.Collection
            && ClassicInverseNodeFacts.IsMachineField(collection, machine);
    }

    /// <summary>
    /// A read of the loop accumulator: a machine field that is neither the
    /// hoisted collection nor the loop index the same recipe already bound.
    /// </summary>
    static bool IsAccumulatorRead(
        IrExpression expression,
        ClassicInverseShellFacts shell,
        FieldRef collection,
        FieldRef index)
        => expression is LoadField { Instance: LoadArgument { Index: 0 } } load
            && ClassicInverseNodeFacts.IsMachineField(load.Field, shell.Machine)
            && load.Field != collection
            && load.Field != index;

    // ---- Recipe: await inside try/finally --------------------------------

    static ClassicInverseCandidate? TryTryFinally(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        RecipeIndex recipeIndex,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments is not [_, LoadLocal result]
            || getResults.Count != 1
            || !recipeIndex.HasTryFinally)
        {
            return null;
        }

        if (!recipeIndex.TryFind<TryFinally>(
                execution.Body,
                static _ => true,
                budget,
                out List<TryFinally> tryFinallys)
            || tryFinallys is not [TryFinally tryFinally])
        {
            return null;
        }

        if (!recipeIndex.TryFind<StoreLocal>(
                tryFinally.TryBody,
                store => store.Index == result.Index
                    && recipeIndex.Contains(
                        store.Value,
                        getResults[0],
                        budget),
                budget,
                out List<StoreLocal> resultStores))
        {
            return null;
        }
        StoreLocal? resultStore =
            resultStores.Count == 0 ? null : resultStores[^1];
        if (resultStore is null
            || !ProvesCompletionTransfer(
                rawExecution,
                resultStore,
                setResult,
                budget))
            return null;

        if (!recipeIndex.TryFind<IfStatement>(
                tryFinally.FinallyBody,
                guard => guard.Parent is Block block
                    && ReferenceEquals(block.Parent, tryFinally.FinallyBody),
                budget,
                out List<IfStatement> guards))
        {
            return null;
        }
        if (guards is not [IfStatement guard]
            || guard.Then.Children is not [ExpressionStatement guarded]
            || guard.HasElse)
        {
            return null;
        }

        var candidate = new ClassicInverseCandidate("classic-await-try-finally")
        {
            ResultLocal = result.Index,
        };
        var rewriter = new ClassicInverseRewriter(
            planning,
            shell,
            candidate,
            budget,
            recipeIndex.AwaitedOperands);
        IrNode? value = rewriter.Rewrite(resultStore.Value);
        IrNode? finallyStatement = rewriter.Rewrite(guarded);
        if (value is not IrExpression returned
            || finallyStatement is not ExpressionStatement finallyOutput)
        {
            return null;
        }

        var ret = new Return(returned);
        var output = new TryFinally(Container(ret), Container(finallyOutput));
        candidate.Statements.Add(output);

        candidate.Claim(
            resultStore,
            ret,
            ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(
            guarded,
            finallyOutput,
            ClassicInverseRealizationRule.Statement);

        candidate.DeclareContainer(
            tryFinally,
            ClassicInverseAncestorKind.Reproduced,
            "try-finally",
            output);
        candidate.DeclareContainer(
            tryFinally.TryBody,
            ClassicInverseAncestorKind.Reproduced,
            "try-body",
            output.TryBody);
        candidate.DeclareContainer(
            tryFinally.FinallyBody,
            ClassicInverseAncestorKind.Reproduced,
            "finally-body",
            output.FinallyBody);

        // The compiler runs the user's finally body only when the machine is
        // not suspended. That guard is the shell's, and the reproduced C#
        // `finally` accounts for its semantics.
        if (IsFinallyStateGuard(guard.Condition, shell))
        {
            candidate.DeclareContainer(
                guard,
                ClassicInverseAncestorKind.Protocol,
                "finally-state-guard",
                outputContext: null);
            candidate.DeclareProtocol(guard.Condition, "finally-state-guard-test");
        }

        return candidate;
    }

    static bool IsFinallyStateGuard(
        IrExpression condition,
        ClassicInverseShellFacts shell)
        => shell.StateLocal >= 0
            && condition is Comparison
            {
                Kind: ComparisonKind.LessThan,
                IsUnsigned: false,
                Left: LoadLocal load,
                Right: Constant { Value: 0 },
            }
            && load.Index == shell.StateLocal;

    static BlockContainer Container(IrNode statement)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(statement);
        container.Add(block);
        return container;
    }

}

/// <summary>Accumulates the output local table a recipe introduces.</summary>
internal sealed class ClassicInverseLocalTable
{
    readonly ImmutableArray<TypeRef>.Builder _types =
        ImmutableArray.CreateBuilder<TypeRef>();
    readonly ImmutableArray<string?>.Builder _names =
        ImmutableArray.CreateBuilder<string?>();
    readonly ImmutableArray<string?>.Builder _synthesizedNames =
        ImmutableArray.CreateBuilder<string?>();

    internal int Add(TypeRef type, string? name)
    {
        int index = _types.Count;
        _types.Add(type);
        _names.Add(name);
        _synthesizedNames.Add(null);
        return index;
    }

    internal int AddSynthesized(TypeRef type, string name)
    {
        int index = Add(type, null);
        _synthesizedNames[index] = name;
        return index;
    }

    internal ImmutableArray<TypeRef> Types => _types.ToImmutable();

    internal ImmutableArray<string?> Names => _names.ToImmutable();

    internal ImmutableArray<string?> SynthesizedNames => _synthesizedNames.ToImmutable();
}
