using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ILInspector.JsExportSurface.OperatorFixtures;

/// <summary>
/// A compiler-produced attributed operator. The runtime JSExport generator does
/// not publish operators, so this is intentionally rejected before facade
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

[SupportedOSPlatform("browser")]
public static class GenericJsExportFixture
{
    [JSExport]
    public static T Echo<T>(T value) => value;
}

[SupportedOSPlatform("browser")]
public static class FilteredJsExportFixture
{
    public static int Value
    {
        [JSExport]
        get => 42;
    }

    public static int InvokeLocal()
    {
        [JSExport]
        static int Local() => 42;

        return Local();
    }
}
