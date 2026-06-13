using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler;

namespace ILInspector.Decompiler.Tests;

public class ControlFlowStructuringTests
{
    // --- Dominator tree ---

    [Fact]
    public void DominatorTree_Add_EntryDominatesAll()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.Add));
        var domTree = DominatorTree.Build(cfg);

        for (int i = 0; i < cfg.BasicBlocks.Count; i++)
            Assert.True(domTree.Dominates(0, i), $"Block 0 should dominate Block {i}");
    }

    [Fact]
    public void DominatorTree_EntryHasNoImmediateDominator()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.Add));
        var domTree = DominatorTree.Build(cfg);

        Assert.Equal(-1, domTree.GetImmediateDominator(0));
    }

    [Fact]
    public void DominatorTree_LoopSum_MultipleBlocks()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.LoopSum));
        var domTree = DominatorTree.Build(cfg);

        Assert.True(cfg.BasicBlocks.Count >= 3, "LoopSum should have at least 3 blocks");

        // Entry should dominate all blocks
        for (int i = 0; i < cfg.BasicBlocks.Count; i++)
            Assert.True(domTree.Dominates(0, i));
    }

    [Fact]
    public void DominatorTree_SelfDomination()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.IsPositive));
        var domTree = DominatorTree.Build(cfg);

        for (int i = 0; i < cfg.BasicBlocks.Count; i++)
            Assert.True(domTree.Dominates(i, i), $"Block {i} should dominate itself");
    }

    [Fact]
    public void DominatorTree_Children_EntryHasChildren()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.LoopSum));
        var domTree = DominatorTree.Build(cfg);

        var children = domTree.GetDominatorTreeChildren(0);
        Assert.NotEmpty(children);
    }

    [Fact]
    public void DominatorTree_Depth_EntryIsZero()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.Add));
        var domTree = DominatorTree.Build(cfg);

        Assert.Equal(0, domTree.GetDepth(0));
    }

    // --- Loop detection ---

    [Fact]
    public void LoopDetector_LoopSum_FindsOneLoop()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.LoopSum));
        var domTree = DominatorTree.Build(cfg);
        var loops = LoopDetector.DetectLoops(cfg, domTree);

        Assert.NotEmpty(loops);
    }

    [Fact]
    public void LoopDetector_LoopSum_LoopHasBackEdge()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.LoopSum));
        var domTree = DominatorTree.Build(cfg);
        var loops = LoopDetector.DetectLoops(cfg, domTree);

        Assert.All(loops, loop =>
        {
            Assert.NotEmpty(loop.BackEdgeSources);
            Assert.True(loop.BodyIndices.Count >= 2, "Loop body should have at least 2 blocks");
        });
    }

    [Fact]
    public void LoopDetector_Add_NoLoops()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.Add));
        var domTree = DominatorTree.Build(cfg);
        var loops = LoopDetector.DetectLoops(cfg, domTree);

        Assert.Empty(loops);
    }

    [Fact]
    public void LoopDetector_HeaderDominatesBody()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.LoopSum));
        var domTree = DominatorTree.Build(cfg);
        var loops = LoopDetector.DetectLoops(cfg, domTree);

        foreach (var loop in loops)
        {
            foreach (int bodyIdx in loop.BodyIndices)
            {
                Assert.True(domTree.Dominates(loop.HeaderIndex, bodyIdx),
                    $"Loop header {loop.HeaderIndex} should dominate body block {bodyIdx}");
            }
        }
    }

    // --- Conditional detection ---

    [Fact]
    public void ConditionalDetector_Classify_FindsConditional()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.Classify));
        var domTree = DominatorTree.Build(cfg);
        var loops = LoopDetector.DetectLoops(cfg, domTree);
        var conditionals = ConditionalDetector.DetectConditionals(cfg, domTree, loops);

        Assert.NotEmpty(conditionals);
    }

    [Fact]
    public void ConditionalDetector_Add_NoConditionals()
    {
        var (cfg, _) = CreateCfg(nameof(CfgSampleClass.Add));
        var domTree = DominatorTree.Build(cfg);
        var loops = LoopDetector.DetectLoops(cfg, domTree);
        var conditionals = ConditionalDetector.DetectConditionals(cfg, domTree, loops);

        Assert.Empty(conditionals);
    }

    // --- Structured control flow ---

    [Fact]
    public void StructuredControlFlow_Add_HasSequence()
    {
        var context = CreateContext(nameof(CfgSampleClass.Add));
        var result = StructuredControlFlow.Analyze(context!);

        Assert.Equal(StructuredBlockKind.Sequence, result.Root.Kind);
        Assert.NotEmpty(result.Root.Children);
    }

    [Fact]
    public void StructuredControlFlow_LoopSum_HasLoop()
    {
        var context = CreateContext(nameof(CfgSampleClass.LoopSum));
        var result = StructuredControlFlow.Analyze(context!);

        Assert.NotEmpty(result.Loops);
        bool hasLoopBlock = result.Root.Children.Any(c => c.Kind == StructuredBlockKind.Loop);
        Assert.True(hasLoopBlock, "Expected a Loop structured block");
    }

    [Fact]
    public void StructuredControlFlow_Classify_HasConditional()
    {
        var context = CreateContext(nameof(CfgSampleClass.Classify));
        var result = StructuredControlFlow.Analyze(context!);

        Assert.NotEmpty(result.Conditionals);
    }

    [Fact]
    public void StructuredControlFlow_TryCatch_HasExceptionRegion()
    {
        var context = CreateContext(nameof(CfgSampleClass.TryCatch));
        var result = StructuredControlFlow.Analyze(context!);

        Assert.NotEmpty(result.ExceptionRegions);
    }

    [Fact]
    public void StructuredControlFlow_ToString_IsReadable()
    {
        var context = CreateContext(nameof(CfgSampleClass.LoopSum));
        var result = StructuredControlFlow.Analyze(context!);

        string output = result.Root.ToString();
        Assert.NotEmpty(output);
        Assert.Contains("Block_", output);
    }

    // --- Platform stress test ---

    [Fact]
    public void PlatformAssembly_AnalyzeAll_NoCrashes()
    {
        var assembly = typeof(object).Assembly;
        var path = assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        int totalMethods = 0;
        int analyzed = 0;
        List<string> failures = [];

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                totalMethods++;

                try
                {
                    var context = MethodBodyContext.Create(peReader, reader, method);
                    if (context is null) continue;

                    var result = StructuredControlFlow.Analyze(context);
                    Assert.NotNull(result.Root);
                    analyzed++;
                }
                catch (Exception ex)
                {
                    string typeName = reader.GetString(typeDef.Name);
                    string methodName = reader.GetString(method.Name);
                    failures.Add($"{typeName}::{methodName}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        Assert.True(totalMethods > 1000);
        Assert.True(analyzed > 1000);

        double failureRate = (double)failures.Count / totalMethods;
        Assert.True(failureRate < 0.02,
            $"Analyze failed for {failures.Count}/{totalMethods} ({failureRate:P1}):\n" +
            string.Join("\n", failures.Take(10)));
    }

    // --- Helpers ---

    static (ControlFlowGraph Cfg, MethodBodyContext Context) CreateCfg(string methodName)
    {
        var context = CreateContext(methodName);
        var cfg = ControlFlowGraph.Create(context!);
        return (cfg, context!);
    }

    static MethodBodyContext? CreateContext(string methodName)
    {
        var assemblyPath = typeof(CfgSampleClass).Assembly.Location;
        var stream = File.OpenRead(assemblyPath);
        var peReader = new PEReader(stream);
        return MethodBodyContext.Create(
            peReader,
            typeof(CfgSampleClass).FullName!,
            methodName);
    }
}
