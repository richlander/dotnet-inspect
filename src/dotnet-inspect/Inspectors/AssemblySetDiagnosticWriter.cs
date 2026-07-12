using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

internal static class AssemblySetDiagnosticWriter
{
    public static void Write(AssemblySet assemblySet, bool includeErrors = true)
    {
        foreach (var diagnostic in assemblySet.Diagnostics)
        {
            if (!includeErrors && diagnostic.Severity == AssemblySetDiagnosticSeverity.Error)
                continue;

            Write(diagnostic);
        }
    }

    public static void Write(AssemblySetDiagnostic diagnostic)
    {
        var prefix = diagnostic.Severity == AssemblySetDiagnosticSeverity.Error
            ? "Error"
            : "Warning";
        Console.Error.WriteLine($"{prefix}: {diagnostic.Message}");
    }
}
