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
