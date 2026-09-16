

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Tests for sample URL resolution from relative paths.
/// </summary>
public class SampleUrlResolutionTests
{
    [Fact]
    public void ResolveSampleUrl_MarkoutStyle_SimpleTwoLevelUp()
    {
        // Markout pattern: source in src/MarkOut/, samples in samples/
        // From src/MarkOut/TreeNode.cs, relative path is ../../samples/Serialization/WriterUsage.cs
        var sourceUrl = "https://raw.githubusercontent.com/user/markout/abc123/src/MarkOut/TreeNode.cs";
        var relativePath = "../../samples/Serialization/WriterUsage.cs";
        
        var result = GitHubUrlResolver.ResolveSampleUrl(sourceUrl, relativePath);
        
        Assert.Equal("https://raw.githubusercontent.com/user/markout/abc123/samples/Serialization/WriterUsage.cs", result);
    }

    [Fact]
    public void ResolveSampleUrl_NewtonsoftStyle_ParentDirectoryReference()
    {
        // Newtonsoft pattern: source in Src/Newtonsoft.Json/Serialization/, samples in Src/Newtonsoft.Json.Tests/
        // The relative path references a parent directory name that exists in the current path
        var sourceUrl = "https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/abc123/Src/Newtonsoft.Json/Serialization/IContractResolver.cs";
        var relativePath = "../Src/Newtonsoft.Json.Tests/Documentation/SerializationTests.cs";
        
        var result = GitHubUrlResolver.ResolveSampleUrl(sourceUrl, relativePath);
        
        Assert.Equal("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/abc123/Src/Newtonsoft.Json.Tests/Documentation/SerializationTests.cs", result);
    }

    [Fact]
    public void ResolveSampleUrl_SingleDotDot_SiblingDirectory()
    {
        // Simple case: go up one level to sibling
        var sourceUrl = "https://raw.githubusercontent.com/user/repo/abc123/src/Lib/File.cs";
        var relativePath = "../Tests/TestFile.cs";
        
        var result = GitHubUrlResolver.ResolveSampleUrl(sourceUrl, relativePath);
        
        Assert.Equal("https://raw.githubusercontent.com/user/repo/abc123/src/Tests/TestFile.cs", result);
    }

    [Fact]
    public void ResolveSampleUrl_NoRelativePrefix_DirectPath()
    {
        // Edge case: path without ../ prefix (relative from same directory)
        var sourceUrl = "https://raw.githubusercontent.com/user/repo/abc123/src/Lib/File.cs";
        var relativePath = "Samples/Demo.cs";
        
        var result = GitHubUrlResolver.ResolveSampleUrl(sourceUrl, relativePath);
        
        Assert.Equal("https://raw.githubusercontent.com/user/repo/abc123/src/Lib/Samples/Demo.cs", result);
    }

    [Fact]
    public void ResolveSampleUrl_DeepNesting_MultipleLevelsUp()
    {
        // Deep nesting with multiple levels up
        var sourceUrl = "https://raw.githubusercontent.com/user/repo/abc123/src/Core/Internal/Helpers/Utils.cs";
        var relativePath = "../../../../samples/Demo.cs";
        
        var result = GitHubUrlResolver.ResolveSampleUrl(sourceUrl, relativePath);
        
        Assert.Equal("https://raw.githubusercontent.com/user/repo/abc123/samples/Demo.cs", result);
    }

    [Fact]
    public void ResolveSampleUrl_NormalizesEmptySegments()
    {
        // Path with empty segments (e.g., from double slashes)
        var sourceUrl = "https://raw.githubusercontent.com/user/repo/abc123/src/Lib/File.cs";
        var relativePath = "..//Tests//Demo.cs";
        
        var result = GitHubUrlResolver.ResolveSampleUrl(sourceUrl, relativePath);
        
        Assert.Equal("https://raw.githubusercontent.com/user/repo/abc123/src/Tests/Demo.cs", result);
    }

    [Fact]
    public void ResolveSampleUrl_SkipsDotSegments()
    {
        // Path with . segments
        var sourceUrl = "https://raw.githubusercontent.com/user/repo/abc123/src/Lib/File.cs";
        var relativePath = "./../Tests/./Demo.cs";
        
        var result = GitHubUrlResolver.ResolveSampleUrl(sourceUrl, relativePath);
        
        Assert.Equal("https://raw.githubusercontent.com/user/repo/abc123/src/Tests/Demo.cs", result);
    }

    [Fact]
    public void ResolveSampleUrl_InvalidUrl_ReturnsNull()
    {
        var result = GitHubUrlResolver.ResolveSampleUrl("not-a-valid-url", "../test.cs");
        
        Assert.Null(result);
    }

    [Fact]
    public void ResolveSampleUrl_GitHubRawFormat()
    {
        // Ensure we handle the github.com/raw format (alternative to raw.githubusercontent.com)
        var sourceUrl = "https://github.com/user/repo/raw/abc123/src/Lib/File.cs";
        var relativePath = "../../samples/Demo.cs";
        
        var result = GitHubUrlResolver.ResolveSampleUrl(sourceUrl, relativePath);
        
        Assert.Equal("https://github.com/user/repo/raw/abc123/samples/Demo.cs", result);
    }
}
