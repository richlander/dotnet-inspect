using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

public class PackageSourceTests
{
    [Fact]
    public void NuGetOrg_IsNuGetOrg()
    {
        var source = PackageSource.NuGetOrg;
        Assert.True(source.IsNuGetOrg);
        Assert.Equal("nuget.org", source.Name);
    }

    [Fact]
    public void IsNuGetOrg_FalseForCustomSource()
    {
        var source = new PackageSource("custom", "https://custom.feed/v3/index.json");
        Assert.False(source.IsNuGetOrg);
    }

    [Fact]
    public void GetFlatContainerUrl_NuGetOrg_ReturnsUrl()
    {
        var source = PackageSource.NuGetOrg;
        var url = source.GetFlatContainerUrl();
        Assert.NotNull(url);
        Assert.Contains("api.nuget.org", url);
        Assert.False(url.EndsWith('/'));
    }

    [Fact]
    public void GetFlatContainerUrl_CustomSource_ReturnsNull()
    {
        var source = new PackageSource("custom", "https://custom.feed/v3/index.json");
        Assert.Null(source.GetFlatContainerUrl());
    }

    [Fact]
    public void GetAuthHeader_NoCredential_ReturnsNull()
    {
        var source = PackageSource.NuGetOrg;
        Assert.Null(source.GetAuthHeader());
    }

    [Fact]
    public void GetAuthHeader_WithCredential_ReturnsBasicAuth()
    {
        var source = new PackageSource("test", "https://test.feed/v3/index.json",
            new PackageSourceCredential("user", "pass"));
        var header = source.GetAuthHeader();
        Assert.NotNull(header);
        Assert.Equal("Basic", header.Scheme);
    }

    [Fact]
    public void IsNuGetOrg_RejectsSpoofedUrl()
    {
        // Substring-based check would match this; host-based should reject it
        var source = new PackageSource("evil", "https://evil.com/api.nuget.org/v3/index.json");
        Assert.False(source.IsNuGetOrg);
    }

    [Fact]
    public void IsNuGetOrg_AcceptsSubdomains()
    {
        var source = new PackageSource("sub", "https://globalcdn.nuget.org/v3/index.json");
        Assert.True(source.IsNuGetOrg);
    }

    [Fact]
    public void PackageSourceCredential_ToString_MasksPassword()
    {
        var cred = new PackageSourceCredential("myuser", "supersecret");
        string str = cred.ToString();
        Assert.Contains("myuser", str);
        Assert.DoesNotContain("supersecret", str);
        Assert.Contains("***", str);
    }
}
