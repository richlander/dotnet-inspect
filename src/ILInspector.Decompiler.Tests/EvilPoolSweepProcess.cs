using System.Diagnostics;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Starts the file-based sweep with a deterministic build-time restore policy.
/// </summary>
/// <remarks>
/// This controls only the implicit restore that happens before the script runs.
/// The sweep's runtime package-source boundary remains
/// <c>DOTNET_INSPECT_SWEEP_NUGET_CONFIG</c>. The exact build arguments are gated
/// by <c>EvilPoolSweepGateTests.SweepLauncherOverridesAmbientNuGetSourcesAndAudit</c>.
/// </remarks>
internal static class EvilPoolSweepProcess
{
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
        startInfo.ArgumentList.Add(
            "-p:RestoreSources=https://api.nuget.org/v3/index.json");
        startInfo.ArgumentList.Add("-p:NuGetAudit=false");
        startInfo.ArgumentList.Add(Path.Combine(
            repositoryRoot,
            "eng",
            "prepare-decompiler-package-sweep.cs"));
        return startInfo;
    }
}
