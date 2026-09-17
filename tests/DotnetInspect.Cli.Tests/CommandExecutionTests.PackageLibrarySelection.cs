using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;

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
            Assert.Contains(
                "### lib/net10.0/Latest.One.dll (net10.0)",
                result.Output);
            Assert.Contains(
                "### lib/net10.0/Latest.Two.dll (net10.0)",
                result.Output);
            Assert.DoesNotContain("Older.dll", result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--print")]
    [InlineData("--value")]
    [InlineData("--urls")]
    [InlineData("--paths")]
    public async Task LibraryCommand_AggregateRejectsScalarProjection(
        string projection)
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var arguments = new List<string>
            {
                "library",
                packagePath,
                "-S",
                projection == "--print"
                    ? "Context: Source Location"
                    : "Library Info",
            };
            if (projection == "--print")
                arguments.AddRange(["--il-offset", "0x06000001+0x0"]);
            arguments.AddRange([projection, "--tips", "q"]);

            var result = await RunAppAsync(arguments.ToArray());

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "Scalar Library projections require one exact Library",
                result.Error);
            Assert.Contains("--library <asset>", result.Error);
            Assert.Contains("--namesake-library", result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_AggregateRowsRetainProducerLibrary()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var table = await RunAppAsync(
                "library", packagePath,
                "-S", "References",
                "--table", "--rows", "1",
                "--tips", "q");
            var tsv = await RunAppAsync(
                "library", packagePath,
                "-S", "References",
                "--tsv", "--rows", "1",
                "--tips", "q");
            var jsonl = await RunAppAsync(
                "library", packagePath,
                "-S", "References",
                "--jsonl", "--rows", "1",
                "--tips", "q");
            var projected = await RunAppAsync(
                "library", packagePath,
                "-S", "References",
                "--tsv", "--columns", "Name",
                "--rows", "1",
                "--tips", "q");
            var exact = await RunAppAsync(
                "library", "Latest.One.dll",
                "--package", packagePath,
                "-S", "References",
                "--tsv", "--rows", "1",
                "--tips", "q");

            Assert.Equal(0, table.Exit);
            Assert.Empty(table.Error);
            Assert.StartsWith("library", table.Output);
            Assert.Contains("Name", table.Output);
            Assert.Contains(
                "lib/net10.0/Latest.One.dll",
                table.Output);
            Assert.Contains(
                "lib/net10.0/Latest.Two.dll",
                table.Output);

            Assert.Equal(0, tsv.Exit);
            Assert.Empty(tsv.Error);
            Assert.Contains("library\tname\t", tsv.Output);
            Assert.Contains(
                "lib/net10.0/Latest.One.dll",
                tsv.Output);
            Assert.Contains(
                "lib/net10.0/Latest.Two.dll",
                tsv.Output);

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

            Assert.Equal(0, exact.Exit);
            Assert.Empty(exact.Error);
            Assert.StartsWith("name\t", exact.Output);
            Assert.DoesNotContain("library\t", exact.Output);

        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AggregateIntegrationsRetainProducerLibrary()
    {
        var (packagePath, tempDir) = CreateLocalIntegrationPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "-S", "Integrations",
                "--tsv",
                "--rows", "40",
                "--tips", "q");

            Assert.True(
                result.Exit == 0,
                $"Exit {result.Exit}: {result.Error}");
            Assert.StartsWith("library\t", result.Output);
            Assert.Contains(
                "lib/net11.0/Microsoft.Extensions.Configuration.dll",
                result.Output);
            Assert.Contains(
                "lib/net11.0/Microsoft.Extensions.Configuration.Json.dll",
                result.Output);
            Assert.Contains(
                "Microsoft.Extensions.Configuration."
                    + "JsonConfigurationExtensions.AddJsonFile(...)",
                result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_AggregateRejectsExactOperation()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            string offsetsPath = Path.Combine(
                tempDir,
                "offsets.txt");
            File.WriteAllText(
                offsetsPath,
                "0x06000001+0x0");

            foreach (string[] coordinate in new[]
            {
                new[] { "--il-offset", "0x06000001+0x0" },
                new[] { "--il-offsets", offsetsPath },
                new[] { "--heap", "#Strings:0x1" },
                new[]
                {
                    "-S", "Resources",
                    "--extract-resources", Path.Combine(tempDir, "resources"),
                },
            })
            {
                var result = await RunAppAsync(
                    ["library", packagePath, .. coordinate, "--tips", "q"]);

                Assert.Equal(1, result.Exit);
                Assert.Empty(result.Output);
                Assert.Contains(
                    "requires one exact Library",
                    result.Error);
                Assert.Contains("--library <asset>", result.Error);
                Assert.Contains(
                    "--namesake-library",
                    result.Error);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_ExactMissingReportsSelectedLibraries()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "library", "Missing.dll",
                "--package", packagePath,
                "-S", "Library Info",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "Library 'Missing.dll' was not found",
                result.Error);
            Assert.Contains("Available libraries:", result.Error);
            Assert.Contains(
                "lib/net10.0/Latest.One.dll",
                result.Error);
            Assert.DoesNotContain("Older.dll", result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_ExplicitTfmReportsNoMatch()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "library", packagePath,
                "--tfm", "net472",
                "-S", "Library Info",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "No compile libraries were selected for TFM 'net472'",
                result.Error);
            Assert.Contains("Available TFMs:", result.Error);
            Assert.Contains("net8.0", result.Error);
            Assert.Contains("net10.0", result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_ExplicitEmptyCompileGroupIsVisible()
    {
        var (packagePath, tempDir) =
            CreatePackageWithEmptyReferenceGroup();
        try
        {
            var result = await RunAppAsync(
                "library", packagePath,
                "--tfm", "net8.0",
                "-S", "Library Info",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "declares an empty compile group for TFM 'net8.0'",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_NamesakeAmbiguityFailsClosed()
    {
        var (packagePath, tempDir) =
            CreateNamesakeAmbiguityPackage();
        try
        {
            var result = await RunAppAsync(
                "library", packagePath,
                "--namesake-library",
                "-S", "Library Info",
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "does not have one unique namesake library",
                result.Error);
            Assert.Contains("First.dll", result.Error);
            Assert.Contains("Second.dll", result.Error);
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
                "Could not select library descriptor for "
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
        var (packagePath, tempDir) =
            CreatePackageWithNamesakeAndPlaceholder();
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
            Assert.Contains(
                "# Renamed.dll (net8.0)",
                result.Output);
            Assert.Contains("## Library Info", result.Output);
            Assert.DoesNotContain("Text.dll", result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_NamesakeFailsClosedForUnreadableParticipant()
    {
        var (packagePath, tempDir) =
            CreatePackageWithNamesakeAndPlaceholder(
                includeUnreadable: true);
        try
        {
            var result = await RunAppAsync(
                "library", packagePath,
                "--namesake-library",
                "-S", "Library Info",
                "--markdown", "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "managed assembly identity could not be read",
                result.Error);
            Assert.Contains("lib/net8.0/Invalid.dll", result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_BareInspectionRemainsPackageScoped()
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
    public async Task LibraryCommand_SingleLibraryPackageRemainsNatural()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var result = await RunAppAsync(
                "library", packagePath,
                "-S", "Library Info",
                "--markdown", "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains(
                "### lib/net10.0/Test.Primary.dll (net10.0)",
                result.Output);
            Assert.Contains("## Libraries", result.Output);
            Assert.DoesNotContain("Latest.", result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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
            Assert.Contains(
                "### lib/net10.0/Latest.One.dll (net10.0)",
                result.Output);
            Assert.Contains(
                "### lib/net10.0/Latest.Two.dll (net10.0)",
                result.Output);
            Assert.DoesNotContain("Older.dll", result.Output);
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
            Assert.Contains(
                "# Latest.Two.dll (net10.0)",
                result.Output);
            Assert.DoesNotContain("Latest.One.dll", result.Output);
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
    public async Task LibraryAndPackageCommands_AllTfmsRenderEveryFrameworkAggregate()
    {
        var (packagePath, tempDir) =
            CreateLocalMultiTfmLibraryPackage(
                includeCompanion: true);
        try
        {
            var results = new[]
            {
                await RunAppAsync(
                    "library", packagePath,
                    "--tfm", "all",
                    "-S", "Library Info",
                    "--markdown", "--tips", "q"),
                await RunAppAsync(
                    "package", packagePath,
                    "--tfm", "all",
                    "-S", "Library Info",
                    "--markdown", "--tips", "q"),
            };

            foreach (var result in results)
            {
                Assert.Equal(0, result.Exit);
                Assert.Contains(
                    "lib/net8.0/Tfm.All.Sample.dll (net8.0)",
                    result.Output);
                Assert.Contains(
                    "lib/net8.0/Companion.dll (net8.0)",
                    result.Output);
                Assert.Contains(
                    "lib/net10.0/Tfm.All.Sample.dll (net10.0)",
                    result.Output);
                Assert.Contains(
                    "lib/net10.0/Companion.dll (net10.0)",
                    result.Output);
                Assert.Empty(result.Error);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static (string PackagePath, string TempDir)
        CreateLocalIntegrationPackage(params string[] assemblyNames)
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        string libraryDirectory =
            Path.Combine(packageRoot, "lib", "net11.0");
        Directory.CreateDirectory(libraryDirectory);

        foreach (string assemblyName in assemblyNames)
        {
            var (path, _, _, error) =
                PlatformResolver.ResolveAssembly(assemblyName);
            Assert.True(
                error is null && path is not null,
                $"Could not resolve platform assembly '{assemblyName}': "
                    + error);
            File.Copy(
                path,
                Path.Combine(
                    libraryDirectory,
                    Path.GetFileName(path)));
        }

        string packagePath =
            Path.Combine(tempDir, "Test.Integrations.1.0.0.nupkg");
        System.IO.Compression.ZipFile.CreateFromDirectory(
            packageRoot,
            packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir)
        CreateNamesakeAmbiguityPackage()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        string libDir = Path.Combine(packageRoot, "lib", "net8.0");
        Directory.CreateDirectory(libDir);
        File.Copy(
            TestAssemblyPath,
            Path.Combine(libDir, "First.dll"));
        File.Copy(
            TestAssemblyPath,
            Path.Combine(libDir, "Second.dll"));

        string packagePath = Path.Combine(
            tempDir,
            "DotnetInspect.Cli.Tests.1.0.0.nupkg");
        System.IO.Compression.ZipFile.CreateFromDirectory(
            packageRoot,
            packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir)
        CreateLocalMultiTfmLibraryPackage(
            string packageId = "Tfm.All.Sample",
            string assetName = "Tfm.All.Sample.dll",
            bool includeCompanion = false)
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
            if (includeCompanion)
            {
                File.Copy(
                    TestAssemblyPath,
                    Path.Combine(libDir, "Companion.dll"));
            }
        }

        string packagePath =
            Path.Combine(tempDir, $"{packageId}.1.0.0.nupkg");
        System.IO.Compression.ZipFile.CreateFromDirectory(
            packageRoot,
            packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir)
        CreatePackageWithNamesakeAndPlaceholder(
            bool includeUnreadable = false)
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        string libDir = Path.Combine(packageRoot, "lib", "net8.0");
        Directory.CreateDirectory(libDir);
        File.Copy(
            TestAssemblyPath,
            Path.Combine(libDir, "Renamed.dll"));
        File.WriteAllText(
            Path.Combine(libDir, "Text.dll"),
            "café",
            new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true));
        if (includeUnreadable)
        {
            File.WriteAllBytes(
                Path.Combine(libDir, "Invalid.dll"),
                [1, 2, 3]);
        }

        string packagePath = Path.Combine(
            tempDir,
            "DotnetInspect.Cli.Tests.1.0.0.nupkg");
        System.IO.Compression.ZipFile.CreateFromDirectory(
            packageRoot,
            packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir)
        CreatePackageWithEmptyReferenceGroup()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        string libDir = Path.Combine(packageRoot, "lib", "net8.0");
        Directory.CreateDirectory(libDir);
        File.Copy(
            TestAssemblyPath,
            Path.Combine(libDir, "Implementation.dll"));
        string refDir = Path.Combine(packageRoot, "ref", "net8.0");
        Directory.CreateDirectory(refDir);
        File.WriteAllText(
            Path.Combine(refDir, "_._"),
            "");

        string packagePath = Path.Combine(
            tempDir,
            "Empty.Reference.Group.1.0.0.nupkg");
        System.IO.Compression.ZipFile.CreateFromDirectory(
            packageRoot,
            packagePath);
        return (packagePath, tempDir);
    }

}
