using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

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
    public async Task ExecuteList_AppliesItemWindow()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(
                SkillCommand.ExecuteList(
                    OutputFormat.Json,
                    rows: RowWindow.Head(1))));

        Assert.Equal(0, exitCode);
        using var document = System.Text.Json.JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetArrayLength());
        Assert.Equal(
            SkillCommand.Skills[0].Name,
            document.RootElement[0].GetProperty("skill").GetString());
    }

    [Fact]
    public async Task ListCommand_AppliesItemWindow()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(async () =>
        {
            string[] args = CommandLineBuilder.PreprocessArgs(
                ["skill", "list", "-n", "1", "--json"]);
            return await CommandLineBuilder.CreateRootCommand()
                .Parse(args)
                .InvokeAsync();
        });

        Assert.Equal(0, exitCode);
        using var document = System.Text.Json.JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListCommand_AppliesAbsoluteRange()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            string[] args = CommandLineBuilder.PreprocessArgs(
                ["skill", "list", "--rows", "2..3", "--json"]);
            return await CommandLineBuilder.CreateRootCommand()
                .Parse(args)
                .InvokeAsync();
        });

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = System.Text.Json.JsonDocument.Parse(output);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal(
            SkillCommand.Skills[1].Name,
            document.RootElement[0].GetProperty("skill").GetString());
    }

    [Fact]
    public async Task ListCommand_AppliesParentAbsoluteRange()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            string[] args = CommandLineBuilder.PreprocessArgs(
                ["skill", "--rows", "2..3", "list", "--json"]);
            return await CommandLineBuilder.CreateRootCommand()
                .Parse(args)
                .InvokeAsync();
        });

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = System.Text.Json.JsonDocument.Parse(output);
        Assert.Equal(2, document.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData("--lines")]
    [InlineData("--tail-lines")]
    public async Task ListCommand_AppliesRenderedLineWindow(string lineOption)
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            string[] args = CommandLineBuilder.PreprocessArgs(
                ["skill", "list", "-n", "1", lineOption]);
            var result = CommandLineBuilder.CreateRootCommand().Parse(args);
            return await CommandLineBuilder.InvokeAsync(result);
        });

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Single(output.TrimEnd('\n').Split('\n'));
    }

    [Fact]
    public async Task FocusedSkill_RejectsItemWindow()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            string[] args = CommandLineBuilder.PreprocessArgs(
                ["skill", "query", "-n", "1"]);
            var result = CommandLineBuilder.CreateRootCommand().Parse(args);
            return await CommandLineBuilder.InvokeAsync(result);
        });

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "-n selects items for commands with row windows",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootSkill_RejectsAbsoluteRangeTruthfully()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            string[] args = CommandLineBuilder.PreprocessArgs(
                ["skill", "--rows", "2..3"]);
            var result = CommandLineBuilder.CreateRootCommand().Parse(args);
            return await CommandLineBuilder.InvokeAsync(result);
        });

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "--rows absolute row ranges apply to 'skill list'",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("-n item windows", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FocusedSkill_RejectsNonPositiveWindow()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            string[] args = CommandLineBuilder.PreprocessArgs(
                ["skill", "query", "-n", "0"]);
            var result = CommandLineBuilder.CreateRootCommand().Parse(args);
            return await CommandLineBuilder.InvokeAsync(result);
        });

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("-n must be a positive integer.", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FocusedSkill_AppliesRenderedLineWindow()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            string[] args = CommandLineBuilder.PreprocessArgs(
                ["skill", "query", "-n", "1", "--lines"]);
            var result = CommandLineBuilder.CreateRootCommand().Parse(args);
            return await CommandLineBuilder.InvokeAsync(result);
        });

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Single(output.TrimEnd('\n').Split('\n'));
    }

    [Fact]
    public void BaselineSkill_IsAtMostFiftyLines()
    {
        var assembly = typeof(SkillCommand).Assembly;
        using var stream = assembly.GetManifestResourceStream(SkillCommand.RouterResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var lineCount = reader.ReadToEnd().TrimEnd('\n').Split('\n').Length;
        Assert.True(lineCount <= 50, $"baseline SKILL.md is {lineCount} lines (limit 50)");
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
