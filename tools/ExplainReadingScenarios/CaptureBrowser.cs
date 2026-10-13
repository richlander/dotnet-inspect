using System.Runtime.Versioning;
using DotnetInspect.Web.Interop.Catalog;

[SupportedOSPlatform("browser")]
internal static class CaptureBrowser
{
    // Like BrowserStyleOptionsTests, invoke the pure managed serialization path.
    // This does not start a browser or claim Wasm execution evidence.
    private static void Main(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException("Supply one output JSON file path.");
        File.WriteAllText(args[0], CatalogExports.InspectVocabulary());
    }
}
