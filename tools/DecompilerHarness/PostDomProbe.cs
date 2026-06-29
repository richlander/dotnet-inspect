using ILInspector.ControlFlow;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Read-only step-4 prototype probe (issue #1175, throwaway). For every method
/// the Default pipeline leaves with a residual <see cref="ConditionalBranch"/>
/// (the <c>structuring: conditional-branch</c> bucket the index-range
/// <see cref="StructuringPass"/> could not consume), this computes the
/// <see cref="PostDominators"/> merge that the index-range model cannot name and
/// reports how the population breaks down by merge shape.
///
/// <para>The whole point is the go/no-go sizing the design doc
/// (docs/design/control-flow-structuring.md, "Soundness strategy") demands
/// before any production change: how many residual-conditional methods have a
/// single post-dominator merge (the Target-A retained-label slice), how many a
/// short return-tail merge (the Target-B cheap-elimination slice), and how many
/// are multi-merge or unrooted (out of the first slice). It mutates nothing: it
/// runs the same passes and only inspects the finished flat container.</para>
/// </summary>
static class PostDomProbe
{
    // A merge block counts as a "short return tail" (Target B, cheap full
    // elimination by inlining) when it is small and ends in a return — the
    // ReturnMergePass-style shape the doc calls out as the clean case.
    const int MaxReturnTailStatements = 3;

    enum MergeShape
    {
        SingleMerge,            // one real-block post-dominator join — Target A
        SingleMergeReturnTail,  // that join is a short return tail — Target B
        MultiMergeNested,       // >1 join, but regions nest/sequence — safe (SingleMerge x N)
        MultiMergeCrossing,     // >1 join whose regions overlap — the genuine hard climb
        ExitMerge,              // arms flow to the method exit independently
        Unrooted,               // a residual conditional cannot reach the exit
        Loop,                   // a back-edge survives — out of forward-structuring scope
    }

    public static int Run(List<string> assemblies, int cap, int sample)
    {
        long total = 0, residualMethods = 0;
        var byShape = new Dictionary<string, long>(StringComparer.Ordinal);
        var byMerge = new Dictionary<MergeShape, (long Count, string Example)>();
        // (shape bucket x merge shape) so the reader sees which residual shapes
        // carry foldable merges.
        var crossTab = new Dictionary<(string, MergeShape), long>();
        // Within the safe non-crossing forward-merge buckets, split by whether the
        // residual is a pure boolean-expression diamond (all guard blocks pure ->
        // extendable via sound OR-combination, no all-or-nothing relaxation) or a
        // genuine merge with effectful guard blocks (needs the structurer).
        long safePureGuards = 0, safeEffectful = 0;
        string? pureExample = null, effectfulExample = null;
        var samples = new List<string>();
        bool capped = false;

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                if (total >= cap) { capped = true; break; }
                total++;

                var context = new PassContext(new Stepper(enabled: false), new StructuringDiagnostics());
                try { IrPasses.Run(function, IrPasses.Default, context); }
                catch { continue; }   // pass bugs are inventoried elsewhere

                var container = ResidualConditionalContainer(function);
                if (container is null)
                    continue;
                residualMethods++;

                string shape = ConditionalBranchShapeClassifier.Classify(function);
                byShape[shape] = byShape.GetValueOrDefault(shape) + 1;

                var merge = ClassifyMerge(container);
                var priorM = byMerge.GetValueOrDefault(merge);
                byMerge[merge] = (priorM.Count + 1, priorM.Example ?? $"{typeName}::{methodName}");
                crossTab[(shape, merge)] = crossTab.GetValueOrDefault((shape, merge)) + 1;

                // The safe slice = non-crossing forward merges with a real join.
                if (merge is MergeShape.SingleMerge or MergeShape.SingleMergeReturnTail or MergeShape.MultiMergeNested)
                {
                    if (AllGuardsPure(container))
                    {
                        safePureGuards++;
                        pureExample ??= $"{typeName}::{methodName}";
                    }
                    else
                    {
                        safeEffectful++;
                        effectfulExample ??= $"{typeName}::{methodName}";
                    }
                }

                if (samples.Count < sample
                    && merge is MergeShape.SingleMerge or MergeShape.SingleMergeReturnTail)
                    samples.Add(Sketch(typeName, methodName, container, merge));
            }
            if (capped) break;
        }

        string scope = capped ? $"{total} methods (capped)" : $"{total} methods";
        Console.WriteLine();
        Console.WriteLine($"post-dominator merge probe over {scope}:");
        Console.WriteLine($"  {residualMethods} methods left with a residual conditional branch");
        Console.WriteLine();
        Console.WriteLine("by post-dominator merge shape:");
        foreach (var entry in byMerge.OrderByDescending(e => e.Value.Count))
            Console.WriteLine($"  {entry.Value.Count,8}  {entry.Key,-24}  e.g. {entry.Value.Example}");

        Console.WriteLine();
        Console.WriteLine("residual conditional-branch shape x merge shape:");
        foreach (var shapeEntry in byShape.OrderByDescending(e => e.Value))
        {
            Console.WriteLine($"  {shapeEntry.Value,8}  {shapeEntry.Key}");
            foreach (var mergeShape in Enum.GetValues<MergeShape>())
                if (crossTab.TryGetValue((shapeEntry.Key, mergeShape), out long n) && n > 0)
                    Console.WriteLine($"           {n,8}  -> {mergeShape}");
        }

        if (samples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"single-merge readability sketches ({samples.Count}):");
            foreach (var s in samples)
            {
                Console.WriteLine();
                Console.WriteLine(s);
            }
        }

        Console.WriteLine();
        Console.WriteLine("safe non-crossing slice (SingleMerge + ReturnTail + MultiMergeNested) by guard purity:");
        Console.WriteLine($"  {safePureGuards,8}  pure-guard boolean diamonds (OR-combination-extensible, no all-or-nothing relaxation)  e.g. {pureExample}");
        Console.WriteLine($"  {safeEffectful,8}  effectful-condition genuine merges (need the structurer)  e.g. {effectfulExample}");
        return 0;
    }

    /// <summary>
    /// Approximates "this residual is a boolean-expression diamond an extended
    /// OR-combination could fold soundly" — mirrors <c>OrChainDiamondPass</c>'s
    /// rule that inner guards are pure single-condition blocks (<c>[ConditionalBranch]</c>),
    /// allowing only the root guard to carry leading operand setup. When at most
    /// one guard block carries extra statements, the guard run is a pure
    /// short-circuit chain (the condition is the only work); two or more
    /// effectful guards mean the decisions do independent side-effecting work — a
    /// genuine merge that needs the structurer, not condition-combining.
    /// </summary>
    static bool AllGuardsPure(BlockContainer container)
    {
        int guardsWithSetup = 0;
        foreach (var block in container.Blocks)
        {
            if (block.Children.Count == 0 || block.Children[^1] is not ConditionalBranch)
                continue;
            // Any control flow among the non-terminal statements disqualifies outright.
            for (int s = 0; s < block.Children.Count - 1; s++)
                if (block.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                    return false;
            if (block.Children.Count > 1)
                guardsWithSetup++;
        }
        return guardsWithSetup <= 1;
    }

    /// <summary>The first container holding a residual flat conditional branch, or null.</summary>
    static BlockContainer? ResidualConditionalContainer(IrFunction function)
    {
        foreach (var c in function.Descendants.OfType<BlockContainer>())
            if (c.Blocks.Any(b => b.Children.Count > 0 && b.Children.Any(n => n is ConditionalBranch)))
                return c;
        return null;
    }

    static MergeShape ClassifyMerge(BlockContainer container)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        // A back-edge (a branch to a block at or before its own index) means an
        // unraised loop survives in the container — out of the acyclic
        // forward-merge first slice (doc step 4 scope), and its post-dominator
        // merge would otherwise masquerade as a foldable return tail.
        for (int i = 0; i < blocks.Count; i++)
            foreach (int target in BranchTargets(blocks[i]))
                if (offsetToIndex.TryGetValue(target, out int ti) && ti <= i)
                    return MergeShape.Loop;

        var pd = PostDominators.Of(Cfg.Build(blocks));

        // Each residual conditional's region is the half-open span [decision, join)
        // from the branch block to its immediate post-dominator. Crossing spans are
        // the interleaved/irreducible shape; nested or sequential (disjoint, or
        // sharing only a boundary) spans are the safe SingleMerge-x-N case.
        var regions = new List<(int Start, int End)>();
        var merges = new HashSet<int>();
        bool anyExit = false;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Children.Count == 0 || blocks[i].Children[^1] is not ConditionalBranch)
                continue;
            int ipdom = pd.ImmediatePostDominator(i);
            if (ipdom == PostDominators.None)
                return MergeShape.Unrooted;
            if (ipdom == PostDominators.VirtualExit)
                anyExit = true;
            else
            {
                merges.Add(ipdom);
                regions.Add((i, ipdom));
            }
        }

        if (merges.Count == 0)
            return MergeShape.ExitMerge;

        if (merges.Count > 1 || anyExit)
        {
            // anyExit alongside a real join means at least one decision escapes
            // while another rejoins — treat the escape as a degenerate region
            // [decision, exit) that spans to the container end.
            if (anyExit)
                for (int i = 0; i < blocks.Count; i++)
                    if (blocks[i].Children.Count > 0 && blocks[i].Children[^1] is ConditionalBranch
                        && pd.ImmediatePostDominator(i) == PostDominators.VirtualExit)
                        regions.Add((i, blocks.Count));
            return AnyCrossing(regions) ? MergeShape.MultiMergeCrossing : MergeShape.MultiMergeNested;
        }

        int merge = merges.Single();
        return IsShortReturnTail(blocks[merge])
            ? MergeShape.SingleMergeReturnTail
            : MergeShape.SingleMerge;
    }

    /// <summary>True when two region spans partially overlap without one nesting the
    /// other — the interleaved (crossing) shape. Disjoint, shared-boundary, and
    /// nested spans are all non-crossing.</summary>
    static bool AnyCrossing(List<(int Start, int End)> regions)
    {
        for (int i = 0; i < regions.Count; i++)
            for (int j = i + 1; j < regions.Count; j++)
            {
                var (a, b) = regions[i].Start <= regions[j].Start ? (regions[i], regions[j]) : (regions[j], regions[i]);
                if (a.Start < b.Start && b.Start < a.End && a.End < b.End)
                    return true;
            }
        return false;
    }

    static IEnumerable<int> BranchTargets(Block block)
    {
        foreach (var node in block.Children)
            switch (node)
            {
                case Branch b: yield return b.TargetOffset; break;
                case ConditionalBranch c: yield return c.TargetOffset; break;
                case SwitchBranch sw:
                    foreach (int t in sw.TargetOffsets) yield return t;
                    break;
                case Leave lv: yield return lv.TargetOffset; break;
            }
    }

    static bool IsShortReturnTail(Block block) =>
        block.Children.Count is > 0 and <= MaxReturnTailStatements
        && block.Children[^1] is Return;

    static string Sketch(string typeName, string methodName, BlockContainer container, MergeShape merge)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;
        var pd = PostDominators.Of(Cfg.Build(blocks));

        var lines = new List<string>
        {
            $"  {typeName}::{methodName}  [{merge}, {blocks.Count} blocks]",
        };
        for (int i = 0; i < blocks.Count; i++)
        {
            var last = blocks[i].Children.Count > 0 ? blocks[i].Children[^1] : null;
            string term = last switch
            {
                ConditionalBranch cb => $"if (…) goto {Label(cb.TargetOffset, offsetToIndex)}  | ipdom={Label(pd, i)}",
                Branch b => $"goto {Label(b.TargetOffset, offsetToIndex)}",
                Return => "return",
                Throw => "throw",
                _ => "(fallthrough)",
            };
            lines.Add($"    B{i,-2} IL_{blocks[i].StartOffset:X4}  {term}");
        }
        return string.Join('\n', lines);
    }

    static string Label(int offset, Dictionary<int, int> offsetToIndex) =>
        offsetToIndex.TryGetValue(offset, out int idx) ? $"B{idx}" : $"IL_{offset:X4}(ext)";

    static string Label(PostDominators pd, int block)
    {
        int ipdom = pd.ImmediatePostDominator(block);
        return ipdom switch
        {
            PostDominators.VirtualExit => "exit",
            PostDominators.None => "none",
            _ => $"B{ipdom}",
        };
    }
}
