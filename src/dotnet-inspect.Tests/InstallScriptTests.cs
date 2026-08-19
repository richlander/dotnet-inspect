using System.Diagnostics;
using System.Text;

namespace DotnetInspector.Tests;

public sealed class InstallScriptTests
{
    static readonly string RepoRoot = FindRepoRoot();
    static readonly string InstallScript = Path.Combine(RepoRoot, "install.ps1");
    static readonly IReadOnlyList<string> PowerShellExecutables =
        FindPowerShellExecutables();

    [Fact]
    public async Task MissingInstaller_UsesTemporaryToolAndHonorsInstallDirectory()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "install.ps1 is Windows-only");
        Assert.SkipUnless(PowerShellExecutables.Count > 0, "PowerShell is not available");

        foreach (string executable in PowerShellExecutables)
            await VerifyMissingInstallerAsync(executable);
    }

    static async Task VerifyMissingInstallerAsync(string executable)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = CreateTempDirectory();
        try
        {
            string bootstrapLog = Path.Combine(directory, "bootstrap.log");
            string installerLog = Path.Combine(directory, "installer.log");
            string pathLog = Path.Combine(directory, "path.log");
            string installDirectory = Path.Combine(directory, "custom install");
            string wrapper = Path.Combine(directory, "run.ps1");
            await File.WriteAllTextAsync(
                wrapper,
                """
                function dotnet {
                    [IO.File]::WriteAllLines(
                        $env:DOTNET_BOOTSTRAP_TEST_LOG,
                        [string[]]$args)
                    $toolPath = $args[3]
                    New-Item -ItemType Directory -Path $toolPath -Force | Out-Null
                    @'
                @echo off
                > "%DOTNET_INSTALL_TEST_LOG%" echo %*
                exit /b 0
                '@ | Set-Content `
                        -LiteralPath (Join-Path $toolPath "dotnet-install.cmd") `
                        -Encoding ascii
                    $global:LASTEXITCODE = 0
                }

                & $env:DOTNET_INSTALL_TEST_SCRIPT
                [IO.File]::WriteAllText($env:DOTNET_PATH_TEST_LOG, $env:PATH)
                """,
                cancellationToken);

            string initialPath = RemoveInstallerDirectoriesFromPath();
            var environment = new Dictionary<string, string?>
            {
                ["DOTNET_BOOTSTRAP_TEST_LOG"] = bootstrapLog,
                ["DOTNET_INSTALL_TEST_LOG"] = installerLog,
                ["DOTNET_INSTALL_TEST_SCRIPT"] = InstallScript,
                ["DOTNET_INSTALL_DIR"] = installDirectory,
                ["DOTNET_PATH_TEST_LOG"] = pathLog,
                ["PATH"] = initialPath,
                ["TEMP"] = directory,
                ["TMP"] = directory,
            };

            ProcessResult result =
                await RunPowerShellAsync(executable, wrapper, environment);

            AssertSuccessful(executable, result);
            string[] bootstrapArguments =
                await File.ReadAllLinesAsync(bootstrapLog, cancellationToken);
            Assert.Equal(
                ["tool", "install", "--tool-path"],
                bootstrapArguments[..3]);
            string bootstrapDirectory = bootstrapArguments[3];
            Assert.StartsWith(directory, bootstrapDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(
                "dotnet-install",
                bootstrapArguments[4],
                StringComparison.Ordinal);
            Assert.False(Directory.Exists(bootstrapDirectory));
            Assert.Equal(
                initialPath,
                await File.ReadAllTextAsync(pathLog, cancellationToken));

            string invocation =
                await File.ReadAllTextAsync(installerLog, cancellationToken);
            Assert.Contains("--package dotnet-inspect", invocation);
            Assert.Contains("--output", invocation);
            Assert.Contains(installDirectory, invocation);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingInstallerWithoutOverride_UsesDefaultArguments()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "install.ps1 is Windows-only");
        Assert.SkipUnless(PowerShellExecutables.Count > 0, "PowerShell is not available");

        foreach (string executable in PowerShellExecutables)
            await VerifyExistingInstallerAsync(executable);
    }

    static async Task VerifyExistingInstallerAsync(string executable)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = CreateTempDirectory();
        try
        {
            string installerLog = Path.Combine(directory, "installer.log");
            string wrapper = Path.Combine(directory, "run.ps1");
            await File.WriteAllTextAsync(
                wrapper,
                """
                function dotnet {
                    throw "dotnet bootstrap should not run"
                }
                function dotnet-install {
                    [IO.File]::WriteAllLines(
                        $env:DOTNET_INSTALL_TEST_LOG,
                        [string[]]$args)
                    $global:LASTEXITCODE = 0
                }

                & $env:DOTNET_INSTALL_TEST_SCRIPT
                """,
                cancellationToken);

            var environment = new Dictionary<string, string?>
            {
                ["DOTNET_INSTALL_TEST_LOG"] = installerLog,
                ["DOTNET_INSTALL_TEST_SCRIPT"] = InstallScript,
                ["DOTNET_INSTALL_DIR"] = null,
            };

            ProcessResult result =
                await RunPowerShellAsync(executable, wrapper, environment);

            AssertSuccessful(executable, result);
            Assert.Equal(
                ["--package", "dotnet-inspect"],
                await File.ReadAllLinesAsync(installerLog, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static async Task<ProcessResult> RunPowerShellAsync(
        string executable,
        string script,
        IReadOnlyDictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        foreach ((string name, string? value) in environment)
        {
            if (value is null)
                startInfo.Environment.Remove(name);
            else
                startInfo.Environment[name] = value;
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start pwsh.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await stdout,
            await stderr);
    }

    static void AssertSuccessful(string executable, ProcessResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"""
            {Path.GetFileName(executable)} exited with code {result.ExitCode}.
            stdout:
            {result.Stdout}
            stderr:
            {result.Stderr}
            """);
    }

    static string RemoveInstallerDirectoriesFromPath()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return "";

        return string.Join(
            Path.PathSeparator,
            path.Split(Path.PathSeparator)
                .Where(static directory =>
                    !File.Exists(Path.Combine(directory, "dotnet-install.cmd"))
                    && !File.Exists(Path.Combine(directory, "dotnet-install.exe"))));
    }

    static IReadOnlyList<string> FindPowerShellExecutables()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return [];

        return path.Split(Path.PathSeparator)
            .SelectMany(static directory => new[]
            {
                Path.Combine(directory, "powershell.exe"),
                Path.Combine(directory, "pwsh.exe"),
            })
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-install-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing dotnet-inspect.slnx.");
    }

    readonly record struct ProcessResult(
        int ExitCode,
        string Stdout,
        string Stderr);
}
