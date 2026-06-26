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

    [Theory]
    [InlineData("source")]
    [InlineData("performance")]
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
