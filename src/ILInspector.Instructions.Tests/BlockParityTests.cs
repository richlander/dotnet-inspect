using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;
using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Phase 1 directional differential: prove the substrate's block builder is a regression-free
/// replacement for <c>ReachingDefinitions</c>'s (the Analysis-first cutover target). Block-start
/// offsets must match exactly over real IL. A diff is triaged improvement/regression — for the
/// test assembly (no <c>no.</c>-style edge cases) the decoders agree, so exact parity is expected;
/// any divergence is a real finding, not flat-corpus noise.
/// </summary>
public class BlockParityTests
{
    [Fact]
    public void Block_starts_match_reaching_definitions_over_real_il()
    {
        using var pe = new PEReader(File.OpenRead(typeof(BlockParityTests).Assembly.Location));
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
            var substrate = MethodInstructions.Decode(il, il.Length, body.ExceptionRegions)
                .Blocks.Blocks.Select(b => b.Start).ToImmutableArray();
            var reaching = ReachingDefinitions.BlockStartsForParity(il, body.ExceptionRegions);

            if (!substrate.AsSpan().SequenceEqual(reaching.AsSpan()))
                mismatches.Add($"{reader.GetString(method.Name)}: substrate=[{string.Join(",", substrate)}] vs RD=[{string.Join(",", reaching)}]");
        }

        Assert.True(methods > 50, $"expected to exercise many method bodies, saw {methods}");
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} block-start mismatch(es):\n{string.Join("\n", mismatches.Take(10))}");
    }
}
