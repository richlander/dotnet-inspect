using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace TsJsExport.ContextFixtures.NormalizationComposed;

[SupportedOSPlatform("browser")]
public static partial class ComposedExports
{
    [JSExport]
    public static string Identify() => "composed";
}
