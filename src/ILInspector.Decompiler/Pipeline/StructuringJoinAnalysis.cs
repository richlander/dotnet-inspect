using System.Collections.Immutable;
using ILInspector.ControlFlow;

namespace ILInspector.Decompiler.Pipeline;

internal enum StructuringJoinRegionKind
{
    Forward,
    BackEdge,
}

internal readonly record struct StructuringJoinRegion(
    StructuringJoinRegionKind Kind,
    int Start,
    int End,
    int Merge,
    ImmutableArray<int> BackEdgeSources,
    bool IsNonCrossing,
    bool IsBackEdgeEntangled);

internal sealed record StructuringJoinPlan(
    ImmutableArray<StructuringJoinRegion> Regions,
    ImmutableArray<StructuringJoinRegion> ForwardRegions,
    ImmutableArray<StructuringJoinRegion> BackEdgeRegions,
    ImmutableArray<int> VirtualExitDecisions,
    ImmutableArray<int> UnrootedDecisions);

internal static class StructuringJoinAnalysis
{
    public static StructuringJoinPlan Analyze(IReadOnlyList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (blocks.Count == 0)
            return new([], [], [], [], []);

        var edges = Cfg.Build(blocks);
        var postDominators = PostDominators.Of(edges);
        var forwardRegions = ImmutableArray.CreateBuilder<StructuringJoinRegion>();
        var virtualExitDecisions = ImmutableArray.CreateBuilder<int>();
        var unrootedDecisions = ImmutableArray.CreateBuilder<int>();
        var backEdgesByHead = new Dictionary<int, List<int>>();
        var backEdgeSources = new HashSet<int>();
        for (int source = 0; source < edges.Count; source++)
        {
            foreach (int target in edges[source].Successors)
            {
                if (target > source)
                    continue;
                if (!backEdgesByHead.TryGetValue(target, out var sources))
                    backEdgesByHead[target] = sources = [];
                sources.Add(source);
                backEdgeSources.Add(source);
            }
        }

        for (int index = 0; index < blocks.Count; index++)
        {
            if (blocks[index].Children.Count == 0
                || blocks[index].Children[^1] is not ConditionalBranch)
            {
                continue;
            }

            int merge = postDominators.ImmediatePostDominator(index);
            if (merge == PostDominators.VirtualExit)
            {
                virtualExitDecisions.Add(index);
            }
            else if (merge == PostDominators.None)
            {
                unrootedDecisions.Add(index);
            }
            else if (merge > index && !backEdgeSources.Contains(index))
            {
                forwardRegions.Add(new(
                    StructuringJoinRegionKind.Forward,
                    index,
                    merge,
                    merge,
                    [],
                    IsNonCrossing: true,
                    IsBackEdgeEntangled: false));
            }
        }

        var backEdgeRegions = ImmutableArray.CreateBuilder<StructuringJoinRegion>();
        foreach (var (head, sourceList) in backEdgesByHead)
        {
            int end = sourceList.Max() + 1;
            bool reachesVirtualExit = false;
            var exits = new HashSet<int>();
            for (int source = head; source < end; source++)
            {
                foreach (int target in edges[source].Successors)
                {
                    if (target < head || target >= end)
                        exits.Add(target);
                }
                reachesVirtualExit |= edges[source].ExitsMethod
                    || edges[source].ExternalTargets.Count > 0
                    || edges[source].LeavesRegion;
            }

            int merge = reachesVirtualExit
                ? PostDominators.VirtualExit
                : exits.Count > 0
                    ? postDominators.NearestCommonPostDominator([.. exits.Order()])
                    : PostDominators.None;
            backEdgeRegions.Add(new(
                StructuringJoinRegionKind.BackEdge,
                head,
                end,
                merge,
                [.. sourceList.Distinct().Order()],
                IsNonCrossing: true,
                IsBackEdgeEntangled: false));
        }

        var classifiedForwardRegions = forwardRegions.ToArray();
        var classifiedBackEdgeRegions = backEdgeRegions.ToArray();
        for (int index = 0; index < classifiedForwardRegions.Length; index++)
        {
            bool crossesAnotherRegion = classifiedForwardRegions
                .Where((_, otherIndex) => otherIndex != index)
                .Any(other => Crosses(classifiedForwardRegions[index], other))
                || classifiedBackEdgeRegions.Any(other => Crosses(classifiedForwardRegions[index], other));
            bool backEdgeEntangled = classifiedBackEdgeRegions
                .Any(other => Overlaps(classifiedForwardRegions[index], other));
            classifiedForwardRegions[index] = classifiedForwardRegions[index] with
            {
                IsNonCrossing = !crossesAnotherRegion,
                IsBackEdgeEntangled = backEdgeEntangled,
            };
        }

        for (int index = 0; index < classifiedBackEdgeRegions.Length; index++)
        {
            bool crossesAnotherRegion = classifiedBackEdgeRegions
                .Where((_, otherIndex) => otherIndex != index)
                .Any(other => Crosses(classifiedBackEdgeRegions[index], other))
                || classifiedForwardRegions.Any(other => Crosses(classifiedBackEdgeRegions[index], other));
            classifiedBackEdgeRegions[index] = classifiedBackEdgeRegions[index] with
            {
                IsNonCrossing = !crossesAnotherRegion,
                IsBackEdgeEntangled = classifiedForwardRegions
                    .Any(other => Overlaps(classifiedBackEdgeRegions[index], other)),
            };
        }

        var sortedForwardRegions = classifiedForwardRegions
            .OrderBy(region => region.End - region.Start)
            .ThenByDescending(region => region.Start)
            .ToImmutableArray();
        var sortedBackEdgeRegions = classifiedBackEdgeRegions
            .OrderBy(region => region.End - region.Start)
            .ThenByDescending(region => region.Start)
            .ToImmutableArray();
        var regions = sortedForwardRegions
            .Concat(sortedBackEdgeRegions)
            .OrderBy(region => region.End - region.Start)
            .ThenByDescending(region => region.Start)
            .ThenBy(region => region.Kind)
            .ToImmutableArray();

        return new(
            regions,
            sortedForwardRegions,
            sortedBackEdgeRegions,
            virtualExitDecisions.ToImmutable(),
            unrootedDecisions.ToImmutable());
    }

    static bool Crosses(StructuringJoinRegion first, StructuringJoinRegion second)
    {
        var (earlier, later) = first.Start <= second.Start
            ? (first, second)
            : (second, first);
        return earlier.Start < later.Start
            && later.Start < earlier.End
            && earlier.End < later.End;
    }

    static bool Overlaps(StructuringJoinRegion first, StructuringJoinRegion second)
        => first.Start < second.End && second.Start < first.End;
}
