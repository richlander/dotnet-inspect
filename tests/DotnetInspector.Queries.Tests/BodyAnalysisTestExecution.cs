using ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

internal static class BodyAnalysisTestExecution
{
    public static LibraryBodyAnalysisExecution Open(string path) =>
        LibraryBodyAnalysisService.ExecutePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures.Default));
}
