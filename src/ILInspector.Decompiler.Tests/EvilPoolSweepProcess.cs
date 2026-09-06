using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Starts the file-based sweep using the repository's build-time restore policy.
/// </summary>
/// <remarks>
/// This controls only the implicit restore that happens before the script runs.
/// The sweep's runtime package-source boundary remains
/// <c>DOTNET_INSPECT_SWEEP_NUGET_CONFIG</c>. The exact build arguments are gated
/// by <c>EvilPoolSweepGateTests.SweepLauncherUsesRepositoryBuildWarningPolicy</c>.
/// </remarks>
internal static class EvilPoolSweepProcess
{
    internal const string DisableFileLockingEnvironmentVariable =
        "DOTNET_SYSTEM_IO_DISABLEFILELOCKING";
    internal const string DisableFileLockingSwitch =
        "System.IO.DisableFileLocking";

    internal static readonly TimeSpan RunLockTimeout =
        TimeSpan.FromMinutes(6);

    public static ProcessStartInfo Create(
        string repositoryRoot,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                is { Length: > 0 } host
                    ? host
                    : "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add(Path.Combine(
            repositoryRoot,
            "eng",
            "prepare-decompiler-package-sweep.cs"));
        return startInfo;
    }

    public static EvilPoolSweepRun Start(ProcessStartInfo startInfo) =>
        Start(startInfo, RunLockTimeout);

    internal static EvilPoolSweepRun Start(
        ProcessStartInfo startInfo,
        TimeSpan lockTimeout)
    {
        IDisposable runLock = AcquireRunLock(lockTimeout);
        try
        {
            Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("could not start the sweep");
            return new EvilPoolSweepRun(process, runLock);
        }
        catch
        {
            runLock.Dispose();
            throw;
        }
    }

    internal static IDisposable AcquireRunLock(TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows() && IsFileLockingDisabled())
        {
            throw new InvalidOperationException(
                "The package-sweep launcher requires cross-process file locking, "
                + "but the runtime configuration disables it.");
        }

        string directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "dotnet-inspect",
            "test-locks");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            "prepare-decompiler-package-sweep.lock");
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                try
                {
                    // FileShare.None may be advisory on Unix. Where supported,
                    // an explicit range lock makes unsupported locking fail
                    // closed. Apple and FreeBSD exclusion is exercised by the
                    // independent-host gate because FileStream.Lock is
                    // unavailable there.
                    if (!OperatingSystem.IsMacOS()
                        && !OperatingSystem.IsIOS()
                        && !OperatingSystem.IsTvOS()
                        && !OperatingSystem.IsMacCatalyst()
                        && !OperatingSystem.IsFreeBSD())
                    {
                        stream.Lock(0, 1);
                    }

                    return stream;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch (IOException) when (stopwatch.Elapsed < timeout)
            {
                Thread.Sleep(25);
            }
            catch (IOException ex)
            {
                throw new TimeoutException(
                    "Timed out waiting for exclusive access to the package-sweep "
                    + "file-app launcher.",
                    ex);
            }
        }
    }

    private static bool IsFileLockingDisabled()
    {
        string? environmentValue = Environment.GetEnvironmentVariable(
            DisableFileLockingEnvironmentVariable);
        if (environmentValue == "1"
            || string.Equals(
                environmentValue,
                bool.TrueString,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (environmentValue == "0"
            || string.Equals(
                environmentValue,
                bool.FalseString,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AppContext.TryGetSwitch(
            DisableFileLockingSwitch,
            out bool appContextValue)
            && appContextValue;
    }
}

internal sealed class EvilPoolSweepRun(
    Process process,
    IDisposable runLock,
    Action<Process, bool>? kill = null) : IDisposable
{
    private readonly Action<Process, bool> _kill =
        kill ?? (static (candidate, entireProcessTree) =>
            candidate.Kill(entireProcessTree));

    public Process Process { get; } = process;

    public void Terminate()
    {
        ExceptionDispatchInfo? treeKillFailure = null;
        if (!Process.HasExited)
        {
            try
            {
                _kill(Process, true);
            }
            catch (InvalidOperationException) when (Process.HasExited)
            {
            }
            catch (Exception ex)
            {
                treeKillFailure = ExceptionDispatchInfo.Capture(ex);
                try
                {
                    if (!Process.HasExited)
                        _kill(Process, false);
                }
                catch (InvalidOperationException) when (Process.HasExited)
                {
                }
                catch (Exception fallbackEx)
                {
                    treeKillFailure = ExceptionDispatchInfo.Capture(
                        new AggregateException(ex, fallbackEx));
                }
            }
        }

        Process.WaitForExit();
        treeKillFailure?.Throw();
    }

    public void Dispose()
    {
        try
        {
            Terminate();
        }
        finally
        {
            try
            {
                Process.Dispose();
            }
            finally
            {
                runLock.Dispose();
            }
        }
    }
}
