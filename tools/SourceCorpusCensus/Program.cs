using System.Text.Json;

using DotnetInspector.SourceCorpusCensus;

return args switch
{
    ["--validate"] => await CensusValidation.RunAsync(),
    ["documents", .. var corpusPaths] when corpusPaths.Length > 0 =>
        await DocumentCensus.RunAsync(corpusPaths),
    ["types", var assembliesPath, var manifestPath, var cachePath] =>
        await TypeMappingCensus.RunAsync(
            assembliesPath,
            manifestPath,
            cachePath),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine(
        """
        Usage:
          source-corpus-census --validate
          source-corpus-census documents <corpus.jsonl>...
          source-corpus-census types <assemblies.txt> <sweep-manifest.json> <cache-directory>

        Structured reports are written to stdout; progress and diagnostics use stderr.
        """);
    return 2;
}

namespace DotnetInspector.SourceCorpusCensus
{
    static class CensusOutput
    {
        public static void Write(DocumentCorpusReport report) =>
            Console.Out.WriteLine(
                JsonSerializer.Serialize(
                    report,
                    CensusJsonContext.Default.DocumentCorpusReport));

        public static void Write(TypeMappingCorpusReport report) =>
            Console.Out.WriteLine(
                JsonSerializer.Serialize(
                    report,
                    CensusJsonContext.Default.TypeMappingCorpusReport));
    }
}
