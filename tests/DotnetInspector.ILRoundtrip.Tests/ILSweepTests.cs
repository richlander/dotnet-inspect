using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Metadata;

namespace DotnetInspector.ILRoundtrip.Tests;

/// <summary>
/// Assembly-wide ratchet: every method body in the named assemblies must
/// round-trip through the vendored ILAssembler with opcode equality. A failure
/// here means either a canonical-IL rendering regression or a vendored
/// assembler regression after a sync.
/// </summary>
public class ILSweepTests
{
    public static TheoryData<string> SweptAssemblies => new(
        typeof(ILSweepTests).Assembly.Location,        // this test assembly
        typeof(ILDisassembler).Assembly.Location);     // DotnetInspector.Metadata

    [Theory]
    [MemberData(nameof(SweptAssemblies))]
    public void EveryMethod_RoundtripsWithOpcodeEquality(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        int total = 0;
        List<string> failures = [];

        foreach (var tdh in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(tdh);
            string typePath = IlasmScaffold.TypePath(reader, tdh);
            foreach (var mh in td.GetMethods())
            {
                var method = reader.GetMethodDefinition(mh);
                if (method.RelativeVirtualAddress == 0)
                    continue;
                total++;
                string id = $"{typePath}::{reader.GetString(method.Name)}";

                try
                {
                    string il = IlasmScaffold.BuildCompilationUnit(peReader, reader, method);
                    var result = IlasmScaffold.Assemble(il);
                    if (!result.Succeeded)
                    {
                        failures.Add($"{id}: {result.Describe().ReplaceLineEndings(" ")}");
                        continue;
                    }

                    var original = ILDisassembler.Disassemble(peReader, reader, method)!;
                    var roundtripped = IlasmScaffold.DisassembleByName(
                        result.Image!, reader.GetString(method.Name), typePath, IlasmScaffold.ParamTypes(method));
                    if (roundtripped is null)
                    {
                        failures.Add($"{id}: method missing after assembly");
                        continue;
                    }

                    var a = original.Select(i => IlasmScaffold.CanonicalOpcode(i.OpCodeName));
                    var b = roundtripped.Select(i => IlasmScaffold.CanonicalOpcode(i.OpCodeName));
                    if (!a.SequenceEqual(b))
                        failures.Add($"{id}: opcode mismatch");
                }
                catch (Exception ex)
                {
                    failures.Add($"{id}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count}/{total} methods failed round-trip in {Path.GetFileName(assemblyPath)}:\n"
            + string.Join("\n", failures.Take(20)));
        Assert.True(total > 0, "sweep found no method bodies");
    }
}
