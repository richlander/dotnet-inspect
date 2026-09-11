using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

public class PackageExtractorTests
{
    [Theory]
    [InlineData("Foo@1.2.3", "Foo", "1.2.3")]
    [InlineData("Newtonsoft.Json@13.0.3", "Newtonsoft.Json", "13.0.3")]
    [InlineData("System.Text.Json@9.0.0-preview.1", "System.Text.Json", "9.0.0-preview.1")]
    public void ParsePackageReference_WithVersion(string spec, string expectedId, string expectedVersion)
    {
        var result = PackageExtractor.ParsePackageReference(spec);
        Assert.NotNull(result);
        Assert.Equal(expectedId, result.Id);
        Assert.Equal(expectedVersion, result.Version);
    }

    [Theory]
    [InlineData("Newtonsoft.Json", "Newtonsoft.Json")]
    [InlineData("  Foo  ", "Foo")]
    public void ParsePackageReference_WithoutVersion(string spec, string expectedId)
    {
        var result = PackageExtractor.ParsePackageReference(spec);
        Assert.NotNull(result);
        Assert.Equal(expectedId, result.Id);
        Assert.Null(result.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@1.0.0")]
    public void ParsePackageReference_Invalid(string? spec)
    {
        var result = PackageExtractor.ParsePackageReference(spec!);
        Assert.Null(result);
    }

    [Fact]
    public void IsValidPackage_NonexistentDirectory_ReturnsFalse()
    {
        Assert.False(PackageExtractor.IsValidPackage("/nonexistent/path"));
    }

    [Fact]
    public void IsValidPackage_EmptyDirectory_ReturnsFalse()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(PackageExtractor.IsValidPackage(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IsValidPackage_DirectoryWithNuspec_ReturnsTrue()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test.nuspec"), "<package/>");
        try
        {
            Assert.True(PackageExtractor.IsValidPackage(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IsValidPackage_DirectoryWithLib_ReturnsTrue()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "lib"));
        try
        {
            Assert.True(PackageExtractor.IsValidPackage(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IsValidPackage_DirectoryWithTools_ReturnsTrue()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "tools"));
        try
        {
            Assert.True(PackageExtractor.IsValidPackage(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ExtractAsync_ExtractsZipContents()
    {
        // Create a minimal zip in memory
        string dir = Path.Combine(Path.GetTempPath(), $"nf-extract-{Guid.NewGuid():N}");
        string zipFile = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}.zip");
        try
        {
            Directory.CreateDirectory(dir + "-src");
            File.WriteAllText(Path.Combine(dir + "-src", "test.nuspec"), "<package/>");
            System.IO.Compression.ZipFile.CreateFromDirectory(dir + "-src", zipFile);

            using var stream = File.OpenRead(zipFile);
            string result = await PackageExtractor.ExtractAsync(stream, dir, TestContext.Current.CancellationToken);

            Assert.Equal(dir, result);
            Assert.True(File.Exists(Path.Combine(dir, "test.nuspec")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (Directory.Exists(dir + "-src")) Directory.Delete(dir + "-src", true);
            if (File.Exists(zipFile)) File.Delete(zipFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_ZipSlipEntry_Throws()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-zipslip-{Guid.NewGuid():N}");
        string zipFile = Path.Combine(Path.GetTempPath(), $"nf-zipslip-{Guid.NewGuid():N}.zip");
        try
        {
            // Create a zip with a path traversal entry
            using (var archive = System.IO.Compression.ZipFile.Open(zipFile, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../../../etc/evil.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("malicious");
            }

            using var stream = File.OpenRead(zipFile);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                PackageExtractor.ExtractAsync(stream, dir, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (File.Exists(zipFile)) File.Delete(zipFile);
        }
    }

    private static string WriteToolSettings(string root, string rid, string id)
    {
        string dir = Path.Combine(root, "tools", "net10.0", "any");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "DotnetToolSettings.xml");
        File.WriteAllText(path, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="mytool" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="mytool.linux-x64" />
                <RuntimeIdentifierPackage RuntimeIdentifier="{rid}" Id="{id}" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """);
        return path;
    }

    [Fact]
    public void HasManagedLibraries_TrueForNonResourceDll()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}");
        try
        {
            string libDir = Path.Combine(dir, "tools", "net10.0", "any");
            Directory.CreateDirectory(libDir);
            File.WriteAllText(Path.Combine(libDir, "MyLib.dll"), "stub");

            Assert.True(PackageExtractor.HasManagedLibraries(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void HasManagedLibraries_FalseForResourceOnlyOrEmpty()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}");
        try
        {
            string libDir = Path.Combine(dir, "tools", "net10.0", "any", "de");
            Directory.CreateDirectory(libDir);
            File.WriteAllText(Path.Combine(libDir, "MyLib.resources.dll"), "stub");

            Assert.False(PackageExtractor.HasManagedLibraries(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryGetToolWrapperRedirect_ReturnsAnyPackage_ForWrapper()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}");
        try
        {
            WriteToolSettings(dir, "any", "mytool.any");
            Assert.Equal("mytool.any", PackageExtractor.TryGetToolWrapperRedirect(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryGetToolWrapperRedirect_NullWhenManagedLibrariesPresent()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}");
        try
        {
            WriteToolSettings(dir, "any", "mytool.any");
            string libDir = Path.Combine(dir, "tools", "net10.0", "any");
            File.WriteAllText(Path.Combine(libDir, "mytool.dll"), "stub");

            Assert.Null(PackageExtractor.TryGetToolWrapperRedirect(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryGetToolWrapperRedirect_NullWhenNoManifest()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            Assert.Null(PackageExtractor.TryGetToolWrapperRedirect(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
