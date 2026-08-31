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
        var result = await RunCliAsync(
            "type",
            typeof(CommandLineBuilder).FullName!,
            "--library",
            ProductAssemblyPath(),
            "-S",
            "Source Files",
            "--print",
            "--row",
            "first",
            "--repo",
            repositoryRoot,
            "-v:n",
            "--trace-mermaid",
            "--bare",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "public static class CommandLineBuilder",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "failed to fetch verified source",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "source-fetch",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeSourceFilesPrint_RouterPreservesRepoAtCliBoundaryWhileOffline()
    {
        string[] arguments =
        [
            typeof(CommandLineBuilder).FullName!,
            "--library",
            ProductAssemblyPath(),
            "-S",
            "Source Files",
            "--print",
            "--row",
            "first",
            "--repo",
            FindRepositoryRoot(),
            "-v:n",
            "--bare",
            "--tips",
            "q"
        ];

        var direct = await RunCliAsync(["type", .. arguments]);
        var deferred = await RunCliAsync(arguments);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
        Assert.Contains(
            "public static class CommandLineBuilder",
            deferred.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberSourceLocationsPrint_UsesPdbRecordedLocalPathWhileOffline()
    {
        var result = await RunCliAsync(
            "member",
            typeof(CommandLineBuilder).FullName!,
            "--library",
            ProductAssemblyPath(),
            "-m",
            nameof(CommandLineBuilder.TryGetStaleArgumentError),
            "-S",
            "Source Locations",
            "--print",
            "--row",
            "first",
            "--bare",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "public static bool TryGetStaleArgumentError",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "failed to fetch verified source",
            result.Error,
            StringComparison.Ordinal);
    }

    async Task<(int Exit, string Output, string Error)> RunCliAsync(
        params string[] arguments)
    {
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
        return (process.ExitCode, output, error);
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

    static string ProductAssemblyPath()
        => Path.Combine(AppContext.BaseDirectory, "dotnet-inspect.dll");
}
