using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

public class CallerScopeResolverTests
{
    [Fact]
    public async Task ResolveAsync_HardLinkedAssembliesAreScannedOnce()
    {
        string directory = Directory.CreateTempSubdirectory(
            "caller-scope-hard-link-").FullName;
        string original = Path.Combine(directory, "Caller.dll");
        string alias = Path.Combine(directory, "Alias.dll");

        try
        {
            File.Copy(typeof(CallerScopeResolverTests).Assembly.Location, original);
            CreateHardLink(alias, original);

            using var httpClient = new HttpClient();
            using CallerScopeAssemblySet assemblySet =
                await CallerScopeResolver.ResolveAsync(
                    directories: [directory],
                    projects: [],
                    packages: [],
                    tfm: null,
                    ownAssemblyPath: null,
                    httpClient,
                    new VerboseLogger(enabled: false));

            Assert.Single(assemblySet.Assemblies);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_HardLinkedOwnAssemblyIsExcluded()
    {
        string directory = Directory.CreateTempSubdirectory(
            "caller-scope-own-hard-link-").FullName;
        string original = Path.Combine(directory, "Caller.dll");
        string alias = Path.Combine(directory, "Alias.dll");

        try
        {
            File.Copy(typeof(CallerScopeResolverTests).Assembly.Location, original);
            CreateHardLink(alias, original);

            using var httpClient = new HttpClient();
            using CallerScopeAssemblySet assemblySet =
                await CallerScopeResolver.ResolveAsync(
                    directories: [directory],
                    projects: [],
                    packages: [],
                    tfm: null,
                    ownAssemblyPath: original,
                    httpClient,
                    new VerboseLogger(enabled: false));

            Assert.Empty(assemblySet.Assemblies);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_CaseDistinctWindowsAssembliesRemainDistinct()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string directory = Directory.CreateTempSubdirectory(
            "caller-scope-case-").FullName;

        try
        {
            EnableCaseSensitiveDirectory(directory);
            string upper = Path.Combine(directory, "Evidence.dll");
            string lower = Path.Combine(directory, "evidence.dll");
            File.Copy(typeof(CallerScopeResolverTests).Assembly.Location, upper);
            File.Copy(typeof(CallerScopeResolver).Assembly.Location, lower);
            Assert.Equal(
                2,
                Directory.EnumerateFiles(directory, "*.dll").Count());

            using var httpClient = new HttpClient();
            using CallerScopeAssemblySet assemblySet =
                await CallerScopeResolver.ResolveAsync(
                    directories: [directory],
                    projects: [],
                    packages: [],
                    tfm: null,
                    ownAssemblyPath: null,
                    httpClient,
                    new VerboseLogger(enabled: false));

            Assert.Equal(2, assemblySet.Assemblies.Count);
            Assert.Contains(Path.GetFullPath(upper), assemblySet.Assemblies);
            Assert.Contains(Path.GetFullPath(lower), assemblySet.Assemblies);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_CallerPackageKeepsExtractedAssembliesUntilDisposed()
    {
        var packageDir = Directory.CreateTempSubdirectory("caller-scope-package-test").FullName;
        var packagePath = Path.Combine(packageDir, "CallerScope.1.0.0.nupkg");
        var sourceAssembly = typeof(CallerScopeResolverTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "lib/net10.0/CallerScope.dll");
            }

            using var httpClient = new HttpClient();
            var assemblySet = await CallerScopeResolver.ResolveAsync(
                directories: [],
                projects: [],
                packages: [packagePath],
                tfm: "net10.0",
                ownAssemblyPath: null,
                httpClient,
                new VerboseLogger(enabled: false));

            var assemblyPath = Assert.Single(assemblySet.Assemblies);
            Assert.True(File.Exists(assemblyPath));

            assemblySet.Dispose();

            Assert.False(File.Exists(assemblyPath));
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    private static void CreateHardLink(string path, string target)
    {
        ProcessStartInfo startInfo;
        if (OperatingSystem.IsWindows())
        {
            startInfo = new ProcessStartInfo("fsutil.exe");
            startInfo.ArgumentList.Add("hardlink");
            startInfo.ArgumentList.Add("create");
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            startInfo = new ProcessStartInfo("ln");
        }
        else
        {
            Assert.Skip($"Hard-link creation is not supported on " +
                $"{RuntimeInformation.OSDescription}.");
            return;
        }

        startInfo.ArgumentList.Add(path);
        startInfo.ArgumentList.Add(target);
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.UseShellExecute = false;

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start {startInfo.FileName}.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"Could not hard-link '{path}' to '{target}'.\n" +
            $"stdout:\n{standardOutput}\nstderr:\n{standardError}");
    }

    private static void EnableCaseSensitiveDirectory(string directory)
    {
        var startInfo = new ProcessStartInfo("fsutil.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("file");
        startInfo.ArgumentList.Add("SetCaseSensitiveInfo");
        startInfo.ArgumentList.Add(directory);
        startInfo.ArgumentList.Add("enable");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start fsutil.exe.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"Could not enable case sensitivity for '{directory}'.\n" +
            $"stdout:\n{standardOutput}\nstderr:\n{standardError}");
    }
}
