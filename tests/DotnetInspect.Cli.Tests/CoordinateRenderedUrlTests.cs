using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class CoordinateRenderedUrlTests
{
    const string RepositoryPath =
        "richlander/dotnet-inspect/0cdbe500d11cb77ae7fb3c8612a5ba7bcc83ff86/src/ILInspector.SourceLink/SourceLinkService.cs";
    const string RawOrigin = "https://raw.githubusercontent.com/" + RepositoryPath;
    const string RawRoute =
        "https://github.com/richlander/dotnet-inspect/raw/0cdbe500d11cb77ae7fb3c8612a5ba7bcc83ff86/src/ILInspector.SourceLink/SourceLinkService.cs";
    const string Rendered =
        "https://github.com/richlander/dotnet-inspect/blob/0cdbe500d11cb77ae7fb3c8612a5ba7bcc83ff86/src/ILInspector.SourceLink/SourceLinkService.cs";
    const string Unmapped = "https://source.example/repository/raw/main/Source.cs";

    // PR-fast: bounded SourceLink URL matrix through the production coordinate command.
    [Theory]
    [InlineData(RawOrigin, RawOrigin, Rendered)]
    [InlineData(RawRoute, RawRoute, Rendered)]
    [InlineData(Unmapped, Unmapped, Unmapped)]
    [InlineData(RawRoute + "?plain=1#section", RawRoute + "?plain=1", Rendered + "?plain=1")]
    [InlineData(Unmapped + "?plain=1#section", Unmapped + "?plain=1", Unmapped + "?plain=1")]
    [InlineData(RawRoute + "#L999", RawRoute, Rendered)]
    [InlineData(RawRoute + "?path=%23section#old", RawRoute + "?path=%23section", Rendered + "?path=%23section")]
    public async Task CoordinateUrls_ApplyPreferenceAndPreserveLine(
        string sourceUrl,
        string fetchableUrl,
        string renderedUrl)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("coordinate-url-");
        try
        {
            string assemblyPath = Path.Combine(directory.FullName, "CoordinateUrlFixture.dll");
            WriteFixture(assemblyPath, sourceUrl);
            using var source = SourceLinkService.Open(assemblyPath);
            var location = Assert.IsType<SourceLinkResolver.ILOffsetSourceInfo>(
                source.ResolveByILOffset(0x06000001, 0));
            Assert.Equal(sourceUrl, location.SourceUrl);

            foreach (bool preferRendered in new[] { false, true })
            {
                string[] arguments =
                [
                    "library", "coordinate", "0x06000001+0x0",
                    "--library", assemblyPath,
                    "-S", "Context: Source Location", "--urls", "--tips", "q",
                    .. preferRendered ? new[] { "--prefer-rendered-urls" } : [],
                ];
                var root = CommandLineBuilder.CreateRootCommand();
                var (exit, output, error) = await ConsoleCapture.RunAsync(
                    () => CommandLineBuilder.InvokeAsync(root.Parse(arguments), arguments));

                Assert.Equal(0, exit);
                Assert.Empty(error);
                Assert.Equal(
                    $"{(preferRendered ? renderedUrl : fetchableUrl)}#L{location.Line}",
                    output.Trim());
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    static void WriteFixture(string assemblyPath, string sourceUrl)
    {
        const string documentPath = "/_/CoordinateUrlFixture.cs";
        var compilation = CSharpCompilation.Create(
            "CoordinateUrlFixture",
            [
                CSharpSyntaxTree.ParseText(
                    SourceText.From(
                        """
                        public static class CoordinateUrlFixture
                        {
                            public static int Echo(int value) => value;
                        }
                        """,
                        Encoding.UTF8),
                    path: documentPath),
            ],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true));
        using var assembly = File.Create(assemblyPath);
        using var sourceLink = new MemoryStream(
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                documents = new Dictionary<string, string> { [documentPath] = sourceUrl },
            }));
        EmitResult result = compilation.Emit(
            assembly,
            sourceLinkStream: sourceLink,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }
}
