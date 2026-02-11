using System.Reflection.Metadata;
using System.Text;

namespace DotnetInspector.Decompiler;

/// <summary>
/// Kinds of structured control flow nodes.
/// </summary>
public enum StructuredBlockKind
{
    /// <summary>A sequential list of structured blocks.</summary>
    Sequence,

    /// <summary>A single basic block with its ILAst nodes.</summary>
    BasicBlock,

    /// <summary>An if/then/else conditional.</summary>
    IfThenElse,

    /// <summary>A natural loop (while/do-while/for).</summary>
    Loop,

    /// <summary>A try/catch/finally block.</summary>
    TryCatchFinally,

    /// <summary>A switch statement.</summary>
    Switch
}

/// <summary>
/// A structured control flow node representing recovered high-level structure.
/// </summary>
public class StructuredBlock
{
    public StructuredBlockKind Kind { get; init; }

    /// <summary>Block index in the CFG (for BasicBlock kind).</summary>
    public int BlockIndex { get; init; } = -1;

    /// <summary>Child blocks (for Sequence, Loop body, etc.).</summary>
    public List<StructuredBlock> Children { get; init; } = [];

    /// <summary>Child blocks in the try body (for TryCatchFinally).</summary>
    public List<StructuredBlock> TryChildren { get; init; } = [];

    /// <summary>Child blocks in the handler body (for TryCatchFinally).</summary>
    public List<StructuredBlock> HandlerChildren { get; init; } = [];

    /// <summary>Condition block index (for IfThenElse).</summary>
    public int ConditionBlockIndex { get; init; } = -1;

    /// <summary>Then branch (for IfThenElse).</summary>
    public StructuredBlock? ThenBlock { get; init; }

    /// <summary>Else branch (for IfThenElse, may be null).</summary>
    public StructuredBlock? ElseBlock { get; init; }

    /// <summary>Whether the condition should be negated when emitting.</summary>
    public bool NegateCondition { get; init; }

    /// <summary>Loop header block index (for Loop).</summary>
    public int LoopHeaderIndex { get; init; } = -1;

    /// <summary>Exception region (for TryCatchFinally).</summary>
    public ExceptionRegion? ExceptionRegion { get; init; }

    /// <summary>Label for the block.</summary>
    public string? Label { get; init; }

    public void WriteTo(StringBuilder sb, int indent = 0)
    {
        switch (Kind)
        {
            case StructuredBlockKind.Sequence:
                foreach (var child in Children)
                    child.WriteTo(sb, indent);
                break;

            case StructuredBlockKind.BasicBlock:
                WriteIndent(sb, indent);
                sb.AppendLine($"Block_{BlockIndex}:");
                break;

            case StructuredBlockKind.IfThenElse:
                WriteIndent(sb, indent);
                sb.AppendLine($"if (Block_{ConditionBlockIndex}) {{");
                ThenBlock?.WriteTo(sb, indent + 1);
                if (ElseBlock is not null)
                {
                    WriteIndent(sb, indent);
                    sb.AppendLine("} else {");
                    ElseBlock.WriteTo(sb, indent + 1);
                }
                WriteIndent(sb, indent);
                sb.AppendLine("}");
                break;

            case StructuredBlockKind.Loop:
                WriteIndent(sb, indent);
                sb.AppendLine($"loop (header: Block_{LoopHeaderIndex}) {{");
                foreach (var child in Children)
                    child.WriteTo(sb, indent + 1);
                WriteIndent(sb, indent);
                sb.AppendLine("}");
                break;

            case StructuredBlockKind.TryCatchFinally:
                WriteIndent(sb, indent);
                if (ExceptionRegion?.Kind == ExceptionRegionKind.Finally)
                    sb.AppendLine("try { ... } finally { ... }");
                else
                    sb.AppendLine("try { ... } catch { ... }");
                break;

            case StructuredBlockKind.Switch:
                WriteIndent(sb, indent);
                sb.AppendLine("switch { ... }");
                break;
        }
    }

    static void WriteIndent(StringBuilder sb, int indent)
    {
        for (int i = 0; i < indent; i++)
            sb.Append("    ");
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        WriteTo(sb);
        return sb.ToString();
    }
}

/// <summary>
/// Result of control flow structuring analysis.
/// </summary>
public sealed class StructuredControlFlow
{
    /// <summary>The root structured block.</summary>
    public StructuredBlock Root { get; }

    /// <summary>The dominator tree used for analysis.</summary>
    public DominatorTree DominatorTree { get; }

    /// <summary>Detected natural loops.</summary>
    public List<NaturalLoop> Loops { get; }

    /// <summary>Detected conditional patterns.</summary>
    public List<ConditionalPattern> Conditionals { get; }

    /// <summary>Exception regions from metadata.</summary>
    public List<ExceptionRegionInfo> ExceptionRegions { get; }

    StructuredControlFlow(
        StructuredBlock root,
        DominatorTree domTree,
        List<NaturalLoop> loops,
        List<ConditionalPattern> conditionals,
        List<ExceptionRegionInfo> exceptionRegions)
    {
        Root = root;
        DominatorTree = domTree;
        Loops = loops;
        Conditionals = conditionals;
        ExceptionRegions = exceptionRegions;
    }

    /// <summary>
    /// Analyze a method's control flow and produce structured representation.
    /// </summary>
    public static StructuredControlFlow Analyze(MethodBodyContext context)
    {
        var cfg = ControlFlowGraph.Create(context);
        return Analyze(context, cfg);
    }

    /// <summary>
    /// Analyze control flow from a pre-computed CFG.
    /// </summary>
    public static StructuredControlFlow Analyze(MethodBodyContext context, ControlFlowGraph cfg)
    {
        var domTree = DominatorTree.Build(cfg);
        var loops = LoopDetector.DetectLoops(cfg, domTree);
        var conditionals = ConditionalDetector.DetectConditionals(cfg, domTree, loops);
        var exRegions = MapExceptionRegions(context, cfg);

        var root = BuildStructuredTree(cfg, domTree, loops, conditionals, exRegions);

        return new StructuredControlFlow(root, domTree, loops, conditionals, exRegions);
    }

    static StructuredBlock BuildStructuredTree(
        ControlFlowGraph cfg,
        DominatorTree domTree,
        List<NaturalLoop> loops,
        List<ConditionalPattern> conditionals,
        List<ExceptionRegionInfo> exRegions)
    {
        // Build lookup tables
        var loopHeaders = new Dictionary<int, NaturalLoop>();
        foreach (var loop in loops)
            loopHeaders[loop.HeaderIndex] = loop;

        // Map each loop body block to its loop, so we can trigger loop emission
        // from the first body block encountered (not just the header).
        // Roslyn's for-over-array pattern places body blocks BEFORE the header
        // (header = condition check at the end), so we must detect them early.
        var blockToLoop = new Dictionary<int, NaturalLoop>();
        foreach (var loop in loops)
        {
            foreach (int bodyIdx in loop.BodyIndices)
                blockToLoop.TryAdd(bodyIdx, loop);
        }

        var conditionBlocks = new Dictionary<int, ConditionalPattern>();
        foreach (var cond in conditionals)
            conditionBlocks[cond.ConditionIndex] = cond;

        var exRegionsByBlock = new Dictionary<int, ExceptionRegionInfo>();
        foreach (var region in exRegions)
        {
            if (region.TryStartBlockIndex >= 0)
                exRegionsByBlock.TryAdd(region.TryStartBlockIndex, region);
        }

        // Build the tree by walking blocks in order
        var processed = new HashSet<int>();
        var children = new List<StructuredBlock>();

        // Pre-compute which blocks are inside exception regions
        var inExceptionRegion = new Dictionary<int, ExceptionRegionInfo>();
        foreach (var region in exRegions)
        {
            int tryEnd = region.Region.TryOffset + region.Region.TryLength;
            int handlerEnd = region.Region.HandlerOffset + region.Region.HandlerLength;
            for (int b = 0; b < cfg.BasicBlocks.Count; b++)
            {
                int start = cfg.BasicBlocks[b].Start;
                if ((start >= region.Region.TryOffset && start < tryEnd) ||
                    (start >= region.Region.HandlerOffset && start < handlerEnd))
                {
                    inExceptionRegion.TryAdd(b, region);
                }
            }
        }

        for (int i = 0; i < cfg.BasicBlocks.Count; i++)
        {
            if (processed.Contains(i)) continue;

            // Exception regions take priority — process all blocks in the region together
            if (exRegionsByBlock.TryGetValue(i, out var exRegion))
            {
                // Collect all blocks in the try body, recursively structuring conditionals within
                var tryChildren = new List<StructuredBlock>();
                int tryEnd = exRegion.Region.TryOffset + exRegion.Region.TryLength;
                for (int t = i; t < cfg.BasicBlocks.Count; t++)
                {
                    if (cfg.BasicBlocks[t].Start >= tryEnd) break;
                    if (processed.Contains(t)) continue;
                    processed.Add(t);

                    // Check for conditionals within the try body
                    if (conditionBlocks.TryGetValue(t, out var innerCond))
                    {
                        var thenBlock = new StructuredBlock
                        {
                            Kind = StructuredBlockKind.BasicBlock,
                            BlockIndex = innerCond.ThenIndex,
                            Label = $"Block_{innerCond.ThenIndex}"
                        };
                        if (cfg.BasicBlocks[innerCond.ThenIndex].Start < tryEnd)
                            processed.Add(innerCond.ThenIndex);

                        StructuredBlock? elseBlock = null;
                        if (innerCond.ElseIndex >= 0)
                        {
                            elseBlock = new StructuredBlock
                            {
                                Kind = StructuredBlockKind.BasicBlock,
                                BlockIndex = innerCond.ElseIndex,
                                Label = $"Block_{innerCond.ElseIndex}"
                            };
                            if (cfg.BasicBlocks[innerCond.ElseIndex].Start < tryEnd)
                                processed.Add(innerCond.ElseIndex);
                        }

                        tryChildren.Add(new StructuredBlock
                        {
                            Kind = StructuredBlockKind.IfThenElse,
                            ConditionBlockIndex = t,
                            ThenBlock = thenBlock,
                            ElseBlock = elseBlock,
                            NegateCondition = innerCond.NegateCondition
                        });
                    }
                    else
                    {
                        tryChildren.Add(new StructuredBlock
                        {
                            Kind = StructuredBlockKind.BasicBlock,
                            BlockIndex = t,
                            Label = $"Block_{t}"
                        });
                    }
                }

                // Collect all blocks in the handler body
                var handlerChildren = new List<StructuredBlock>();
                int handlerEnd = exRegion.Region.HandlerOffset + exRegion.Region.HandlerLength;
                for (int h = exRegion.HandlerStartBlockIndex; h >= 0 && h < cfg.BasicBlocks.Count; h++)
                {
                    if (cfg.BasicBlocks[h].Start >= handlerEnd) break;
                    if (processed.Contains(h)) continue;
                    processed.Add(h);

                    // Check for conditionals within the handler
                    if (conditionBlocks.TryGetValue(h, out var handlerCond))
                    {
                        var thenBlock = new StructuredBlock
                        {
                            Kind = StructuredBlockKind.BasicBlock,
                            BlockIndex = handlerCond.ThenIndex,
                            Label = $"Block_{handlerCond.ThenIndex}"
                        };
                        if (cfg.BasicBlocks[handlerCond.ThenIndex].Start < handlerEnd)
                            processed.Add(handlerCond.ThenIndex);

                        StructuredBlock? elseBlock = null;
                        if (handlerCond.ElseIndex >= 0)
                        {
                            elseBlock = new StructuredBlock
                            {
                                Kind = StructuredBlockKind.BasicBlock,
                                BlockIndex = handlerCond.ElseIndex,
                                Label = $"Block_{handlerCond.ElseIndex}"
                            };
                            if (cfg.BasicBlocks[handlerCond.ElseIndex].Start < handlerEnd)
                                processed.Add(handlerCond.ElseIndex);
                        }

                        handlerChildren.Add(new StructuredBlock
                        {
                            Kind = StructuredBlockKind.IfThenElse,
                            ConditionBlockIndex = h,
                            ThenBlock = thenBlock,
                            ElseBlock = elseBlock,
                            NegateCondition = handlerCond.NegateCondition
                        });
                    }
                    else
                    {
                        handlerChildren.Add(new StructuredBlock
                        {
                            Kind = StructuredBlockKind.BasicBlock,
                            BlockIndex = h,
                            Label = $"Block_{h}"
                        });
                    }
                }

                children.Add(new StructuredBlock
                {
                    Kind = StructuredBlockKind.TryCatchFinally,
                    BlockIndex = i,
                    ExceptionRegion = exRegion.Region,
                    TryChildren = tryChildren,
                    HandlerChildren = handlerChildren
                });
                continue;
            }

            if (blockToLoop.TryGetValue(i, out var loop))
            {
                var loopChildren = new List<StructuredBlock>();
                foreach (int bodyIdx in loop.BodyIndices.OrderBy(x => x))
                {
                    processed.Add(bodyIdx);
                    loopChildren.Add(new StructuredBlock
                    {
                        Kind = StructuredBlockKind.BasicBlock,
                        BlockIndex = bodyIdx,
                        Label = $"Block_{bodyIdx}"
                    });
                }

                children.Add(new StructuredBlock
                {
                    Kind = StructuredBlockKind.Loop,
                    LoopHeaderIndex = loop.HeaderIndex,
                    Children = loopChildren
                });
                continue;
            }

            if (conditionBlocks.TryGetValue(i, out var cond))
            {
                var thenBlock = new StructuredBlock
                {
                    Kind = StructuredBlockKind.BasicBlock,
                    BlockIndex = cond.ThenIndex,
                    Label = $"Block_{cond.ThenIndex}"
                };
                processed.Add(cond.ThenIndex);

                StructuredBlock? elseBlock = null;
                if (cond.ElseIndex >= 0)
                {
                    elseBlock = new StructuredBlock
                    {
                        Kind = StructuredBlockKind.BasicBlock,
                        BlockIndex = cond.ElseIndex,
                        Label = $"Block_{cond.ElseIndex}"
                    };
                    processed.Add(cond.ElseIndex);
                }

                processed.Add(i);
                children.Add(new StructuredBlock
                {
                    Kind = StructuredBlockKind.IfThenElse,
                    ConditionBlockIndex = i,
                    ThenBlock = thenBlock,
                    ElseBlock = elseBlock,
                    NegateCondition = cond.NegateCondition
                });
                continue;
            }

            processed.Add(i);
            children.Add(new StructuredBlock
            {
                Kind = StructuredBlockKind.BasicBlock,
                BlockIndex = i,
                Label = $"Block_{i}"
            });
        }

        return new StructuredBlock
        {
            Kind = StructuredBlockKind.Sequence,
            Children = children
        };
    }

    static List<ExceptionRegionInfo> MapExceptionRegions(MethodBodyContext context, ControlFlowGraph cfg)
    {
        List<ExceptionRegionInfo> result = [];
        foreach (var region in context.ExceptionRegions)
        {
            int tryStart = cfg.LookupIndex(region.TryOffset);
            int handlerStart = cfg.LookupIndex(region.HandlerOffset);
            result.Add(new ExceptionRegionInfo(region, tryStart, handlerStart));
        }
        return result;
    }
}

/// <summary>
/// Maps an <see cref="ExceptionRegion"/> to its block indices in the CFG.
/// </summary>
public sealed record ExceptionRegionInfo(
    ExceptionRegion Region,
    int TryStartBlockIndex,
    int HandlerStartBlockIndex);
