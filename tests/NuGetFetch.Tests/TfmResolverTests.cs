using System.Globalization;
using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

public class TfmResolverTests
{
    // --- Framework identity parsing is total and culture-independent ---

    /// <summary>
    /// The version text in a TFM is ASCII digits. A number parser is not that
    /// grammar: it takes the ambient culture's sign and separators, so under a
    /// culture whose negative sign is U+2212 an archive folder named
    /// <c>netstandard\u22121.0</c> parsed as version -1.0 and threw out of
    /// <see cref="Version"/>'s constructor — after the package it came from had
    /// been committed.
    /// </summary>
    [Theory]
    [InlineData("netstandard-1.0")]
    [InlineData("netstandard\u22121.0")]
    [InlineData("netstandard+1.0")]
    [InlineData("net-1.0")]
    [InlineData("net\u22121.0")]
    [InlineData("netcoreapp-1.0")]
    [InlineData("netstandard 1.0")]
    [InlineData("netstandard1 .0")]
    [InlineData("netstandard1. 0")]
    [InlineData("netstandard\u0661.\u0660")]
    [InlineData("netstandard１.０")]
    [InlineData("netstandard1_000.0")]
    [InlineData("netstandard.0")]
    [InlineData("netstandard1.")]
    [InlineData("netstandard0.0")]
    [InlineData("netstandard99999999999.0")]
    [InlineData("net9999999999.0")]
    [InlineData("net-45")]
    [InlineData("net\u221245")]
    [InlineData("net 45")]
    [InlineData("net4x")]
    [InlineData("net09")]
    [InlineData("net010")]
    [InlineData("net045")]
    [InlineData("net08.0")]
    [InlineData("net010.0")]
    public void TryGetFrameworkIdentity_RejectsEverythingOutsideTheDigitGrammar(
        string tfm)
    {
        foreach (CultureInfo culture in
            new[] { CultureInfo.InvariantCulture, new CultureInfo("sv-SE") })
        {
            using var scope = new CultureScope(culture);

            Assert.False(
                TfmResolver.TryGetFrameworkIdentity(tfm, out _),
                $"{tfm} under {culture.Name}");

            // Totality: the compatibility and ranking answers built on it must
            // not throw for the same input, in either direction.
            _ = TfmResolver.IsFrameworkCompatible(tfm, "net10.0");
            _ = TfmResolver.IsFrameworkCompatible("net10.0", tfm);
            _ = TfmResolver.GetFrameworkFallbackRank(tfm, "net10.0");
            _ = TfmResolver.GetFrameworkFallbackRank("net10.0", tfm);
        }
    }

    [Theory]
    [InlineData("net10.0", 10, 0, 0)]
    [InlineData("netstandard1.0", 1, 0, 0)]
    [InlineData("netcoreapp1.0", 1, 0, 0)]
    [InlineData("net45", 4, 5, 0)]
    [InlineData("net481", 4, 8, 1)]
    public void TryGetFrameworkIdentity_KeepsLegitimateMonikersUnderAnyCulture(
        string tfm,
        int major,
        int minor,
        int build)
    {
        foreach (CultureInfo culture in
            new[] { CultureInfo.InvariantCulture, new CultureInfo("sv-SE") })
        {
            using var scope = new CultureScope(culture);

            Assert.True(
                TfmResolver.TryGetFrameworkIdentity(
                    tfm,
                    out TfmResolver.FrameworkIdentity identity),
                $"{tfm} under {culture.Name}");
            Assert.Equal(new Version(major, minor, build), identity.Version);
        }
    }

    sealed class CultureScope : IDisposable
    {
        readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        internal CultureScope(CultureInfo culture) =>
            CultureInfo.CurrentCulture = culture;

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    // --- Version-aware framework compatibility ---

    /// <summary>
    /// .NET Standard is a contract several unrelated framework lines implement,
    /// so its acceptance is decided by the support matrix rather than by
    /// comparing two families' priority numbers as if they measured one age.
    /// </summary>
    [Theory]
    // .NET Framework implements .NET Standard up to the version it shipped with.
    [InlineData("netstandard2.0", "net472", true)]
    [InlineData("netstandard2.0", "net481", true)]
    [InlineData("netstandard2.0", "net461", true)]
    [InlineData("netstandard2.0", "net46", false)]
    [InlineData("netstandard1.3", "net46", true)]
    [InlineData("netstandard1.1", "net45", true)]
    [InlineData("netstandard1.3", "net45", false)]
    // .NET Core stops where its own release stopped.
    [InlineData("netstandard2.1", "netcoreapp1.0", false)]
    [InlineData("netstandard1.6", "netcoreapp1.0", true)]
    [InlineData("netstandard2.0", "netcoreapp2.0", true)]
    [InlineData("netstandard2.1", "netcoreapp2.0", false)]
    [InlineData("netstandard2.1", "netcoreapp3.0", true)]
    [InlineData("netstandard2.1", "net8.0", true)]
    // Within one lineage the candidate may not be newer than the target.
    [InlineData("net6.0", "net8.0", true)]
    [InlineData("net8.0", "net6.0", false)]
    [InlineData("netcoreapp3.1", "net8.0", true)]
    [InlineData("net472", "net481", true)]
    [InlineData("net481", "net472", false)]
    // Unrelated lineages never pair.
    [InlineData("net8.0", "net481", false)]
    [InlineData("net481", "net8.0", false)]
    [InlineData("net8.0", "netcoreapp3.1", false)]
    [InlineData("netstandard2.1", "netstandard2.0", false)]
    [InlineData("netstandard2.0", "netstandard2.1", true)]
    public void IsFrameworkCompatible_IsVersionAndFamilyAware(
        string candidate,
        string target,
        bool expected)
    {
        Assert.Equal(
            expected,
            TfmResolver.IsFrameworkCompatible(candidate, target));
    }

    [Theory]
    [InlineData("net10.0", TfmFamily.NetModern, 10, 0, 0)]
    [InlineData("net5.0", TfmFamily.NetModern, 5, 0, 0)]
    [InlineData("netcoreapp3.1", TfmFamily.NetCore, 3, 1, 0)]
    [InlineData("netstandard2.0", TfmFamily.NetStandard, 2, 0, 0)]
    [InlineData("net45", TfmFamily.NetFramework, 4, 5, 0)]
    [InlineData("net472", TfmFamily.NetFramework, 4, 7, 2)]
    [InlineData("net481", TfmFamily.NetFramework, 4, 8, 1)]
    public void TryGetFrameworkIdentity_ParsesRealMonikers(
        string tfm,
        TfmFamily family,
        int major,
        int minor,
        int build)
    {
        Assert.True(
            TfmResolver.TryGetFrameworkIdentity(
                tfm,
                out TfmResolver.FrameworkIdentity identity));
        Assert.Equal(family, identity.Family);
        Assert.Equal(new Version(major, minor, build), identity.Version);
    }

    [Theory]
    [InlineData("net10.0", TfmFamily.NetModern, 10, 0)]
    [InlineData("net10.0-browser", TfmFamily.NetModern, 10, 0)]
    [InlineData(
        "net10.0-windows10.0.19041.0",
        TfmFamily.NetModern,
        10,
        0)]
    [InlineData("netcoreapp3.1-linux", TfmFamily.NetCore, 3, 1)]
    public void TryGetBaseFrameworkIdentity_ParsesPlatformQualifiedMonikers(
        string tfm,
        TfmFamily family,
        int major,
        int minor)
    {
        Assert.True(
            TfmResolver.TryGetBaseFrameworkIdentity(
                tfm,
                out TfmResolver.FrameworkIdentity identity));
        Assert.Equal(family, identity.Family);
        Assert.Equal(new Version(major, minor, 0), identity.Version);
    }

    [Theory]
    [InlineData("-browser")]
    [InlineData("net10.0-")]
    public void TryGetBaseFrameworkIdentity_RejectsMissingSegments(string tfm)
    {
        Assert.False(TfmResolver.TryGetBaseFrameworkIdentity(tfm, out _));
    }

    [Theory]
    [InlineData("uap10.0")]
    [InlineData("netmf")]
    [InlineData("")]
    [InlineData("net")]
    [InlineData("notaframework")]
    public void TryGetFrameworkIdentity_RefusesToInventAVersion(string tfm)
    {
        Assert.False(TfmResolver.TryGetFrameworkIdentity(tfm, out _));
    }

    [Theory]
    [InlineData("net8.0", "net8.0", 2)]
    [InlineData("net6.0", "net8.0", 2)]
    [InlineData("netcoreapp3.1", "net8.0", 2)]
    [InlineData("netstandard2.0", "net8.0", 1)]
    [InlineData("netstandard2.0", "net472", 1)]
    [InlineData("netstandard2.0", "netstandard2.1", 2)]
    [InlineData("netstandard2.1", "netcoreapp1.0", 0)]
    [InlineData("net481", "net8.0", 0)]
    public void GetFrameworkFallbackRank_PrefersTheTargetsOwnLineage(
        string candidate,
        string target,
        int expected)
    {
        Assert.Equal(
            expected,
            TfmResolver.GetFrameworkFallbackRank(candidate, target));
    }

    /// <summary>
    /// An unmodelled moniker keeps the permissive family-level answer rather
    /// than being assigned a version this resolver would have to guess.
    /// </summary>
    [Fact]
    public void IsFrameworkCompatible_UnrecognizedMoniker_FallsBackToFamilyRule()
    {
        Assert.Equal(
            TfmResolver.IsTfmCompatible("netstandard2.0", "uap10.0"),
            TfmResolver.IsFrameworkCompatible("netstandard2.0", "uap10.0"));
        Assert.Equal(
            TfmResolver.IsTfmCompatible("uap10.0", "net8.0"),
            TfmResolver.IsFrameworkCompatible("uap10.0", "net8.0"));
    }

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

    [Theory]
    [InlineData("lib/net8.0/Foo.dll", "net8.0")]
    [InlineData("lib/uap10.0/Foo.dll", "uap10.0")]
    [InlineData(
        "ref/portable-net45+win8/Foo.dll",
        "portable-net45+win8")]
    [InlineData("tools\\net9.0\\any\\tool.dll", "net9.0")]
    public void ExtractFrameworkFolderFromPath_AssetPaths(
        string path,
        string expectedFramework)
    {
        Assert.Equal(
            expectedFramework,
            TfmResolver.ExtractFrameworkFolderFromPath(path));
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("")]
    [InlineData("content/uap10.0/Foo.dll")]
    [InlineData("lib/Foo.dll")]
    public void ExtractFrameworkFolderFromPath_NoFrameworkFolder_ReturnsNull(
        string path)
    {
        Assert.Null(
            TfmResolver.ExtractFrameworkFolderFromPath(path));
    }

    [Theory]
    [InlineData("lib/net8.0/Foo.dll", "lib/net8.0")]
    [InlineData(
        "runtimes\\win-x64\\lib\\net8.0\\Foo.dll",
        "runtimes/win-x64/lib/net8.0")]
    [InlineData("tools/net9.0/any/tool.dll", "tools/net9.0/any")]
    public void ExtractAssetDirectoryFromPath_AssetPaths(
        string path,
        string expectedDirectory)
    {
        Assert.Equal(
            expectedDirectory,
            TfmResolver.ExtractAssetDirectoryFromPath(path));
    }

    [Theory]
    [InlineData("Foo.dll")]
    [InlineData("")]
    public void ExtractAssetDirectoryFromPath_NoDirectory_ReturnsNull(
        string path)
    {
        Assert.Null(TfmResolver.ExtractAssetDirectoryFromPath(path));
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
