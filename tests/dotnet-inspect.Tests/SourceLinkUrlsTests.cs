using DotnetInspector.Services;

namespace DotnetInspector.Tests;

public class SourceLinkUrlsTests
{
    private const string Sha = "4370ea16341331f045fa9b89cc46e03aed27195c";

    [Theory]
    [InlineData($"https://raw.githubusercontent.com/dotnet/dotnet/{Sha}/src/a.cs")]
    [InlineData("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/0123456789abcdefABCDEF0123456789abcdef01/Src/b.cs")]
    public void CommitPinnedGitHubUrls_AreImmutable(string url) =>
        Assert.True(SourceLinkUrls.IsImmutable(url));

    [Theory]
    [InlineData($"https://dev.azure.com/org/project/_apis/git/repositories/repo/items?api-version=1.0&versionType=commit&version={Sha}&path=/src/a.cs")]
    [InlineData($"https://account.visualstudio.com/project/_apis/git/repositories/repo/items?api-version=1.0&versionType=commit&version={Sha}&path=/src/a.cs")]
    [InlineData($"https://account.visualstudio.com/DefaultCollection/project/_apis/git/repositories/repo/items?api-version=1.0&versionType=commit&version={Sha}&path=/src/a.cs")]
    public void CommitPinnedAzureDevOpsUrls_AreImmutable(string url) =>
        Assert.True(SourceLinkUrls.IsImmutable(url));

    [Theory]
    [InlineData("https://raw.githubusercontent.com/dotnet/dotnet/main/src/a.cs")]
    [InlineData("https://raw.githubusercontent.com/dotnet/dotnet/v1.2.3/src/a.cs")]
    [InlineData("https://raw.githubusercontent.com/dotnet/dotnet/abc123/src/a.cs")]
    public void MovingOrAmbiguousGitHubRefs_AreMutable(string url) =>
        Assert.False(SourceLinkUrls.IsImmutable(url));

    [Theory]
    [InlineData($"https://dev.azure.com/org/project/_apis/git/repositories/repo/items?api-version=1.0&versionType=branch&version={Sha}&path=/src/a.cs")]
    [InlineData("https://dev.azure.com/org/project/_apis/git/repositories/repo/items?api-version=1.0&versionType=commit&version=abc123&path=/src/a.cs")]
    [InlineData($"https://dev.azure.com/org/project/_apis/git/repositories/repo/items?api-version=1.0&versionType=commit&version={Sha}&versionOptions=previousChange&path=/src/a.cs")]
    public void MovingOrAmbiguousAzureDevOpsSelectors_AreMutable(string url) =>
        Assert.False(SourceLinkUrls.IsImmutable(url));

    [Theory]
    [InlineData($"https://example.com/dotnet/dotnet/{Sha}/a.cs")]
    [InlineData($"http://raw.githubusercontent.com/dotnet/dotnet/{Sha}/a.cs")]
    public void CommitLikeUrlsOutsideKnownHttpsGrammars_AreMutable(string url) =>
        Assert.False(SourceLinkUrls.IsImmutable(url));
}
