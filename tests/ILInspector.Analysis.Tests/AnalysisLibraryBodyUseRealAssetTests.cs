namespace ILInspector.Analysis.Tests;

public sealed class AnalysisLibraryBodyUseRealAssetTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public void SystemTextJson10_PublishesQualifiedBodyUses()
    {
        AnalysisLibraryBodyUseResult result = Product(
            Path.Combine(
                AppContext.BaseDirectory,
                "PinnedArtifacts",
                "packages",
                "System.Text.Json.10.0.0.dll"));

        Assert.Equal("System.Text.Json", result.Receipt.Assembly.Name);
        Assert.Equal(
            new Version(10, 0, 0, 0),
            result.Receipt.Assembly.Version);
        Assert.Equal(
            "cc7b13ffcd2ddd51",
            result.Receipt.Assembly.PublicKeyToken,
            ignoreCase: true);
        Assert.True(
            result.Disposition
                != AnalysisLibraryBodyUseDisposition.Partial,
            DescribeDiagnostics(result));
        Assert.NotEmpty(result.Types);
        Assert.NotEmpty(result.Occurrences);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Net11CoreLib_PublishesQualifiedBodyUsesAtScale()
    {
        AnalysisLibraryBodyUseResult result = Product(
            Path.Combine(
                AppContext.BaseDirectory,
                "PinnedArtifacts",
                "System.Private.CoreLib.dll"));

        Assert.Equal(
            "System.Private.CoreLib",
            result.Receipt.Assembly.Name);
        Assert.True(
            result.Disposition
                != AnalysisLibraryBodyUseDisposition.Partial,
            DescribeDiagnostics(result));
        Assert.True(result.Types.Length > 1_000);
        Assert.True(result.Occurrences.Length > 10_000);
    }

    static AnalysisLibraryBodyUseResult Product(string path)
    {
        AnalysisLibraryBodyUseOutcome outcome =
            AnalysisLibraryBodyUseService.ExecutePath(
                path,
                new(),
                TestContext.Current.CancellationToken);
        return outcome switch
        {
            AnalysisLibraryBodyUseOutcome.Available available =>
                available.Result,
            AnalysisLibraryBodyUseOutcome.Rejected rejected =>
                throw new Xunit.Sdk.XunitException(
                    $"{rejected.Kind}: {rejected.Detail}"),
            _ => throw new Xunit.Sdk.XunitException(
                $"Unexpected outcome {outcome.GetType().Name}."),
        };
    }

    static string DescribeDiagnostics(
        AnalysisLibraryBodyUseResult result) =>
        string.Join(
            Environment.NewLine,
            result.Diagnostics
                .GroupBy(static diagnostic =>
                    (diagnostic.Kind, diagnostic.Detail))
                .Select(static group =>
                    $"{group.Count()}x {group.Key.Kind}: "
                    + group.Key.Detail));
}
