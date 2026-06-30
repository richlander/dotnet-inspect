using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;
using ILInspector.ControlFlow;
using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Phase 1 directional differential: prove the substrate's block builder is a regression-free
/// replacement for <c>ReachingDefinitions</c>'s (the Analysis-first cutover target). Block start,
/// end, and outgoing edges must match exactly over real IL — edges included, because reaching-defs
/// dataflow depends on them. A diff is triaged improvement/regression; for clean IL the decoders
/// agree, so exact parity is expected and any divergence is a real finding.
/// </summary>
public class BlockParityTests
{
    [Fact]
    public void Blocks_match_reaching_definitions_over_the_test_assembly()
    {
        var (methods, mismatches) = Compare(typeof(BlockParityTests).Assembly.Location);
        Assert.True(methods > 50, $"expected to exercise many method bodies, saw {methods}");
        Assert.True(mismatches.Count == 0, Report(mismatches));
    }

    [Fact]
    public void Blocks_match_reaching_definitions_over_corelib()
    {
        // Corpus-scale cutover evidence: the substrate's blocks (start/end/edges) must equal
        // ReachingDefinitions' across all of System.Private.CoreLib, or the diff is a finding.
        var (methods, mismatches) = Compare(typeof(object).Assembly.Location);
        Assert.True(methods > 10000, $"expected the full CoreLib body set, saw {methods}");
        Assert.True(mismatches.Count == 0, Report(mismatches));
    }

    static (int Methods, List<string> Mismatches) Compare(string assemblyPath)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var reader = pe.GetMetadataReader();

        int methods = 0;
        var mismatches = new List<string>();
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            MethodBodyBlock body;
            try { body = pe.GetMethodBody(method.RelativeVirtualAddress); }
            catch { continue; }
            byte[] il = body.GetILBytes() ?? [];
            if (il.Length == 0)
                continue;

            methods++;
            string name = reader.GetString(method.Name);

            ImmutableArray<InstructionBlock> substrate;
            ImmutableArray<(int Start, int End, BlockEdges Edges)> reaching;
            try
            {
                substrate = MethodInstructions.Decode(il, il.Length, body.ExceptionRegions).Blocks.Blocks;
                reaching = ReachingDefinitions.BlocksForParity(il, body.ExceptionRegions);
            }
            catch (Exception ex)
            {
                mismatches.Add($"{name}: threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            string? diff = DiffBlocks(substrate, reaching);
            if (diff is not null)
                mismatches.Add($"{name}: {diff}");
        }

        return (methods, mismatches);
    }

    static string? DiffBlocks(ImmutableArray<InstructionBlock> substrate, ImmutableArray<(int Start, int End, BlockEdges Edges)> reaching)
    {
        if (substrate.Length != reaching.Length)
            return $"block count {substrate.Length} vs {reaching.Length}";
        for (int i = 0; i < substrate.Length; i++)
        {
            var s = substrate[i];
            var r = reaching[i];
            if (s.Start != r.Start || s.End != r.End)
                return $"block {i} span [{s.Start},{s.End}) vs [{r.Start},{r.End})";
            if (!s.Edges.Successors.SequenceEqual(r.Edges.Successors))
                return $"block {i} successors [{string.Join(",", s.Edges.Successors)}] vs [{string.Join(",", r.Edges.Successors)}]";
            if (!s.Edges.ExternalTargets.SequenceEqual(r.Edges.ExternalTargets))
                return $"block {i} external [{string.Join(",", s.Edges.ExternalTargets)}] vs [{string.Join(",", r.Edges.ExternalTargets)}]";
            if (s.Edges.ExitsMethod != r.Edges.ExitsMethod)
                return $"block {i} exits {s.Edges.ExitsMethod} vs {r.Edges.ExitsMethod}";
            if (s.Edges.LeavesRegion != r.Edges.LeavesRegion)
                return $"block {i} leaves {s.Edges.LeavesRegion} vs {r.Edges.LeavesRegion}";
        }
        return null;
    }

    static string Report(List<string> mismatches)
        => $"{mismatches.Count} block mismatch(es):\n{string.Join("\n", mismatches.Take(15))}";
}
