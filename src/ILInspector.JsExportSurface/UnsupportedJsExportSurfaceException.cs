namespace ILInspector.JsExportSurface;

public sealed class UnsupportedJsExportSurfaceException
    : Exception
{
    public UnsupportedJsExportSurfaceException(
        string location,
        string reason)
        : base($"{location}: {reason}.")
    {
        Location = location;
        Reason = reason;
    }

    public string Location { get; }

    public string Reason { get; }
}
