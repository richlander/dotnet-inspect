using System.Text.Json;
using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// The CLI host of the Library API Reference
/// (docs/design/library-api-reference.md#production-adoption): the exact
/// <c>library -S "API Reference"</c> selection writes the document to stdout,
/// or publishes it to the <c>-o</c> file only when the write completes. These
/// run the product out of process, as an agent does, offline and with an
/// isolated cache.
/// </summary>
public partial class CommandExecutionTests
{
    [Fact]
    public async Task ApiReference_WritesTheDocumentToStdout()
    {
        using var library = ApiReferenceLibrary.Create(withPdb: false);

        var (exit, output, error) = await RunApiReferenceAsync(
            library,
            library.AssemblyPath);

        Assert.Equal(0, exit);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        Assert.Equal("complete", root.GetProperty("outcome").GetString());
        Assert.Equal("file", root.GetProperty("library").GetProperty("source_kind").GetString());
        Assert.Equal("public_api", root.GetProperty("request").GetProperty("admission").GetString());
        Assert.True(root.GetProperty("counts").GetProperty("members").GetInt32() > 0);

        // No PDB beside the file, and --offline forbids settlement's network
        // providers, so source_link settles offline and the document stays
        // complete.
        Assert.Equal("offline", SectionStatus(root, "source_link"));
        Assert.Contains("source_link is offline", error, StringComparison.Ordinal);

        // The same Library and request write the same bytes.
        var (_, again, _) = await RunApiReferenceAsync(
            library,
            library.AssemblyPath);
        Assert.Equal(output, again);
    }

    [Fact]
    public async Task ApiReference_ReadsALocalLibrarysSiblingPdb()
    {
        using var library = ApiReferenceLibrary.Create(withPdb: true);

        var (exit, output, _) = await RunApiReferenceAsync(
            library,
            library.AssemblyPath);

        Assert.Equal(0, exit);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        Assert.Equal("complete", SectionStatus(root, "source_link"));
        Assert.Contains(
            root.GetProperty("members").EnumerateArray(),
            member => member.GetProperty("source").GetProperty("first_statement_line").GetInt32() > 0);
    }

    [Fact]
    public async Task ApiReference_OutputFileIsReplacedOnlyByACompleteWrite()
    {
        using var library = ApiReferenceLibrary.Create(withPdb: false);
        string destination = Path.Combine(library.Directory, "library.api.json");
        File.WriteAllText(destination, "earlier document");
        string unreadable = Path.Combine(library.Directory, "NotManaged.dll");
        File.WriteAllText(unreadable, "not a PE image");

        // An unreadable Library aborts: the earlier file is unchanged and no
        // temporary file is left beside it.
        var (failedExit, failedOutput, failedError) = await RunApiReferenceAsync(
            library,
            unreadable,
            "-o",
            destination);

        Assert.Equal(1, failedExit);
        Assert.Empty(failedOutput);
        Assert.NotEmpty(failedError);
        Assert.Equal("earlier document", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(library.Directory, "*.tmp"));

        var (exit, output, _) = await RunApiReferenceAsync(
            library,
            library.AssemblyPath,
            "-o",
            destination);

        Assert.Equal(0, exit);
        Assert.Empty(output);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(destination));
        Assert.Equal("complete", document.RootElement.GetProperty("outcome").GetString());
        Assert.Empty(Directory.GetFiles(library.Directory, "*.tmp"));
    }

    [Fact]
    public async Task ApiReference_AllAdmitsEveryDeclaration()
    {
        using var library = ApiReferenceLibrary.Create(withPdb: false);

        var (_, publicOutput, _) = await RunApiReferenceAsync(
            library,
            library.AssemblyPath);
        var (exit, allOutput, _) = await RunApiReferenceAsync(
            library,
            library.AssemblyPath,
            "--all");

        Assert.Equal(0, exit);
        using JsonDocument publicApi = JsonDocument.Parse(publicOutput);
        using JsonDocument all = JsonDocument.Parse(allOutput);
        Assert.Equal("all", all.RootElement.GetProperty("request").GetProperty("admission").GetString());
        Assert.True(
            Count(all.RootElement, "types") > Count(publicApi.RootElement, "types"),
            "--all admits the non-public Types the public API omits.");

        static int Count(JsonElement root, string name) =>
            root.GetProperty("counts").GetProperty(name).GetInt32();
    }

    [Fact]
    public async Task ApiReference_AllReachesAPackageLibrary()
    {
        // A Package route rebuilds the Library request; --all must survive
        // it, as it does on a file.
        using var library = ApiReferenceLibrary.Create(withPdb: false);
        string content = Path.Combine(library.Directory, "content");
        string libraryDirectory = Path.Combine(content, "lib", "net11.0");
        Directory.CreateDirectory(libraryDirectory);
        File.Copy(
            library.AssemblyPath,
            Path.Combine(libraryDirectory, Path.GetFileName(library.AssemblyPath)));
        WriteAddressPackageManifest(content, "ApiReference.Package");
        string package = Path.Combine(library.Directory, "ApiReference.Package.1.0.0.nupkg");
        System.IO.Compression.ZipFile.CreateFromDirectory(content, package);

        var (exit, output, error) = await RunAppInDirectoryWithEnvironmentAsync(
            library.Directory,
            library.Environment,
            "--offline",
            "library",
            "--package",
            package,
            "-S",
            "API Reference",
            "--all");

        Assert.True(exit == 0, error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        Assert.Equal("nuget", root.GetProperty("library").GetProperty("source_kind").GetString());
        Assert.Equal("all", root.GetProperty("request").GetProperty("admission").GetString());
    }

    [Theory]
    [InlineData("--count")]
    [InlineData("--tsv")]
    public async Task ApiReference_RejectsProjections(string projection)
    {
        using var library = ApiReferenceLibrary.Create(withPdb: false);

        var (exit, output, error) = await RunApiReferenceAsync(
            library,
            library.AssemblyPath,
            projection);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("API Reference", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiReference_AllRequiresTheApiReferenceSection()
    {
        using var library = ApiReferenceLibrary.Create(withPdb: false);

        var (exit, output, error) = await RunAppInDirectoryWithEnvironmentAsync(
            library.Directory,
            library.Environment,
            "--offline",
            "library",
            library.AssemblyPath,
            "--all");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--all requires -S \"API Reference\"", error, StringComparison.Ordinal);
    }

    private static Task<(int Exit, string Output, string Error)> RunApiReferenceAsync(
        ApiReferenceLibrary library,
        string assemblyPath,
        params string[] extra) =>
        RunAppInDirectoryWithEnvironmentAsync(
            library.Directory,
            library.Environment,
            ["--offline", "library", assemblyPath, "-S", "API Reference", .. extra]);

    private static string? SectionStatus(JsonElement root, string name) =>
        root.GetProperty("section_outcomes").EnumerateArray()
            .Single(section => section.GetProperty("name").GetString() == name)
            .GetProperty("status").GetString();

    /// <summary>
    /// A small fixture Library copied into its own directory, with or
    /// without its sibling Portable PDB, and an empty cache.
    /// </summary>
    private sealed class ApiReferenceLibrary : IDisposable
    {
        private ApiReferenceLibrary(string directory, string assemblyPath)
        {
            Directory = directory;
            AssemblyPath = assemblyPath;
            Environment = new Dictionary<string, string?>
            {
                ["DOTNET_INSPECT_CACHE_DIR"] = System.IO.Path.Combine(directory, "cache"),
            };
        }

        internal string Directory { get; }
        internal string AssemblyPath { get; }
        internal IReadOnlyDictionary<string, string?> Environment { get; }

        internal static ApiReferenceLibrary Create(bool withPdb)
        {
            string source = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
            string directory = System.IO.Directory.CreateTempSubdirectory("api-reference-").FullName;
            string assembly = System.IO.Path.Combine(directory, System.IO.Path.GetFileName(source));
            File.Copy(source, assembly);
            if (withPdb)
            {
                File.Copy(
                    System.IO.Path.ChangeExtension(source, ".pdb"),
                    System.IO.Path.ChangeExtension(assembly, ".pdb"));
            }
            return new(directory, assembly);
        }

        public void Dispose() =>
            System.IO.Directory.Delete(Directory, recursive: true);
    }
}
