using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace DotnetInspector.Tests;

public sealed class LauncherContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string PowerShellLauncher =
        Path.Combine(RepoRoot, "eng", "dotnet.ps1");
    private static readonly string BashLauncher =
        Path.Combine(RepoRoot, "eng", "dotnet.sh");

    [Fact]
    public async Task PowerShell_ForwardsArgumentsStreamsAndExitCode()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "PowerShell launcher is Windows-only.");
        using var fixture = LauncherFixture.Create();
        var environment = fixture.Environment(
            ("LAUNCHER_FIXTURE_STDERR", "fixture-error"),
            ("LAUNCHER_FIXTURE_EXIT", "42"));
        string[] forwarded =
        [
            "command",
            "",
            "a b",
            "x\"y",
            "trail\\",
            "--",
            "-p:Value=x"
        ];
        string driver = fixture.WritePowerShellDriver(
            "$arguments = @(" +
            string.Join(", ", forwarded.Select(QuotePowerShell)) +
            $"){Environment.NewLine}& {QuotePowerShell(PowerShellLauncher)} @arguments" +
            $"{Environment.NewLine}exit $LASTEXITCODE");
        ProcessResult result = await RunAsync(
            "pwsh",
            ["-NoProfile", "-File", driver],
            environment);

        AssertExitCode(42, result);
        Assert.Equal("fixture-error", result.StandardError);
        LauncherInvocation actual = DeserializeInvocation(result.StandardOutput);
        Assert.Equal(forwarded, actual.Args);
        Assert.Equal("", actual.Stdin);
    }

    [Fact]
    public async Task PowerShell_ForwardsPipelineInput()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "PowerShell launcher is Windows-only.");
        using var fixture = LauncherFixture.Create();
        string driver = fixture.WritePowerShellDriver(
            "$lines = @('stdin-one', 'stdin-two'); " +
            $"$lines | & {QuotePowerShell(PowerShellLauncher)} command" +
            $"{Environment.NewLine}exit $LASTEXITCODE");

        ProcessResult result = await RunAsync(
            "pwsh",
            ["-NoProfile", "-File", driver],
            fixture.Environment());

        AssertExitCode(0, result);
        LauncherInvocation actual = DeserializeInvocation(result.StandardOutput);
        Assert.Equal(["command"], actual.Args);
        Assert.Equal($"stdin-one{Environment.NewLine}stdin-two{Environment.NewLine}", actual.Stdin);
    }

    [Fact]
    public async Task PowerShell_PreservesRedirectedInputBytes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "PowerShell launcher is Windows-only.");
        using var fixture = LauncherFixture.Create();
        byte[] standardInput = [0x41, 0xc3, 0xa9, 0x0a, 0xff, 0x42];

        ProcessResult result = await RunAsync(
            "pwsh",
            ["-NoProfile", "-File", PowerShellLauncher, "command"],
            fixture.Environment(),
            standardInput);

        AssertExitCode(0, result);
        LauncherInvocation actual = DeserializeInvocation(result.StandardOutput);
        Assert.True(
            Convert.ToBase64String(standardInput) == actual.StdinBase64,
            $"stdin mismatch; stderr:{Environment.NewLine}{result.StandardError}");
    }

    [Fact]
    public async Task PowerShell_DoesNotDrainRedirectedInputBeforeLaunchingCommand()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "PowerShell launcher is Windows-only.");
        using var fixture = LauncherFixture.Create();
        var environment = fixture.Environment(("LAUNCHER_FIXTURE_READ_STDIN", "false"));

        ProcessResult result = await RunWithOpenInputAsync(
            "pwsh",
            ["-NoProfile", "-File", PowerShellLauncher, "command"],
            environment);

        AssertExitCode(0, result);
        LauncherInvocation actual = DeserializeInvocation(result.StandardOutput);
        Assert.Equal(["command"], actual.Args);
    }

    [Fact]
    public async Task PowerShell_RejectsFallbackWhenSelectedSdkIsNot11()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "PowerShell launcher is Windows-only.");
        using var fixture = LauncherFixture.Create();
        var environment = fixture.Environment(
            ("LAUNCHER_FIXTURE_INSTALL", "fail"),
            ("LAUNCHER_FIXTURE_SDKS", "11.0.100 [fixture]"),
            ("LAUNCHER_FIXTURE_VERSION", "10.0.100"));

        ProcessResult result = await RunAsync(
            "pwsh",
            ["-NoProfile", "-File", PowerShellLauncher, "command"],
            environment);

        AssertExitCode(23, result);
        Assert.Contains("did not select the required .NET 11 SDK", result.StandardError);
        Assert.DoesNotContain("\"Args\"", result.StandardOutput);
    }

    [Fact]
    public async Task PowerShell_FailedRefreshUsesSelected11WithWarning()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "PowerShell launcher is Windows-only.");
        using var fixture = LauncherFixture.Create();
        var environment = fixture.Environment(
            ("LAUNCHER_FIXTURE_INSTALL", "fail"),
            ("LAUNCHER_FIXTURE_SDKS", "11.0.100 [fixture]"),
            ("LAUNCHER_FIXTURE_VERSION", "11.0.100"));

        ProcessResult result = await RunAsync(
            "pwsh",
            ["-NoProfile", "-File", PowerShellLauncher, "command"],
            environment);

        AssertExitCode(0, result);
        Assert.Contains("using the installed .NET 11 SDK", result.StandardError);
        LauncherInvocation actual = DeserializeInvocation(result.StandardOutput);
        Assert.Equal(["command"], actual.Args);
    }

    [Fact]
    public async Task Bash_ForwardsArgumentsStreamsInputAndExitCode()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "Bash launcher runs in the Unix CI lane.");
        using var fixture = LauncherFixture.Create();
        var environment = fixture.Environment(
            ("LAUNCHER_FIXTURE_STDERR", "fixture-error"),
            ("LAUNCHER_FIXTURE_EXIT", "42"));
        string[] forwarded =
        [
            "command",
            "",
            "a b",
            "x\"y",
            "trail\\",
            "--",
            "-p:Value=x"
        ];

        ProcessResult result = await RunAsync(
            "bash",
            [BashLauncher, .. forwarded],
            environment,
            Encoding.UTF8.GetBytes($"stdin-one{Environment.NewLine}stdin-two{Environment.NewLine}"));

        AssertExitCode(42, result);
        Assert.Equal("fixture-error", result.StandardError);
        LauncherInvocation actual = DeserializeInvocation(result.StandardOutput);
        Assert.Equal(forwarded, actual.Args);
        Assert.Equal($"stdin-one{Environment.NewLine}stdin-two{Environment.NewLine}", actual.Stdin);
    }

    [Fact]
    public async Task Bash_RejectsFailedVersionProbeThatListsInstalled11()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "Bash launcher runs in the Unix CI lane.");
        using var fixture = LauncherFixture.Create();
        var environment = fixture.Environment(
            ("LAUNCHER_FIXTURE_VERSION", $"SDK resolution failed{Environment.NewLine}11.0.100 [fixture]"),
            ("LAUNCHER_FIXTURE_VERSION_EXIT", "155"));

        ProcessResult result = await RunAsync(
            "bash",
            [BashLauncher, "command"],
            environment);

        AssertExitCode(155, result);
        Assert.Contains("did not select the required .NET 11 SDK", result.StandardError);
        Assert.DoesNotContain("\"Args\"", result.StandardOutput);
    }

    [Fact]
    public async Task Bash_FailedRefreshUsesSelected11WithWarning()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "Bash launcher runs in the Unix CI lane.");
        using var fixture = LauncherFixture.Create();
        var environment = fixture.Environment(
            ("LAUNCHER_FIXTURE_INSTALL", "fail"),
            ("LAUNCHER_FIXTURE_SDKS", "11.0.100 [fixture]"),
            ("LAUNCHER_FIXTURE_VERSION", "11.0.100"));

        ProcessResult result = await RunAsync(
            "bash",
            [BashLauncher, "command"],
            environment);

        AssertExitCode(0, result);
        Assert.Contains("using the installed .NET 11 SDK", result.StandardError);
        LauncherInvocation actual = DeserializeInvocation(result.StandardOutput);
        Assert.Equal(["command"], actual.Args);
    }

    private static LauncherInvocation DeserializeInvocation(string standardOutput)
        => JsonSerializer.Deserialize<LauncherInvocation>(standardOutput.Trim())
           ?? throw new InvalidOperationException("Launcher fixture did not return an invocation.");

    private static void AssertExitCode(int expected, ProcessResult actual)
    {
        Assert.True(
            expected == actual.ExitCode,
            $"Expected exit code {expected}, actual {actual.ExitCode}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{actual.StandardOutput}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{actual.StandardError}");
    }

    private static string QuotePowerShell(string value)
        => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        byte[]? standardInput = null)
    {
        using var process = Process.Start(CreateStartInfo(fileName, arguments, environment))
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        if (standardInput is not null)
            await process.StandardInput.BaseStream.WriteAsync(standardInput);
        process.StandardInput.Close();
        await process.WaitForExitAsync();

        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static async Task<ProcessResult> RunWithOpenInputAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment)
    {
        using var process = Process.Start(CreateStartInfo(fileName, arguments, environment))
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        Task exited = process.WaitForExitAsync();
        if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(10))) != exited)
        {
            process.Kill(entireProcessTree: true);
            process.StandardInput.Close();
            await exited;
            throw new TimeoutException(
                $"{fileName} waited for its redirected input to close before launching the command.");
        }

        process.StandardInput.Close();
        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static ProcessStartInfo CreateStartInfo(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = RepoRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        foreach ((string key, string? value) in environment)
            startInfo.Environment[key] = value;
        return startInfo;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record LauncherInvocation(string[] Args, string Stdin, string StdinBase64);

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class LauncherFixture : IDisposable
    {
        private readonly string _directory;

        private LauncherFixture(string directory)
        {
            _directory = directory;
        }

        public static LauncherFixture Create()
        {
            Assembly testAssembly = typeof(LauncherContractTests).Assembly;
            string configuration = testAssembly
                .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
                ?? throw new InvalidOperationException("Test configuration not found.");
            string frameworkName = testAssembly
                .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName
                ?? throw new InvalidOperationException("Test target framework not found.");
            const string frameworkPrefix = ".NETCoreApp,Version=v";
            if (!frameworkName.StartsWith(frameworkPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unsupported target framework '{frameworkName}'.");
            string targetFramework = $"net{frameworkName[frameworkPrefix.Length..]}";
            string fixtureOutput = Path.Combine(
                RepoRoot,
                "tests",
                "DotnetInspector.LauncherFixture",
                "bin",
                configuration,
                targetFramework);
            string sourceAppHost = Path.Combine(
                fixtureOutput,
                "DotnetInspector.LauncherFixture" + (OperatingSystem.IsWindows() ? ".exe" : ""));

            string directory = Path.Combine(
                Path.GetTempPath(),
                $"dotnet-inspect-launcher-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            foreach (string source in Directory.EnumerateFiles(
                         fixtureOutput,
                         "DotnetInspector.LauncherFixture.*"))
            {
                File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));
            }

            string dotnetup = Path.Combine(
                directory,
                "dotnetup" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            File.Copy(sourceAppHost, dotnetup);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    dotnetup,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return new LauncherFixture(directory);
        }

        public Dictionary<string, string?> Environment(
            params (string Key, string? Value)[] values)
        {
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["DOTNETUP_INSTALL_DIR"] = _directory
            };
            foreach ((string key, string? value) in values)
                environment[key] = value;
            return environment;
        }

        public string WritePowerShellDriver(string contents)
        {
            string path = Path.Combine(_directory, $"driver-{Guid.NewGuid():N}.ps1");
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
