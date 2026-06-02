using DotnetInspector.Services;

namespace DotnetInspector.Tests;

public class SourceLinkUrlsTests
{
    [Theory]
    [InlineData("https://raw.githubusercontent.com/dotnet/dotnet/4370ea16341331f045fa9b89cc46e03aed27195c/src/a.cs")]
    [InlineData("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/0123456789abcdefABCDEF0123456789abcdef01/Src/b.cs")]
    public void CommitPinnedUrls_AreImmutable(string url) =>
        Assert.True(SourceLinkUrls.IsImmutable(url));

    [Theory]
    [InlineData("https://raw.githubusercontent.com/dotnet/dotnet/main/src/a.cs")]            // branch, not sha
    [InlineData("https://raw.githubusercontent.com/dotnet/dotnet/v1.2.3/src/a.cs")]          // tag, not sha
    [InlineData("https://raw.githubusercontent.com/dotnet/dotnet/abc123/src/a.cs")]          // short sha
    [InlineData("https://example.com/dotnet/dotnet/4370ea16341331f045fa9b89cc46e03aed27195c/a.cs")] // other host
    [InlineData("http://raw.githubusercontent.com/dotnet/dotnet/4370ea16341331f045fa9b89cc46e03aed27195c/a.cs")] // http
    public void NonCommitPinnedUrls_AreMutable(string url) =>
        Assert.False(SourceLinkUrls.IsImmutable(url));
}
