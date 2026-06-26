using DotnetInspector.Commands;

namespace DotnetInspector.Tests;

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
    public async Task ExecuteList_ListsEveryRegisteredSkill()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SkillCommand.ExecuteList()));

        Assert.Equal(0, exitCode);
        foreach (var skill in SkillCommand.Skills)
        {
            Assert.Contains(skill.Name, output);
        }
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
}
