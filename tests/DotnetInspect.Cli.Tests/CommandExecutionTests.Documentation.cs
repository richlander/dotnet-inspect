using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Theory]
    [InlineData(Verbosity.Quiet)]
    [InlineData(Verbosity.Minimal)]
    [InlineData(Verbosity.Normal)]
    [InlineData(Verbosity.Detailed)]
    public void AuthoredDocumentationRequiresExplicitDemand(Verbosity verbosity)
    {
        ApiOptions[] callers = [new ApiOptions(), new TypeOptions(), new MemberOptions()];
        foreach (ApiOptions caller in callers)
        {
            var automatic = caller with
            {
                ShowDocs = true,
                Verbosity = Verbosity.Detailed,
                VerbosityExplicitlySet = true,
                UserVerbosityOverride = verbosity,
            };
            Assert.False(DocumentationEnricher.AuthorizesAuthoredDocumentation(automatic));
            Assert.False(DocumentationEnricher.AuthorizesAuthoredDocumentation(
                automatic with { UserVerbosityOverride = null, Verbosity = verbosity }));
            Assert.True(DocumentationEnricher.AuthorizesAuthoredDocumentation(
                automatic with { DocsExplicitlySet = true }));
            Assert.False(DocumentationEnricher.AuthorizesAuthoredDocumentation(
                automatic with { DocsExplicitlySet = true, ShowDocs = false }));
            Assert.True(DocumentationEnricher.AuthorizesAuthoredDocumentation(
                automatic with { ShowDocs = false, ShowSamples = true }));
        }
    }

    // PR-fast: small cataloged documentation fixture through production Member routes.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Member_DefaultDocumentationIgnoresCompatibilityFlags(
        bool exact,
        bool showDocs)
    {
        var options = new MemberOptions
        {
            TypeName = "InspectWeb.DocumentationFixtures.WidgetExtensions",
            MemberFilter = ["Measure"],
            OverloadIndex = exact ? 1 : null,
            AssemblyPath = FixtureCatalog.InspectWebDocumentation.AssemblyPath(),
            ShowDocs = showDocs,
            DocsExplicitlySet = true,
            CompanionOutput = CompanionOutput.None,
        };
        var result = await ConsoleCapture.RunAsync(() =>
            MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Signature", result.Output);
        Assert.Contains("## Documentation", result.Output);
        Assert.Contains(
            "Measures a widget through its declaring extension member.",
            result.Output);
    }

    [Theory]
    [InlineData("Measure:1", SectionNames.Signature, "--table")]
    [InlineData("Measure:1", SectionNames.Signature, "--tsv")]
    [InlineData("Measure:1", SectionNames.Signature, "--jsonl")]
    [InlineData("Measure:1", SectionNames.Signature, "--json")]
    [InlineData("Measure", SectionNames.Overloads, "--jsonl")]
    [InlineData("Measure", SectionNames.Overloads, "--count")]
    [InlineData("Measure", SectionNames.DocumentedOverloads, "--count")]
    [InlineData("Measure", SectionNames.Methods, "--count")]
    public async Task Member_MetadataOnlySectionsDoNotReadDocumentation(
        string selector,
        string section,
        string terminal)
    {
        string directory = Path.Combine(
            Path.GetTempPath(), $"cli-member-demand-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(
            directory, FixtureCatalog.InspectWebDocumentation.AssemblyFileName);
        File.Copy(FixtureCatalog.InspectWebDocumentation.AssemblyPath(), assemblyPath);
        await File.WriteAllTextAsync(
            Path.ChangeExtension(assemblyPath, ".xml"),
            "<doc><members>",
            TestContext.Current.CancellationToken);
        try
        {
            var result = await RunAppAsync(
                [
                    "member",
                    "InspectWeb.DocumentationFixtures.WidgetExtensions",
                    selector,
                    "--library", assemblyPath,
                    "-S", section,
                    .. section == SectionNames.Signature && terminal != "--json"
                        ? new[] { "--columns", "Signature;Description" }
                        : [],
                    .. terminal is "--count" or "--json" ? new[] { "-v:d" } : [],
                    terminal,
                ]);

            Assert.Equal(0, result.Exit);
            Assert.Equal(
                section == SectionNames.Signature && terminal != "--json"
                    ? $"Note: 1 field has no data: Description{Environment.NewLine}"
                    : string.Empty,
                result.Error);
            Assert.DoesNotContain("## Documentation", result.Output);
            Assert.DoesNotContain("malformed", result.Output);
            if (terminal == "--count")
                Assert.Equal("1", result.Output.Trim());
            else if (terminal == "--json")
                Assert.Contains("WidgetExtensions", result.Output);
            else
                Assert.Contains("Measure", result.Output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("--table", "")]
    [InlineData("--tsv", "")]
    [InlineData("--jsonl", "")]
    [InlineData("--markdown", "-v:q")]
    [InlineData("--markdown", "-v:m")]
    [InlineData("--markdown", "-v:d")]
    public async Task Member_CompatibilitySectionOwnsDocumentation(
        string terminal,
        string verbosity)
    {
        var result = await RunAppAsync(
            [
                "member",
                "InspectWeb.DocumentationFixtures.WidgetExtensions",
                "Measure",
                "--library", FixtureCatalog.InspectWebDocumentation.AssemblyPath(),
                "-S", SectionNames.Methods,
                "--columns", "Signature;Description",
                .. verbosity.Length > 0 ? new[] { verbosity } : [],
                terminal,
            ]);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Contains(
            "Measures a widget through its declaring extension member.",
            result.Output);
    }

    [Fact]
    public async Task Member_ExactMalformedDocumentationRetainsDeclaration()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), $"cli-member-malformed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(
            directory, FixtureCatalog.InspectWebDocumentation.AssemblyFileName);
        File.Copy(FixtureCatalog.InspectWebDocumentation.AssemblyPath(), assemblyPath);
        await File.WriteAllTextAsync(
            Path.ChangeExtension(assemblyPath, ".xml"),
            "<doc><members>",
            TestContext.Current.CancellationToken);
        try
        {
            var result = await RunAppAsync(
                "member",
                "InspectWeb.DocumentationFixtures.WidgetExtensions.Measure:1",
                "--library", assemblyPath);

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains("## Signature", result.Output);
            Assert.Contains("Measure(", result.Output);
            Assert.Contains("## Documentation", result.Output);
            Assert.Contains(
                "Compiled documentation was malformed or unreadable.",
                result.Output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
            "-v:d");

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
        Member_DirectLibraryExactDocumentAttachesCompiledDocumentation()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "InspectWeb.DocumentationFixtures.WidgetExtensions.Measure:1",
            "--library",
            FixtureCatalog.InspectWebDocumentation.AssemblyPath());

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Signature", output);
        Assert.Contains("## Documentation", output);
        Assert.Contains(
            "Measures a widget through its declaring extension member.",
            output);
    }

    [Fact]
    public async Task
        Member_DirectLibraryGroupAttachesDocumentationOnlyToReturnedRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "InspectWeb.DocumentationFixtures.WidgetExtensions",
            "Measure",
            "--library",
            FixtureCatalog.InspectWebDocumentation.AssemblyPath(),
            "-v:d");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "| Signature | Description |",
            output);
        Assert.Contains(
            "Measures a widget through its declaring extension member.",
            output);
    }

    [Fact]
    public async Task
        Member_DirectLibraryWithoutCompanionKeepsUnavailableOutcomeVisible()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                $"cli-member-documentation-{Guid.NewGuid():N}");
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
                "member",
                "InspectWeb.DocumentationFixtures.WidgetExtensions.Measure:1",
                "--library",
                assemblyPath);

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains(
                "Compiled documentation is unavailable.",
                output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
            "--tsv");

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
            "-v:d");

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
                "-v:d");

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
                "--json");

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
                "-v:d");

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
