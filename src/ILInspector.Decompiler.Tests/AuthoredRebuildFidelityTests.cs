using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Findings;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;

namespace ILInspector.DecompilerHarness;

public sealed class AuthoredRebuildFidelityTests
{
    static readonly FindingSubject Subject = new("test", "test");

    [Fact]
    public void BuildContextAssessment_KeepsDeterminismSeparateFromRecordedContext()
    {
        string runtimePath = typeof(object).Assembly.Location;
        var assessment = AuthoredRebuildFidelity.AssessBuildContext(
            isDeterministic: false,
            CompleteOptions(
                new CompilationOptionInfo("optimization", "release"),
                new CompilationOptionInfo("unsafe", "true")),
            CompleteReferences(new CompilationReferenceInfo(
                Path.GetFileName(runtimePath),
                Aliases: "",
                CompilationReferenceImageKind.Assembly,
                EmbedInteropTypes: false,
                Timestamp: 0,
                ImageSize: 0,
                ModuleVersionId: Guid.Empty)),
            [MetadataReference.CreateFromFile(runtimePath)]);

        Assert.Equal(AuthoredBuildContextStatus.Recorded, assessment.Status);
        Assert.False(assessment.IsDeterministic);
    }

    [Fact]
    public void BuildContextAssessment_ReportsContextDriftIndependently()
    {
        var assessment = AuthoredRebuildFidelity.AssessBuildContext(
            isDeterministic: true,
            CompleteOptions(new CompilationOptionInfo("optimization", "debug")),
            CompleteReferences(new CompilationReferenceInfo(
                "Missing.Reference.dll",
                Aliases: "",
                CompilationReferenceImageKind.Assembly,
                EmbedInteropTypes: false,
                Timestamp: 0,
                ImageSize: 0,
                ModuleVersionId: Guid.Empty)),
            []);

        Assert.Equal(AuthoredBuildContextStatus.Drift, assessment.Status);
        Assert.True(assessment.IsDeterministic);
        Assert.Contains("optimization=debug", assessment.Detail, StringComparison.Ordinal);
        Assert.Contains("Missing.Reference.dll", assessment.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void AuthoredBody_ReusesFinalRtsRequestAndProductIlDiff()
    {
        var decompiler = ReturnToSender.CompileBackFirstPropertyGetter(
            FixtureCatalog.DiffPair.OldAssemblyPath());
        var context = new AuthoredBuildContextAssessment(
            AuthoredBuildContextStatus.Incomplete,
            IsDeterministic: true,
            "test context");

        var result = AuthoredRebuildFidelity.CompileAuthoredBody(
            decompiler,
            decompiler.TargetBody,
            SourceChecksumVerification.Exact,
            context);

        Assert.True(
            result.Outcome is AuthoredRebuildOutcome.Exact or AuthoredRebuildOutcome.IlDifferent,
            result.Detail);
        Assert.NotNull(result.ImplementationDiff);
        Assert.Equal(SourceChecksumVerification.Exact, result.ChecksumVerification);
        Assert.Equal(decompiler, result.DecompilerLane);
    }

    [Theory]
    [InlineData("public int Value { get { return 1; } }", "get_Value", "return 1;")]
    [InlineData("public int Value => 2;", "get_Value", "return 2;")]
    [InlineData("public int M() { return 3; }", "M", "return 3;")]
    public void AuthoredMemberSource_ExtractsRtsTargetBody(
        string memberSource,
        string methodName,
        string expected)
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            memberSource,
            methodName,
            out string body));
        Assert.Contains(expected, body, StringComparison.Ordinal);
    }

    static FindingInspection<CompilationOptionInfo> CompleteOptions(
        params CompilationOptionInfo[] options)
        => MetadataFindings.InspectCompilationOptions(options, Subject);

    static FindingInspection<CompilationReferenceInfo> CompleteReferences(
        params CompilationReferenceInfo[] references)
        => MetadataFindings.InspectCompilationReferences(references, Subject);
}
