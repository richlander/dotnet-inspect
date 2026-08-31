using System.Diagnostics;

using DotnetInspector.Services;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class LocalRepoSourceProjectionTests : IDisposable
{
    readonly string _cacheDirectory =
        Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-local-repo-projection-{Guid.NewGuid():N}");

    [Fact]
    public async Task TypeSourceFilesPrint_AcceptsRepoAtCliBoundaryWhileOffline()
    {
        string repositoryRoot = FindRepositoryRoot();
        string productAssembly = Path.Combine(
            AppContext.BaseDirectory,
            "dotnet-inspect.dll");
        string executable = Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "dotnet-inspect.exe" : "dotnet-inspect");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        string[] arguments =
        [
            "type",
            typeof(CommandLineBuilder).FullName!,
            "--library",
            productAssembly,
            "-S",
            "Source Files",
            "--print",
            "--row",
            "first",
            "--repo",
            repositoryRoot,
            "--bare",
            "--tips",
            "q",
        ];
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DOTNET_INSPECT_OFFLINE"] = "1";
        startInfo.Environment["DOTNET_INSPECT_CACHE_DIR"] = _cacheDirectory;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {executable}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (
            !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            OutOfProcessCliProcess.KillAndWaitForExit(
                process,
                TimeSpan.FromSeconds(10));
            throw new TimeoutException($"{executable} did not exit.");
        }

        string output = await standardOutput;
        string error = await standardError;
        Assert.Equal(0, process.ExitCode);
        Assert.Contains(
            "public static class CommandLineBuilder",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "failed to fetch verified source",
            error,
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
            Directory.Delete(_cacheDirectory, recursive: true);
    }

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
    }
}
