using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed class LocalPackageSourceIdentityTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"local-source-{Guid.NewGuid():N}");

    public LocalPackageSourceIdentityTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void RelativePathResolvesFromProvidedBase()
    {
        string baseDirectory = Path.Combine(_root, "config");

        LocalPackageSourceIdentity identity =
            LocalPackageSourceIdentity.Create(
                Path.Combine("..", "feed", "."),
                baseDirectory);

        Assert.Equal(
            Path.Combine(_root, "feed"),
            identity.CanonicalPath);
    }

    [Fact]
    public void PathAndFileUriSpellingsShareIdentity()
    {
        string path = Path.Combine(_root, "feed with spaces");
        string fileUri = new Uri(path).AbsoluteUri;

        LocalPackageSourceIdentity fromPath =
            LocalPackageSourceIdentity.Create(path, _root);
        LocalPackageSourceIdentity fromUri =
            LocalPackageSourceIdentity.Create(fileUri, _root);

        Assert.Equal(fromPath, fromUri);
        Assert.Equal(fromPath.GetHashCode(), fromUri.GetHashCode());
    }

    [Fact]
    public void AbsoluteIdentityRejectsRelativePathWithoutBase()
    {
        Assert.Throws<ArgumentException>(
            () => LocalPackageSourceIdentity.CreateAbsolute(
                Path.Combine("relative", "feed")));
    }

    [Fact]
    public void ResolutionBaseMustBeAbsolute()
    {
        Assert.Throws<ArgumentException>(
            () => LocalPackageSourceIdentity.Create(
                "feed",
                "relative-base"));
    }

    [Fact]
    public void DotSegmentsAndTrailingSeparatorsShareIdentity()
    {
        string direct = Path.Combine(_root, "feed");
        string indirect = Path.Combine(
            _root,
            "subdirectory",
            "..",
            "feed",
            ".");

        Assert.Equal(
            LocalPackageSourceIdentity.Create(direct, _root),
            LocalPackageSourceIdentity.Create(
                indirect + Path.DirectorySeparatorChar,
                _root));
    }

    [Fact]
    public void RootRetainsItsDirectorySeparator()
    {
        string root = Path.GetPathRoot(_root)!;

        LocalPackageSourceIdentity identity =
            LocalPackageSourceIdentity.Create(root, _root);

        Assert.Equal(root, identity.CanonicalPath);
    }

    [Fact]
    public void CaseComparisonUsesHostPathSemantics()
    {
        LocalPackageSourceIdentity upper =
            LocalPackageSourceIdentity.Create(
                Path.Combine(_root, "FeedA"),
                _root);
        LocalPackageSourceIdentity lower =
            LocalPackageSourceIdentity.Create(
                Path.Combine(_root, "feeda"),
                _root);

        Assert.Equal(OperatingSystem.IsWindows(), upper.Equals(lower));
    }

    [Fact]
    public void StableOrdinalIgnoreCaseFoldDoesNotBroadenIdentity()
    {
        Assert.Equal(
            LocalPackageSourceIdentity.FoldOrdinalIgnoreCase("feed"),
            LocalPackageSourceIdentity.FoldOrdinalIgnoreCase("FEED"));
        Assert.Equal(
            LocalPackageSourceIdentity.FoldOrdinalIgnoreCase("\u00e4"),
            LocalPackageSourceIdentity.FoldOrdinalIgnoreCase("\u00c4"));
        Assert.NotEqual(
            LocalPackageSourceIdentity.FoldOrdinalIgnoreCase("s"),
            LocalPackageSourceIdentity.FoldOrdinalIgnoreCase("\u017f"));
        Assert.NotEqual(
            LocalPackageSourceIdentity.FoldOrdinalIgnoreCase("\ud800"),
            LocalPackageSourceIdentity.FoldOrdinalIgnoreCase("\ufffd"));
    }

    [Fact]
    public void WindowsDrivePathAndFileUriShareIdentity()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows drive semantics require a Windows host.");

        string root = Path.GetPathRoot(_root)!;
        string path = Path.Combine(root, "feeds", "local");

        Assert.Equal(
            LocalPackageSourceIdentity.Create(path, _root),
            LocalPackageSourceIdentity.Create(
                new Uri(path).AbsoluteUri,
                _root));
    }

    [Fact]
    public void WindowsUncPathAndFileUriShareIdentity()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("UNC path semantics require a Windows host.");

        const string path = @"\\server\share\feeds\local";

        Assert.Equal(
            LocalPackageSourceIdentity.Create(path, _root),
            LocalPackageSourceIdentity.Create(
                new Uri(path).AbsoluteUri,
                _root));
    }

    [Fact]
    public void NonWindowsUncFileUriIsRejected()
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("The rejection is specific to non-Windows hosts.");

        Assert.Throws<ArgumentException>(
            () => LocalPackageSourceIdentity.Create(
                "file://server/share/feeds/local",
                _root));
    }

    [Fact]
    public void SymbolicLinkDoesNotCollapseToItsTarget()
    {
        string target = Path.Combine(_root, "target");
        string link = Path.Combine(_root, "link");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is
            IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic links are unavailable: {ex.GetType().Name}");
        }

        Assert.NotEqual(
            LocalPackageSourceIdentity.Create(target, _root),
            LocalPackageSourceIdentity.Create(link, _root));
    }

    [Theory]
    [InlineData("file:///tmp/feed?tenant=a")]
    [InlineData("file:///tmp/feed#fragment")]
    public void FileUriQueryOrFragmentIsRejected(string source)
    {
        Assert.Throws<ArgumentException>(
            () => LocalPackageSourceIdentity.Create(source, _root));
    }

    [Fact]
    public void NonFileUriIsNotLocal()
    {
        Assert.False(
            LocalPackageSourceIdentity.IsLocalSource(
                "https://feed.example/v3/index.json"));
        Assert.Throws<ArgumentException>(
            () => LocalPackageSourceIdentity.Create(
                "https://feed.example/v3/index.json",
                _root));
    }
}
