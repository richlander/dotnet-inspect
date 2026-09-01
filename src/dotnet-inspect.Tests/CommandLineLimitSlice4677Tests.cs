using System.CommandLine;
using System.Text.RegularExpressions;
using DotnetInspector.CommandLine;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class CommandLineLimitSlice4677Tests
{
    [Fact]
    public void ShippedGuidanceDoesNotTeachCompatibilityOnlyNumericSelectors()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        string[] files =
        [
            Path.Combine(root, "README.md"),
            .. Directory.EnumerateFiles(
                Path.Combine(root, "skills"),
                "*.md",
                SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(
                Path.Combine(root, "docs", "workflows"),
                "*.md",
                SearchOption.AllDirectories),
        ];
        var obsolete = new Regex(
            @"--(?:versions(?:-with-feed)?|take)(?:=|\s+)(?:N|\d+)",
            RegexOptions.CultureInvariant);

        var findings = files
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, Number: index + 1)))
            .Where(item => obsolete.IsMatch(item.Line))
            .Select(item => $"{Path.GetRelativePath(root, item.Path)}:{item.Number}: {item.Line}")
            .ToArray();

        Assert.Empty(findings);
    }

    [Fact]
    public void RouterPreflightRecognizesOnlyRouteIndependentLimitConflicts()
    {
        Assert.Equal(
            SharedOptions.CountWindowConflictError,
            RouterCommandDefinition.GetRouteIndependentLimitError(
                ["Target", "--count", "-n", "1"]));
        Assert.Equal(
            SharedOptions.DiscoveryTopConflictError,
            RouterCommandDefinition.GetRouteIndependentLimitError(
                ["Target", "-D", "--top=1"]));

        Assert.Null(
            RouterCommandDefinition.GetRouteIndependentLimitError(
                ["Target", "--count=false", "-n", "1"]));
        Assert.Null(
            RouterCommandDefinition.GetRouteIndependentLimitError(
                ["Target", "--count", "--", "-n", "1"]));
        Assert.Null(
            RouterCommandDefinition.GetRouteIndependentLimitError(
                ["Target", "--library", "-2", "--count"]));
    }

    [Fact]
    public void UniversalLimitShorthandIsArityAware()
    {
        Assert.Empty(CommandLineBuilder.CreateRootCommand()
            .Parse(CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-n", "2"])).Errors);
        Assert.Empty(CommandLineBuilder.CreateRootCommand()
            .Parse(CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-n=2"])).Errors);
        Assert.Empty(CommandLineBuilder.CreateRootCommand()
            .Parse(CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-2"])).Errors);

        var zero = CommandLineBuilder.CreateRootCommand()
            .Parse(CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-0"]));
        Assert.Contains(zero.Errors, error => error.Message.Contains("-n must be a positive integer.", StringComparison.Ordinal));

        var duplicate = CommandLineBuilder.CreateRootCommand()
            .Parse(CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-2", "-n", "3"]));
        Assert.Contains(duplicate.Errors, error => error.Message.Contains("Specify -n only once.", StringComparison.Ordinal));

        string[] terminated = ["package", "System.Text.Json", "--", "-2"];
        Assert.Same(terminated, CommandLineBuilder.PreprocessArgs(terminated));

        string[] requiredValue = ["find", "--package-prefix", "Azure", "--type", "-5"];
        Assert.Same(requiredValue, CommandLineBuilder.PreprocessArgs(requiredValue));

        Assert.Equal(
            ["package", "System.Text.Json", "--version", "-n", "5"],
            CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "--version", "-5"]));
        Assert.Equal(
            ["package", "System.Text.Json", "--preview", "-n", "5"],
            CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "--preview", "-5"]));
        Assert.Equal(
            ["package", "search", "Json", "-n", "5"],
            CommandLineBuilder.PreprocessArgs(
                ["package", "search", "Json", "-5"]));

        var numericSelector = CommandLineBuilder.PreprocessArgs(
            ["find", "--package-prefix", "Azure", "--type", "-5"]);
        Assert.Equal(
            ["find", "--package-prefix", "Azure", "--type", "-5"],
            numericSelector);

        // `--platform` is a value-less bool flag for search-scope commands (find, implements,
        // extensions, depends), unlike its required-value form for type/member/match/assembly.
        // A bare -N following it must still expand to -n N rather than being swallowed as a
        // (nonexistent) --platform value.
        Assert.Equal(
            ["find", "JsonSerializer", "--platform", "-n", "5"],
            CommandLineBuilder.PreprocessArgs(["find", "JsonSerializer", "--platform", "-5"]));

        // For commands where --platform does take a required value, the following bare -N must
        // still be treated as that value, not expanded.
        string[] platformRequiredValue = ["member", "JsonSerializer", "--platform", "-5"];
        Assert.Same(platformRequiredValue, CommandLineBuilder.PreprocessArgs(platformRequiredValue));

        // `--library` and `--version` have the same command-dependent duality: both are
        // ArgumentArity.ZeroOrOne on `package` ("use alone" selects the primary library / shows
        // the resolved version), but required-value everywhere else (`--library` on search-scope
        // commands, `--version` on `library`'s platform runtime-version option).
        Assert.Equal(
            ["package", "System.Text.Json", "--library", "-n", "2"],
            CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "--library", "-2"]));
        Assert.Equal(
            ["package", "System.Text.Json", "--version", "-n", "5"],
            CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "--version", "-5"]));

        string[] libraryRequiredValueOnSearch = ["find", "JsonSerializer", "--library", "-5"];
        Assert.Same(libraryRequiredValueOnSearch, CommandLineBuilder.PreprocessArgs(libraryRequiredValueOnSearch));

        string[] versionRequiredValueOnLibrary = ["library", "System.Text.Json", "--version", "-5"];
        Assert.Same(versionRequiredValueOnLibrary, CommandLineBuilder.PreprocessArgs(versionRequiredValueOnLibrary));

        // Implicit invocations retain -N until the router selects the real command. This is
        // the architectural boundary: preprocessing no longer predicts the router outcome.
        Assert.Equal(
            ["router", "System.Text.Json", "--library", "-2", "--tips", "q"],
            CommandLineBuilder.PreprocessArgs(["System.Text.Json", "--library", "-2", "--tips", "q"]));
        Assert.Equal(
            ["router", "System.Text.Json", "--version", "-2", "--tips", "q"],
            CommandLineBuilder.PreprocessArgs(["System.Text.Json", "--version", "-2", "--tips", "q"]));

        var root = CommandLineBuilder.CreateRootCommand();
        Assert.Equal(
            ["package", "System.Text.Json", "--library", "-n", "2"],
            ArgumentPreprocessor.PreprocessRoutedArgs(
                ["package", "System.Text.Json", "--library", "-2"],
                root));
        string[] routedType =
            ["type", "System.Collections.Generic.List<T>", "--library", "-2"];
        Assert.Same(
            routedType,
            ArgumentPreprocessor.PreprocessRoutedArgs(routedType, root));

        // But an implicit .dll-path target routes straight to `library` (not through
        // "router"), where --version is required-value; a bare -N following it must still be
        // treated as that value, not expanded.
        Assert.Equal(
            ["library", "Foo.dll", "--version", "-5"],
            CommandLineBuilder.PreprocessArgs(["Foo.dll", "--version", "-5"]));

        // The same deferral applies to every router shape; the router's one implementation
        // owns whether these become package, type, or member commands.
        Assert.Equal(
            ["router", "System.Text.Json", "--package", "System.Text.Json", "--version", "-2"],
            CommandLineBuilder.PreprocessArgs(
                ["System.Text.Json", "--package", "System.Text.Json", "--version", "-2"]));

        // ...and an implicit `.nupkg` target routes straight to `package` (only `.dll` routes
        // to `library`), so it must not be excluded by the same file-path guard that correctly
        // excludes `.dll` targets.
        Assert.Equal(
            ["package", "Foo.nupkg", "--library", "-n", "2"],
            CommandLineBuilder.PreprocessArgs(["Foo.nupkg", "--library", "-2"]));

        // Root option arity comes from the command model while locating the implicit target.
        Assert.Equal(
            ["router", "System.Text.Json", "--tips", "q", "--version", "-2"],
            CommandLineBuilder.PreprocessArgs(["--tips", "q", "System.Text.Json", "--version", "-2"]));

        // More-specific source selectors are likewise untouched before routing.
        Assert.Equal(
            ["router", "System.Text.Json", "--platform", "--version", "-5"],
            CommandLineBuilder.PreprocessArgs(["System.Text.Json", "--platform", "--version", "-5"]));

        Assert.False(
            CommandLineBuilder.TryGetStaleArgumentError(
                ["System.Text.Json", "--versions", "--tail", "-2"],
                out _));

        Assert.Equal(
            ["router", "System.Collections.Generic.List<T>", "--library", "-2"],
            CommandLineBuilder.PreprocessArgs(["System.Collections.Generic.List<T>", "--library", "-2"]));

        Assert.Equal(
            ["router", "System.Text.Json", "--package", "System.Text.Json", "--type", "JsonSerializer", "--library", "-2"],
            CommandLineBuilder.PreprocessArgs(
                ["System.Text.Json", "--package", "System.Text.Json", "--type", "JsonSerializer", "--library", "-2"]));

        Assert.Equal(
            ["router", "System.Text.Json", "--package", "System.Text.Json", "JsonSerializer", "--library", "-2"],
            CommandLineBuilder.PreprocessArgs(
                ["System.Text.Json", "--package", "System.Text.Json", "JsonSerializer", "--library", "-2"]));
    }

    [Fact]
    public void UniversalLimitShorthandDerivesRequiredValuesFromCommandModel()
    {
        var root = new RootCommand();
        var command = new Command("future");
        var value = new Option<string?>("--future-value")
        {
            Arity = ArgumentArity.ExactlyOne
        };
        command.Options.Add(value);
        command.Options.Add(new Option<int?>("-n"));
        root.Subcommands.Add(command);

        string[] required =
            ["future", "--future-value", "-7"];
        Assert.Same(
            required,
            ArgumentPreprocessor.PreprocessRoutedArgs(
                required,
                root));

        value.Arity = ArgumentArity.ZeroOrOne;
        Assert.Equal(
            ["future", "--future-value", "-n", "7"],
            ArgumentPreprocessor.PreprocessRoutedArgs(
                ["future", "--future-value", "-7"],
                root));

        string[] focus =
            ["member", "JsonSerializer", "--focus", "-5"];
        Assert.Same(
            focus,
            CommandLineBuilder.PreprocessArgs(focus));
        string[] output =
            ["package", "System.Text.Json", "--out", "-5"];
        Assert.Same(
            output,
            CommandLineBuilder.PreprocessArgs(output));
        Assert.Equal(
            [
                "member",
                "JsonSerializer",
                "--focus=-5",
                "-n",
                "2"
            ],
            CommandLineBuilder.PreprocessArgs(
                [
                    "member",
                    "JsonSerializer",
                    "--focus=-5",
                    "-2"
                ]));

        Assert.Equal(
            ["router", "JsonSerializer", "--json"],
            CommandLineBuilder.PreprocessArgs(
                ["--json", "JsonSerializer"]));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void SeparatedPlatformBooleanRemainsOwnedByTheOption(
        string value)
    {
        string[] separated =
        [
            "find",
            "JsonSerializer",
            "--platform",
            value,
            "-n",
            "1",
        ];

        Assert.Equal(
            separated,
            CommandLineBuilder.PreprocessArgs(separated));
        Assert.Empty(
            CommandLineBuilder.CreateRootCommand()
                .Parse(separated)
                .Errors);
    }

    [Fact]
    public async Task ImplicitRouterExpandsShorthandAfterSelectingCommand()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var package = await RouterCommandDefinition.RouteTokensAsync(
            ["System.Text.Json", "--library", "-2"],
            NuGetSourceOptions.Default,
            root);
        Assert.True(package.Routed);
        Assert.Equal(
            [
                "package",
                "System.Text.Json",
                "--library",
                "-n",
                "2"
            ],
            package.Arguments);

        var version = await RouterCommandDefinition.RouteTokensAsync(
            ["System.Text.Json", "--version", "-2"],
            NuGetSourceOptions.Default,
            root);
        Assert.True(version.Routed);
        Assert.Equal(
            [
                "package",
                "System.Text.Json",
                "--version",
                "-n",
                "2"
            ],
            version.Arguments);

        var genericType = await RouterCommandDefinition.RouteTokensAsync(
            [
                "System.Collections.Generic.List<T>",
                "--library",
                "-2"
            ],
            NuGetSourceOptions.Default,
            root);
        Assert.True(genericType.Routed);
        Assert.NotEqual("package", genericType.Arguments[0]);
        Assert.Contains("-2", genericType.Arguments);
        Assert.DoesNotContain("-n", genericType.Arguments);

        var selectedType = await RouterCommandDefinition.RouteTokensAsync(
            [
                "System.Text.Json",
                "--package",
                "System.Text.Json",
                "--type",
                "JsonSerializer",
                "--library",
                "-2"
            ],
            NuGetSourceOptions.Default,
            root);
        Assert.True(selectedType.Routed);
        Assert.Equal("type", selectedType.Arguments[0]);
        Assert.Contains("-2", selectedType.Arguments);
        Assert.DoesNotContain("-n", selectedType.Arguments);
    }

    [Fact]
    public async Task ImplicitRouterConsumesSeparatedBooleanValues()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            var args = CommandLineBuilder.PreprocessArgs(
                ["--tail", "false", "System.Text.Json", "--tips", "q"]);
            return await CommandLineBuilder.CreateRootCommand()
                .Parse(args)
                .InvokeAsync();
        });

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("# System.Text.Json.dll", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsedLineWindowRecognizesSupportedOptionValueForms()
    {
        // Round 15 (Opus): --lines/--tail-lines/--tail line-mode detection compared raw tokens
        // for exact equality, missing the "=" and ":" attached-value forms System.CommandLine
        // accepts for boolean flags and -n's value -- silently dropping the requested line
        // window with no error, rather than reporting one of these unrecognized.
        ApplyLineWindow(["package", "System.Text.Json", "-n", "5", "--lines"]);
        Assert.Equal(5, CommandLineBuilder.HeadLines);

        ApplyLineWindow(["package", "System.Text.Json", "-n", "5", "--lines=true"]);
        Assert.Equal(5, CommandLineBuilder.HeadLines);

        ApplyLineWindow(["package", "System.Text.Json", "-n", "5", "--lines:true"]);
        Assert.Equal(5, CommandLineBuilder.HeadLines);

        ApplyLineWindow(["package", "System.Text.Json", "-n:5", "--lines"]);
        Assert.Equal(5, CommandLineBuilder.HeadLines);

        ApplyLineWindow(["package", "System.Text.Json", "-n5", "--lines"]);
        Assert.Equal(5, CommandLineBuilder.HeadLines);

        ApplyLineWindow(["package", "System.Text.Json", "-n", "5", "--tail-lines=true"]);
        Assert.Equal(5, CommandLineBuilder.TailLines);
        Assert.Null(CommandLineBuilder.HeadLines);

        // An explicit "=false"/":false" must not enable line mode.
        ApplyLineWindow(["package", "System.Text.Json", "-n", "5", "--lines=false"]);
        Assert.Null(CommandLineBuilder.HeadLines);
        Assert.Null(CommandLineBuilder.TailLines);

        ApplyLineWindow(["package", "System.Text.Json", "-n", "5", "--lines", "false"]);
        Assert.Null(CommandLineBuilder.HeadLines);
        Assert.Null(CommandLineBuilder.TailLines);

        static void ApplyLineWindow(string[] arguments)
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed =
                CommandLineBuilder.PreprocessArgs(arguments, root);
            CommandLineBuilder.ApplyParsedLineWindow(root.Parse(processed));
        }
    }

    [Fact]
    public void WindowModifiersBindOnlyToActiveCount()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        Assert.Contains(
            root.Parse(["package", "System.Text.Json", "--lines"]).Errors,
            error => error.Message.Contains("--lines requires -n N.", StringComparison.Ordinal));
        Assert.Contains(
            root.Parse(["package", "System.Text.Json", "--tail-lines"]).Errors,
            error => error.Message.Contains("--lines requires -n N.", StringComparison.Ordinal));
        Assert.Contains(
            root.Parse(["package", "System.Text.Json", "--head"]).Errors,
            error => error.Message.Contains("--head requires -n N.", StringComparison.Ordinal));
        Assert.Contains(
            root.Parse(["package", "System.Text.Json", "--tail"]).Errors,
            error => error.Message.Contains("--tail requires -n N.", StringComparison.Ordinal));
        Assert.Contains(
            root.Parse(["package", "System.Text.Json", "--rows", "2..3", "--tail"]).Errors,
            error => error.Message.Contains("cannot combine with bare --head or --tail", StringComparison.Ordinal));

        Assert.Empty(root.Parse(["package", "System.Text.Json", "-n", "2", "--lines"]).Errors);
        Assert.Empty(root.Parse(["package", "System.Text.Json", "-n", "2", "--tail-lines"]).Errors);
    }

    [Theory]
    [InlineData("-n", "2")]
    [InlineData("-2")]
    [InlineData("-n", "2", "--lines")]
    [InlineData("--top", "2")]
    [InlineData("--rows", "2..3")]
    [InlineData("--row", "2")]
    public void CountRejectsWindowSelectors(params string[] windowArguments)
    {
        string[] args =
        [
            "library",
            "System.Text.Json",
            "--count",
            .. windowArguments,
        ];

        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(CommandLineBuilder.PreprocessArgs(args));

        Assert.Contains(
            result.Errors,
            error => error.Message.Contains(
                "--count cannot be combined",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitFalseCountDoesNotConflictWithItemWindow()
    {
        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(["package", "System.Text.Json", "--count=false", "-n", "2"]);

        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("find", "JsonSerializer")]
    [InlineData("implements", "IDisposable")]
    [InlineData("extensions", "string")]
    public async Task NumericLegacyTypeLimitRejectsCountBeforeAcquisition(
        string command,
        string target)
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            var args = CommandLineBuilder.PreprocessArgs(
            [
                command,
                target,
                "--package-prefix",
                "Does.Not.Exist.4677",
                "--source",
                "http://127.0.0.1:1/v3/index.json",
                "-t",
                "2",
                "--count",
            ]);
            return await CommandLineBuilder.CreateRootCommand()
                .Parse(args)
                .InvokeAsync();
        });

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "--count cannot be combined with a numeric -t/--type limit.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("type", "-t", "-t/--type")]
    [InlineData("type", "-m", "-m/--member")]
    [InlineData("member", "-m", "-m/--member")]
    public async Task ApiNumericLegacyLimitsRejectCountBeforeAcquisition(
        string command,
        string option,
        string optionName)
    {
        var arguments = new List<string> { command };
        if (command == "member")
            arguments.Add("System.String");
        arguments.AddRange(
        [
            "--package",
            "Does.Not.Exist.4677",
            "--source",
            "http://127.0.0.1:1/v3/index.json",
            option,
            "2",
            "--count",
        ]);

        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            var args = CommandLineBuilder.PreprocessArgs([.. arguments]);
            return await CommandLineBuilder.CreateRootCommand()
                .Parse(args)
                .InvokeAsync();
        });

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            $"--count cannot be combined with a numeric {optionName} limit.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-n", "1", "item windows")]
    [InlineData("--rows", "2..3", "absolute row ranges")]
    public async Task FindRejectsDocumentJsonWindowsBeforeAcquisition(
        string option,
        string value,
        string windowName)
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            var args = CommandLineBuilder.PreprocessArgs(
            [
                "find",
                "JsonSerializer",
                "--package-prefix",
                "Does.Not.Exist.4677",
                "--source",
                "http://127.0.0.1:1/v3/index.json",
                "--json",
                option,
                value,
            ]);
            return await CommandLineBuilder.CreateRootCommand()
                .Parse(args)
                .InvokeAsync();
        });

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains($"Document --json {windowName}", error, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("implements", "IDisposable")]
    [InlineData("extensions", "string")]
    public async Task SearchCommandsRejectDocumentJsonWindowsBeforeAcquisition(
        string command,
        string target)
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            var args = CommandLineBuilder.PreprocessArgs(
            [
                command,
                target,
                "--package-prefix",
                "Does.Not.Exist.4677",
                "--source",
                "http://127.0.0.1:1/v3/index.json",
                "--json",
                "--columns",
                "Type",
                "-n",
                "1",
            ]);
            return await CommandLineBuilder.CreateRootCommand()
                .Parse(args)
                .InvokeAsync();
        });

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("Document --json item windows", error, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("package")]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    [InlineData("match")]
    [InlineData("diff")]
    [InlineData("timeline")]
    public async Task AcquisitionBackedCommandsRejectDocumentJsonWindowsBeforeAcquisition(
        string command)
    {
        const string package = "Does.Not.Exist.4677";
        string[] target = command switch
        {
            "package" => [command, package],
            "library" => [command, package],
            "type" => [command, "Missing.Type", "--package", package],
            "member" => [command, "Missing.Type.Missing", "--package", package],
            "match" =>
            [
                command,
                "Missing.Type.Left",
                "Missing.Type.Right",
                "--package",
                package,
            ],
            "diff" =>
                [command, "--package", $"{package}@1.0.0..2.0.0"],
            "timeline" =>
                [command, $"{package}@1.0.0..2.0.0", "Missing.Type"],
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        string[] projection = command is "package" or "library" or "type" or "member"
            ? ["--columns", "Type"]
            : [];

        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            string[] args = CommandLineBuilder.PreprocessArgs(
            [
                .. target,
                "--source",
                "http://127.0.0.1:1/v3/index.json",
                "--json",
                .. projection,
                "-n",
                "1",
                "--tips",
                "q",
            ]);
            return await CommandLineBuilder.CreateRootCommand()
                .Parse(args)
                .InvokeAsync();
        });

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "Document --json item windows",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryRankedJsonPreflightOnlyAllowsPerformanceKinds()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-ranked-json-{Guid.NewGuid():N}.dll");

        async Task<(int Exit, string Output, string Error)> RunAsync(
            string section) =>
            await ConsoleCapture.RunAsync(async () =>
            {
                string[] args = CommandLineBuilder.PreprocessArgs(
                [
                    "library",
                    missingPath,
                    "-S",
                    section,
                    "--top",
                    "1",
                    "--json",
                    "--tips",
                    "q",
                ]);
                return await CommandLineBuilder.CreateRootCommand()
                    .Parse(args)
                    .InvokeAsync();
            });

        var unsupported = await RunAsync(SectionNames.TopLeverage);
        var performance = await RunAsync(SectionNames.PerformanceBoxing);

        Assert.Equal(1, unsupported.Exit);
        Assert.Empty(unsupported.Output);
        Assert.Contains(
            "Document --json ranked selections",
            unsupported.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            missingPath,
            unsupported.Error,
            StringComparison.Ordinal);

        Assert.Equal(1, performance.Exit);
        Assert.Empty(performance.Output);
        Assert.DoesNotContain(
            "Document --json ranked selections",
            performance.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            $"File not found: {missingPath}",
            performance.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RankedTopProjectionDiagnosticNamesTop()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            var args = CommandLineBuilder.PreprocessArgs(
            [
                "library",
                "missing.dll",
                "-S",
                SectionNames.PerformanceBoxing,
                "--value",
                "--top",
                "1",
            ]);
            return await CommandLineBuilder.CreateRootCommand()
                .Parse(args)
                .InvokeAsync();
        });

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("--top ranked selections", error, StringComparison.Ordinal);
        Assert.DoesNotContain("-n item windows", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RankedTopBindsToSelectedPerformanceKindRowSet()
    {
        var shared = new SharedOptions();
        var bound = shared.BindPerformanceTriageToSelectedKindSections(
            new PerformanceTriageOptions { Top = 1 },
            [SectionNames.PerformanceArrays],
            PerformanceKinds.Sections,
            infoSections: null,
            categories: null,
            selectDefault: false);

        Assert.Equal([SectionNames.PerformanceArrays], bound.SelectedKindSections);
    }

    [Fact]
    public void RankedTopBindsPerformanceKindsWithinMixedSelection()
    {
        var shared = new SharedOptions();
        var bound = shared.BindPerformanceTriageToSelectedKindSections(
            new PerformanceTriageOptions { Top = 1 },
            [SectionNames.PerformanceArrays, SectionNames.TopLeverage],
            [.. PerformanceKinds.Sections, SectionNames.TopLeverage],
            infoSections: null,
            categories: null,
            selectDefault: false);

        Assert.Equal([SectionNames.PerformanceArrays], bound.SelectedKindSections);
    }

    [Theory]
    [InlineData("-n", "invalid")]
    [InlineData("-n", "2147483648")]
    [InlineData("-n", "1", "-n", "2")]
    public void InvalidLineCountsRemainParseErrors(params string[] countArguments)
    {
        string[] args =
        [
            "package",
            "System.Text.Json",
            .. countArguments,
            "--lines",
        ];

        string[] processed = CommandLineBuilder.PreprocessArgs(args);
        var result = CommandLineBuilder.CreateRootCommand().Parse(processed);
        CommandLineBuilder.ApplyParsedLineWindow(result);

        Assert.NotEmpty(result.Errors);
        Assert.Null(CommandLineBuilder.HeadLines);
        Assert.Null(CommandLineBuilder.TailLines);
    }

    [Fact]
    public void CurrentCliExplainsUnavailableOrderedRangeComposition()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var ranked = root.Parse(
            [
                "library",
                "System.Text.Json",
                "-S",
                "Performance: Arrays",
                "--top",
                "1",
                "--rows",
                "2..3",
            ]);
        Assert.Contains(
            ranked.Errors,
            error => error.Message.Contains(
                "--top cannot be combined with --rows because ordered row-stage "
                + "composition is not available yet.",
                StringComparison.Ordinal));

        var item = root.Parse(
            [
                "package",
                "System.Text.Json",
                "-n",
                "1",
                "--rows",
                "2..3",
            ]);
        Assert.Contains(
            item.Errors,
            error => error.Message.Contains(
                "--rows 2..3 cannot be combined with item-mode -n because "
                + "ordered row-stage composition is not available yet.",
                StringComparison.Ordinal));
    }
}
