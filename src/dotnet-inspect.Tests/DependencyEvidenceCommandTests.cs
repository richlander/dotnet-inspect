using System.Buffers.Binary;
using System.Collections.Immutable;
using System.CommandLine;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using InertText;
using Markout;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace DotnetInspector.Tests;

/// <summary>
/// Gates for the <c>dependency-evidence</c> command contract owned by
/// <c>docs/design/dependency-evidence-cli.md</c>.
/// </summary>
[Collection("Console")]
public sealed class DependencyEvidenceCommandTests
{
    private static string NuspecFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath("manifest.nuspec");

    private static string AssetsFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath("project.assets.json");

    private static string ProjectDirectoryFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.ProjectDirectory();

    // ---- registration and routing -----------------------------------------

    [Fact]
    public void RootCommand_RegistersDependencyEvidenceWithNoAliasOrPositional()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();
        Command command = Assert.Single(
            root.Subcommands,
            subcommand => subcommand.Name == DependencyEvidenceCommand.Name);

        Assert.Empty(command.Aliases);
        Assert.Empty(command.Arguments);
        Assert.Contains(
            DependencyEvidenceCommand.Name,
            ArgumentPreprocessor.KnownCommands);
    }

    [Fact]
    public void Help_DistinguishesNormalizedEvidenceFromDependsTraversal()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();
        Command command = root.Subcommands.Single(subcommand =>
            subcommand.Name == DependencyEvidenceCommand.Name);

        Assert.Contains("direct", command.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("depends", command.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Preprocessor_DoesNotRouteTheCommandTokenToImplicitPackageInspection()
    {
        string[] processed = ArgumentPreprocessor.PreprocessArgs(
            [DependencyEvidenceCommand.Name, "--package", "Example"]);

        Assert.Equal(DependencyEvidenceCommand.Name, processed[0]);
    }

    [Fact]
    public async Task Router_SuggestsTheCommandForANearMiss()
    {
        (int exitCode, _, string error) = await ConsoleCapture.RunAsync(
            () => RunAsync(["dependency-evidenc"]));

        Assert.Equal(1, exitCode);
        Assert.Contains(DependencyEvidenceCommand.Name, error, StringComparison.Ordinal);
    }

    // ---- parse validation ---------------------------------------------------

    [Fact]
    public void Validate_RequiresAtLeastOneRoot()
    {
        Assert.False(ValidateCaptured(new DependencyEvidenceOptions(), out string error));
        Assert.Contains("--package-prefix", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsPrefixCombinedWithExplicitRoots()
    {
        var options = new DependencyEvidenceOptions
        {
            PackagePrefix = "Contoso.",
            Nuspecs = [NuspecFixture],
        };

        Assert.False(ValidateCaptured(options, out string error));
        Assert.Contains("--package-prefix cannot be combined", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsSourceOverridesWithoutARemotePackageTarget()
    {
        var options = new DependencyEvidenceOptions
        {
            Nuspecs = [NuspecFixture],
            SourceOptions = new NuGetSourceOptions
            {
                Sources = ["https://example.invalid/index.json"],
            },
        };

        Assert.False(ValidateCaptured(options, out string error));
        Assert.Contains("remote --package target", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AcceptsSourceOverridesWithARemotePackageTarget()
    {
        var options = new DependencyEvidenceOptions
        {
            Packages = ["Example.Package"],
            SourceOptions = new NuGetSourceOptions
            {
                Sources = ["https://example.invalid/index.json"],
            },
        };

        Assert.True(ValidateCaptured(options, out _));
    }

    [Fact]
    public void Validate_RejectsPrefixSourceOverridesAndBadBounds()
    {
        Assert.False(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    PackagePrefix = "Contoso.",
                    SourceOptions = new NuGetSourceOptions
                    {
                        AdditionalSources = ["https://example.invalid/index.json"],
                    },
                },
                out string sourceError));
        Assert.Contains("NuGet Gallery", sourceError, StringComparison.Ordinal);

        Assert.False(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    PackagePrefix = "Contoso.",
                    MaxPackages = 5_000,
                },
                out string boundError));
        Assert.Contains("--max-packages must be between 1 and 1000", boundError, StringComparison.Ordinal);

        Assert.False(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    Nuspecs = [NuspecFixture],
                    MaxPackages = 10,
                },
                out string unboundedError));
        Assert.Contains("--max-packages bounds --package-prefix", unboundedError, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsDefaultCountForPrefixButAllowsFailuresCount()
    {
        Assert.False(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    PackagePrefix = "Contoso.",
                    Count = true,
                },
                out string error));
        Assert.Contains("bounded profile is not an exhaustive package set", error, StringComparison.Ordinal);

        Assert.True(
            DependencyEvidenceCommand.Validate(
                new DependencyEvidenceOptions
                {
                    PackagePrefix = "Contoso.",
                    Count = true,
                },
                [DependencyEvidenceSections.Failures]));
    }

    [Fact]
    public void Validate_RejectsAnEmptyPrefixGestureRatherThanTreatingItAsAbsence()
    {
        Assert.False(
            ValidateCaptured(
                new DependencyEvidenceOptions { PackagePrefix = "" },
                out string aloneError));
        Assert.Contains(
            "--package-prefix must be 1 to 100 characters",
            aloneError,
            StringComparison.Ordinal);

        // Combined with an explicit root the gesture is still present, so exclusivity applies
        // rather than the prefix being silently ignored.
        Assert.False(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    PackagePrefix = "",
                    Nuspecs = [NuspecFixture],
                },
                out string combinedError));
        Assert.Contains(
            "--package-prefix cannot be combined",
            combinedError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyPrefix_IsRejectedAtTheProductEntry()
    {
        (int exitCode, _, string error) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--package-prefix", ""]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "--package-prefix must be 1 to 100 characters",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Only exactly one selected <c>Failures</c> section is structurally exact for prefix input,
    /// so a multi-section prefix count is rejected before anything is acquired.
    /// </summary>
    [Fact]
    public async Task PrefixCount_RejectsAMultiSectionViewBeforeAcquisition()
    {
        Assert.False(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    PackagePrefix = "Contoso.",
                    Count = true,
                },
                [
                    DependencyEvidenceSections.Failures,
                    DependencyEvidenceSections.Dependencies,
                ],
                out _));

        // The product entry rejects before acquisition too: nothing here reaches a source, and
        // prefix input admits no source override that could point one somewhere reachable.
        (int exitCode, _, string error) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--package-prefix",
            "Contoso.",
            "-S",
            DependencyEvidenceSections.Failures,
            "-S",
            DependencyEvidenceSections.Dependencies,
            "--count",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "bounded profile is not an exhaustive package set",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            $"Only '-S {DependencyEvidenceSections.Failures} --count' on its own is exact",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CommandDoesNotDeclareRowPredicatesOrOrdering()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();
        Command command = root.Subcommands.Single(subcommand =>
            subcommand.Name == DependencyEvidenceCommand.Name);
        string[] names = [.. command.Options.Select(option => option.Name)];

        Assert.DoesNotContain("--where", names);
        Assert.DoesNotContain("--order-by", names);
    }

    // ---- equivalent declared rows -------------------------------------------

    [Fact]
    public async Task PackageNuspecProjectLocatorAndDirectAssets_DeclareEquivalentDependencies()
    {
        DependencyEvidenceProjection nuspec = await ProjectAsync(
            new DependencyEvidenceOptions { Nuspecs = [NuspecFixture] });
        DependencyEvidenceProjection archive = await ProjectAsync(
            new DependencyEvidenceOptions { Packages = [CreateFixtureArchive()] });
        DependencyEvidenceProjection directAssets = await ProjectAsync(
            new DependencyEvidenceOptions { Projects = [AssetsFixture] });
        DependencyEvidenceProjection locator = await ProjectAsync(
            new DependencyEvidenceOptions { Projects = [ProjectDirectoryFixture] });

        Assert.Equal(Declared(nuspec), Declared(archive));
        Assert.Equal(Declared(nuspec), Declared(directAssets));
        Assert.Equal(Declared(directAssets), Declared(locator));

        Assert.Equal(
            PackageDependencyEvidenceSourceKind.PackageArchive,
            Assert.Single(archive.Roots).SourceKind);
        Assert.Equal(
            PackageDependencyEvidenceSourceKind.ProjectAssets,
            Assert.Single(directAssets.Roots).SourceKind);
        Assert.Equal(
            PackageDependencyEvidenceSourceKind.ProjectLocator,
            Assert.Single(locator.Roots).SourceKind);
    }

    [Fact]
    public async Task RestoredProjectRoot_AddsGraphEvidenceAndDisclosesItsSelectedTarget()
    {
        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions { Projects = [AssetsFixture] });

        Assert.NotEmpty(projection.RestoredEdges);
        Assert.NotEmpty(projection.RestoredPackages);
        Assert.Contains(
            projection.RestoredEdges,
            edge => edge.Role == RestoredProjectDependencyRole.Transitive);

        DependencyEvidenceRootRow root = Assert.Single(projection.Roots);
        Assert.Equal(DependencyEvidenceGraphState.Available, root.GraphState);
        Assert.NotNull(root.RestoredTargetFrameworkIdentity);
        Assert.NotNull(root.RestoredTargetFrameworkSpelling);
        // The fixture's default selection resolves a framework-only target, so the
        // disclosure carries no runtime identifier rather than hiding one.
        Assert.Equal(
            RestoredProjectTargetSelectionProvenance.Default,
            root.RestoredTargetProvenance);
        Assert.Null(root.RestoredRuntimeIdentifier);
    }

    [Fact]
    public async Task RestoredTargetRequest_SelectsOneTargetAndScopesItsGraph()
    {
        DependencyEvidenceProjection net10 = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Projects = [AssetsFixture],
                Tfm = "net10.0",
            });

        DependencyEvidenceRootRow root = Assert.Single(net10.Roots);
        Assert.Equal(
            RestoredProjectTargetSelectionProvenance.Requested,
            root.RestoredTargetProvenance);
        Assert.Equal("net10.0", root.RestoredTargetFrameworkIdentity);
    }

    [Fact]
    public async Task PackageTargetFrameworkRequest_SelectsWithoutFilteringDeclarations()
    {
        DependencyEvidenceProjection unscoped = await ProjectAsync(
            new DependencyEvidenceOptions { Nuspecs = [NuspecFixture] });
        DependencyEvidenceProjection scoped = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Nuspecs = [NuspecFixture],
                Tfm = "net10.0",
            });

        Assert.Equal(Declared(unscoped), Declared(scoped));
        Assert.Equal(
            PackageDependencyEvidenceSelectionStatus.Selected,
            Assert.Single(scoped.Roots).SelectionStatus);
        Assert.Contains(scoped.Dependencies, row => row.IsSelectedGroup);
        Assert.Contains(scoped.Dependencies, row => !row.IsSelectedGroup);

        DependencyEvidenceProjection unmatched = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Nuspecs = [NuspecFixture],
                Tfm = "net6.0",
            });

        Assert.Equal(Declared(unscoped), Declared(unmatched));
        Assert.Equal(
            PackageDependencyEvidenceSelectionStatus.NoMatchingTargetFramework,
            Assert.Single(unmatched.Roots).SelectionStatus);
    }

    // ---- verbosity, section discovery, and row shaping ----------------------

    [Fact]
    public async Task Verbosity_LaddersFromDocumentFieldsToDetailedSections()
    {
        (_, string quiet, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--project", AssetsFixture, "-v:q"]);
        (_, string minimal, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--project", AssetsFixture]);
        (_, string normal, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--project", AssetsFixture, "-v:n"]);
        (_, string detailed, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--project", AssetsFixture, "-v:d"]);

        Assert.Contains("| Root Set |", quiet, StringComparison.Ordinal);
        Assert.DoesNotContain("## ", quiet, StringComparison.Ordinal);

        Assert.Contains("## Dependencies", minimal, StringComparison.Ordinal);
        Assert.DoesNotContain("## Roots", minimal, StringComparison.Ordinal);

        Assert.Contains("## Roots", normal, StringComparison.Ordinal);
        Assert.Contains("## Restored Edges", normal, StringComparison.Ordinal);
        Assert.DoesNotContain("## Dependency Groups", normal, StringComparison.Ordinal);

        Assert.Contains("## Dependency Groups", detailed, StringComparison.Ordinal);
        Assert.Contains("## Restored Packages", detailed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discovery_ListsTheSchemaWithoutAcquiringRoots()
    {
        (int exitCode, string output, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "-D"]);

        Assert.Equal(0, exitCode);
        foreach (string section in DependencyEvidenceSections.SectionOrder)
            Assert.Contains(section, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectingAnEmptySectionRendersNoSyntheticRow()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "-S",
            DependencyEvidenceSections.RestoredEdges,
        ]);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("## Restored Edges", output, StringComparison.Ordinal);
        Assert.Contains("| Root Set |", output, StringComparison.Ordinal);
        Assert.Contains(
            "no admitted root carries restored graph evidence",
            error,
            StringComparison.Ordinal);
    }

    // ---- output-shape equivalence -------------------------------------------

    [Fact]
    public async Task TypedJson_CarriesIdentityProvenanceAndCompletion()
    {
        (int exitCode, string output, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--project", AssetsFixture, "-v:d", "--json"]);

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;

        Assert.Equal(
            "Complete",
            root.GetProperty("summary").GetProperty("root_set_completion").GetString());
        Assert.Equal(
            1,
            root.GetProperty("summary").GetProperty("admitted_roots").GetInt32());
        JsonElement first = root.GetProperty("roots")[0];
        Assert.Equal("RestoredProject", first.GetProperty("owner").GetString());
        Assert.Equal("ProjectAssets", first.GetProperty("source_kind").GetString());
        Assert.Equal("Complete", first.GetProperty("graph_completion").GetString());
        Assert.True(root.GetProperty("restored_edges").GetArrayLength() > 0);
        Assert.True(root.GetProperty("dependency_groups").GetArrayLength() > 0);
    }

    [Fact]
    public async Task LoweredJsonTableTsvAndJsonlSelectTheSameRows()
    {
        string[] baseArgs =
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--columns",
            "Package,Version",
        ];

        (_, string loweredJson, _) = await RunCapturedAsync([.. baseArgs, "--json"]);
        (_, string tsv, _) = await RunCapturedAsync([.. baseArgs, "--tsv"]);
        (_, string jsonl, _) = await RunCapturedAsync([.. baseArgs, "--jsonl"]);
        (_, string count, _) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--count",
        ]);

        using JsonDocument document = JsonDocument.Parse(loweredJson);
        int loweredRows = document.RootElement
            .GetProperty("dependencies")
            .GetArrayLength();
        int tsvRows = tsv
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Length - 1;
        int jsonlRows = jsonl
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.StartsWith('{'));

        Assert.Equal(loweredRows, tsvRows);
        Assert.Equal(loweredRows, jsonlRows);
        Assert.Equal(loweredRows, int.Parse(count.Trim()));
    }

    [Fact]
    public async Task RowWindowAppliesEquallyToEveryFormat()
    {
        (_, string tsv, _) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--tsv",
            "--columns",
            "Package",
            "--rows",
            "2",
        ]);
        (_, string jsonl, _) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--jsonl",
            "--columns",
            "Package",
            "--rows",
            "2",
        ]);
        (_, string count, _) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--count",
            "--rows",
            "2",
        ]);
        (_, string json, _) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--json",
            "--rows",
            "2",
        ]);
        (_, string loweredJson, _) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--json",
            "--columns",
            "Package",
            "--rows",
            "2",
        ]);
        (_, string markdown, _) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--rows",
            "2",
        ]);

        Assert.Equal(
            2,
            tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);
        Assert.Equal(
            2,
            jsonl
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.StartsWith('{')));
        Assert.Equal("2", count.Trim());
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            2,
            document.RootElement.GetProperty("dependencies").GetArrayLength());
        using JsonDocument lowered = JsonDocument.Parse(loweredJson);
        Assert.Equal(
            2,
            lowered.RootElement.GetProperty("dependencies").GetArrayLength());
        Assert.Equal(2, MarkdownDataRows(markdown, "Dependencies"));

        // The window selects rows; it never edits the document's completion fields.
        Assert.Contains("| Root Set | Complete |", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// A row window is per-row-set. The document's root-set and completion fields are not rows,
    /// so a partial outcome keeps reporting that it is partial even at the narrowest window and
    /// the quietest verbosity, where no section renders at all.
    /// </summary>
    [Fact]
    public async Task RowWindow_KeepsRootSetAndFailedRootsForAPartialOutcome()
    {
        (int exitCode, string markdown, _) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--nuspec",
            Path.Combine(CreateTemporaryDirectory(), "absent.nuspec"),
            "-v:q",
            "--rows",
            "1",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("| Root Set | Incomplete |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Failed Roots | 1 |", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every selected row set is windowed on its own, and the same window produces the same rows
    /// in the Markdown document and the typed JSON document.
    /// </summary>
    [Fact]
    public async Task RowWindow_WindowsEachSelectedRowSetIndependently()
    {
        string[] baseArgs =
        [
            DependencyEvidenceCommand.Name,
            "--project",
            AssetsFixture,
            "-v:d",
            "--rows",
            "1",
        ];

        (_, string markdown, _) = await RunCapturedAsync(baseArgs);
        (_, string json, _) = await RunCapturedAsync([.. baseArgs, "--json"]);

        Assert.Contains("| Root Set | Complete |", markdown, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        foreach ((string heading, string property) in new[]
        {
            (DependencyEvidenceSections.Dependencies, "dependencies"),
            (DependencyEvidenceSections.Roots, "roots"),
            (DependencyEvidenceSections.RestoredEdges, "restored_edges"),
            (DependencyEvidenceSections.DependencyGroups, "dependency_groups"),
            (DependencyEvidenceSections.RestoredPackages, "restored_packages"),
        })
        {
            Assert.Equal(1, MarkdownDataRows(markdown, heading));
            Assert.Equal(
                1,
                document.RootElement.GetProperty(property).GetArrayLength());
        }
    }

    // ---- containment --------------------------------------------------------

    [Fact]
    public void HostileManifestText_StaysContainedThroughMarkoutAndJson()
    {
        const string hostile = "Hostile\u202E\nPackage";
        var row = new DependencyEvidenceFailureRow(
            DependencyEvidenceFailurePhase.Root,
            "NotFound",
            PackageDependencyEvidenceSourceKind.DirectNuspec,
            null,
            null,
            null,
            null,
            null,
            new InertString(TextPolicy.Field, hostile),
            null,
            null,
            new InertString(TextPolicy.Field, hostile),
            new InertString(TextPolicy.Prose, hostile),
            1);
        DependencyEvidenceFailureView view = DependencyEvidenceFailureView.From(row);

        string subject = Assert.IsType<string>(view.Subject);
        string sourceLabel = Assert.IsType<string>(view.SourceLabel);
        Assert.DoesNotContain('\u202E', subject);
        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\u202E', sourceLabel);
        Assert.Equal(row.Subject!.Value.ToString(), subject);
    }

    [Fact]
    public async Task HostileNuspecText_IsContainedInEveryRenderedFormat()
    {
        string path = WriteTemporaryFile(
            "hostile.nuspec",
            Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>Hostile.Package</id>
                    <version>1.0.0</version>
                    <authors>Author</authors>
                    <description>Hostile fixture.</description>
                    <dependencies>
                      <group targetFramework="net8.0&#x202E;hostile">
                        <dependency id="Contoso.Dependency" version="[1.0.0]" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """));

        (int exitCode, string markdown, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--nuspec", path]);

        Assert.Equal(0, exitCode);
        Assert.Contains("Contoso.Dependency", markdown, StringComparison.Ordinal);
        HostileOutputAssert.NoRenderingHazard(markdown, "stdout");
        HostileOutputAssert.NoLineSplit(markdown, "Contoso.Dependency");

        (_, string typedJson, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--nuspec", path, "--json"]);
        (_, string loweredJson, _) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            path,
            "--json",
            "--columns",
            "TFM",
        ]);
        (_, string tsv, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--nuspec", path, "--tsv"]);

        foreach (string rendered in new[] { typedJson, loweredJson, tsv })
            HostileOutputAssert.NoRenderingHazard(rendered, "stdout");

        // The contained spelling survives as data: JSON escapes the backslash the
        // containment introduced, and TSV carries it literally.
        Assert.Contains(@"net8.0\\u202Ehostile", typedJson, StringComparison.Ordinal);
        Assert.Contains(@"net8.0\\u202Ehostile", loweredJson, StringComparison.Ordinal);
        Assert.Contains(@"net8.0\u202Ehostile", tsv, StringComparison.Ordinal);
    }

    // ---- package prefix (no live network) -----------------------------------

    [Fact]
    public async Task PackagePrefix_RetainsCompletionMatchesAndFailures()
    {
        using var source = new FakePrefixSource(
            [
                new SearchResult("Contoso.First", "1.0.0"),
                new SearchResult("Contoso.Missing", "2.0.0"),
            ],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso.first@1.0.0"] = Manifest(
                    "Contoso.First",
                    "1.0.0",
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Third.Party" version="[3.0.0]" />
                    </group>
                    """),
            });

        (PackageDependencyEvidenceRequest request, PackageProfileSummary summary) =
            await DependencyEvidenceAcquisition.AcquirePackagePrefixAsync(
                source,
                new PackagePrefixProfileRequest("Contoso.", 10),
                targetFramework: null,
                operationContext: null,
                TestContext.Current.CancellationToken);
        DependencyEvidenceProjection projection =
            DependencyEvidenceProjection.Create(
                PackageDependencyEvidenceQuery.Execute(request));

        Assert.Equal(2, summary.Candidates);
        Assert.Equal(1, summary.Matches);
        Assert.Equal(1, summary.Failures);
        Assert.NotNull(projection.Summary.PackagePrefix);
        Assert.Equal(2, projection.Summary.PackagePrefix!.Candidates);
        Assert.Single(projection.Dependencies);
        Assert.Contains(
            projection.Failures,
            failure => failure.Phase == DependencyEvidenceFailurePhase.PackageProfile);
        Assert.Equal(1, DependencyEvidenceCommand.ExitCode(projection));
    }

    [Fact]
    public async Task PackagePrefix_RequestedLimitTruncationSucceedsAndCountsFailuresExactly()
    {
        using var source = new FakePrefixSource(
            [new SearchResult("Contoso.First", "1.0.0")],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso.first@1.0.0"] = Manifest("Contoso.First", "1.0.0"),
            })
        {
            SearchTruncationReason = PackageSearchTruncationReason.RequestedLimit,
        };

        (PackageDependencyEvidenceRequest request, _) =
            await DependencyEvidenceAcquisition.AcquirePackagePrefixAsync(
                source,
                new PackagePrefixProfileRequest("Contoso.", 1),
                targetFramework: null,
                operationContext: null,
                TestContext.Current.CancellationToken);
        DependencyEvidenceProjection projection =
            DependencyEvidenceProjection.Create(
                PackageDependencyEvidenceQuery.Execute(request));

        Assert.Equal(0, DependencyEvidenceCommand.ExitCode(projection));
        Assert.False(
            DependencyEvidenceCommand.IsExactRowSet(
                projection,
                DependencyEvidenceSections.Dependencies));
        Assert.True(
            DependencyEvidenceCommand.IsExactRowSet(
                projection,
                DependencyEvidenceSections.Failures));
    }

    [Fact]
    public async Task PackagePrefix_PaginationTruncationFails()
    {
        using var source = new FakePrefixSource(
            [new SearchResult("Contoso.First", "1.0.0")],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso.first@1.0.0"] = Manifest("Contoso.First", "1.0.0"),
            })
        {
            SearchTruncationReason = PackageSearchTruncationReason.SourcePageLimit,
        };

        (PackageDependencyEvidenceRequest request, _) =
            await DependencyEvidenceAcquisition.AcquirePackagePrefixAsync(
                source,
                new PackagePrefixProfileRequest("Contoso.", 10),
                targetFramework: null,
                operationContext: null,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            1,
            DependencyEvidenceCommand.ExitCode(
                DependencyEvidenceProjection.Create(
                    PackageDependencyEvidenceQuery.Execute(request))));
    }

    // ---- partial outcomes and no restore ------------------------------------

    [Fact]
    public async Task UnrestoredProject_ReportsATypedFailureWithoutRestoringOrBuilding()
    {
        string directory = CreateTemporaryDirectory();
        string project = Path.Combine(directory, "Sample.csproj");
        File.WriteAllText(
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--project",
            project,
            "-S",
            DependencyEvidenceSections.Failures,
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("NotRestored", output, StringComparison.Ordinal);
        Assert.Contains("-S Failures", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(directory, "obj")));
        Assert.False(Directory.Exists(Path.Combine(directory, "bin")));
    }

    [Fact]
    public async Task PartialOutcome_StillRendersTheUsableDocument()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--nuspec",
            Path.Combine(CreateTemporaryDirectory(), "absent.nuspec"),
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("## Dependencies", output, StringComparison.Ordinal);
        Assert.Contains("| Failed Roots | 1 |", output, StringComparison.Ordinal);
        Assert.Contains("Warning:", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialOutcome_RejectsAnInexactCount()
    {
        (int exitCode, _, string error) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--nuspec",
            Path.Combine(CreateTemporaryDirectory(), "absent.nuspec"),
            "--count",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "--count cannot report an exact 'Dependencies' count",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>--columns</c>/<c>--fields</c> projection selects within the section row sets. The
    /// document's root-set and phase-completion fields are mandatory summary state, not rows, so
    /// a partial outcome keeps reporting that it is partial in both document-shaped sinks no
    /// matter what the caller projected.
    /// </summary>
    [Theory]
    [InlineData("--columns", "Package")]
    [InlineData("--fields", "Package")]
    public async Task Projection_KeepsMandatoryCompletionFields(
        string flag,
        string name)
    {
        string[] roots =
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            NuspecFixture,
            "--nuspec",
            Path.Combine(CreateTemporaryDirectory(), "absent.nuspec"),
            flag,
            name,
        ];

        (int exitCode, string markdown, _) = await RunCapturedAsync(roots);
        (_, string loweredJson, _) = await RunCapturedAsync([.. roots, "--json"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("| Root Set | Incomplete |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Failed Roots | 1 |", markdown, StringComparison.Ordinal);
        Assert.Contains(
            "| Complete Declarations | 1 |",
            markdown,
            StringComparison.Ordinal);

        using JsonDocument lowered = JsonDocument.Parse(loweredJson);
        JsonElement summary = lowered.RootElement.GetProperty("summary");
        Assert.Equal("Incomplete", summary.GetProperty("root_set").GetString());
        Assert.Equal("1", summary.GetProperty("failed_roots").GetString());
        Assert.Equal(
            "1",
            summary.GetProperty("complete_declarations").GetString());
    }

    /// <summary>
    /// The lowered JSON summary is a stable named object whose members are the same labels
    /// Markdown shows. A field table is a table, so lowering it as one would emit the summary
    /// under an anonymous key that no consumer can address.
    /// </summary>
    [Fact]
    public async Task LoweredJson_NamesTheSummaryRatherThanEmittingAnAnonymousKey()
    {
        (_, string loweredJson, _) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--project",
            AssetsFixture,
            "--columns",
            "*",
            "--json",
        ]);

        using JsonDocument lowered = JsonDocument.Parse(loweredJson);
        foreach (JsonProperty property in lowered.RootElement.EnumerateObject())
            Assert.NotEqual(string.Empty, property.Name);

        JsonElement summary = lowered.RootElement.GetProperty("summary");
        Assert.Equal(JsonValueKind.Object, summary.ValueKind);
        Assert.Equal("Complete", summary.GetProperty("root_set").GetString());
        Assert.Equal("1", summary.GetProperty("roots").GetString());
        Assert.Equal("1", summary.GetProperty("complete_graphs").GetString());
        Assert.Equal(
            4,
            lowered.RootElement.GetProperty("dependencies").GetArrayLength());
    }

    /// <summary>
    /// Optional prefix accounting is part of the same summary, so a package-prefix request keeps
    /// its prefix fields under a projection too.
    /// </summary>
    [Fact]
    public async Task Projection_KeepsPopulatedPrefixSummaryFields()
    {
        using var source = new FakePrefixSource(
            [new SearchResult("Contoso.First", "1.0.0")],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso.first@1.0.0"] = Manifest("Contoso.First", "1.0.0"),
            });

        (PackageDependencyEvidenceRequest request, _) =
            await DependencyEvidenceAcquisition.AcquirePackagePrefixAsync(
                source,
                new PackagePrefixProfileRequest("Contoso.", 10),
                targetFramework: null,
                operationContext: null,
                TestContext.Current.CancellationToken);
        DependencyEvidenceProjection projection =
            DependencyEvidenceProjection.Create(
                PackageDependencyEvidenceQuery.Execute(request));

        (_, string markdown, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(
                DependencyEvidenceCommand.Write(
                    projection,
                    new DependencyEvidenceOptions { Columns = ["Package"] },
                    [DependencyEvidenceSections.Dependencies])
                    ? 0
                    : 1));

        Assert.Contains("| Prefix | Contoso. |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Prefix Matches | 1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Root Set | Complete |", markdown, StringComparison.Ordinal);
    }

    // ---- owner-issued identity ----------------------------------------------

    /// <summary>
    /// Two explicit groups may name one framework. The projection must keep them apart by the
    /// owner's group identity and by a document-stable occurrence index, because the framework
    /// spelling alone identifies neither group nor the declarations inside it.
    /// </summary>
    [Fact]
    public async Task SameFrameworkExplicitGroups_StayDistinguishableByOwnerIdentity()
    {
        string path = WriteTemporaryFile(
            "twin-groups.nuspec",
            Manifest(
                "Contoso.Twin",
                "1.0.0",
                """
                <group targetFramework="net8.0">
                  <dependency id="Contoso.First" version="[1.0.0]" />
                </group>
                <group targetFramework="net8.0">
                  <dependency id="Contoso.Second" version="[2.0.0]" />
                </group>
                """));

        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions { Nuspecs = [path] });

        Assert.Equal(2, projection.DependencyGroups.Length);
        DependencyEvidenceGroupRow first = projection.DependencyGroups[0];
        DependencyEvidenceGroupRow second = projection.DependencyGroups[1];

        Assert.Equal("net8.0", first.CanonicalFramework);
        Assert.Equal("net8.0", second.CanonicalFramework);
        Assert.NotEqual(first.Identity, second.Identity);
        Assert.NotEqual(first.GroupIndex, second.GroupIndex);
        Assert.Equal(0, first.GroupIndex);
        Assert.Equal(1, second.GroupIndex);

        // The retained occurrences are the owner's, not a recomputed count.
        Assert.Equal(
            0,
            Assert.IsType<PackageDependencyEvidenceGroupOccurrence.Package>(
                Assert.Single(first.SourceOccurrences)).SourceIndex);
        Assert.Equal(
            1,
            Assert.IsType<PackageDependencyEvidenceGroupOccurrence.Package>(
                Assert.Single(second.SourceOccurrences)).SourceIndex);
        Assert.NotEqual(first.OrderKey, second.OrderKey);

        // Every declaration names the exact group it came from.
        DependencyEvidenceDependencyRow firstDeclaration = Assert.Single(
            projection.Dependencies,
            row => row.PackageId == "contoso.first");
        DependencyEvidenceDependencyRow secondDeclaration = Assert.Single(
            projection.Dependencies,
            row => row.PackageId == "contoso.second");
        Assert.Equal(first.GroupIndex, firstDeclaration.GroupIndex);
        Assert.Equal(first.Identity, firstDeclaration.GroupIdentity);
        Assert.Equal(second.GroupIndex, secondDeclaration.GroupIndex);
        Assert.Equal(second.Identity, secondDeclaration.GroupIdentity);
        Assert.Equal(
            first.Identity,
            firstDeclaration.DeclarationIdentity.Group);
        Assert.Equal(
            "contoso.first",
            firstDeclaration.DeclarationIdentity.CanonicalPackageId);
        Assert.Equal(projection.Roots[0].Identity, firstDeclaration.RootIdentity);
    }

    [Fact]
    public async Task HumanTables_NumberGroupsFromOne()
    {
        string path = WriteTemporaryFile(
            "twin-groups-human.nuspec",
            Manifest(
                "Contoso.Twin",
                "1.0.0",
                """
                <group targetFramework="net8.0">
                  <dependency id="Contoso.First" version="[1.0.0]" />
                </group>
                <group targetFramework="net8.0">
                  <dependency id="Contoso.Second" version="[2.0.0]" />
                </group>
                """));

        (_, string markdown, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--nuspec", path]);

        Assert.Contains("| Root | Group | TFM |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 1 | net8.0 | Contoso.First |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 2 | net8.0 | Contoso.Second |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypedJson_CarriesConcreteOwnerIssuedIdentityDtos()
    {
        (int exitCode, string output, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--project", AssetsFixture, "-v:d", "--json"]);

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;

        JsonElement rootRow = root.GetProperty("roots")[0];
        JsonElement selection = rootRow
            .GetProperty("identity")
            .GetProperty("restored_project");
        Assert.Equal(
            "RestoredProject",
            rootRow.GetProperty("identity").GetProperty("owner").GetString());
        Assert.Equal(64, selection.GetProperty("facts_digest").GetString()!.Length);
        Assert.Equal(
            selection.GetProperty("target_identity").GetString(),
            rootRow.GetProperty("restored_selection")
                .GetProperty("target_identity").GetString());

        JsonElement group = root.GetProperty("dependency_groups")[0];
        Assert.True(group.GetProperty("group").GetInt32() >= 0);
        Assert.False(string.IsNullOrEmpty(group.GetProperty("order_key").GetString()));
        Assert.NotEqual(0, group.GetProperty("occurrences").GetArrayLength());
        Assert.False(
            string.IsNullOrEmpty(
                group.GetProperty("identity")
                    .GetProperty("restored_project")
                    .GetProperty("pivot_identity").GetString()));

        JsonElement dependency = root.GetProperty("dependencies")[0];
        Assert.Equal(
            group.GetProperty("group").GetInt32(),
            dependency.GetProperty("group").GetInt32());
        Assert.False(
            string.IsNullOrEmpty(
                dependency.GetProperty("declaration_identity")
                    .GetProperty("canonical_package_id").GetString()));

        JsonElement edge = root.GetProperty("restored_edges")[0];
        Assert.False(
            string.IsNullOrEmpty(
                edge.GetProperty("identity")
                    .GetProperty("parent")
                    .GetProperty("kind").GetString()));
        Assert.False(
            string.IsNullOrEmpty(
                edge.GetProperty("identity")
                    .GetProperty("dependency")
                    .GetProperty("coordinate")
                    .GetProperty("package_id").GetString()));

        JsonElement node = root.GetProperty("restored_packages")[0];
        Assert.Equal(
            selection.GetProperty("facts_digest").GetString(),
            node.GetProperty("identity")
                .GetProperty("selection")
                .GetProperty("facts_digest").GetString());
    }

    /// <summary>
    /// A source association is opaque reference identity, so the document projects it as a
    /// deterministic request-local token beside the producer facts the source actually publishes.
    /// </summary>
    [Fact]
    public async Task TypedJson_ProjectsSourceAssociationAsARequestLocalToken()
    {
        using var source = new FakePrefixSource(
            [new SearchResult("Contoso.First", "1.0.0")],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso.first@1.0.0"] = Manifest("Contoso.First", "1.0.0"),
            });

        (PackageDependencyEvidenceRequest request, _) =
            await DependencyEvidenceAcquisition.AcquirePackagePrefixAsync(
                source,
                new PackagePrefixProfileRequest("Contoso.", 10),
                targetFramework: null,
                operationContext: null,
                TestContext.Current.CancellationToken);
        DependencyEvidenceProjection projection =
            DependencyEvidenceProjection.Create(
                PackageDependencyEvidenceQuery.Execute(request));

        (_, string output, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(
                DependencyEvidenceCommand.Write(
                    projection,
                    new DependencyEvidenceOptions
                    {
                        PackagePrefix = "Contoso.",
                        JsonOutput = true,
                        Verbosity = Verbosity.Normal,
                    },
                    [DependencyEvidenceSections.Roots])
                    ? 0
                    : 1));

        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement prefixSource = document.RootElement
            .GetProperty("summary")
            .GetProperty("package_prefix")
            .GetProperty("source");
        JsonElement rootSource = document.RootElement
            .GetProperty("roots")[0]
            .GetProperty("source");

        Assert.Equal(1, prefixSource.GetProperty("association").GetInt32());
        Assert.Equal(
            prefixSource.GetProperty("association").GetInt32(),
            rootSource.GetProperty("association").GetInt32());
        Assert.False(
            string.IsNullOrEmpty(prefixSource.GetProperty("producer_key").GetString()));
        Assert.False(
            string.IsNullOrEmpty(
                prefixSource.GetProperty("producer_display").GetString()));
        Assert.False(
            string.IsNullOrEmpty(
                prefixSource.GetProperty("transport_kind").GetString()));
    }

    /// <summary>A declaration failure the owner scoped to a group keeps that group identity.</summary>
    [Fact]
    public async Task ConflictingDeclarationFailure_RetainsItsGroupIdentity()
    {
        string path = WriteTemporaryFile(
            "conflicting.nuspec",
            Manifest(
                "Contoso.Conflicting",
                "1.0.0",
                """
                <group targetFramework="net8.0">
                  <dependency id="Contoso.Dependency" version="[1.0.0]" />
                  <dependency id="Contoso.Dependency" version="[2.0.0]" />
                </group>
                """));

        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions { Nuspecs = [path] });

        DependencyEvidenceFailureRow failure = Assert.Single(
            projection.Failures,
            row => row.Reason == "ConflictingPackageDeclaration");
        Assert.NotNull(failure.Group);
        Assert.Equal(0, failure.GroupIndex);
        Assert.Equal(
            Assert.Single(projection.DependencyGroups).Identity,
            failure.Group);
        Assert.Equal(projection.Roots[0].Identity, failure.RootIdentity);
    }

    /// <summary>
    /// A group-scoped declaration failure names its group in every rendered sink. Two explicit
    /// groups may name one framework, so the framework spelling alone cannot say which group
    /// the failure came from.
    /// </summary>
    [Fact]
    public async Task GroupScopedFailure_NamesItsGroupInEveryRenderedFormat()
    {
        string path = WriteTemporaryFile(
            "twin-groups-conflicting.nuspec",
            Manifest(
                "Contoso.Twin",
                "1.0.0",
                """
                <group targetFramework="net8.0">
                  <dependency id="Contoso.First" version="[1.0.0]" />
                </group>
                <group targetFramework="net8.0">
                  <dependency id="Contoso.Second" version="[1.0.0]" />
                  <dependency id="Contoso.Second" version="[2.0.0]" />
                </group>
                """));
        string[] baseArgs =
        [
            DependencyEvidenceCommand.Name,
            "--nuspec",
            path,
            "-S",
            DependencyEvidenceSections.Failures,
        ];

        (int exitCode, string markdown, _) = await RunCapturedAsync(baseArgs);
        (_, string tsv, _) = await RunCapturedAsync([.. baseArgs, "--tsv"]);
        (_, string jsonl, _) = await RunCapturedAsync([.. baseArgs, "--jsonl"]);
        (_, string loweredJson, _) = await RunCapturedAsync(
            [.. baseArgs, "--json", "--columns", "Reason,Group,Package"]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "| Phase | Reason | Source | Subject | Group |",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "| ConflictingPackageDeclaration | DirectNuspec | contoso.twin | 2 |",
            markdown,
            StringComparison.Ordinal);

        string[] tsvLines = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string[] headers = tsvLines[0].Split('\t');
        int groupColumn = Array.IndexOf(headers, "group");
        Assert.True(groupColumn >= 0, "The TSV row stream declares no group column.");
        Assert.Equal("2", tsvLines[1].Split('\t')[groupColumn]);

        using JsonDocument jsonlRow = JsonDocument.Parse(
            jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.StartsWith('{')));
        Assert.Equal("2", jsonlRow.RootElement.GetProperty("group").GetString());

        using JsonDocument lowered = JsonDocument.Parse(loweredJson);
        JsonElement failure = lowered.RootElement.GetProperty("failures")[0];
        Assert.Equal("2", failure.GetProperty("group").GetString());
        Assert.Equal(
            "ConflictingPackageDeclaration",
            failure.GetProperty("reason").GetString());
    }

    // ---- remote source fallback ---------------------------------------------

    /// <summary>
    /// Authorization is a list of sources. One source failing, omitting the coordinate, or
    /// serving a manifest the facts query rejects says nothing about the next source, so a later
    /// authorized source still admits the root.
    /// </summary>
    [Fact]
    public async Task RemoteSources_AdmitALaterSourceAfterAnEarlierInvalidManifest()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        (ImmutableArray<PackageDependencyEvidenceInput> roots,
            ImmutableArray<PackageDependencyEvidenceRootFailure> failures) =
            await AcquireFromSourcesAsync(
                coordinate,
                [
                    ("missing", MissingSource()),
                    ("invalid", InvalidManifestSource(coordinate)),
                    ("valid", ValidManifestSource(coordinate)),
                ]);

        Assert.Empty(failures);
        DependencyEvidenceProjection projection =
            DependencyEvidenceProjection.Create(
                PackageDependencyEvidenceQuery.Execute(
                    new PackageDependencyEvidenceRequest(roots, failures)));

        DependencyEvidenceRootRow root = Assert.Single(projection.Roots);
        Assert.Equal(
            PackageDependencyEvidenceSourceKind.PackageSourceManifest,
            root.SourceKind);
        Assert.Equal("contoso.fallback", root.PackageId);
        Assert.Equal(
            "contoso.dependency",
            Assert.Single(projection.Dependencies).PackageId);
    }

    /// <summary>
    /// When no authorized source succeeds, the terminal failure is the most informative one the
    /// existing algebra can carry: the last typed manifest failure with its established
    /// coordinate, reported as a package failure rather than a package-profile failure.
    /// </summary>
    [Fact]
    public async Task RemoteSources_ReportTheLastTypedManifestFailureWhenNoneSucceed()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        (ImmutableArray<PackageDependencyEvidenceInput> roots,
            ImmutableArray<PackageDependencyEvidenceRootFailure> failures) =
            await AcquireFromSourcesAsync(
                coordinate,
                [
                    ("invalid", InvalidManifestSource(coordinate)),
                    ("missing", MissingSource()),
                ]);

        Assert.Empty(roots);
        PackageDependencyEvidenceRootFailure.Package failure =
            Assert.IsType<PackageDependencyEvidenceRootFailure.Package>(
                Assert.Single(failures));
        Assert.Equal(
            PackageDependencyEvidenceSourceKind.PackageSourceManifest,
            failure.SourceKind);
        Assert.Equal(coordinate, failure.Coordinate);
        Assert.Equal(
            PackageManifestFailureReason.IdentityMismatch,
            failure.Failure.Reason);
    }

    /// <summary>Every authorized source being unavailable stays an acquisition failure.</summary>
    [Fact]
    public async Task RemoteSources_ReportSourceUnavailableWhenNoClientExists()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        var roots = ImmutableArray.CreateBuilder<PackageDependencyEvidenceInput>();
        var failures =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceRootFailure>();

        await DependencyEvidenceAcquisition.AcquireSourceManifestAsync(
            coordinate,
            [new PackageSource("unavailable", "https://unavailable.invalid/index.json")],
            _ => null,
            targetFramework: null,
            new InertString(TextPolicy.Field, "Contoso.Fallback@1.0.0"),
            operationContext: null,
            roots,
            failures,
            TestContext.Current.CancellationToken);

        Assert.Empty(roots);
        PackageDependencyEvidenceRootFailure.Acquisition failure =
            Assert.IsType<PackageDependencyEvidenceRootFailure.Acquisition>(
                Assert.Single(failures));
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionFailureReason.SourceUnavailable,
            failure.Reason);
    }

    /// <summary>
    /// Every attempted source answering with a typed <c>NotFound</c> is an authoritative
    /// absence claim, so the terminal reason is <c>NotFound</c> rather than the generic
    /// acquisition failure that also covers "some source was never heard from".
    /// </summary>
    [Fact]
    public async Task RemoteSources_ReportNotFoundWhenEverySourceReportsAbsence()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        (ImmutableArray<PackageDependencyEvidenceInput> roots,
            ImmutableArray<PackageDependencyEvidenceRootFailure> failures) =
            await AcquireFromSourcesAsync(
                coordinate,
                [("first", MissingSource()), ("second", MissingSource())]);

        Assert.Empty(roots);
        PackageDependencyEvidenceRootFailure.Acquisition failure =
            Assert.IsType<PackageDependencyEvidenceRootFailure.Acquisition>(
                Assert.Single(failures));
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionFailureReason.NotFound,
            failure.Reason);
    }

    /// <summary>
    /// An authorized source this build has no client for was never heard from, so a later
    /// source's typed absence claim does not speak for the whole set: the terminal reason is
    /// the generic acquisition failure rather than <c>NotFound</c>.
    /// </summary>
    [Fact]
    public async Task RemoteSources_ReportAcquisitionFailedWhenASourceHasNoClient()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        var roots = ImmutableArray.CreateBuilder<PackageDependencyEvidenceInput>();
        var failures =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceRootFailure>();
        using IPackageSourceClient missing = MissingSource();

        await DependencyEvidenceAcquisition.AcquireSourceManifestAsync(
            coordinate,
            [
                new PackageSource(
                    "unavailable",
                    "https://unavailable.invalid/v3/index.json"),
                new PackageSource(
                    "missing",
                    "https://missing.invalid/v3/index.json"),
            ],
            source => source.Name == "missing" ? missing : null,
            targetFramework: null,
            new InertString(TextPolicy.Field, "Contoso.Fallback@1.0.0"),
            operationContext: null,
            roots,
            failures,
            TestContext.Current.CancellationToken);

        Assert.Empty(roots);
        PackageDependencyEvidenceRootFailure.Acquisition failure =
            Assert.IsType<PackageDependencyEvidenceRootFailure.Acquisition>(
                Assert.Single(failures));
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionFailureReason.AcquisitionFailed,
            failure.Reason);
    }

    /// <summary>
    /// One source that could not answer makes the set non-authoritative: the coordinate may
    /// exist there, so the generic acquisition failure is retained rather than claiming absence.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoteSources_ReportAcquisitionFailedWhenAnySourceIsNotAuthoritative(
        bool throwsTransport)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        IPackageSourceClient inconclusive = throwsTransport
            ? UnreachableSource()
            : FailingSource(PackageSourceFailureKind.Transport);
        (ImmutableArray<PackageDependencyEvidenceInput> roots,
            ImmutableArray<PackageDependencyEvidenceRootFailure> failures) =
            await AcquireFromSourcesAsync(
                coordinate,
                [("missing", MissingSource()), ("inconclusive", inconclusive)]);

        Assert.Empty(roots);
        PackageDependencyEvidenceRootFailure.Acquisition failure =
            Assert.IsType<PackageDependencyEvidenceRootFailure.Acquisition>(
                Assert.Single(failures));
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionFailureReason.AcquisitionFailed,
            failure.Reason);
    }

    /// <summary>
    /// A typed manifest-validation failure stays more informative than either acquisition
    /// classification, even when every other attempted source reported absence.
    /// </summary>
    [Fact]
    public async Task RemoteSources_PreferATypedManifestFailureOverAbsence()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso.Fallback", "1.0.0");
        (_, ImmutableArray<PackageDependencyEvidenceRootFailure> failures) =
            await AcquireFromSourcesAsync(
                coordinate,
                [
                    ("missing", MissingSource()),
                    ("invalid", InvalidManifestSource(coordinate)),
                ]);

        Assert.IsType<PackageDependencyEvidenceRootFailure.Package>(
            Assert.Single(failures));
    }

    /// <summary>
    /// A local folder is an ordinary authorized source under the normal <c>--source</c> policy.
    /// An exact coordinate served from one is admitted and renders its dependency evidence
    /// without contacting a network.
    /// </summary>
    [Fact]
    public async Task LocalFolderSource_AdmitsAnExactPackageAndRendersItsEvidence()
    {
        string folder = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            folder,
            "Contoso.Local",
            "1.2.3",
            """
            <group targetFramework="net8.0">
              <dependency id="Contoso.Dependency" version="[1.0.0]" />
            </group>
            """);

        (int exitCode, string markdown, _) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--package",
            "Contoso.Local@1.2.3",
            "--source",
            folder,
        ]);

        Assert.Equal(0, exitCode);
        Assert.Contains("| Root Set | Complete |", markdown, StringComparison.Ordinal);
        Assert.Contains("Contoso.Dependency", markdown, StringComparison.Ordinal);
    }

    // ---- tabular arity and schema -------------------------------------------

    [Fact]
    public async Task DefaultTabularOutput_IsOneDependencyRowSchema()
    {
        (int exitCode, string tsv, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--project", AssetsFixture, "--tsv"]);

        Assert.Equal(0, exitCode);
        string[] lines = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("root\tgroup\tframework\tpackage", lines[0], StringComparison.Ordinal);
        Assert.All(
            lines,
            line => Assert.Equal(
                lines[0].Count(character => character == '\t'),
                line.Count(character => character == '\t')));

        // Document summary fields are a differently shaped record; a parsed row stream
        // carries the one row schema only.
        Assert.DoesNotContain("root_set_completion", tsv, StringComparison.Ordinal);
        Assert.DoesNotContain("admitted_roots", tsv, StringComparison.Ordinal);

        (_, string jsonl, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--project", AssetsFixture, "--jsonl"]);
        foreach (string line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using JsonDocument row = JsonDocument.Parse(line);
            Assert.False(row.RootElement.TryGetProperty("root_set", out _));
            Assert.False(row.RootElement.TryGetProperty("roots", out _));
            Assert.True(row.RootElement.TryGetProperty("package", out _));
        }
    }

    [Fact]
    public async Task TabularOutput_RejectsMoreThanOneSelectedTable()
    {
        (int exitCode, _, string error) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--project",
            AssetsFixture,
            "-S",
            DependencyEvidenceSections.Roots,
            "-S",
            DependencyEvidenceSections.Failures,
            "--tsv",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "requires exactly one selected table section",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-v:n")]
    [InlineData("-v:q")]
    public async Task TabularOutput_RejectsAMultiSectionOrFieldOnlyView(string verbosity)
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--project",
            AssetsFixture,
            verbosity,
            "--tsv",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("--tsv", error, StringComparison.Ordinal);
    }

    /// <summary>The arity rule is evaluated before any root is acquired.</summary>
    [Fact]
    public void TabularArity_IsDecidedFromTheStructuralCandidateSet()
    {
        Assert.True(
            ValidateTabularCaptured(
                new DependencyEvidenceOptions { Tabular = true, Tsv = true },
                [DependencyEvidenceSections.Dependencies],
                out _));
        Assert.False(
            ValidateTabularCaptured(
                new DependencyEvidenceOptions { Tabular = true, Tsv = true },
                [],
                out string emptyError));
        Assert.Contains("selects none", emptyError, StringComparison.Ordinal);

        // --count keeps its ordered section/count table.
        Assert.True(
            ValidateTabularCaptured(
                new DependencyEvidenceOptions
                {
                    Tabular = true,
                    Tsv = true,
                    Count = true,
                },
                [
                    DependencyEvidenceSections.Roots,
                    DependencyEvidenceSections.Failures,
                ],
                out _));
    }

    // ---- local archive safety -----------------------------------------------

    /// <summary>
    /// A local <c>.nupkg</c> is untrusted bytes. An archive whose end-of-central-directory
    /// declares more entries than the configured bound must be refused before anything
    /// enumerates it, and must land as a typed failed root rather than an exception.
    /// </summary>
    [Fact]
    public async Task LocalArchive_DeclaringTooManyEntries_IsATypedFailedRoot()
    {
        string path = WriteTemporaryFile(
            "Hostile.Package.1.0.0.nupkg",
            WithDeclaredEntryCount(
                File.ReadAllBytes(CreateFixtureArchive()),
                declared: (ushort)(PackagePayloadLimits.Default.MaxEntryCount + 10_000)));

        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions { Packages = [path] });

        Assert.Empty(projection.Roots);
        Assert.Empty(projection.Dependencies);
        Assert.Equal(1, projection.Summary.FailedRootCount);
        DependencyEvidenceFailureRow failure = Assert.Single(projection.Failures);
        Assert.Equal(DependencyEvidenceFailurePhase.Root, failure.Phase);
        Assert.Equal(
            PackageDependencyEvidenceSourceKind.PackageArchive,
            failure.SourceKind);
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionFailureReason.ProducerContract
                .ToString(),
            failure.Reason);
        Assert.Equal(1, DependencyEvidenceCommand.ExitCode(projection));

        // The partial document still renders, and the typed rejection carries no archive bytes.
        // The validator's own detailed reason is deliberately not retained: naming it would
        // require widening the host-neutral acquisition failure algebra.
        (int exitCode, _, string error) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--package",
            path,
            "--nuspec",
            NuspecFixture,
        ]);
        Assert.Equal(1, exitCode);
        Assert.Contains("Warning:", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalArchive_StillDeclaresItsDependencies()
    {
        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions { Packages = [CreateFixtureArchive()] });

        Assert.Single(projection.Roots);
        Assert.NotEmpty(projection.Dependencies);
        Assert.Empty(projection.Failures);
    }

    // ---- --preview ----------------------------------------------------------

    [Fact]
    public void Validate_PreviewRequiresALatestRemotePackageTarget()
    {
        Assert.True(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    Packages = ["Contoso.Package"],
                    IncludePrerelease = true,
                },
                out _));

        Assert.True(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    Packages = ["Contoso.Exact@1.0.0", "Contoso.Latest"],
                    IncludePrerelease = true,
                },
                out _));

        Assert.False(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    Packages = ["Contoso.Exact@1.0.0"],
                    IncludePrerelease = true,
                },
                out string exactError));
        Assert.Contains(
            "already names an exact version",
            exactError,
            StringComparison.Ordinal);

        Assert.False(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    PackagePrefix = "Contoso.",
                    IncludePrerelease = true,
                },
                out string prefixError));
        Assert.Contains("--package-prefix", prefixError, StringComparison.Ordinal);

        Assert.False(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    Packages = ["Contoso.Package.1.0.0.nupkg"],
                    IncludePrerelease = true,
                },
                out string localError));
        Assert.Contains(
            "--preview applies only to latest remote",
            localError,
            StringComparison.Ordinal);

        Assert.False(
            ValidateCaptured(
                new DependencyEvidenceOptions
                {
                    Nuspecs = [NuspecFixture],
                    IncludePrerelease = true,
                },
                out string nuspecError));
        Assert.Contains(
            "--preview applies only to latest remote",
            nuspecError,
            StringComparison.Ordinal);
    }

    // ---- discovery-only options ---------------------------------------------

    [Fact]
    public async Task SchemaAndTreeRequireDiscovery()
    {
        (int schemaExit, _, string schemaError) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--nuspec", NuspecFixture, "--schema"]);
        Assert.Equal(1, schemaExit);
        Assert.Contains("--schema requires -D/--discover.", schemaError, StringComparison.Ordinal);

        (int treeExit, _, string treeError) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "--nuspec", NuspecFixture, "--tree"]);
        Assert.Equal(1, treeExit);
        Assert.Contains("--tree requires -D/--discover.", treeError, StringComparison.Ordinal);

        (int discoverExit, _, _) = await RunCapturedAsync(
            [DependencyEvidenceCommand.Name, "-D", "--schema"]);
        Assert.Equal(0, discoverExit);
    }

    // ---- partial failure and cancellation -----------------------------------

    /// <summary>
    /// One unusable project root fails that root only. The remaining roots keep producing
    /// evidence, which is what makes a heterogeneous root set usable at all.
    /// </summary>
    [Fact]
    public async Task UnusableProjectPath_FailsOnlyThatRoot()
    {
        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Projects = ["invalid\u0000path/Sample.csproj", AssetsFixture],
            });

        Assert.Single(projection.Roots);
        Assert.NotEmpty(projection.Dependencies);
        Assert.Equal(1, projection.Summary.FailedRootCount);
        DependencyEvidenceFailureRow failure = Assert.Single(projection.Failures);
        Assert.Equal(
            PackageDependencyEvidenceSourceKind.ProjectLocator,
            failure.SourceKind);
    }

    [Fact]
    public async Task Cancellation_PropagatesRatherThanBecomingADiagnosedFailure()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DependencyEvidenceCommand.ExecuteAsync(
                new DependencyEvidenceOptions { Projects = [AssetsFixture] },
                cancellation.Token));
    }

    // ---- explicit root gestures ---------------------------------------------

    /// <summary>
    /// A named root that names nothing is one typed failed root. It neither ends the request
    /// before its siblings are acquired nor reaches source resolution, which rejects a blank
    /// package id by throwing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankPackageTarget_IsATypedFailedRootAndKeepsItsSibling(
        string package)
    {
        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Packages = [package],
                Nuspecs = [NuspecFixture],
            });

        Assert.Single(projection.Roots);
        Assert.NotEmpty(projection.Dependencies);
        Assert.Equal(1, projection.Summary.FailedRootCount);
        DependencyEvidenceFailureRow failure = Assert.Single(projection.Failures);
        Assert.Equal(
            PackageDependencyEvidenceSourceKind.PackageSourceManifest,
            failure.SourceKind);
        Assert.Equal("ProducerContract", failure.Reason);
    }

    /// <summary>
    /// <c>ID@</c> is an explicit exact-version gesture whose version is empty. Normalizing that
    /// spelling away would silently rebind the target to floating latest, so it is refused by
    /// the coordinate grammar before any source or version resolution is reached.
    /// </summary>
    [Theory]
    [InlineData("Contoso.Pinned@")]
    [InlineData("Contoso.Pinned@ ")]
    [InlineData("Contoso.Pinned@1.0.*")]
    public async Task ExplicitEmptyOrMalformedVersion_IsAProducerContractFailure(
        string package)
    {
        var authorization = new RecordingPackageSourceAuthorization(
            PackageSourceAuthorization.Authorize([StubFeed]));
        bool resolved = false;
        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Packages = [package],
                Nuspecs = [NuspecFixture],
            },
            authorization,
            resolveCoordinate: (_, _, _, _) =>
            {
                resolved = true;
                return Task.FromResult<PackageCoordinateResolution>(
                    new PackageCoordinateResolution.Unavailable("unused"));
            });

        // The grammar decides admissibility first, so a refused target asks no host for a
        // source policy and never reaches version resolution.
        Assert.Empty(authorization.PackageIds);
        Assert.False(resolved, "A malformed target must not reach version resolution.");
        Assert.Single(projection.Roots);
        DependencyEvidenceFailureRow failure = Assert.Single(projection.Failures);
        Assert.Equal("ProducerContract", failure.Reason);
    }

    /// <summary>
    /// An admissible target asks the host's source policy exactly once, under the canonical
    /// lowercase identity, so every spelling of one package id gets that host's single answer
    /// — and the authorized producers, not a widened set, are what version resolution sees.
    /// </summary>
    [Theory]
    [InlineData("Contoso.Pinned@1.0.0")]
    [InlineData("CONTOSO.PINNED@1.0.0")]
    [InlineData("contoso.pinned@1.0.0")]
    public async Task ValidPackageTarget_AuthorizesTheCanonicalIdentityOnce(
        string package)
    {
        var authorization = new RecordingPackageSourceAuthorization(
            PackageSourceAuthorization.Authorize([StubFeed]));
        List<IReadOnlyList<PackageSource>> resolvedAgainst = [];

        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Packages = [package],
                Nuspecs = [NuspecFixture],
            },
            authorization,
            resolveCoordinate: (_, sources, _, _) =>
            {
                resolvedAgainst.Add(sources);
                return Task.FromResult<PackageCoordinateResolution>(
                    new PackageCoordinateResolution.Unavailable(
                        "the stub feed answered nothing"));
            });

        Assert.Equal(["contoso.pinned"], authorization.PackageIds);
        Assert.Equal(
            [StubFeed.Name],
            Assert.Single(resolvedAgainst).Select(source => source.Name));

        // The unanswered package root is inconclusive, and its nuspec sibling still reports
        // the evidence it declares.
        DependencyEvidenceFailureRow failure = Assert.Single(projection.Failures);
        Assert.Equal("AcquisitionFailed", failure.Reason);
        Assert.Single(projection.Roots);
        Assert.NotEmpty(projection.Dependencies);
    }

    /// <summary>
    /// A blank explicit path gesture binds to nothing. The project locator would read it as the
    /// current directory and answer about a project the caller never named, so it is a typed
    /// producer-contract failure for that root instead.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankPathRoots_AreProducerContractFailuresRatherThanCurrentDirectory(
        string path)
    {
        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Nuspecs = [path],
                Projects = [path, AssetsFixture],
            });

        Assert.Single(projection.Roots);
        Assert.NotEmpty(projection.Dependencies);
        Assert.Equal(2, projection.Summary.FailedRootCount);
        Assert.Equal(
            ["ProducerContract", "ProducerContract"],
            projection.Failures.Select(failure => failure.Reason));
        Assert.Equal(
            [
                PackageDependencyEvidenceSourceKind.DirectNuspec,
                PackageDependencyEvidenceSourceKind.ProjectLocator,
            ],
            projection.Failures.Select(failure => failure.SourceKind).Order());
    }

    /// <summary>
    /// Package source mapping that authorizes no producer for one package id is that root's
    /// typed failure, decided through the shared authorization seam. It neither ends the
    /// request nor carries the selected configuration's text into a message sink.
    /// </summary>
    [Fact]
    public async Task SourcePolicyDenial_FailsOnlyThePackageRoot()
    {
        string config = WriteMappedConfig("Other.*");

        // The same policy the command builds from '--nugetconfig', asked the same canonical
        // question, states this denial. Its text quotes the selected configuration, so it is
        // what must not reach either sink.
        PackageSourceAuthorization denial =
            new SourcePolicyPackageSourceAuthorization(
                new NuGetSourceOptions { ConfigFile = config })
                .AuthorizeSourcesFor("contoso.denied");
        Assert.Empty(denial.Sources);
        Assert.NotNull(denial.DenialReason);

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            DependencyEvidenceCommand.Name,
            "--package",
            "Contoso.Denied@1.0.0",
            "--nuspec",
            NuspecFixture,
            "--nugetconfig",
            config,
            "-S",
            "Dependencies,Failures",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("| Failed Roots | 1 |", output, StringComparison.Ordinal);
        Assert.Contains("SourceUnavailable", output, StringComparison.Ordinal);
        Assert.DoesNotContain(config, output, StringComparison.Ordinal);
        Assert.DoesNotContain(config, error, StringComparison.Ordinal);
        Assert.DoesNotContain(denial.DenialReason, output, StringComparison.Ordinal);
        Assert.DoesNotContain(denial.DenialReason, error, StringComparison.Ordinal);

        // The nuspec sibling still renders its declared evidence.
        Assert.Contains("## Dependencies", output, StringComparison.Ordinal);
        Assert.Contains("NuGet.Packaging", output, StringComparison.Ordinal);
    }

    // ---- floating resolution contract ---------------------------------------

    /// <summary>
    /// Floating binding consumes the package-owned composition, which is normative for what a
    /// configured authority publishes. A local folder is an authority like any other there, so
    /// its candidates join an HTTP authority's, the aggregate is sorted globally before it is
    /// limited, and the selected head may come from either one. Nothing here is decided from a
    /// source's text or transport.
    /// </summary>
    [Fact]
    public async Task FloatingDiscovery_ComposesLocalAndHttpAuthoritiesAndSortsGlobally()
    {
        string folder = CreateTemporaryDirectory();
        WriteLocalSourcePackage(folder, ComposedFeedHandler.PackageId, "1.5.0", "");
        WriteLocalSourcePackage(
            folder,
            ComposedFeedHandler.PackageId,
            "2.0.0-beta.1",
            "");

        // The HTTP authority publishes both the globally latest stable version and the
        // globally latest prerelease, so a selection that ignored it would be visible.
        using var handler = new ComposedFeedHandler(["1.0.0", "3.0.0", "4.0.0-rc.1"]);
        await using DesktopPackageSourceComposition composition =
            CreateComposition(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [folder, ComposedFeedHandler.ServiceIndexUrl],
        };

        PackageVersionDiscoveryResult aggregate =
            await composition.GetVersionsAsync(
                ComposedFeedHandler.PackageId,
                includePrerelease: true,
                limit: null,
                sourceOptions,
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            PackageVersionDiscoveryState.Authoritative,
            aggregate.State);
        Assert.Equal(
            ["4.0.0-rc.1", "3.0.0", "2.0.0-beta.1", "1.5.0", "1.0.0"],
            aggregate.Versions);

        // One row is the globally latest acceptable version, not the first authority's.
        PackageVersionDiscoveryResult stable =
            await composition.GetVersionsAsync(
                ComposedFeedHandler.PackageId,
                includePrerelease: false,
                limit: 1,
                sourceOptions,
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(PackageVersionDiscoveryState.Authoritative, stable.State);
        Assert.Equal(["3.0.0"], stable.Versions);

        PackageVersionDiscoveryResult preview =
            await composition.GetVersionsAsync(
                ComposedFeedHandler.PackageId,
                includePrerelease: true,
                limit: 1,
                sourceOptions,
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(PackageVersionDiscoveryState.Authoritative, preview.State);
        Assert.Equal(["4.0.0-rc.1"], preview.Versions);
    }

    /// <summary>
    /// The command binds the version that composition selected and then acquires that exact
    /// manifest. Here the local-folder authority publishes both heads, so the whole gesture —
    /// floating selection and manifest acquisition — completes against a local folder the
    /// previous HTTP-only listing path could not have answered for at all.
    /// </summary>
    [Theory]
    [InlineData(false, "1.5.0")]
    [InlineData(true, "2.0.0-beta.1")]
    public async Task FloatingPackage_BindsAndAcquiresTheSelectedLocalManifest(
        bool includePrerelease,
        string expectedVersion)
    {
        string folder = CreateTemporaryDirectory();
        const string Dependencies = """
            <group targetFramework="net8.0">
              <dependency id="Contoso.Dependency" version="[1.0.0]" />
            </group>
            """;
        WriteLocalSourcePackage(
            folder,
            ComposedFeedHandler.PackageId,
            "1.5.0",
            Dependencies);
        WriteLocalSourcePackage(
            folder,
            ComposedFeedHandler.PackageId,
            "2.0.0-beta.1",
            Dependencies);

        // The HTTP authority answers the version question and publishes nothing newer, so the
        // selected head is the local one and no manifest request leaves the machine.
        using var handler = new ComposedFeedHandler(["1.0.0"]);
        await using DesktopPackageSourceComposition composition =
            CreateComposition(handler);
        var localSource = new PackageSource("local", folder);
        var authorized = PackageSourceAuthorization.Authorize(
            [localSource, new PackageSource("stub", ComposedFeedHandler.ServiceIndexUrl)]);

        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Packages = [ComposedFeedHandler.PackageId],
                IncludePrerelease = includePrerelease,
            },
            authorization: new RecordingPackageSourceAuthorization(authorized),
            discoverVersions: (packageId, prerelease, token) =>
                composition.GetVersionsAsync(
                    packageId,
                    prerelease,
                    limit: 1,
                    new NuGetSourceOptions
                    {
                        Sources = [folder, ComposedFeedHandler.ServiceIndexUrl],
                    },
                    cancellationToken: token));

        Assert.Empty(projection.Failures);
        DependencyEvidenceRootRow root = Assert.Single(projection.Roots);
        Assert.Equal(expectedVersion, root.PackageVersion);
        Assert.Contains(
            "Contoso.Dependency",
            projection.Dependencies.Select(row => row.PackageId),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An unqualified <c>ID</c> target is documented as latest stable, so a package publishing
    /// only prereleases is refused rather than quietly resolved to a prerelease head. The
    /// authority answered, so the aggregate is authoritative and empty, which is still
    /// inconclusive rather than an absence claim: the root reports the conservative acquisition
    /// failure and its sibling still renders.
    /// </summary>
    [Fact]
    public async Task FloatingPackage_WithoutPreview_RefusesAPrereleaseOnlyPackageAsInconclusive()
    {
        using var handler = new ComposedFeedHandler(["1.0.0-beta.1"]);
        await using DesktopPackageSourceComposition composition =
            CreateComposition(handler);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [ComposedFeedHandler.ServiceIndexUrl],
        };

        PackageVersionDiscoveryResult discovery =
            await composition.GetVersionsAsync(
                ComposedFeedHandler.PackageId,
                includePrerelease: false,
                limit: 1,
                sourceOptions,
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(PackageVersionDiscoveryState.Authoritative, discovery.State);
        Assert.Empty(discovery.Versions);

        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Packages = [ComposedFeedHandler.PackageId],
                Nuspecs = [NuspecFixture],
            },
            authorization: new UniformPackageSourceAuthorization(
                [new PackageSource("stub", ComposedFeedHandler.ServiceIndexUrl)]),
            discoverVersions: (packageId, prerelease, token) =>
                composition.GetVersionsAsync(
                    packageId,
                    prerelease,
                    limit: 1,
                    sourceOptions,
                    cancellationToken: token));

        Assert.Single(projection.Roots);
        DependencyEvidenceFailureRow failure = Assert.Single(projection.Failures);
        Assert.Equal(
            PackageDependencyEvidenceSourceKind.PackageSourceManifest,
            failure.SourceKind);
        Assert.Equal("AcquisitionFailed", failure.Reason);
    }

    /// <summary>
    /// A floating target becomes the root's exact coordinate, stated as latest across every
    /// authorized producer, so only an authoritative aggregate that selected a version may be
    /// admitted. A partial aggregate never heard from some authority, a failed one heard from
    /// none, and an authoritative empty one publishes nothing this request accepts. None proves
    /// the coordinate absent, so each is the conservative <c>AcquisitionFailed</c> — never
    /// <c>NotFound</c> — while a valid sibling root still renders.
    /// </summary>
    [Theory]
    [InlineData(PackageVersionDiscoveryState.Partial, "9.9.9")]
    [InlineData(PackageVersionDiscoveryState.Failed, null)]
    [InlineData(PackageVersionDiscoveryState.Authoritative, null)]
    public async Task FloatingDiscovery_ThatSelectsNoAuthoritativeVersion_IsAcquisitionFailed(
        PackageVersionDiscoveryState state,
        string? version)
    {
        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Packages = ["Contoso.Inconclusive"],
                Nuspecs = [NuspecFixture],
            },
            authorization: new UniformPackageSourceAuthorization([StubFeed]),
            discoverVersions: (_, _, _) => Task.FromResult(
                DiscoveryResult(state, version)));

        Assert.Single(projection.Roots);
        DependencyEvidenceFailureRow failure = Assert.Single(projection.Failures);
        Assert.Equal(
            PackageDependencyEvidenceSourceKind.PackageSourceManifest,
            failure.SourceKind);
        Assert.Equal("AcquisitionFailed", failure.Reason);
        Assert.NotEqual("NotFound", failure.Reason);

        // The sibling nuspec root still renders its declared evidence.
        Assert.Contains(
            "NuGet.Packaging",
            projection.Dependencies.Select(row => row.PackageId),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An exact pin asks no latest-version question, so it never reaches version discovery —
    /// including a pinned prerelease without <c>--preview</c>, which stays exact. A floating
    /// target reaches it exactly once and becomes that selected coordinate.
    /// </summary>
    [Theory]
    [InlineData("1.0.0-beta.1", false, "1.0.0-beta.1", 0)]
    [InlineData("2.0.0", false, "2.0.0", 0)]
    [InlineData(null, false, "3.0.0", 1)]
    public async Task ExactPin_BypassesVersionDiscoveryThatAFloatingTargetConsults(
        string? version,
        bool includePrerelease,
        string expectedVersion,
        int expectedDiscoveryCalls)
    {
        int calls = 0;
        using var client = new HttpClient(new StubFeedHandler());

        PackageCoordinateResolution resolution =
            await DependencyEvidenceAcquisition.ResolveCoordinateAsync(
                client,
                new PackageCoordinate(StubFeedHandler.PackageId, version),
                [StubFeed],
                (_, _, _) =>
                {
                    calls++;
                    return Task.FromResult(
                        DiscoveryResult(
                            PackageVersionDiscoveryState.Authoritative,
                            "3.0.0"));
                },
                log: null,
                includePrerelease,
                TestContext.Current.CancellationToken);

        PackageCoordinateResolution.Resolved resolved =
            Assert.IsType<PackageCoordinateResolution.Resolved>(resolution);
        Assert.Equal(expectedVersion, resolved.Coordinate.Version);
        Assert.Equal(expectedDiscoveryCalls, calls);
        Assert.Contains(StubFeed, resolved.Coordinate.Sources);
    }

    // ---- owner-issued authority identity ------------------------------------

    /// <summary>
    /// Production manifest acquisition constructs each client with the association its
    /// <c>PackageSourceAuthorization.Authorities</c> entry already carries, so the admitted
    /// root's result identity is recoverable as that exact configured authority. Minting a
    /// fresh association here would make the result claim a scope no configured authority
    /// owns. The claim is identity, not display text.
    /// </summary>
    [Fact]
    public async Task ManifestAcquisition_KeepsTheOwnerIssuedAuthorityAssociation()
    {
        string folder = CreateTemporaryDirectory();
        WriteLocalSourcePackage(folder, "Contoso.Owned", "1.2.3", "");
        var authorized = PackageSourceAuthorization.Authorize(
            [new PackageSource("local", folder)]);
        ConfiguredPackageAuthority authority = Assert.Single(authorized.Authorities);

        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions { Packages = ["Contoso.Owned@1.2.3"] },
            authorization: new RecordingPackageSourceAuthorization(authorized));

        Assert.Empty(projection.Failures);
        DependencyEvidenceRootRow root = Assert.Single(projection.Roots);
        PackageSourceResultIdentity source = Assert.IsType<PackageSourceResultIdentity>(
            root.Source);
        Assert.Same(authority.Association, source.Association);
        Assert.True(
            authorized.TryGetAuthority(
                source.Association,
                out ConfiguredPackageAuthority? recovered));
        Assert.Same(authority, recovered);
    }

    /// <summary>
    /// The same claim at the construction seam, for both authority families: an HTTP authority
    /// and a local-folder one each produce a client bound to their own owner-issued
    /// association, and neither is reconstructed from source text.
    /// </summary>
    [Fact]
    public void ManifestClientCreation_BindsEachAuthorityToItsOwnAssociation()
    {
        var authorized = PackageSourceAuthorization.Authorize(
            [new PackageSource("local", CreateTemporaryDirectory()), StubFeed]);
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(TimeSpan.FromSeconds(5));

        Assert.Equal(2, authorized.Authorities.Count);
        foreach (ConfiguredPackageAuthority authority in authorized.Authorities)
        {
            using IPackageSourceClient? client =
                DependencyEvidenceAcquisition.CreateSourceClient(
                    authority,
                    fetchOptions);
            Assert.NotNull(client);
            Assert.Same(authority.Association, client.Source.Association);
        }
    }

    /// <summary>
    /// The resolver's typed outcomes keep their classification: a rejected coordinate is a
    /// producer-contract failure, and an unavailable one is never reported as absence, because
    /// this resolver proves no authoritative all-source absence.
    /// </summary>
    [Theory]
    [InlineData(true, "ProducerContract")]
    [InlineData(false, "AcquisitionFailed")]
    public async Task ResolutionOutcome_IsClassifiedWithoutClaimingAbsence(
        bool invalid,
        string expectedReason)
    {
        DependencyEvidenceProjection projection = await ProjectAsync(
            new DependencyEvidenceOptions
            {
                Packages = ["Contoso.Unresolved"],
                Nuspecs = [NuspecFixture],
            },
            authorization: new UniformPackageSourceAuthorization([StubFeed]),
            resolveCoordinate: (_, _, _, _) => Task.FromResult(
                invalid
                    ? new PackageCoordinateResolution.Invalid("rejected")
                    : (PackageCoordinateResolution)
                        new PackageCoordinateResolution.Unavailable("inconclusive")));

        Assert.Single(projection.Roots);
        DependencyEvidenceFailureRow failure = Assert.Single(projection.Failures);
        Assert.Equal(expectedReason, failure.Reason);
        Assert.NotEqual("NotFound", failure.Reason);
    }

    // ---- helpers ------------------------------------------------------------

    private static readonly PackageSource StubFeed =
        new("stub", StubFeedHandler.ServiceIndexUrl);

    /// <summary>
    /// Builds the package-owned composition over a test transport, so a floating question is
    /// answered by the real owner — real source resolution, real authority classification, real
    /// local-folder and HTTP clients — without reaching a live feed.
    /// </summary>
    private static DesktopPackageSourceComposition CreateComposition(
        HttpMessageHandler transport) =>
        new(
            TimeSpan.FromSeconds(30),
            new UnavailableCredentialSource(),
            (_, _) => transport);

    /// <summary>
    /// One version-discovery answer in the owner's own vocabulary, so a regression can state
    /// what this command did with a partial, failed, or authoritatively empty aggregate.
    /// </summary>
    private static PackageVersionDiscoveryResult DiscoveryResult(
        PackageVersionDiscoveryState state,
        string? version) =>
        new(
            state,
            version is null ? [] : [version],
            state == PackageVersionDiscoveryState.Authoritative
                ? []
                : [
                    new PackageAuthorityFailure(
                        new InertString(TextPolicy.Field, "authority"),
                        PackageAuthorityFailureKind.Transport,
                        "The configured authority did not answer."),
                ],
            hasAnyCandidate: version is not null);

    /// <summary>A credential source the composition must never need to query.</summary>
    private sealed class UnavailableCredentialSource : ICredentialSource
    {
        public bool HasCredentialSources => false;

        public Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri uri,
            bool isRetry,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "An unavailable credential source must not be queried.");
    }

    /// <summary>
    /// One V3 HTTP authority publishing an explicit version list, used as the remote half of a
    /// composed local-plus-HTTP authority set. Every other request is a 404, so nothing here
    /// reaches a real network.
    /// </summary>
    private sealed class ComposedFeedHandler(IReadOnlyList<string> versions)
        : HttpMessageHandler
    {
        internal const string PackageId = "Contoso.Composed";
        internal const string ServiceIndexUrl = "https://composed.test/v3/index.json";
        private const string FlatContainer = "https://composed.test/flat/";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            HttpResponseMessage response;
            if (url.Equals(ServiceIndexUrl, StringComparison.Ordinal))
            {
                response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""
                        {
                          "version": "3.0.0",
                          "resources": [
                            { "@id": "{{FlatContainer}}", "@type": "PackageBaseAddress/3.0.0" }
                          ]
                        }
                        """),
                };
            }
            else if (url.Equals(
                         $"{FlatContainer}{PackageId.ToLowerInvariant()}/index.json",
                         StringComparison.OrdinalIgnoreCase))
            {
                response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"versions":[{{string.Join(
                            ",",
                            versions.Select(version => $"\"{version}\""))}}]}"""),
                };
            }
            else
            {
                response = new HttpResponseMessage(
                    System.Net.HttpStatusCode.NotFound)
                {
                    Content = new StringContent(""),
                };
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Records every package id one request authorizes and answers each with the supplied
    /// authorization, so a regression can state which identity a host was asked about, and how
    /// often, without depending on any real source policy.
    /// </summary>
    private sealed class RecordingPackageSourceAuthorization(
        PackageSourceAuthorization authorization) : IPackageSourceAuthorization
    {
        private readonly List<string> _packageIds = [];

        internal IReadOnlyList<string> PackageIds => _packageIds;

        public PackageSourceAuthorization AuthorizeSourcesFor(string packageId)
        {
            _packageIds.Add(packageId);
            return authorization;
        }
    }

    /// <summary>
    /// Writes a <c>nuget.config</c> whose package source mapping authorizes exactly one
    /// pattern, so any other package id is denied by the product's own mapping policy.
    /// </summary>
    private static string WriteMappedConfig(string pattern)
    {
        string path = Path.Combine(CreateTemporaryDirectory(), "nuget.config");
        File.WriteAllText(
            path,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="mapped" value="https://mapped.test/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="mapped">
                  <package pattern="{pattern}" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        return path;
    }

    /// <summary>
    /// Serves one V3 feed that publishes a single prerelease version, plus one authorized
    /// source that cannot answer at all. Every other request is a 404, so nothing here reaches
    /// a real network.
    /// </summary>
    private sealed class StubFeedHandler : HttpMessageHandler
    {
        internal const string PackageId = "contoso.preview";
        internal const string PrereleaseVersion = "1.0.0-beta.1";
        internal const string ServiceIndexUrl = "https://stub.test/v3/index.json";

        internal static readonly PackageSource OfflineSource =
            new("offline", "https://offline.test/v3/index.json");

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            return url switch
            {
                ServiceIndexUrl => Json(
                    """{"resources":[{"@id":"https://stub.test/flat","@type":"PackageBaseAddress/3.0.0"}]}"""),
                $"https://stub.test/flat/{PackageId}/index.json" => Json(
                    $$"""{"versions":["{{PrereleaseVersion}}"]}"""),
                "https://offline.test/v3/index.json" => Task.FromResult(
                    new HttpResponseMessage(
                        System.Net.HttpStatusCode.ServiceUnavailable)),
                _ => Task.FromResult(
                    new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)),
            };

            static Task<HttpResponseMessage> Json(string body) =>
                Task.FromResult(
                    new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
        }
    }

    /// <summary>Counts the data rows one rendered Markdown section table carries.</summary>
    private static int MarkdownDataRows(string markdown, string heading)
    {
        string[] lines = markdown.Split('\n');
        int start = Array.FindIndex(
            lines,
            line => line.TrimEnd() == $"## {heading}");
        Assert.True(start >= 0, $"'{heading}' did not render.");

        int rows = 0;
        // Skip the section heading, its blank line, the header row, and the delimiter row.
        for (int index = start + 4; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd();
            if (!line.StartsWith('|'))
                break;

            rows++;
        }

        return rows;
    }

    private static IReadOnlyList<(string Package, string Constraint)> Declared(
        DependencyEvidenceProjection projection) =>
    [
        .. projection.Dependencies
            .Select(row => (row.PackageId, row.VersionConstraint))
            .OrderBy(row => row.PackageId, StringComparer.Ordinal)
            .ThenBy(row => row.VersionConstraint, StringComparer.Ordinal),
    ];

    private static async Task<DependencyEvidenceProjection> ProjectAsync(
        DependencyEvidenceOptions options) =>
        await ProjectAsync(options, authorization: null);

    /// <summary>
    /// Projects one explicit-root request, optionally through narrow authorization,
    /// coordinate-resolution, and transport seams so a regression can state what a source
    /// policy or resolver answered without depending on live NuGet state.
    /// </summary>
    private static async Task<DependencyEvidenceProjection> ProjectAsync(
        DependencyEvidenceOptions options,
        IPackageSourceAuthorization? authorization,
        DependencyEvidenceCoordinateResolver? resolveCoordinate = null,
        HttpClient? httpClient = null,
        DependencyEvidenceVersionDiscovery? discoverVersions = null)
    {
        HttpClient client = httpClient ?? new HttpClient();
        try
        {
            PackageDependencyEvidenceRequest request =
                await DependencyEvidenceAcquisition.AcquireExplicitRootsAsync(
                    options,
                    client,
                    log: null,
                    TestContext.Current.CancellationToken,
                    authorization,
                    resolveCoordinate,
                    discoverVersions);
            return DependencyEvidenceProjection.Create(
                PackageDependencyEvidenceQuery.Execute(request));
        }
        finally
        {
            if (httpClient is null)
                client.Dispose();
        }
    }

    private static bool ValidateTabularCaptured(
        DependencyEvidenceOptions options,
        string[] candidateSections,
        out string error)
    {
        bool valid = false;
        (_, _, string captured) = ConsoleCapture
            .RunAsync(() =>
            {
                valid = DependencyEvidenceCommand.ValidateTabularArity(
                    options,
                    candidateSections);
                return Task.FromResult(valid ? 0 : 1);
            })
            .GetAwaiter()
            .GetResult();
        error = captured;
        return valid;
    }

    /// <summary>
    /// Rewrites both end-of-central-directory entry-count fields so the archive declares more
    /// entries than it carries. This is the shape the validator's preflight refuses before
    /// <c>ZipArchive</c> materializes a directory.
    /// </summary>
    private static byte[] WithDeclaredEntryCount(byte[] archive, ushort declared)
    {
        byte[] rewritten = (byte[])archive.Clone();
        int end = EndOfCentralDirectory(rewritten);
        BinaryPrimitives.WriteUInt16LittleEndian(
            rewritten.AsSpan(end + 8),
            declared);
        BinaryPrimitives.WriteUInt16LittleEndian(
            rewritten.AsSpan(end + 10),
            declared);
        return rewritten;
    }

    private static int EndOfCentralDirectory(byte[] archive)
    {
        for (int offset = archive.Length - 22; offset >= 0; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(offset))
                == 0x06054B50)
            {
                return offset;
            }
        }

        throw new InvalidOperationException(
            "The test archive has no end-of-central-directory record.");
    }

    private static bool ValidateCaptured(
        DependencyEvidenceOptions options,
        out string error) =>
        ValidateCaptured(options, selectedSections: null, out error);

    private static bool ValidateCaptured(
        DependencyEvidenceOptions options,
        HashSet<string>? selectedSections,
        out string error)
    {
        bool valid = false;
        (_, _, string captured) = ConsoleCapture
            .RunAsync(() =>
            {
                valid = DependencyEvidenceCommand.Validate(
                    options,
                    selectedSections);
                return Task.FromResult(valid ? 0 : 1);
            })
            .GetAwaiter()
            .GetResult();
        error = captured;
        return valid;
    }

    private static Task<(int ExitCode, string Output, string Error)> RunCapturedAsync(
        string[] args) =>
        ConsoleCapture.RunAsync(() => RunAsync(args));

    private static Task<int> RunAsync(string[] args)
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();
        string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
        return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "dependency-evidence-tests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteTemporaryFile(string name, byte[] content)
    {
        string path = Path.Combine(CreateTemporaryDirectory(), name);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>
    /// Packs the shared nuspec fixture into a real <c>.nupkg</c> so the archive adapter is
    /// exercised over the same declaration seed as the nuspec and project adapters.
    /// </summary>
    private static string CreateFixtureArchive()
    {
        string path = Path.Combine(
            CreateTemporaryDirectory(),
            "RestoredProjectFixture.1.0.0.nupkg");
        using (FileStream file = File.Create(path))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry =
                archive.CreateEntry("RestoredProjectFixture.nuspec");
            using Stream entryStream = entry.Open();
            entryStream.Write(File.ReadAllBytes(NuspecFixture));
        }

        return path;
    }

    private static byte[] Manifest(
        string packageId,
        string version,
        string dependencies = "") =>
        Encoding.UTF8.GetBytes(
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
                <authors>Manifest Author</authors>
                <description>Dependency evidence test.</description>
                <dependencies>{{dependencies}}</dependencies>
              </metadata>
            </package>
            """);

    /// <summary>
    /// Runs the CLI's source-manifest adapter over fake authorized sources, exercising the real
    /// fallback order without duplicating source semantics in the test.
    /// </summary>
    private static async Task<(
        ImmutableArray<PackageDependencyEvidenceInput> Roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure> Failures)>
        AcquireFromSourcesAsync(
            PackageSourceCoordinate coordinate,
            IReadOnlyList<(string Name, IPackageSourceClient Client)> sources)
    {
        var roots = ImmutableArray.CreateBuilder<PackageDependencyEvidenceInput>();
        var failures =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceRootFailure>();
        Dictionary<string, IPackageSourceClient> clients = sources.ToDictionary(
            source => source.Name,
            source => source.Client,
            StringComparer.Ordinal);

        await DependencyEvidenceAcquisition.AcquireSourceManifestAsync(
            coordinate,
            [
                .. sources.Select(source => new PackageSource(
                    source.Name,
                    $"https://{source.Name}.invalid/v3/index.json")),
            ],
            source => clients[source.Name],
            targetFramework: null,
            new InertString(
                TextPolicy.Field,
                $"{coordinate.PackageId}@{coordinate.Version}"),
            operationContext: null,
            roots,
            failures,
            TestContext.Current.CancellationToken);
        return (roots.ToImmutable(), failures.ToImmutable());
    }

    private static IPackageSourceClient MissingSource() =>
        new FakePrefixSource(
            [],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase));

    /// <summary>A source whose typed manifest failure is not an absence claim.</summary>
    private static IPackageSourceClient FailingSource(
        PackageSourceFailureKind kind) =>
        new FakePrefixSource(
            [],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase))
        {
            MissingManifestFailure = kind,
        };

    /// <summary>A source that cannot be reached at all.</summary>
    private static IPackageSourceClient UnreachableSource() =>
        new FakePrefixSource(
            [],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase))
        {
            ManifestTransportFailure = true,
        };

    /// <summary>
    /// Writes one package into a flat local folder source, the layout
    /// <c>LocalFolderPackageSourceClient</c> reads.
    /// </summary>
    private static void WriteLocalSourcePackage(
        string folder,
        string packageId,
        string version,
        string dependencies)
    {
        string path = Path.Combine(folder, $"{packageId}.{version}.nupkg");
        using FileStream file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry($"{packageId}.nuspec");
        using Stream entryStream = entry.Open();
        entryStream.Write(Manifest(packageId, version, dependencies));
    }

    /// <summary>A source serving a manifest whose declared identity is not the requested one.</summary>
    private static IPackageSourceClient InvalidManifestSource(
        PackageSourceCoordinate coordinate) =>
        new FakePrefixSource(
            [],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [$"{coordinate.PackageId}@{coordinate.Version}"] =
                    Manifest("Contoso.Different", "9.9.9"),
            });

    private static IPackageSourceClient ValidManifestSource(
        PackageSourceCoordinate coordinate) =>
        new FakePrefixSource(
            [],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [$"{coordinate.PackageId}@{coordinate.Version}"] = Manifest(
                    "Contoso.Fallback",
                    "1.0.0",
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Contoso.Dependency" version="[1.0.0]" />
                    </group>
                    """),
            });

    private static PackageSourceResultFactory CreateResultFactory()
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                PackageSourceAssociation.Create(),
                factory =>
                {
                    captured = factory;
                    return new UnusedPackageSource(factory.Source);
                });
        return Assert.IsType<PackageSourceResultFactory>(captured);
    }

    private sealed class UnusedPackageSource(PackageSourceResultIdentity source)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.None;

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
            string query,
            int take = 20,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class FakePrefixSource(
        IReadOnlyList<SearchResult> matches,
        IReadOnlyDictionary<string, byte[]> manifests)
        : IPackageSourceClient
    {
        private readonly PackageSourceResultFactory _results =
            CreateResultFactory();

        public PackageSearchTruncationReason SearchTruncationReason { get; init; }

        /// <summary>The typed failure kind returned for a coordinate this source lacks.</summary>
        public PackageSourceFailureKind MissingManifestFailure { get; init; } =
            PackageSourceFailureKind.NotFound;

        /// <summary>Whether a manifest request fails as an unreachable transport instead.</summary>
        public bool ManifestTransportFailure { get; init; }

        public PackageSourceResultIdentity Source => _results.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Search
            | PackageSourceCapabilities.Manifest;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            Task.FromResult(
                _results.SucceededSearch(
                    _results.Search(matches, SearchTruncationReason)));

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            if (ManifestTransportFailure)
                throw new HttpRequestException("The fake source is unreachable.");

            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(packageId, version);
            string key = $"{coordinate.PackageId}@{coordinate.Version}";
            return Task.FromResult(
                manifests.TryGetValue(key, out byte[]? content)
                    ? _results.SucceededManifest(
                        coordinate,
                        _results.Manifest(coordinate, content))
                    : _results.FailedManifest(
                        coordinate,
                        MissingManifestFailure));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
            string query,
            int take = 20,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
