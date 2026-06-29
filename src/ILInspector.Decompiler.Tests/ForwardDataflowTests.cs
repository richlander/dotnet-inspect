using ILInspector.ControlFlow;

namespace ILInspector.Decompiler.Tests;

public class ForwardDataflowTests
{
    [Fact]
    public void Solve_Intersection_DropsFactsMissingOnOnePredecessor()
    {
        var edges = new[]
        {
            Edge(1, 2),
            Edge(3),
            Edge(3),
            Exit(),
        };
        var transfers = new[]
        {
            Gen(),
            Gen(0),
            Gen(),
            Gen(),
        };

        var result = ForwardDataflow.Solve(
            edges,
            transfers,
            entry: new HashSet<int>(),
            universe: new HashSet<int> { 0 },
            DataflowMerge.Intersection);

        Assert.Empty(result.Blocks[3].In);
        Assert.Equal([0], result.Blocks[1].Out.Order());
        Assert.Empty(result.Blocks[2].Out);
    }

    [Fact]
    public void Solve_Intersection_KeepsFactsGeneratedOnEveryPredecessor()
    {
        var edges = new[]
        {
            Edge(1, 2),
            Edge(3),
            Edge(3),
            Exit(),
        };
        var transfers = new[]
        {
            Gen(),
            Gen(0),
            Gen(0),
            Gen(),
        };

        var result = ForwardDataflow.Solve(
            edges,
            transfers,
            entry: new HashSet<int>(),
            universe: new HashSet<int> { 0 },
            DataflowMerge.Intersection);

        Assert.Equal([0], result.Blocks[3].In.Order());
    }

    [Fact]
    public void Solve_Union_AppliesGenKillAndMergesAnyPredecessorFact()
    {
        var edges = new[]
        {
            Edge(1, 2),
            Edge(3),
            Edge(3),
            Exit(),
        };
        var transfers = new[]
        {
            Gen(0),
            new GenKillSet(new HashSet<int>(), new HashSet<int> { 0 }),
            Gen(1),
            Gen(),
        };

        var result = ForwardDataflow.Solve(
            edges,
            transfers,
            entry: new HashSet<int>(),
            universe: new HashSet<int> { 0, 1 },
            DataflowMerge.Union);

        Assert.Equal([0], result.Blocks[1].In.Order());
        Assert.Empty(result.Blocks[1].Out);
        Assert.Equal([0, 1], result.Blocks[3].In.Order());
    }

    [Fact]
    public void Solve_MergePredecessors_IncludesEntryBackEdges()
    {
        var edges = new[]
        {
            Edge(1),
            Edge(0),
        };
        var transfers = new[]
        {
            Gen(),
            Gen(1),
        };

        var result = ForwardDataflow.Solve(
            edges,
            transfers,
            entry: new HashSet<int> { 0 },
            universe: new HashSet<int> { 0, 1 },
            DataflowMerge.Union,
            DataflowEntry.MergePredecessors);

        Assert.Equal([0, 1], result.Blocks[0].In.Order());
        Assert.Equal([0, 1], result.Blocks[0].Out.Order());
    }

    [Fact]
    public void Solve_FixedEntry_IgnoresEntryBackEdges()
    {
        var edges = new[]
        {
            Edge(1),
            Edge(0),
        };
        var transfers = new[]
        {
            Gen(),
            Gen(1),
        };

        var result = ForwardDataflow.Solve(
            edges,
            transfers,
            entry: new HashSet<int> { 0 },
            universe: new HashSet<int> { 0, 1 },
            DataflowMerge.Union);

        Assert.Equal([0], result.Blocks[0].In.Order());
        Assert.Equal([0], result.Blocks[0].Out.Order());
    }

    [Fact]
    public void Solve_IgnoresUnreachablePredecessorsWhenMergingReachableBlock()
    {
        var edges = new[]
        {
            Edge(2),
            Edge(2),
            Exit(),
        };
        var transfers = new[]
        {
            Gen(0),
            new GenKillSet(new HashSet<int>(), new HashSet<int> { 0 }),
            Gen(),
        };

        var result = ForwardDataflow.Solve(
            edges,
            transfers,
            entry: new HashSet<int>(),
            universe: new HashSet<int> { 0 },
            DataflowMerge.Intersection);

        Assert.False(result.Blocks[1].Reachable);
        Assert.Equal([0], result.Blocks[2].In.Order());
    }

    [Fact]
    public void Solve_MarksBlocksUnreachableFromEntry()
    {
        var edges = new[]
        {
            Edge(1),
            Exit(),
            Edge(2),
        };
        var transfers = new[] { Gen(), Gen(), Gen(0) };

        var result = ForwardDataflow.Solve(
            edges,
            transfers,
            entry: new HashSet<int>(),
            universe: new HashSet<int> { 0 },
            DataflowMerge.Intersection);

        Assert.True(result.Blocks[0].Reachable);
        Assert.True(result.Blocks[1].Reachable);
        Assert.False(result.Blocks[2].Reachable);
    }

    static BlockEdges Edge(params int[] successors)
        => new(successors, [], ExitsMethod: false, LeavesRegion: false);

    static BlockEdges Exit()
        => new([], [], ExitsMethod: true, LeavesRegion: false);

    static GenKillSet Gen(params int[] facts)
        => new(new HashSet<int>(facts), new HashSet<int>());
}
