using DotnetInspector.Inspectors;
using DotnetInspector.Packages;
using InertText;

namespace DotnetInspector.Tests;

public sealed class PackageIndexCacheTests
{
    [Fact]
    public void Description_RoundTripsAsContainedProse()
    {
        var description = new InertString(
            TextPolicy.Prose,
            "first\nliteral \\u202E and live \u202E");

        string encoded = PackageIndexCache.SerializeDescription(description)!;
        InertString cached = PackageIndexCache.DeserializeDescription(encoded)!.Value;

        Assert.DoesNotContain('\n', encoded);
        Assert.Equal("first\\^Jliteral \\\\u202E and live \\u202E", encoded);
        Assert.Equal(description, cached);
        Assert.Equal("first\nliteral \\\\u202E and live \\u202E", cached.ToString());
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
}
