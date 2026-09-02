using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace TsJsExport.ContextFixtures.NormalizationDecomposed;

[SupportedOSPlatform("browser")]
public static partial class DecomposedExports
{
    [JSExport]
    public static string Identify() => "decomposed";
}
