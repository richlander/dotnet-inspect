using DotnetInspector.Services;

namespace DotnetInspector.Services.Tests;

public class RenderedUrlPreferenceTests
{
    const string Revision = "0cdbe500d11cb77ae7fb3c8612a5ba7bcc83ff86";
    const string SourcePath = "src/ILInspector.SourceLink/SourceLinkService.cs";

    [Theory]
    [InlineData("https://raw.githubusercontent.com/richlander/dotnet-inspect/" + Revision + "/" + SourcePath)]
    [InlineData("https://github.com/richlander/dotnet-inspect/raw/" + Revision + "/" + SourcePath)]
    public void SupportedGitHubSource_PrefersRenderedView(string url)
    {
        Assert.Equal(
            $"https://github.com/richlander/dotnet-inspect/blob/{Revision}/{SourcePath}",
            GitHubUrlResolver.ConvertRawToBlobUrl(url));
    }

    [Theory]
    [InlineData("#section")]
    [InlineData("#L12-L20")]
    [InlineData("#section%20two")]
    public void SupportedGitHubSource_PreservesAuthoredFragment(string fragment)
    {
        string[] sourceUrls =
        [
            $"https://raw.githubusercontent.com/richlander/dotnet-inspect/{Revision}/{SourcePath}",
            $"https://github.com/richlander/dotnet-inspect/raw/{Revision}/{SourcePath}",
        ];
        foreach (string url in sourceUrls)
        {
            Assert.Equal(
                $"https://github.com/richlander/dotnet-inspect/blob/{Revision}/{SourcePath}{fragment}",
                GitHubUrlResolver.ConvertRawToBlobUrl(url + fragment));
        }
    }

    [Fact]
    public void GitHubRawRoute_PreservesFilePathQueryAndFragment()
    {
        Assert.Equal(
            "https://github.com/richlander/dotnet-inspect/blob/main/docs/raw/example.md?plain=1#L12",
            GitHubUrlResolver.ConvertRawToBlobUrl(
                "https://github.com/richlander/dotnet-inspect/raw/main/docs/raw/example.md?plain=1#L12"));
    }

    [Theory]
    [InlineData("https://gitlab.com/owner/repository/-/raw/main/Source.cs")]
    [InlineData("https://source.example/owner/repository/raw/main/Source.cs")]
    [InlineData("https://dev.azure.com/org/project/_apis/git/repositories/repo/items?path=/raw/Source.cs")]
    [InlineData("https://github.com/richlander/dotnet-inspect/blob/main/docs/raw/example.md")]
    [InlineData("https://github.com/richlander/dotnet-inspect/issues/1?path=/raw/Source.cs")]
    [InlineData("https://raw.githubusercontent.com/richlander/dotnet-inspect/main/docs/raw/example.md")]
    [InlineData("https://raw.githubusercontent.com/richlander/dotnet-inspect/" + Revision + "/" + SourcePath + "?plain=1#section")]
    public void UnsupportedMapping_PreservesOriginalUrl(string url)
    {
        Assert.Equal(url, GitHubUrlResolver.ConvertRawToBlobUrl(url));
    }
}
