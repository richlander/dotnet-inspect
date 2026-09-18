using DotnetInspect.Cli.Sections;
using System.IO.Compression;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task PackageWithoutAllLibraries_RemainsPackageScoped()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "-S", "Package files",
                "--markdown", "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains("# Test.LibraryFiles", result.Output);
            Assert.Contains("## Package files", result.Output);
            Assert.DoesNotContain("## Libraries", result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_UsesSharedLibraryRendering()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var markdown = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--markdown", "--tips", "q");
            var tsv = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "References",
                "--tsv", "--rows", "1",
                "--tips", "q");
            var table = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "References",
                "--table", "--rows", "1",
                "--tips", "q");
            var jsonl = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "References",
                "--jsonl", "--rows", "1",
                "--tips", "q");
            var projected = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "References",
                "--tsv", "--columns", "Name",
                "--rows", "1",
                "--tips", "q");

            Assert.Equal(0, markdown.Exit);
            Assert.StartsWith(
                "# Test.LibraryFiles\n\n## Libraries\n",
                markdown.Output);
            Assert.Contains(
                "### lib/net10.0/Latest.One.dll (net10.0)",
                markdown.Output);
            Assert.Contains(
                "### lib/net10.0/Latest.Two.dll (net10.0)",
                markdown.Output);
            Assert.DoesNotContain("Older.dll", markdown.Output);
            Assert.Empty(markdown.Error);

            Assert.Equal(0, tsv.Exit);
            Assert.Empty(tsv.Error);
            Assert.Single(
                tsv.Output.ReplaceLineEndings("\n")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries),
                line => line.StartsWith(
                    "library\t",
                    StringComparison.Ordinal));
            Assert.Contains(
                "lib/net10.0/Latest.One.dll",
                tsv.Output);
            Assert.Contains(
                "lib/net10.0/Latest.Two.dll",
                tsv.Output);

            Assert.Equal(0, table.Exit);
            Assert.Empty(table.Error);
            Assert.StartsWith("library", table.Output);
            Assert.Contains(
                "lib/net10.0/Latest.One.dll",
                table.Output);
            Assert.Contains(
                "lib/net10.0/Latest.Two.dll",
                table.Output);

            Assert.Equal(0, jsonl.Exit);
            Assert.Empty(jsonl.Error);
            Assert.Contains(
                "\"library\":\"lib/net10.0/Latest.One.dll\"",
                jsonl.Output);
            Assert.Contains(
                "\"library\":\"lib/net10.0/Latest.Two.dll\"",
                jsonl.Output);

            Assert.Equal(0, projected.Exit);
            Assert.Empty(projected.Error);
            Assert.StartsWith("library\tname\n", projected.Output);
            Assert.Contains(
                "lib/net10.0/Latest.One.dll",
                projected.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--tree")]
    [InlineData("--dependencies")]
    public async Task PackageAllLibraries_RejectsExactLibraryOperations(
        string operation)
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "References",
                operation,
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "requires one exact Library",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_HonorsOutputDestination()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            string outputPath = Path.Combine(tempDir, "aggregate.md");
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--markdown",
                "--out", outputPath,
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Output);
            Assert.Empty(result.Error);
            Assert.Contains(
                "# Test.LibraryFiles",
                File.ReadAllText(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_AllTfmsRejectsPartialCompileSelection()
    {
        var (packagePath, tempDir) =
            CreatePackageWithHealthyAndEmptyReferenceGroups();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Library Info",
                "--markdown",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "declares an empty compile group for TFM 'net10.0'",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_AllTfmsRetainsNestedCompileLibraries()
    {
        var (packagePath, tempDir) =
            CreateLocalNestedMultiTfmLibraryPackage();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "--tfm", "all",
                "-S", "Library Info",
                "--markdown",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains(
                "lib/net8.0/Direct.dll (net8.0)",
                result.Output);
            Assert.Contains(
                "lib/net8.0/x64/Nested.dll (net8.0)",
                result.Output);
            Assert.Contains(
                "lib/net10.0/x64/Nested.dll (net10.0)",
                result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_DescriptorlessParticipantIsVisible()
    {
        var (packagePath, tempDir) =
            CreatePackageWithDescriptorlessParticipant();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--markdown",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Contains(
                "### lib/net8.0/Readable.dll (net8.0)",
                result.Output);
            Assert.Contains(
                "Could not select library descriptor for "
                + "'lib/net8.0/Text.dll'",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_CountRejectsIncompleteParticipants()
    {
        var (packagePath, tempDir) =
            CreatePackageWithDescriptorlessParticipant();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", "Library Info",
                "--count",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "Count output is unavailable because one or more "
                + "selected package Libraries could not be inspected.",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAllLibraries_IdentifierAuditPreservesHealthyResultsOnFailure()
    {
        var (packagePath, tempDir) =
            CreateIdentifierConfusionReferencePackage();
        try
        {
            string packageRoot = Path.Combine(tempDir, "content");
            string libraryDirectory =
                Path.Combine(packageRoot, "lib", "net8.0");
            WriteReferenceFixtureAssembly(
                Path.Combine(libraryDirectory, "A.Valid.dll"),
                "\u0405ystem.Valid");
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Bridge.dll"),
                "not a managed assembly");
            File.Delete(packagePath);
            ZipFile.CreateFromDirectory(packageRoot, packagePath);

            var result = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", SectionNames.IdentifierConfusion,
                "--tips", "q");
            var count = await RunAppAsync(
                "package", packagePath,
                "--all-libraries",
                "-S", SectionNames.IdentifierConfusion,
                "--count",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Contains("U+0405→S", result.Output);
            Assert.Contains(
                "Could not select library descriptor for "
                + "'lib/net8.0/Bridge.dll'",
                result.Error);
            Assert.Contains(
                "Warning: Identifier audit failed for "
                + "'lib/net8.0/Root.dll': invalid assembly metadata",
                result.Error);
            Assert.Equal(1, count.Exit);
            Assert.Empty(count.Output);
            Assert.Contains(
                "Identifier audit failed for 'lib/net8.0/Root.dll'",
                count.Error);
            Assert.Contains(
                "Count output is unavailable because one or more "
                + "selected package Libraries could not be inspected.",
                count.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static (string PackagePath, string TempDir)
        CreateLocalNestedMultiTfmLibraryPackage()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        foreach (string framework in new[] { "net8.0", "net10.0" })
        {
            string libDir =
                Path.Combine(packageRoot, "lib", framework);
            string nestedDir = Path.Combine(libDir, "x64");
            Directory.CreateDirectory(nestedDir);
            File.Copy(
                TestAssemblyPath,
                Path.Combine(nestedDir, "Nested.dll"));
            if (framework == "net8.0")
            {
                File.Copy(
                    TestAssemblyPath,
                    Path.Combine(libDir, "Direct.dll"));
            }
        }

        string packagePath =
            Path.Combine(tempDir, "Nested.Tfm.All.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir)
        CreatePackageWithHealthyAndEmptyReferenceGroups()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        string libDir = Path.Combine(packageRoot, "lib", "net8.0");
        Directory.CreateDirectory(libDir);
        File.Copy(
            TestAssemblyPath,
            Path.Combine(libDir, "Healthy.dll"));
        string refDir = Path.Combine(packageRoot, "ref", "net10.0");
        Directory.CreateDirectory(refDir);
        File.WriteAllText(Path.Combine(refDir, "_._"), "");

        string packagePath = Path.Combine(
            tempDir,
            "Partial.Compile.Selection.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir)
        CreatePackageWithDescriptorlessParticipant()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        string libDir = Path.Combine(packageRoot, "lib", "net8.0");
        Directory.CreateDirectory(libDir);
        File.Copy(
            TestAssemblyPath,
            Path.Combine(libDir, "Readable.dll"));
        File.WriteAllText(
            Path.Combine(libDir, "Text.dll"),
            "café",
            new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true));

        string packagePath = Path.Combine(
            tempDir,
            "Descriptorless.Participant.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }
}
