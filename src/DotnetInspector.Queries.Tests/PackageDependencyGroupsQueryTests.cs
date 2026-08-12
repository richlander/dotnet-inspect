using System.IO.Compression;
using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageDependencyGroupsQueryTests
{
    [Fact]
    public async Task ExecuteAsync_ProjectsDeclaredGroupsAndExactSelection()
    {
        InMemoryPackageContent content = Content(
            ("Example.Package.nuspec", Manifest(
                """
                <group targetFramework="net8.0">
                  <dependency id="First.Dependency" version="[1.0.0]" />
                </group>
                <group targetFramework=".NETStandard2.0">
                  <dependency id="Second.Dependency" version="2.*" />
                </group>
                """)));

        PackageDependencyGroups result = Available(
            await ExecuteAsync(
                content,
                "Example.Package",
                "netstandard2.0"));

        Assert.Equal(PackageDependencyGroupSelectionStatus.Selected, result.SelectionStatus);
        Assert.Equal("netstandard2.0", result.RequestedTargetFramework);
        Assert.Equal(".NETStandard2.0", result.SelectedTargetFramework);
        Assert.Equal(1, result.SelectedGroupIndex);
        Assert.Equal(
            ["net8.0", ".NETStandard2.0"],
            result.Groups.Select(group => group.TargetFramework));
        DeclaredPackageDependency dependency = Assert.Single(result.Groups[1].Dependencies);
        Assert.Equal("Second.Dependency", dependency.Id);
        Assert.Equal("2.*", dependency.VersionRange);
    }

    [Fact]
    public async Task ExecuteAsync_SelectsUngroupedDependenciesForAnyFramework()
    {
        InMemoryPackageContent content = Content(
            ("Example.Package.nuspec", Manifest(
                """
                <dependency id="Universal.Dependency" version="1.0.0" />
                """)));

        PackageDependencyGroups result = Available(
            await ExecuteAsync(
                content,
                "Example.Package",
                "net9.0"));

        Assert.Equal(PackageDependencyGroupSelectionStatus.Selected, result.SelectionStatus);
        Assert.Equal("any", result.SelectedTargetFramework);
        DeclaredPackageDependencyGroup group = Assert.Single(result.Groups);
        Assert.Equal("Universal.Dependency", Assert.Single(group.Dependencies).Id);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesImplicitGroupDeclarationPosition()
    {
        InMemoryPackageContent content = Content(
            ("Example.Package.nuspec", Manifest(
                """
                <dependency id="Universal.Dependency" version="1.0.0" />
                <group targetFramework="net8.0">
                  <dependency id="Framework.Dependency" version="2.0.0" />
                </group>
                """)));

        PackageDependencyGroups result = Available(
            await ExecuteAsync(
                content,
                "Example.Package",
                "net8.0"));

        Assert.Equal(
            ["any", "net8.0"],
            result.Groups.Select(group => group.TargetFramework));
        Assert.Equal(
            "Universal.Dependency",
            Assert.Single(result.Groups[0].Dependencies).Id);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesInterleavedImplicitDependencyRuns()
    {
        InMemoryPackageContent content = Content(
            ("Example.Package.nuspec", Manifest(
                """
                <dependency id="Before" version="1.0.0" />
                <group targetFramework="any">
                  <dependency id="Middle" version="2.0.0" />
                </group>
                <dependency id="After" version="3.0.0" />
                """)));

        PackageDependencyGroups result = Available(
            await ExecuteAsync(
                content,
                "Example.Package",
                "net8.0"));

        Assert.Equal(
            ["Before", "Middle", "After"],
            result.Groups.SelectMany(group =>
                group.Dependencies.Select(dependency => dependency.Id)));
        Assert.Equal(3, result.Groups.Length);
        Assert.Equal(0, result.SelectedGroupIndex);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesGroupsWhenExactFrameworkIsAbsent()
    {
        InMemoryPackageContent content = Content(
            ("Example.Package.nuspec", Manifest(
                """
                <group targetFramework="net8.0">
                  <dependency id="Dependency" version="1.0.0" />
                </group>
                """)));

        PackageDependencyGroups result = Available(
            await ExecuteAsync(
                content,
                "Example.Package",
                "net9.0"));

        Assert.Equal(
            PackageDependencyGroupSelectionStatus.NoMatchingTargetFramework,
            result.SelectionStatus);
        Assert.Equal("net9.0", result.RequestedTargetFramework);
        Assert.Null(result.SelectedTargetFramework);
        Assert.Null(result.SelectedGroupIndex);
        Assert.Single(result.Groups);
    }

    [Fact]
    public async Task ExecuteAsync_DistinguishesMissingManifestFromNoDependencies()
    {
        PackageDependencyGroupsResult missing =
            await ExecuteAsync(
                Content(("content/readme.txt", "hello")),
                "Example.Package",
                "net8.0");
        PackageDependencyGroups empty = Available(
            await ExecuteAsync(
                Content(("Example.Package.nuspec", Manifest(""))),
                "Example.Package",
                "net8.0"));

        Assert.IsType<PackageDependencyGroupsResult.NoManifest>(missing);
        Assert.Empty(empty.Groups);
        Assert.Equal(
            PackageDependencyGroupSelectionStatus.NoDependencyGroups,
            empty.SelectionStatus);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAmbiguousAndUnsafeManifestPaths()
    {
        PackageDependencyGroupsResult ambiguous =
            await ExecuteAsync(
                Content(
                    ("A.nuspec", Manifest("")),
                    ("B.nuspec", Manifest(""))),
                "Example.Package");
        PackageDependencyGroupsResult backslash =
            await ExecuteAsync(
                Content(
                    ("nested\\Example.Package.nuspec", Manifest(""))),
                "Example.Package");
        PackageDependencyGroupsResult volumeQualified =
            await ExecuteAsync(
                Content(
                    ("C:Example.Package.nuspec", Manifest(""))),
                "Example.Package");
        PackageDependencyGroupsResult traversal =
            await ExecuteAsync(
                Content(
                    ("nested/../Example.Package.nuspec", Manifest(""))),
                "Example.Package");
        PackageDependencyGroupsResult currentDirectory =
            await ExecuteAsync(
                Content(
                    ("./Example.Package.nuspec", Manifest(""))),
                "Example.Package");

        Assert.IsType<InvalidDataException>(Failed(ambiguous));
        Assert.IsType<InvalidDataException>(Failed(backslash));
        Assert.IsType<InvalidDataException>(Failed(volumeQualified));
        Assert.IsType<InvalidDataException>(Failed(traversal));
        Assert.IsType<InvalidDataException>(Failed(currentDirectory));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMismatchedManifestIdentity()
    {
        InMemoryPackageContent content = Content(
            ("Example.Package.nuspec", Manifest("", "Different.Package")));

        Exception error = Failed(
            await ExecuteAsync(
                content,
                "Example.Package"));

        Assert.IsType<InvalidDataException>(error);
        Assert.DoesNotContain("Different.Package", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ValidatesNormalizedManifestVersion()
    {
        PackageDependencyGroupsResult normalized =
            await ExecuteAsync(
                Content((
                    "Example.Package.nuspec",
                    Manifest("", version: "1.0"))),
                "Example.Package");
        Exception mismatch = Failed(
            await ExecuteAsync(
                Content((
                    "Example.Package.nuspec",
                    Manifest("", version: "2.0.0"))),
                "Example.Package"));

        Assert.IsType<PackageDependencyGroupsResult.Available>(normalized);
        Assert.IsType<InvalidDataException>(mismatch);
        Assert.DoesNotContain("2.0.0", mismatch.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsDtdWithoutQuotingArtifactText()
    {
        const string manifest = """
            <!DOCTYPE package [
              <!ENTITY payload "SHOULD-NOT-REACH-THE-DIAGNOSTIC">
            ]>
            <package>
              <metadata>
                <id>Example.Package</id>
                <version>1.0.0</version>
                <description>&payload;</description>
              </metadata>
            </package>
            """;

        Exception error = Failed(
            await ExecuteAsync(
                Content(("Example.Package.nuspec", manifest)),
                "Example.Package"));

        Assert.IsType<NuspecParseException>(error);
        Assert.DoesNotContain(
            "SHOULD-NOT-REACH-THE-DIAGNOSTIC",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManifestBounds_AreEnforcedForEveryPackageStore(
        bool fileSystem)
    {
        string exact = PadToManifestByteLimit(Manifest(""));
        string oversized = exact + " ";
        string? root = null;
        try
        {
            IPackageContent exactContent;
            IPackageContent oversizedContent;
            if (fileSystem)
            {
                root = Path.Combine(
                    Path.GetTempPath(),
                    $"dependency-query-{Guid.NewGuid():N}");
                string exactRoot = Path.Combine(root, "exact");
                string oversizedRoot = Path.Combine(root, "oversized");
                Directory.CreateDirectory(exactRoot);
                Directory.CreateDirectory(oversizedRoot);
                File.WriteAllText(
                    Path.Combine(exactRoot, "Example.Package.nuspec"),
                    exact,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(
                    Path.Combine(oversizedRoot, "Example.Package.nuspec"),
                    oversized,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                exactContent = new FileSystemPackageContent(
                    exactRoot,
                    nupkgPath: null,
                    fromCache: false,
                    producerKey: "query-tests");
                oversizedContent = new FileSystemPackageContent(
                    oversizedRoot,
                    nupkgPath: null,
                    fromCache: false,
                    producerKey: "query-tests");
            }
            else
            {
                exactContent = Content(("Example.Package.nuspec", exact));
                oversizedContent = Content(("Example.Package.nuspec", oversized));
            }

            Assert.IsType<PackageDependencyGroupsResult.Available>(
                await ExecuteAsync(
                    exactContent,
                    "Example.Package"));
            Assert.IsType<InvalidDataException>(
                Failed(
                    await ExecuteAsync(
                        oversizedContent,
                        "Example.Package")));
        }
        finally
        {
            if (root is not null)
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_EnforcesDecodedCharacterLimit()
    {
        string manifest = PadToCharacterCount(
            Manifest(""),
            PackageDependencyGroupsQuery.MaxManifestCharacters + 1);

        Exception error = Failed(
            await ExecuteAsync(
                Content(("Example.Package.nuspec", manifest)),
                "Example.Package"));

        Assert.IsType<NuspecParseException>(error);
    }

    static PackageDependencyGroups Available(PackageDependencyGroupsResult result) =>
        Assert.IsType<PackageDependencyGroupsResult.Available>(result).Value;

    static Task<PackageDependencyGroupsResult> ExecuteAsync(
        IPackageContent content,
        string packageId,
        string? requestedTargetFramework = null,
        string packageVersion = "1.0.0")
        => PackageDependencyGroupsQuery.ExecuteAsync(
            content,
            packageId,
            packageVersion,
            requestedTargetFramework,
            TestContext.Current.CancellationToken);

    static Exception Failed(PackageDependencyGroupsResult result) =>
        Assert.IsType<PackageDependencyGroupsResult.Failed>(result).Error;

    static string Manifest(
        string dependencies,
        string packageId = "Example.Package",
        string version = "1.0.0")
        => $"""
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{packageId}</id>
                <version>{version}</version>
                <dependencies>
                  {dependencies}
                </dependencies>
              </metadata>
            </package>
            """;

    static string PadToManifestByteLimit(string manifest)
    {
        int bytes = Encoding.UTF8.GetByteCount(manifest);
        int remaining = PackageDependencyGroupsQuery.MaxManifestBytes - bytes;
        Assert.True(remaining >= 7);
        int multibyteCharacters = (remaining - 7) / 3;
        int singleBytePadding = remaining - 7 - (multibyteCharacters * 3);
        string padded = manifest
            + "<!--"
            + new string('漢', multibyteCharacters)
            + new string(' ', singleBytePadding)
            + "-->";
        Assert.Equal(
            PackageDependencyGroupsQuery.MaxManifestBytes,
            Encoding.UTF8.GetByteCount(padded));
        return padded;
    }

    static string PadToCharacterCount(string manifest, int characters)
    {
        int remaining = characters - manifest.Length;
        Assert.True(remaining >= 7);
        return manifest + "<!--" + new string('a', remaining - 7) + "-->";
    }

    static InMemoryPackageContent Content(
        params (string Path, string Text)[] entries)
    {
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(
            package,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, string text) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                entry.Write(bytes);
            }
        }

        return new InMemoryPackageContent(
            package.ToArray(),
            fromCache: false,
            producerKey: "dependency-query-tests");
    }
}
