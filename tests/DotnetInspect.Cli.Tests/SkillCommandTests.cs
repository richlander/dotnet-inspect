using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class SkillCommandTests
{
    [Fact]
    public async Task Execute_PrintsRouterSkill()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.Execute()));

        Assert.Equal(0, exitCode);
        Assert.Contains("name: dotnet-inspect", output);
        Assert.Contains("## Skills", output);
    }

    [Fact]
    public async Task Execute_AppendsSkillsSectionAfterBaseline()
    {
        var (_, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.Execute()));

        // The generated skill list must come last so a head -n filter keeps the
        // high-value baseline content.
        var skillsIndex = output.IndexOf("## Skills", StringComparison.Ordinal);
        var commonStartsIndex = output.IndexOf("## Common starts", StringComparison.Ordinal);
        Assert.True(commonStartsIndex >= 0, "baseline content missing");
        Assert.True(skillsIndex > commonStartsIndex, "Skills section must follow baseline");

        // Every focused skill is listed in the generated section.
        foreach (var skill in SkillCommand.Skills)
        {
            Assert.Contains(skill.Name, output[skillsIndex..]);
        }
    }

    [Fact]
    public void BaselineSkill_IsAtMostSixtyLines()
    {
        var assembly = typeof(SkillCommand).Assembly;
        using var stream = assembly.GetManifestResourceStream(SkillCommand.RouterResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var lineCount = reader.ReadToEnd().TrimEnd('\n').Split('\n').Length;
        Assert.True(lineCount <= 60, $"baseline SKILL.md is {lineCount} lines (limit 60)");
    }

    [Fact]
    public void EverySkillFrontmatterHasNameAndDescription()
    {
        foreach (var skill in SkillCommand.Skills)
        {
            var name = SkillCommand.ReadFrontmatterValue(skill.ResourceName, "name");
            var description = SkillCommand.ReadFrontmatterValue(skill.ResourceName, "description");

            Assert.False(string.IsNullOrWhiteSpace(name), $"{skill.Name}: missing frontmatter name");
            Assert.StartsWith("dotnet-inspect-", name);
            Assert.False(string.IsNullOrWhiteSpace(description), $"{skill.Name}: missing frontmatter description");

            // The registry no longer stores descriptions; they come from YAML.
            Assert.Equal(description, skill.Description);
        }
    }

    [Fact]
    public async Task ExecuteList_UsesFrontmatterDescriptions()
    {
        var (_, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteList()));

        foreach (var skill in SkillCommand.Skills)
        {
            var description = SkillCommand.ReadFrontmatterValue(skill.ResourceName, "description")!;
            Assert.Contains(skill.Name, output);
            Assert.Contains(description, output);
        }
    }

    [Fact]
    public async Task ExecuteList_ListsEverySkillAsMarkdownH1()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteList()));

        Assert.Equal(0, exitCode);
        Assert.Contains("# Skills", output);
        Assert.DoesNotContain("## Skills", output); // standalone document => H1, not H2
        Assert.Contains("| Skill | Use it for |", output);
        foreach (var skill in SkillCommand.Skills)
        {
            Assert.Contains(skill.Name, output);
        }
    }

    [Fact]
    public async Task ExecuteList_Tsv_EmitsStableHeadersAndRows()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteList(OutputFormat.Tsv)));

        Assert.Equal(0, exitCode);
        Assert.Contains("skill\tuse_for", output);
        Assert.DoesNotContain("# Skills", output); // no markdown heading
        Assert.DoesNotContain("|", output);        // no markdown table
        foreach (var skill in SkillCommand.Skills)
        {
            Assert.Contains(skill.Name, output);
        }
    }

    [Fact]
    public async Task ExecuteList_Json_EmitsStructuredRows()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteList(OutputFormat.Json)));

        Assert.Equal(0, exitCode);
        Assert.Contains("\"skill\":", output.Replace(" ", ""));
        Assert.Contains("\"use_for\":", output.Replace(" ", ""));
        foreach (var skill in SkillCommand.Skills)
        {
            Assert.Contains(skill.Name, output);
        }
    }

    [Fact]
    public async Task ExecuteList_NoHeaders_OmitsHeaderRow()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteList(OutputFormat.Tsv, noHeader: true)));

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("skill\tuse_for", output);
        Assert.Contains("query", output);
    }

    public static IEnumerable<object[]> RegisteredSkills()
        => SkillCommand.Skills.Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(RegisteredSkills))]
    public async Task ExecuteSkill_PrintsRegisteredFocusedSkill(string name)
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteSkill(name)));

        Assert.Equal(0, exitCode);
        Assert.Contains($"name: dotnet-inspect-{name}", output);
    }

    [Fact]
    public async Task ExecuteSkill_UnknownName_FailsWithGuidance()
    {
        var (exitCode, _, error) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteSkill("does-not-exist")));

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown skill", error);
        Assert.Contains("skill list", error);
    }

    [Fact]
    public async Task ExecuteSkill_QueryDocumentsFindResultArray()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteSkill("query")));

        Assert.Equal(0, exitCode);
        Assert.Contains("plain `--json` retains the typed root result array", output);
    }

    [Fact]
    public async Task EmbeddedSkills_DistinguishApiAndPackageDiscovery()
    {
        var (_, router, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.Execute()));
        var (_, query, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteSkill("query")));
        var (_, packageSkills, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteSkill("package-skills")));

        Assert.Contains(
            "Use dotnet-inspect to find evidence",
            router);
        Assert.Contains(
            "installed .NET Runtime, ASP.NET Core, and .NET Standard",
            router);
        Assert.Contains("find JsonSerializer", router);
        Assert.Contains("find ControllerBase", router);
        Assert.Contains("find OptionsBuilder", router);
        Assert.Contains(
            "Use this skill to shape the result",
            query);
        Assert.Contains(
            "Use this workflow to reach the right package-authored skill",
            packageSkills);

        foreach (string output in new[] { router, query, packageSkills }
            .Select(text => string.Join(
                ' ',
                text.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries))))
        {
            Assert.Contains("router choose", output);
            Assert.Contains("installed .NET Runtime", output);
            Assert.Contains("ASP.NET Core", output);
            Assert.Contains(".NET Standard", output);
            Assert.Contains("Microsoft.Extensions", output);
            Assert.Contains("`--package", output);
            Assert.Contains("`--package-prefix", output);
            Assert.Contains("`--project", output);
            Assert.Contains("package query", output);
            Assert.Contains("library query", output);
            Assert.Contains("types", output);
            Assert.Contains("members", output);
        }

        Assert.Contains("find JsonSerializer -n 5 --table", router);
        Assert.Contains("find ControllerBase -n 5 --table", router);
        Assert.Contains("find OptionsBuilder -n 5 --table", router);
        Assert.Contains(
            "find IChatClient --package Microsoft.Extensions.AI.Abstractions",
            router);
    }

    [Fact]
    public async Task EmbeddedSkills_UseCurrentVersionSelectionCommands()
    {
        var (_, router, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.Execute()));
        var (_, compatibility, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteSkill("compatibility")));
        var (_, privateFeeds, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteSkill("private-feeds")));

        foreach (string output in new[] { router, compatibility, privateFeeds })
        {
            Assert.DoesNotContain("--latest-version", output);
            Assert.Contains("--versions -n 1", output);
        }

        Assert.Contains("--major-versions", router);
        Assert.Contains("--major-versions", compatibility);
        Assert.Contains("--major-versions", privateFeeds);
    }

    [Fact]
    public async Task ExecuteSkill_QueryDocumentsLegacyRowsLineComposition()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteSkill("query")));

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "Legacy `--rows` composes with an",
            output);
        Assert.Contains(
            "inferred or explicit rendered-line `-n`",
            output);
        Assert.DoesNotContain(
            "all legacy `--rows` forms reject `-n`",
            output);
    }

    [Fact]
    public async Task FocusedSkill_BareCountInfersFixedMarkdownLineSelection()
    {
        string? original =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                "json");
            string[] inferredArgs =
                ["skill", "query", "-n", "1"];
            var inferredParseResult =
                CommandLineBuilder.CreateRootCommand().Parse(inferredArgs);
            var inferred =
                await ConsoleCapture.RunAsync(
                    () => CommandLineBuilder.InvokeWithLineWindowAsync(
                        inferredParseResult,
                        inferredArgs));
            string[] explicitArgs =
                ["skill", "query", "-n", "1", "--lines"];
            var explicitParseResult =
                CommandLineBuilder.CreateRootCommand().Parse(explicitArgs);
            var explicitResult =
                await ConsoleCapture.RunAsync(
                    () => CommandLineBuilder.InvokeWithLineWindowAsync(
                        explicitParseResult,
                        explicitArgs));

            Assert.Equal(0, inferred.ExitCode);
            Assert.Empty(inferred.Error);
            Assert.Equal(explicitResult, inferred);
            Assert.Single(
                inferred.Output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                original);
        }
    }

    [Theory]
    [InlineData("skill")]
    [InlineData("focused")]
    public async Task SkillDocument_CountSelectsRenderedLines(string route)
    {
        string[] args =
            route == "skill"
                ? ["skill", "-n", "2"]
                : ["skill", "query", "-n", "2"];
        var parseResult =
            CommandLineBuilder.CreateRootCommand().Parse(args);
        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.InvokeWithLineWindowAsync(
                    parseResult,
                    args));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(
            2,
            output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task FocusedSkill_WithoutCountPrintsCompleteDocument()
    {
        string[] args = ["skill", "query"];
        var parseResult =
            CommandLineBuilder.CreateRootCommand().Parse(args);
        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.InvokeWithLineWindowAsync(
                    parseResult,
                    args));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("name: dotnet-inspect-query", output);
        Assert.Contains(
            "plain `--json` retains the typed root result array",
            output);
    }

    [Fact]
    public async Task FocusedSkill_BareCountAndTailSelectTrailingLines()
    {
        string[] args = ["skill", "query", "-n", "3", "--tail"];
        var parseResult =
            CommandLineBuilder.CreateRootCommand().Parse(args);
        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.InvokeWithLineWindowAsync(
                    parseResult,
                    args));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("selection.", output);
        Assert.DoesNotContain(
            "name: dotnet-inspect-query",
            output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkillList_BareCountSelectsRenderedLines(
        bool beforeSubcommand)
    {
        string[] args =
            beforeSubcommand
                ? ["skill", "-n", "1", "list"]
                : ["skill", "list", "-n", "1"];
        var parseResult =
            CommandLineBuilder.CreateRootCommand().Parse(args);
        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.InvokeWithLineWindowAsync(
                    parseResult,
                    args));

        Assert.Equal(0, exitCode);
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
    public async Task SkillList_ExplicitLinesAcceptsCountPlacement(
        bool beforeSubcommand)
    {
        string[] args =
            beforeSubcommand
                ? ["skill", "-n", "1", "--lines", "list"]
                : ["skill", "list", "-n", "1", "--lines"];
        var parseResult =
            CommandLineBuilder.CreateRootCommand().Parse(args);
        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.InvokeWithLineWindowAsync(
                    parseResult,
                    args));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Single(
            output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public async Task FocusedSkill_HelpDescribesLineItems()
    {
        string[] args = ["skill", "query", "--help"];
        var parseResult =
            CommandLineBuilder.CreateRootCommand().Parse(args);
        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.InvokeWithLineWindowAsync(
                    parseResult,
                    args));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            "Select rendered lines",
            output);
    }

    [Theory]
    [InlineData("--head")]
    [InlineData("--tail")]
    public async Task FocusedSkill_DirectionWithoutCountRejects(
        string direction)
    {
        string[] args = ["skill", "query", direction];
        var parseResult =
            CommandLineBuilder.CreateRootCommand().Parse(args);
        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.InvokeWithLineWindowAsync(
                    parseResult,
                    args));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains($"{direction} requires -n", error);
    }

    [Fact]
    public async Task EveryRegisteredSkillResourceResolves()
    {
        foreach (var skill in SkillCommand.Skills)
        {
            var (exitCode, _, _) = await ConsoleCapture.RunAsync(
                () => Task.FromResult(SkillCommand.ExecuteSkill(skill.Name)));
            Assert.Equal(0, exitCode);
        }
    }

    [Fact]
    public void FocusedSkillFilesRegistryAndEmbeddedResourcesAgree()
    {
        string root = FindRepositoryRoot();
        string skillsRoot = Path.Combine(root, "skills");

        string[] files = Directory
            .EnumerateDirectories(skillsRoot)
            .Where(path => !Path.GetFileName(path).Equals("dotnet-inspect", StringComparison.Ordinal))
            .Where(path => File.Exists(Path.Combine(path, "SKILL.md")))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;

        string[] registry = SkillCommand.Skills
            .Select(skill => skill.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        const string prefix = "dotnet-inspect.skills.";
        const string suffix = ".md";
        string[] resources = typeof(SkillCommand).Assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)
                && name.EndsWith(suffix, StringComparison.Ordinal))
            .Select(name => name[prefix.Length..^suffix.Length])
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(files, registry);
        Assert.Equal(files, resources);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
