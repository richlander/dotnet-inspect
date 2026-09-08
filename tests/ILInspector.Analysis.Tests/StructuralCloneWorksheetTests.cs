using System.Diagnostics;
using System.Text.Json;

using ILInspector.Analysis.StructuralCloneFixtures;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class StructuralCloneWorksheetTests
{
    [Fact]
    public void Run_ProjectsProductOwnedRanking()
    {
        StructuralCloneWorksheetReport report =
            StructuralCloneWorksheet.Run(
                typeof(StructuralCloneFixture).Assembly.Location,
                $"{typeof(StructuralCloneFixture).FullName}::"
                    + nameof(StructuralCloneFixture.NearConstantA));

        Assert.True(report.Success);
        StructuralCloneWorksheetCandidate peer = Assert.Single(
            report.Candidates,
            static candidate =>
                candidate.Method.Name
                    == nameof(StructuralCloneFixture.NearConstantB));
        Assert.InRange(peer.Rank, 1, 5);
        Assert.Contains(
            "FUZZY CLONE WORKSHEET: Completed",
            StructuralCloneWorksheet.Format(report));
        Assert.Contains(
            $"#{peer.Rank} score={peer.Similarity.Score}",
            StructuralCloneWorksheet.Format(report));
    }

    [Fact]
    public void Json_RetainsEveryProductCandidate()
    {
        StructuralCloneWorksheetReport report =
            StructuralCloneWorksheet.Run(
                typeof(StructuralCloneFixture).Assembly.Location,
                $"{typeof(StructuralCloneFixture).FullName}::"
                    + nameof(StructuralCloneFixture.NearCallTargetA));

        using JsonDocument json = JsonDocument.Parse(
            StructuralCloneWorksheet.ToJson(report));

        Assert.Equal(
            report.Candidates.Length,
            json.RootElement.GetProperty("candidates").GetArrayLength());
        Assert.Equal(
            report.Receipt.ReturnedCandidates,
            report.Candidates.Length);
    }

    [Fact]
    public async Task Command_RequiresSeed()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(
            typeof(StructuralCloneWorksheet).Assembly.Location);
        start.ArgumentList.Add("--clone-worksheet");
        start.ArgumentList.Add(
            typeof(StructuralCloneFixture).Assembly.Location);

        using Process process = Process.Start(start)!;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Task<string> standardError =
            process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> standardOutput =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        Assert.Equal(2, process.ExitCode);
        Assert.Contains(
            "--clone-worksheet requires --seed.",
            await standardError);
        Assert.Equal("", await standardOutput);
    }

    [Fact]
    public async Task Command_RejectsBlankSeed()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(
            typeof(StructuralCloneWorksheet).Assembly.Location);
        start.ArgumentList.Add("--clone-worksheet");
        start.ArgumentList.Add(
            typeof(StructuralCloneFixture).Assembly.Location);
        start.ArgumentList.Add("--seed");
        start.ArgumentList.Add("");

        using Process process = Process.Start(start)!;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Task<string> standardError =
            process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> standardOutput =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        Assert.Equal(2, process.ExitCode);
        Assert.Contains(
            "--clone-worksheet requires a non-empty --seed.",
            await standardError);
        Assert.Equal("", await standardOutput);
    }

    [Fact]
    public async Task Command_RunsProductWorksheet()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(
            typeof(StructuralCloneWorksheet).Assembly.Location);
        start.ArgumentList.Add("--clone-worksheet");
        start.ArgumentList.Add(
            typeof(StructuralCloneFixture).Assembly.Location);
        start.ArgumentList.Add("--seed");
        start.ArgumentList.Add(
            $"{typeof(StructuralCloneFixture).FullName}::"
                + nameof(StructuralCloneFixture.NearConstantA));
        start.ArgumentList.Add("--top");
        start.ArgumentList.Add("3");

        using Process process = Process.Start(start)!;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Task<string> standardError =
            process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> standardOutput =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        Assert.Equal(0, process.ExitCode);
        Assert.Equal("", await standardError);
        Assert.Contains(
            "FUZZY CLONE WORKSHEET: Completed",
            await standardOutput);
    }
}
