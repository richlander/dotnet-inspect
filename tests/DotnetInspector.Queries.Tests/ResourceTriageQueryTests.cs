using System.Buffers;
using System.Runtime.CompilerServices;
using ILInspector.Analysis;
using ILInspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed class ResourceTriageQueryTests
{
    [Fact]
    public void Execute_ReturnsLifecycleFindingsAndTypedAssessments()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            typeof(ResourceTriageQueryTests).Assembly.Location,
            LibraryBodyAnalysisFeatures.LeakTriage);

        ResourceTriageResult result = ResourceTriageQuery.Execute(
            index,
            new FindingSubject("query-tests", "query-tests"));

        var available =
            Assert.IsType<ResourceTriageResult.Available>(result);
        ResourceTriageAssessment assessment = Assert.Single(
            available.Assessments,
            candidate =>
                candidate.Source.Payload.Method.Name
                    == nameof(ReadBeforeReturn));
        Assert.Contains(
            available.Inspection.Findings,
            finding => finding == assessment.Source);
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            assessment.Actionability);
        Assert.Contains(
            assessment.Boundaries,
            boundary =>
                boundary.Kind
                    == ResourceTriageBoundaryKind.ExternalInput);
    }

    [Fact]
    public void Definition_IsUnbounded()
        => Assert.Equal(
            InspectionCost.Unbounded,
            ResourceTriageQuery.Definition.Cost);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadBeforeReturn(Stream stream)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        int read = stream.Read(buffer, 0, 16);
        ArrayPool<byte>.Shared.Return(buffer);
        return read;
    }
}
