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

        // The implicit-router form (no explicit `package` keyword) resolves to `package` at
        // runtime too -- RouterCommandDefinition.RewriteAsync routes a bare, non-file-path
        // target with --library or a version query to `package` by default. A bare -N must
        // expand there as well. PreprocessArgs also always prepends "router" and moves the
        // bare target immediately after it for these implicit invocations.
        Assert.Equal(
            ["router", "System.Text.Json", "--library", "-n", "2", "--tips", "q"],
            CommandLineBuilder.PreprocessArgs(["System.Text.Json", "--library", "-2", "--tips", "q"]));
        Assert.Equal(
            ["router", "System.Text.Json", "--version", "-n", "2", "--tips", "q"],
            CommandLineBuilder.PreprocessArgs(["System.Text.Json", "--version", "-2", "--tips", "q"]));

        // But an implicit .dll-path target routes straight to `library` (not through
        // "router"), where --version is required-value; a bare -N following it must still be
        // treated as that value, not expanded.
        Assert.Equal(
            ["library", "Foo.dll", "--version", "-5"],
            CommandLineBuilder.PreprocessArgs(["Foo.dll", "--version", "-5"]));

        // Round 14 (Gemini Pro): two more deterministic implicit-router shapes reach `package`
        // without matching the R13 heuristic above. A redundant, self-referential
        // "--package <same target>" (RewriteAsync's IsExplicitSourceIdentity check) routes to
        // `package` even though --package is present, so the R13 "no more-specific selector"
        // guard must not block it.
        Assert.Equal(
            ["router", "System.Text.Json", "--package", "System.Text.Json", "--version", "-n", "2"],
            CommandLineBuilder.PreprocessArgs(
                ["System.Text.Json", "--package", "System.Text.Json", "--version", "-2"]));

        // ...and an implicit `.nupkg` target routes straight to `package` (only `.dll` routes
        // to `library`), so it must not be excluded by the same file-path guard that correctly
        // excludes `.dll` targets.
        Assert.Equal(
            ["package", "Foo.nupkg", "--library", "-n", "2"],
            CommandLineBuilder.PreprocessArgs(["Foo.nupkg", "--library", "-2"]));

        // Round 14 (Sol): the implicit target need not be the very first token -- a leading
        // global option (e.g. --tips) can precede it, and the router still resolves the same
        // way once the option's own value is skipped. The R13 heuristic wrongly required the
        // resolved command token to sit at index 0; it must key off the resolved token itself
        // (as platformIsValueless/isPackageCommand's explicit-command check already do), not
        // its position.
        Assert.Equal(
            ["router", "System.Text.Json", "--tips", "q", "--version", "-n", "2"],
            CommandLineBuilder.PreprocessArgs(["--tips", "q", "System.Text.Json", "--version", "-2"]));

        // ...and an implicit form combined with a more specific source selector (--package,
        // --platform, --project, or a type/member selector) is conservatively left alone (the
        // -5 stays unexpanded) too, since the full router decision for those shapes is not
        // safely predictable here -- it still gets the "router" prefix, though.
        Assert.Equal(
            ["router", "System.Text.Json", "--platform", "--version", "-5"],
            CommandLineBuilder.PreprocessArgs(["System.Text.Json", "--platform", "--version", "-5"]));

        // Round 15 (Sol): a target with explicit generic notation (e.g. "List<T>") never
        // routes to `package` from this fallback shape -- RewriteAsync's hasExplicitApiSource
        // branch takes any --library <value> reaching it unexpanded to type/member before the
        // final "ContainsOption(tokens, --library) => package" catch-all is reached. A bare -N
        // there must stay attached to --library as its required value, not expand.
        Assert.Equal(
            ["router", "System.Collections.Generic.List<T>", "--library", "-2"],
            CommandLineBuilder.PreprocessArgs(["System.Collections.Generic.List<T>", "--library", "-2"]));

        // Round 15 (Sol): the self-referential "--package <same target>" exception from Round
        // 14 must not override a --type/--member selector also present -- RewriteAsync's
        // TryRouteExplicitSourceTarget checks --type/--member before the self-referential-
        // identity fallback and routes to `type`/`member` instead, where --library is
        // required-value, not the package primary-library selector.
        Assert.Equal(
            ["router", "System.Text.Json", "--package", "System.Text.Json", "--type", "JsonSerializer", "--library", "-2"],
            CommandLineBuilder.PreprocessArgs(
                ["System.Text.Json", "--package", "System.Text.Json", "--type", "JsonSerializer", "--library", "-2"]));

        // Round 15 (Opus): the self-referential exception must also decline when a second,
        // unattached positional token is present (not just an explicit --type/--member
        // selector) -- RewriteAsync's TryFindPositionalIndex treats that second positional as
        // the deferred type/member target once the redundant "--package <target>" pair is set
        // aside, routing to `type`/`member`, not `package`.
        Assert.Equal(
            ["router", "System.Text.Json", "--package", "System.Text.Json", "JsonSerializer", "--library", "-2"],
            CommandLineBuilder.PreprocessArgs(
                ["System.Text.Json", "--package", "System.Text.Json", "JsonSerializer", "--library", "-2"]));
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
}
