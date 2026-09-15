namespace ILInspector.JsExportSurface;

public sealed class UnsupportedJsExportSurfaceException(
    string location,
    string reason)
    : Exception($"{location}: {reason}.");
