using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DotnetInspect.Cli.Tests;

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
        Assert.Contains("DotnetInspect.Cli.Tests.SampleDiscoveredUnion", result.Output);
        Assert.Contains("SampleUnionCat", result.Output);
        Assert.Contains("SampleUnionDog", result.Output);
        Assert.Contains("| Yes |", result.Output);
        Assert.DoesNotContain(nameof(SampleUnionLookalike), result.Output);
    }

    [Fact]
    public async Task LibraryUnionTypes_RealPackagePreservesNativeUnionInventory()
    {
        string packagePath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "SectionGrowth",
            "dotwasm.models.0.1.0.nupkg");

        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            PackagePath = packagePath,
            NamesakeLibrary = true,
            IncludeSections = ["Union Types"],
            Tabular = true,
            Jsonl = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
            Format = OutputFormat.Jsonl,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);

        string[] expectedTypes =
        [
            "DotWasm.Models.CompositeType",
            "DotWasm.Models.ExternalType",
            "DotWasm.Models.StorageType",
            "DotWasm.Models.WasmValueType",
        ];
        string[] actualTypes =
        [
            .. result.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(ReadType),
        ];

        Assert.Equal(
            expectedTypes.OrderBy(static type => type, StringComparer.Ordinal),
            actualTypes.OrderBy(static type => type, StringComparer.Ordinal));
    }

    static string ReadType(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement.GetProperty("type").GetString()!;
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
