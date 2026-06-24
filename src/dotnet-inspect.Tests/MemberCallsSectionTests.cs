using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class MemberCallsSectionTests
{
    [Fact]
    public async Task CallsSection_RendersOneRowPerCallSite()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsWriteLineTwice));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Calls", result.Output);
        Assert.Equal(2, CountOccurrences(result.Output, "`System.Console.WriteLine(string)`"));
        Assert.Contains("`IL_", result.Output);
        Assert.Contains("`0x0A", result.Output);
    }

    [Fact]
    public async Task CallsSection_KeepsInterfaceCallvirtAsDeclaredTarget()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsInterfaceItem));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("`System.Collections.Generic.IList<int>.get_Item(int)`", result.Output);
        Assert.Contains("| callvirt |", result.Output);
    }

    [Fact]
    public async Task CallsSection_SelectedSecondOverload_UsesSelectedMethod()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.Overloaded), overloadIndex: 2);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Calls", result.Output);
        Assert.Contains("`System.Console.WriteLine(string)`", result.Output);
    }

    [Fact]
    public async Task CallsSection_TsvUsesPlainNormalizedValues()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsWriteLineTwice), tsv: true);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("callee\tkind\til\ttoken", result.Output);
        Assert.DoesNotContain('`', result.Output);
        Assert.Equal(2, CountOccurrences(result.Output, "System.Console.WriteLine(string)"));
    }

    [Fact]
    public async Task EffectiveDiscovery_ListsCallsForSelectedMember()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsWriteLineTwice), discover: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Calls\tsection", result.Output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunMemberCallsAsync(string memberName, bool tsv = false, bool discover = false, int? overloadIndex = null)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallsFixture).FullName,
            AssemblyPath = typeof(MemberCallsFixture).Assembly.Location,
            MemberFilter = [memberName],
            OverloadIndex = overloadIndex,
            IncludeSections = [SectionNames.Calls],
            TipLevel = TipLevel.Quiet,
            Discover = discover ? [] : null,
            Verbosity = Verbosity.Minimal,
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

public static class MemberCallsFixture
{
    public static void CallsWriteLineTwice()
    {
        Console.WriteLine("first");
        Console.WriteLine("second");
    }

    public static int CallsInterfaceItem(IList<int> values) => values[0];

    public static void Overloaded(int value)
    {
    }

    public static void Overloaded(string value)
    {
        Console.WriteLine(value);
    }

    // Non-public member: only selectable under --all. Regression coverage for #1323,
    // where the body-load path counted overloads public-only and so reported "no IL body"
    // for a method that the Calls/IL index reads fine.
    internal static int InternalHelper(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}
