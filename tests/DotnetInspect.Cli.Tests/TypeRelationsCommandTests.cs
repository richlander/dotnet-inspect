using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
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
    public async Task SemanticSelectionUsesDisplayedTypeOrder()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ordered-relations-{Guid.NewGuid():N}.dll");
        await File.WriteAllBytesAsync(
            path,
            BuildOrderedHierarchyAssembly(),
            TestContext.Current.CancellationToken);
        try
        {
            var first = await ExecuteAsync(
                "type",
                "Probe.IContract",
                "--library",
                path,
                "-S",
                "Implementers",
                "--rows",
                "1..1",
                "--json");
            var last = await ExecuteAsync(
                "type",
                "Probe.IContract",
                "--library",
                path,
                "-S",
                "Implementers",
                "-n",
                "1",
                "--tail",
                "--json");

            Assert.Equal(0, first.ExitCode);
            Assert.Empty(first.Error);
            Assert.Equal(["Probe.Alpha"], ReadJsonTypes(first.Output));
            Assert.Equal(0, last.ExitCode);
            Assert.Empty(last.Error);
            Assert.Equal(["Probe.Zulu"], ReadJsonTypes(last.Output));
        }
        finally
        {
            File.Delete(path);
        }
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

    [Fact]
    public async Task IncompleteHierarchyEvidenceRemainsVisible()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"partial-relations-{Guid.NewGuid():N}.dll");
        await File.WriteAllBytesAsync(
            path,
            BuildPartialHierarchyAssembly(includeValidImplementer: false),
            TestContext.Current.CancellationToken);
        try
        {
            var json = await ExecuteAsync(
                "type",
                "Probe.IContract",
                "--library",
                path,
                "-S",
                "Implementers",
                "--json");
            var markdown = await ExecuteAsync(
                "type",
                "Probe.IContract",
                "--library",
                path,
                "-S",
                "Implementers");
            await File.WriteAllBytesAsync(
                path,
                BuildPartialHierarchyAssembly(includeValidImplementer: true),
                TestContext.Current.CancellationToken);
            var partialRows = await ExecuteAsync(
                "type",
                "Probe.IContract",
                "--library",
                path,
                "-S",
                "Implementers",
                "--json");

            Assert.Equal(1, json.ExitCode);
            Assert.Equal([], ReadJsonTypes(json.Output));
            Assert.Contains(
                "Subject Relations results are incomplete",
                json.Error);
            Assert.Contains("1 unavailable", json.Error);
            Assert.Equal(1, markdown.ExitCode);
            Assert.Contains(
                "available candidate evidence; inspection was incomplete",
                markdown.Output);
            Assert.Contains(
                "Subject Relations results are incomplete",
                markdown.Error);
            Assert.Equal(1, partialRows.ExitCode);
            Assert.Equal(
                ["Probe.Good"],
                ReadJsonTypes(partialRows.Output));
            Assert.Contains(
                "Subject Relations results are incomplete",
                partialRows.Error);
        }
        finally
        {
            File.Delete(path);
        }
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
    public async Task OrdinaryRelationDiscoveryRemainsStructural()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-relations-{Guid.NewGuid():N}.dll");
        var section = await ExecuteAsync(
            "type",
            "Probe.IContract",
            "--library",
            missingLibrary,
            "-D",
            "Implementers");
        var category = await ExecuteAsync(
            "type",
            "Probe.IContract",
            "--library",
            missingLibrary,
            "-D",
            "@Relations");
        var selected = await ExecuteAsync(
            "type",
            "Probe.IContract",
            "--library",
            missingLibrary,
            "-S",
            "Implementers",
            "-D",
            "--json");

        Assert.Equal(0, section.ExitCode);
        Assert.Empty(section.Error);
        Assert.Contains("Relationship", section.Output);
        Assert.Equal(0, category.ExitCode);
        Assert.Empty(category.Error);
        Assert.Contains("Implementers", category.Output);
        Assert.Contains("Derived Types", category.Output);
        Assert.Equal(0, selected.ExitCode);
        Assert.Empty(selected.Error);
        using JsonDocument document = JsonDocument.Parse(selected.Output);
        Assert.Equal(
            ["@Relations"],
            document.RootElement
                .EnumerateArray()
                .Select(row => row.GetProperty("name").GetString()));
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

    private static byte[] BuildPartialHierarchyAssembly(
        bool includeValidImplementer)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Partial.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Partial"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        TypeDefinitionHandle AddType(
            string name,
            TypeAttributes attributes) =>
            metadata.AddTypeDefinition(
                attributes,
                metadata.GetOrAddString("Probe"),
                metadata.GetOrAddString(name),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));

        AddType("<Module>", default);
        TypeDefinitionHandle contract = AddType(
            "IContract",
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract);
        if (includeValidImplementer)
        {
            metadata.AddInterfaceImplementation(
                AddType("Good", TypeAttributes.Public),
                contract);
        }
        TypeSpecificationHandle malformed =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(new byte[] { 0xff }));
        metadata.AddInterfaceImplementation(
            AddType("Bad", TypeAttributes.Public),
            malformed);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    private static byte[] BuildOrderedHierarchyAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Ordered.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Ordered"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        TypeDefinitionHandle AddType(
            string name,
            TypeAttributes attributes) =>
            metadata.AddTypeDefinition(
                attributes,
                metadata.GetOrAddString("Probe"),
                metadata.GetOrAddString(name),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));

        AddType("<Module>", default);
        TypeDefinitionHandle contract = AddType(
            "IContract",
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract);
        metadata.AddInterfaceImplementation(
            AddType("Zulu", TypeAttributes.Public),
            contract);
        metadata.AddInterfaceImplementation(
            AddType("Alpha", TypeAttributes.Public),
            contract);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
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
