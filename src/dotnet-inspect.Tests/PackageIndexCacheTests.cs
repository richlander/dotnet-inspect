using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Packages;
using InertText;

namespace DotnetInspector.Tests;

public sealed class PackageIndexCacheTests
{
    public PackageIndexCacheTests()
        => CoreCache.Initialize("dotnet-inspect-test");

    [Fact]
    public void Description_RoundTripsAsContainedProse()
    {
        string packageName = $"Description.RoundTrip.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        var description = new InertString(
            TextPolicy.Prose,
            "first\r\n\tliteral \\u202E and live \u202E\nauthors: attacker\n");
        var result = new InspectionResult
        {
            PackageName = packageName,
            Version = Version,
            Description = description,
            Authors = "real author"
        };

        PackageIndexCache.Set(packageName, Version, result);
        InspectionResult cached = PackageIndexCache.TryGet(packageName, Version)!;

        Assert.Equal(description, cached.Description);
        Assert.Equal(
            "first\r\n\tliteral \\\\u202E and live \\u202E\nauthors: attacker\n",
            cached.Description?.ToString());
        Assert.Equal("real author", cached.Authors);
    }

    [Fact]
    public void Description_MalformedEnvelopeRejectsTheWholeCacheEntry()
    {
        AssertMalformedCacheMiss(
            "invalid-length",
            "description-bytes: nope\n\npackageName: Corrupt\nversion: 1.0.0\n"u8.ToArray());

        AssertMalformedCacheMiss(
            "invalid-utf8",
            [
                .. "description-bytes: 1\n"u8,
                0xFF,
                .. "\npackageName: Corrupt\nversion: 1.0.0\n"u8
            ]);

        byte[] rawBidi = Encoding.UTF8.GetBytes("\u202E");
        AssertMalformedCacheMiss(
            "invalid-encoded-text",
            [
                .. Encoding.UTF8.GetBytes($"description-bytes: {rawBidi.Length}\n"),
                .. rawBidi,
                .. "\npackageName: Corrupt\nversion: 1.0.0\n"u8
            ]);
    }

    [Fact]
    public void Description_TruncatedValueRequiresHigherProvenancePersistence()
    {
        string packageName = $"Description.Truncated.{Guid.NewGuid():N}";
        var result = new InspectionResult
        {
            PackageName = packageName,
            Version = "1.0.0",
            Description = new InertString(TextPolicy.Field, "a\u202Eb", maxLength: 3)
        };

        Assert.Throws<InvalidOperationException>(
            () => PackageIndexCache.Set(packageName, "1.0.0", result));
    }

    [Fact]
    public void Description_NullAndEmptyRemainDistinct()
    {
        string nullPackage = $"Description.Null.{Guid.NewGuid():N}";
        string emptyPackage = $"Description.Empty.{Guid.NewGuid():N}";
        const string Version = "1.0.0";

        PackageIndexCache.Set(
            nullPackage,
            Version,
            new InspectionResult
            {
                PackageName = nullPackage,
                Version = Version,
                Description = null
            });
        PackageIndexCache.Set(
            emptyPackage,
            Version,
            new InspectionResult
            {
                PackageName = emptyPackage,
                Version = Version,
                Description = InertString.Empty
            });

        Assert.Null(PackageIndexCache.TryGet(nullPackage, Version)?.Description);
        InertString? empty = PackageIndexCache.TryGet(emptyPackage, Version)?.Description;
        Assert.NotNull(empty);
        Assert.True(empty.Value.IsEmpty);
    }

    [Theory]
    [InlineData("[1.0.0,2.0.0)")]
    [InlineData("[3.0.0,)")]
    [InlineData("(,4.0.0]")]
    public void DependencyRange_RoundTripsWithoutSplitting(string versionRange)
    {
        var group = new DependencyGroup
        {
            TargetFramework = "net10.0",
            Dependencies =
            [
                new PackageDependency
                {
                    Id = "Dependency.With.Range",
                    Version = versionRange
                }
            ]
        };

        var serialized = PackageIndexCache.SerializeDependencyGroup(group);
        var cached = PackageIndexCache.DeserializeDependencyGroup(serialized);

        Assert.Equal("net10.0", cached.TargetFramework);
        var dependency = Assert.Single(cached.Dependencies);
        Assert.Equal("Dependency.With.Range", dependency.Id);
        Assert.Equal(versionRange, dependency.Version);
    }

    private static void AssertMalformedCacheMiss(string suffix, byte[] bytes)
    {
        string packageName = $"Description.Malformed.{suffix}.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        string key = $"{packageName.ToLowerInvariant()}@{Version}";
        CoreCache.SetBytes(PackageIndexCache.Category, key, bytes, extension: "md");

        Assert.Null(PackageIndexCache.TryGet(packageName, Version));
    }
}
