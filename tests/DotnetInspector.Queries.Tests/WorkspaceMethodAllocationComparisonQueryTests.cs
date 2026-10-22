using DotnetInspector.Fixtures;
using Inspector.Findings;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceMethodAllocationComparisonQueryTests
{
    [Fact]
    public async Task ReorderedMethodDefsResolveExactImplementationBody()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            FixtureCatalog.AnalysisMethodCorrespondenceSurface
                .AssemblyPath(),
            FixtureCatalog.AnalysisMethodCorrespondenceRuntime
                .AssemblyPath());
        AssemblyContextParticipant surface = group.Participants[0];
        AssemblyContextParticipant implementation = group.Participants[1];
        int surfaceToken = SurfaceMethodToken(
            group,
            surface,
            "MethodCorrespondenceFixture.Widget",
            "Allocate");

        var exact = Assert.IsType<
            AssemblyContextMethodCorrespondenceResult.Exact>(
                AssemblyContextMethodCorrespondenceQuery.Execute(
                    group,
                    surface,
                    surfaceToken,
                    group,
                    implementation,
                    TestContext.Current.CancellationToken));

        Assert.NotEqual(surfaceToken, exact.MethodToken);
        AssemblyMethodAllocationAnalysis analysis = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAllocationAnalysis>.Available>(
                AssemblyContextMethodAllocationQuery.ExecuteParticipant(
                    group,
                    implementation,
                    exact.MethodToken,
                    TestContext.Current.CancellationToken)).Value;
        Assert.Contains(
            analysis.Allocations,
            allocation => allocation.AllocatedType?.Name.Contains(
                "Helper",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task BodylessImplementationIsRejectedBeforeAnalysis()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            FixtureCatalog.AnalysisMethodCorrespondenceSurface
                .AssemblyPath(),
            FixtureCatalog.AnalysisMethodCorrespondenceRuntime
                .AssemblyPath());
        AssemblyContextParticipant surface = group.Participants[0];
        AssemblyContextParticipant implementation = group.Participants[1];
        int surfaceToken = SurfaceMethodToken(
            group,
            surface,
            "MethodCorrespondenceFixture.Bodyless",
            "Run");

        var bodyless = Assert.IsType<
            AssemblyContextMethodCorrespondenceResult.Bodyless>(
                AssemblyContextMethodCorrespondenceQuery.Execute(
                    group,
                    surface,
                    surfaceToken,
                    group,
                    implementation,
                    TestContext.Current.CancellationToken));

        Assert.NotEqual(0, bodyless.MethodToken);
    }

    [Fact]
    public async Task DiagnosticBearingAllocationAnalysisFailsInspection()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            typeof(WorkspaceMethodAllocationComparisonQueryTests)
                .Assembly.Location);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        int token = typeof(WorkspaceMethodAllocationComparisonQueryTests)
            .GetMethod(nameof(DiagnosticBearingAllocationAnalysisFailsInspection))!
            .MetadataToken;
        var available = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAllocationAnalysis>.Available>(
                AssemblyContextMethodAllocationQuery.ExecuteParticipant(
                    group,
                    participant,
                    token,
                    TestContext.Current.CancellationToken));
        var diagnostic = new AnalysisDiagnostic(
            token,
            available.Value.Method.Name,
            "Synthetic recoverable analysis failure.");
        var incomplete = new AssemblyContextEntry<
            AssemblyMethodAllocationAnalysis>.Available(
                available.Subject,
                available.Value with
                {
                    Allocations = [],
                    Diagnostics = [diagnostic],
                });
        WorkspaceMethodAllocationComparisonResult result =
            WorkspaceMethodAllocationComparisonQuery.Compare(
                incomplete,
                available,
                available.Subject.Identity.Name,
                token,
                TestContext.Current.CancellationToken);

        var compared = Assert.IsType<
            WorkspaceMethodAllocationComparisonResult.Compared>(result);
        FindingComparison<AllocationOccurrence> comparison = Assert.Single(
            compared.Comparison.RetainedComparisons.Get<AllocationOccurrence>(
                AnalysisFindings.AllocationDescriptor)).Comparison;
        var failed = Assert.IsType<
            FindingComparison<AllocationOccurrence>.Failed>(
                comparison.Value);
        var inspection = Assert.IsType<
            FindingInspection<AllocationOccurrence>.Failed>(
                failed.OldInspection.Value);
        Assert.Contains(
            diagnostic.Message,
            inspection.Error.Reason,
            StringComparison.Ordinal);
    }

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        params string[] paths)
        => workspace.CreateAssemblyContextGroup(
            paths.Select(
                static path => new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "method-allocation-comparison-test")),
                    NoResolverAssemblyBindingPolicy.Instance)));

    static int SurfaceMethodToken(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string typeName,
        string memberName)
    {
        AssemblyApiSurface api = Assert.IsType<
            AssemblyContextEntry<AssemblyApiSurface>.Available>(
                AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                    group,
                    participant)).Value;
        ApiType type = Assert.Single(
            api.Surface.Types,
            candidate => candidate.FullName == typeName);
        ResolvedMemberTarget target = Assert.IsType<ResolvedMemberTarget>(
            MemberTargetResolver.Resolve(
                type,
                MemberTargetSelector.Parse(memberName)).Target);
        return Assert.IsType<int>(target.Body!.MetadataToken);
    }
}
