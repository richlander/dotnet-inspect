using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Corpus differential for the decompiler's overlapping control-flow views.
/// <see cref="ControlFlowViews_AgreeOverCoreLib"/> is the gate for their shared
/// edge semantics; intentional region-aware differences remain explicit.
/// </summary>
[Trait("Speed", "Slow")]
[Trait("Area", "Corpus")]
public class ControlFlowModelDifferentialTests
{
    const int SampleCap = 20;

    static readonly ImmutableArray<IIrPass> PreSwitchPasses =
        [.. IrPasses.Default.TakeWhile(pass => pass is not SwitchRaisingPass)];

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
        public long NestedStructuredTransferBlocks;
        public long DifferenceCount;
        public readonly List<string> Differences = [];
    }

    [Fact]
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
        Assert.True(comparison.NestedStructuredTransferBlocks > 0,
            "The corpus did not expose conditional structured-transfer paths.");
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
            + $"nested-structured-transfer-blocks={comparison.NestedStructuredTransferBlocks}");
    }

    [Fact]
    public void ControlFlowViews_AgreeOnSyntheticBoundaryTerminators()
    {
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
        CompareContainer([breakBlock, afterBreak], "synthetic/structured-break", comparison);

        var continueBlock = new Block(0x30);
        continueBlock.Add(new Continue());
        var afterContinue = new Block(0x38);
        afterContinue.Add(new Return(null));
        CompareContainer([continueBlock, afterContinue], "synthetic/structured-continue", comparison);

        var nonFinalBreak = new Block(0x40);
        nonFinalBreak.Add(new Break());
        nonFinalBreak.Add(new Return(null));
        CompareContainer([nonFinalBreak], "synthetic/non-final-structured-break", comparison);

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

        var loopBody = new BlockContainer();
        var ownedBreak = new Block(0x60);
        ownedBreak.Add(new Break());
        loopBody.Add(ownedBreak);
        var loopBlock = new Block(0x68);
        loopBlock.Add(new DoWhileLoop(
            loopBody,
            new Constant(false, TypeRef.CoreLib("System", "Boolean"))));
        var afterLoop = new Block(0x70);
        afterLoop.Add(new Return(null));
        Block[] loopBlocks = [loopBlock, afterLoop];
        CompareContainer(loopBlocks, "synthetic/owned-break", comparison);
        AssertSwitchSuccessors(loopBlocks, 0, [1]);

        Assert.Equal(1, comparison.EndFilterTerminators);
        Assert.Equal(1, comparison.ExternalExplicitEdges);
        Assert.Equal(3, comparison.DirectStructuredTransferBlocks);
        Assert.Equal(1, comparison.NestedStructuredTransferBlocks);
        AssertNoDifferences(comparison);
    }

    static Comparison CompareCoreLib()
    {
        var comparison = new Comparison();
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);

        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            long methodOrdinal = comparison.Methods;
            comparison.Methods++;
            IrPasses.Run(function, PreSwitchPasses);

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

        foreach (var (targetOffset, predecessors) in facts.JumpPredecessorIndices)
        {
            foreach (int predecessor in predecessors)
            {
                if (facts.OffsetToIndex.TryGetValue(targetOffset, out int target))
                    resolvedFactEdges.Add((predecessor, target));
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
            bool hasDirectStructuredTransfer =
                blocks[from].Children.Any(child => child is Break or Continue);
            if (hasDirectStructuredTransfer)
            {
                comparison.DirectStructuredTransferBlocks++;
                if (switchModelsBlock)
                {
                    AddDifference(comparison,
                        $"{identity}: switch successor view accepts a direct "
                            + $"Break/Continue transfer in block {from}");
                }
                continue;
            }
            if (hasStructuredTransfer)
            {
                comparison.NestedStructuredTransferBlocks++;
                if (!switchModelsBlock)
                {
                    AddDifference(comparison,
                        $"{identity}: switch successor view declines block {from}, whose conditional "
                            + "structured transfer retains an in-container fall-through");
                }
            }

            foreach (int to in cfg[from].Successors.Distinct())
            {
                if (to != from + 1 && !resolvedFactEdges.Contains((from, to)))
                    AddDifference(comparison,
                        $"{identity}: Cfg.Build non-fallthrough edge {from}->{to} "
                            + "is absent from StructuringFlowFacts");
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

            if (from + 1 < blocks.Count)
            {
                int nextOffset = blocks[from + 1].StartOffset;
                int explicitEdgesToNext =
                    facts.JumpPredecessorIndices.TryGetValue(nextOffset, out var nextOwners)
                        ? nextOwners.Count(owner => owner == from)
                        : 0;
                bool hasImplicitFallthrough = HasImplicitFallthrough(terminator);
                int expectedEdgesToNext = explicitEdgesToNext + (hasImplicitFallthrough ? 1 : 0);
                int actualEdgesToNext = cfg[from].Successors.Count(to => to == from + 1);

                if (hasImplicitFallthrough)
                {
                    comparison.ImplicitFallthroughEdges++;
                    if (terminator is SwitchBranch)
                        comparison.SwitchFallthroughEdges++;
                }

                if (actualEdgesToNext != expectedEdgesToNext)
                {
                    AddDifference(comparison,
                        $"{identity}: Cfg.Build has {actualEdgesToNext} edge(s) from block {from} "
                            + $"to fall-through block {from + 1}; expected {explicitEdgesToNext} explicit "
                            + $"and {(hasImplicitFallthrough ? 1 : 0)} implicit edge(s)");
                }
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
