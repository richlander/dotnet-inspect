using System.Diagnostics;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ReleaseNotesTests
{
    [Fact]
    public async Task ReleaseNotes_PrintsEmbeddedContent()
    {
        string executable = Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows()
                ? "dotnet-inspect.exe"
                : "dotnet-inspect");
        Assert.True(
            File.Exists(executable),
            $"Expected the built CLI at '{executable}'.");

        var startInfo = new ProcessStartInfo(executable, "--release-notes")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start CLI apphost '{executable}'.");
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        Assert.Equal(0, process.ExitCode);
        Assert.Empty(await stderr);
        string output = await stdout;
        Assert.StartsWith("# Release Notes", output);
        Assert.Contains("## Unreleased", output, StringComparison.Ordinal);
    }
}
