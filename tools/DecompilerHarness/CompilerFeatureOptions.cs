using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler.Pipeline;

using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

static class CompilerFeatureOptions
{
    const System.Reflection.MethodImplAttributes RuntimeAsync =
        (System.Reflection.MethodImplAttributes)0x2000;

    public static CSharpParseOptions ParseOptions()
        => new(LanguageVersion.Preview);

    public static CSharpParseOptions ParseOptions(string assemblyPath)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        return ParseOptions(pe);
    }

    public static CSharpParseOptions ParseOptions(PEReader pe)
    {
        var options = ParseOptions();
        var features = new List<KeyValuePair<string, string>>();
        if (pe.HasMetadata && ModuleUsesUpdatedMemorySafetyRules(pe))
        {
            features.Add(new("updated-memory-safety-rules", "true"));
        }

        if (pe.HasMetadata && ModuleUsesRuntimeAsync(pe))
            features.Add(new("runtime-async", "on"));

        return features.Count == 0 ? options : options.WithFeatures(features);
    }

    /// <summary>
    /// Recompilation must replay the mode the printer used, so this defers to
    /// the printer's own predicate rather than deriving the mode independently.
    /// Any second reader — including the more faithful
    /// <c>MemorySafetyMetadataIndex</c>, which accepts marker spellings the
    /// printer ignores — can disagree with the printer and compile output back
    /// under rules it was not printed with.
    /// <c>CompilerFeatureOptionsTests.HarnessReplayMatchesPrinterMode</c> is the
    /// gate.
    /// </summary>
    static bool ModuleUsesUpdatedMemorySafetyRules(PEReader pe)
        => IrImporter.ModuleUsesUpdatedMemorySafetyRules(pe.GetMetadataReader());

    static bool ModuleUsesRuntimeAsync(PEReader pe)
    {
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.MethodDefinitions)
        {
            if ((reader.GetMethodDefinition(handle).ImplAttributes & RuntimeAsync) != 0)
                return true;
        }

        return false;
    }
}
