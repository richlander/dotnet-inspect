using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ILInspector.JsExportSurface.AsyncDelegateCompileNegative;

[SupportedOSPlatform("browser")]
public static partial class AsyncDelegateExports
{
    [JSExport]
    public static void RegisterAsyncCallback(
        [JSMarshalAs<JSType.Function<
            JSType.Number,
            JSType.Promise<JSType.Number>>>]
        Func<int, Task<int>> callback)
    {
    }
}
