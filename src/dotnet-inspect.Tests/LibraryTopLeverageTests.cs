using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class LibraryTopLeverageTests
{
    [Fact]
    public async Task LibraryTopLeverage_RendersRankedTable()
    {
        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = typeof(LibraryLeverageFixture).Assembly.Location,
            IncludeSections = ["Top Leverage"],
            Markdown = true,
            Rows = 25,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Top Leverage", result.Output);
        Assert.Contains("| Member | Callers | Root Reach | Fanout | Depth | Loop Calls |", result.Output);
        // Assembly-wide rows are fully-qualified.
        Assert.Contains("| `DotnetInspector.Tests.", result.Output);
    }

    [Fact]
    public async Task LibraryTopLeverage_StaysSilentWhenNotSelected()
    {
        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = typeof(LibraryLeverageFixture).Assembly.Location,
            IncludeSections = ["Library Info"],
            Markdown = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("## Top Leverage", result.Output);
    }
}

public static class LibraryLeverageFixture
{
    public static void EntryA() => SharedHelper();

    public static void EntryB() => SharedHelper();

    public static void SharedHelper() => System.Console.WriteLine("x");
}
