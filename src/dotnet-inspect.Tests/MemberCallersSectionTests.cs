using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class MemberCallersSectionTests
{
    [Fact]
    public async Task CallersSection_RendersOneRowPerCallSite()
    {
        var result = await RunMemberCallersAsync(typeof(MemberCallersFixture).FullName!, nameof(MemberCallersFixture.Target));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Callers", result.Output);
        Assert.Equal(1, CountOccurrences(result.Output, $"{nameof(MemberCallersFixture.CallsTargetOnce)}()"));
        Assert.Equal(2, CountOccurrences(result.Output, $"{nameof(MemberCallersFixture.CallsTargetTwice)}()"));
        Assert.Contains("| call |", result.Output);
        Assert.Contains("`IL_", result.Output);
        Assert.Contains("`0x06", result.Output);
    }

    [Fact]
    public async Task CallersSection_KeepsVirtualCallvirtAsDeclaredTarget()
    {
        var result = await RunMemberCallersAsync(typeof(CallersBase).FullName!, nameof(CallersBase.Speak));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"{nameof(MemberCallersFixture.InvokesSpeak)}(", result.Output);
        Assert.Contains("| callvirt |", result.Output);
    }

    [Fact]
    public async Task CallersSection_TsvUsesPlainNormalizedValues()
    {
        var result = await RunMemberCallersAsync(typeof(MemberCallersFixture).FullName!, nameof(MemberCallersFixture.Target), tsv: true);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("caller\tkind\til\ttoken", result.Output);
        Assert.DoesNotContain('`', result.Output);
        Assert.Contains($"{nameof(MemberCallersFixture.CallsTargetOnce)}()", result.Output);
    }

    [Fact]
    public async Task EffectiveDiscovery_ListsCallersForSelectedMember()
    {
        var result = await RunMemberCallersAsync(
            typeof(MemberCallersFixture).FullName!, nameof(MemberCallersFixture.Target), discover: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Callers\tsection", result.Output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunMemberCallersAsync(
        string typeName, string memberName, bool tsv = false, bool discover = false)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeName,
            AssemblyPath = typeof(MemberCallersFixture).Assembly.Location,
            MemberFilter = [memberName],
            IncludeSections = [SectionNames.Callers],
            TipLevel = TipLevel.Quiet,
            Discover = discover ? [] : null,
            Verbosity = Verbosity.Normal,
            OneLine = tsv || discover,
            Tsv = tsv || discover,
            OneLineExplicitlySet = tsv || discover,
            FormatExplicitlySet = true,
        }));

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}

public class CallersBase
{
    public virtual void Speak()
    {
    }
}

public static class MemberCallersFixture
{
    public static void Target()
    {
    }

    public static void CallsTargetOnce() => Target();

    public static void CallsTargetTwice()
    {
        Target();
        Target();
    }

    public static void InvokesSpeak(CallersBase thing) => thing.Speak();
}
