using DotnetInspector.Fixtures;
using DotnetInspector.Services;

namespace ILInspector.DecompilerHarness;

public sealed class AuthoredSourceCensusTests
{
    static ReturnToSender.Result RealDecompilerResult()
        => ReturnToSender.CompileBackFirstPropertyGetter(FixtureCatalog.DiffPair.OldAssemblyPath());

    static ReturnToSender.RequestedTarget TargetFor(ReturnToSender.Result result)
    {
        var identity = result.Plan.TargetMethod;
        return new ReturnToSender.RequestedTarget(
            identity.Type,
            identity.Method,
            identity.Overload,
            identity.Signature);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void ClassifyExpectedBody_ReportsValidMatch_WhenAuthoredBodyIsWhitespaceEquivalent()
    {
        var decompiler = RealDecompilerResult();
        var target = TargetFor(decompiler);
        string expected = $"  {decompiler.TargetBody}  \n";

        var result = AuthoredSourceCensus.ClassifyExpectedBody(target, decompiler, "https://example.test/Source.cs", expected);

        Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome);
        Assert.Equal("valid_match", result.Reason);
        Assert.True(result.Passed);
        Assert.Equal(decompiler.TargetBody, result.ActualBody);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void ClassifyExpectedBody_ReportsValidDifferent_WhenAuthoredBodyDiffersInSubstance()
    {
        var decompiler = RealDecompilerResult();
        var target = TargetFor(decompiler);
        string expected = decompiler.TargetBody + " /* an authored-only comment marker that is not whitespace */ return 12345;";

        var result = AuthoredSourceCensus.ClassifyExpectedBody(target, decompiler, "https://example.test/Source.cs", expected);

        Assert.Equal(ReturnToSenderSourceOutcome.ValidDifferent, result.Outcome);
        Assert.True(result.Different);
        Assert.Equal(expected, result.ExpectedBody);
        Assert.Equal(decompiler.TargetBody, result.ActualBody);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ClassifyAsync_ReportsUnsupportedTarget_WhenRtsProducedNoFinalRequest()
    {
        var decompiler = RealDecompilerResult() with { FinalRequest = null };
        using var httpClient = new HttpClient();
        var fetcher = new SourceFetcher(httpClient);

        var result = await AuthoredSourceCensus.ClassifyAsync(null!, fetcher, decompiler);

        Assert.Equal(ReturnToSenderSourceOutcome.UnsupportedTarget, result.Outcome);
        Assert.Equal("unsupported-rts-target", result.Reason);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ClassifyAsync_ReportsInvalid_WhenRtsRecompileFailed()
        => await AssertClassifiesInvalid(FidelityCheck.CompileBackStatus.RecompileFail);

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ClassifyAsync_ReportsInvalid_WhenRtsContextFailed()
        => await AssertClassifiesInvalid(FidelityCheck.CompileBackStatus.ContextFail);

    static async Task AssertClassifiesInvalid(FidelityCheck.CompileBackStatus status)
    {
        var decompiler = RealDecompilerResult() with { Status = status, Detail = "synthetic failure detail" };
        using var httpClient = new HttpClient();
        var fetcher = new SourceFetcher(httpClient);

        var result = await AuthoredSourceCensus.ClassifyAsync(null!, fetcher, decompiler);

        Assert.Equal(ReturnToSenderSourceOutcome.Invalid, result.Outcome);
        Assert.True(result.Failed);
    }
}
