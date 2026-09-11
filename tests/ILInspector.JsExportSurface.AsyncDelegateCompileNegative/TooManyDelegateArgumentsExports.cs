using System.Runtime.InteropServices.JavaScript;

namespace ILInspector.JsExportSurface.AsyncDelegateCompileNegative;

public static partial class TooManyDelegateArgumentsExports
{
    [JSExport]
    public static void Register(
        [JSMarshalAs<JSType.Function<
            JSType.Number,
            JSType.Number,
            JSType.Number,
            JSType.Number>>]
        Action<int, int, int, int> callback)
    {
    }
}
