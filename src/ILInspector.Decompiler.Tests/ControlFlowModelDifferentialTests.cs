using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Corpus differential for the decompiler's overlapping control-flow views.
/// <see cref="ControlFlowViews_AgreeOverCoreLib"/> is the gate for their shared
/// edge semantics; intentional region-aware differences remain explicit.
/// </summary>
public class ControlFlowModelDifferentialTests
{
    const int SampleCap = 20;

    sealed class Comparison
    {
        public long Methods;
        public long Containers;
        public long ResolvedExplicitEdges;
        public long ExternalExplicitEdges;
        public long ImplicitFallthroughEdges;
        public long SwitchFallthroughEdges;
        public long SwitchSuccessorBlocks;
        public long TerminalLeaves;
        public long EndFinallyTerminators;
        public long EndFilterTerminators;
        public long DirectStructuredTransferBlocks;
        public long DirectBreakBlocks;
        public long DirectContinueBlocks;
        public long AcceptedTerminalContinueBlocks;
        public long NestedStructuredTransferBlocks;
        public long NestedStructuredTransferBlocksModeledBySwitch;
        public long DifferenceCount;
        public readonly List<string> Differences = [];
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Corpus")]
    public void ControlFlowViews_AgreeOverCoreLib()
    {
        var comparison = CompareCoreLib();

        Assert.True(comparison.Methods > 10_000,
            $"Expected a large CoreLib corpus; inspected only {comparison.Methods} methods.");
        Assert.True(comparison.Containers > 10_000,
            $"Expected broad container coverage; inspected only {comparison.Containers} containers.");
        Assert.True(comparison.ResolvedExplicitEdges > 10_000,
            "Expected broad resolved explicit-edge coverage; compared only "
                + $"{comparison.ResolvedExplicitEdges} edges.");
        Assert.True(comparison.ImplicitFallthroughEdges > 10_000,
            "Expected broad implicit-fallthrough coverage; compared only "
                + $"{comparison.ImplicitFallthroughEdges} edges.");
        Assert.True(comparison.SwitchFallthroughEdges > 0,
            "The corpus did not exercise switch fall-through edges.");
        Assert.True(comparison.SwitchSuccessorBlocks > 10_000,
            "The switch-region successor projection was not exercised broadly enough: "
                + $"{comparison.SwitchSuccessorBlocks} blocks.");
        Assert.True(comparison.TerminalLeaves > 0,
            "The corpus did not exercise the terminal Leave divergence domain.");
        Assert.True(comparison.EndFinallyTerminators > 0,
            "The corpus did not exercise EndFinally terminators.");
        Assert.True(comparison.DirectStructuredTransferBlocks > 0,
            "The corpus did not expose direct structured-transfer terminators.");
        Assert.Equal(
            comparison.DirectStructuredTransferBlocks,
            comparison.DirectBreakBlocks);
        Assert.Equal(0, comparison.DirectContinueBlocks);
        Assert.Equal(0, comparison.AcceptedTerminalContinueBlocks);
        Assert.True(comparison.NestedStructuredTransferBlocks > 0,
            "The corpus did not expose conditional structured-transfer paths.");
        Assert.Equal(
            comparison.NestedStructuredTransferBlocks,
            comparison.NestedStructuredTransferBlocksModeledBySwitch);
        AssertNoDifferences(comparison);

        Console.WriteLine(
            $"FLOW-AGREEMENT methods={comparison.Methods} containers={comparison.Containers} "
            + $"resolved-explicit-edges={comparison.ResolvedExplicitEdges} "
            + $"external-explicit-edges={comparison.ExternalExplicitEdges} "
            + $"implicit-fallthrough-edges={comparison.ImplicitFallthroughEdges} "
            + $"switch-fallthrough-edges={comparison.SwitchFallthroughEdges} "
            + $"switch-successor-blocks={comparison.SwitchSuccessorBlocks} "
            + $"terminal-leaves={comparison.TerminalLeaves} "
            + $"end-finally-terminators={comparison.EndFinallyTerminators} "
            + $"end-filter-terminators={comparison.EndFilterTerminators} "
            + $"direct-structured-transfer-blocks={comparison.DirectStructuredTransferBlocks} "
            + $"direct-break-blocks={comparison.DirectBreakBlocks} "
            + $"direct-continue-blocks={comparison.DirectContinueBlocks} "
            + $"accepted-terminal-continue-blocks={comparison.AcceptedTerminalContinueBlocks} "
            + $"nested-structured-transfer-blocks={comparison.NestedStructuredTransferBlocks} "
            + "nested-structured-transfer-blocks-modeled-by-switch="
            + $"{comparison.NestedStructuredTransferBlocksModeledBySwitch}");
    }

    [Fact]
    [Trait("Area", "Pass")]
    public void ControlFlowViews_AgreeOnSyntheticBoundaryTerminators()
    {
        Assert.NotEmpty(PassesBeforeSwitchRaising());

        var comparison = new Comparison();
        var int32 = TypeRef.CoreLib("System", "Int32");

        var endFilter = new Block(0x00);
        endFilter.Add(new EndFilter(new Constant(1, int32)));
        var afterFilter = new Block(0x08);
        afterFilter.Add(new Return(null));
        CompareContainer([endFilter, afterFilter], "synthetic/end-filter", comparison);

        var finalSwitch = new Block(0x10);
        finalSwitch.Add(new SwitchBranch(new Constant(0, int32), [0x10]));
        CompareContainer([finalSwitch], "synthetic/final-switch", comparison);

        var externalBranch = new Block(0x18);
        externalBranch.Add(new Branch(0xDEAD));
        CompareContainer([externalBranch], "synthetic/external-branch", comparison);

        var breakBlock = new Block(0x20);
        breakBlock.Add(new Break());
        var afterBreak = new Block(0x28);
        afterBreak.Add(new Return(null));
        Block[] breakBlocks = [breakBlock, afterBreak];
        CompareContainer(breakBlocks, "synthetic/structured-break", comparison);
        AssertSwitchDeclines(breakBlocks, 0);

        var continueBlock = new Block(0x30);
        continueBlock.Add(new Continue());
        var afterContinue = new Block(0x38);
        afterContinue.Add(new Return(null));
        Block[] continueBlocks = [continueBlock, afterContinue];
        CompareContainer(continueBlocks, "synthetic/structured-continue", comparison);
        AssertSwitchSuccessors(continueBlocks, 0, []);

        var nonFinalBreak = new Block(0x40);
        nonFinalBreak.Add(new Break());
        nonFinalBreak.Add(new Return(null));
        Block[] nonFinalBreakBlocks = [nonFinalBreak];
        CompareContainer(
            nonFinalBreakBlocks,
            "synthetic/non-final-structured-break",
            comparison);
        AssertSwitchDeclines(nonFinalBreakBlocks, 0);

        var conditionalBreakArm = new Block(0x48);
        conditionalBreakArm.Add(new Break());
        var conditionalBreak = new Block(0x50);
        conditionalBreak.Add(new IfStatement(
            new Constant(true, TypeRef.CoreLib("System", "Boolean")),
            conditionalBreakArm,
            elseArm: null));
        var afterConditionalBreak = new Block(0x58);
        afterConditionalBreak.Add(new Return(null));
        Block[] conditionalBreakBlocks = [conditionalBreak, afterConditionalBreak];
        CompareContainer(conditionalBreakBlocks, "synthetic/conditional-break", comparison);
        AssertSwitchSuccessors(conditionalBreakBlocks, 0, [1]);

        var nestedBreakArm = new Block(0x60);
        nestedBreakArm.Add(new Break());
        var nestedBreakThenEndFinally = new Block(0x68);
        nestedBreakThenEndFinally.Add(new IfStatement(
            new Constant(true, TypeRef.CoreLib("System", "Boolean")),
            nestedBreakArm,
            elseArm: null));
        nestedBreakThenEndFinally.Add(new EndFinally());
        Block[] nestedBreakThenEndFinallyBlocks = [nestedBreakThenEndFinally];
        CompareContainer(
            nestedBreakThenEndFinallyBlocks,
            "synthetic/nested-break-before-end-finally",
            comparison);
        AssertSwitchDeclines(nestedBreakThenEndFinallyBlocks, 0);

        var loopBody = new BlockContainer();
        var ownedBreak = new Block(0x70);
        ownedBreak.Add(new Break());
        loopBody.Add(ownedBreak);
        var loopBlock = new Block(0x78);
        loopBlock.Add(new DoWhileLoop(
            loopBody,
            new Constant(false, TypeRef.CoreLib("System", "Boolean"))));
        var afterLoop = new Block(0x80);
        afterLoop.Add(new Return(null));
        Block[] loopBlocks = [loopBlock, afterLoop];
        CompareContainer(loopBlocks, "synthetic/owned-break", comparison);
        AssertSwitchSuccessors(loopBlocks, 0, [1]);

        var branchToNext = new Block(0x88);
        branchToNext.Add(new Branch(0x90));
        var nextBlock = new Block(0x90);
        nextBlock.Add(new Return(null));
        CompareContainer([branchToNext, nextBlock], "synthetic/branch-to-next", comparison);

        Assert.Equal(1, comparison.EndFilterTerminators);
        Assert.Equal(1, comparison.EndFinallyTerminators);
        Assert.Equal(1, comparison.ExternalExplicitEdges);
        Assert.Equal(3, comparison.DirectStructuredTransferBlocks);
        Assert.Equal(2, comparison.DirectBreakBlocks);
        Assert.Equal(1, comparison.DirectContinueBlocks);
        Assert.Equal(1, comparison.AcceptedTerminalContinueBlocks);
        Assert.Equal(2, comparison.NestedStructuredTransferBlocks);
        Assert.Equal(1, comparison.NestedStructuredTransferBlocksModeledBySwitch);
        AssertNoDifferences(comparison);
    }

    static Comparison CompareCoreLib()
    {
        var comparison = new Comparison();
        var preSwitchPasses = PassesBeforeSwitchRaising();
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);

        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            long methodOrdinal = comparison.Methods;
            comparison.Methods++;
            IrPasses.Run(function, preSwitchPasses);

            int containerOrdinal = 0;
            foreach (var container in function.Descendants.OfType<BlockContainer>())
            {
                comparison.Containers++;
                CompareContainer(
                    container.Blocks,
                    $"{typeName}::{methodName}[token=0x{function.MetadataToken:X8},"
                        + $"method={methodOrdinal},container={containerOrdinal++}]"
                        + $"/IL_{container.Blocks.FirstOrDefault()?.StartOffset ?? 0:X4}",
                    comparison);
            }
        }

        return comparison;
    }

    static void CompareContainer(
        IReadOnlyList<Block> blocks,
        string identity,
        Comparison comparison)
    {
        var cfg = Cfg.Build(blocks);
        var facts = StructuringFlowFacts.Collect(blocks);
        var resolvedFactEdges = new HashSet<(int From, int To)>();
        var externalFactEdges = new HashSet<(int From, int TargetOffset)>();
        var resolvedFactSuccessors = Enumerable.Range(0, blocks.Count)
            .Select(_ => new List<int>())
            .ToArray();

        foreach (var (targetOffset, predecessors) in facts.JumpPredecessorIndices)
        {
            foreach (int predecessor in predecessors)
            {
                if (facts.OffsetToIndex.TryGetValue(targetOffset, out int target))
                {
                    resolvedFactEdges.Add((predecessor, target));
                    resolvedFactSuccessors[predecessor].Add(target);
                }
                else
                    externalFactEdges.Add((predecessor, targetOffset));
            }
        }

        comparison.ResolvedExplicitEdges += resolvedFactEdges.Count;
        comparison.ExternalExplicitEdges += externalFactEdges.Count;

        foreach (var edge in resolvedFactEdges)
        {
            if (!cfg[edge.From].Successors.Contains(edge.To))
                AddDifference(comparison,
                    $"{identity}: StructuringFlowFacts edge {edge.From}->{edge.To} is absent from Cfg.Build");
        }

        foreach (var edge in externalFactEdges)
        {
            if (!cfg[edge.From].ExternalTargets.Contains(edge.TargetOffset))
                AddDifference(comparison,
                    $"{identity}: StructuringFlowFacts external edge {edge.From}->IL_{edge.TargetOffset:X4} "
                        + "is absent from Cfg.Build");
        }

        for (int from = 0; from < cfg.Count; from++)
        {
            var terminator = blocks[from].Children.LastOrDefault();
            bool switchModelsBlock = SwitchRaisingPass.TrySuccessors(
                blocks,
                from,
                facts.OffsetToIndex,
                out var switchSuccessors);

            foreach (int successor in cfg[from].Successors)
            {
                if ((uint)successor >= (uint)blocks.Count)
                {
                    AddDifference(comparison,
                        $"{identity}: Cfg.Build edge {from}->{successor} is outside "
                            + $"the {blocks.Count}-block container");
                }
            }

            bool hasStructuredTransfer = ContainsStructuredTransferLeavingBlock(blocks[from]);
            bool hasDirectBreak = blocks[from].Children.Any(child => child is Break);
            bool hasDirectContinue = blocks[from].Children.Any(child => child is Continue);
            bool hasDirectStructuredTransfer = hasDirectBreak || hasDirectContinue;
            if (hasDirectStructuredTransfer)
            {
                comparison.DirectStructuredTransferBlocks++;
                if (hasDirectBreak)
                    comparison.DirectBreakBlocks++;
                if (hasDirectContinue)
                    comparison.DirectContinueBlocks++;

                bool expectsTerminalContinue = terminator is Continue
                    && !HasTopLevelSwitchDeclineReason(
                        blocks[from],
                        facts.OffsetToIndex);
                if (switchModelsBlock != expectsTerminalContinue)
                {
                    AddDifference(comparison,
                        $"{identity}: switch successor view "
                            + $"{(switchModelsBlock ? "accepts" : "declines")} direct structured "
                            + $"transfer block {from}; expected "
                            + $"{(expectsTerminalContinue ? "terminal Continue acceptance" : "decline")}");
                }
                if (switchModelsBlock && terminator is Continue)
                    comparison.AcceptedTerminalContinueBlocks++;
                continue;
            }
            if (hasStructuredTransfer)
            {
                comparison.NestedStructuredTransferBlocks++;
                if (switchModelsBlock)
                    comparison.NestedStructuredTransferBlocksModeledBySwitch++;
                if (!switchModelsBlock
                    && !HasTopLevelSwitchDeclineReason(
                        blocks[from],
                        facts.OffsetToIndex))
                {
                    AddDifference(comparison,
                        $"{identity}: switch successor view declines block {from}, whose conditional "
                            + "structured transfer retains an in-container fall-through");
                }
            }

            foreach (int targetOffset in cfg[from].ExternalTargets.Distinct())
            {
                if (!externalFactEdges.Contains((from, targetOffset)))
                    AddDifference(comparison,
                        $"{identity}: Cfg.Build external edge {from}->IL_{targetOffset:X4} "
                            + "is absent from StructuringFlowFacts");
            }

            if (!hasStructuredTransfer)
            {
                bool expectsMethodExit = terminator is Return or Throw;
                bool expectsRegionExit = terminator is Leave or EndFinally or EndFilter;
                if (cfg[from].ExitsMethod != expectsMethodExit
                    || cfg[from].LeavesRegion != expectsRegionExit)
                {
                    AddDifference(comparison,
                        $"{identity}: Cfg.Build classification for block {from} "
                            + $"exits={cfg[from].ExitsMethod}, leaves={cfg[from].LeavesRegion} "
                            + $"disagrees with terminator {terminator?.GetType().Name ?? "<none>"}");
                }
            }

            bool hasImplicitFallthrough = HasImplicitFallthrough(terminator);
            var expectedSuccessors = new List<int>(resolvedFactSuccessors[from]);
            if (hasImplicitFallthrough && from + 1 < blocks.Count)
            {
                expectedSuccessors.Add(from + 1);
                comparison.ImplicitFallthroughEdges++;
                if (terminator is SwitchBranch)
                    comparison.SwitchFallthroughEdges++;
            }

            int[] actualSuccessors = [.. cfg[from].Successors.Order()];
            int[] orderedExpectedSuccessors = [.. expectedSuccessors.Order()];
            if (!actualSuccessors.SequenceEqual(orderedExpectedSuccessors))
            {
                AddDifference(comparison,
                    $"{identity}: Cfg.Build successors for block {from} "
                        + $"[{string.Join(",", actualSuccessors)}] disagree with "
                        + $"StructuringFlowFacts plus fall-through "
                        + $"[{string.Join(",", orderedExpectedSuccessors)}]");
            }

            if (switchModelsBlock)
            {
                comparison.SwitchSuccessorBlocks++;
                if (switchSuccessors.Any(successor => (uint)successor >= (uint)blocks.Count)
                    || cfg[from].LeavesRegion
                    || cfg[from].ExternalTargets.Count > 0
                    || !switchSuccessors.ToHashSet().SetEquals(cfg[from].Successors))
                {
                    AddDifference(comparison,
                        $"{identity}: switch successor view for block {from} "
                            + $"[{string.Join(",", switchSuccessors)}] disagrees with Cfg.Build "
                            + $"[{string.Join(",", cfg[from].Successors)}], "
                            + $"external={cfg[from].ExternalTargets.Count}, leaves={cfg[from].LeavesRegion}");
                }
            }

            if (terminator is Leave leave)
            {
                comparison.TerminalLeaves++;
                if (!cfg[from].LeavesRegion
                    || cfg[from].Successors.Count > 0
                    || cfg[from].ExternalTargets.Count > 0
                    || switchModelsBlock
                    || (facts.JumpPredecessorIndices.TryGetValue(leave.TargetOffset, out var jumpOwners)
                        && jumpOwners.Contains(from)))
                {
                    AddDifference(comparison,
                        $"{identity}: terminal Leave in block {from} does not preserve the declared "
                            + "region-exit/non-jump distinction");
                }
            }
            else if (terminator is EndFinally or EndFilter)
            {
                if (terminator is EndFinally)
                    comparison.EndFinallyTerminators++;
                else
                    comparison.EndFilterTerminators++;
                if (!cfg[from].LeavesRegion
                    || cfg[from].Successors.Count > 0
                    || cfg[from].ExternalTargets.Count > 0
                    || switchModelsBlock)
                {
                    AddDifference(comparison,
                        $"{identity}: {terminator.GetType().Name} in block {from} does not preserve "
                            + "the declared region-exit distinction");
                }
            }
        }
    }

    static bool ContainsStructuredTransferLeavingBlock(Block block)
    {
        foreach (var transfer in block.Descendants)
        {
            if (transfer is Break && !HasOwnerInsideBlock(transfer, block, breakCanTargetSwitch: true))
                return true;
            if (transfer is Continue && !HasOwnerInsideBlock(transfer, block, breakCanTargetSwitch: false))
                return true;
        }
        return false;
    }

    static bool HasOwnerInsideBlock(IrNode transfer, Block block, bool breakCanTargetSwitch)
    {
        for (var ancestor = transfer.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, block))
                return false;
            if (ancestor is Lambda or LocalFunctionStatement)
                return true;
            if (ancestor is WhileLoop or DoWhileLoop or ForLoop or ForeachStatement
                || (breakCanTargetSwitch && ancestor is Switch))
            {
                return true;
            }
        }
        return false;
    }

    static bool HasImplicitFallthrough(IrNode? terminator)
        => terminator switch
        {
            ConditionalBranch or SwitchBranch => true,
            Branch or Return or Throw or Leave or EndFinally or EndFilter => false,
            Break or Continue => throw new InvalidOperationException(
                "Structured transfers must be excluded before projecting fall-through."),
            _ => true,
        };

    static bool HasTopLevelSwitchDeclineReason(
        Block block,
        IReadOnlyDictionary<int, int> offsetToIndex)
    {
        for (int i = 0; i < block.Children.Count - 1; i++)
        {
            if (block.Children[i] is Branch or ConditionalBranch or SwitchBranch
                or Leave or EndFinally or EndFilter or Break or Continue)
            {
                return true;
            }
        }

        return block.Children.LastOrDefault() switch
        {
            Branch branch => !offsetToIndex.ContainsKey(branch.TargetOffset),
            ConditionalBranch conditional =>
                !offsetToIndex.ContainsKey(conditional.TargetOffset),
            SwitchBranch or Leave or EndFinally or EndFilter or Break => true,
            Continue => false,
            _ => false,
        };
    }

    static ImmutableArray<IIrPass> PassesBeforeSwitchRaising()
    {
        var switchPass = Assert.Single(IrPasses.Default.OfType<SwitchRaisingPass>());
        int switchIndex = IrPasses.Default.IndexOf(switchPass);
        Assert.True(switchIndex > 0, "SwitchRaisingPass must remain anchored in the default pipeline.");
        return [.. IrPasses.Default.Take(switchIndex)];
    }

    static void AssertSwitchSuccessors(
        IReadOnlyList<Block> blocks,
        int blockIndex,
        IReadOnlyList<int> expected)
    {
        var offsetToIndex = blocks
            .Select((block, index) => (block.StartOffset, index))
            .ToDictionary(pair => pair.StartOffset, pair => pair.index);
        Assert.True(SwitchRaisingPass.TrySuccessors(
            blocks,
            blockIndex,
            offsetToIndex,
            out var successors));
        Assert.Equal(expected, successors);
    }

    static void AssertSwitchDeclines(IReadOnlyList<Block> blocks, int blockIndex)
    {
        var offsetToIndex = blocks
            .Select((block, index) => (block.StartOffset, index))
            .ToDictionary(pair => pair.StartOffset, pair => pair.index);
        Assert.False(SwitchRaisingPass.TrySuccessors(
            blocks,
            blockIndex,
            offsetToIndex,
            out _));
    }

    static void AssertNoDifferences(Comparison comparison)
        => Assert.True(comparison.DifferenceCount == 0,
            $"{comparison.DifferenceCount} control-flow difference(s)"
                + (comparison.Differences.Count < comparison.DifferenceCount
                    ? $" (showing first {comparison.Differences.Count})"
                    : "")
                + ":\n  " + string.Join("\n  ", comparison.Differences));

    static void AddDifference(Comparison comparison, string difference)
    {
        comparison.DifferenceCount++;
        if (comparison.Differences.Count < SampleCap)
            comparison.Differences.Add(difference);
    }
}
