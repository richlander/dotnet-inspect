using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Decode-fidelity invariants over real compiled IL (this test assembly's own method bodies):
/// a faithful decode exactly tiles the IL bytes and every branch target lands on an instruction
/// boundary. These are oracle-free checks — a desync (e.g. a mishandled prefix) breaks tiling at once.
/// </summary>
public class DecodeFidelityTests
{
    [Fact]
    public void Decoded_stream_tiles_il_and_branch_targets_align()
    {
        using var pe = new PEReader(File.OpenRead(typeof(DecodeFidelityTests).Assembly.Location));
        var reader = pe.GetMetadataReader();

        int methods = 0;
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

            var instructions = InstructionDecoder.Decode(il);
            methods++;
            string name = reader.GetString(method.Name);

            // Tiling: contiguous, starts at 0, ends exactly at il.Length, no gaps/overlaps.
            Assert.Equal(0, instructions[0].Offset);
            for (int i = 1; i < instructions.Length; i++)
                Assert.True(instructions[i - 1].NextOffset == instructions[i].Offset,
                    $"{name}: gap/overlap at IL_{instructions[i].Offset:X4}");
            Assert.Equal(il.Length, instructions[^1].NextOffset);

            // Branch-target alignment: every target is a real instruction offset.
            var offsets = instructions.Select(x => x.Offset).ToHashSet();
            foreach (var ins in instructions)
                foreach (int target in ins.BranchTargets)
                    Assert.True(offsets.Contains(target),
                        $"{name}: branch target IL_{target:X4} not on an instruction boundary");
        }

        Assert.True(methods > 50, $"expected to exercise many method bodies, saw {methods}");
    }
}
