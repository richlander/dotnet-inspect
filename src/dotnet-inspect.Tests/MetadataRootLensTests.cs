using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Root selection substitutes one metadata address space for another. Shared synthetic fixtures
/// give the manifest distinct rows and heap values, so a relabeled CLI projection cannot pass.
/// </summary>
public partial class CommandExecutionTests
{
    private const string ManifestRoot = "r2r-manifest";
    private const string CliRoot = "cli";
    private const string ReadyToRunSection = MetadataSectionNames.ReadyToRun;

    /// <summary>
    /// Omitting <c>-S</c> with an explicit root selects the image facts and nothing else, and those
    /// facts name both the requested root and the canonical root it resolved to.
    ///
    /// The two roots are separate facts for the same reason provenance is separate from identity
    /// everywhere else: a manifest can be its own address space or an alias of the CLI one, and a
    /// caller reading the requested name alone cannot tell which it got.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_DefaultSelectionIsImageFactsNamingBothRoots()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        var (exit, output, error) = await RunAppAsync(
            "library", path, "--metadata-root", ManifestRoot, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains($"## {MetadataSectionNames.Image}", output, StringComparison.Ordinal);

        // The four root facts lead the image section, in this order. Requested and canonical are
        // separate rows because a manifest may be its own address space or an alias of the CLI one,
        // and the extent rows are what make the choice checkable against the R2R summary.
        Assert.Equal(
            ["Requested root", "Canonical root", "Root RVA", "Root size"],
            FactLabels(output).Take(4).ToArray());
        Assert.Contains("| Requested root | ReadyToRunManifest |", output, StringComparison.Ordinal);
        Assert.Contains("| Canonical root | ReadyToRunManifest |", output, StringComparison.Ordinal);
        Assert.Matches(@"\| Root RVA \| 0x[0-9A-F]{8} \|", output);
        Assert.Matches(@"\| Root size \| \d+ bytes \|", output);

        // Exactly one section: an explicit root must not drag the ordinary library report along
        // with it, and must not open the rest of the metadata family either.
        var headings = output.Split('\n')
            .Where(l => l.StartsWith("## ", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .ToArray();
        Assert.Equal([$"## {MetadataSectionNames.Image}"], headings);
    }

    /// <summary>
    /// The neighbor: with no explicit root the image facts carry no root rows at all, so the rows
    /// above are evidence of the selection rather than of a section that always reports them.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_OmittedRoot_LeavesImageFactsUnchanged()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", MetadataSectionNames.Image, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("| Metadata version |", output, StringComparison.Ordinal);
        Assert.DoesNotContain("| Requested root |", output, StringComparison.Ordinal);
        Assert.DoesNotContain("| Canonical root |", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two recognized tokens are matched case-insensitively, and <c>cli</c> names the root the
    /// lens already read — so an explicit CLI selection reports the same coordinates the default
    /// path does, differing only by the provenance rows it adds.
    /// </summary>
    [Theory]
    [InlineData("cli", "Cli")]
    [InlineData("CLI", "Cli")]
    [InlineData("Cli", "Cli")]
    [InlineData("r2r-manifest", "ReadyToRunManifest")]
    [InlineData("R2R-Manifest", "ReadyToRunManifest")]
    [InlineData("R2R-MANIFEST", "ReadyToRunManifest")]
    public async Task MetadataLens_ManifestRoot_TokenSpellingIsCaseInsensitive(
        string token,
        string expectedRoot)
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        var (exit, output, error) = await RunAppAsync(
            "library", path, "--metadata-root", token, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains($"| Requested root | {expectedRoot} |", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rows really are the manifest's. The manifest carries two assembly references that exist
    /// in no compiled assembly, and the CLI root of the very same file carries neither, so this
    /// separates "read the manifest" from "read the image and relabel it".
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_ProjectsRowsIndependentOfTheCliRoot()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        var (manifestExit, manifestOutput, _) = await RunAppAsync(
            "library", path, "-S", "Metadata: AssemblyRef",
            "--metadata-root", ManifestRoot, "--tsv", "--tips", "q");

        Assert.Equal(0, manifestExit);
        Assert.Contains(ReadyToRunImageFixture.ManifestDependencyName, manifestOutput, StringComparison.Ordinal);
        Assert.Contains(ReadyToRunImageFixture.ManifestNeighborName, manifestOutput, StringComparison.Ordinal);

        var (cliExit, cliOutput, _) = await RunAppAsync(
            "library", path, "-S", "Metadata: AssemblyRef",
            "--metadata-root", CliRoot, "--tsv", "--tips", "q");
        var (defaultExit, defaultOutput, _) = await RunAppAsync(
            "library", path, "-S", "Metadata: AssemblyRef", "--tsv", "--tips", "q");

        Assert.Equal(0, cliExit);
        Assert.Equal(0, defaultExit);
        Assert.DoesNotContain(ReadyToRunImageFixture.ManifestDependencyName, cliOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(ReadyToRunImageFixture.ManifestDependencyName, defaultOutput, StringComparison.Ordinal);
        Assert.Equal(cliOutput, defaultOutput);
        Assert.NotEqual(cliOutput, manifestOutput);
    }

    /// <summary>
    /// The exact-alias case: a manifest section that points at the CLI metadata directory is the
    /// same address space, so the canonical root collapses to <c>Cli</c> while the requested root
    /// stays <c>ReadyToRunManifest</c>. Losing either half loses information — the first would
    /// claim a second address space that does not exist, the second would discard what the caller
    /// asked for.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_ExactAlias_KeepsRequestedAndCanonicalRootsDistinct()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Alias.Lib.dll", AliasImageBytes());

        var (aliasExit, aliasOutput, aliasError) = await RunAppAsync(
            "library", path, "--metadata-root", ManifestRoot, "--tips", "q");
        var (cliExit, cliOutput, _) = await RunAppAsync(
            "library", path, "--metadata-root", CliRoot, "--tips", "q");

        Assert.Equal(0, aliasExit);
        Assert.Equal(0, cliExit);
        Assert.Empty(aliasError);
        Assert.Contains("| Requested root | ReadyToRunManifest |", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("| Canonical root | Cli |", aliasOutput, StringComparison.Ordinal);

        // Same address space: every coordinate but the requested-root row matches the CLI selection.
        Assert.Equal(
            RootRows(cliOutput).Where(r => !r.StartsWith("| Requested root ", StringComparison.Ordinal)),
            RootRows(aliasOutput).Where(r => !r.StartsWith("| Requested root ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Heap addresses are root-local. The same <c>#Strings</c> offset means one thing in the
    /// manifest and something else entirely in the CLI metadata, so an address read against the
    /// wrong root is not an error the caller can see — it is a plausible wrong answer. The address
    /// is taken from the manifest's own listing rather than hard-coded, so the listing and the
    /// coordinate lookup are proven to agree on the same address space.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_HeapAddressesAreRootLocal()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        var (listExit, listOutput, _) = await RunAppAsync(
            "library", path, "-S", MetadataSectionNames.ForHeap(HeapKind.String),
            "--metadata-root", ManifestRoot, "--tsv", "--tips", "q");

        Assert.Equal(0, listExit);
        Assert.Contains(ReadyToRunImageFixture.ManifestDependencyName, listOutput, StringComparison.Ordinal);

        int address = HeapAddressOf(listOutput, ReadyToRunImageFixture.ManifestDependencyName);
        Assert.True(address > 0, $"the manifest listing must address {ReadyToRunImageFixture.ManifestDependencyName}");

        var (manifestExit, manifestOutput, _) = await RunAppAsync(
            "library", path, "--heap", $"#Strings:{address}",
            "--metadata-root", ManifestRoot, "--tsv", "--tips", "q");

        Assert.Equal(0, manifestExit);
        Assert.Contains(ReadyToRunImageFixture.ManifestDependencyName, manifestOutput, StringComparison.Ordinal);

        var (cliExit, cliOutput, _) = await RunAppAsync(
            "library", path, "--heap", $"#Strings:{address}",
            "--metadata-root", CliRoot, "--tsv", "--tips", "q");

        Assert.Equal(0, cliExit);
        Assert.DoesNotContain(ReadyToRunImageFixture.ManifestDependencyName, cliOutput, StringComparison.Ordinal);

        // The CLI listing must not carry the manifest's strings either: the listing walks the
        // selected root's referenced values, not the union of both roots.
        var (cliListExit, cliListOutput, _) = await RunAppAsync(
            "library", path, "-S", MetadataSectionNames.ForHeap(HeapKind.String),
            "--metadata-root", CliRoot, "--tsv", "--tips", "q");

        Assert.Equal(0, cliListExit);
        Assert.DoesNotContain(ReadyToRunImageFixture.ManifestDependencyName, cliListOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--count</c> counts the selected root's rows. Asserted against the rendered rows of the
    /// same selection rather than against a literal, because the failure this guards is the two
    /// paths disagreeing: a count taken from the CLI root beside a projection taken from the
    /// manifest reports a table size that matches nothing the caller can print.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_CountMatchesTheRenderedRows()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        var (countExit, countOutput, countError) = await RunAppAsync(
            "library", path, "-S", "Metadata: AssemblyRef",
            "--metadata-root", ManifestRoot, "--count", "--tips", "q");
        var (rowExit, rowOutput, _) = await RunAppAsync(
            "library", path, "-S", "Metadata: AssemblyRef",
            "--metadata-root", ManifestRoot, "--tsv", "--tips", "q");

        Assert.Equal(0, countExit);
        Assert.Equal(0, rowExit);
        Assert.Empty(countError);

        int rendered = DataRows(rowOutput).Length;
        Assert.Equal(2, rendered);
        Assert.Equal(rendered, int.Parse(countOutput.Trim()));

        // The neighbor: the CLI root of the same file has a different number of assembly
        // references, so the count is measuring the selection rather than the file.
        var (cliCountExit, cliCountOutput, _) = await RunAppAsync(
            "library", path, "-S", "Metadata: AssemblyRef",
            "--metadata-root", CliRoot, "--count", "--tips", "q");

        Assert.Equal(0, cliCountExit);
        Assert.NotEqual(countOutput.Trim(), cliCountOutput.Trim());
    }

    /// <summary>
    /// The machine formats and the column projection are the existing pipelines, not new ones: one
    /// section means one set of rows in every format, and <c>--columns</c> narrows a manifest table
    /// exactly as it narrows a CLI one. Projected <c>--json</c> is refused rather than silently
    /// emitting a different shape; see the dedicated JSON case for the full verdict matrix.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_MachineFormatsAndColumnsReuseTheSharedPipelines()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        string[] selection = ["library", path, "-S", "Metadata: AssemblyRef", "--metadata-root", ManifestRoot];

        var (tsvExit, tsvOutput, _) = await RunAppAsync([.. selection, "--tsv", "--tips", "q"]);
        var (jsonlExit, jsonlOutput, _) = await RunAppAsync([.. selection, "--jsonl", "--tips", "q"]);

        Assert.Equal(0, tsvExit);
        Assert.Equal(0, jsonlExit);

        var jsonlLines = jsonlOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(DataRows(tsvOutput).Length, jsonlLines.Length);
        Assert.Contains(ReadyToRunImageFixture.ManifestDependencyName, jsonlOutput, StringComparison.Ordinal);
        Assert.All(jsonlLines, l => Assert.StartsWith("{", l.TrimStart(), StringComparison.Ordinal));

        var (columnExit, columnOutput, _) = await RunAppAsync(
            [.. selection, "--columns", "Name", "--tsv", "--tips", "q"]);

        Assert.Equal(0, columnExit);
        var cells = columnOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r').Split('\t'))
            .ToArray();
        Assert.Equal(["table", "rid", "name"], cells[0]);
        Assert.All(cells, row => Assert.Equal(3, row.Length));

        var (windowExit, windowOutput, _) = await RunAppAsync(
            [.. selection, "--rows", "2..2", "--tsv", "--tips", "q"]);

        Assert.Equal(0, windowExit);
        var windowed = DataRows(windowOutput);
        string onlyRow = Assert.Single(windowed);
        // The row-id column is root-relative, so the window addresses the manifest's own row 2.
        Assert.Equal("2", onlyRow.Split('\t')[1]);
        Assert.Contains(ReadyToRunImageFixture.ManifestNeighborName, onlyRow, StringComparison.Ordinal);
    }

    /// <summary>
    /// The JSON verdicts, stated as a matrix so no caller mistakes a typed library document for
    /// root-selected rows. Projected JSON is the only JSON that could carry lens rows, and it is
    /// refused for a selected root; the carve-outs the diagnostic advertises -- <c>--count</c> and
    /// discovery -- really do succeed. Unprojected <c>--json</c> is deliberately not asserted to
    /// contain lens rows: it is the pre-existing typed-library document, which the row projections
    /// do not participate in.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_ProjectedJsonIsRefusedWhileCountAndDiscoveryStayAvailable()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        string[] selection = ["library", path, "-S", "Metadata: AssemblyRef", "--metadata-root", ManifestRoot];

        // Projected JSON is the shape that would carry rows, so it carries the root-specific refusal.
        var (projectedExit, projectedOutput, projectedError) = await RunAppAsync(
            [.. selection, "--json", "--columns", "Table,Rid,Name", "--tips", "q"]);

        Assert.Equal(1, projectedExit);
        Assert.Empty(projectedOutput);
        Assert.Contains("do not support --json", projectedError, StringComparison.Ordinal);
        Assert.Contains("--jsonl", projectedError, StringComparison.Ordinal);

        // Bare --json takes the same verdict; the diagnostic names the working alternatives.
        var (bareExit, bareOutput, bareError) = await RunAppAsync([.. selection, "--json", "--tips", "q"]);

        Assert.Equal(1, bareExit);
        Assert.Empty(bareOutput);
        Assert.Contains("do not support --json", bareError, StringComparison.Ordinal);
        Assert.Contains("--jsonl", bareError, StringComparison.Ordinal);
        Assert.Contains("--count", bareError, StringComparison.Ordinal);

        // The advertised carve-outs are real, not aspirational.
        var (countExit, countOutput, _) = await RunAppAsync([.. selection, "--count", "--json", "--tips", "q"]);

        Assert.Equal(0, countExit);
        Assert.NotEmpty(countOutput.Trim());

        var (discoverExit, discoverOutput, _) = await RunAppAsync(
            "library", path, "-D", "@Metadata", "--metadata-root", ManifestRoot, "--json", "--tips", "q");

        Assert.Equal(0, discoverExit);
        Assert.Contains("\"name\"", discoverOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// An explicitly requested root that is absent fails visibly. Falling back to the CLI root
    /// would be the worst outcome available: the caller receives rows, they look like metadata, and
    /// nothing says they came from a different address space than the one that was asked for.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_AbsentManifest_FailsWithoutCliFallback()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("NoManifest.Lib.dll", NoManifestImageBytes());

        var (exit, output, error) = await RunAppAsync(
            "library", path, "-S", "Metadata: AssemblyRef",
            "--metadata-root", ManifestRoot, "--tsv", "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("AssemblyRef\t", output, StringComparison.Ordinal);
        // The diagnostic names the root that was requested, not a generic read failure: a caller
        // who asked for one address space and got nothing must be able to tell "this image has no
        // manifest" from "this image could not be read at all".
        Assert.Contains(
            "The requested ReadyToRunManifest metadata root is absent.",
            error,
            StringComparison.Ordinal);

        // The same file still reads through its CLI root, so the failure is about the requested
        // root and not about the image being unreadable.
        var (cliExit, cliOutput, _) = await RunAppAsync(
            "library", path, "-S", "Metadata: AssemblyRef",
            "--metadata-root", CliRoot, "--tsv", "--tips", "q");

        Assert.Equal(0, cliExit);
        Assert.Contains("AssemblyRef\t", cliOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// A malformed manifest is a failure, not an absence, and not a reason to distrust the CLI
    /// metadata beside it. Both halves matter: a lens that failed the whole command would make one
    /// corrupt R2R section hide an otherwise readable assembly.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_MalformedManifest_FailsWhileCliRootStaysReadable()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Malformed.Lib.dll", MalformedManifestImageBytes());

        var (exit, _, error) = await RunAppAsync(
            "library", path, "-S", "Metadata: AssemblyRef",
            "--metadata-root", ManifestRoot, "--tsv", "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.NotEmpty(error);

        var (cliExit, cliOutput, _) = await RunAppAsync(
            "library", path, "--metadata-root", CliRoot, "--tips", "q");
        var (defaultExit, defaultOutput, _) = await RunAppAsync(
            "library", path, "-S", MetadataSectionNames.Image, "--tips", "q");

        Assert.Equal(0, cliExit);
        Assert.Equal(0, defaultExit);
        Assert.Contains("| Canonical root | Cli |", cliOutput, StringComparison.Ordinal);
        Assert.Contains("| Metadata version |", defaultOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Effective discovery describes the selected root. The manifest carries no MethodDef rows and
    /// the CLI root of the same file carries thousands, so a root-specific listing that still names
    /// MethodDef is answering from the wrong address space.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_EffectiveDiscovery_DescribesTheSelectedRoot()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        var (manifestExit, manifestOutput, _) = await RunAppAsync(
            "library", path, "-D", SectionCategoryNames.Metadata, "--effective",
            "--metadata-root", ManifestRoot, "--tsv", "--tips", "q");

        Assert.Equal(0, manifestExit);
        var manifestNames = DiscoveryNames(manifestOutput);
        Assert.Contains("Metadata: AssemblyRef", manifestNames);
        Assert.Contains("Metadata: TypeRef", manifestNames);
        Assert.Contains("Metadata: Module", manifestNames);
        Assert.DoesNotContain("Metadata: MethodDef", manifestNames);

        var (cliExit, cliOutput, _) = await RunAppAsync(
            "library", path, "-D", SectionCategoryNames.Metadata, "--effective",
            "--metadata-root", CliRoot, "--tsv", "--tips", "q");

        Assert.Equal(0, cliExit);
        Assert.Contains("Metadata: MethodDef", DiscoveryNames(cliOutput));
    }

    /// <summary>
    /// Structural discovery is a capability list, so it is the same for either root: only the
    /// <c>--effective</c> listing above is image-dependent. Without this the effective assertions
    /// could pass for a lens that filtered the registered set instead of reading the root.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_StructuralDiscovery_IsRootIndependent()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        var (manifestExit, manifestOutput, _) = await RunAppAsync(
            "library", path, "-D", SectionCategoryNames.Metadata,
            "--metadata-root", ManifestRoot, "--tsv", "--tips", "q");
        var (cliExit, cliOutput, _) = await RunAppAsync(
            "library", path, "-D", SectionCategoryNames.Metadata,
            "--metadata-root", CliRoot, "--tsv", "--tips", "q");

        Assert.Equal(0, manifestExit);
        Assert.Equal(0, cliExit);
        Assert.Equal(DiscoveryNames(cliOutput), DiscoveryNames(manifestOutput));
        Assert.Contains("Metadata: MethodDef", DiscoveryNames(manifestOutput));
    }

    /// <summary>
    /// Bare effective discovery is the cache-enabled route. A warm catalog must not skip
    /// resolution of an explicitly requested, absent root.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_EffectiveDiscovery_BypassesTheWarmedCliCatalog()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("NoManifest.Lib.dll", NoManifestImageBytes());

        var (warmExit, warmOutput, _) = await RunAppAsync(
            "library", path, "-D", "--effective", "--tsv", "--tips", "q");
        Assert.Equal(0, warmExit);
        Assert.Contains(SectionCategoryNames.Metadata, DiscoveryNames(warmOutput));

        var (cachedExit, cachedOutput, cachedTrace) = await RunAppAsync(
            "library", path, "-D", "--effective", "--tsv", "--trace", "--tips", "q");
        Assert.Equal(0, cachedExit);
        Assert.Equal(warmOutput, cachedOutput);
        Assert.Contains(
            "queries executed\n    (none)",
            cachedTrace.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

        var (manifestExit, manifestOutput, manifestError) = await RunAppAsync(
            "library", path, "-D", "--effective",
            "--metadata-root", ManifestRoot, "--tsv", "--tips", "q");

        Assert.Equal(1, manifestExit);
        Assert.Empty(manifestOutput);
        Assert.Contains("The requested ReadyToRunManifest metadata root is absent.", manifestError, StringComparison.Ordinal);

        var (afterExit, afterOutput, _) = await RunAppAsync(
            "library", path, "-D", "--effective", "--tsv", "--tips", "q");

        Assert.Equal(0, afterExit);
        Assert.Equal(warmOutput, afterOutput);
    }

    /// <summary>
    /// A root selection that reaches no metadata-root section is rejected rather than silently
    /// ignored: the caller named an address space for a report that has no addresses in it, and
    /// answering with the ordinary section would be an answer to a different question.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_WithoutAMetadataSection_IsRejected()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        var (exit, output, error) = await RunAppAsync(
            "library", path, "-S", "Custom Attributes", "--metadata-root", ManifestRoot, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--metadata-root", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unrecognized token is refused by the parser, before any file is read.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ManifestRoot_UnknownToken_IsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "--metadata-root", "native", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("native", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>Metadata: ReadyToRun</c> is explicit-only and answers for every managed image, including
    /// the ordinary ones: "no" is a fact about the image, not an empty section. Rendering nothing
    /// here would leave a caller unable to distinguish "not ReadyToRun" from "the question was not
    /// asked".
    /// </summary>
    [Fact]
    public async Task MetadataLens_R2RSection_OrdinaryImage_ReportsNo()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", ReadyToRunSection, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains($"## {ReadyToRunSection}", output, StringComparison.Ordinal);
        // One row, and it is a fact rather than an empty section: a caller must be able to tell
        // "not ReadyToRun" from "the question was not asked".
        Assert.Equal(["ReadyToRun"], FactLabels(output));
        Assert.Contains("| ReadyToRun | no |", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The advertised case: a fixed Property/Value summary of the ReadyToRun envelope, including
    /// the manifest extent and whether it aliases the CLI metadata directory. The alias row is the
    /// one that cannot be derived from the extent alone, so it is asserted in both directions.
    /// </summary>
    [Fact]
    public async Task MetadataLens_R2RSection_AdvertisedImage_ReportsEnvelopeAndManifestExtent()
    {
        using var images = new SyntheticImageDirectory();
        string manifestPath = images.Write("Manifest.Lib.dll", ManifestImageBytes());
        string aliasPath = images.Write("Alias.Lib.dll", AliasImageBytes());

        var (exit, output, error) = await RunAppAsync(
            "library", manifestPath, "-S", ReadyToRunSection, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        // A fixed summary means a fixed row set, in a fixed order: asserted as a sequence rather
        // than as scattered substring hits, because a row silently dropped from the middle is
        // exactly the failure a set of Contains checks cannot see.
        Assert.Equal(
            [
                "ReadyToRun", "Role", "Advertisements", "R2R version",
                "Header RVA", "Header size", "Flags", "Section count",
                "Manifest metadata", "Manifest RVA", "Manifest size", "Aliases CLI metadata",
            ],
            FactLabels(output));

        Assert.Contains("| ReadyToRun | yes |", output, StringComparison.Ordinal);
        Assert.Contains("| Role | Standalone |", output, StringComparison.Ordinal);
        Assert.Contains("| Advertisements | ManagedNativeHeader |", output, StringComparison.Ordinal);
        Assert.Contains("| R2R version | 25.0 |", output, StringComparison.Ordinal);
        Assert.Contains("| Section count | 1 |", output, StringComparison.Ordinal);
        Assert.Contains("| Manifest metadata | present |", output, StringComparison.Ordinal);
        Assert.Contains("| Aliases CLI metadata | no |", output, StringComparison.Ordinal);
        Assert.Matches(@"\| Header RVA \| 0x[0-9A-F]{8} \|", output);
        Assert.Matches(@"\| Manifest RVA \| 0x[0-9A-F]{8} \|", output);
        Assert.Matches(@"\| Manifest size \| \d+ bytes \|", output);
        // The Partial header flag the fixture encodes, rendered as a fixed-width hex word rather
        // than as an enum: unknown flag bits must survive a round trip through the report.
        Assert.Contains("| Flags | 0x00000004 |", output, StringComparison.Ordinal);

        var (aliasExit, aliasOutput, _) = await RunAppAsync(
            "library", aliasPath, "-S", ReadyToRunSection, "--tips", "q");

        Assert.Equal(0, aliasExit);
        Assert.Contains("| Aliases CLI metadata | yes |", aliasOutput, StringComparison.Ordinal);

        // An advertised image with no manifest section says so instead of omitting the rows. The
        // three manifest-extent rows are the only ones that drop, and they drop together.
        string bareRootPath = images.Write("NoManifest.Lib.dll", NoManifestImageBytes());
        var (bareExit, bareOutput, _) = await RunAppAsync(
            "library", bareRootPath, "-S", ReadyToRunSection, "--tips", "q");

        Assert.Equal(0, bareExit);
        Assert.Equal(
            [
                "ReadyToRun", "Role", "Advertisements", "R2R version",
                "Header RVA", "Header size", "Flags", "Section count",
                "Manifest metadata",
            ],
            FactLabels(bareOutput));
        Assert.Contains("| ReadyToRun | yes |", bareOutput, StringComparison.Ordinal);
        Assert.Contains("| Manifest metadata | absent |", bareOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// A malformed ReadyToRun advertisement exits nonzero rather than reporting "no". The two are
    /// opposite facts: "this image is not ReadyToRun" and "this image claims to be ReadyToRun and
    /// its header cannot be read" must never render the same way.
    /// </summary>
    [Fact]
    public async Task MetadataLens_R2RSection_MalformedAdvertisement_ExitsNonZero()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("BadHeader.Lib.dll", MalformedAdvertisementImageBytes());

        var (exit, output, error) = await RunAppAsync(
            "library", path, "-S", ReadyToRunSection, "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("| ReadyToRun | no |", output, StringComparison.Ordinal);
        Assert.NotEmpty(error);

        // The rest of the lens is unaffected: a corrupt R2R header is not a corrupt assembly.
        var (imageExit, imageOutput, _) = await RunAppAsync(
            "library", path, "-S", MetadataSectionNames.Image, "--tips", "q");

        Assert.Equal(0, imageExit);
        Assert.Contains("| Metadata version |", imageOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// The summary stays outside automatic verbosity and the flat catalog, but the explicit
    /// <c>@Metadata</c> door includes it.
    /// </summary>
    [Theory]
    [InlineData("-v:m")]
    [InlineData("-v:d")]
    public async Task MetadataLens_R2RSection_IsReachedOnlyByNameOrTheCategoryDoor(string verbosity)
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        var (exit, output, _) = await RunAppAsync("library", path, verbosity, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain(ReadyToRunSection, output, StringComparison.Ordinal);

        var (discoverExit, discoverOutput, _) = await RunAppAsync(
            "library", path, "-D", "--tsv", "--tips", "q");

        Assert.Equal(0, discoverExit);
        Assert.DoesNotContain(ReadyToRunSection, DiscoveryNames(discoverOutput));

        var (doorExit, doorOutput, _) = await RunAppAsync(
            "library", path, "-S", SectionCategoryNames.Metadata, "--count", "--tips", "q");

        Assert.Equal(0, doorExit);
        Assert.Contains(MetadataSectionNames.Image, doorOutput, StringComparison.Ordinal);
        Assert.Contains(ReadyToRunSection, doorOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fixed summary is one set of rows in every format, and <c>--count</c> reports that same
    /// size — the property <c>Metadata: Image</c> already carries, restated for the new section
    /// because it is rendered through the same fact-row builder.
    /// </summary>
    [Fact]
    public async Task MetadataLens_R2RSection_HasSameRowsInEveryFormat()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        var (markdownExit, markdownOutput, _) = await RunAppAsync(
            "library", path, "-S", ReadyToRunSection, "--tips", "q");
        var (tsvExit, tsvOutput, _) = await RunAppAsync(
            "library", path, "-S", ReadyToRunSection, "--tsv", "--tips", "q");
        var (countExit, countOutput, _) = await RunAppAsync(
            "library", path, "-S", ReadyToRunSection, "--count", "--tips", "q");

        Assert.Equal(0, markdownExit);
        Assert.Equal(0, tsvExit);
        Assert.Equal(0, countExit);

        int markdownRows = markdownOutput.Split('\n')
            .Count(l => l.TrimStart().StartsWith("| ", StringComparison.Ordinal)) - 2;
        int tsvRows = tsvOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;

        Assert.True(markdownRows > 0, "the ReadyToRun section must render facts");
        Assert.Equal(markdownRows, tsvRows);
        Assert.Equal(markdownRows, int.Parse(countOutput.Trim()));
    }

    /// <summary>
    /// ReadyToRun rows take the same JSON verdict as root-selected rows. Lowered JSON is not
    /// implemented for <c>library</c> and this slice does not implement it, so a row request must
    /// be refused with the remedy that exists rather than silently answering with the typed
    /// library document -- a document that contains none of the requested facts. The count and
    /// discovery carve-outs stay available because neither emits section rows.
    /// </summary>
    [Fact]
    public async Task MetadataLens_R2RSection_JsonIsRejectedWithTheRowRemedy()
    {
        using var images = new SyntheticImageDirectory();
        string path = images.Write("Manifest.Lib.dll", ManifestImageBytes());

        var (jsonExit, jsonOutput, jsonError) = await RunAppAsync(
            "library", path, "-S", ReadyToRunSection, "--json", "--tips", "q");

        Assert.Equal(1, jsonExit);
        Assert.DoesNotContain("\"file_name\"", jsonOutput, StringComparison.Ordinal);
        Assert.Contains("do not support --json", jsonError, StringComparison.Ordinal);
        Assert.Contains("--jsonl", jsonError, StringComparison.Ordinal);

        // The column variant is refused too; either owner's diagnostic names the row remedy.
        var (columnExit, columnOutput, columnError) = await RunAppAsync(
            "library", path, "-S", ReadyToRunSection, "--json", "--columns", "Property,Value", "--tips", "q");

        Assert.Equal(1, columnExit);
        Assert.Empty(columnOutput);
        Assert.Contains("--jsonl", columnError, StringComparison.Ordinal);

        var (countExit, countOutput, _) = await RunAppAsync(
            "library", path, "-S", ReadyToRunSection, "--count", "--json", "--tips", "q");

        Assert.Equal(0, countExit);
        Assert.NotEmpty(countOutput.Trim());

        var (discoverExit, discoverOutput, _) = await RunAppAsync(
            "library", path, "-D", "@Metadata", "--effective", "--json", "--tips", "q");

        Assert.Equal(0, discoverExit);
        Assert.Contains("\"name\"", discoverOutput, StringComparison.Ordinal);
    }

    /// <summary>A ReadyToRun image whose manifest is a well-formed, independent metadata root.</summary>
    private static byte[] ManifestImageBytes() => ReadyToRunImageFixture.Create(
        TestAssemblyPath,
        managedNative: true,
        exported: false,
        sections:
        [
            new(
                ReadyToRunImageFixture.ManifestMetadataSectionType,
                ReadyToRunImageFixture.BuildManifestMetadata()),
        ]).Bytes;

    /// <summary>A ReadyToRun image whose manifest section points at the CLI metadata directory.</summary>
    private static byte[] AliasImageBytes() => ReadyToRunImageFixture.Create(
        TestAssemblyPath,
        managedNative: true,
        exported: false,
        manifestAliasesCliMetadata: true,
        sections:
        [
            new(
                ReadyToRunImageFixture.ManifestMetadataSectionType,
                ReadyToRunImageFixture.BuildManifestMetadata()),
        ]).Bytes;

    /// <summary>A ReadyToRun image that advertises no manifest section at all.</summary>
    private static byte[] NoManifestImageBytes() => ReadyToRunImageFixture.Create(
        TestAssemblyPath,
        managedNative: true,
        exported: false,
        sections: [new(ReadyToRunImageFixture.CompilerIdentifierSectionType, [1, 2, 3])]).Bytes;

    /// <summary>A ReadyToRun image whose manifest section is present but not a metadata root.</summary>
    private static byte[] MalformedManifestImageBytes() => ReadyToRunImageFixture.Create(
        TestAssemblyPath,
        managedNative: true,
        exported: false,
        sections: [new(ReadyToRunImageFixture.ManifestMetadataSectionType, "BSJB"u8.ToArray())]).Bytes;

    /// <summary>
    /// A ReadyToRun image whose header advertises an impossible section count, so the envelope
    /// itself cannot be read.
    /// </summary>
    private static byte[] MalformedAdvertisementImageBytes()
    {
        var image = ReadyToRunImageFixture.Create(
            TestAssemblyPath,
            managedNative: true,
            exported: false);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            image.Bytes.AsSpan(image.HeaderOffset + 12, 4), uint.MaxValue);
        return image.Bytes;
    }

    /// <summary>
    /// The Property column of a rendered Property/Value fact table, in order, with the header and
    /// separator rows dropped. Used to assert a fixed summary as a sequence rather than as a bag of
    /// substrings, so a row dropped from the middle cannot pass.
    /// </summary>
    private static string[] FactLabels(string markdown) => markdown
        .Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.StartsWith("| ", StringComparison.Ordinal))
        .Select(l => l.Split('|', StringSplitOptions.RemoveEmptyEntries)[0].Trim())
        .Where(l => !l.Equals("Property", StringComparison.Ordinal)
            && !l.All(c => c is '-' or ':'))
        .ToArray();

    /// <summary>TSV data rows: every line but the header, carriage returns trimmed.</summary>
    private static string[] DataRows(string output) => output
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.TrimEnd('\r'))
        .Skip(1)
        .ToArray();

    /// <summary>The root-provenance and coordinate rows of a rendered image-facts section.</summary>
    private static string[] RootRows(string output) => output
        .Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.StartsWith("| Requested root ", StringComparison.Ordinal)
            || l.StartsWith("| Canonical root ", StringComparison.Ordinal)
            || l.StartsWith("| Root RVA ", StringComparison.Ordinal)
            || l.StartsWith("| Root size ", StringComparison.Ordinal))
        .ToArray();

    /// <summary>
    /// Resolves the address column by its emitted header rather than another numeric cell.
    /// </summary>
    private static int HeapAddressOf(string tsv, string value)
    {
        string[] headers = tsv.Split('\n')[0].TrimEnd('\r').Split('\t');
        int addressColumn = Array.FindIndex(headers, header =>
            header.Equals("address", StringComparison.OrdinalIgnoreCase));
        Assert.True(addressColumn >= 0);
        string row = Assert.Single(DataRows(tsv), line =>
            line.Split('\t').Contains(value, StringComparer.Ordinal));
        return int.Parse(row.Split('\t')[addressColumn], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A temporary directory of synthetic images. Each test builds its own so a fixture written by
    /// one case can never answer for another through the content-keyed discovery cache.
    /// </summary>
    private sealed class SyntheticImageDirectory : IDisposable
    {
        public SyntheticImageDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), $"metadata-root-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Write(string name, byte[] bytes)
        {
            string path = Path.Combine(Root, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
