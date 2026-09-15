using DotnetInspector.Platforms;

namespace DotnetInspector.Platforms.Tests;

public sealed class PlatformTargetCurrencyTests
{
    [Fact]
    public void FamilySetIsClosedAndDeterministicallyOrdered()
    {
        Assert.Equal(
            [PlatformFamily.DotNetRuntime, PlatformFamily.AspNetCore],
            Enum.GetValues<PlatformFamily>());
        Assert.True(
            Comparer<PlatformFamily>.Default.Compare(
                PlatformFamily.DotNetRuntime,
                PlatformFamily.AspNetCore) < 0);
    }

    [Theory]
    [InlineData("netcoreapp1.0", PlatformTargetFrameworkForm.NetCoreApp, 1, 0)]
    [InlineData("netcoreapp1.1", PlatformTargetFrameworkForm.NetCoreApp, 1, 1)]
    [InlineData("netcoreapp2.0", PlatformTargetFrameworkForm.NetCoreApp, 2, 0)]
    [InlineData("netcoreapp2.1", PlatformTargetFrameworkForm.NetCoreApp, 2, 1)]
    [InlineData("netcoreapp2.2", PlatformTargetFrameworkForm.NetCoreApp, 2, 2)]
    [InlineData("netcoreapp3.0", PlatformTargetFrameworkForm.NetCoreApp, 3, 0)]
    [InlineData("netcoreapp3.1", PlatformTargetFrameworkForm.NetCoreApp, 3, 1)]
    [InlineData("net5.0", PlatformTargetFrameworkForm.Net, 5, 0)]
    [InlineData("net11.0", PlatformTargetFrameworkForm.Net, 11, 0)]
    [InlineData("net11.2", PlatformTargetFrameworkForm.Net, 11, 2)]
    public void CanonicalFrameworksRoundTrip(
        string text,
        PlatformTargetFrameworkForm form,
        int major,
        int minor)
    {
        Assert.True(PlatformTargetFramework.TryParse(text, out var framework));
        Assert.NotNull(framework);
        Assert.Equal(form, framework.Form);
        Assert.Equal(major, framework.Major);
        Assert.Equal(minor, framework.Minor);
        Assert.Equal(text, framework.ToString());
        Assert.Equal(framework, PlatformTargetFramework.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" net11.0")]
    [InlineData("net11.0 ")]
    [InlineData("NET11.0")]
    [InlineData("net11")]
    [InlineData("net11.0.0")]
    [InlineData("net011.0")]
    [InlineData("net11.00")]
    [InlineData("net4.8")]
    [InlineData("netstandard2.0")]
    [InlineData("netcoreapp1.2")]
    [InlineData("netcoreapp2.3")]
    [InlineData("netcoreapp3.2")]
    [InlineData("netcoreapp4.0")]
    [InlineData("net11.0-windows")]
    [InlineData("net11.*")]
    [InlineData("net11.0..net12.0")]
    [InlineData("net１１.0")]
    public void NonCanonicalFrameworksAreRejected(string text)
    {
        Assert.False(PlatformTargetFramework.TryParse(text, out _));
        Assert.Throws<FormatException>(() => PlatformTargetFramework.Parse(text));
    }

    [Theory]
    [InlineData("0.0.0", "0", "0", "0", null, null)]
    [InlineData("11.0.0", "11", "0", "0", null, null)]
    [InlineData("11.0.0-preview.7.26381.103", "11", "0", "0", "preview.7.26381.103", null)]
    [InlineData("11.0.0-rc.1+servicing.2", "11", "0", "0", "rc.1", "servicing.2")]
    [InlineData("11.0.0+build.001", "11", "0", "0", null, "build.001")]
    public void ExactVersionsRoundTrip(
        string text,
        string major,
        string minor,
        string patch,
        string? prerelease,
        string? buildMetadata)
    {
        Assert.True(PlatformVersion.TryParse(text, out var version));
        Assert.NotNull(version);
        Assert.Equal(major, version.Major.ToString());
        Assert.Equal(minor, version.Minor.ToString());
        Assert.Equal(patch, version.Patch.ToString());
        Assert.Equal(prerelease, version.Prerelease);
        Assert.Equal(buildMetadata, version.BuildMetadata);
        Assert.Equal(prerelease is not null, version.IsPrerelease);
        Assert.Equal(text, version.Value);
        Assert.Equal(text, version.ToString());
        Assert.Equal(version, PlatformVersion.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" 11.0.0")]
    [InlineData("11.0.0 ")]
    [InlineData("v11.0.0")]
    [InlineData("11")]
    [InlineData("11.0")]
    [InlineData("11.0.0.0")]
    [InlineData("011.0.0")]
    [InlineData("11.00.0")]
    [InlineData("11.0.00")]
    [InlineData("11.0.0-")]
    [InlineData("11.0.0-alpha..1")]
    [InlineData("11.0.0-01")]
    [InlineData("11.0.0-alpha_1")]
    [InlineData("11.0.0+")]
    [InlineData("11.0.0+build..1")]
    [InlineData("11.0.0+build+1")]
    [InlineData("11.*")]
    [InlineData("[11.0.0]")]
    [InlineData("latest")]
    [InlineData("１１.0.0")]
    [InlineData("11.0.0-alphaβ")]
    public void NonExactVersionsAreRejected(string text)
    {
        Assert.False(PlatformVersion.TryParse(text, out _));
        Assert.Throws<FormatException>(() => PlatformVersion.Parse(text));
    }

    [Fact]
    public void SemanticPrecedenceFollowsSemVer()
    {
        string[] ordered =
        [
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-alpha.beta",
            "1.0.0-beta",
            "1.0.0-beta.2",
            "1.0.0-beta.11",
            "1.0.0-rc.1",
            "1.0.0",
        ];

        PlatformVersion[] versions = ordered
            .Select(PlatformVersion.Parse)
            .ToArray();
        Assert.Equal(
            versions,
            versions
                .Reverse()
                .Order(PlatformVersion.SemanticPrecedenceComparer));
        for (int i = 0; i < versions.Length - 1; i++)
            Assert.True(versions[i].ComparePrecedenceTo(versions[i + 1]) < 0);
    }

    [Fact]
    public void BuildMetadataChangesIdentityButNotPrecedence()
    {
        var first = PlatformVersion.Parse("11.0.0+servicing.1");
        var second = PlatformVersion.Parse("11.0.0+servicing.2");

        Assert.NotEqual(first, second);
        Assert.Equal(0, first.ComparePrecedenceTo(second));
        Assert.Equal(
            0,
            PlatformVersion.SemanticPrecedenceComparer.Compare(first, second));
    }

    [Fact]
    public void LargeNumericPrereleaseIdentifiersCompareWithoutOverflow()
    {
        var lower = PlatformVersion.Parse(
            "11.0.0-99999999999999999999999999999999999999");
        var higher = PlatformVersion.Parse(
            "11.0.0-100000000000000000000000000000000000000");

        Assert.True(lower.ComparePrecedenceTo(higher) < 0);
    }

    [Theory]
    [InlineData(
        "99999999999999999999999999999999999999.0.0",
        "100000000000000000000000000000000000000.0.0")]
    [InlineData(
        "11.99999999999999999999999999999999999999.0",
        "11.100000000000000000000000000000000000000.0")]
    [InlineData(
        "11.0.99999999999999999999999999999999999999",
        "11.0.100000000000000000000000000000000000000")]
    public void LargeCoreComponentsCompareWithoutOverflow(
        string lowerText,
        string higherText)
    {
        var lower = PlatformVersion.Parse(lowerText);
        var higher = PlatformVersion.Parse(higherText);

        Assert.True(lower.ComparePrecedenceTo(higher) < 0);
        Assert.True(higher.ComparePrecedenceTo(lower) > 0);
        Assert.True(
            PlatformVersion.SemanticPrecedenceComparer.Compare(lower, higher) < 0);
    }

    [Fact]
    public void FamilyTargetsRetainAllIdentityDimensions()
    {
        var framework = PlatformTargetFramework.Parse("net11.0");
        var version = PlatformVersion.Parse("11.0.0-preview.1+build.7");
        var runtime = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            framework,
            version);
        var equal = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0-preview.1+build.7"));
        var aspnet = new PlatformFamilyTarget(
            PlatformFamily.AspNetCore,
            framework,
            version);
        var differentBuild = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            framework,
            PlatformVersion.Parse("11.0.0-preview.1+build.8"));

        Assert.Equal(runtime, equal);
        Assert.NotEqual(runtime, aspnet);
        Assert.NotEqual(runtime, differentBuild);
        Assert.Equal(
            "DotNetRuntime/net11.0/11.0.0-preview.1+build.7",
            runtime.ToString());
    }

    [Fact]
    public void FamilyTargetRejectsMismatchedReleaseBands()
    {
        var framework = PlatformTargetFramework.Parse("net11.0");

        Assert.Throws<ArgumentException>(() => new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("11.0.0")));
        Assert.Throws<ArgumentException>(() => new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            framework,
            PlatformVersion.Parse("11.1.0")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlatformFamilyTarget(
            (PlatformFamily)42,
            framework,
            PlatformVersion.Parse("11.0.0")));
    }

    [Fact]
    public void NullInputsAreRejected()
    {
        Assert.False(PlatformTargetFramework.TryParse(null, out _));
        Assert.False(PlatformVersion.TryParse(null, out _));
        Assert.Throws<ArgumentNullException>(() => PlatformTargetFramework.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => PlatformVersion.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            null!,
            PlatformVersion.Parse("11.0.0")));
        Assert.Throws<ArgumentNullException>(() => new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            null!));
    }
}
