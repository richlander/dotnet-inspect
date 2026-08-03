using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

public class TfmResolverTests
{
    // --- GetTfmPriority ordering (inspired by NuGet.Client CompatibilityTests) ---

    [Theory]
    [InlineData("net10.0", "net9.0")]
    [InlineData("net9.0", "net8.0")]
    [InlineData("net8.0", "net7.0")]
    [InlineData("net7.0", "net6.0")]
    [InlineData("net6.0", "net5.0")]
    [InlineData("net5.0", "netcoreapp3.1")]
    [InlineData("netcoreapp3.1", "netcoreapp3.0")]
    [InlineData("netcoreapp3.0", "netcoreapp2.2")]
    [InlineData("netcoreapp2.2", "netcoreapp2.1")]
    [InlineData("netcoreapp2.1", "netcoreapp2.0")]
    [InlineData("netcoreapp2.0", "netcoreapp1.1")]
    [InlineData("netcoreapp1.1", "netcoreapp1.0")]
    [InlineData("net8.0", "netstandard2.1")]
    [InlineData("netstandard2.1", "netstandard2.0")]
    [InlineData("netstandard2.0", "netstandard1.6")]
    [InlineData("netstandard1.6", "netstandard1.5")]
    [InlineData("netstandard1.5", "netstandard1.4")]
    [InlineData("netstandard1.4", "netstandard1.3")]
    [InlineData("netstandard1.3", "netstandard1.2")]
    [InlineData("netstandard1.2", "netstandard1.1")]
    [InlineData("netstandard1.1", "netstandard1.0")]
    [InlineData("net6.0", "netstandard2.1")]
    [InlineData("netstandard2.0", "net461")]
    [InlineData("net481", "net48")]
    [InlineData("net48", "net472")]
    [InlineData("net472", "net471")]
    [InlineData("net471", "net47")]
    [InlineData("net47", "net462")]
    [InlineData("net462", "net461")]
    [InlineData("net461", "net46")]
    [InlineData("net46", "net452")]
    [InlineData("net452", "net451")]
    [InlineData("net451", "net45")]
    public void GetTfmPriority_HigherIsNewer(string newer, string older)
    {
        Assert.True(TfmResolver.GetTfmPriority(newer) > TfmResolver.GetTfmPriority(older),
            $"Expected {newer} > {older}, got {TfmResolver.GetTfmPriority(newer)} vs {TfmResolver.GetTfmPriority(older)}");
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net9.0")]
    [InlineData("net8.0")]
    [InlineData("net6.0")]
    [InlineData("net5.0")]
    [InlineData("netcoreapp3.1")]
    [InlineData("netcoreapp2.1")]
    [InlineData("netcoreapp1.0")]
    [InlineData("netstandard2.1")]
    [InlineData("netstandard2.0")]
    [InlineData("netstandard1.0")]
    [InlineData("net481")]
    [InlineData("net48")]
    [InlineData("net472")]
    [InlineData("net461")]
    [InlineData("net45")]
    public void GetTfmPriority_KnownTfms_ReturnPositive(string tfm)
    {
        Assert.True(TfmResolver.GetTfmPriority(tfm) > 0,
            $"Expected positive priority for {tfm}, got {TfmResolver.GetTfmPriority(tfm)}");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("randomstring")]
    public void GetTfmPriority_Unknown_ReturnsZero(string tfm)
    {
        Assert.Equal(0, TfmResolver.GetTfmPriority(tfm));
    }

    // --- IsTfmLike detection ---

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    [InlineData("net6.0")]
    [InlineData("net5.0")]
    [InlineData("netstandard2.0")]
    [InlineData("netstandard2.1")]
    [InlineData("netstandard1.0")]
    [InlineData("netcoreapp3.1")]
    [InlineData("netcoreapp2.1")]
    [InlineData("netcoreapp1.0")]
    [InlineData("net461")]
    [InlineData("net45")]
    [InlineData("net48")]
    [InlineData("net481")]
    public void IsTfmLike_ValidTfms_ReturnsTrue(string value)
    {
        Assert.True(TfmResolver.IsTfmLike(value), $"Expected '{value}' to be TFM-like");
    }

    [Theory]
    [InlineData("notaframework")]
    [InlineData("readme.txt")]
    [InlineData("lib")]
    [InlineData("tools")]
    [InlineData("any")]
    [InlineData("")]
    [InlineData("runtimes")]
    [InlineData("_rels")]
    public void IsTfmLike_InvalidValues_ReturnsFalse(string value)
    {
        Assert.False(TfmResolver.IsTfmLike(value), $"Expected '{value}' to NOT be TFM-like");
    }

    // --- ExtractTfmFromPath ---

    [Theory]
    [InlineData("lib/net8.0/Foo.dll", "net8.0")]
    [InlineData("lib/netstandard2.0/Bar.dll", "netstandard2.0")]
    [InlineData("lib/net6.0/Sub/Baz.dll", "net6.0")]
    [InlineData("tools/net9.0/any/tool.dll", "net9.0")]
    [InlineData("lib/net461/Legacy.dll", "net461")]
    [InlineData("lib/netcoreapp3.1/App.dll", "netcoreapp3.1")]
    [InlineData("ref/net8.0/Ref.dll", "net8.0")]
    public void ExtractTfmFromPath_ValidPaths(string path, string expectedTfm)
    {
        Assert.Equal(expectedTfm, TfmResolver.ExtractTfmFromPath(path));
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("")]
    [InlineData("content/file.txt")]
    [InlineData("[Content_Types].xml")]
    public void ExtractTfmFromPath_NoTfm_ReturnsNull(string path)
    {
        Assert.Null(TfmResolver.ExtractTfmFromPath(path));
    }

    // --- ResolvePackagePath with file-system fixtures ---

    [Fact]
    public void ResolvePackagePath_WithLibDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-tfm-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "lib", "net8.0"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "net8.0", "Test.dll"), [0]);

            string? result = TfmResolver.ResolvePackagePath(dir);
            Assert.NotNull(result);
            Assert.Contains("net8.0", result);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ResolvePackagePath_PicksHighestTfm()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-tfm-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "lib", "net6.0"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "net6.0", "Test.dll"), [0]);
            Directory.CreateDirectory(Path.Combine(dir, "lib", "net8.0"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "net8.0", "Test.dll"), [0]);
            Directory.CreateDirectory(Path.Combine(dir, "lib", "netstandard2.0"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "netstandard2.0", "Test.dll"), [0]);

            string? result = TfmResolver.ResolvePackagePath(dir);
            Assert.NotNull(result);
            Assert.Contains("net8.0", result);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ResolvePackagePath_PrefersNetOverNetstandard()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-tfm-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "lib", "netstandard2.1"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "netstandard2.1", "Test.dll"), [0]);
            Directory.CreateDirectory(Path.Combine(dir, "lib", "net6.0"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "net6.0", "Test.dll"), [0]);

            string? result = TfmResolver.ResolvePackagePath(dir);
            Assert.NotNull(result);
            Assert.Contains("net6.0", result);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ResolvePackagePath_EmptyLib_ReturnsNull()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-tfm-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "lib"));
            // No TFM subdirectories

            string? result = TfmResolver.ResolvePackagePath(dir);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetPackageDlls_ReturnsAllDlls()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-tfm-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "lib", "net8.0"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "net8.0", "Foo.dll"), [0]);
            File.WriteAllBytes(Path.Combine(dir, "lib", "net8.0", "Bar.dll"), [0]);

            var dlls = TfmResolver.GetPackageDlls(dir);
            Assert.Equal(2, dlls.Count);
            Assert.All(dlls, d => Assert.Equal("net8.0", d.Tfm));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetPackageDlls_MultipleTfms_ReturnsAll()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-tfm-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "lib", "net6.0"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "net6.0", "Foo.dll"), [0]);
            Directory.CreateDirectory(Path.Combine(dir, "lib", "net8.0"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "net8.0", "Foo.dll"), [0]);

            var dlls = TfmResolver.GetPackageDlls(dir);
            Assert.Equal(2, dlls.Count);
            Assert.Contains(dlls, d => d.Tfm == "net6.0");
            Assert.Contains(dlls, d => d.Tfm == "net8.0");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetPackageDlls_NoLib_ReturnsEmpty()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-tfm-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);

            var dlls = TfmResolver.GetPackageDlls(dir);
            Assert.Empty(dlls);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // --- Cross-compatibility: net vs netstandard vs netcoreapp priority chains ---

    [Theory]
    [InlineData("net8.0", "netcoreapp3.1")]
    [InlineData("net6.0", "netcoreapp3.1")]
    [InlineData("net5.0", "netcoreapp3.1")]
    [InlineData("netcoreapp3.1", "netstandard2.1")]
    [InlineData("net8.0", "netstandard2.1")]
    [InlineData("net6.0", "netstandard2.0")]
    [InlineData("net8.0", "net472")]
    [InlineData("netstandard2.0", "net461")]
    [InlineData("netstandard2.0", "net45")]
    public void GetTfmPriority_CrossFamily_HigherIsPreferred(string preferred, string fallback)
    {
        Assert.True(TfmResolver.GetTfmPriority(preferred) > TfmResolver.GetTfmPriority(fallback),
            $"Expected {preferred} > {fallback}");
    }

    // --- TFM family detection ---

    [Theory]
    [InlineData("net8.0", TfmFamily.NetModern)]
    [InlineData("net6.0", TfmFamily.NetModern)]
    [InlineData("net5.0", TfmFamily.NetModern)]
    [InlineData("net10.0", TfmFamily.NetModern)]
    [InlineData("netcoreapp3.1", TfmFamily.NetCore)]
    [InlineData("netcoreapp2.1", TfmFamily.NetCore)]
    [InlineData("netstandard2.0", TfmFamily.NetStandard)]
    [InlineData("netstandard1.0", TfmFamily.NetStandard)]
    [InlineData("net481", TfmFamily.NetFramework)]
    [InlineData("net45", TfmFamily.NetFramework)]
    [InlineData("net461", TfmFamily.NetFramework)]
    [InlineData("unknown", TfmFamily.Unknown)]
    public void GetTfmFamily_ReturnsCorrectFamily(string tfm, TfmFamily expected)
    {
        Assert.Equal(expected, TfmResolver.GetTfmFamily(tfm));
    }

    // --- Cross-family compatibility ---

    [Theory]
    [InlineData("netstandard2.0", "net8.0", true)]
    [InlineData("netstandard2.0", "net481", true)]
    [InlineData("netstandard2.0", "netcoreapp3.1", true)]
    [InlineData("net6.0", "net8.0", true)]
    [InlineData("netcoreapp3.1", "net8.0", true)]
    [InlineData("net481", "net8.0", false)]   // .NET Framework not compatible with modern .NET
    [InlineData("net8.0", "net481", false)]   // Modern .NET not compatible with .NET Framework
    [InlineData("net45", "net6.0", false)]    // .NET Framework not compatible with modern .NET
    [InlineData("net461", "netcoreapp3.1", false)]  // .NET Framework not compatible with .NET Core
    public void IsTfmCompatible_ReturnsCorrectResult(string candidate, string target, bool expected)
    {
        Assert.Equal(expected, TfmResolver.IsTfmCompatible(candidate, target));
    }

    [Fact]
    public void ResolvePackagePath_WithTargetTfm_RejectsIncompatibleFamily()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nf-tfm-{Guid.NewGuid():N}");
        try
        {
            // Package has both net481 and netstandard2.0
            Directory.CreateDirectory(Path.Combine(dir, "lib", "net481"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "net481", "Test.dll"), [0]);
            Directory.CreateDirectory(Path.Combine(dir, "lib", "netstandard2.0"));
            File.WriteAllBytes(Path.Combine(dir, "lib", "netstandard2.0", "Test.dll"), [0]);

            // Targeting net8.0 should pick netstandard2.0, NOT net481
            string? result = TfmResolver.ResolvePackagePath(dir, targetTfm: "net8.0");
            Assert.NotNull(result);
            Assert.Contains("netstandard2.0", result);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
