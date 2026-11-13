using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes whole-library Top Leverage through the focused Method source.
/// </summary>
public static class LibraryTopLeverageInspectionOperation
{
    public static InspectionEnvelope<TopLeverageResult> Execute(
        AssemblyInspectionSession session,
        string sourceName,
        AssemblyReferenceBindingAccess? referenceBinding = null) =>
        Execute(
            session,
            sourceName,
            referenceBinding,
            parallelExecution: null);

    public static InspectionEnvelope<TopLeverageResult> Execute(
        AssemblyInspectionSession session,
        string sourceName,
        AssemblyReferenceBindingAccess? referenceBinding,
        MethodDefinitionParallelExecution? parallelExecution)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        TopLeverageResult content;
        if (!session.HasMetadata)
        {
            content = new TopLeverageResult.NoMetadata();
        }
        else
        {
            LibraryCallGraphAnalysisResult? analysis = null;
            try
            {
                analysis = LibraryMethodLeverageAnalysisQuery.Execute(
                    session,
                    sourceName,
                    referenceBinding,
                    parallelExecution);
                content = TopLeverageQuery.Execute(
                    LibraryLeverageAnalysisResult
                        .FromMethodLeverageProducer(
                        analysis));
            }
            catch (Exception error)
            {
                content = new TopLeverageResult.Failed(error);
            }
            finally
            {
                analysis?.ReleaseCaches();
            }
        }

        return new(
            content,
            new InspectionShare.NonProjectable(
                "library-top-leverage/share",
                "Library Top Leverage does not yet have a canonical "
                    + "Workspace Share projection."),
            Diagnostics(content));
    }

    static IEnumerable<InspectionDiagnostic> Diagnostics(
        TopLeverageResult content)
    {
        if (content is TopLeverageResult.Available available)
        {
            foreach (AnalysisDiagnostic diagnostic in available.Diagnostics)
            {
                yield return new(
                    "library-top-leverage.analysis",
                    InspectionDiagnosticSeverity.Warning,
                    $"Method 0x{diagnostic.MethodToken:X8} "
                        + $"'{diagnostic.Method}': {diagnostic.Message}");
            }
        }
        else if (content is TopLeverageResult.Failed failed)
        {
            yield return new(
                "library-top-leverage.failed",
                InspectionDiagnosticSeverity.Error,
                $"{failed.Error.GetType().Name}: {failed.Error.Message}");
        }
    }
}
