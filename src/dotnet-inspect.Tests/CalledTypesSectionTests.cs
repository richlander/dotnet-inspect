using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class CalledTypesSectionTests
{
    [Fact]
    public async Task TypeCalledTypes_AggregatesOutboundCalleeTypes()
    {
        var result = await RunCalledTypesAsync(typeof(CalledTypesFixture));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Called Types", result.Output);
        Assert.Contains("| Type | Assembly | Calls | Members | Call Kinds |", result.Output);
        Assert.Contains("`DotnetInspector.Tests.CalledTypesDependency`", result.Output);
        Assert.Contains("| dotnet-inspect.Tests | 3 | 2 | direct |", result.Output);
        Assert.Contains("`System.Console`", result.Output);
        Assert.Contains("`System.Math`", result.Output);
        Assert.DoesNotContain("CalledTypesFixture`", result.Output);
    }

    [Fact]
    public async Task TypeCalledTypes_UsesOpenGenericTypesAndExcludesGenericSelfCalls()
    {
        var result = await RunCalledTypesAsync(typeof(CalledTypesGenericFixture<>));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Called Types", result.Output);
        Assert.Contains("`System.Collections.Generic.List`", result.Output);
        Assert.Contains("| System.Collections | 2 | 1 |", result.Output);
        Assert.DoesNotContain("`DotnetInspector.Tests.CalledTypesGenericFixture", result.Output);
        Assert.DoesNotContain("`object`", result.Output);
        Assert.DoesNotContain("List<int>", result.Output);
        Assert.DoesNotContain("List<string>", result.Output);
    }

    [Fact]
    public async Task TypeCalledTypes_TsvUsesPlainAggregateColumns()
    {
        var result = await RunCalledTypesAsync(typeof(CalledTypesFixture), tsv: true);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("type\tassembly\tcalls\tmembers\tcall_kinds", result.Output);
        Assert.DoesNotContain('`', result.Output);
        Assert.Contains("DotnetInspector.Tests.CalledTypesDependency\tdotnet-inspect.Tests\t3\t2\tdirect", result.Output);
    }

    [Fact]
    public async Task TypeCalledTypes_RendersEmptyState_WhenExplicitlySelected()
    {
        var result = await RunCalledTypesAsync(typeof(CalledTypesEmptyFixture));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Called Types", result.Output);
        Assert.Contains("No called types found for this type.", result.Output);
    }

    [Fact]
    public async Task TypeEffectiveDiscovery_ListsCalledTypesAsOptIn()
    {
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(CalledTypesFixture).FullName,
            AssemblyPath = typeof(CalledTypesFixture).Assembly.Location,
            Discover = [],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            OneLine = true,
            Tsv = true,
            OneLineExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Called Types\tsection", result.Output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunCalledTypesAsync(Type type, bool tsv = false)
        => ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = type.FullName,
            AssemblyPath = type.Assembly.Location,
            IncludeSections = [SectionNames.CalledTypes],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            OneLine = tsv,
            Tsv = tsv,
            OneLineExplicitlySet = tsv,
            MarkdownExplicitlySet = !tsv,
            FormatExplicitlySet = true,
        }));
}

public static class CalledTypesFixture
{
    public static void First()
    {
        CalledTypesDependency.Touch();
        Console.WriteLine("first");
        SelfCall();
    }

    public static int Second(int value)
    {
        CalledTypesDependency.Touch();
        CalledTypesDependency.TouchOther();
        return Math.Abs(value);
    }

    static void SelfCall()
    {
    }
}

public static class CalledTypesDependency
{
    public static void Touch()
    {
    }

    public static void TouchOther()
    {
    }
}

public static class CalledTypesEmptyFixture
{
    public static int Identity(int value) => value;
}

public sealed class CalledTypesGenericFixture<T>
{
    public void First(List<int> values)
    {
        values.Add(1);
        Self();
    }

    public void Second(List<string> values)
    {
        values.Add("x");
        Self();
    }

    void Self()
    {
    }
}
