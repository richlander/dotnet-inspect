using System.Text.Json;

using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public void
        AuthoredDocumentationAuthorizationUsesUserVerbosityBeforePromotion()
    {
        Assert.False(
            DocumentationEnricher.AuthorizesAuthoredDocumentation(
                new ApiOptions
                {
                    ShowDocs = true,
                    Verbosity = Verbosity.Detailed,
                    VerbosityExplicitlySet = true,
                    UserVerbosityOverride = Verbosity.Normal,
                }));
        Assert.True(
            DocumentationEnricher.AuthorizesAuthoredDocumentation(
                new ApiOptions
                {
                    ShowDocs = true,
                    Verbosity = Verbosity.Detailed,
                    VerbosityExplicitlySet = true,
                    UserVerbosityOverride = Verbosity.Detailed,
                }));
    }

    [Fact]
    public async Task
        Member_DirectLibrary_UsesCompiledDocumentationHouseForDeclaration()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "InspectWeb.DocumentationFixtures.WidgetExtensions",
            "Measure",
            "--library",
            FixtureCatalog.InspectWebDocumentation.AssemblyPath(),
            "-v:d",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "Methods that extend documentation fixture types.",
            output);
        Assert.Contains(
            "Measures a widget through its declaring extension member.",
            output);
    }

    [Fact]
    public async Task
        Member_DirectLibrary_UsesDeclarationDocsForProjectedExtension()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "InspectWeb.DocumentationFixtures.Widget",
            "Measure",
            "--library",
            FixtureCatalog.InspectWebDocumentation.AssemblyPath(),
            "-S",
            "Extension Methods",
            "--columns",
            "Signature;Description",
            "--tsv",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "Measures a widget through its declaring extension member.",
            output);
    }

    [Fact]
    public async Task
        Member_DirectLibraryIncludeAllResolvesNonPublicTypeDocumentation()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "InspectWeb.DocumentationFixtures.HiddenDocumentedType",
            "Read",
            "--library",
            FixtureCatalog.InspectWebDocumentation.AssemblyPath(),
            "--all",
            "-v:d",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "A non-public type retained by the browser accessibility surface.",
            output);
        Assert.Contains(
            "Reads documentation from a non-public type.",
            output);
    }

    [Fact]
    public async Task
        Type_DirectLibraryWithoutCompanion_PreservesNoDocumentationBehavior()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                $"cli-documentation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath =
            Path.Combine(
                directory,
                FixtureCatalog.InspectWebDocumentation.AssemblyFileName);
        File.Copy(
            FixtureCatalog.InspectWebDocumentation.AssemblyPath(),
            assemblyPath);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "type",
                "InspectWeb.DocumentationFixtures.Widget",
                "--library",
                assemblyPath,
                "-v:d",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.DoesNotContain(
                "A receiver for projected extension methods.",
                output);

            (exit, output, error) = await RunAppAsync(
                "type",
                "InspectWeb.DocumentationFixtures.Widget",
                "--library",
                assemblyPath,
                "-v:d",
                "--json",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using JsonDocument json = JsonDocument.Parse(output);
            Assert.False(
                json.RootElement.TryGetProperty(
                    "source_resolution",
                    out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task
        Type_MalformedDirectLibraryDocumentation_IsVisible()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                $"cli-documentation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath =
            Path.Combine(
                directory,
                FixtureCatalog.InspectWebDocumentation.AssemblyFileName);
        string xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        File.Copy(
            FixtureCatalog.InspectWebDocumentation.AssemblyPath(),
            assemblyPath);
        await File.WriteAllTextAsync(
            xmlPath,
            "<doc><members>",
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, _, error) = await RunAppAsync(
                "type",
                "InspectWeb.DocumentationFixtures.Widget",
                "--library",
                assemblyPath,
                "-v:d",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Contains(
                "Compiled documentation was malformed or unreadable",
                error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
