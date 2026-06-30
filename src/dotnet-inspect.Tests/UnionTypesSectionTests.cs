using DotnetInspector.Commands;
using DotnetInspector.Options;
using System.Runtime.CompilerServices;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class UnionTypesSectionTests
{
    [Fact]
    public async Task LibraryUnionTypes_ListsUnionAttributeTypes()
    {
        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = typeof(SampleDiscoveredUnion).Assembly.Location,
            IncludeSections = ["Union Types"],
            Markdown = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Union Types", result.Output);
        Assert.Contains("DotnetInspector.Tests.SampleDiscoveredUnion", result.Output);
        Assert.Contains("SampleUnionCat", result.Output);
        Assert.Contains("SampleUnionDog", result.Output);
        Assert.Contains("| Yes |", result.Output);
        Assert.DoesNotContain(nameof(SampleUnionLookalike), result.Output);
    }
}

public sealed class SampleUnionCat;
public sealed class SampleUnionDog;

[Union]
public readonly struct SampleDiscoveredUnion : IUnion
{
    public SampleDiscoveredUnion(SampleUnionCat value) => Value = value;
    public SampleDiscoveredUnion(SampleUnionDog value) => Value = value;

    public object? Value { get; }
}

public sealed class SampleUnionLookalike : IUnion
{
    public object? Value => null;
}
