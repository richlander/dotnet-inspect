using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

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
    /// Recompilation must replay the mode the printer used, and the printer
    /// keys on module marker <em>presence</em> rather than the version (see
    /// <c>IrImporter.ModuleUsesUpdatedMemorySafetyRules</c>). Any observed
    /// module marker therefore selects the updated-rules feature, so an
    /// unsupported or conflicting version still compiles back under the same
    /// rules it was printed with. Only a module with no marker at all
    /// (<see cref="MemorySafetyRulesState.Legacy"/>) compiles as legacy.
    /// </summary>
    static bool ModuleUsesUpdatedMemorySafetyRules(PEReader pe)
        => MemorySafetyMetadataIndex.Create(pe.GetMetadataReader()).Rules
            is MemorySafetyRulesResult.Available
            {
                State: not MemorySafetyRulesState.Legacy
            };

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
