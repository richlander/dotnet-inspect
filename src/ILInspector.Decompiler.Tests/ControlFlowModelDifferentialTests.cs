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
        public long ExplicitEdges;
        public long SwitchSuccessorBlocks;
        public long TerminalLeaves;
        public long NestedLeaves;
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
        Assert.True(comparison.ExplicitEdges > 10_000,
            $"Expected broad explicit-edge coverage; compared only {comparison.ExplicitEdges} edges.");
        Assert.True(comparison.SwitchSuccessorBlocks > 10_000,
            "The switch-region successor projection was not exercised broadly enough: "
                + $"{comparison.SwitchSuccessorBlocks} blocks.");
        Assert.True(comparison.TerminalLeaves > 0,
            "The corpus did not exercise the terminal Leave divergence domain.");
        Assert.True(comparison.NestedLeaves > 0,
            "The corpus did not exercise nested Leave clone ownership.");
        Assert.True(comparison.DifferenceCount == 0,
            $"{comparison.DifferenceCount} control-flow difference(s)"
                + (comparison.Differences.Count < comparison.DifferenceCount
                    ? $" (showing first {comparison.Differences.Count})"
                    : "")
                + ":\n  " + string.Join("\n  ", comparison.Differences));

        Console.WriteLine(
            $"FLOW-AGREEMENT methods={comparison.Methods} containers={comparison.Containers} "
            + $"explicit-edges={comparison.ExplicitEdges} "
            + $"switch-successor-blocks={comparison.SwitchSuccessorBlocks} "
            + $"terminal-leaves={comparison.TerminalLeaves} nested-leaves={comparison.NestedLeaves}");
    }

    static Comparison CompareCoreLib()
    {
        var comparison = new Comparison();
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);

        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            comparison.Methods++;
            IrPasses.Run(function, PreSwitchPasses);

            foreach (var container in function.Descendants.OfType<BlockContainer>())
            {
                comparison.Containers++;
                CompareContainer(
                    container.Blocks,
                    $"{typeName}::{methodName}/IL_{container.Blocks.FirstOrDefault()?.StartOffset ?? 0:X4}",
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

        comparison.ExplicitEdges += resolvedFactEdges.Count + externalFactEdges.Count;

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

            bool switchModelsBlock = SwitchRaisingPass.TrySuccessors(
                blocks,
                from,
                facts.OffsetToIndex,
                out var switchSuccessors);
            if (switchModelsBlock)
            {
                comparison.SwitchSuccessorBlocks++;
                if (cfg[from].LeavesRegion
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

            if (blocks[from].Children.LastOrDefault() is Leave leave)
            {
                comparison.TerminalLeaves++;
                if (!cfg[from].LeavesRegion
                    || cfg[from].Successors.Count > 0
                    || cfg[from].ExternalTargets.Count > 0
                    || switchModelsBlock
                    || !facts.BranchTargets.Contains(leave.TargetOffset)
                    || !facts.ClonePredecessorIndices.TryGetValue(leave.TargetOffset, out var owners)
                    || !owners.Contains(from)
                    || (facts.JumpPredecessorIndices.TryGetValue(leave.TargetOffset, out var jumpOwners)
                        && jumpOwners.Contains(from)))
                {
                    AddDifference(comparison,
                        $"{identity}: terminal Leave in block {from} does not preserve the declared "
                            + "region-exit/clone-owner distinction");
                }
            }

            foreach (var descendantLeave in blocks[from].Descendants.OfType<Leave>())
            {
                if (!ReferenceEquals(descendantLeave.Parent, blocks[from]))
                    comparison.NestedLeaves++;
                if (!facts.BranchTargets.Contains(descendantLeave.TargetOffset)
                    || !facts.ClonePredecessorIndices.TryGetValue(descendantLeave.TargetOffset, out var owners)
                    || !owners.Contains(from))
                {
                    AddDifference(comparison,
                        $"{identity}: descendant Leave IL_{descendantLeave.TargetOffset:X4} in block {from} "
                            + "is absent from StructuringFlowFacts clone ownership");
                }
            }
        }
    }

    static void AddDifference(Comparison comparison, string difference)
    {
        comparison.DifferenceCount++;
        if (comparison.Differences.Count < SampleCap)
            comparison.Differences.Add(difference);
    }
}
