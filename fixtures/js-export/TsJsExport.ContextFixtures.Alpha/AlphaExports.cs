using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace TsJsExport.ContextFixtures.Alpha;

[SupportedOSPlatform("browser")]
public static partial class AlphaExports
{
    [JSExport]
    public static string IdentifyAlpha(string value) => $"alpha:{value}";
}

[SupportedOSPlatform("browser")]
public static partial class AlphaOtherExports
{
    [JSExport]
    public static int Double(int value) => value * 2;
}

public sealed class AlphaSecondaryAnchor;
