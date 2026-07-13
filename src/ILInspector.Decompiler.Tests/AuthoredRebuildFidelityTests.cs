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
    [InlineData("int IFoo.Value { get { return 4; } }", "Sample.IFoo.get_Value", "return 4;")]
    [InlineData("int IFoo.M() { return 5; }", "Sample.IFoo.M", "return 5;")]
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

    [Fact]
    public void AuthoredMemberSource_DoesNotUseDifferentPropertyBody()
    {
        Assert.False(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public int Value { get { return 1; } }",
            "get_Other",
            out _));
    }

    [Fact]
    public void AuthoredMemberSource_FindsTargetAfterNeighboringProperty()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public int Other { get { return 1; } } public int Value { get { return 2; } }",
            "get_Value",
            out string body));
        Assert.Contains("return 2;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_DistinguishesExplicitInterfaceMethod()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public int M() { return 1; } int IFoo.M() { return 2; }",
            "Sample.IFoo.M",
            out string body));
        Assert.Contains("return 2;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_PrefersQualifiedExplicitInterfaceMethod()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "int I1.M() { return 1; } int N.I1.M() { return 2; }",
            "N.I1.M",
            out string body));
        Assert.Contains("return 2;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_MatchesGenericExplicitInterfaceProperty()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "int ICustom<int>.Value { get { return 3; } }",
            "ICustom<System.Int32>.get_Value",
            out string body));
        Assert.Contains("return 3;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_DistinguishesConstructedGenericInterfaces()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "int ICustom<int>.Value { get { return 1; } } "
                + "int ICustom<string>.Value { get { return 2; } }",
            "ICustom<System.String>.get_Value",
            out string body));
        Assert.Contains("return 2;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_AllowsUnresolvedNamespaceAlias()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "int Alias::I1.Value { get { return 4; } }",
            "N.I1.get_Value",
            out string body));
        Assert.Contains("return 4;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredMemberSource_ExtractsIndexerBody()
    {
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(
            "public int this[int index] { get { return index; } }",
            "get_Item",
            out string body));
        Assert.Contains("return index;", body, StringComparison.Ordinal);
    }

    static FindingInspection<CompilationOptionInfo> CompleteOptions(
        params CompilationOptionInfo[] options)
        => MetadataFindings.InspectCompilationOptions(options, Subject);

    static FindingInspection<CompilationReferenceInfo> CompleteReferences(
        params CompilationReferenceInfo[] references)
        => MetadataFindings.InspectCompilationReferences(references, Subject);
}
