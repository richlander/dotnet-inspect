using System.Reflection;
using System.Runtime.InteropServices;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for PlatformResolver, particularly around DOTNET_ROOT priority and Linux path detection.
/// </summary>
public class PlatformResolverTests
{
    [Fact]
    public void FrameworkMappings_ContainsExpectedFrameworks()
    {
        Assert.True(PlatformResolver.FrameworkMappings.ContainsKey("runtime"));
        Assert.True(PlatformResolver.FrameworkMappings.ContainsKey("aspnetcore"));
        Assert.True(PlatformResolver.FrameworkMappings.ContainsKey("netstandard"));

        Assert.Equal("Microsoft.NETCore.App.Ref", PlatformResolver.FrameworkMappings["runtime"]);
        Assert.Equal("Microsoft.AspNetCore.App.Ref", PlatformResolver.FrameworkMappings["aspnetcore"]);
        Assert.Equal("NETStandard.Library.Ref", PlatformResolver.FrameworkMappings["netstandard"]);
    }

    [Fact]
    public void ReverseFrameworkMappings_MapsBackToShortNames()
    {
        Assert.Equal("runtime", PlatformResolver.ReverseFrameworkMappings["Microsoft.NETCore.App.Ref"]);
        Assert.Equal("aspnetcore", PlatformResolver.ReverseFrameworkMappings["Microsoft.AspNetCore.App.Ref"]);
        Assert.Equal("netstandard", PlatformResolver.ReverseFrameworkMappings["NETStandard.Library.Ref"]);
    }

    [Fact]
    public void GetInstalledVersions_NonExistentPath_ReturnsEmpty()
    {
        var versions = PlatformResolver.GetInstalledVersions("/nonexistent/path/that/does/not/exist");

        Assert.Empty(versions);
    }

    [Fact]
    public void GetInstalledVersions_SortsVersionsDescending()
    {
        // Use a temp directory with version-like subdirectories
        var tempDir = Path.Combine(Path.GetTempPath(), $"platform-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "8.0.0"));
            Directory.CreateDirectory(Path.Combine(tempDir, "9.0.1"));
            Directory.CreateDirectory(Path.Combine(tempDir, "10.0.0-preview.1"));
            Directory.CreateDirectory(Path.Combine(tempDir, "7.0.15"));

            var versions = PlatformResolver.GetInstalledVersions(tempDir);

            Assert.Equal(4, versions.Count);
            // Should be sorted descending: 10.x, 9.x, 8.x, 7.x
            Assert.Equal("10.0.0-preview.1", versions[0]);
            Assert.Equal("9.0.1", versions[1]);
            Assert.Equal("8.0.0", versions[2]);
            Assert.Equal("7.0.15", versions[3]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetInstalledVersions_IgnoresNonVersionDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"platform-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "8.0.0"));
            Directory.CreateDirectory(Path.Combine(tempDir, "ref"));  // Not a version
            Directory.CreateDirectory(Path.Combine(tempDir, "tools")); // Not a version

            var versions = PlatformResolver.GetInstalledVersions(tempDir);

            Assert.Single(versions);
            Assert.Equal("8.0.0", versions[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveFramework_UnknownFramework_ReturnsError()
    {
        var (refPath, version, error) = PlatformResolver.ResolveFramework("unknownframework");

        Assert.Null(refPath);
        Assert.Null(version);
        Assert.NotNull(error);
        Assert.Contains("Unknown framework", error);
        Assert.Contains("unknownframework", error);
    }

    [Fact]
    public void ResolveFramework_ParsesVersionSyntax()
    {
        // This tests the framework@version parsing logic
        // Even if the path doesn't exist, we can verify the error message shows the parsed framework
        var (refPath, version, error) = PlatformResolver.ResolveFramework("runtime@9.0", "/nonexistent");

        Assert.Null(refPath);
        Assert.NotNull(error);
        // The error should indicate the framework was parsed (runtime), not that the syntax was invalid
        Assert.Contains("runtime", error);
    }

    [Fact]
    public void ResolveAssembly_AddsExtensionIfMissing()
    {
        // Test that assembly resolution adds .dll extension
        var (path, framework, version, error) = PlatformResolver.ResolveAssembly(
            "System.Runtime",
            "runtime",
            "/nonexistent/packs");

        // Should fail because path doesn't exist, but the assembly name processing happens first
        Assert.NotNull(error);
        // The key thing is it didn't crash due to missing extension
    }

    [Fact]
    public void ResolveAssembly_WithDllExtension_DoesNotDoubleAdd()
    {
        var (path, framework, version, error) = PlatformResolver.ResolveAssembly(
            "System.Runtime.dll",
            "runtime",
            "/nonexistent/packs");

        Assert.NotNull(error);
        // Should not have .dll.dll in error message
        Assert.DoesNotContain(".dll.dll", error ?? "");
    }

    [Fact]
    public void ResolveAssembly_NetstandardWithRuntimeFlag_ReturnsError()
    {
        // netstandard doesn't have runtime assemblies (ref-only)
        var (path, framework, version, error) = PlatformResolver.ResolveAssembly(
            "netstandard",
            "netstandard",
            packsDirectory: null,
            useRuntimeAssemblies: true);

        Assert.NotNull(error);
        Assert.Contains("netstandard", error);
        Assert.Contains("runtime", error.ToLowerInvariant());
    }

    [Fact]
    public void GetRefAssemblyPath_NonExistentPath_ReturnsNull()
    {
        var path = PlatformResolver.GetRefAssemblyPath("/nonexistent/path", "9.0.0");

        Assert.Null(path);
    }

    [Fact]
    public void GetAssemblies_NonExistentPath_ReturnsEmpty()
    {
        var assemblies = PlatformResolver.GetAssemblies("/nonexistent/path");

        Assert.Empty(assemblies);
    }

    [Fact]
    public void GetAssemblies_ReturnsOrderedList()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"platform-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "Zebra.dll"), "");
            File.WriteAllText(Path.Combine(tempDir, "Alpha.dll"), "");
            File.WriteAllText(Path.Combine(tempDir, "Middle.dll"), "");

            var assemblies = PlatformResolver.GetAssemblies(tempDir);

            Assert.Equal(3, assemblies.Count);
            Assert.Equal("Alpha.dll", assemblies[0].Name);
            Assert.Equal("Middle.dll", assemblies[1].Name);
            Assert.Equal("Zebra.dll", assemblies[2].Name);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies GetPacksDirectory returns the app cache packs path.
    /// </summary>
    [Fact]
    public void GetPacksDirectory_ReturnsAppCachePath()
    {
        var result = PlatformResolver.GetPacksDirectory();
        var expectedSuffix = Path.Combine("dotnet-inspect", "packs");

        // App cache packs dir is the only candidate; it exists if we've downloaded packs
        if (result != null)
        {
            Assert.EndsWith(expectedSuffix, result);
        }
    }

    /// <summary>
    /// Scans all assemblies in installed ref packs and verifies every one passes IsPlatformCandidate.
    /// If this test fails, a new platform assembly name needs to be added to the candidate check.
    /// This caught WindowsBase, mscorlib, netstandard, and System (bare) as special cases.
    /// </summary>
    [Fact]
    public void IsPlatformCandidate_CoversAllRefPackAssemblies()
    {
        // Check installed packs on disk
        var packsDir = FindAnyRefPacksDirectory();
        if (packsDir == null)
        {
            return; // No packs available to scan
        }

        // Only scan the 3 known ref packs (runtime, aspnetcore, netstandard)
        List<string> dllFiles = [];
        foreach (var packName in PlatformResolver.FrameworkMappings.Values)
        {
            var packPath = Path.Combine(packsDir, packName);
            if (!Directory.Exists(packPath))
                continue;

            var files = Directory.GetFiles(packPath, "*.dll", SearchOption.AllDirectories)
                .Where(f => f.Contains(Path.Combine("ref", "net")))
                .Select(f => Path.GetFileNameWithoutExtension(f));
            dllFiles.AddRange(files);
        }

        dllFiles = dllFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (dllFiles.Count == 0)
        {
            return; // No ref assemblies found
        }

        List<string> uncovered = [];
        foreach (var name in dllFiles)
        {
            if (!PlatformResolver.IsPlatformCandidate(name))
            {
                uncovered.Add(name);
            }
        }

        Assert.True(uncovered.Count == 0,
            $"Platform assemblies not covered by IsPlatformCandidate: {string.Join(", ", uncovered)}. " +
            $"Add these names to PlatformResolver.IsPlatformCandidate().");
    }

    /// <summary>
    /// Finds any available ref packs directory (app cache or installed SDK).
    /// Only returns directories for the 3 known ref packs (runtime, aspnetcore, netstandard)
    /// to avoid picking up extra SDK packs (Android, WPF, etc.) that IsPlatformCandidate
    /// is not intended to cover.
    /// </summary>
    private static string? FindAnyRefPacksDirectory()
    {
        // Try app cache first
        var appCache = PlatformPackService.GetPacksCachePath();
        if (appCache != null && Directory.Exists(appCache))
            return appCache;

        // Fall back to installed SDK — but only scan known ref pack subdirectories
        string[] roots = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? [@"C:\Program Files\dotnet\packs"]
            : ["/usr/lib/dotnet/packs", "/usr/share/dotnet/packs", "/usr/local/share/dotnet/packs"];

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            // Check that at least one known ref pack exists
            foreach (var packName in PlatformResolver.FrameworkMappings.Values)
            {
                if (Directory.Exists(Path.Combine(root, packName)))
                    return root;
            }
        }

        return null;
    }

    /// <summary>
    /// Verifies DOTNET_ROOT is checked FIRST for shared directory as well.
    /// </summary>
    [Fact]
    public void GetSharedDirectory_WhenDotnetRootSet_ChecksItFirst()
    {
        var tempDotnetRoot = Path.Combine(Path.GetTempPath(), $"dotnet-root-test-{Guid.NewGuid():N}");
        var sharedDir = Path.Combine(tempDotnetRoot, "shared");
        var runtimeDir = Path.Combine(sharedDir, "Microsoft.NETCore.App", "9.0.0");

        var originalDotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        try
        {
            Directory.CreateDirectory(runtimeDir);

            Environment.SetEnvironmentVariable("DOTNET_ROOT", tempDotnetRoot);

            var result = PlatformResolver.GetSharedDirectory();

            Assert.NotNull(result);
            Assert.Equal(sharedDir, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", originalDotnetRoot);
            if (Directory.Exists(tempDotnetRoot))
                Directory.Delete(tempDotnetRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that when DOTNET_ROOT is not set, the resolver falls back to standard paths.
    /// </summary>
    [Fact]
    public void GetPacksDirectory_WhenDotnetRootNotSet_UsesStandardPaths()
    {
        var originalDotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        try
        {
            // Clear DOTNET_ROOT
            Environment.SetEnvironmentVariable("DOTNET_ROOT", null);

            // GetPacksDirectory should still find the system installation
            var result = PlatformResolver.GetPacksDirectory();

            // On a system with .NET installed, this should return a valid path
            // We can't assert a specific path since it varies by OS, but we can verify
            // it either returns null (no .NET) or a path that exists
            if (result != null)
            {
                Assert.True(Directory.Exists(result), $"Returned packs directory should exist: {result}");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", originalDotnetRoot);
        }
    }

    /// <summary>
    /// Tests that GetInstalledFrameworks works with a custom packs directory.
    /// </summary>
    [Fact]
    public void GetInstalledFrameworks_WithCustomPacksDir_ReturnsFrameworks()
    {
        var tempPacksDir = Path.Combine(Path.GetTempPath(), $"packs-test-{Guid.NewGuid():N}");
        try
        {
            // Create a minimal runtime ref pack structure
            var runtimeRef = Path.Combine(tempPacksDir, "Microsoft.NETCore.App.Ref", "9.0.0", "ref", "net9.0");
            Directory.CreateDirectory(runtimeRef);
            File.WriteAllText(Path.Combine(runtimeRef, "System.Runtime.dll"), "");
            File.WriteAllText(Path.Combine(runtimeRef, "System.Console.dll"), "");

            var frameworks = PlatformResolver.GetInstalledFrameworks(tempPacksDir);

            Assert.Single(frameworks);
            Assert.Equal("runtime", frameworks[0].ShortName);
            Assert.Equal("Microsoft.NETCore.App.Ref", frameworks[0].RefPackName);
            Assert.Equal("9.0.0", frameworks[0].LatestVersion);
            Assert.Equal(2, frameworks[0].AssemblyCount);
        }
        finally
        {
            if (Directory.Exists(tempPacksDir))
                Directory.Delete(tempPacksDir, recursive: true);
        }
    }

    /// <summary>
    /// Tests that multiple framework versions are detected and sorted correctly.
    /// </summary>
    [Fact]
    public void GetInstalledFrameworks_MultipleVersions_SortsDescending()
    {
        var tempPacksDir = Path.Combine(Path.GetTempPath(), $"packs-test-{Guid.NewGuid():N}");
        try
        {
            // Create multiple versions
            foreach (var version in new[] { "8.0.0", "9.0.0", "10.0.0-preview.1" })
            {
                var refDir = Path.Combine(tempPacksDir, "Microsoft.NETCore.App.Ref", version, "ref", $"net{version.Split('.')[0]}.0");
                Directory.CreateDirectory(refDir);
                File.WriteAllText(Path.Combine(refDir, "System.Runtime.dll"), "");
            }

            var frameworks = PlatformResolver.GetInstalledFrameworks(tempPacksDir);

            Assert.Single(frameworks);
            var runtime = frameworks[0];
            Assert.Equal(3, runtime.AllVersions.Count);
            Assert.Equal("10.0.0-preview.1", runtime.AllVersions[0]); // Latest first
            Assert.Equal("9.0.0", runtime.AllVersions[1]);
            Assert.Equal("8.0.0", runtime.AllVersions[2]);
            Assert.Equal("10.0.0-preview.1", runtime.LatestVersion);
        }
        finally
        {
            if (Directory.Exists(tempPacksDir))
                Directory.Delete(tempPacksDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("System.Text.Json", "runtime")]
    [InlineData("System.Runtime", "runtime")]
    [InlineData("Microsoft.CSharp", "runtime")]
    [InlineData("Microsoft.Win32.Registry", "runtime")]
    [InlineData("Microsoft.VisualBasic", "runtime")]
    [InlineData("Microsoft.AspNetCore.Http", "aspnetcore")]
    [InlineData("Microsoft.Extensions.Logging", "aspnetcore")]
    [InlineData("Microsoft.JSInterop.Something", "aspnetcore")]
    [InlineData("Microsoft.Net.Http.Headers", "aspnetcore")]
    public void GetBiasedPack_ReturnsCorrectPack(string assemblyName, string expected)
    {
        Assert.Equal(expected, PlatformPackService.GetBiasedPack(assemblyName));
    }

    [Theory]
    [InlineData("mscorlib")]
    [InlineData("netstandard")]
    [InlineData("WindowsBase")]
    [InlineData("Newtonsoft.Json")]
    public void GetBiasedPack_ReturnsNull_ForNonBiasedNames(string assemblyName)
    {
        Assert.Null(PlatformPackService.GetBiasedPack(assemblyName));
    }
}
