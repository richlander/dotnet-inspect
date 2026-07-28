using ILInspector.CSharp;
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

    /// <summary>
    /// The single boundary where assembly-set diagnostics reach stderr.
    /// </summary>
    /// <remarks>
    /// Diagnostic messages embed the subject the caller asked for -- a package
    /// id, library name, or path -- which an agent may have copied out of
    /// untrusted metadata. Containing here rather than at each message-building
    /// site means a diagnostic added later is contained by construction
    /// (issue #3319).
    /// </remarks>
    public static void Write(AssemblySetDiagnostic diagnostic)
    {
        var prefix = diagnostic.Severity == AssemblySetDiagnosticSeverity.Error
            ? "Error"
            : "Warning";
        Console.Error.WriteLine($"{prefix}: {CSharpIdentifier.ContainRenderedText(diagnostic.Message)}");
    }
}
