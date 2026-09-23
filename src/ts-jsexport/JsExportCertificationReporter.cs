using ILInspector.JsExportSurface;

namespace TsJsExport;

internal static class JsExportCertificationReporter
{
    public static bool Report(
        JsExportSurface surface,
        string toolName,
        TextWriter error,
        bool warningsAsErrors)
    {
        bool hasWarnings = false;
        foreach (JsExportFunction function in surface.Functions)
        foreach (JsExportCertificationDiagnostic diagnostic
            in function.CertificationDiagnostics)
        {
            hasWarnings = true;
            error.WriteLine(
                $"{toolName}: "
                    + (warningsAsErrors ? "error" : "warning")
                    + $": {function.DeclaringType}.{function.Name}: "
                    + $"{diagnostic.Message}.");
        }

        return !warningsAsErrors || !hasWarnings;
    }
}
