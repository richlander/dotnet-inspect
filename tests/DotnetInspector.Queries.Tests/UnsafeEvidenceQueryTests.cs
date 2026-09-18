using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class UnsafeEvidenceQueryTests
{
    [Fact]
    public void Execute_MissingSafetyAcquisitionRemainsTypedFailure()
    {
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.None));

        UnsafeEvidenceResult result =
            UnsafeEvidenceQuery.Execute(
                analysis.Safety);

        var failed =
            Assert.IsType<UnsafeEvidenceResult.Failed>(
                result);
        Assert.Contains(
            "was not requested",
            failed.Error.Message,
            StringComparison.Ordinal);
    }
}
