using DotnetInspector.Inspectors;
using DotnetInspector.Models;

namespace DotnetInspector.Tests;

public class PackageFileListerTests
{
    private static string CreateExtractDir(params string[] relativePaths)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        foreach (var rel in relativePaths)
        {
            var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, rel); // size = byte length of the path string
        }
        return root;
    }

    [Fact]
    public void ListAll_ExcludesZipAndRestorePlumbing()
    {
        var root = CreateExtractDir(
            "README.md",
            "LICENSE.md",
            "lib/net8.0/Foo.dll",
            "Foo.nuspec",
            "_rels/.rels",
            "[Content_Types].xml",
            "package/services/metadata/core-properties/abc.psmdcp",
            ".signature.p7s",
            ".nupkg.metadata",
            "foo.1.0.0.nupkg",
            "foo.1.0.0.nupkg.sha512");
        try
        {
            var files = PackageFileLister.ListAll(root);
            var paths = files.Select(f => f.Path).ToList();

            Assert.Equal(new[] { "LICENSE.md", "README.md", "lib/net8.0/Foo.dll" }.OrderBy(x => x, StringComparer.Ordinal),
                paths.OrderBy(x => x, StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListAll_ReportsUncompressedSizeAndSortsByPath()
    {
        var root = CreateExtractDir("b.txt", "a.txt", "lib/c.txt");
        try
        {
            var files = PackageFileLister.ListAll(root);

            Assert.Equal(new[] { "a.txt", "b.txt", "lib/c.txt" }, files.Select(f => f.Path).ToArray());
            Assert.All(files, f => Assert.Equal(f.Path.Length, f.Size));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static List<PackageFile> Sample() =>
    [
        new("README.md", 10),
        new("LICENSE.md", 20),
        new("lib/net8.0/Foo.dll", 30),
        new("lib/net8.0/Foo.xml", 40),
        new("lib/net6.0/Foo.dll", 50),
        new("docs/guide.md", 60),
    ];

    [Theory]
    [InlineData("/")]
    [InlineData(".")]
    [InlineData("./")]
    public void Filter_Root_ReturnsTopLevelFilesOnly(string pattern)
    {
        var result = PackageFileLister.Filter(Sample(), pattern);
        Assert.Equal(new[] { "README.md", "LICENSE.md" }.OrderBy(x => x, StringComparer.Ordinal),
            result.Select(f => f.Path).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Filter_NullOrEmpty_ReturnsEverything()
    {
        Assert.Equal(6, PackageFileLister.Filter(Sample(), null).Count);
        Assert.Equal(6, PackageFileLister.Filter(Sample(), "").Count);
    }

    [Fact]
    public void Filter_GlobMd_MatchesMarkdownAnywhere()
    {
        var result = PackageFileLister.Filter(Sample(), "*.md");
        Assert.Equal(new[] { "README.md", "LICENSE.md", "docs/guide.md" }.OrderBy(x => x, StringComparer.Ordinal),
            result.Select(f => f.Path).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Filter_DirectoryTrailingSlash_ReturnsImmediateChildren()
    {
        var result = PackageFileLister.Filter(Sample(), "lib/net8.0/");
        Assert.Equal(new[] { "lib/net8.0/Foo.dll", "lib/net8.0/Foo.xml" }.OrderBy(x => x, StringComparer.Ordinal),
            result.Select(f => f.Path).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Filter_ExactFile_ReturnsSingleRow()
    {
        var result = PackageFileLister.Filter(Sample(), "README.md");
        Assert.Single(result);
        Assert.Equal("README.md", result[0].Path);
    }

    [Fact]
    public void Filter_BareDirectory_TreatedAsDirectory()
    {
        var result = PackageFileLister.Filter(Sample(), "lib/net6.0");
        Assert.Single(result);
        Assert.Equal("lib/net6.0/Foo.dll", result[0].Path);
    }

    [Fact]
    public void ListAll_MarksDeclaredReadme_RegardlessOfName()
    {
        var root = CreateExtractDir("PACKAGE.md", "README.md", "lib/net8.0/Foo.dll");
        try
        {
            var files = PackageFileLister.ListAll(root, declaredReadme: "PACKAGE.md");
            var readmes = files.Where(f => f.IsReadme).Select(f => f.Path).ToList();
            Assert.Equal(new[] { "PACKAGE.md" }, readmes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListAll_MarksRootAgentsCaseInsensitively()
    {
        var root = CreateExtractDir("agents.md", "docs/AGENTS.md", "README.md");
        try
        {
            var files = PackageFileLister.ListAll(root);
            var agents = files.Where(f => f.IsAgents).Select(f => f.Path).ToList();

            Assert.Equal(["agents.md"], agents);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListAll_MarksDeclaredReadme_InSubdirectory()
    {
        var root = CreateExtractDir("docs/package-readme.md", "README.md");
        try
        {
            var files = PackageFileLister.ListAll(root, declaredReadme: "docs/package-readme.md");
            Assert.Single(files.Where(f => f.IsReadme), f => f.Path == "docs/package-readme.md");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListAll_NoDeclaredReadme_MarksNothing()
    {
        var root = CreateExtractDir("README.md", "lib/net8.0/Foo.dll");
        try
        {
            var files = PackageFileLister.ListAll(root);
            Assert.DoesNotContain(files, f => f.IsReadme);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Filter_AtReadme_ReturnsTheDeclaredReadmeOnly()
    {
        List<PackageFile> files =
        [
            new("README.md", 10, IsReadme: false),
            new("PACKAGE.md", 20, IsReadme: true),
            new("lib/net8.0/Foo.dll", 30),
        ];

        var result = PackageFileLister.Filter(files, "@readme");
        Assert.Single(result);
        Assert.Equal("PACKAGE.md", result[0].Path);
        Assert.True(result[0].IsReadme);
    }

    [Fact]
    public void Filter_AtReadme_IsCaseInsensitive()
    {
        List<PackageFile> files = [new("PACKAGE.md", 20, IsReadme: true)];
        Assert.Single(PackageFileLister.Filter(files, "@README"));
    }

    [Fact]
    public void Filter_AtAgents_ReturnsRootAgentsOnly()
    {
        List<PackageFile> files =
        [
            new("AGENTS.md", 10, IsAgents: true),
            new("docs/AGENTS.md", 20),
            new("README.md", 30),
        ];

        var result = PackageFileLister.Filter(files, "@agents");

        Assert.Single(result);
        Assert.Equal("AGENTS.md", result[0].Path);
        Assert.True(result[0].IsAgents);
    }
}
