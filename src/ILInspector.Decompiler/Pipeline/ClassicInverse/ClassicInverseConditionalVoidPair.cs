namespace ILInspector.Decompiler.Pipeline;

internal static partial class ClassicInverseRecipes
{
    // One Roslyn Release shell, not a general conditional-await finder.
    static ClassicInverseCandidate? TryConditionalVoidPair(
        IrFunction raw, ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell, Call setResult, List<Call> results,
        RecipeIndex index, ClassicInverseBudget budget)
    {
        if (setResult.Arguments.Count != 1 || results.Count != 2
            || index.HasTryFinally || index.HasForeachHoist
            || results[0].Parent is not ExpressionStatement first
            || results[1].Parent is not ExpressionStatement second
            || first.Parent is not Block firstEnd
            || firstEnd.Parent is not BlockContainer container)
            return null;
        var blocks = container.Blocks;
        if (blocks.Count != 15 || !ReferenceEquals(blocks[7], firstEnd)
            || !ReferenceEquals(blocks[11], second.Parent)
            || blocks[0].Children is not [ConditionalBranch dispatch0]
            || blocks[1].Children is not [ConditionalBranch dispatch1]
            || !shell.Protocol.Proves(dispatch0, ClassicInverseLoweringProof.StateDispatch)
            || !shell.Protocol.Proves(dispatch1, ClassicInverseLoweringProof.StateDispatch)
            || blocks[2].Children.LastOrDefault() is not ConditionalBranch { Condition: LogicalNot } outer
            || blocks[3].Children is not [ConditionalBranch { Condition: LogicalNot } optional]
            || blocks[12].Children is not [ConditionalBranch { Condition: LogicalNot } alternative]
            || outer.TargetOffset != blocks[12].StartOffset
            || optional.TargetOffset != blocks[8].StartOffset
            || alternative.TargetOffset != blocks[14].StartOffset
            || !ReferenceEquals(firstEnd.Children.FirstOrDefault(), first)
            || blocks[11].Children is not [ExpressionStatement, Branch join]
            || join.TargetOffset != blocks[14].StartOffset
            || blocks[14].Children.Count != 0
            || !index.AwaitedOperands.TryGetValue(results[0], out var operand0)
            || !index.AwaitedOperands.TryGetValue(results[1], out var operand1)
            || operand0.Parent?.Parent is not StoreLocal bind0
            || operand1.Parent?.Parent is not StoreLocal bind1
            || !ReferenceEquals(bind0.Parent, blocks[4])
            || blocks[4].Children is not [.., StoreLocal, ConditionalBranch]
            || blocks[8].Children is not [StoreLocal onlyBind, ConditionalBranch]
            || !ReferenceEquals(onlyBind, bind1)
            || !shell.Protocol.Proves(bind0, ClassicInverseLoweringProof.AwaiterBind)
            || !shell.Protocol.Proves(bind1, ClassicInverseLoweringProof.AwaiterBind))
            return null;
        // The first branch tests a reference; the two inner skips test Int32.
        if (!CoercionRendering.IsProvenReference(((LogicalNot)outer.Condition).Operand.ResultType, raw.TypeShapes)
            || !MemberIdentity.IsCoreLibraryType(((LogicalNot)optional.Condition).Operand.ResultType, "System", "Int32")
            || !MemberIdentity.IsCoreLibraryType(((LogicalNot)alternative.Condition).Operand.ResultType, "System", "Int32"))
            return null;
        int[][] expected = [[1, 6], [2, 10], [3, 12], [4, 8], [5, 7], [], [7], [8], [9, 11], [], [11], [14], [13, 14], [14], []];
        if (!ClassicInverseCfg.TryBuild(blocks, budget, out var edges)
            || !Matches(edges, Enumerable.Range(0, 15).ToArray())) return null;
        var byOffset = new Dictionary<int, Block>();
        var leaves = new List<Leave>();
        var completions = new List<Call>();
        foreach (IrNode node in raw.Body.Descendants)
        {
            if (!budget.Charge()) return null;
            if (node is Block block && !byOffset.TryAdd(block.StartOffset, block)) return null;
            if (node is Leave leave) leaves.Add(leave);
            if (node is Call call && call.SourceOffset == setResult.SourceOffset) completions.Add(call);
        }
        if (completions is not [Call completion] || completion.Callee != setResult.Callee
            || completion.Parent?.Parent is not Block completed) return null;
        var originals = new Block[15];
        var positions = new int[15];
        for (int i = 0; i < originals.Length; i++)
        {
            if (!budget.Charge() || !byOffset.TryGetValue(blocks[i].StartOffset, out var original)) return null;
            originals[i] = original;
        }
        if (originals[0].Parent is not BlockContainer rawContainer) return null;
        for (int i = 0; i < positions.Length; i++)
        {
            if (!budget.Charge() || !ReferenceEquals(originals[i].Parent, rawContainer)) return null;
            positions[i] = BlockIndex(rawContainer.Blocks, originals[i], budget);
            if (positions[i] < 0) return null;
        }
        int successfulLeaves = 0;
        foreach (var leave in leaves)
        {
            if (!budget.Charge()) return null;
            if (leave.TargetOffset == completed.StartOffset) successfulLeaves++;
        }
        if (!ClassicInverseCfg.TryBuild(rawContainer.Blocks, budget, out var rawEdges)
            || !Matches(rawEdges, positions)
            || originals[14].Children is not [Leave final]
            || final.TargetOffset != completed.StartOffset || successfulLeaves != 1)
            return null;
        foreach (var (position, branch) in new[] { (2, outer), (3, optional), (12, alternative) })
        {
            if (!budget.Charge() || originals[position].Children.LastOrDefault() is not ConditionalBranch original
                || original.SourceOffset != branch.SourceOffset || original.TargetOffset != branch.TargetOffset
                || original.Origin != branch.Origin
                || !ClassicInverseExpressionRules.SameTree(original.Condition, branch.Condition, budget)) return null;
        }
        if (originals[11].Children.LastOrDefault() is not Branch rawJoin
            || rawJoin.SourceOffset != join.SourceOffset || rawJoin.TargetOffset != join.TargetOffset) return null;

        var candidate = new ClassicInverseCandidate("classic-conditional-void-pair");
        var rewriter = new ClassicInverseRewriter(planning, shell, candidate, budget, index.AwaitedOperands);
        var prefix = new Block();
        var stream = new Block();
        var written = new Block();
        var otherwise = new Block();
        var advanced = new Block();
        if (!Statements(blocks[2].Children.Take(blocks[2].Children.Count - 1), prefix)
            || !Statements(blocks[4].Children.Take(blocks[4].Children.Count - 2), written)
            || !Statements(blocks[7].Children, written)
            || !Statements([second], stream)
            || !Statements(blocks[13].Children, advanced)
            || !Condition(optional, written, null, out var guardedWrite)
            || !Condition(alternative, advanced, null, out var guardedAdvance)) return null;
        // Keep output order aligned with physical semantic order: optional
        // write and cleanup precede the second await; alternative work follows.
        var flush = stream.Children[0];
        stream.DetachChildren();
        stream.Add(guardedWrite);
        stream.Add(flush);
        otherwise.Add(guardedAdvance);
        if (!Condition(outer, stream, otherwise, out var guardedStream)) return null;
        var prefixNodes = prefix.Children.ToArray();
        prefix.DetachChildren();
        foreach (var node in prefixNodes)
        {
            if (!budget.Charge()) return null;
            candidate.Statements.Add(node);
        }
        candidate.Statements.Add(guardedStream);
        candidate.Statements.Add(new Return(null));
        candidate.DeclareProtocol(join, "conditional-void-pair-join");
        candidate.DeclareControlRegion("conditional-stream", [.. blocks.Skip(3).Take(9)], stream);
        candidate.DeclareControlRegion("conditional-write", [.. blocks.Skip(4).Take(4)], written);
        candidate.DeclareControlRegion("conditional-alternative", [blocks[12], blocks[13]], otherwise);
        candidate.DeclareControlRegion("conditional-advance", [blocks[13]], advanced);
        return candidate;

        bool Condition(ConditionalBranch branch, Block then, Block? @else, out IfStatement output)
        {
            output = null!;
            if (!budget.Charge() || rewriter.Rewrite(((LogicalNot)branch.Condition).Operand) is not IrExpression condition) return false;
            output = new IfStatement(condition, then, @else);
            candidate.Claim(branch, condition, ClassicInverseRealizationRule.ConditionalVoidCondition);
            return true;
        }
        bool Statements(IEnumerable<IrNode> input, Block output)
        {
            foreach (var node in input)
            {
                if (!budget.Charge() || !IsStatement(node) || rewriter.Rewrite(node) is not IrNode rewritten) return false;
                output.Add(rewritten);
                candidate.Claim(node, rewritten, ClassicInverseRealizationRule.Statement);
            }
            return true;
        }
        bool IsStatement(IrNode node) => node is ExpressionStatement
            || node is StoreProperty { UpdateKind: null }
            || node is StoreField field && !ClassicInverseNodeFacts.IsMachineField(field.Field, shell.Machine)
            || node is InitObject { Address: LoadFieldAddress address } init
                && Equals(init.Type, address.Field.Type) && !ClassicInverseNodeFacts.IsMachineField(address.Field, shell.Machine);
        bool Matches(IReadOnlyList<ILInspector.ControlFlow.BlockEdges> graph, int[] mapping)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (!budget.Charge()) return false;
                int[] successors = expected[i].Select(j => mapping[j]).ToArray();
                if (!(successors.Length == 0 ? HasNoSuccessors(graph[mapping[i]]) : HasOnlySuccessors(graph[mapping[i]], budget, successors))) return false;
                if (i == 0) continue;
                var predecessors = new List<int>();
                for (int j = 0; j < expected.Length; j++)
                {
                    if (!budget.Charge()) return false;
                    if (expected[j].Contains(i)) predecessors.Add(mapping[j]);
                }
                if (!HasOnlyPredecessors(graph, mapping[i], budget, predecessors.ToArray())) return false;
            }
            return true;
        }
    }
}
