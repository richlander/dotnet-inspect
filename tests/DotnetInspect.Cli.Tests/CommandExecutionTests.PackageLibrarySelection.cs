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
    public async Task LibraryCommand_AggregateRejectsLibraryCoordinate()
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
            Assert.Contains(
                "# lib/net10.0/Test.Primary.dll (net10.0)",
                result.Output);
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
            var projected = await RunAppAsync(
                "type", packagePath,
                "-t", "CommandExecutionTests",
                "--tsv", "--columns", "Type",
                "--tips", "q");
            var projectedTable = await RunAppAsync(
                "type", packagePath,
                "-t", "CommandExecutionTests",
                "--table", "--columns", "Type",
                "--tips", "q");
            var projectedJsonl = await RunAppAsync(
                "type", packagePath,
                "-t", "CommandExecutionTests",
                "--jsonl", "--columns", "Type",
                "--tips", "q");
            var projectedSection = await RunAppAsync(
                "type", packagePath,
                "-t", "CommandExecutionTests",
                "-S", "Classes",
                "--tsv", "--columns", "Type",
                "--tips", "q");
            var exactListing = await RunAppAsync(
                "type",
                "--package", packagePath,
                "--library", "Latest.One.dll",
                "-t", "CommandExecutionTests",
                "--tsv", "--columns", "Type",
                "--tips", "q");
            var exact = await RunAppAsync(
                "type", packagePath,
                "DotnetInspect.Cli.Tests.CommandExecutionTests",
                "--tips", "q");

            Assert.Equal(0, listing.Exit);
            Assert.Contains("Latest.One.dll", listing.Output);
            Assert.Contains("Latest.Two.dll", listing.Output);
            Assert.Equal(0, projected.Exit);
            Assert.Empty(projected.Error);
            Assert.StartsWith("type\tlibrary\n", projected.Output);
            Assert.Contains("Latest.One.dll", projected.Output);
            Assert.Contains("Latest.Two.dll", projected.Output);
            Assert.Equal(0, projectedTable.Exit);
            Assert.Empty(projectedTable.Error);
            Assert.Contains("Library", projectedTable.Output);
            Assert.Contains("Latest.One.dll", projectedTable.Output);
            Assert.Contains("Latest.Two.dll", projectedTable.Output);
            Assert.Equal(0, projectedJsonl.Exit);
            Assert.Empty(projectedJsonl.Error);
            Assert.Contains("\"library\":", projectedJsonl.Output);
            Assert.Contains("Latest.One.dll", projectedJsonl.Output);
            Assert.Contains("Latest.Two.dll", projectedJsonl.Output);
            Assert.Equal(0, projectedSection.Exit);
            Assert.Empty(projectedSection.Error);
            Assert.StartsWith(
                "type\tlibrary\n",
                projectedSection.Output);
            Assert.Contains(
                "Latest.One.dll",
                projectedSection.Output);
            Assert.Contains(
                "Latest.Two.dll",
                projectedSection.Output);
            Assert.Equal(0, exactListing.Exit);
            Assert.Empty(exactListing.Error);
            Assert.StartsWith("type\n", exactListing.Output);
            Assert.DoesNotContain(
                "\tlibrary",
                exactListing.Output,
                StringComparison.OrdinalIgnoreCase);
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
    public async Task MemberCommand_AggregateUnsafeAnalysisUsesDefiningLibrary()
    {
        string packagePath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PackageLibrarySelection",
            "avalonia.12.1.2.nupkg");

        var result = await RunAppAsync(
            "member",
            "Avalonia.OpenGL.GlInterface",
            "--package", packagePath,
            "--tfm", "net8.0",
            "-S", "Unsafe Members",
            "--tips", "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains("## Unsafe Members", result.Output);
        Assert.Contains("Unsafe signature", result.Output);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task ApiCommand_UnreadableAggregateParticipantPreservesHealthySurface(
        string command)
    {
        var (packagePath, tempDir) =
            CreatePackageWithAdditionalLibrary(
                "Invalid.dll",
                [1, 2, 3]);
        try
        {
            var result = command == "type"
                ? await RunAppAsync(
                    "type", packagePath,
                    "-t", "CommandExecutionTests",
                    "--table", "--columns", "Type,Library",
                    "--tips", "q")
                : await RunAppAsync(
                    "member",
                    "DotnetInspect.Cli.Tests.CommandExecutionTests",
                    "--package", packagePath,
                    "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Contains(
                "DotnetInspect.Cli.Tests.CommandExecutionTests",
                result.Output);
            Assert.Contains(
                command == "member"
                    ? "Package Library 'lib/net8.0/Invalid.dll' is not "
                        + "a readable managed assembly image"
                    : "API inspection rejected 1 metadata row",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCommand_UnreadableParticipantMakesMissingTypeIncomplete()
    {
        var (packagePath, tempDir) =
            CreatePackageWithAdditionalLibrary(
                "Invalid.dll",
                [1, 2, 3]);
        try
        {
            var result = await RunAppAsync(
                "type",
                "Possibly.In.Unreadable.Library",
                "--package", packagePath,
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "Package Library 'lib/net8.0/Invalid.dll' is not "
                    + "a readable managed assembly image",
                result.Error);
            Assert.Contains(
                "lookup is incomplete because one or more selected "
                    + "Libraries could not be inspected",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCommand_AggregateDocsUseDefiningLibrary()
    {
        var (packagePath, tempDir) =
            CreatePackageWithDefiningLibraryDocumentation();
        try
        {
            var result = await RunAppAsync(
                "type",
                "DotnetInspector.Services.TfmSelector",
                "--package", packagePath,
                "--markdown", "-v:n",
                "--tips", "q");
            var listing = await RunAppAsync(
                "type",
                "--package", packagePath,
                "-t", "TfmSelector",
                "--tsv",
                "--columns", "Type,Description,Library",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "Defining Library documentation.",
                result.Output);
            Assert.Equal(0, listing.Exit);
            Assert.Empty(listing.Error);
            Assert.Contains(
                "Defining Library documentation.",
                listing.Output);
            Assert.Contains("Z.Services.dll", listing.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task MemberCommand_AggregateDocsUseDefiningLibrary()
    {
        var (packagePath, tempDir) =
            CreatePackageWithDefiningLibraryDocumentation();
        try
        {
            var result = await RunAppAsync(
                "member",
                "DotnetInspector.Services.TfmSelector",
                "NormalizeTfm",
                "--package", packagePath,
                "--markdown", "-v:n",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "Defining Library member documentation.",
                result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task MemberCommand_AggregateSourceUsesDefiningLibrary()
    {
        var (packagePath, tempDir) =
            CreatePackageWithDefiningLibraryDocumentation();
        try
        {
            var result = await RunAppAsync(
                "member",
                "DotnetInspector.Services.TfmSelector",
                "NormalizeTfm:1",
                "--package", packagePath,
                "-S", "PDB Source",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains("## PDB Source", result.Output);
            Assert.Contains(
                "public static string NormalizeTfm",
                result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task MemberCommand_AggregateCallerScopeUsesDefiningLibrary()
    {
        var (packagePath, tempDir) =
            CreatePackageWithDefiningLibraryDocumentation();
        string callerDirectory = Path.Combine(
            tempDir,
            "callers");
        Directory.CreateDirectory(callerDirectory);
        try
        {
            var result = await RunAppAsync(
                "member",
                "DotnetInspector.Services.TfmSelector",
                "NormalizeTfm",
                "--package", packagePath,
                "--bin", callerDirectory,
                "-S", "Callers",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains("## Callers", result.Output);
            Assert.Contains("GetTfmPriority", result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Invalid.dll", false)]
    [InlineData("Text.dll", true)]
    public async Task FindCommand_ClassifiesAggregateParticipants(
        string additionalLibrary,
        bool plainText)
    {
        var (packagePath, tempDir) =
            CreatePackageWithAdditionalLibrary(
                additionalLibrary,
                plainText
                    ? "not an assembly"u8.ToArray()
                    : [1, 2, 3]);
        try
        {
            var result = await RunAppAsync(
                "find",
                "CommandExecutionTests",
                "--package", packagePath,
                "--table",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains("CommandExecutionTests", result.Output);
            Assert.Contains("DotnetInspect.Cli.Tests", result.Output);
            if (plainText)
            {
                Assert.Empty(result.Error);
                Assert.DoesNotContain(
                    additionalLibrary,
                    result.Output);
            }
            else
            {
                Assert.Contains("Could not read", result.Error);
                Assert.Contains(
                    additionalLibrary,
                    result.Error);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCommand_NonAssemblyAggregateParticipantIsExcluded()
    {
        var (packagePath, tempDir) =
            CreatePackageWithAdditionalLibrary(
                "Text.dll",
                "not an assembly"u8.ToArray());
        try
        {
            var result = await RunAppAsync(
                "type", packagePath,
                "-t", "CommandExecutionTests",
                "--table", "--columns", "Type,Library",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "DotnetInspect.Cli.Tests.CommandExecutionTests",
                result.Output);
            Assert.DoesNotContain("Text.dll", result.Output);
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
                "# lib/net10.0/Latest.Two.dll (net10.0)",
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
    public async Task TypeCommand_NamesakeSkipsNonAssemblyPlaceholder()
    {
        var (packagePath, tempDir) =
            CreatePackageWithNamesakeAndPlaceholder();
        try
        {
            var result = await RunAppAsync(
                "type",
                "DotnetInspect.Cli.Tests.CommandExecutionTests",
                "--package", packagePath,
                "--namesake-library",
                "--markdown", "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Contains(
                "DotnetInspect.Cli.Tests.CommandExecutionTests",
                result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCommand_EmptyReferenceGroupSuppressesLibraryFallback()
    {
        var (packagePath, tempDir) =
            CreatePackageWithEmptyReferenceGroup();
        try
        {
            var result = await RunAppAsync(
                "type",
                "DotnetInspect.Cli.Tests.CommandExecutionTests",
                "--package", packagePath,
                "--tfm", "net8.0",
                "--markdown", "--tips", "q");

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

    private static (string PackagePath, string TempDir)
        CreatePackageWithAdditionalLibrary(
            string additionalLibraryName,
            byte[] additionalLibraryContent)
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
        File.WriteAllBytes(
            Path.Combine(libDir, additionalLibraryName),
            additionalLibraryContent);

        string packagePath = Path.Combine(
            tempDir,
            "Aggregate.Participant.Sample.1.0.0.nupkg");
        System.IO.Compression.ZipFile.CreateFromDirectory(
            packageRoot,
            packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir)
        CreatePackageWithNamesakeAndPlaceholder()
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

    private static (string PackagePath, string TempDir)
        CreatePackageWithDefiningLibraryDocumentation()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        string packageRoot = Path.Combine(tempDir, "content");
        string libDir = Path.Combine(packageRoot, "lib", "net8.0");
        Directory.CreateDirectory(libDir);
        File.Copy(
            TestAssemblyPath,
            Path.Combine(libDir, "A.Healthy.dll"));
        string servicesAssembly =
            typeof(DotnetInspector.Services.TfmSelector)
                .Assembly.Location;
        string definingLibrary =
            Path.Combine(libDir, "Z.Services.dll");
        File.Copy(
            servicesAssembly,
            definingLibrary);
        string servicesPdb =
            Path.ChangeExtension(servicesAssembly, ".pdb");
        if (File.Exists(servicesPdb))
        {
            File.Copy(
                servicesPdb,
                Path.ChangeExtension(definingLibrary, ".pdb"));
        }
        File.WriteAllText(
            Path.ChangeExtension(definingLibrary, ".xml"),
            """
            <?xml version="1.0"?>
            <doc>
              <assembly>
                <name>DotnetInspector.Services</name>
              </assembly>
              <members>
                <member name="T:DotnetInspector.Services.TfmSelector">
                  <summary>Defining Library documentation.</summary>
                </member>
                <member name="M:DotnetInspector.Services.TfmSelector.NormalizeTfm(System.String)">
                  <summary>Defining Library member documentation.</summary>
                </member>
              </members>
            </doc>
            """);

        string packagePath = Path.Combine(
            tempDir,
            "Aggregate.Documentation.Sample.1.0.0.nupkg");
        System.IO.Compression.ZipFile.CreateFromDirectory(
            packageRoot,
            packagePath);
        return (packagePath, tempDir);
    }
}
