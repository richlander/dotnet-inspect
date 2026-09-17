using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task LibraryCommand_PackageDefaultsToSelectedTfmAggregate()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "library", packagePath,
                "-S", "Library Info",
                "--markdown", "--tips", "q");

            Assert.True(
                result.Exit == 0,
                $"Exit {result.Exit}: {result.Error}");
            Assert.Contains("# Latest.One.dll", result.Output);
            Assert.Contains("# Latest.Two.dll", result.Output);
            Assert.DoesNotContain("# Older.dll", result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_UnreadableAggregateParticipantIsVisible()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        try
        {
            string libDir = Path.Combine(
                tempDir,
                "content",
                "lib",
                "net8.0");
            Directory.CreateDirectory(libDir);
            File.Copy(
                TestAssemblyPath,
                Path.Combine(libDir, "Readable.dll"));
            File.WriteAllBytes(
                Path.Combine(libDir, "Invalid.dll"),
                [1, 2, 3]);
            string packagePath =
                Path.Combine(tempDir, "Unreadable.Sample.1.0.0.nupkg");
            System.IO.Compression.ZipFile.CreateFromDirectory(
                Path.Combine(tempDir, "content"),
                packagePath);

            var result = await RunAppAsync(
                "library", packagePath,
                "-S", "Library Info",
                "--markdown", "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Contains(
                "# lib/net8.0/Readable.dll (net8.0)",
                result.Output);
            Assert.Contains(
                "Warning: Library inspection failed for "
                + "'lib/net8.0/Invalid.dll'",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_NamesakeNarrowsAggregate()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var result = await RunAppAsync(
                "library", packagePath,
                "--namesake-library",
                "-S", "Library Info",
                "--markdown", "--tips", "q");

            Assert.True(
                result.Exit == 0,
                $"Exit {result.Exit}: {result.Error}");
            Assert.Contains("# Test.Primary.dll", result.Output);
            Assert.Contains("## Library Info", result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCommand_AggregateRetainsProducerLibraryAndAmbiguity()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var listing = await RunAppAsync(
                "type", packagePath,
                "-t", "CommandExecutionTests",
                "--table", "--columns", "Type,Library",
                "--tips", "q");
            var exact = await RunAppAsync(
                "type", packagePath,
                "DotnetInspect.Cli.Tests.CommandExecutionTests",
                "--tips", "q");

            Assert.Equal(0, listing.Exit);
            Assert.Contains("Latest.One.dll", listing.Output);
            Assert.Contains("Latest.Two.dll", listing.Output);
            Assert.Equal(1, exact.Exit);
            Assert.Empty(exact.Output);
            Assert.Contains(
                "ambiguous across the selected libraries",
                exact.Error);
            Assert.Contains("Latest.One.dll", exact.Error);
            Assert.Contains("Latest.Two.dll", exact.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCommand_AggregateDeduplicatesForwardedDeclaration()
    {
        string packagePath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PackageLibrarySelection",
            "avalonia.12.1.2.nupkg");

        var listing = await RunAppAsync(
            "type", packagePath,
            "-t",
            "Avalonia.Data.MultiBinding",
            "--tfm", "net8.0",
            "--table", "--columns", "Type,Library",
            "--tips", "q");
        var exact = await RunAppAsync(
            "type",
            "Avalonia.Data.MultiBinding",
            "--package", packagePath,
            "--tfm", "net8.0",
            "--tips", "q");

        Assert.Equal(0, listing.Exit);
        Assert.Equal(
            1,
            listing.Output.Split(
                "Avalonia.Data.MultiBinding",
                StringSplitOptions.None).Length - 1);
        Assert.Contains("Avalonia.Base.dll", listing.Output);
        Assert.Equal(0, exact.Exit);
        Assert.DoesNotContain(
            "ambiguous across the selected libraries",
            exact.Error);
    }

    [Fact]
    public async Task PackageCommand_NamesakeNarrowsWithoutFirstLibraryFallback()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath, "--namesake-library");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "does not have one unique namesake library",
                result.Error);
            Assert.DoesNotContain("# Latest.One.dll", result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_LibrarySectionUsesSelectedTfmAggregate()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "-S", "Library Info",
                "--markdown", "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains("# Latest.One.dll", result.Output);
            Assert.Contains("# Latest.Two.dll", result.Output);
            Assert.DoesNotContain("# Older.dll", result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_ExactLibraryNarrowsAggregate()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--library", "Latest.Two.dll",
                "-S", "Library Info",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains("# Latest.Two.dll", result.Output);
            Assert.DoesNotContain("# Latest.One.dll", result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibrariesExplainsAggregateDefault()
    {
        var result = await RunAppAsync(
            "package", "Any.Package", "--all-libraries");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Selected-TFM package Library inspection now includes all "
                + "compatible libraries by default",
            result.Error);
        Assert.Contains("--namesake-library", result.Error);
        Assert.Contains("--library <asset>", result.Error);
    }

    [Fact]
    public async Task PackageLibraryAggregateIdentifierConfusionAudit_CollectsTransitiveReferences()
    {
        var (packagePath, tempDir) =
            CreateIdentifierConfusionReferencePackage();
        try
        {
            var result = await RunAppAsync(
                "library", packagePath,
                "-S", SectionNames.IdentifierConfusion,
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "IdentifierConfusionReferenceClosure[",
                result.Output);
            Assert.Contains("U+03BF→O", result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageLibraryAggregateIdentifierConfusionAudit_PreservesHealthyResultsOnTraversalFailure()
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
            System.IO.Compression.ZipFile.CreateFromDirectory(
                packageRoot,
                packagePath);

            var result = await RunAppAsync(
                "library", packagePath,
                "-S", SectionNames.IdentifierConfusion,
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Contains("U+0405→S", result.Output);
            Assert.Equal(
                "Warning: Identifier audit failed for "
                + "'lib/net8.0/Root.dll': invalid assembly metadata"
                + Environment.NewLine,
                result.Error);
            Assert.DoesNotContain("Bridge", result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageLibraryAggregateIdentifierConfusionAudit_FailsWhenDirectReferencesCannotBeDecoded()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-reference-decode-package-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        string libraryDirectory =
            Path.Combine(packageRoot, "lib", "net8.0");
        Directory.CreateDirectory(libraryDirectory);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(libraryDirectory, "A.Valid.dll"),
                "\u0405ystem.Valid");
            WriteMalformedAssemblyReferenceNameAssembly(
                Path.Combine(libraryDirectory, "Root.dll"));
            string packagePath = Path.Combine(
                tempDir,
                "Identifier.Reference.Decode.1.0.0.nupkg");
            System.IO.Compression.ZipFile.CreateFromDirectory(
                packageRoot,
                packagePath);

            var result = await RunAppAsync(
                "library", packagePath,
                "-S", SectionNames.IdentifierConfusion,
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Contains("U+0405→S", result.Output);
            Assert.Equal(
                "Warning: Identifier audit failed for "
                + "'lib/net8.0/Root.dll': invalid assembly metadata"
                + Environment.NewLine,
                result.Error);
            Assert.DoesNotContain("System.Runtime", result.Error);

            var signals = await RunAppAsync(
                "library", packagePath,
                "-S", SectionNames.Signals,
                "--tips", "q");

            Assert.Equal(1, signals.Exit);
            Assert.Contains(
                "| Identity | Identifier confusion | Unavailable "
                + "| invalid assembly metadata |",
                signals.Output);
            Assert.DoesNotContain(
                "| Identity | Identifier confusion | None |",
                signals.Output);
            Assert.Equal(
                "Warning: Identifier audit failed for "
                + "'lib/net8.0/Root.dll': invalid assembly metadata"
                + Environment.NewLine,
                signals.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCommand_AllTfmsKeepsFrameworkAggregatesSeparate()
    {
        var (packagePath, tempDir) =
            CreateLocalMultiTfmLibraryPackage();
        try
        {
            var result = await RunAppAsync(
                "type", packagePath,
                "DotnetInspect.Cli.Tests.CommandExecutionTests",
                "--tfm", "all",
                "--markdown", "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains(
                "# Tfm.All.Sample (net10.0)",
                result.Output);
            Assert.Contains(
                "# Tfm.All.Sample (net8.0)",
                result.Output);
            Assert.DoesNotContain(
                "ambiguous across",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task MemberCommand_AllTfmsKeepsFrameworkAggregatesSeparate()
    {
        var (packagePath, tempDir) =
            CreateLocalMultiTfmLibraryPackage();
        try
        {
            var result = await RunAppAsync(
                "member",
                "DotnetInspect.Cli.Tests.CommandExecutionTests",
                "--package", packagePath,
                "--tfm", "all",
                "--markdown", "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains(
                "# Tfm.All.Sample (net10.0)",
                result.Output);
            Assert.Contains(
                "# Tfm.All.Sample (net8.0)",
                result.Output);
            Assert.DoesNotContain(
                "ambiguous across",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCommand_AllTfmsNamesakeNarrowsEachFramework()
    {
        var (packagePath, tempDir) =
            CreateLocalMultiTfmLibraryPackage(
                "DotnetInspect.Cli.Tests",
                "Different.File.dll");
        try
        {
            var result = await RunAppAsync(
                "type",
                "DotnetInspect.Cli.Tests.CommandExecutionTests",
                "--package", packagePath,
                "--namesake-library",
                "--tfm", "all",
                "--markdown", "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains(
                "# DotnetInspect.Cli.Tests (net10.0)",
                result.Output);
            Assert.Contains(
                "# DotnetInspect.Cli.Tests (net8.0)",
                result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryAndPackageCommands_AllTfmsNarrowEachFramework()
    {
        var (packagePath, tempDir) =
            CreateLocalMultiTfmLibraryPackage(
                "DotnetInspect.Cli.Tests",
                "Different.File.dll");
        try
        {
            var results = new[]
            {
                await RunAppAsync(
                    "library", "Different.File.dll",
                    "--package", packagePath,
                    "--tfm", "all",
                    "-S", "Library Info",
                    "--markdown", "--tips", "q"),
                await RunAppAsync(
                    "library", packagePath,
                    "--namesake-library",
                    "--tfm", "all",
                    "-S", "Library Info",
                    "--markdown", "--tips", "q"),
                await RunAppAsync(
                    "package", packagePath,
                    "--library", "Different.File.dll",
                    "--tfm", "all",
                    "-S", "Library Info",
                    "--markdown", "--tips", "q"),
                await RunAppAsync(
                    "package", packagePath,
                    "--namesake-library",
                    "--tfm", "all",
                    "-S", "Library Info",
                    "--markdown", "--tips", "q"),
            };

            foreach (var result in results)
            {
                Assert.True(
                    result.Exit == 0,
                    $"Exit {result.Exit}: {result.Error}");
                Assert.Contains("net10.0", result.Output);
                Assert.Contains("net8.0", result.Output);
                Assert.Empty(result.Error);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCommand_AllTfmsRejectsRowOutput()
    {
        var (packagePath, tempDir) =
            CreateLocalMultiTfmLibraryPackage();
        try
        {
            var result = await RunAppAsync(
                "type", packagePath,
                "--tfm", "all",
                "--table", "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "supports Markdown document output",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static (string PackagePath, string TempDir)
        CreateLocalMultiTfmLibraryPackage(
            string packageId = "Tfm.All.Sample",
            string assetName = "Tfm.All.Sample.dll")
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        foreach (string framework in new[] { "net8.0", "net10.0" })
        {
            string libDir =
                Path.Combine(packageRoot, "lib", framework);
            Directory.CreateDirectory(libDir);
            File.Copy(
                TestAssemblyPath,
                Path.Combine(libDir, assetName));
        }

        string packagePath =
            Path.Combine(tempDir, $"{packageId}.1.0.0.nupkg");
        System.IO.Compression.ZipFile.CreateFromDirectory(
            packageRoot,
            packagePath);
        return (packagePath, tempDir);
    }
}
