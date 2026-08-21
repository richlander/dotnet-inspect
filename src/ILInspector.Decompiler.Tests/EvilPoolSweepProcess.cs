using System.Diagnostics;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Starts the file-based sweep with a deterministic build-time restore policy.
/// </summary>
/// <remarks>
/// This controls only the implicit restore that happens before the script runs.
/// The sweep's runtime package-source boundary remains
/// <c>DOTNET_INSPECT_SWEEP_NUGET_CONFIG</c>. The exact build arguments are gated
/// by <c>EvilPoolSweepGateTests.SweepLauncherSuppressesAmbientSourceAndAuditDiagnostics</c>.
/// </remarks>
internal static class EvilPoolSweepProcess
{
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
        // Keep the machine's usable sources, including corporate proxies. The
        // sweep's own package acquisition is isolated separately at runtime.
        startInfo.ArgumentList.Add("-p:NoWarn=NU1507");
        startInfo.ArgumentList.Add("-p:NuGetAudit=false");
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
        string? disabled = Environment.GetEnvironmentVariable(
            "DOTNET_SYSTEM_IO_DISABLEFILELOCKING");
        if (disabled is "1"
            || bool.TryParse(disabled, out bool disableFileLocking)
                && disableFileLocking)
        {
            throw new InvalidOperationException(
                "The package-sweep launcher requires cross-process file locking, "
                + "but DOTNET_SYSTEM_IO_DISABLEFILELOCKING disables it.");
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
                    // closed. macOS exclusion is exercised by the independent-
                    // host gate because FileStream.Lock is unavailable there.
                    if (!OperatingSystem.IsMacOS())
                        stream.Lock(0, 1);

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
}

internal sealed class EvilPoolSweepRun(
    Process process,
    IDisposable runLock) : IDisposable
{
    public Process Process { get; } = process;

    public void Terminate()
    {
        if (!Process.HasExited)
        {
            try
            {
                Process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (Process.HasExited)
            {
            }
        }

        Process.WaitForExit();
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
