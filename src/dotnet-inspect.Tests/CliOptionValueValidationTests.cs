using System.CommandLine;
using System.Text.Json;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class CliOptionValueValidationTests
{
    public static TheoryData<string, string[]> AttachedValues
    {
        get
        {
            var cases = new TheoryData<string, string[]>();
            foreach ((string alias, string name) in new[]
            {
                ("--flag", "--flag"), ("-f", "--flag"),
                ("--switch", "--switch"), ("-s", "--switch"),
                ("-fs", "--switch"), ("-sf", "--flag"),
                ("--presence", "--presence")
            })
            foreach (string separator in new[] { "=", ":" })
            foreach (string value in new[] { "2", "true", "false", "", "word", "--flag" })
                cases.Add(name, [$"{alias}{separator}{value}"]);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(AttachedValues))]
    public async Task AttachedValuesUseDeclaredZeroArity(string name, string[] arguments)
    {
        var fixture = new Fixture();
        AssertRejected(await fixture.Run(arguments), name);
        Assert.Null(fixture.Executed);
    }

    [Theory]
    [InlineData("--flag", "one", "--flag", "two")]
    [InlineData("--flag", "one", "-f", "word")]
    [InlineData("--switch", "one", "--flag", "-s", "false")]
    [InlineData("--switch", "--flag", "one", "--switch", "2")]
    [InlineData("--flag", "--required", "owned", "one", "-f", "true")]
    [InlineData("--flag", "one", "--optional-bool", "false", "-f", "two")]
    [InlineData("--flag", "one", "-rvalue", "-f", "two")]
    [InlineData("--switch", "one", "-fs", "two")]
    [InlineData("--presence", "one", "--presence", "two")]
    public async Task SurplusImmediatelyAfterFlagUsesItsOwnedName(
        string name, params string[] arguments)
    {
        var fixture = new Fixture();
        AssertRejected(await fixture.Run(arguments), name);
        Assert.Null(fixture.Executed);
    }

    [Theory]
    [InlineData("--flag", "one")]
    [InlineData("one", "--flag")]
    [InlineData("--flag", "2")]
    [InlineData("-f", "true")]
    [InlineData("--switch", "false")]
    [InlineData("--flag", "--switch", "one")]
    [InlineData("-fs", "one")]
    [InlineData("-fs", "")]
    [InlineData("--flag", "--required", "--switch")]
    [InlineData("--required=--flag=true", "one")]
    [InlineData("--required=--flag:false", "one")]
    [InlineData("--required", "--flag", "--switch", "one")]
    [InlineData("--optional", "one", "--flag", "two")]
    [InlineData("--optional-bool", "false", "one", "--flag")]
    [InlineData("--optional-bool=true", "one", "-f")]
    [InlineData("--many", "one", "two", "--flag", "three")]
    [InlineData("--flag", "--", "--switch=true")]
    public async Task PositionalsAndNonzeroArityValuesKeepTheirOwnership(params string[] arguments)
    {
        var fixture = new Fixture();
        var result = await fixture.Run(arguments);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal($"executed{Environment.NewLine}", result.Output);
        Assert.NotNull(fixture.Executed);
    }

    [Theory]
    [InlineData("--flag", "one", "two")]
    [InlineData("one", "--flag", "--required", "owned", "two")]
    [InlineData("one", "--flag", "--", "two")]
    [InlineData("one", "two", "--flag")]
    [InlineData("one", "--unknown", "--flag")]
    [InlineData("one", "--flagged=true")]
    public async Task UnrelatedSurplusKeepsParserDiagnostic(params string[] arguments)
    {
        var fixture = new Fixture();
        var result = await fixture.Run(arguments);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.DoesNotContain("does not accept a value", result.Error);
        Assert.NotEmpty(result.Error);
        Assert.Null(fixture.Executed);
    }

    [Fact]
    public async Task ZeroFiniteAndUnboundedPositionalCapacitiesRemainDistinct()
    {
        var none = new Fixture(0);
        AssertRejected(await none.Run("--flag", "one"), "--flag");

        var two = new Fixture(2);
        Assert.Equal(0, (await two.Run("one", "--flag", "two")).ExitCode);
        AssertRejected(await two.Run("one", "two", "--flag", "three"), "--flag");

        var unbounded = new Fixture(ArgumentArity.ZeroOrMore.MaximumNumberOfValues);
        var result = await unbounded.Run("one", "two", "--flag", "three", "four");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            ["one", "two", "three", "four"],
            Assert.IsType<string[]>(unbounded.Executed!.GetValue(unbounded.Targets)));
    }

    [Fact]
    public async Task SeparateFiniteArgumentsAndParentScopesRetainOwnership()
    {
        var root = new RootCommand();
        var parentTarget = new Argument<string?>("parent") { Arity = ArgumentArity.ZeroOrOne };
        var parentFlag = new Option<bool>("--flag") { Arity = ArgumentArity.Zero };
        root.Arguments.Add(parentTarget);
        root.Options.Add(parentFlag);
        var child = new Command("child");
        var first = new Argument<string>("first");
        var second = new Argument<string>("second");
        var childFlag = new Option<bool>("--child-flag") { Arity = ArgumentArity.Zero };
        child.Arguments.Add(first);
        child.Arguments.Add(second);
        child.Options.Add(childFlag);
        root.Subcommands.Add(child);
        bool executed = false;
        child.SetAction(_ => executed = true);

        string[] valid = ["parent", "--flag", "child", "one", "--child-flag", "two"];
        var accepted = await ConsoleCapture.RunAsync(() =>
            CommandLineBuilder.InvokeAsync(root.Parse(valid), valid));
        Assert.Equal(0, accepted.ExitCode);
        Assert.True(executed);

        executed = false;
        string[] invalid = ["parent", "child", "one", "two", "--child-flag", "three"];
        AssertRejected(await ConsoleCapture.RunAsync(() =>
            CommandLineBuilder.InvokeAsync(root.Parse(invalid), invalid)), "--child-flag");
        Assert.False(executed);
    }

    [Fact]
    public async Task ShadowedAliasesUseTheOwningOptionArity()
    {
        var root = new RootCommand();
        root.Options.Add(new Option<bool>("--flag") { Arity = ArgumentArity.Zero, Recursive = true });
        var child = new Command("child");
        var value = new Option<string>("--flag") { Arity = ArgumentArity.ExactlyOne };
        child.Options.Add(value);
        root.Subcommands.Add(child);
        string? received = null;
        child.SetAction(result => { received = result.GetValue(value); });

        string[] arguments = ["--flag", "child", "--flag", "owned"];
        var accepted = await ConsoleCapture.RunAsync(() =>
            CommandLineBuilder.InvokeAsync(root.Parse(arguments), arguments));
        Assert.True(accepted.ExitCode == 0, accepted.Error);
        Assert.Equal("owned", received);

        string[] invalid = ["--flag=word", "child", "--flag", "owned"];
        AssertRejected(await ConsoleCapture.RunAsync(() =>
            CommandLineBuilder.InvokeAsync(root.Parse(invalid), invalid)), "--flag");
    }

    public static TheoryData<string, string[]> PackageViolations
    {
        get
        {
            var cases = new TheoryData<string, string[]>();
            foreach (bool implicitCommand in new[] { false, true })
            foreach (bool query in new[] { false, true })
            foreach (string selector in new[] { "--versions", "--versions-with-feed" })
            {
                string[] prefix = implicitCommand ? [] : ["package"];
                string[] suffix = query ? ["-Q", "--json"] : [];
                foreach (string value in new[] { "2", "ordinary", "true", "false" })
                    cases.Add(selector, [.. prefix, "System.CommandLine", selector, value, .. suffix]);
                foreach (string separator in new[] { "=", ":" })
                foreach (string value in new[] { "2", "ordinary", "true", "false", "" })
                    cases.Add(selector, [.. prefix, "System.CommandLine", $"{selector}{separator}{value}", .. suffix]);
                foreach (string modifier in new[] { "--head", "--tail", "--lines", "--tail-lines" })
                {
                    // Text output keeps the separate format-compatibility guard out of this case.
                    string[] textSuffix = query ? ["-Q", "--markdown"] : [];
                    cases.Add(modifier, [.. prefix, "System.CommandLine", selector, "-n", "2", $"{modifier}=false", .. textSuffix]);
                    cases.Add(modifier, [.. prefix, "System.CommandLine", selector, "-2", modifier, "ordinary", .. textSuffix]);
                    cases.Add(modifier, [.. prefix, "System.CommandLine", selector, "-n2", modifier, "2", .. textSuffix]);
                }
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(PackageViolations))]
    public async Task PackageExplicitImplicitAndEarlyQueryRejectBeforeOutput(
        string name, string[] arguments) =>
        AssertRejected(await RunPackage(arguments), name);

    public static TheoryData<string[]> ValidPackageQueries
    {
        get
        {
            var cases = new TheoryData<string[]>();
            foreach (bool implicitCommand in new[] { false, true })
            foreach (string selector in new[] { "--versions", "--versions-with-feed" })
            foreach (string target in new[] { "System.CommandLine", "2", "2147483648", "true", "false" })
            {
                string[] prefix = implicitCommand ? [] : ["package"];
                cases.Add([.. prefix, selector, target, "-n", "2", "-Q", "--json"]);
                cases.Add([.. prefix, target, selector, "-2", "-Q", "--json"]);
                cases.Add([.. prefix, selector, "--head", target, "-n2", "-Q", "--json"]);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ValidPackageQueries))]
    public async Task PackageQueriesPreserveValidPositionalsAndNormalizedLimits(string[] arguments)
    {
        var result = await RunPackage(arguments);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.TryGetProperty("sections", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageUnrelatedSurplusIsNotReattributedByImplicitRouting(bool implicitCommand)
    {
        string[] prefix = implicitCommand ? [] : ["package"];
        var result = await RunPackage([.. prefix, "--versions", "First", "Second"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Multiple package inspection cannot be combined", result.Error);
        Assert.DoesNotContain("does not accept a value", result.Error);
    }

    [Theory]
    [InlineData("package", "--versions", "--", "false", "-Q")]
    [InlineData("package", "First", "--versions", "--", "Second")]
    public async Task PackageEndOfOptionsIsNotAnOptionOccurrence(params string[] arguments)
    {
        var result = await RunPackage(arguments);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.DoesNotContain("does not accept a value", result.Error);
        Assert.NotEmpty(result.Error);
    }

    [Theory]
    [InlineData("package", "First", "Second", "--json", "-Q")]
    [InlineData("package", "First", "--json", "false", "Second", "-Q", "--markdown")]
    [InlineData("package", "--out=--versions=true", "First", "-Q", "--json")]
    [InlineData("package", "--out", "--head", "First", "--versions", "-n", "2", "-Q", "--json")]
    [InlineData("package", "First", "--versions", "-n", "2", "--json", "false", "-Q", "--markdown")]
    public async Task PackageMultiInputAndOptionOwnedFlagTextRemainValid(params string[] arguments)
    {
        var result = await RunPackage(arguments);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.NotEmpty(result.Output);
    }

    private static Task<(int ExitCode, string Output, string Error)> RunPackage(string[] arguments) =>
        ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            if (CommandLineBuilder.TryGetStaleArgumentError(arguments, root, out string? error))
            {
                CommandError.Write(error!);
                return 1;
            }

            string[] processed = CommandLineBuilder.PreprocessArgs(arguments, root);
            return await CommandLineBuilder.InvokeWithLineWindowAsync(root.Parse(processed), processed);
        });

    private static void AssertRejected(
        (int ExitCode, string Output, string Error) result,
        string name)
    {
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal($"Error: {name} does not accept a value.{Environment.NewLine}", result.Error);
    }

    private sealed class Fixture
    {
        private readonly RootCommand _root = new();
        public Argument<string[]> Targets { get; }
        public ParseResult? Executed { get; private set; }

        public Fixture(int capacity = 1)
        {
            Targets = new("targets") { Arity = new ArgumentArity(0, capacity) };
            if (capacity == 1)
                _root.Arguments.Add(new Argument<string?>("target") { Arity = ArgumentArity.ZeroOrOne });
            else
                _root.Arguments.Add(Targets);
            _root.Options.Add(new Option<bool>("--flag", "-f") { Arity = ArgumentArity.Zero });
            _root.Options.Add(new Option<bool>("--switch", "-s") { Arity = ArgumentArity.Zero });
            _root.Options.Add(new Option<string?>("--presence") { Arity = ArgumentArity.Zero });
            _root.Options.Add(new Option<string>("--required", "-r") { Arity = ArgumentArity.ExactlyOne });
            _root.Options.Add(new Option<string?>("--optional") { Arity = ArgumentArity.ZeroOrOne });
            _root.Options.Add(new Option<bool>("--optional-bool"));
            _root.Options.Add(new Option<string[]>("--many")
            {
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            });
            _root.SetAction(result =>
            {
                Executed = result;
                Console.WriteLine("executed");
            });
        }

        public Task<(int ExitCode, string Output, string Error)> Run(params string[] arguments) =>
            ConsoleCapture.RunAsync(() =>
                CommandLineBuilder.InvokeWithLineWindowAsync(_root.Parse(arguments), arguments));
    }
}
