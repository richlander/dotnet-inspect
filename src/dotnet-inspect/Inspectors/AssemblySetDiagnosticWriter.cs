using DotnetInspector.Output;
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
    ///
    /// Severity is dispatched to <see cref="CommandError"/> rather than
    /// composed into a prefix here. This method used to interpolate its own,
    /// and contained the message correctly, but the shape is the one that let a
    /// sibling writer emit an uncontained <c>Error:</c> line that no gate could
    /// see -- so there is now one spelling of the prefix in the product.
    /// </remarks>
    public static void Write(AssemblySetDiagnostic diagnostic)
    {
        if (diagnostic.Severity == AssemblySetDiagnosticSeverity.Error)
        {
            CommandError.Write(diagnostic.Message);
        }
        else
        {
            CommandError.WriteWarning(diagnostic.Message);
        }
    }
}
