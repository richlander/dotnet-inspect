using System.IO.Compression;
using DotnetInspector.Models;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// The <c>@Metadata</c> lens on <c>library</c>: the raw ECMA-335 table projection reached through
/// the normal section surface.
///
/// The load-bearing property is <em>disclosure</em>. These sections are unbounded — a table such as
/// MethodDef grows with the assembly — so no verbosity, not even <c>-v:d</c>, may render one. They
/// are reachable only by exact name or the <c>@Metadata</c> door.
/// <see cref="MetadataLens_NoVerbosity_RendersAnyMetadataSection"/> is the gate that enforces that;
/// it is not vacuous, because <see cref="MetadataLens_ExactName_RendersOnlyThatTable"/> proves the
/// same sections do render when asked for.
/// </summary>
public partial class CommandExecutionTests
{
    private const string MetadataHeadingPrefix = "## " + MetadataSectionNames.Prefix;

    /// <summary>
    /// The registered section set is derived from <see cref="MetadataTableProjector.ProjectedTables"/>,
    /// not restated. This asserts <em>set equality</em> so both failure directions are caught: a
    /// table added to the projector without a section, and a section left behind by a table the
    /// projector dropped. Without the equality a stale entry would pass unnoticed.
    /// </summary>
    [Fact]
    public void MetadataLens_RegisteredSections_EqualProjectedTables()
    {
        var expected = new[] { MetadataSectionNames.Image }
            .Concat(MetadataTableProjector.ProjectedTables.Select(t => MetadataSectionNames.Prefix + t))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected, MetadataSectionNames.All.ToHashSet(StringComparer.Ordinal));

        var registered = LibrarySections.CreatePipeline()
            .AllSectionNames
            .Where(MetadataSectionNames.IsMetadataSection)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected, registered);
    }

    /// <summary>
    /// Every metadata section belongs to the <c>@Metadata</c> category, so no table can be
    /// registered yet be unreachable through the door.
    /// </summary>
    [Fact]
    public void MetadataLens_CategoryMembership_CoversEveryRegisteredSection()
    {
        var categories = LibrarySections.CreatePipeline().GetCategoryMap();
        Assert.True(categories.TryGetValue(SectionCategoryNames.Metadata, out var members));
        Assert.Equal(
            MetadataSectionNames.All.ToHashSet(StringComparer.Ordinal),
            members.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// The disclosure gate. No verbosity may surface a metadata section, because the tables are
    /// unbounded and would swamp every default view. Covers the whole ladder plus <c>-S @All</c>,
    /// rather than a single level, so promoting one verbosity or fanning out the all-selector
    /// cannot slip through.
    ///
    /// Suppression is enforced twice over — <c>ExplicitOnly</c> and <c>SectionCost.Unbounded</c>
    /// were each measured to be sufficient alone — so this gate fails only when both are lost.
    /// See the remarks on <see cref="MetadataSections.AddMetadataLens"/>.
    /// </summary>
    [Theory]
    [InlineData("-v:q")]
    [InlineData("-v:m")]
    [InlineData("-v:n")]
    [InlineData("-v:d")]
    public async Task MetadataLens_NoVerbosity_RendersAnyMetadataSection(string verbosity)
    {
        var (exit, output, _) = await RunAppAsync("library", TestAssemblyPath, verbosity, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain(MetadataHeadingPrefix, output, StringComparison.Ordinal);

        // @All is a separate door: it fans out to every section a verbosity would reach, so it is
        // its own way for an unbounded table to arrive unasked-for.
        var (allExit, allOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, verbosity, "-S", "@All", "--tips", "q");

        Assert.Equal(0, allExit);
        Assert.DoesNotContain(MetadataHeadingPrefix, allOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Selecting a section by exact name renders that table and only that table. This is also what
    /// keeps <see cref="MetadataLens_NoVerbosity_RendersAnyMetadataSection"/> honest: the sections
    /// really do produce output, so its absence assertion is measuring suppression rather than an
    /// empty projection.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ExactName_RendersOnlyThatTable()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Metadata: TypeRef", "--rows", "5", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Metadata: TypeRef", output, StringComparison.Ordinal);

        var headings = output.Split('\n')
            .Where(l => l.StartsWith(MetadataHeadingPrefix, StringComparison.Ordinal))
            .Select(l => l.Trim())
            .ToArray();
        Assert.Equal(["## Metadata: TypeRef"], headings);
    }

    /// <summary>
    /// The category door selects the whole family. Asserted through <c>--count</c> because that
    /// reports the per-section row counts without printing a hundred thousand rows.
    /// </summary>
    [Fact]
    public async Task MetadataLens_CategoryDoor_SelectsTheWholeFamily()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", SectionCategoryNames.Metadata, "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Metadata: TypeDef", output, StringComparison.Ordinal);
        Assert.Contains("Metadata: TypeRef", output, StringComparison.Ordinal);
        Assert.Contains("Metadata: Image", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Bare <c>-D</c> lists the door but not its seventeen members: the tables are registered with
    /// <c>ListedInCatalog = false</c> so they do not crowd out the sections a caller actually
    /// reaches for. Both halves are asserted — a door with no way in, and a catalog flooded with
    /// tables, are both failures.
    /// </summary>
    [Fact]
    public async Task MetadataLens_BareDiscovery_ListsDoorWithoutMembers()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        var names = DiscoveryNames(output);

        Assert.Contains(SectionCategoryNames.Metadata, names);
        Assert.DoesNotContain(names, n => n.StartsWith(MetadataSectionNames.Prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Drilling into the door lists the tables that carry rows and omits the ones that do not, so
    /// discovery describes this image rather than the ECMA-335 spec. The test assembly always has
    /// TypeDef rows and never has ExportedType rows.
    /// </summary>
    [Fact]
    public async Task MetadataLens_DiscoveryDrillIn_ListsOnlyTablesWithRows()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", SectionCategoryNames.Metadata, "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        var names = DiscoveryNames(output);

        Assert.Contains("Metadata: TypeDef", names);
        Assert.Contains("Metadata: Image", names);
        Assert.DoesNotContain("Metadata: ExportedType", names);
        Assert.All(names, n => Assert.StartsWith(MetadataSectionNames.Prefix, n, StringComparison.Ordinal));
    }

    /// <summary>
    /// Regression gate for the effective-section cache. Entries are written with
    /// <c>AppendLine</c> (CRLF on Windows) and read back by splitting on <c>'\n'</c>, which left a
    /// trailing carriage return welded to every cached section name. A name carrying <c>'\r'</c>
    /// compares unequal to the registered name, so it silently escaped every name-keyed filter —
    /// which is how seventeen catalog-hidden tables reached the top-level catalog while the
    /// <c>@Metadata</c> door itself was filtered out.
    ///
    /// Runs discovery twice: the second run is guaranteed to be served from the cache written by
    /// the first, so the two must agree exactly.
    /// </summary>
    [Fact]
    public async Task MetadataLens_DiscoveryCatalog_SurvivesCacheRoundTrip()
    {
        var (coldExit, cold, _) = await RunAppAsync("library", TestAssemblyPath, "-D", "--tsv", "--tips", "q");
        var (warmExit, warm, _) = await RunAppAsync("library", TestAssemblyPath, "-D", "--tsv", "--tips", "q");

        Assert.Equal(0, coldExit);
        Assert.Equal(0, warmExit);
        Assert.Equal(DiscoveryNames(cold), DiscoveryNames(warm));
        Assert.DoesNotContain('\r', warm.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains(SectionCategoryNames.Metadata, DiscoveryNames(warm));
    }

    /// <summary>
    /// A table with no rows is excluded rather than rendered as an empty section, and the caller is
    /// told why. Emitting an empty table would read as "this table has no rows" and "the projection
    /// failed" identically.
    /// </summary>
    [Fact]
    public async Task MetadataLens_EmptyTable_IsExcludedNotRenderedEmpty()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Metadata: ExportedType", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("## Metadata: ExportedType", output, StringComparison.Ordinal);
        // Named explicitly, so "this table is empty" never reads the same as "the projection
        // failed" or "the section was never requested".
        Assert.Contains("no data", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Metadata: ExportedType", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tabular output carries a leading <c>table</c> column, so a row remains self-identifying once
    /// the Markdown headings that separated the tables are gone. Without it, rows from MethodDef
    /// and Param would interleave into one undifferentiated stream.
    /// </summary>
    [Fact]
    public async Task MetadataLens_Tabular_RowsAreSelfIdentifying()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Metadata: TypeRef", "--tsv", "--rows", "3", "--tips", "q");

        Assert.Equal(0, exit);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        Assert.StartsWith("table\t", lines[0], StringComparison.Ordinal);
        var dataRows = lines.Skip(1).ToArray();
        Assert.NotEmpty(dataRows);
        Assert.All(dataRows, l => Assert.StartsWith("TypeRef\t", l, StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>--rows</c> windows the lens exactly as it windows every other section: the lens inherits
    /// the shared row limiter rather than reimplementing a window over the projection.
    /// </summary>
    [Fact]
    public async Task MetadataLens_Rows_WindowsTheTable()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Metadata: TypeRef", "--tsv", "--rows", "2..4", "--tips", "q");

        Assert.Equal(0, exit);
        var dataRows = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Skip(1)
            .ToArray();

        Assert.Equal(3, dataRows.Length);
        // The row-id column follows the table label and is image-relative, so the window is
        // addressing real metadata row ids rather than renumbering from one.
        Assert.Equal(["2", "3", "4"], dataRows.Select(r => r.Split('\t')[1]));
    }

    /// <summary>
    /// A package that resolves to several assemblies is rejected rather than rendered. Row ids are
    /// image-relative and the section names carry no assembly, so a multi-assembly document would
    /// repeat <c>## Metadata: TypeDef</c> with rows silently belonging to different images.
    /// </summary>
    [Fact]
    public async Task MetadataLens_MultipleAssemblies_IsRejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"metadata-multitfm-{Guid.NewGuid():N}");
        try
        {
            var content = Path.Combine(tempDir, "content");
            foreach (var tfm in new[] { "net8.0", "net10.0" })
            {
                var dir = Path.Combine(content, "lib", tfm);
                Directory.CreateDirectory(dir);
                File.Copy(TestAssemblyPath, Path.Combine(dir, "Lib.dll"));
            }
            var packagePath = Path.Combine(tempDir, "Metadata.MultiTfm.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(content, packagePath);

            var (exit, output, error) = await RunAppAsync(
                "library", "Lib.dll", "--package", packagePath, "--tfm", "all",
                "-S", SectionCategoryNames.Metadata, "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Contains("single assembly", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--library", error, StringComparison.Ordinal);
            Assert.DoesNotContain(MetadataHeadingPrefix, output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>Section names from a <c>-D --tsv</c> listing, header row dropped.</summary>
    private static string[] DiscoveryNames(string output) => output
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.TrimEnd('\r').Split('\t')[0])
        .Where(n => !n.Equals("name", StringComparison.Ordinal))
        .ToArray();
}
