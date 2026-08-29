using System.CommandLine;
using DotnetInspector.CommandLine;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

public class CommandLineLimitSlice4677Tests
{
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
    public void LineModeDetectionRecognizesAttachedOptionValueForms()
    {
        // Round 15 (Opus): --lines/--tail-lines/--tail line-mode detection compared raw tokens
        // for exact equality, missing the "=" and ":" attached-value forms System.CommandLine
        // accepts for boolean flags and -n's value -- silently dropping the requested line
        // window with no error, rather than reporting one of these unrecognized.
        CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-n", "5", "--lines"]);
        Assert.Equal(5, CommandLineBuilder.HeadLines);

        CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-n", "5", "--lines=true"]);
        Assert.Equal(5, CommandLineBuilder.HeadLines);

        CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-n", "5", "--lines:true"]);
        Assert.Equal(5, CommandLineBuilder.HeadLines);

        CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-n:5", "--lines"]);
        Assert.Equal(5, CommandLineBuilder.HeadLines);

        CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-n5", "--lines"]);
        Assert.Equal(5, CommandLineBuilder.HeadLines);

        CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-n", "5", "--tail-lines=true"]);
        Assert.Equal(5, CommandLineBuilder.TailLines);
        Assert.Null(CommandLineBuilder.HeadLines);

        // An explicit "=false"/":false" must not enable line mode.
        CommandLineBuilder.PreprocessArgs(["package", "System.Text.Json", "-n", "5", "--lines=false"]);
        Assert.Null(CommandLineBuilder.HeadLines);
        Assert.Null(CommandLineBuilder.TailLines);
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

        Assert.NotEmpty(result.Errors);
        Assert.Null(CommandLineBuilder.HeadLines);
        Assert.Null(CommandLineBuilder.TailLines);
    }
}
