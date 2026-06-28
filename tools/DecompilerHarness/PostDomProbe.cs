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
        ExitMerge,              // arms flow to the method exit independently
        MultiMerge,             // residual conditionals join at >1 distinct block
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
        return 0;
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

        var pd = PostDominators.Of(blocks);

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
                merges.Add(ipdom);
        }

        if (merges.Count == 0)
            return MergeShape.ExitMerge;
        if (merges.Count > 1)
            return MergeShape.MultiMerge;

        // Exactly one real-block join. If a residual conditional also flowed
        // straight to the exit, the arms are not a clean single diamond.
        if (anyExit)
            return MergeShape.MultiMerge;

        int merge = merges.Single();
        return IsShortReturnTail(blocks[merge])
            ? MergeShape.SingleMergeReturnTail
            : MergeShape.SingleMerge;
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
        var pd = PostDominators.Of(blocks);

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
