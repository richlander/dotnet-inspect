using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

static class CompilerFeatureOptions
{
    static readonly KeyValuePair<string, string>[] UpdatedMemorySafetyRules =
    [
        new("updated-memory-safety-rules", "true"),
    ];

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
        if (pe.HasMetadata
            && AssemblyDetailScanner.ScanAuditMetadata(pe).MemorySafetyRulesVersion is not null)
        {
            options = options.WithFeatures(UpdatedMemorySafetyRules);
        }

        return options;
    }
}
