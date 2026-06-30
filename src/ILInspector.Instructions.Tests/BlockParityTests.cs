using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;
using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Phase 1 directional differential: prove the substrate's block builder is a regression-free
/// replacement for <c>ReachingDefinitions</c>'s (the Analysis-first cutover target). Block-start
/// offsets must match exactly over real IL. A diff is triaged improvement/regression — for clean
/// IL the decoders agree, so exact parity is expected; any divergence is a real finding.
/// </summary>
public class BlockParityTests
{
    [Fact]
    public void Block_starts_match_reaching_definitions_over_the_test_assembly()
    {
        var (methods, mismatches) = Compare(typeof(BlockParityTests).Assembly.Location);
        Assert.True(methods > 50, $"expected to exercise many method bodies, saw {methods}");
        Assert.True(mismatches.Count == 0, Report(mismatches));
    }

    [Fact]
    public void Block_starts_match_reaching_definitions_over_corelib()
    {
        // Corpus-scale cutover evidence: the substrate's blocks must equal ReachingDefinitions'
        // across all of System.Private.CoreLib, or the diff must be a characterized improvement.
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

            ImmutableArray<int> substrate;
            ImmutableArray<int> reaching;
            try
            {
                substrate = MethodInstructions.Decode(il, il.Length, body.ExceptionRegions)
                    .Blocks.Blocks.Select(b => b.Start).ToImmutableArray();
                reaching = ReachingDefinitions.BlockStartsForParity(il, body.ExceptionRegions);
            }
            catch (Exception ex)
            {
                // One side threw where the other did not — that is itself a difference to record.
                mismatches.Add($"{reader.GetString(method.Name)}: threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (!substrate.AsSpan().SequenceEqual(reaching.AsSpan()))
                mismatches.Add($"{reader.GetString(method.Name)}: substrate=[{string.Join(",", substrate)}] vs RD=[{string.Join(",", reaching)}]");
        }

        return (methods, mismatches);
    }

    static string Report(List<string> mismatches)
        => $"{mismatches.Count} block-start mismatch(es):\n{string.Join("\n", mismatches.Take(15))}";
}
