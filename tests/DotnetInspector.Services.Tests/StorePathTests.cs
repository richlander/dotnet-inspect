using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

public class StorePathTests
{
    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("safe/../../outside.dll")]
    [InlineData("/outside.dll")]
    [InlineData("C:/outside.dll")]
    [InlineData(@"C:\outside.dll")]
    [InlineData("//server/share/outside.dll")]
    [InlineData("safe//outside.dll")]
    [InlineData("safe/./outside.dll")]
    public void TryResolveUnderRoot_RejectsPathsThatAreNotContained(
        string key)
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-store-path-").FullName;
        try
        {
            Assert.False(
                StorePath.TryResolveUnderRoot(
                    root,
                    key,
                    out string? resolved));
            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryResolveUnderRoot_PreservesSafeRelativeIdentity()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-store-path-").FullName;
        try
        {
            Assert.True(
                StorePath.TryResolveUnderRoot(
                    root,
                    "lib/net9.0/Foo..dll",
                    out string? resolved));
            Assert.Equal(
                Path.Combine(root, "lib", "net9.0", "Foo..dll"),
                resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveUnderRoot_RejectionDoesNotEchoArtifactPath()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-store-path-").FullName;
        try
        {
            const string hostile = "../outside-\u001b.dll";

            var exception = Assert.Throws<ArgumentException>(
                () => StorePath.ResolveUnderRoot(root, hostile));

            Assert.DoesNotContain(hostile, exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
