using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Cache;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using ILInspector.Analysis;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{

    [Fact]
    public async Task LibraryCoordinateCommand_ImplicitlySelectsSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Source Location", output);
        Assert.Contains("| Field | Value |", output);
        Assert.Contains("| Method | System.HexConverter.FromChar |", output);
        Assert.Contains("| Token | 0x6000001 |", output);
        Assert.Contains("| IL Offset | 0x0 |", output);
        Assert.Contains("HexConverter.cs", output);
        Assert.Contains("## Context: Member", output);
        Assert.Contains("## Context: Instruction", output);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_BareLocalRequestRendersMemberContext()
    {
        var (token, callOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        string coordinate = $"0x{token:X8}+0x{callOffset:X}";

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            coordinate,
            "--library",
            TestAssemblyPath,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Member", output);
        Assert.Contains(nameof(SemanticFactsFixture.AllSignals), output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryCoordinateCommand_BareCountSelectsRenderedLines(
            bool beforeSubcommand)
    {
        string[] args =
            beforeSubcommand
                ?
                [
                    "library", "-n", "1", "coordinate",
                    "0x06000001+0x0",
                    "--platform", "System.Text.Json",
                    "--tips", "q",
                ]
                :
                [
                    "library", "coordinate",
                    "0x06000001+0x0",
                    "--platform", "System.Text.Json",
                    "-n", "1",
                    "--tips", "q",
                ];

        var (exit, output, error) = await RunAppAsync(args);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Single(
            output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryCoordinateCommand_ExplicitLinesAcceptsCountPlacement(
            bool beforeSubcommand)
    {
        string[] args =
            beforeSubcommand
                ?
                [
                    "library", "-n", "1", "--lines", "coordinate",
                    "0x06000001+0x0",
                    "--platform", "System.Text.Json",
                    "--tips", "q",
                ]
                :
                [
                    "library", "coordinate",
                    "0x06000001+0x0",
                    "--platform", "System.Text.Json",
                    "-n", "1", "--lines",
                    "--tips", "q",
                ];

        var (exit, output, error) = await RunAppAsync(args);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Single(
            output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
    }

    [Theory]
    [InlineData("--head")]
    [InlineData("--tail")]
    public async Task LibraryCoordinateCommand_InferredLinesComposeWithRows(
            string direction)
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "--rows",
            "1..1",
            "-n",
            "1",
            direction,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Single(
            output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public async Task LibraryCoordinateCommand_UsesPackageRelativeLibrary()
    {
        var (token, callOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        string tempDir = Directory.CreateTempSubdirectory(
            "library-coordinate-package-").FullName;
        string content = Path.Combine(tempDir, "content");
        string relativeLibraryPath =
            "lib/net11.0/Coordinate.Package.dll";
        string libraryPath = Path.Combine(
            content,
            "lib",
            "net11.0",
            "Coordinate.Package.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        File.Copy(TestAssemblyPath, libraryPath);
        string packagePath = Path.Combine(
            tempDir,
            "Coordinate.Package.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(content, packagePath);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                $"0x{token:X8}+0x{callOffset:X}",
                "--package",
                packagePath,
                "--library",
                relativeLibraryPath,
                "-S",
                "Context: Member",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("## Context: Member", output);
            Assert.Contains(nameof(SemanticFactsFixture.AllSignals), output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_UsesExplicitPackageLibraryWithinTfm()
    {
        var openMethod = typeof(AssemblyInspectionSession).GetMethod(
            nameof(AssemblyInspectionSession.Open),
            [typeof(string)])!;
        string tempDir = Directory.CreateTempSubdirectory(
            "library-coordinate-package-tfm-").FullName;
        string content = Path.Combine(tempDir, "content");
        string libraryDirectory = Path.Combine(
            content,
            "lib",
            "net11.0");
        Directory.CreateDirectory(libraryDirectory);
        File.Copy(
            TestAssemblyPath,
            Path.Combine(libraryDirectory, "Coordinate.Package.dll"));
        File.Copy(
            typeof(AssemblyInspectionSession).Assembly.Location,
            Path.Combine(libraryDirectory, "Alternate.dll"));
        string packagePath = Path.Combine(
            tempDir,
            "Coordinate.Package.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(content, packagePath);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                $"0x{openMethod.MetadataToken:X8}+0x0",
                "--package",
                packagePath,
                "--library",
                "lib/net11.0/Alternate.dll",
                "--tfm",
                "net11.0",
                "-S",
                "Context: Member",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("# Alternate.dll (net11.0)", output);
            Assert.Contains("| Assembly | ILInspector.Metadata |", output);
            Assert.Contains(
                "| Member | ILInspector.Metadata.AssemblyInspectionSession.Open |",
                output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_InvalidCoordinateFailsBeforeAcquisition()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "not-a-coordinate",
            "--library",
            missingLibrary,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Invalid coordinate", error);
        Assert.DoesNotContain(missingLibrary, error);
        Assert.DoesNotContain("--il-offset", error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_InvalidMetadataRootFailsBeforeAcquisition()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "#Strings:1",
            "--library",
            missingLibrary,
            "--metadata-root",
            "not-a-root",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("expected cli or r2r-manifest", error);
        Assert.DoesNotContain(missingLibrary, error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_HeapSelectionMismatchFailsBeforeAcquisition()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "#Strings:1",
            "--library",
            missingLibrary,
            "-S",
            MetadataSectionNames.Image,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "library coordinate requires the heap coordinate section",
            error);
        Assert.DoesNotContain(missingLibrary, error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RequiresNamedLibrarySource()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x0",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "requires --library, --package, or --platform",
            error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsMultiLibraryTfmBeforeAcquisition()
    {
        string missingPackage = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-package-{Guid.NewGuid():N}.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x0",
            "--package",
            missingPackage,
            "--tfm",
            "all",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("requires one selected Library", error);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_MemberSelectionAllowsNonInstructionBoundary()
    {
        var bare = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x2",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");
        var member = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x2",
            "--platform",
            "System.Text.Json",
            "-S",
            "Context: Member",
            "--tips",
            "q");

        Assert.Equal(1, bare.Exit);
        Assert.Empty(bare.Output);
        Assert.Contains("not an instruction boundary", bare.Error);
        Assert.Equal(0, member.Exit);
        Assert.Empty(member.Error);
        Assert.Contains("## Context: Member", member.Output);
        Assert.Contains(
            "| Member | System.HexConverter.FromChar |",
            member.Output);
        Assert.DoesNotContain(
            "## Context: Instruction",
            member.Output);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_DiscoveryIsCoordinateScoped()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "-D",
            "@Context",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Context: Source Location", output);
        Assert.Contains("Context: Member", output);
        Assert.Contains("Context: Instruction", output);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_TreeDiscoveryDoesNotRequireLibraryAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x0",
            "-D",
            "--schema",
            "--tree",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Context", output);
        Assert.Contains("Source Location", output);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsParentPositionalSource()
    {
        string parentSource = Path.Combine(
            Path.GetTempPath(),
            $"missing-parent-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            parentSource,
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "A Library inspection source cannot precede library coordinate",
            error);
        Assert.DoesNotContain(parentSource, error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsEmptyParentPositionalSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "",
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "A Library inspection source cannot precede library coordinate",
            error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsParentSourceOption()
    {
        string parentPackage = Path.Combine(
            Path.GetTempPath(),
            $"missing-parent-package-{Guid.NewGuid():N}.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "--package",
            parentPackage,
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--package cannot be combined with library coordinate",
            error);
        Assert.DoesNotContain(parentPackage, error);
    }

    [Theory]
    [InlineData("--type=", "-t")]
    [InlineData("-t:", "-t")]
    [InlineData("--package=", "--package")]
    [InlineData("--extract-resources:", "--extract-resources")]
    public async Task LibraryCoordinateCommand_RejectsInlineEmptyParentValueBeforeAcquisition(
            string parentOption,
            string diagnosticOption)
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            parentOption,
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"{diagnosticOption} cannot be combined with library coordinate",
            error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsInlineEmptyParentValueAfterCoordinateValue()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "--type",
            "coordinate",
            "--package=",
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--package cannot be combined with library coordinate",
            error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsParentOperation()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "--references",
            "coordinate",
            "0x06000001+0x0",
            "--library",
            missingLibrary,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--references cannot be combined with library coordinate",
            error);
        Assert.DoesNotContain(missingLibrary, error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_HelpShowsFocusAndNamedSources()
    {
        var parent = await RunAppAsync("library", "--help");
        var child = await RunAppAsync(
            "library",
            "coordinate",
            "--help");

        Assert.Equal(0, parent.Exit);
        Assert.Contains("coordinate", parent.Output);
        Assert.DoesNotContain("--il-offset", parent.Output);
        Assert.DoesNotContain("--il-offsets", parent.Output);
        Assert.DoesNotContain("--heap", parent.Output);
        Assert.Empty(parent.Error);
        Assert.Equal(0, child.Exit);
        Assert.Contains("<coordinate>", child.Output);
        Assert.Contains("--library", child.Output);
        Assert.Contains("--package", child.Output);
        Assert.Contains("--platform", child.Output);
        Assert.Contains("--file", child.Output);
        Assert.Contains("--metadata-root", child.Output);
        Assert.Contains("#Strings:0x1a4", child.Output);
        Assert.Empty(child.Error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RequiresExactCoordinateOrFile()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "requires one exact coordinate or --file <path>",
            error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsExactCoordinateAndFileBeforeAcquisition()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-library-{Guid.NewGuid():N}.dll");
        string missingCoordinates = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinates-{Guid.NewGuid():N}.txt");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x0",
            "--file",
            missingCoordinates,
            "--library",
            missingLibrary,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "accepts either one exact coordinate or --file, not both",
            error);
        Assert.DoesNotContain(missingLibrary, error);
        Assert.DoesNotContain(missingCoordinates, error);
    }

    [Theory]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    public async Task LibraryCoordinateCommand_FileSupportsEveryTabularFormat(
            string format)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(
            path,
            """
            first 0x06000001+0x1
            second 0x06000001+0x6
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                path,
                "--library",
                TestAssemblyPath,
                format,
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.NotEmpty(output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileUsesPackageRelativeLibrary()
    {
        var (token, callOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        string tempDir = Directory.CreateTempSubdirectory(
            "library-coordinate-file-package-").FullName;
        string content = Path.Combine(tempDir, "content");
        string relativeLibraryPath =
            "lib/net11.0/Coordinate.Package.dll";
        string libraryPath = Path.Combine(
            content,
            "lib",
            "net11.0",
            "Coordinate.Package.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        File.Copy(TestAssemblyPath, libraryPath);
        string packagePath = Path.Combine(
            tempDir,
            "Coordinate.Package.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(content, packagePath);
        string coordinatePath = Path.Combine(
            tempDir,
            "coordinates.txt");
        await File.WriteAllTextAsync(
            coordinatePath,
            $"""
            first-coordinate 0x{token:X8}+0x{callOffset:X}
            second-coordinate 0x{token:X8}+0x{callOffset:X}
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--package",
                packagePath,
                "--library",
                relativeLibraryPath,
                "-n",
                "1",
                "--tail",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains(
                nameof(SemanticFactsFixture.AllSignals),
                output);
            Assert.DoesNotContain("first-coordinate", output);
            Assert.Contains("second-coordinate", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileUsesPlatformLibrary()
    {
        string coordinatePath = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(
            coordinatePath,
            "sample 0x06000001+0x0",
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--platform",
                "System.Text.Json",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("System.HexConverter.FromChar", output);
        }
        finally
        {
            File.Delete(coordinatePath);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FilePreservesSourceRecordOrder()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(
            path,
            """
            first 0x06000001+0x1
            malformed record
            last 0x06000002+0x0
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                path,
                "--library",
                TestAssemblyPath,
                "--json",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            JsonElement[] rows = document.RootElement
                .GetProperty("rows")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Equal("first", rows[0].GetProperty("label").GetString());
            Assert.Equal(
                $"{path}:2",
                rows[1].GetProperty("label").GetString());
            Assert.Equal("last", rows[2].GetProperty("label").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileWindowsSourceRecordOrder()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(
            path,
            """
            malformed first
            middle 0x06000001+0x1
            last 0x06000002+0x0
            """,
            TestContext.Current.CancellationToken);
        try
        {
            string[] request =
            [
                "library",
                "coordinate",
                "--file",
                path,
                "--library",
                TestAssemblyPath,
                "-n",
                "1",
                "--jsonl",
                "--tips",
                "q",
            ];
            var head = await RunAppAsync(
                [.. request, "--head"]);
            var tail = await RunAppAsync(
                [.. request, "--tail"]);

            Assert.Equal(1, head.Exit);
            Assert.Empty(head.Error);
            Assert.Contains(
                $"\"label\":\"{path}:1\"",
                head.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain("\"label\":\"last\"", head.Output);
            Assert.Equal(1, tail.Exit);
            Assert.Empty(tail.Error);
            Assert.Contains("\"label\":\"last\"", tail.Output);
            Assert.DoesNotContain(
                $"\"label\":\"{path}:1\"",
                tail.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryCoordinateCommand_FileBareLimitSelectsRows(
            bool beforeSubcommand)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(
            path,
            """
            first 0x06000001+0x1
            last 0x06000002+0x0
            """,
            TestContext.Current.CancellationToken);
        try
        {
            string[] args =
                beforeSubcommand
                    ?
                    [
                        "library", "-n", "1", "coordinate",
                        "--file", path,
                        "--library", TestAssemblyPath,
                        "--jsonl",
                        "--tips", "q",
                    ]
                    :
                    [
                        "library", "coordinate",
                        "--file", path,
                        "--library", TestAssemblyPath,
                        "-n", "1",
                        "--jsonl",
                        "--tips", "q",
                    ];

            var (exit, output, error) = await RunAppAsync(args);

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("\"label\":\"first\"", output);
            Assert.DoesNotContain("\"label\":\"last\"", output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileLimitFailsBeforeLibraryAcquisition()
    {
        string coordinatePath = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-library-{Guid.NewGuid():N}.dll");
        await File.WriteAllLinesAsync(
            coordinatePath,
            [
                "",
                "# comment",
                .. Enumerable
                    .Range(
                        1,
                        ILOffsetQuery.MaximumCoordinatePopulation + 1)
                    .Select(index => $"malformed-{index}"),
            ],
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--library",
                missingLibrary,
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("1,024-record limit", error);
            Assert.Contains(
                $"{coordinatePath}:1027",
                error,
                StringComparison.Ordinal);
            Assert.DoesNotContain(missingLibrary, error);
        }
        finally
        {
            File.Delete(coordinatePath);
        }
    }

    [Theory]
    [InlineData("--schema")]
    [InlineData("Context: Member")]
    [InlineData("@Context")]
    public async Task LibraryCoordinateCommand_FileStructuralDiscoveryReadsNeitherInput(
            string discovery)
    {
        string missingCoordinates = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinates-{Guid.NewGuid():N}.txt");
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "--file",
            missingCoordinates,
            "--library",
            missingLibrary,
            "-D",
            discovery,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.NotEmpty(output);
        Assert.Empty(error);
        Assert.DoesNotContain(missingCoordinates, output);
        Assert.DoesNotContain(missingLibrary, output);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileEffectiveDiscoveryReadsCoordinateInput()
    {
        string missingCoordinates = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinates-{Guid.NewGuid():N}.txt");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "--file",
            missingCoordinates,
            "--platform",
            "System.Text.Json",
            "-D",
            "Context: Member",
            "--effective",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Coordinate file not found", error);
        Assert.Contains(missingCoordinates, error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileEffectiveDiscoveryPreservesFailureDetails()
    {
        string coordinatePath = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(
            coordinatePath,
            "0x06FFFFFF+0x0",
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--platform",
                "System.Text.Json",
                "-D",
                "@Context",
                "--effective",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("0x06FFFFFF+0x0", error);
            Assert.Contains("Could not resolve member context", error);
            Assert.Contains("MethodDef row", error);
            Assert.DoesNotContain(": could not resolve.", error);
        }
        finally
        {
            File.Delete(coordinatePath);
        }
    }

    [Theory]
    [InlineData("0x06000001+0x0", "Context: Member")]
    [InlineData("#Strings:0x1", "@Context")]
    public async Task LibraryCoordinateCommand_ExactStructuralDiscoveryReadsNoLibrary(
            string coordinate,
            string discovery)
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            coordinate,
            "--library",
            missingLibrary,
            "-D",
            discovery,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.NotEmpty(output);
        Assert.Empty(error);
        Assert.DoesNotContain(missingLibrary, output);
    }

    [Theory]
    [InlineData("Context: Member", "Member")]
    [InlineData("@Context", "Context: Member")]
    public async Task LibraryCoordinateCommand_FileEffectiveDiscoveryRendersDiscovery(
            string discovery,
            string expected)
    {
        string coordinatePath = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(
            coordinatePath,
            "0x06000001+0x0",
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--platform",
                "System.Text.Json",
                "-D",
                discovery,
                "--effective",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Contains(expected, output);
            Assert.DoesNotContain("## IL Coordinates", output);
            Assert.Empty(error);
        }
        finally
        {
            File.Delete(coordinatePath);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileEffectiveDiscoveryUnionsPopulationEvidence()
    {
        string coordinatePath = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(
                coordinatePath,
                "0x06000001+0x0",
                TestContext.Current.CancellationToken);
            var (singleExit, singleOutput, singleError) =
                await RunAppAsync(
                    "library",
                    "coordinate",
                    "--file",
                    coordinatePath,
                    "--platform",
                    "System.Text.Json",
                    "-D",
                    "@Context",
                    "--effective",
                    "--tips",
                    "q");

            Assert.Equal(0, singleExit);
            Assert.Empty(singleError);
            Assert.DoesNotContain(
                "Context: Callsite",
                singleOutput,
                StringComparison.Ordinal);

            await File.WriteAllLinesAsync(
                coordinatePath,
                ["0x06000001+0x0", "0x06000001+0x1"],
                TestContext.Current.CancellationToken);
            var (unionExit, unionOutput, unionError) =
                await RunAppAsync(
                    "library",
                    "coordinate",
                    "--file",
                    coordinatePath,
                    "--platform",
                    "System.Text.Json",
                    "-D",
                    "@Context",
                    "--effective",
                    "--tips",
                    "q");

            Assert.Equal(0, unionExit);
            Assert.Empty(unionError);
            Assert.Contains(
                "Context: Callsite",
                unionOutput,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(coordinatePath);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileEffectiveDiscoveryUnionsHeterogeneousEvidence()
    {
        string coordinatePath = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(
            coordinatePath,
            ["0x06000001+0x2", "0x06000001+0x0"],
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--platform",
                "System.Text.Json",
                "-D",
                "@Context",
                "--effective",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("Context: Member", output);
            Assert.Contains("Context: Instruction", output);
        }
        finally
        {
            File.Delete(coordinatePath);
        }
    }

    [Fact]
    public async Task
        LibraryCoordinateCommand_FileEffectiveDiscoveryRejectsOffsetOutsideMethodExtent()
    {
        string coordinatePath = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(
            coordinatePath,
            "0x06000001+0x7FFFFFFF",
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--platform",
                "System.Text.Json",
                "-D",
                "@Context",
                "--effective",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "outside the decoded method body",
                error,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(coordinatePath);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileEffectiveDiscoveryFiltersNonMemberFields()
    {
        var (token, offset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        string coordinatePath = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(
                coordinatePath,
                $"0x{token:X8}+0x{offset:X}",
                TestContext.Current.CancellationToken);

            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--library",
                TestAssemblyPath,
                "-D",
                "Context: Instruction",
                "--effective",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("| Operand Token | field |", output);
            Assert.DoesNotContain("| Branch Targets | field |", output);
        }
        finally
        {
            File.Delete(coordinatePath);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileEffectiveDiscoveryFiltersListFields()
    {
        int catchToken = typeof(ILOffsetExceptionFixture)
            .GetMethod(nameof(ILOffsetExceptionFixture.TryCatch))!
            .MetadataToken;
        int filterToken = typeof(ILOffsetExceptionFixture)
            .GetMethod(nameof(ILOffsetExceptionFixture.FilteredCatch))!
            .MetadataToken;
        string coordinatePath = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(
                coordinatePath,
                $"0x{catchToken:X8}+0x1",
                TestContext.Current.CancellationToken);
            var (singleExit, singleOutput, singleError) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--library",
                TestAssemblyPath,
                "-D",
                "Context: Exception",
                "--effective",
                "--tips",
                "q");

            Assert.Equal(0, singleExit);
            Assert.Empty(singleError);
            Assert.DoesNotContain("| Filter Range | column |", singleOutput);

            await File.AppendAllTextAsync(
                coordinatePath,
                $"{Environment.NewLine}0x{filterToken:X8}+0x1",
                TestContext.Current.CancellationToken);
            var (unionExit, unionOutput, unionError) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--library",
                TestAssemblyPath,
                "-D",
                "Context: Exception",
                "--effective",
                "--tips",
                "q");

            Assert.Equal(0, unionExit);
            Assert.Empty(unionError);
            Assert.Contains("| Filter Range | column |", unionOutput);
        }
        finally
        {
            File.Delete(coordinatePath);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileEffectiveDiscoveryUnionsScalarFields()
    {
        var (syncToken, syncOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        var (asyncToken, asyncOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AsyncVirtualDispatch),
            ILOpCode.Call);
        string coordinatePath = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(
                coordinatePath,
                $"0x{syncToken:X8}+0x{syncOffset:X}",
                TestContext.Current.CancellationToken);
            var (singleExit, singleOutput, singleError) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--library",
                TestAssemblyPath,
                "-D",
                "Context: Member",
                "--effective",
                "--tips",
                "q");

            Assert.Equal(0, singleExit);
            Assert.Empty(singleError);
            Assert.DoesNotContain("| Async | field |", singleOutput);

            await File.AppendAllTextAsync(
                coordinatePath,
                $"{Environment.NewLine}0x{asyncToken:X8}+0x{asyncOffset:X}",
                TestContext.Current.CancellationToken);
            var (unionExit, unionOutput, unionError) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--library",
                TestAssemblyPath,
                "-D",
                "Context: Member",
                "--effective",
                "--tips",
                "q");

            Assert.Equal(0, unionExit);
            Assert.Empty(unionError);
            Assert.Contains("| Async | field |", unionOutput);

            await File.WriteAllTextAsync(
                coordinatePath,
                $"0x06000001+0x0{Environment.NewLine}0x06000001+0x1",
                TestContext.Current.CancellationToken);
            var (sourceExit, sourceOutput, sourceError) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatePath,
                "--platform",
                "System.Text.Json",
                "-D",
                "Context: Source Location",
                "--effective",
                "--tips",
                "q");

            Assert.Equal(0, sourceExit);
            Assert.Empty(sourceError);
            Assert.Contains("| Matched Offset | field |", sourceOutput);
        }
        finally
        {
            File.Delete(coordinatePath);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileRendersCoordinateSummary()
    {
        var (token, callOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        var (returnToken, returnCallOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.UnsafeAs),
            ILOpCode.Call);
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            $$"""
            # label coordinate
            profiler-sample 0x{{token:X8}}+0x{{callOffset:X}}
            return-address 0x{{returnToken:X8}}+0x{{returnCallOffset + 5:X}}
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "coordinate", "--file", path,
                "--library", TestAssemblyPath, "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("## IL Coordinates", output);
            Assert.Contains("Coordinate", output);
            Assert.Contains("IL Offset", output);
            Assert.Contains("Meaning", output);
            Assert.Contains("Evidence", output);
            Assert.Contains("profiler-sample", output);
            Assert.Contains("callsite", output);
            Assert.Contains("return-address", output);
            Assert.Contains("return address", output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileRejectsMissingFileBeforeLibraryAcquisition()
    {
        string missingCoordinatesPath =
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");
        string missingLibraryPath =
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "--file",
            missingCoordinatesPath,
            "--library",
            missingLibraryPath,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"Coordinate file not found: {missingCoordinatesPath}",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(missingLibraryPath, error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileRejectsMalformedDescriptorBeforeResolvingCoordinates()
    {
        string tempDir = Directory.CreateTempSubdirectory(
            "library-descriptor-direct-").FullName;
        string malformedPath = Path.Combine(tempDir, "Malformed.dll");
        string coordinatesPath =
            Path.Combine(tempDir, "coordinates.txt");
        try
        {
            WriteTruncatedMetadataTableAssembly(
                TestAssemblyPath,
                malformedPath);
            await File.WriteAllTextAsync(
                coordinatesPath,
                "0x06000001+0x0\n",
                TestContext.Current.CancellationToken);

            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatesPath,
                "--library",
                malformedPath,
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(malformedPath, error);
            Assert.Contains(
                "selected managed assembly contains invalid metadata",
                error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_PackageFileRejectsMalformedDescriptorBeforeResolvingCoordinates()
    {
        string tempDir = Directory.CreateTempSubdirectory(
            "library-descriptor-package-").FullName;
        string content = Path.Combine(tempDir, "content");
        string libraryDirectory = Path.Combine(content, "lib", "net11.0");
        Directory.CreateDirectory(libraryDirectory);
        string malformedPath = Path.Combine(
            libraryDirectory,
            "Malformed.dll");
        WriteTruncatedMetadataTableAssembly(
            TestAssemblyPath,
            malformedPath);
        string packagePath = Path.Combine(
            tempDir,
            "Malformed.Package.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(content, packagePath);
        string coordinatesPath =
            Path.Combine(tempDir, "coordinates.txt");
        try
        {
            await File.WriteAllTextAsync(
                coordinatesPath,
                "0x06000001+0x0\n",
                TestContext.Current.CancellationToken);
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatesPath,
                "--package",
                packagePath,
                "--library",
                "Malformed.dll",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("Malformed.dll", error);
            Assert.Contains(
                "selected managed assembly contains invalid metadata",
                error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_PlatformFileRejectsMalformedAssemblyBeforeResolvingCoordinates()
    {
        string? originalDotnetRoot =
            Environment.GetEnvironmentVariable("DOTNET_ROOT");
        string tempDir = Directory.CreateTempSubdirectory(
            "library-descriptor-platform-").FullName;
        const string Version = "999.0.0";
        string runtimeDirectory = Path.Combine(
            tempDir,
            "shared",
            "Microsoft.NETCore.App",
            Version);
        Directory.CreateDirectory(runtimeDirectory);
        string malformedPath = Path.Combine(
            runtimeDirectory,
            "Malformed.Platform.dll");
        WriteTruncatedMetadataTableAssembly(
            TestAssemblyPath,
            malformedPath);
        string coordinatesPath =
            Path.Combine(tempDir, "coordinates.txt");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", tempDir);
            await File.WriteAllTextAsync(
                coordinatesPath,
                "0x06000001+0x0\n",
                TestContext.Current.CancellationToken);
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                coordinatesPath,
                "--platform",
                "Malformed.Platform",
                "--framework",
                "runtime",
                "--version",
                Version,
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(malformedPath, error);
            Assert.Contains(
                "selected managed assembly contains invalid metadata",
                error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_ROOT",
                originalDotnetRoot);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FilePrefersExactOperationIdentity()
    {
        var (allSignalsToken, virtualCallOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        var (_, allocationOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Newarr);
        var (unsafeToken, unsafeCallOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.UnsafeAs),
            ILOpCode.Call);

        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(
            path,
            [
                $"hot-virtual-call 0x{allSignalsToken:X8}+0x{virtualCallOffset:X}",
                $"allocation 0x{allSignalsToken:X8}+0x{allocationOffset:X}",
                $"unsafe-call 0x{unsafeToken:X8}+0x{unsafeCallOffset:X}"
            ],
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "coordinate", "--file", path,
                "--library", TestAssemblyPath, "--json", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            var rows = document.RootElement.GetProperty("rows").EnumerateArray().ToArray();

            var callsite = Assert.Single(rows, row => row.GetProperty("label").GetString() == "hot-virtual-call");
            Assert.Equal("callsite", callsite.GetProperty("meaning").GetString());
            Assert.Contains("virtual dispatch", callsite.GetProperty("evidence").GetString());

            var allocation = Assert.Single(rows, row => row.GetProperty("label").GetString() == "allocation");
            Assert.Equal("allocation", allocation.GetProperty("meaning").GetString());

            var safety = Assert.Single(rows, row => row.GetProperty("label").GetString() == "unsafe-call");
            Assert.Equal("safety", safety.GetProperty("meaning").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileRejectsBadCoordinateLine()
    {
        var (token, callOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            $$"""
            bad debugger frame
            good 0x{{token:X8}}+0x{{callOffset:X}}
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "coordinate", "--file", path,
                "--library", TestAssemblyPath, "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(error);
            Assert.Contains(Path.GetFileName(path), output);
            Assert.Contains("expected a MethodDef token + IL offset coordinate", output);
            Assert.Contains("good", output);
            Assert.Contains("callsite", output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_FileJsonUsesSnakeCaseEnvelope()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "sample 0x06000001+0x1", TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "coordinate", "--file", path,
                "--library", TestAssemblyPath, "--json", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("\"rows\"", output);
            Assert.Contains("\"il_offset\"", output);
            Assert.DoesNotContain("\"ILOffset\"", output);
            Assert.DoesNotContain("\"Coordinate\"", output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_SourceLocationSectionSelectorUsesCoordinate()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json",
            "-S", "Context: Source Location", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Source Location", output);
        Assert.Contains("| Field | Value |", output);
        Assert.Contains("| Method | System.HexConverter.FromChar |", output);
        Assert.Contains("| Token | 0x6000001 |", output);
        Assert.Contains("| IL Offset | 0x0 |", output);
        Assert.Contains("HexConverter.cs", output);
    }

    [Fact]
    public async Task LibraryCommand_LegacyILOffsetSectionSelector_ResolvesSourceLocation()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-S", "IL Offset", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Source Location", output);
        Assert.DoesNotContain("## IL Offset", output);
    }

    [Fact]
    public async Task LibraryCommand_LegacyILOffsetSectionSelector_RequiresFlagParameter()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "IL Offset", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "IL coordinate sections require library coordinate",
            error);
    }

    [Fact]
    public async Task LibraryCommand_LegacyCoordinateAliasInMixedSelection_RequiresFlagParameter()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Member Context,Library Info", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "IL coordinate sections require library coordinate",
            error);
    }

    [Fact]
    public async Task LibraryCommand_CoordinateWildcardInMixedSelection_IsOmitted()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Context: Mem*,Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Library Info", output);
        Assert.DoesNotContain("## Context: Member", output);
        Assert.DoesNotContain(
            "IL coordinate sections require library coordinate",
            error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetDiscovery_IsCoordinateScoped()
    {
        var (withoutExit, withoutOutput, withoutError) = await RunAppAsync(
            "library", "--platform", "System.Text.Json", "-D", "--table", "--tips", "q");
        var (withExit, withOutput, withError) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-D", "--table", "--tips", "q");

        Assert.Equal(0, withoutExit);
        Assert.Equal(0, withExit);
        Assert.Empty(withoutError);
        Assert.Empty(withError);
        Assert.DoesNotContain("Context: Source Location", withoutOutput);
        Assert.DoesNotContain("Context: Member", withoutOutput);
        Assert.DoesNotContain("Context: Instruction", withoutOutput);
        Assert.DoesNotContain("Context: Exception", withoutOutput);
        Assert.DoesNotContain("Context: Callsite", withoutOutput);
        Assert.DoesNotContain("Context: Return Address", withoutOutput);
        Assert.Contains("@Context", withOutput);
        Assert.DoesNotContain("Context: Source Location", withOutput);
        Assert.DoesNotContain("Context: Member", withOutput);
        Assert.DoesNotContain("Context: Instruction", withOutput);

        var (contextExit, contextOutput, contextError) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-D", "@Context", "--table", "--tips", "q");
        Assert.Equal(0, contextExit);
        Assert.Empty(contextError);
        Assert.Contains("Context: Source Location", contextOutput);
        Assert.Contains("Context: Member", contextOutput);
        Assert.Contains("Context: Instruction", contextOutput);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetMemberContext_RendersMemberFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-S", "Context: Member", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Member", output);
        Assert.Contains("| Type | System.HexConverter |", output);
        Assert.Contains("| Type Kind | class |", output);
        Assert.Contains("| Member | System.HexConverter.FromChar |", output);
        Assert.Contains("| Signature | int FromChar(int c) |", output);
        Assert.Contains("| Static | Yes |", output);
        Assert.Contains("| Metadata Token | 0x6000001 |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetMemberContext_ValueProjectsType()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-S", "Context: Member", "--fields", "Type", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("System.HexConverter", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_RendersInstructionFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-S", "Context: Instruction", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Instruction", output);
        Assert.Contains("| IL Offset | 0x0 |", output);
        Assert.Contains("| Boundary | Exact |", output);
        Assert.Contains("| Opcode | ldarg.0 |", output);
        Assert.Contains("| Operand Kind | None |", output);
        Assert.Contains("| Next Offset | 0x1 |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_ValueProjectsOpcode()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-S", "Context: Instruction", "--fields", "Opcode", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("ldarg.0", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_RequiresInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x2",
            "--platform", "System.Text.Json", "-S", "Context: Instruction", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not an instruction boundary", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetBareReport_RequiresInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x2",
            "--platform", "System.Text.Json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not an instruction boundary", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetMemberContext_AllowsNonInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x2",
            "--platform", "System.Text.Json", "-S", "Context: Member", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Member", output);
        Assert.Contains("| Member | System.HexConverter.FromChar |", output);
        Assert.DoesNotContain("## Context: Instruction", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_FormatsFloatOperands()
    {
        var token = typeof(ILOffsetFloatFixture).GetMethod(nameof(ILOffsetFloatFixture.FloatConstant))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", $"0x{token:X}+0x0",
            "--library", TestAssemblyPath, "-S", "Context: Instruction", "--fields", "Operand", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1.5", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetExceptionContext_RendersContainingRegion()
    {
        var token = typeof(ILOffsetExceptionFixture).GetMethod(nameof(ILOffsetExceptionFixture.TryCatch))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", $"0x{token:X}+0x1",
            "--library", TestAssemblyPath, "-S", "Context: Exception", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Exception", output);
        Assert.Contains("| Region | Context | Clause | Try Range | Handler Range |", output);
        Assert.Contains("| 1 | try | catch |", output);
        Assert.Contains("System.DivideByZeroException", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetExceptionContext_ValueProjectsClause()
    {
        var token = typeof(ILOffsetExceptionFixture).GetMethod(nameof(ILOffsetExceptionFixture.TryCatch))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", $"0x{token:X}+0x1",
            "--library", TestAssemblyPath, "-S", "Context: Exception", "--fields", "Clause", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("catch", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCallsiteContext_RendersCallsite()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x1",
            "--platform", "System.Text.Json", "-S", "Context: Callsite", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Callsite", output);
        Assert.Contains("| Call Offset | IL_0001 |", output);
        Assert.Contains("| Opcode | call |", output);
        Assert.Contains("| Call Kind | direct |", output);
        Assert.Contains("| Callee | System.HexConverter::get_CharToHexLookup() |", output);
        Assert.Contains("| Return Address | IL_0006 |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCallsiteContext_ValueProjectsCallee()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x1",
            "--platform", "System.Text.Json", "-S", "Context: Callsite", "--fields", "Callee", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("System.HexConverter::get_CharToHexLookup()", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_RendersPreviousCall()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x6",
            "--platform", "System.Text.Json", "-S", "Context: Return Address", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Return Address", output);
        Assert.Contains("| IL Offset | IL_0006 |", output);
        Assert.Contains("| Call Offset | IL_0001 |", output);
        Assert.Contains("| Opcode | call |", output);
        Assert.Contains("| Callee | System.HexConverter::get_CharToHexLookup() |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_ValueProjectsCallOffset()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x6",
            "--platform", "System.Text.Json", "-S", "Context: Return Address", "--fields", "Call Offset", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("IL_0001", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_RequiresInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x2",
            "--platform", "System.Text.Json", "-S", "Context: Return Address", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not an instruction boundary", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_IgnoresMethodPointerFallthrough()
    {
        var token = typeof(ILOffsetFunctionPointerFixture).GetMethod(nameof(ILOffsetFunctionPointerFixture.CreateDelegate))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", $"0x{token:X}+0x10",
            "--library", TestAssemblyPath, "-S", "Context: Return Address", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Equal(
            "This section (Context: Return Address) produced no output.",
            error.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCount_ReturnsSingletonLocationCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-S", "Context: Source Location", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetContextCountsUseTypedRows()
    {
        var scalar = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json",
            "-S", "Context: Member",
            "--fields", "Type", "--rows", "2..2",
            "--count", "--tips", "q");
        var map = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json",
            "-S", "Context: Member,Context: Instruction",
            "--count", "--json", "--tips", "q");

        Assert.Equal(0, scalar.Exit);
        Assert.Equal("0", scalar.Output.Trim());
        Assert.Empty(scalar.Error);

        Assert.Equal(0, map.Exit);
        Assert.Empty(map.Error);
        using var document = JsonDocument.Parse(map.Output);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.All(
            document.RootElement.EnumerateArray(),
            row => Assert.Equal(1, row.GetProperty("count").GetInt32()));
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCountPreservesProjectionKind()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json",
            "-S", "Context: Member,Performance: Boxing",
            "--columns", "Member",
            "--count", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var counts = document.RootElement
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("section").GetString()!,
                row => row.GetProperty("count").GetInt32());
        Assert.Equal(0, counts["Context: Member"]);
        Assert.True(counts["Performance: Boxing"] > 0);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCountPreservesStructuralWildcard()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json",
            "-S", "Context: Member",
            "--columns", "*",
            "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetExceptionContextCountsTypedRows()
    {
        var method = typeof(ILOffsetExceptionFixture).GetMethod(
            nameof(ILOffsetExceptionFixture.NestedTryCatch))!;
        var body = method.GetMethodBody()!;
        int offset = Enumerable.Range(0, body.GetILAsByteArray()!.Length)
            .First(candidate => body.ExceptionHandlingClauses.Count(
                clause => candidate >= clause.TryOffset
                    && candidate < clause.TryOffset + clause.TryLength) > 1);
        int expectedRows = body.ExceptionHandlingClauses.Count(
            clause => offset >= clause.TryOffset
                && offset < clause.TryOffset + clause.TryLength);
        string coordinate = $"0x{method.MetadataToken:X}+0x{offset:X}";

        var scalar = await RunAppAsync(
            "library", "coordinate", coordinate,
            "--library", TestAssemblyPath,
            "-S", "Context: Exception",
            "--count", "--tips", "q");
        var windowed = await RunAppAsync(
            "library", "coordinate", coordinate,
            "--library", TestAssemblyPath,
            "-S", "Context: Exception",
            "--rows", "2..2",
            "--count", "--tips", "q");

        Assert.Equal(0, scalar.Exit);
        Assert.Equal(
            expectedRows.ToString(CultureInfo.InvariantCulture),
            scalar.Output.Trim());
        Assert.Empty(scalar.Error);

        Assert.Equal(0, windowed.Exit);
        Assert.Equal("1", windowed.Output.Trim());
        Assert.Empty(windowed.Error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetValue_ProjectsResolvedLine()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-S", "Context: Source Location", "--fields", "Line", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"^\d+$", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetPrint_PrintsResolvedSourceLine()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-S", "Context: Source Location", "--print", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Context: Source Location", output);
        Assert.Contains("CharToHexLookup", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetPrintJsonArray_EmitsPrintableDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-S", "Context: Source Location", "--print", "--json-array", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("[", output.Trim());
        Assert.Contains("\"section\":\"Context: Source Location\"", output);
        Assert.Contains("\"label\":\"System.HexConverter.FromChar\"", output);
        Assert.Contains("CharToHexLookup", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCountRejectsPrint()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-S", "Context: Source Location", "--count", "--print", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--count cannot be combined with --print", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetPrint_DoesNotReadLocalPdbPath()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "secret-local-file-line", TestContext.Current.CancellationToken);
            var result = new ILOffsetProjection
            {
                Method = "Attacker.Method",
                File = tempFile,
                Line = 1
            };

            var (content, error) = await LibraryCommand.ReadILOffsetSourceLineForTestsAsync(result);

            Assert.Null(content);
            Assert.Contains("no printable source body", error);
            Assert.DoesNotContain("secret-local-file-line", error);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetSectionSelector_RequiresFlagParameter()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Context: Source Location", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains(
            "IL coordinate sections require library coordinate",
            error);
    }

    [Theory]
    [InlineData("@Context")]
    [InlineData("Context:*")]
    public async Task LibraryCommand_IlOffsetOnlySelectionWithoutValue_DoesNotBecomeDefaultView(
            string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", selector, "--json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "IL coordinate sections require library coordinate",
            error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetParameterizedSectionSelector_IsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Context: Source Location:0x06000001+0x0", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains(
            "IL coordinate parameters belong in the coordinate argument",
            error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetWildcardSelectionWithoutValue_DoesNotRequireFlag()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "*", "-n", "8", "--lines", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("IL coordinate sections require", error);
        Assert.Contains("##", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetFlag_ErrorsWhenSelectedSectionsExcludeILOffset()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", "coordinate", "0x06000001+0x0",
            "--platform", "System.Text.Json", "-S", "Library Info", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains(
            "library coordinate requires an IL coordinate section",
            error);
    }
}
