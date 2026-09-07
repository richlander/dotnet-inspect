using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Decompiler.Tests;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;

using Microsoft.CodeAnalysis;

namespace ILInspector.DecompilerHarness;

[Trait("Area", "Fidelity")]
[Collection(FidelityGateCollection.Name)]
public sealed class AuthoredBuildContextTests
{
    static readonly FindingSubject Subject = new("rebuild-context", "rebuild-context");

    [Fact]
    public async Task LocalAuthoredSource_RetainsBothActualContextsAfterEvaluation()
    {
        using var http = new HttpClient(new NoNetworkHandler());
        var results = await AuthoredRebuildFidelity.EvaluateAssembliesAsync(
            [FixtureCatalog.DecompilerAuthoredRebuild.AssemblyPath()],
            1, http, new SourceFetcher(http));
        var result = Assert.Single(results);

        Assert.True(result.ChecksumVerification == SourceChecksumVerification.Exact, result.Detail);
        Assert.NotNull(result.MemberComparison);
        Assert.NotNull(result.AuthoredAttempt);
        Assert.NotNull(result.DecompilerLane.CompilationAttempt);
        Assert.Null(result.DecompilerLane.FinalRequest);
        Assert.Equal(BuildContextFactStatus.Agree, Fact(result.AuthoredContext, "checked").Status);
        Assert.Equal(BuildContextFactStatus.Different, Fact(result.DecompiledContext, "checked").Status);
    }

    [Fact]
    public void AppliedOptions_UseEachActualAttempt()
    {
        string assemblyPath = FixtureCatalog.DecompilerAuthoredRebuild.AssemblyPath();
        using var source = SourceLinkService.Open(assemblyPath);
        var context = new RecordedBuildContext(
            SourceLinkInspector.InspectDll(assemblyPath).IsDeterministic,
            MetadataFindings.InspectCompilationOptions(source.Context, Subject),
            MetadataFindings.InspectCompilationReferences(source.Context, Subject));
        Assert.Equal("True", context.Option("checked"));
        Assert.Null(context.Option("optimization"));
        var result = Rebuild(context);

        Assert.True(Assert.IsType<RebuildCompilationAttempt>(result.AuthoredAttempt).Options.CheckOverflow);
        Assert.False(Assert.IsType<RebuildCompilationAttempt>(result.DecompilerLane.CompilationAttempt).Options.CheckOverflow);
        Assert.Equal(BuildContextFactStatus.Agree, Fact(result.AuthoredContext, "checked").Status);
        Assert.Equal(BuildContextFactStatus.Different, Fact(result.DecompiledContext, "checked").Status);
        Assert.Equal(BuildContextFactStatus.Unknown, Fact(result.AuthoredContext, "optimization").Status);
        Assert.Equal(result.DecompilerLane.CompilationAttempt.Target, result.AuthoredAttempt.Target);
        Assert.NotSame(result.DecompilerLane.CompilationAttempt.Artifact, result.AuthoredAttempt.Artifact);
        var authoredQuery = Assert.IsType<LocalComparisonQueryResult.Published>(result.MemberComparison);
        var decompiledQuery = Assert.IsType<LocalComparisonQueryResult.Published>(result.DecompilerLane.MemberComparison);
        Assert.NotSame(authoredQuery.Identity!.Operation, decompiledQuery.Identity!.Operation);
        var native = Assert.IsType<ResearchProducerSessionOutcome.Completed>(decompiledQuery.Outcome).Completion;
        var item = Assert.Single(native.Results);
        var pair = Assert.IsType<ResearchProducerWorkBasis.DesignatedPair>(item.Item.Basis).Pair;
        Assert.NotEqual(
            Assert.IsType<MetadataMethodAddress>(Assert.IsType<ResearchTargetOutcome.Resolved>(pair.Before.Outcome).Address).ModuleVersionId,
            Assert.IsType<MetadataMethodAddress>(Assert.IsType<ResearchTargetOutcome.Resolved>(pair.After.Outcome).Address).ModuleVersionId);
        Assert.Same(Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(item.Outcome).Result.MemberDiff,
            result.DecompilerLane.IlDiff);
        Assert.NotNull(result.DecompilerLane.FidelityDiff);
        Assert.NotSame(result.DecompilerLane.IlDiff!.Diff, result.DecompilerLane.FidelityDiff);
        Assert.Equal(FidelityCheck.ClassifyStatus(
            isFull: true,
            opcodesExact: result.DecompilerLane.OriginalOpcodes == result.DecompilerLane.RecompiledOpcodes,
            fidelityDiff: result.DecompilerLane.FidelityDiff), result.DecompilerLane.Status);
    }

    [Fact]
    public void ExactAuthoredIl_DoesNotCertifyUnknownContext()
    {
        var result = Rebuild(Context(new CompilationOptionInfo("optimization", "debug")));

        Assert.Equal(AuthoredRebuildOutcome.Exact, result.Outcome);
        Assert.Equal(SourceChecksumVerification.Exact, result.ChecksumVerification);
        Assert.True(result.BuildContext.IsDeterministic);
        Assert.Equal(AuthoredBuildContextStatus.Incomplete, result.AuthoredContext.Status);
        Assert.Equal(BuildContextFactStatus.Unknown, Fact(result.AuthoredContext, "compiler-version").Status);
        Assert.Contains(result.AuthoredContext.Facts, fact =>
            fact.Dimension == "generators" && fact.Status == BuildContextFactStatus.Unknown);
        Assert.Contains(result.AuthoredContext.Facts, fact =>
            fact.Dimension == "project" && fact.Status == BuildContextFactStatus.Unknown);
    }

    [Theory]
    [InlineData("match", "Agree")]
    [InlineData("different", "Different")]
    [InlineData("missing", "Unknown")]
    public void ReferenceIdentity_DoesNotUseFilenameAsProof(string mode, string expected)
    {
        var decompiler = Decompile();
        var attempt = Assert.IsType<RebuildCompilationAttempt>(decompiler.CompilationAttempt);
        var reference = attempt.Provenance.References.First(reference => reference.ModuleVersionId is not null);
        Guid mvid = mode switch
        {
            "match" => reference.ModuleVersionId!.Value,
            "different" => Guid.NewGuid(),
            _ => Guid.Empty,
        };
        string name = Path.GetFileName(reference.Display);
        var recorded = new CompilationReferenceInfo(
            $"C:\\recorded\\{name}",
            string.Join(",", reference.Aliases),
            attempt.ReferenceKinds[reference.Ordinal] == MetadataImageKind.Assembly
                ? CompilationReferenceImageKind.Assembly : CompilationReferenceImageKind.Module,
            reference.EmbedInteropTypes, 0, 0, mvid);
        var context = new RecordedBuildContext(
            true, Options(), MetadataFindings.InspectCompilationReferences([recorded], Subject));
        var fact = Assert.Single(context.Assess(attempt).Facts, fact =>
            fact.Dimension == "references" && fact.Name == name && fact.Recorded is not null);
        Assert.Equal(expected, fact.Status.ToString());
        Assert.Contains("MVID=", fact.Effective, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacingAuthoredAttempt_DoesNotReuseItsContextOrVerdict()
    {
        var decompiler = Decompile();
        var debug = AuthoredRebuildFidelity.CompileAuthoredBody(
            decompiler, AuthoredBody(), SourceChecksumVerification.Exact, Context(new CompilationOptionInfo("optimization", "debug")));
        var release = AuthoredRebuildFidelity.CompileAuthoredBody(
            decompiler, AuthoredBody(), SourceChecksumVerification.Exact, Context(new CompilationOptionInfo("optimization", "release")));
        var failed = AuthoredRebuildFidelity.CompileAuthoredBody(
            decompiler, "return MissingValue;", null, Context());
        var different = AuthoredRebuildFidelity.CompileAuthoredBody(
            decompiler, "return 43;", null, Context(new CompilationOptionInfo("optimization", "debug")));

        Assert.Same(decompiler, debug.DecompilerLane);
        Assert.Same(decompiler, release.DecompilerLane);
        Assert.Same(decompiler, failed.DecompilerLane);
        Assert.Same(decompiler, different.DecompilerLane);
        Assert.Equal(AuthoredRebuildOutcome.IlDifferent, different.Outcome);
        Assert.NotSame(debug.MemberComparison!.Identity!.Operation, different.MemberComparison!.Identity!.Operation);
        Assert.Equal(BuildContextFactStatus.Agree, Fact(debug.AuthoredContext, "optimization").Status);
        Assert.Equal(BuildContextFactStatus.Different, Fact(debug.DecompiledContext, "optimization").Status);
        Assert.Equal("debug", Fact(debug.AuthoredContext, "optimization").Effective);
        Assert.Equal("release", Fact(release.AuthoredContext, "optimization").Effective);
        Assert.Equal(AuthoredRebuildOutcome.RecompileFailed, failed.Outcome);
        Assert.NotNull(failed.AuthoredAttempt);
        Assert.Null(failed.MemberComparison);
        Assert.Equal("debug", Fact(debug.AuthoredContext, "optimization").Effective);
    }

    [Fact]
    public void FloorReplacement_DropsPreviousCompilationContext()
    {
        var decompiler = Decompile();
        Assert.NotNull(decompiler.CompilationAttempt);
        var target = decompiler.Plan.TargetMethod;
        var floor = new FidelityCheck.CompileBackResult(
            target.Type, target.Method, target.Overload, target.Signature,
            FidelityCheck.CompileBackStatus.Exact, "ret", "ret", null);

        var replaced = ReturnToSender.WithCompileBackFloor(decompiler, floor);

        Assert.Same(decompiler.MemberComparison, replaced.MemberComparison);
        Assert.True(replaced.UsedCompileBackFloor);
        Assert.Null(replaced.CompilationAttempt);
        Assert.NotNull(decompiler.CompilationAttempt);
        Assert.Equal(BuildContextFactStatus.Unknown, Fact(Context().Assess(replaced.CompilationAttempt), "optimization").Status);
    }

    [Fact]
    public void FailedInspection_RemainsSeparateFromBothComparisons()
    {
        var context = RecordedBuildContext.Failed(Subject, "PDB options unavailable after decode failure");
        var result = Rebuild(context);

        Assert.NotNull(result.MemberComparison);
        Assert.Equal(AuthoredBuildContextStatus.Failed, result.AuthoredContext.Status);
        Assert.Equal(AuthoredBuildContextStatus.Failed, result.DecompiledContext.Status);
        Assert.Null(context.IsDeterministic);
        Assert.Contains(result.AuthoredContext.Facts, fact =>
            fact.Status == BuildContextFactStatus.Failed && fact.Detail.Contains("decode failure", StringComparison.Ordinal));
        Assert.NotNull(result.DecompilerLane.CompilationAttempt);
    }

    [Fact]
    public void FailedReferences_DoNotDiscardRecordedOptions()
    {
        var context = new RecordedBuildContext(
            true,
            Options(new CompilationOptionInfo("optimization", "debug")),
            new FindingInspection<CompilationReferenceInfo>.Failed(
                new(Subject, MetadataFindings.CompilationReferenceDescriptor, "Reference metadata could not be decoded.")));
        var result = Rebuild(context);

        Assert.Equal(AuthoredRebuildOutcome.Exact, result.Outcome);
        Assert.Equal(AuthoredBuildContextStatus.Failed, result.AuthoredContext.Status);
        Assert.Equal(BuildContextFactStatus.Agree, Fact(result.AuthoredContext, "optimization").Status);
        Assert.Equal("debug", Fact(result.AuthoredContext, "optimization").Effective);
        Assert.Contains(result.AuthoredContext.Facts, fact =>
            fact.Dimension == "references" && fact.Status == BuildContextFactStatus.Failed);
    }

    [Fact]
    public void UnsupportedAndMissingOptions_DiscloseActualDefaults()
    {
        var result = Rebuild(Context(
            new("optimization", "unsupported-mode"),
            new("unsafe", "not-a-boolean"),
            new("custom-option", "custom-value")));

        Assert.NotNull(result.AuthoredAttempt);
        Assert.Equal(BuildContextFactStatus.Unknown, Fact(result.AuthoredContext, "optimization").Status);
        Assert.Equal("debug", Fact(result.AuthoredContext, "optimization").Effective);
        Assert.Equal(BuildContextFactStatus.Unknown, Fact(result.AuthoredContext, "unsafe").Status);
        Assert.Equal("False", Fact(result.AuthoredContext, "unsafe").Effective);
        Assert.Equal(BuildContextFactStatus.Unknown, Fact(result.AuthoredContext, "custom-option").Status);
    }

    [Fact]
    public void MissingAuthoredAttempt_DoesNotBorrowDecompilerContext()
    {
        var complete = Rebuild(Context());
        var unavailable = new AuthoredRebuildFidelityResult(
            complete.DecompilerLane, AuthoredRebuildOutcome.SourceAbsent,
            null, complete.BuildContext, "No source", null);

        Assert.Null(unavailable.AuthoredAttempt);
        Assert.Null(Fact(unavailable.AuthoredContext, "optimization").Effective);
        Assert.Equal("release", Fact(unavailable.DecompiledContext, "optimization").Effective);
        Assert.Same(complete.DecompilerLane, unavailable.DecompilerLane);
    }

    [Fact]
    public void Report_PreservesBothLanesAndIndependentCountsUnderCap()
    {
        var result = Rebuild(Context(new("optimization", "debug"), new("custom-option", "\u001bvalue")));
        var failedDecompiler = result with
        {
            DecompilerLane = result.DecompilerLane with
            {
                Status = FidelityCheck.CompileBackStatus.RecompileFail,
                CompilationAttempt = null,
            },
        };
        using var output = new StringWriter();
        AuthoredRebuildFidelity.WriteReport([failedDecompiler, result], 1, output);
        string text = output.ToString();

        Assert.Contains("over 2 target(s)", text, StringComparison.Ordinal);
        Assert.Contains("Examples: 1 shown (limit 1; all results retained)", text, StringComparison.Ordinal);
        Assert.Contains("decompiled : RecompileFail", text, StringComparison.Ordinal);
        Assert.Contains("authored   : Exact", text, StringComparison.Ordinal);
        Assert.Contains("A context  :", text, StringComparison.Ordinal);
        Assert.Contains("B context  :", text, StringComparison.Ordinal);
        Assert.Contains("custom-option: Unknown", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);

        using var noExamples = new StringWriter();
        AuthoredRebuildFidelity.WriteReport([failedDecompiler, result], 0, noExamples);
        Assert.Contains("over 2 target(s)", noExamples.ToString(), StringComparison.Ordinal);
        Assert.Contains("Examples: 0 shown", noExamples.ToString(), StringComparison.Ordinal);
    }

    static BuildContextFact Fact(LaneBuildContext context, string name)
        => Assert.Single(context.Facts, fact => fact.Name == name);

    static ReturnToSender.Result Decompile()
        => ReturnToSender.CompileBackFirstPropertyGetter(FixtureCatalog.DecompilerAuthoredRebuild.AssemblyPath());

    static string AuthoredBody()
    {
        string source = File.ReadAllText(FixtureCatalog.DecompilerAuthoredRebuild.AssetPath("source"));
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBody(source, "get_Value", 0, out string body));
        return body;
    }

    static AuthoredRebuildFidelityResult Rebuild(RecordedBuildContext context)
        => AuthoredRebuildFidelity.CompileAuthoredBody(
            Decompile(), AuthoredBody(), SourceChecksumVerification.Exact, context);

    static RecordedBuildContext Context(params CompilationOptionInfo[] options)
        => new(true, Options(options), MetadataFindings.InspectCompilationReferences([], Subject));

    static FindingInspection<CompilationOptionInfo> Options(params CompilationOptionInfo[] options)
        => MetadataFindings.InspectCompilationOptions(options, Subject);

    sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The fixture must use its local PDB and checksum-verified local source.");
    }
}
