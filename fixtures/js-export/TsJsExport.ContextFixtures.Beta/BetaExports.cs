using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace TsJsExport.ContextFixtures.Beta;

[SupportedOSPlatform("browser")]
public static partial class BetaExports
{
    [JSExport]
    public static string IdentifyBeta(string value) => $"beta:{value}";
}
