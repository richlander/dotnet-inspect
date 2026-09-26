using System.Text.Json;
using DotnetInspect.Cli;
using DotnetInspector.Services;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class TypeRelationsCommandTests
{
    [Fact]
    public async Task LocalLibrary_ImplementersPreserveProvenance()
    {
        var result = await ExecuteAsync(
            "type",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            typeof(TypeRelationsCommandTests).Assembly.Location,
            "-S",
            "Implementers",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement[] rows = [.. document.RootElement.EnumerateArray()];
        Assert.Equal(4, rows.Length);
        Assert.All(
            rows,
            row =>
            {
                Assert.Equal(
                    "interface",
                    row.GetProperty("relationship").GetString());
                Assert.Equal(
                    "DotnetInspect.Cli.Tests",
                    row.GetProperty("library").GetString());
                Assert.Equal(
                    "Library",
                    row.GetProperty("source").GetString());
            });
    }

    [Fact]
    public async Task LocalLibrary_DerivedTypesUseBaseTypeEvidence()
    {
        var result = await ExecuteAsync(
            "type",
            typeof(WorkspaceBase).FullName!,
            "--library",
            typeof(TypeRelationsCommandTests).Assembly.Location,
            "-S",
            "Derived Types",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            [typeof(WorkspaceDerived).FullName!],
            ReadJsonTypes(result.Output));
    }

    [Fact]
    public async Task LocalLibrary_ResolvesExternalInterfaceFocus()
    {
        var result = await ExecuteAsync(
            "type",
            typeof(IDisposable).FullName!,
            "--library",
            typeof(TypeRelationsCommandTests).Assembly.Location,
            "-S",
            "Implementers",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            typeof(WorkspaceDisposableImplementation).FullName!,
            ReadJsonTypes(result.Output));
    }

    [Fact]
    public async Task PinnedPlatformCoordinateExecutesRelations()
    {
        var (_, _, version, error) = PlatformResolver.ResolveAssembly(
            "System.Private.CoreLib",
            "runtime");
        Assert.Null(error);
        Assert.NotNull(version);
        var result = await ExecuteAsync(
            "type",
            typeof(Stream).FullName!,
            "--platform",
            "System.Private.CoreLib",
            "--framework",
            $"runtime@{version!}",
            "-S",
            "Derived Types",
            "--count");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.True(int.Parse(result.Output.Trim()) > 0);
    }

    [Fact]
    public async Task ProjectedJsonHonorsSelectedColumns()
    {
        var result = await ExecuteAsync(
            "type",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            typeof(TypeRelationsCommandTests).Assembly.Location,
            "-S",
            "Implementers",
            "--columns",
            "Type",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement rows = document.RootElement.GetProperty("implementers");
        Assert.All(
            rows.EnumerateArray(),
            row => Assert.Equal(
                ["type"],
                [.. row.EnumerateObject().Select(
                    static property => property.Name)]));
    }

    [Fact]
    public async Task CountAndRowsApplyToTypeCandidates()
    {
        string assembly = typeof(TypeRelationsCommandTests).Assembly.Location;
        string type = typeof(IWorkspaceImplementationMarker).FullName!;
        var count = await ExecuteAsync(
            "type",
            type,
            "--library",
            assembly,
            "-S",
            "Implementers",
            "--count",
            "--rows",
            "2..3");
        var rows = await ExecuteAsync(
            "type",
            type,
            "--library",
            assembly,
            "-S",
            "Implementers",
            "--json",
            "--rows",
            "2..3");

        Assert.Equal(0, count.ExitCode);
        Assert.Empty(count.Error);
        Assert.Equal("2", count.Output.Trim());
        Assert.Equal(0, rows.ExitCode);
        Assert.Empty(rows.Error);
        Assert.Equal(
            [
                typeof(WorkspaceImplementationA).FullName!,
                typeof(WorkspaceImplementationB).FullName!,
            ],
            ReadJsonTypes(rows.Output));
    }

    [Fact]
    public async Task GenericConstructionsProjectToDistinctTypeCandidates()
    {
        string assembly = typeof(TypeRelationsCommandTests).Assembly.Location;
        string type = typeof(IWorkspaceGenericMarker<>).FullName!;
        var count = await ExecuteAsync(
            "type",
            type,
            "--library",
            assembly,
            "-S",
            "Implementers",
            "--count");
        var rows = await ExecuteAsync(
            "type",
            type,
            "--library",
            assembly,
            "-S",
            "Implementers",
            "--json");

        Assert.Equal(0, count.ExitCode);
        Assert.Empty(count.Error);
        Assert.Equal("2", count.Output.Trim());
        Assert.Equal(0, rows.ExitCode);
        Assert.Empty(rows.Error);
        Assert.Equal(
            [
                typeof(WorkspaceGenericImplementation).FullName!,
                typeof(WorkspaceGenericImplementationB).FullName!,
            ],
            ReadJsonTypes(rows.Output));
    }

    [Fact]
    public async Task SemanticLimitAppliesBeforeCountAndJson()
    {
        string assembly = typeof(TypeRelationsCommandTests).Assembly.Location;
        string type = typeof(IWorkspaceImplementationMarker).FullName!;
        var count = await ExecuteAsync(
            "type",
            type,
            "--library",
            assembly,
            "-S",
            "Implementers",
            "-n",
            "1",
            "--count");
        var rows = await ExecuteAsync(
            "type",
            type,
            "--library",
            assembly,
            "-S",
            "Implementers",
            "-n",
            "1",
            "--json");

        Assert.Equal(0, count.ExitCode);
        Assert.Empty(count.Error);
        Assert.Equal("1", count.Output.Trim());
        Assert.Equal(0, rows.ExitCode);
        Assert.Empty(rows.Error);
        Assert.Single(ReadJsonTypes(rows.Output));
    }

    [Fact]
    public async Task UnsatisfiedSemanticWindowFailsBeforeCount()
    {
        var result = await ExecuteAsync(
            "type",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            typeof(TypeRelationsCommandTests).Assembly.Location,
            "-S",
            "Implementers",
            "--rows",
            "1..10",
            "--count");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires row 10, but only 4 candidate rows are available",
            result.Error);
    }

    [Theory]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    public async Task StructuredFormatsRenderRelationRows(string format)
    {
        var result = await ExecuteAsync(
            "type",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            typeof(TypeRelationsCommandTests).Assembly.Location,
            "-S",
            "Implementers",
            format);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            typeof(WorkspaceImplementation).FullName!,
            result.Output);
    }

    [Fact]
    public async Task DiscoveryAndQueryDescribeRelationsWithoutAcquisition()
    {
        var discovery = await ExecuteAsync(
            "type",
            nameof(Stream),
            "--platform",
            "System.Private.CoreLib",
            "-D",
            "@Relations",
            "--schema");
        var query = await ExecuteAsync(
            "type",
            nameof(Stream),
            "--platform",
            "System.Private.CoreLib",
            "-Q",
            "Implementers");

        Assert.Equal(0, discovery.ExitCode);
        Assert.Empty(discovery.Error);
        Assert.Contains("Implementers", discovery.Output);
        Assert.Contains("Derived Types", discovery.Output);
        Assert.Equal(0, query.ExitCode);
        Assert.Empty(query.Error);
        Assert.Contains("incoming", query.Output);
    }

    [Fact]
    public async Task InvalidLocalLibraryFailsVisibly()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(
            path,
            "not a managed assembly",
            TestContext.Current.CancellationToken);
        try
        {
            var result = await ExecuteAsync(
                "type",
                nameof(IDisposable),
                "--library",
                path,
                "-S",
                "Implementers",
                "--json");

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains("Unknown file format", result.Error);
        }

        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RetiredCommandPointsToTypeRelationSections()
    {
        Assert.True(
            CommandLineBuilder.TryGetRemovedCommandError(
                ["implements", nameof(IDisposable)],
                out string? error));
        Assert.Contains(
            "'implements' is no longer valid",
            error);
        Assert.Contains("-S Implementers", error);
    }

    private static async Task<(int ExitCode, string Output, string Error)>
        ExecuteAsync(params string[] arguments)
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string[] processed =
            CommandLineBuilder.PreprocessArgs(arguments, root);
        return await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed));
    }

    private static string[] ReadJsonTypes(string output)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        return
        [
            .. document.RootElement
                .EnumerateArray()
                .Select(row => row.GetProperty("type").GetString()!),
        ];
    }
}

public interface IWorkspaceImplementationMarker;

public sealed class WorkspaceImplementation :
    IWorkspaceImplementationMarker;

public sealed class WorkspaceImplementationA :
    IWorkspaceImplementationMarker;

public sealed class WorkspaceImplementationB :
    IWorkspaceImplementationMarker;

public sealed class WorkspaceImplementationC :
    IWorkspaceImplementationMarker;

public interface IWorkspaceGenericMarker<T>;

public sealed class WorkspaceGenericImplementation :
    IWorkspaceGenericMarker<int>,
    IWorkspaceGenericMarker<string>;

public sealed class WorkspaceGenericImplementationB :
    IWorkspaceGenericMarker<int>,
    IWorkspaceGenericMarker<string>,
    IWorkspaceGenericMarker<Guid>;

public abstract class WorkspaceBase;

public sealed class WorkspaceDerived : WorkspaceBase;

public sealed class WorkspaceDisposableImplementation : IDisposable
{
    public void Dispose()
    {
    }
}
