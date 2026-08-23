using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ILInspector.JsExportSurface.OperatorFixtures;

/// <summary>
/// A compiler-produced attributed operator. The runtime JSExport generator does
/// not publish operators, so this is intentionally rejected before tsbindgen
/// can emit a declaration or wrapper for it.
/// </summary>
[SupportedOSPlatform("browser")]
public readonly struct JsExportOperatorFixture
{
    [JSExport]
    public static JsExportOperatorFixture operator +(
        JsExportOperatorFixture left,
        JsExportOperatorFixture right) => default;
}
