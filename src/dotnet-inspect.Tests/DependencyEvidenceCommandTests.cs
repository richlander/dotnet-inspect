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

    // ---- helpers ------------------------------------------------------------

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
        DependencyEvidenceOptions options)
    {
        using var client = new HttpClient();
        PackageDependencyEvidenceRequest request =
            await DependencyEvidenceAcquisition.AcquireExplicitRootsAsync(
                options,
                client,
                log: null,
                TestContext.Current.CancellationToken);
        return DependencyEvidenceProjection.Create(
            PackageDependencyEvidenceQuery.Execute(request));
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
