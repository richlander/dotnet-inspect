using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class NestedTypeLeverageTests
{
    [Fact]
    public async Task TypeTopLeverage_ReturnsRowsForNestedType()
    {
        var nested = typeof(NestedLeverageHost.Inner);
        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = nested.FullName!.Replace('+', '.'),
            AssemblyPath = nested.Assembly.Location,
            IncludeSections = [SectionNames.TopLeverage],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Top Leverage", result.Output);
        // Shared is called by EntryA and EntryB on the nested type.
        Assert.Contains("`Shared()` | 2 |", result.Output);
    }
}

public class NestedLeverageHost
{
    public class Inner
    {
        public void EntryA() => Shared();

        public void EntryB() => Shared();

        public void Shared() => System.Console.WriteLine("x");
    }
}
