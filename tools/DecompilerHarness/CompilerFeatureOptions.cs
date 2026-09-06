using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

static class CompilerFeatureOptions
{
    const System.Reflection.MethodImplAttributes RuntimeAsync =
        (System.Reflection.MethodImplAttributes)0x2000;

    public static CSharpParseOptions ParseOptions(string assemblyPath)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        return ParseOptions(pe);
    }

    public static CSharpParseOptions ParseOptions(PEReader pe)
    {
        bool usesUpdatedMemorySafetyRules =
            pe.HasMetadata && ModuleUsesUpdatedMemorySafetyRules(pe);
        var options = new CSharpParseOptions(
            usesUpdatedMemorySafetyRules
                ? LanguageVersion.Preview
                : LanguageVersion.Latest);
        var features = new List<KeyValuePair<string, string>>();
        if (usesUpdatedMemorySafetyRules)
        {
            features.Add(new("updated-memory-safety-rules", "true"));
        }

        if (pe.HasMetadata && ModuleUsesRuntimeAsync(pe))
            features.Add(new("runtime-async", "on"));

        return features.Count == 0 ? options : options.WithFeatures(features);
    }

    /// <summary>
    /// Recompilation must replay the normalized module model consumed by the
    /// product. Raw marker presence is insufficient because unsupported or
    /// malformed markers use legacy printer rules rather than V2 rules.
    /// <c>CompilerFeatureOptionsTests.HarnessReplayMatchesPrinterMode</c> is the
    /// gate.
    /// </summary>
    static bool ModuleUsesUpdatedMemorySafetyRules(PEReader pe)
        => MemorySafetyMetadataIndex.Create(pe.GetMetadataReader()).Rules
            is MemorySafetyRulesResult.Available
            {
                State: MemorySafetyRulesState.Updated,
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
