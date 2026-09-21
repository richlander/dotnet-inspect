using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using DotnetInspector.Cache;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspect.Cli.Sections;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task ReferenceHierarchyCountUsesSemanticRows_WhilePackageScalarIgnoresTreePresentation()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (libraryExit, libraryOutput, libraryError) = await RunAppAsync(
                "library", "System.Text.Json",
                "-S", "Reference Hierarchy", "--count", "--depth", "2",
                "--tips", "q");
            var (packageExit, packageOutput, packageError) = await RunAppAsync(
                "package", packagePath,
                "-S", "Target Frameworks", "--count", "--tips", "q");
            var (multiPackageExit, multiPackageOutput, multiPackageError) =
                await RunAppAsync(
                    "package", packagePath, packagePath,
                    "-S", "Target Frameworks", "--count", "--tips", "q");
            var (mapExit, mapOutput, mapError) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Package Info,Target Frameworks",
                "--count", "--tips", "q");

            Assert.Equal(0, libraryExit);
            Assert.True(
                int.TryParse(
                    libraryOutput.Trim(),
                    CultureInfo.InvariantCulture,
                    out int libraryCount));
            Assert.True(libraryCount > 0);
            Assert.Empty(libraryError);

            Assert.Equal(0, packageExit);
            Assert.Empty(packageError);
            Assert.True(
                int.TryParse(
                    packageOutput.Trim(),
                    CultureInfo.InvariantCulture,
                    out var packageCount),
                packageOutput);
            Assert.True(packageCount > 0);

            Assert.Equal(0, multiPackageExit);
            Assert.Empty(multiPackageError);
            Assert.Equal(
                packageCount * 2,
                int.Parse(
                    multiPackageOutput.Trim(),
                    CultureInfo.InvariantCulture));

            Assert.Equal(0, mapExit);
            Assert.Empty(mapError);
            Assert.Contains("Package Info", mapOutput);
            Assert.Contains("Target Frameworks", mapOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Rows_TailWindowDiffersFromHeadWindow()
    {
        // A real command must wire ParseRows and honor the tail branch: the last-N
        // window selects a different endpoint than the first-N window.
        var head = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2", "--tsv", "--tips", "q");
        var tail = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2", "--tail", "--tsv", "--tips", "q");

        Assert.Equal(0, head.Exit);
        Assert.Equal(0, tail.Exit);
        Assert.Empty(head.Error);
        Assert.Empty(tail.Error);
        var headLines = head.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var tailLines = tail.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // header + exactly 2 data rows in each.
        Assert.Equal(3, headLines.Length);
        Assert.Equal(3, tailLines.Length);
        // Same header row, different data rows.
        Assert.Equal(headLines[0], tailLines[0]);
        Assert.NotEqual(headLines[1], tailLines[1]);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1..1")]
    public async Task Rows_LineTailSugarDoesNotChangeSemanticWindow(
        string rows)
    {
        string[] arguments =
        [
            "type",
            "System.String",
            "-S",
            "Member Index",
            "--rows",
            rows,
            "--tsv",
            "--tips",
            "q",
            "-n",
            "1000",
        ];

        var tailLines = await RunAppAsync(
            [.. arguments, "--tail-lines"]);
        var linesTail = await RunAppAsync(
            [.. arguments, "--lines", "--tail"]);

        Assert.Equal(0, tailLines.Exit);
        Assert.Equal(tailLines.Exit, linesTail.Exit);
        Assert.Empty(tailLines.Error);
        Assert.Empty(linesTail.Error);
        Assert.Equal(tailLines.Output, linesTail.Output);
    }

    [Fact]
    public async Task Rows_EqualsSyntaxAppliesTheWindow()
    {
        // The =-syntax reaches the option value by a different path than a separate
        // token, and the arg-preprocessor token scan does not see it. Reading the
        // parse result rather than the raw tokens is what keeps the two equivalent.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows=2", "--tail", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(3, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Rows_RejectsBothHeadAndTail()
    {
        // --head and --tail name opposite ends, so asking for both is a contradiction
        // rather than a narrower window. The =-syntax spelling must be caught too.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows=3", "--head", "--tail", "--tsv", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--head and --tail select opposite ends", error, StringComparison.Ordinal);
        // Pin the failure shape, not just the message: a swallowed BuildRowWindow
        // throw would dump a stack trace that also contains the message and exits 1.
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_FollowedByAnotherOption_BlamesTheMissingValueNotTheOption()
    {
        // Bare --rows used to mean "interpret -n as rows", which put the count on a
        // different flag than the unit. It is now an error -- but System.CommandLine
        // hands a required-argument option the next token regardless, so the spec
        // arrives as "--tsv". The error must name the missing selection rather than
        // sending a reader off to fix the spelling of --tsv.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "--tsv", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--rows requires a row selection, but '--tsv' is another option", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_AtTheEndOfTheCommandLine_ReportsTheMissingValue()
    {
        // Nothing follows --rows here, so System.CommandLine has no token to bind and
        // reports the missing argument itself. The validator must not read the value
        // in this state: doing so throws out of the validator, which surfaced as a
        // stack trace *and* exit code 0 -- a failure invisible to any caller checking
        // the exit code.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Required argument missing for option: '--rows'", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_RangeSelectsTheRowsItNames_NotACountFromTheStart()
    {
        // The distinction the grammar exists for. Over the same table, `4` takes the
        // first four rows and `2..4` takes three rows starting at the second, so the
        // two must not resolve to the same window.
        var count = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "4", "--tsv", "--tips", "q");
        var range = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2..4", "--tsv", "--tips", "q");

        Assert.Equal(0, count.Exit);
        Assert.Equal(0, range.Exit);
        var countLines = count.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var rangeLines = range.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(5, countLines.Length);   // header + 4
        Assert.Equal(4, rangeLines.Length);   // header + 3
        // The range starts one row later, so its first data row is the count's second.
        Assert.Equal(countLines[2], rangeLines[1]);
    }

    [Fact]
    public async Task Rows_StartPlusCountTakesOneMoreRowThanTheSameDigitsAsARange()
    {
        // 2..4 is three rows and 2+4 is four; identical digits, different extents.
        var range = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2..4", "--tsv", "--tips", "q");
        var plus = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2+4", "--tsv", "--tips", "q");

        Assert.Equal(0, range.Exit);
        Assert.Equal(0, plus.Exit);
        Assert.Equal(4, range.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(5, plus.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Rows_RejectsADirectionOnARange()
    {
        // A range already says which rows to keep, so a direction is not a narrower
        // request but a second, conflicting answer to the same question.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2..4", "--tail", "--tsv", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("already names which rows to keep", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_ExplainsTheColonFormRatherThanFailingToParseIt()
    {
        // 2:10 carries Python slice semantics (0-based, end-exclusive) and would
        // differ from 2..10 by a row at each edge, so a generic parse error would
        // leave a reader thinking the digits were wrong rather than the operator.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2:10", "--tsv", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("':'", error, StringComparison.Ordinal);
        Assert.Contains("2..10 is nine rows", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValuedTailFlag_IsReportedAsAMigration_NotBoundAsAPositional()
    {
        // --tail used to carry the count. It is now a bool, so `--tail 20` would
        // otherwise leave "20" to bind as a positional and send the command looking
        // for a package by that name -- a confusing failure at an unrelated task.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--tail", "20", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("'--tail 20' is no longer valid", error, StringComparison.Ordinal);
        Assert.Contains("-n 20 --tail", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValuedTailFlag_AfterEndOfOptions_IsNotReportedAsAMigration()
    {
        // After `--` everything is positional, so `--tail` is a literal argument and
        // not a direction flag at all. The migration guard scans raw tokens, so it
        // would otherwise claim a stale spelling for something that was never a flag.
        var (exit, output, error) = await RunAppAsync(
            "library", "--", "--tail", "5", "--tips", "q");

        Assert.DoesNotContain("is no longer valid", error, StringComparison.Ordinal);
        Assert.DoesNotContain("is no longer valid", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatedTailValues_ReportBoundedParseError()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "Foo",
            "-n1",
            "--tail-lines=false",
            "--tail-lines=true",
            "--help");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--tail-lines does not accept a value",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "InvalidOperationException",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "at DotnetInspector",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatedShorthandUsesNormalDuplicateOptionValidation()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "Foo",
            "-1",
            "-1",
            "--help");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Option '-n' expects a single argument but 2 were provided",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShorthandAfterConcatenatedRequiredValueUsesNormalDuplicateOptionValidation()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "Foo",
            "-o-1",
            "-1",
            "-1",
            "--help");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Option '-n' expects a single argument but 2 were provided",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--value")]
    [InlineData("--urls")]
    [InlineData("--paths")]
    [InlineData("--print")]
    public async Task WholeSurfaceListing_DroppedProjection_FailsLoudly(string projectionFlag)
    {
        // The whole-surface type listing is a name table that exposes no printable payload, so a
        // payload projection cannot be honored. It used to render the full unprojected listing and
        // then trip the audit (#3390); it now (#3386) rejects the projection up front with a
        // targeted message, before rendering, so the audit never has to fire a second line.
        var (exit, output, error) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-S", "Classes", projectionFlag);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not supported when listing types", error);
        Assert.Contains(projectionFlag, error);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task ProjectionAudit_DoesNotFireForHelp()
    {
        // --help short-circuits rendering, so the projection is not dropped; it is moot.
        var (exit, _, error) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-S", "Classes", "--value", "--help");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task ProjectionAudit_DoesNotFireWhenProjectionIsRejected()
    {
        // A command that rejects an unsupported projection has already reported the problem;
        // the audit must not add a second, misleading "this is a bug" line on top of it.
        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Signals", "--urls");

        Assert.Equal(1, exit);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task ProjectionAudit_DoesNotFireForHonoredCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "References", "--count");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("produced unprojected output", error);
        Assert.True(int.TryParse(output.Trim(), out _), $"expected a bare count, got: {output}");
    }

    [Fact]
    public async Task ColumnProjectionWithValue_UnderJson_StillComposes()
    {
        // #3386 boundary: a scalar payload projection composes with --json (--fields picks which
        // column feeds --value), so this must remain honored rather than swept into the rejection.
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Library Info", "--fields", "Assembly Version", "--value", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("\"value\"", output);
        Assert.DoesNotContain("cannot be combined with --json", error);
    }

    [Fact]
    public void ProjectedJsonRoutingAudit_InventoryIncludesEveryProjectionCapableCommand()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var routes = ProjectionCapableRoutes(root)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "depends",
                "ecosystem",
                "extensions",
                "find",
                "graph libraries",
                "implements",
                "library",
                "library coordinate",
                "member",
                "package",
                "package activity",
                "package query",
                "project",
                "type",
                "vocabulary",
            },
            routes);

        static IEnumerable<string> ProjectionCapableRoutes(Command root)
        {
            return Visit(root, "", []);

            static IEnumerable<string> Visit(
                Command parent,
                string parentPath,
                HashSet<string> inheritedOptions)
            {
                foreach (var command in parent.Subcommands)
                {
                    var path = parentPath.Length == 0
                        ? command.Name
                        : $"{parentPath} {command.Name}";
                    var effectiveOptions = new HashSet<string>(
                        inheritedOptions,
                        StringComparer.Ordinal);
                    effectiveOptions.UnionWith(
                        command.Options.Select(option => option.Name));

                    if (effectiveOptions.Contains("--json")
                        && (effectiveOptions.Contains("--fields")
                            || effectiveOptions.Contains("--columns")))
                    {
                        yield return path;
                    }

                    foreach (var descendant in Visit(
                        command,
                        path,
                        effectiveOptions))
                    {
                        yield return descendant;
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData("library")]
    [InlineData("implements")]
    [InlineData("extensions")]
    public async Task ProjectedJsonRoutingAudit_UnadoptedTypedDocumentFailsClosed(string command)
    {
        string[] args = command switch
        {
            "library" =>
                [command, TestAssemblyPath, "-S", "Library Info", "--fields", "Assembly Version", "--json", "--tips", "q"],
            "implements" =>
                [command, "IDisposable", "--library", TestAssemblyPath, "--columns", "Type", "--json", "--tips", "q"],
            "extensions" =>
                [command, "String", "--library", TestAssemblyPath, "--columns", "Method", "--json", "--tips", "q"],
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        var (exit, output, error) = await RunAppAsync(args);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("requires lowered JSON", error);
        Assert.Contains("does not support yet", error);
    }

    [Theory]
    [InlineData("--versions", "--print")]
    [InlineData("--versions-with-feed", "--value")]
    [InlineData("--version", "--urls")]
    [InlineData("--tfms", "--paths")]
    [InlineData("--layout", "--print")]
    [InlineData("--content", "--value")]
    public async Task ProjectedJsonRoutingAudit_PackageLensPayloadFailsBeforeAcquisition(
        string lens,
        string projection)
    {
        var target = lens is "--version" or "--versions" or "--versions-with-feed"
            ? "ThisQueryMustNotReachTheNetwork"
            : Path.Combine(
                Path.GetTempPath(),
                "ThisPackageMustNotBeAcquired.nupkg");
        var args = new List<string> { "package", target, lens, projection };
        if (lens == "--content")
            args.Insert(2, "--path=README.md");

        var (exit, output, error) = await RunAppAsync([.. args]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"{projection} is not available with {lens}", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("--versions")]
    [InlineData("--versions-with-feed")]
    [InlineData("--version")]
    [InlineData("--tfms")]
    [InlineData("--layout")]
    [InlineData("--content")]
    public async Task ProjectedJsonRoutingAudit_PackageLensFieldsFailBeforeAcquisition(
        string lens)
    {
        var target = lens is "--version" or "--versions" or "--versions-with-feed"
            ? "ThisQueryMustNotReachTheNetwork"
            : Path.Combine(
                Path.GetTempPath(),
                "ThisPackageMustNotBeAcquired.nupkg");
        var args = new List<string>
        {
            "package",
            target,
            lens,
            "--tsv",
            "--columns=ZZZBogus",
        };
        if (lens == "--content")
            args.Insert(2, "--path=README.md");

        var (exit, output, error) = await RunAppAsync([.. args]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"--fields/--columns are not available with {lens}",
            error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_PackageLensRoutesFailClosed()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var versions = await RunAppAsync(
                "package", "ThisQueryMustNotReachTheNetwork",
                "--versions", "-n", "1", "--json", "--columns", "Version", "--tips", "q");
            var tfms = await RunAppAsync(
                "package", packagePath,
                "--tfms", "--json", "--columns", "TFM", "--tips", "q");
            var layout = await RunAppAsync(
                "package", packagePath,
                "--layout", "--json", "--columns", "Path", "--tips", "q");

            foreach (var (lens, result) in new[]
            {
                ("--versions", versions),
                ("--tfms", tfms),
                ("--layout", layout),
            })
            {
                Assert.Equal(1, result.Exit);
                Assert.Empty(result.Output);
                Assert.Contains(
                    $"--fields/--columns are not available with {lens}",
                    result.Error);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_LibraryCoordinateFileFailsClosed()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(
            path,
            "sample 0x06000001+0x0",
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                "--file",
                path,
                "--platform",
                "System.Text.Json",
                "--json",
                "--columns",
                "Member",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("requires lowered JSON", error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_LibraryDiscoveryOwnsProjectedJson()
    {
        var projected = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", "Library Info", "--json", "--columns", "Name", "--tips", "q");
        var wildcard = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", "Library Info", "--json", "--columns", "Nam*", "--rows", "1", "--tips", "q");
        var allColumns = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", "Library Info", "--json", "--columns", "*", "--rows", "1", "--tips", "q");
        var overlapping = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", "Library Info", "--json",
            "--fields", "Nam*", "--columns", "N*", "--rows", "1", "--tips", "q");
        var invalid = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", "Library Info", "--json", "--columns", "NoSuchColumn", "--tips", "q");

        AssertProjectedProperties(projected, ["name"]);
        AssertProjectedProperties(wildcard, ["name"]);
        AssertProjectedProperties(allColumns, ["name", "kind"]);
        AssertProjectedProperties(overlapping, ["name"]);

        Assert.Equal(1, invalid.Exit);
        Assert.Empty(invalid.Output);
        Assert.Contains("NoSuchColumn", invalid.Error);

        static void AssertProjectedProperties(
            (int Exit, string Output, string Error) result,
            string[] expected)
        {
            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            using var document = JsonDocument.Parse(result.Output);
            Assert.NotEmpty(document.RootElement.EnumerateArray());
            Assert.All(
                document.RootElement.EnumerateArray(),
                row => Assert.Equal(
                    expected,
                    row.EnumerateObject().Select(property => property.Name)));
        }
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_EmptyEffectiveDiscoveryValidatesProjection()
    {
        var invalid = await RunAppAsync(
            "type", "SampleClassForTesting", "--library", TestAssemblyPath,
            "-D", "Custom Attributes", "--json", "--columns", "NoSuchColumn", "--tips", "q");
        var valid = await RunAppAsync(
            "type", "SampleClassForTesting", "--library", TestAssemblyPath,
            "-D", "Custom Attributes", "--json", "--columns", "Name", "--tips", "q");

        Assert.Equal(1, invalid.Exit);
        Assert.Empty(invalid.Output);
        Assert.Contains("NoSuchColumn", invalid.Error);
        Assert.DoesNotContain("has no data", invalid.Error);

        Assert.Equal(0, valid.Exit);
        Assert.Equal("[]", valid.Output.Trim());
        Assert.Contains(
            "section 'Custom Attributes' has no data for this query",
            valid.Error);
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_EffectiveDiscoveryProjectionPreservesRows()
    {
        var count = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", "--json", "--count", "--tips", "q");
        var typed = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", "--json", "--tips", "q");
        var projected = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", "--json", "--columns", "Name", "--tips", "q");
        var structural = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", "--schema", "--json", "--tips", "q");

        foreach (var result in new[] { count, typed, projected, structural })
        {
            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
        }

        using var typedDocument = JsonDocument.Parse(typed.Output);
        using var projectedDocument = JsonDocument.Parse(projected.Output);
        using var structuralDocument = JsonDocument.Parse(structural.Output);
        var typedNames = typedDocument.RootElement.EnumerateArray()
            .Select(row => row.GetProperty("name").GetString())
            .ToArray();
        var projectedNames = projectedDocument.RootElement.EnumerateArray()
            .Select(row => row.GetProperty("name").GetString())
            .ToArray();
        var structuralNames = structuralDocument.RootElement.EnumerateArray()
            .Select(row => row.GetProperty("name").GetString())
            .ToArray();

        Assert.Equal(typedNames.Length, int.Parse(count.Output, CultureInfo.InvariantCulture));
        Assert.Equal(typedNames, projectedNames);
        Assert.Contains(
            structuralNames,
            name => name is not null
                && name.StartsWith('@')
                && !typedNames.Contains(name));
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_NarrowedDiscoveryOwnsProjectionValidation()
    {
        var library = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", "Library Info", "--effective",
            "--json", "--columns", "Kind", "--tips", "q");
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.NarrowedDiscovery",
            "README.md",
            "# Test package");
        try
        {
            var package = await RunAppAsync(
                "package", packagePath,
                "-D", "-S", "Package Info",
                "--json", "--columns", "Kind", "--tips", "q");

            foreach (var result in new[] { library, package })
            {
                Assert.Equal(0, result.Exit);
                Assert.Empty(result.Error);
                using var document = JsonDocument.Parse(result.Output);
                Assert.NotEmpty(document.RootElement.EnumerateArray());
                Assert.All(
                    document.RootElement.EnumerateArray(),
                    row => Assert.Equal(
                        ["kind"],
                        row.EnumerateObject().Select(property => property.Name)));
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProjectionAudit_NestedInvocationDoesNotDiscardOuterRequest()
    {
        // Invocations nest: the router invokes the command it rewrites to. An inner invocation
        // must not consume the outer one's request, or the outer verify finds nothing to check
        // and a dropped projection escapes.
        var root = CommandLineBuilder.CreateRootCommand();
        var outer = root.Parse(["library", TestAssemblyPath, "-S", "References", "--count"]);
        var inner = root.Parse(["library", TestAssemblyPath, "-S", "References"]);
        var diagnostics = new List<string>();

        try
        {
            using (ProjectionAudit.BeginRequest(outer))
            {
                using (ProjectionAudit.BeginRequest(inner))
                {
                    Assert.Equal(0, ProjectionAudit.Verify(0, diagnostics.Add));
                    Assert.Empty(diagnostics);
                }

                Assert.Equal(1, ProjectionAudit.Verify(0, diagnostics.Add));
            }

            Assert.Contains("--count", Assert.Single(diagnostics));
        }
        finally
        {
            ProjectionAudit.ResetForTesting();
        }
    }

    [Fact]
    public void ProjectionAudit_TracksProjectionDeclaredByAnAncestorCommand()
    {
        // `package --count search <id>` binds --count to the parent command, which the parser
        // accepts. Inspecting only the executing command missed it, so the subcommand rendered
        // its full payload and exited 0 with the projection silently discarded.
        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(["package", "--count", "search", "Newtonsoft.Json"]);
        var diagnostics = new List<string>();

        try
        {
            ProjectionAudit.BeginRequest(result);

            Assert.Equal(1, ProjectionAudit.Verify(0, diagnostics.Add));
            Assert.Contains("--count", Assert.Single(diagnostics));
        }
        finally
        {
            ProjectionAudit.ResetForTesting();
        }
    }

    [Fact]
    public void ProjectionAudit_RejectsConflictDeclaredByAnAncestorCommand()
    {
        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(["package", "--count", "--print", "search", "Newtonsoft.Json"]);
        var diagnostics = new List<string>();

        Assert.False(ProjectionAudit.ValidateExclusive(result, diagnostics.Add));
        Assert.Contains("--count cannot be combined with --print", Assert.Single(diagnostics));
    }

    [Fact]
    public void ProjectionAudit_WrongFlagDoesNotSatisfyRequest()
    {
        // The print writer also serves --bare, so an untyped "honored" signal would let it
        // satisfy an unrelated recorded --count and let that drop escape.
        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(["library", TestAssemblyPath, "-S", "References", "--count"]);
        var diagnostics = new List<string>();

        try
        {
            ProjectionAudit.BeginRequest(result);
            ProjectionAudit.MarkHonored(ProjectionAudit.Print);

            Assert.Equal(1, ProjectionAudit.Verify(0, diagnostics.Add));
            Assert.Contains("--count", Assert.Single(diagnostics));
        }
        finally
        {
            ProjectionAudit.ResetForTesting();
        }
    }

    [Fact]
    public void ProjectionAudit_MatchingFlagSatisfiesRequest()
    {
        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(["library", TestAssemblyPath, "-S", "References", "--count"]);
        var diagnostics = new List<string>();

        try
        {
            ProjectionAudit.BeginRequest(result);
            ProjectionAudit.MarkHonored(ProjectionAudit.Count);

            Assert.Equal(0, ProjectionAudit.Verify(0, diagnostics.Add));
            Assert.Empty(diagnostics);
        }
        finally
        {
            ProjectionAudit.ResetForTesting();
        }
    }

    [Fact]
    public void ProjectionAudit_HelpTokenAsOptionValueDoesNotDisableAudit()
    {
        // '/h' here is the value of --type, not a help request. Matching raw token text
        // rather than option tokens would silently disable the audit for the invocation.
        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(["type", "--library", TestAssemblyPath, "-S", "Classes", "--value", "--type", "/h"]);
        var diagnostics = new List<string>();

        try
        {
            ProjectionAudit.BeginRequest(result);

            Assert.Equal(1, ProjectionAudit.Verify(0, diagnostics.Add));
            Assert.Contains("--value", Assert.Single(diagnostics));
        }
        finally
        {
            ProjectionAudit.ResetForTesting();
        }
    }

    [Fact]
    public async Task ProjectionFlags_AreMutuallyExclusive()
    {
        // Two projections cannot both shape one payload, so honoring either one would
        // discard the other.
        var (exit, output, error) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-S", "Classes", "--count", "--print");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--count cannot be combined with --print", error);
    }

    // ---- Lens-mode payload projections (issues #3395, #3396, #3398) ----
    //
    // Each of these modes renders its own payload and returns before the section pipeline, so
    // the pipeline's projection dispatch never runs for them. Every test here asserts the
    // projected payload rather than just a zero exit: the defect being guarded against is an
    // accepted projection that produces well-formed but unprojected output.

    [Fact]
    public async Task Discover_Count_CountsDiscoveredRows()
    {
        var (listExit, listOutput, _) = await RunAppAsync("project", "-D", "");
        Assert.Equal(0, listExit);
        // Data rows only: the markdown table adds a header and a separator line.
        var expected = listOutput.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.StartsWith("| ", StringComparison.Ordinal)) - 2;
        Assert.True(expected > 0, "Discovery must list rows for this test to prove anything.");

        var (exit, output, error) = await RunAppAsync("project", "-D", "", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(expected, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Discover_Count_CountsDiscoveredRowsForLibrary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "", "--count", "-S", "Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.Parse(output.Trim(), CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task Discover_FilteredUnboundedSection_DoesNotExecuteItsQuery()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", SectionNames.TopLeverage,
            "--count",
            "-S", SectionNames.TopLeverage,
            "--trace",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.True(int.Parse(output.Trim(), CultureInfo.InvariantCulture) > 0);
        Assert.Contains("trace: library", error);
        Assert.DoesNotContain(TopLeverageQuery.Definition.Name, error);
        Assert.DoesNotContain("body index", error);
        Assert.DoesNotContain("drill map", error);
    }

    [Fact]
    public async Task Discover_UnsafeMembers_UsesPresenceProbeWithoutExecutingFullQuery()
    {
        string assemblyPath = typeof(InstructionProducer).Assembly.Location;
        var (renderExit, renderOutput, renderError) = await RunAppAsync(
            "library", assemblyPath,
            "-S", SectionNames.UnsafeMembers,
            "--count",
            "--tips", "q");

        Assert.Equal(0, renderExit);
        Assert.Empty(renderError);
        Assert.True(
            int.Parse(renderOutput.Trim(), CultureInfo.InvariantCulture) > 0,
            "The fixture must contain body-only unsafe evidence for discovery to preserve.");

        var (exit, output, error) = await RunAppAsync(
            "library", assemblyPath,
            "-D", SectionNames.UnsafeMembers,
            "--count",
            "-S", SectionNames.UnsafeMembers,
            "--trace",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.True(int.Parse(output.Trim(), CultureInfo.InvariantCulture) > 0);
        Assert.Contains("trace: library", error);
        Assert.DoesNotContain(UnsafeEvidenceQuery.Definition.Name, error);
        Assert.DoesNotContain("body index", error);

        var (bareExit, bareOutput, bareError) = await RunAppAsync(
            "library", assemblyPath,
            "-D",
            "--trace",
            "--tips", "q");

        Assert.Equal(0, bareExit);
        Assert.Contains(
            $"| {SectionNames.UnsafeMembers} | section |",
            bareOutput);
        Assert.Contains(
            UnsafeEvidencePresenceQuery.Definition.Name,
            bareError);
        Assert.DoesNotContain("body index", bareError);
    }

    [Fact]
    public async Task Discover_Bare_OmitsUnsafeMembersWhenMethodBodiesHaveNoUnsafeEvidence()
    {
        var (assemblyPath, _, fixtureDir) =
            CreateNoSourceLinkDiscoveryAssembly();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", assemblyPath,
                "-D",
                "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.DoesNotContain(
                $"| {SectionNames.UnsafeMembers} | section |",
                output);
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Discover_Bare_FailsVisiblyWhenUnsafePresenceIsIncomplete()
    {
        var (assemblyPath, fixtureDir) =
            CreateIncompleteUnsafeDiscoveryAssembly();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                assemblyPath,
                "-D",
                "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                $"Could not determine {SectionNames.UnsafeMembers} applicability",
                error);
            Assert.Contains(
                "Unsafe evidence presence is incomplete",
                error);
        }
        finally
        {
            Directory.Delete(
                fixtureDir,
                recursive: true);
        }
    }

    [Fact]
    public async Task Discover_Bare_DoesNotLoadAdjacentSourceLinkPdb()
    {
        string assemblyPath =
            FixtureCatalog.SourceLinkNormalized.AssemblyPath();
        using (var sourceLink =
            ILInspector.SourceLink.SourceLinkService.Open(
                assemblyPath))
        {
            Assert.True(
                sourceLink.HasSourceLink,
                "The adjacent fixture PDB must carry SourceLink for this gate to prove it stays unloaded.");
        }

        var (exit, output, error) = await RunAppAsync(
            "library", assemblyPath,
            "-D",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("@SourceLink", output);
    }

    [Fact]
    public async Task Discover_Bare_PreservesEmbeddedSourceLinkDoor()
    {
        var (assemblyPath, fixtureDir) =
            CreateEmbeddedSourceLinkDiscoveryAssembly();
        try
        {
            var (markdownExit, markdown, markdownError) =
                await RunAppAsync(
                    "library", assemblyPath,
                    "-D",
                    "--tips", "q");
            var (jsonExit, json, jsonError) =
                await RunAppAsync(
                    "library", assemblyPath,
                    "-D",
                    "--json",
                    "--tips", "q");
            var (treeExit, tree, treeError) =
                await RunAppAsync(
                    "library", assemblyPath,
                    "-D",
                    "--tree",
                    "--tips", "q");

            Assert.Equal(0, markdownExit);
            Assert.Empty(markdownError);
            Assert.Contains(
                "| @SourceLink | category |",
                markdown);

            Assert.Equal(0, jsonExit);
            Assert.Empty(jsonError);
            using JsonDocument document =
                JsonDocument.Parse(json);
            Assert.Contains(
                document.RootElement.EnumerateArray(),
                item =>
                    item.GetProperty("name").GetString()
                        == "@SourceLink"
                    && item.GetProperty("kind").GetString()
                        == "category");

            Assert.Equal(0, treeExit);
            Assert.Empty(treeError);
            Assert.Contains("@SourceLink", tree);
            Assert.Contains(
                SectionNames.SourceLinkAvailability,
                tree);
        }
        finally
        {
            Directory.Delete(
                fixtureDir,
                recursive: true);
        }
    }

    [Fact]
    public async Task Discover_BareEffective_IgnoresLegacyEffectiveCache()
    {
        const string legacyCategory = "effective-v28";
        const string currentCategory = "effective-v29";
        string directory = Path.Combine(
            Path.GetTempPath(), $"effective-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "Instructions.dll");
        File.Copy(typeof(InstructionProducer).Assembly.Location, assemblyPath);

        string hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assemblyPath)));
        string[] keys =
        [
            LibraryCommand.BuildEffectiveCacheKey(assemblyPath, hash, hasSourceLink: false),
            LibraryCommand.BuildEffectiveCacheKey(assemblyPath, hash, hasSourceLink: true),
        ];

        try
        {
            await PersistentCache.RequestVersionedCategoryCleanupAsync();
            foreach (string key in keys)
            {
                PersistentCache.Set(legacyCategory, key, "Library Info\n", extension: "tsv");
                Assert.NotNull(PersistentCache.TryGet(legacyCategory, key, extension: "tsv"));
            }

            var (exit, output, error) = await RunAppAsync(
                "library", assemblyPath,
                "-D", "--effective",
                "--tree",
                "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains(SectionNames.References, output);
            Assert.Contains(SectionNames.UnsafeMembers, output);
        }
        finally
        {
            foreach (string key in keys)
            {
                DeleteIfPresent(PersistentCache.GetFilePath(legacyCategory, key, extension: "tsv"));
                DeleteIfPresent(PersistentCache.GetFilePath(currentCategory, key, extension: "tsv"));
            }
            Directory.Delete(directory, recursive: true);
        }

        static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Discover_Count_CountsDiscoveredRowsRatherThanTheDocument()
    {
        // Effective discovery renders discovered rows, but the command's own --count branch sat
        // ahead of the discovery branch and counted the inspection document instead — exiting 0
        // with a plausible number for a different payload. Pin the count to the payload.
        var (listExit, listOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "", "--tips", "q");
        Assert.Equal(0, listExit);

        var rows = listOutput.Split('\n').Count(l => l.StartsWith("| ", StringComparison.Ordinal)) - 2;
        Assert.True(rows > 0, "Discovery must list rows for this test to prove anything.");

        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal(rows, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Discover_ShapeProjection_IsRefusedRatherThanAnsweredFromTheDocument()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "", "--value", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("--value is not available with -D/--discover", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discover_Count_CountsDiscoveredRowsForTypeSchema()
    {
        // The type/member discovery path branches in the command definition, before an options
        // record exists, so it reads the request from the parse result instead.
        var (exit, output, error) = await RunAppAsync("type", "--schema", "-D", "", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.Parse(output.Trim(), CultureInfo.InvariantCulture) > 0);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task Discover_Count_StaticSchemaHonorsRowWindow(string command)
    {
        var (exit, output, error) = await RunAppAsync(
            command, "--schema", "-D", "", "--count", "--rows", "1");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Theory]
    [InlineData("--fields")]
    [InlineData("--columns")]
    public async Task Discover_Count_StaticSchemaValidatesProjection(string projection)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "--schema", "-D", "", "--count", projection, "NoSuchField");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("NoSuchField", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discover_Count_CountsStaticSchemaDiscoveryForLibrary()
    {
        // Static -D --schema returns before the library is resolved, a separate early return from
        // the effective-discovery one below it.
        var (countExit, countOutput, countError) = await RunAppAsync(
            "library", TestAssemblyPath, "--schema", "-D", "--count", "--tips", "q");

        Assert.Equal(0, countExit);
        Assert.Empty(countError);

        var (listExit, listOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "--schema", "-D", "--tips", "q");
        Assert.Equal(0, listExit);

        // The count must match the payload it stands in for: rendered rows less header and separator.
        var rows = listOutput.Split('\n').Count(l => l.StartsWith("| ", StringComparison.Ordinal)) - 2;
        Assert.Equal(rows, int.Parse(countOutput.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Print_ProjectsTheSelectedDocumentSectionAndRefusesUnprintableOnes()
    {
        // --print names the section whose rows carry the document, like every other payload
        // projection. There is no per-document flag to disagree with the selection.
        var (exit, output, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "-S", "Package README file", "--print");

        Assert.Equal(0, exit);
        Assert.NotEmpty(output);

        // Printability is a row capability. The whole-package listing also contains assemblies
        // and images, so it declares no printable payload and is refused rather than guessing.
        var (selectedExit, selectedOutput, selectedError) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "-S", "Files", "--print");

        Assert.Equal(1, selectedExit);
        Assert.Empty(selectedOutput);
        Assert.Contains("exactly one printable section", selectedError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discover_Count_EqualsTheRowsDiscoveryRenders(bool schema)
    {
        // The projection and the render each build their own row list, so a filter added to one
        // call and not the other makes --count answer a row set the command never renders --
        // at exit 0, with the audit satisfied because a count really was written.
        string[] args = schema
            ? ["library", TestAssemblyPath, "--schema", "-D", ""]
            : ["library", TestAssemblyPath, "-D", ""];

        var (countExit, countOutput, _) = await RunAppAsync([.. args, "--count"]);
        var (rowsExit, rowsOutput, _) = await RunAppAsync([.. args, "--jsonl"]);

        Assert.Equal(0, countExit);
        Assert.Equal(0, rowsExit);

        var rendered = rowsOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.StartsWith('{'));

        Assert.True(rendered > 0, "the probe must render rows, or it proves nothing.");
        Assert.Equal(rendered, int.Parse(countOutput.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Discover_Print_RefusesInsteadOfPrintingTheGroundingDocument()
    {
        // The grounding branch sat ahead of the discovery branch, so --print fell into it and
        // returned the readme at exit 0 -- an unrelated payload for a projection of discovery.
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "-D", "", "--print");

        Assert.Equal(1, exit);
        Assert.Contains("-D/--discover", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Json.NET", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LensCount_WritesToTheRequestedOutputFile()
    {
        // A count is the command's payload, so --out has to apply to it. Writing it to stdout
        // instead leaves the requested file absent and silently ignores the option.
        var path = Path.Combine(Path.GetTempPath(), $"lens-count-{Guid.NewGuid():N}.txt");

        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", "Newtonsoft.Json@13.0.4", "--tfms", "--count", "--out", path);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(path), "--out was ignored: the requested file was never written.");
            Assert.Equal("8\n", File.ReadAllText(path));
            Assert.Empty(output.Trim());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Discover_ShapeProjection_ReportsTheLensRefusalWithoutRequiringASelection()
    {
        // The ordinary shape gate ran first and reported a missing -S, which is not the actual
        // problem: discovery renders its own payload and cannot answer a column projection.
        var (exit, _, error) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-D", "--value", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("--value is not available with -D/--discover", error, StringComparison.Ordinal);
        Assert.DoesNotContain("requires -S/--select", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LensMode_SectionFilter_IsRefusedRatherThanIgnored()
    {
        // -S was previously accepted and then ignored by the lens, and --count required it,
        // so the mode was reachable only through a filter it did not honor.
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json", "--versions", "-n", "1", "-S", "Files", "--count");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("-S/--select is not available with --versions", error);
    }


    [Fact]
    public async Task ProjectionFlags_ConflictIsMootUnderHelp()
    {
        // Help renders no payload, so there is nothing for two projections to fight over.
        // Rejecting the combination here would turn a working help request into an error.
        var (exit, output, _) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-S", "Classes", "--count", "--print", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("Usage", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyProjection_IsRejectedByCommandsThatDoNotCatchIt()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "No.Such.Package.Xyz123", "--columns", ",", "--table");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--columns requires at least one name.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
        Assert.DoesNotContain("not found", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Type;Type")]
    [InlineData("Type, Type")]
    [InlineData("Type,TYPE")]
    public async Task DuplicateProjection_IsRejectedInEverySpellingTheParserAccepts(string columns)
    {
        // The duplicate check must split the value the same way ParseColumns does, or a duplicate
        // written in a spelling the check does not understand escapes the parse-time gate. --columns
        // accepts semicolons as well as commas, tolerates surrounding whitespace, and matches
        // case-insensitively, so each of these is the same request as "Type,Type".
        //
        // This runs against `package` deliberately. `find` has a catch-all that reports the second
        // gate's throw with the same "Error: Duplicate ..." text, so asserting there cannot tell
        // which gate fired -- an earlier version of this test used `find` and passed even with the
        // splitter replaced by a plain comma split. `package` has no catch-all, so only the
        // parse-time gate can produce a clean error here. Parse-time rejection also precedes any
        // network call, so this does not hit NuGet -- the package name is deliberately one that
        // does not exist, which makes that structural rather than incidental: if the parse-time
        // gate ever stopped firing, this would fail with "Package ... not found" instead, which is
        // a different message rather than a hang.
        var (exit, _, error) = await RunAppAsync(
            "package", "No.Such.Package.Xyz123", "--columns", columns, "--table");

        Assert.Equal(1, exit);
        Assert.Contains("Duplicate --columns entry", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateProjection_IsRejectedAcrossRepeatedOccurrences()
    {
        // Repeated occurrences are merged into one comma-separated value before parsing, so
        // `--columns Type --columns Type` is the same request as `--columns Type,Type` and must
        // fail the same way. This pins that the validator reads the merged token rather than one
        // occurrence in isolation, which would let the duplicate through. Uses `package` for the
        // same reason as above: it distinguishes the parse-time gate from the second gate, and the
        // nonexistent package name keeps it off the network.
        var (exit, _, error) = await RunAppAsync(
            "package", "No.Such.Package.Xyz123", "--columns", "Type", "--columns", "Type", "--table");

        Assert.Equal(1, exit);
        Assert.Contains("Duplicate --columns entry: Type", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateProjection_IsRejectedByCommandsThatDoNotCatchIt()
    {
        // The duplicate check must fire at parse time, not from inside the invocation pipeline.
        // A throw there is only reported cleanly by commands that happen to catch it: `find` has a
        // catch-all that prints "Error: ...", but `package` catches only HttpRequestException, so a
        // pipeline throw reached System.CommandLine's default handler and printed a stack trace.
        // Validating on the --columns/--fields options themselves is what makes the contract
        // uniform across every command. Found by adversarial review of #3494.
        //
        // This asserts on the *absence* of a stack trace, because exit 1 and a message on stderr
        // were already true of the crashing form -- the stack trace was the whole defect.
        var (exit, output, error) = await RunAppAsync(
            "package", "No.Such.Package.Xyz123", "--fields", "Authors,Authors", "--table");

        Assert.Equal(1, exit);
        Assert.Contains("Duplicate --fields entry: Authors", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
        Assert.DoesNotContain("at DotnetInspector", error, StringComparison.Ordinal);
        // Rejection precedes package resolution, so the name is never looked up.
        Assert.DoesNotContain("not found", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.Trim());
    }

    [Fact]
    public async Task LensCounts_ApplyRowsAndValidateProjectedColumns()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var tfms = await RunAppAsync(
                "package", packagePath, "--tfms",
                "--count", "--rows", "1..1", "--tips", "q");
            var projectedTfms = await RunAppAsync(
                "package", packagePath, "--tfms",
                "--columns", "TFM",
                "--count", "--rows", "1..1", "--tips", "q");
            var layout = await RunAppAsync(
                "package", packagePath, "--layout",
                "--count", "--rows", "1..1", "--tips", "q");
            var discovery = await RunAppAsync(
                "library", TestAssemblyPath, "-D", "",
                "--count", "--rows", "1", "--tips", "q");
            var renderedTfms = await RunAppAsync(
                "package", packagePath, "--tfms",
                "--jsonl", "--rows", "1..1", "--tips", "q");
            var renderedDiscovery = await RunAppAsync(
                "library", TestAssemblyPath, "-D", "",
                "--jsonl", "--rows", "1", "--tips", "q");
            var invalid = await RunAppAsync(
                "package", packagePath, "--tfms",
                "--columns", "NoSuchColumn",
                "--count", "--tips", "q");

            foreach (var result in new[] { tfms, projectedTfms, layout, discovery })
            {
                Assert.Equal(0, result.Exit);
                Assert.Equal("1", result.Output.Trim());
                Assert.Empty(result.Error);
            }

            Assert.Equal(0, renderedTfms.Exit);
            Assert.Single(renderedTfms.Output.Split(
                '\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.Empty(renderedTfms.Error);

            Assert.Equal(0, renderedDiscovery.Exit);
            Assert.Single(renderedDiscovery.Output.Split(
                '\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.Empty(renderedDiscovery.Error);

            Assert.Equal(1, invalid.Exit);
            Assert.Empty(invalid.Output);
            Assert.Contains("NoSuchColumn", invalid.Error);
            Assert.DoesNotContain("Payload", invalid.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AppContextSwitchProjection_UsesRawStringsAndLeavesDeduplicationToConsumer()
    {
        var assemblyPath = typeof(AppContextSwitchFixture).Assembly.Location;
        using var session = AssemblyInspectionSession.Open(assemblyPath);
        var occurrences = AppContextSwitchProjectionProducer.Produce(session.MethodBodies);

        Assert.Contains(
            occurrences,
            occurrence => occurrence.Switch == @"DotnetInspector.Fixtures.Literal\nSwitch");
        Assert.Equal(
            2,
            occurrences.Count(
                occurrence => occurrence.Switch == "DotnetInspector.Fixtures.Duplicate"));
        Assert.DoesNotContain(
            occurrences,
            occurrence => occurrence.Switch == "DotnetInspector.Fixtures.Lookalike");
        Assert.Contains(
            occurrences,
            occurrence => occurrence.Switch == "TestSwitch.Ignored");

        var inventory =
            AppContextSwitchProjectionProducer.ProduceInventory(
                session.MethodBodies);
        Assert.Single(
            inventory,
            occurrence => occurrence.Switch == "DotnetInspector.Fixtures.Duplicate");
        Assert.DoesNotContain(
            inventory,
            occurrence => occurrence.Switch.StartsWith(
                "TestSwitch.",
                StringComparison.Ordinal)
                || occurrence.Switch.StartsWith("Switch.", StringComparison.Ordinal)
                || occurrence.Switch.StartsWith(
                    "System.Resources.UseSystemResourceKeys",
                    StringComparison.Ordinal));

        var (exit, output, error) = await RunAppAsync(
            "library", assemblyPath, "-S", "Switches", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Equal(
            1,
            output.Split(
                "DotnetInspector.Fixtures.Duplicate",
                StringSplitOptions.None).Length - 1);
        Assert.Contains(@"DotnetInspector.Fixtures.Literal\nSwitch", output);
        Assert.DoesNotContain("DotnetInspector.Fixtures.Lookalike", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_PackageTypedDocumentFailsClosed()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.JsonProjection",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--json", "--columns", "Package", "--tips", "q");
            var (multiExit, multiOutput, multiError) = await RunAppAsync(
                "package", packagePath, packagePath,
                "--json", "--columns", "Package", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("requires lowered JSON", error);
            Assert.Equal(1, multiExit);
            Assert.Empty(multiOutput);
            Assert.Contains("requires lowered JSON", multiError);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_PackageAllLibrariesLensRejectsProjection()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries",
                "-S", "Library Info", "--json", "--fields", "Assembly Version",
                "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "--all-libraries cannot be combined with --fields",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_PackageDiscoveryOwnsProjectedJson()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.DiscoveryProjection",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath,
                "-D", "--json", "--columns", "Name", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.NotEmpty(document.RootElement.EnumerateArray());
            Assert.All(
                document.RootElement.EnumerateArray(),
                row => Assert.Equal(
                    ["name"],
                    row.EnumerateObject().Select(property => property.Name)));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--value")]
    [InlineData("--urls")]
    [InlineData("--paths")]
    public async Task ProjectedJsonRoutingAudit_MultiPackagePayloadProjectionsFailClosed(
        string projection)
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.MultiPayloadProjection",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Package Info",
                "--json", "--fields", "Version", projection, "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                $"Multiple package inspection cannot be combined with {projection}",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_MultiPackageRootsFailBeforeOutput()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.MultiRootProjection",
            "README.md",
            "# Test package",
            extraFiles: [("lib/net8.0/Test.dll", "test")]);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, packagePath,
                "-S", "Package files",
                "--roots", "--json", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "Multiple package inspection cannot be combined with --roots",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_PackageDiscoveryRootsFailBeforeOutput()
    {
        var (exit, output, error) = await RunAppAsync(
            "--offline",
            "package", "Package.That.Must.Not.Resolve",
            "-D", "-S", "Package files",
            "--roots", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--roots cannot be combined with -D/--discover.",
            error);
        Assert.DoesNotContain("Package.That.Must.Not.Resolve", error);
    }

    [Theory]
    [InlineData("--library")]
    [InlineData("--all-libraries")]
    public async Task ProjectedJsonRoutingAudit_PackageLibraryRootsFailBeforeOutput(
        string mode)
    {
        var (exit, output, error) = await RunAppAsync(
            "--offline",
            "package", "Package.That.Must.Not.Resolve",
            mode,
            "-S", "Library Info",
            "--roots", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"{mode} cannot be combined with --roots.", error);
        Assert.DoesNotContain("Package.That.Must.Not.Resolve", error);
    }

    [Fact]
    public async Task ProjectedJsonRoutingAudit_ProjectHonorsProjection()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Columns", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/selected/SKILL.md", "selected")]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--columns", "Package", "--json");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            JsonElement row = Assert.Single(
                document.RootElement.GetProperty("skills").EnumerateArray());
            Assert.Equal(
                "Test.Project.Columns",
                row.GetProperty("package").GetString());
            Assert.False(row.TryGetProperty("version", out _));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// This extends <see cref="OutputFormatterTests.ArtifactNewlineGate_ProductOwnedFramingUsesLf"/>
    /// through the command-only JSONL builders whose output cannot be exercised at the formatter
    /// seam. Each artifact must keep LF framing when <c>--out</c> writes it to a file.
    /// </summary>
    [Fact]
    public async Task ArtifactNewlineGate_CommandJsonlFilesUseLf()
    {
        var (projectPath, projectTempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.NewlineGate",
                "1.0.0",
                "README.md",
                "readme",
                Skills:
                [CompliantProjectSkill("skills/newline/SKILL.md", "skill body")]));
        var (packagePath, packageTempDir) = CreateLocalReadmePackage(
            "Test.Package.NewlineGate",
            "README.md",
            "readme",
            "agents body");

        try
        {
            var readmeOutput = Path.Combine(projectTempDir, "readme.jsonl");
            var skillsOutput = Path.Combine(projectTempDir, "skills.jsonl");
            var contentOutput = Path.Combine(packageTempDir, "content.jsonl");

            var (readmeExit, readmeStdout, readmeError) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--print",
                "--jsonl",
                "--out", readmeOutput);
            var (skillsExit, skillsStdout, skillsError) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--jsonl", "--out", skillsOutput);
            var (contentExit, contentStdout, contentError) = await RunAppAsync(
                "package", packagePath, "--path", "@agents", "--content", "--jsonl", "--out", contentOutput);

            Assert.Equal(0, readmeExit);
            Assert.Equal(0, skillsExit);
            Assert.Equal(0, contentExit);
            Assert.Empty(readmeStdout);
            Assert.Empty(skillsStdout);
            Assert.Empty(contentStdout);
            Assert.Empty(readmeError);
            Assert.Empty(skillsError);
            Assert.Empty(contentError);

            foreach (var path in new[] { readmeOutput, skillsOutput, contentOutput })
            {
                var artifact = File.ReadAllText(path);
                Assert.DoesNotContain('\r', artifact);
                Assert.EndsWith("\n", artifact, StringComparison.Ordinal);
                using var _ = JsonDocument.Parse(
                    Assert.Single(artifact.Split('\n', StringSplitOptions.RemoveEmptyEntries)));
            }
        }
        finally
        {
            Directory.Delete(projectTempDir, recursive: true);
            Directory.Delete(packageTempDir, recursive: true);
        }
    }
}
