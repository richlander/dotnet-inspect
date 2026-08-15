using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Packages;
using InertText;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class PackageIndexCacheTests
{
    private const string ProducerKey = "producer-a";

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

        PackageIndexCache.Set(packageName, Version, ProducerKey, result);
        InspectionResult cached =
            PackageIndexCache.TryGet(packageName, Version, ProducerKey)!;

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
            () => PackageIndexCache.Set(
                packageName,
                "1.0.0",
                ProducerKey,
                result));
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
            ProducerKey,
            new InspectionResult
            {
                PackageName = nullPackage,
                Version = Version,
                Description = null
            });
        PackageIndexCache.Set(
            emptyPackage,
            Version,
            ProducerKey,
            new InspectionResult
            {
                PackageName = emptyPackage,
                Version = Version,
                Description = InertString.Empty
            });

        Assert.Null(
            PackageIndexCache.TryGet(
                nullPackage,
                Version,
                ProducerKey)?.Description);
        InertString? empty = PackageIndexCache.TryGet(
            emptyPackage,
            Version,
            ProducerKey)?.Description;
        Assert.NotNull(empty);
        Assert.True(empty.Value.IsEmpty);
    }

    [Fact]
    public void EqualCoordinatesFromDifferentProducersDoNotShareInspectionResults()
    {
        string packageName = $"Source.Scoped.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        PackageIndexCache.Set(
            packageName,
            Version,
            "producer-a",
            new InspectionResult
            {
                PackageName = packageName,
                Version = Version,
                Authors = "producer-a",
            });

        Assert.Null(
            PackageIndexCache.TryGet(
                packageName,
                Version,
                "producer-b"));
        Assert.Equal(
            "producer-a",
            PackageIndexCache.TryGet(
                packageName,
                Version,
                "producer-a")!.Authors);
    }

    [Fact]
    public void LegacyRidAvailabilityCacheIsIgnored()
    {
        string donorPackage = $"Legacy.Rid.Donor.{Guid.NewGuid():N}";
        string legacyPackage = $"Legacy.Rid.Target.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        PackageIndexCache.Set(
            donorPackage,
            version,
            ProducerKey,
            new InspectionResult
            {
                PackageName = donorPackage,
                Version = version,
                IsRidSpecificPointerPackage = true,
                RuntimeIdentifierPackages =
                [
                    new RidPackageReference
                    {
                        RuntimeIdentifier = "linux-x64",
                        PackageId = $"{donorPackage}.linux-x64",
                        Exists = false,
                    }
                ],
            });
        byte[] bytes = CoreCache.TryGetBytes(
            PackageIndexCache.Category,
            PackageIndexCache.CacheKey(
                donorPackage,
                version,
                ProducerKey),
            extension: "md")!;
        CoreCache.SetBytes(
            "pkg-index-v15",
            PackageIndexCache.CacheKey(
                legacyPackage,
                version,
                ProducerKey),
            bytes,
            extension: "md");

        Assert.Null(
            PackageIndexCache.TryGet(
                legacyPackage,
                version,
                ProducerKey));
    }

    [Fact]
    public void RidReferenceDelimiterCannotRestoreAvailability()
    {
        string packageName = $"Rid.Delimiter.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        const string rid = "linux|x64";
        const string ridPackage = "Bogus|yes";
        PackageIndexCache.Set(
            packageName,
            version,
            ProducerKey,
            new InspectionResult
            {
                PackageName = packageName,
                Version = version,
                IsRidSpecificPointerPackage = true,
                RuntimeIdentifierPackages =
                [
                    new RidPackageReference
                    {
                        RuntimeIdentifier = rid,
                        PackageId = ridPackage,
                        Exists = true,
                    }
                ],
            });

        RidPackageReference cached = Assert.Single(
            PackageIndexCache.TryGet(
                packageName,
                version,
                ProducerKey)!.RuntimeIdentifierPackages!);
        Assert.Equal(rid, cached.RuntimeIdentifier);
        Assert.Equal(ridPackage, cached.PackageId);
        Assert.Null(cached.Exists);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RidAvailabilityDoesNotPersistAcrossSourcePolicies(
        bool availability)
    {
        string packageName = $"Rid.Policy.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        PackageIndexCache.Set(
            packageName,
            version,
            ProducerKey,
            new InspectionResult
            {
                PackageName = packageName,
                Version = version,
                IsRidSpecificPointerPackage = true,
                RuntimeIdentifierPackages =
                [
                    new RidPackageReference
                    {
                        RuntimeIdentifier = "linux-x64",
                        PackageId = $"{packageName}.linux-x64",
                        Exists = availability,
                    }
                ],
            });

        Assert.Null(
            Assert.Single(
                PackageIndexCache.TryGet(
                    packageName,
                    version,
                    ProducerKey)!.RuntimeIdentifierPackages!).Exists);
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
        Assert.False(cached.IsImplicitManifestGroup);
    }

    [Fact]
    public void DependencyGroup_ImplicitManifestProvenanceRoundTrips()
    {
        var group = new DependencyGroup
        {
            TargetFramework = "",
            IsImplicitManifestGroup = true,
            Dependencies =
            [
                new PackageDependency
                {
                    Id = "Implicit.Dependency",
                    Version = "1.0.0"
                }
            ]
        };

        string serialized = PackageIndexCache.SerializeDependencyGroup(group);
        DependencyGroup cached =
            PackageIndexCache.DeserializeDependencyGroup(serialized);

        Assert.True(cached.IsImplicitManifestGroup);
        Assert.Equal("Implicit.Dependency", Assert.Single(cached.Dependencies).Id);
    }

    private static void AssertMalformedCacheMiss(string suffix, byte[] bytes)
    {
        string packageName = $"Description.Malformed.{suffix}.{Guid.NewGuid():N}";
        const string Version = "1.0.0";
        string key = PackageIndexCache.CacheKey(
            packageName,
            Version,
            ProducerKey);
        CoreCache.SetBytes(PackageIndexCache.Category, key, bytes, extension: "md");

        Assert.Null(
            PackageIndexCache.TryGet(
                packageName,
                Version,
                ProducerKey));
    }
}
