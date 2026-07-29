using System.IO.Compression;
using DotnetInspector.MetadataRendering;
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
    /// The registered section set is derived from
    /// <see cref="MetadataTableProjector.ProjectedTables"/> and
    /// <see cref="MetadataHeapCoordinate.Heaps"/>, not restated. This asserts <em>set equality</em>
    /// so both failure directions are caught: a table or heap gaining no section, and a section
    /// left behind by one that was dropped. Without the equality a stale entry would pass
    /// unnoticed.
    /// </summary>
    [Fact]
    public void MetadataLens_RegisteredSections_EqualProjectedTables()
    {
        var expected = new[] { MetadataSectionNames.Image, MetadataSectionNames.Heap }
            .Concat(MetadataHeapCoordinate.Heaps.Select(
                h => MetadataSectionNames.Prefix + MetadataHeapCoordinate.StreamName(h)))
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

    /// <summary>
    /// The Vector rung of the shape ladder: <c>--columns</c> narrows a table section to the named
    /// ECMA-335 columns, and the schema that <c>-D</c> reports is the same list, so the remedy the
    /// projection error suggests is not a dead end.
    ///
    /// Gate for the derivation in <see cref="MetadataSectionNames.ColumnsFor"/>: it reads
    /// <c>MetadataTableProjector.ColumnsFor</c>, the same declaration the projection renders from,
    /// so a schema that advertises a column the renderer does not emit is not expressible.
    /// </summary>
    [Fact]
    public async Task MetadataLens_Columns_NarrowsTableAndMatchesDiscovery()
    {
        var (discoverExit, discoverOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "Metadata: TypeRef", "--tsv", "--tips", "q");

        Assert.Equal(0, discoverExit);
        var columns = DiscoveryNames(discoverOutput);
        Assert.Equal(
            new[] { MetadataSectionNames.RowIdColumn, "ResolutionScope", "Name", "Namespace" },
            columns);

        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Metadata: TypeRef",
            "--columns", "Name", "--rows", "2", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        var rows = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r').Split('\t'))
            .ToArray();

        // table + rid + Name, and nothing else: ResolutionScope and Namespace are gone.
        Assert.All(rows, cells => Assert.Equal(3, cells.Length));
        Assert.Equal(new[] { "table", "rid", "name" }, rows[0]);
        Assert.Equal(2, rows.Length - 1);
    }

    /// <summary>
    /// A column name absent from the schema is rejected rather than silently ignored, and the
    /// remedy the error names resolves to a real column list.
    /// </summary>
    [Fact]
    public async Task MetadataLens_UnknownColumn_IsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Metadata: TypeRef", "--columns", "Bogus", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("No columns matched projection: Bogus", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// One section means one set of rows in every format. <c>Metadata: Image</c> renders the same
    /// facts in Markdown and in the machine formats, deliberately not the standalone report's
    /// three-part shape — otherwise <c>--count</c> and <c>--rows</c> would report different sizes
    /// for the same selection depending on the output flag.
    ///
    /// This is the gate for that choice: it fails if the tabular path is routed back through
    /// <c>MetadataProjectionRenderer.Render</c>, which folds in per-table row counts.
    /// </summary>
    [Fact]
    public async Task MetadataLens_ImageSection_HasSameRowsInEveryFormat()
    {
        var (markdownExit, markdownOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", MetadataSectionNames.Image, "--tips", "q");
        var (tsvExit, tsvOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", MetadataSectionNames.Image, "--tsv", "--tips", "q");

        Assert.Equal(0, markdownExit);
        Assert.Equal(0, tsvExit);

        // Markdown table lines less the header and separator rows.
        int markdownRows = markdownOutput
            .Split('\n')
            .Count(l => l.TrimStart().StartsWith("| ", StringComparison.Ordinal)) - 2;
        int tsvRows = tsvOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;

        Assert.True(markdownRows > 0, "the image section must render facts");
        Assert.Equal(markdownRows, tsvRows);
    }

    /// <summary>
    /// Replacing an assembly's bytes invalidates its cached discovery catalog, even when the
    /// replacement is exactly the same size.
    ///
    /// The effective-section cache is keyed by identity, not content, so size alone was not
    /// enough: rebuilding in place or copying a different assembly over the same path routinely
    /// produces a same-sized file, and the stale entry then answered for the new bytes. This is
    /// one of the two gates for the content-hash component of that key — it fails if the key
    /// drops back to path + size.
    ///
    /// Bare <c>-D</c> is used deliberately: the cache is only consulted when <c>Discover</c> is
    /// empty, so a category drill-in such as <c>-D @Metadata</c> would recompute and pass
    /// vacuously.
    /// </summary>
    [Fact]
    public async Task LibraryCommand_DiscoverEffective_SameSizeReplacement_InvalidatesCache()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"effcache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Two assemblies whose discovery catalogs differ, the smaller padded to the larger's
            // length so the pair collides on every non-content component of the old key.
            var large = TestAssemblyPath;
            var small = typeof(DotnetInspector.Core.CoreCache).Assembly.Location;
            var largeBytes = File.ReadAllBytes(large);
            var smallBytes = File.ReadAllBytes(small);
            // Asserted rather than skipped: a silent early return would disable this gate
            // wholesale if the fixture pair ever changed size relationship.
            Assert.True(
                smallBytes.Length < largeBytes.Length,
                $"fixture requires a smaller padding source: {small} ({smallBytes.Length}) must be " +
                $"smaller than {large} ({largeBytes.Length})");

            var probe = Path.Combine(dir, "Probe.dll");
            File.WriteAllBytes(probe, largeBytes);
            var (firstExit, firstOutput, _) = await RunAppAsync("library", probe, "-D", "--tsv", "--tips", "q");

            var padded = new byte[largeBytes.Length];
            smallBytes.CopyTo(padded, 0);
            File.WriteAllBytes(probe, padded);
            var (secondExit, secondOutput, _) = await RunAppAsync("library", probe, "-D", "--tsv", "--tips", "q");

            // Ground truth: the same bytes at a path the cache has never seen.
            var fresh = Path.Combine(dir, "Fresh.dll");
            File.WriteAllBytes(fresh, padded);
            var (truthExit, truthOutput, _) = await RunAppAsync("library", fresh, "-D", "--tsv", "--tips", "q");

            Assert.Equal(0, firstExit);
            Assert.Equal(0, secondExit);
            Assert.Equal(0, truthExit);
            Assert.Equal(new FileInfo(probe).Length, largeBytes.Length);

            // Only meaningful if the two assemblies really do disagree.
            Assert.NotEqual(Normalize(firstOutput), Normalize(truthOutput));
            Assert.Equal(Normalize(truthOutput), Normalize(secondOutput));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        static string Normalize(string output) => string.Join(
            '\n',
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')));
    }

    /// <summary>
    /// Replacing an assembly's bytes invalidates its cached discovery catalog even when the
    /// replacement has both the same size and the same write time.
    ///
    /// A write time is not a content identity. A copy preserves the source's stamp, an archive
    /// restores a coarse and often deliberately fixed one, and two writes inside a single
    /// filesystem timestamp tick share one — measured on this repository's NTFS volume, 19 of
    /// 2000 back-to-back rewrites produced an identical stamp. This is the second gate for the
    /// content-hash component of the key: it fails if the key reverts to path + size + write
    /// time, which the earlier same-size gate alone would not catch.
    /// </summary>
    [Fact]
    public async Task LibraryCommand_DiscoverEffective_PreservedWriteTimeReplacement_InvalidatesCache()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"effcache-mtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var large = TestAssemblyPath;
            var small = typeof(DotnetInspector.Core.CoreCache).Assembly.Location;
            var largeBytes = File.ReadAllBytes(large);
            var smallBytes = File.ReadAllBytes(small);
            Assert.True(
                smallBytes.Length < largeBytes.Length,
                $"fixture requires a smaller padding source: {small} ({smallBytes.Length}) must be " +
                $"smaller than {large} ({largeBytes.Length})");

            var probe = Path.Combine(dir, "Probe.dll");
            File.WriteAllBytes(probe, largeBytes);
            var stamp = File.GetLastWriteTimeUtc(probe);
            var (firstExit, firstOutput, _) = await RunAppAsync("library", probe, "-D", "--tsv", "--tips", "q");

            // Same path, same length, and the write time restored to the warmed entry's value:
            // every non-content component of the key is now identical.
            var padded = new byte[largeBytes.Length];
            smallBytes.CopyTo(padded, 0);
            File.WriteAllBytes(probe, padded);
            File.SetLastWriteTimeUtc(probe, stamp);
            var (secondExit, secondOutput, _) = await RunAppAsync("library", probe, "-D", "--tsv", "--tips", "q");

            // Ground truth: the same bytes at a path the cache has never seen.
            var fresh = Path.Combine(dir, "Fresh.dll");
            File.WriteAllBytes(fresh, padded);
            var (truthExit, truthOutput, _) = await RunAppAsync("library", fresh, "-D", "--tsv", "--tips", "q");

            Assert.Equal(0, firstExit);
            Assert.Equal(0, secondExit);
            Assert.Equal(0, truthExit);

            // The collision the key must survive really was constructed.
            Assert.Equal(largeBytes.Length, new FileInfo(probe).Length);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(probe));

            // Only meaningful if the two assemblies really do disagree.
            Assert.NotEqual(Normalize(firstOutput), Normalize(truthOutput));
            Assert.Equal(Normalize(truthOutput), Normalize(secondOutput));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        static string Normalize(string output) => string.Join(
            '\n',
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')));
    }

    // ---- --heap coordinate carrier and heap listings (#3467) ------------------------------

    /// <summary>
    /// The coordinate carrier resolves one heap value and renders it under the section name.
    ///
    /// Also the honesty check for the two discoverability gates below: the section really does
    /// produce output when a coordinate is given, so their absence assertions measure gating
    /// rather than a section that never renders.
    /// </summary>
    [Fact]
    public async Task MetadataLens_HeapCoordinate_RendersTheValueAtThatAddress()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "--heap", "#Strings:1", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## " + MetadataSectionNames.Heap, output, StringComparison.Ordinal);
        Assert.Contains("| #Strings | 1 |", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A coordinate is a coordinate however it is spelled: the ECMA-335 stream name and the
    /// HeapKind member name, hex and decimal, all address the same entry. Asserted by rendering
    /// the same value four ways rather than by unit-testing the parser, so the equivalence holds
    /// through the command surface and not just inside it.
    /// </summary>
    [Theory]
    [InlineData("#Strings:1")]
    [InlineData("String:1")]
    [InlineData("Strings:0x1")]
    [InlineData("#Strings:0x01")]
    public async Task MetadataLens_HeapCoordinate_AcceptsEverySpelling(string coordinate)
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "--heap", coordinate, "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("#Strings\t1\t", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The acceptance property of #3467: the coordinate-scoped section is discoverable exactly
    /// when its coordinate exists. Both directions are asserted in one test because either alone
    /// passes trivially — a section that is never listed satisfies the first, and one that is
    /// always listed satisfies the second.
    /// </summary>
    [Fact]
    public async Task MetadataLens_HeapSection_IsDiscoverableOnlyWithItsCoordinate()
    {
        var (withoutExit, withoutOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", SectionCategoryNames.Metadata, "--tsv", "--tips", "q");

        Assert.Equal(0, withoutExit);
        Assert.DoesNotContain(MetadataSectionNames.Heap, DiscoveryNames(withoutOutput));

        var (withExit, withOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", SectionCategoryNames.Metadata,
            "--heap", "#Strings:1", "--tsv", "--tips", "q");

        Assert.Equal(0, withExit);
        Assert.Contains(MetadataSectionNames.Heap, DiscoveryNames(withOutput));
    }

    /// <summary>
    /// Naming the coordinate section without a coordinate is an error, not an empty section: the
    /// caller asked for something specific that cannot exist, and silently rendering nothing would
    /// read as "this assembly has no heaps".
    /// </summary>
    [Fact]
    public async Task MetadataLens_HeapSection_WithoutCoordinate_IsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", MetadataSectionNames.Heap, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("--heap", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reaching the coordinate section through the <c>@Metadata</c> door without a coordinate is
    /// not an error — a category selection asks for whatever applies — so the rest of the family
    /// still renders. This is the negative case that keeps the rejection above from being a blanket
    /// refusal.
    /// </summary>
    [Fact]
    public async Task MetadataLens_CategoryDoor_WithoutHeapCoordinate_StillRenders()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", SectionCategoryNames.Metadata, "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Metadata: Image", output, StringComparison.Ordinal);
        Assert.DoesNotContain(MetadataSectionNames.Heap, output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A malformed coordinate is rejected before any assembly is read, and the diagnostic names
    /// the half that is wrong rather than the whole coordinate.
    /// </summary>
    [Theory]
    [InlineData("#Strings", "not a heap reference")]
    [InlineData("#Nope:1", "unknown heap")]
    [InlineData("#Strings:zz", "not a heap address")]
    // Reported by adversarial review of #3497: NumberStyles.AllowHexSpecifier on a signed int
    // wraps, so this parsed as -2147483648, reached the heap read, and rendered a malformed cell
    // while still exiting 0. Asserted through the command surface because the exit code was the
    // part that was wrong.
    [InlineData("#Strings:0x80000000", "not a heap address")]
    public async Task MetadataLens_MalformedHeapCoordinate_NamesTheHalfThatIsWrong(
        string coordinate, string expected)
    {
        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "--heap", coordinate, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A string-heap listing renders the values rows point at, and says so. The caveat is asserted
    /// because it is the load-bearing half: without it a referenced-values listing reads as a walk
    /// of the heap, which SRM cannot do and this does not claim to.
    /// </summary>
    [Fact]
    public async Task MetadataLens_StringHeapListing_MarksItselfReferencedOnly()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", MetadataSectionNames.ForHeap(HeapKind.String),
            "--rows", "5", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Metadata: #Strings", output, StringComparison.Ordinal);
        Assert.Contains("not a walk of the heap", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The GUID heap is the one heap that can be listed completely, because its entries are
    /// fixed-size records. The entry count is asserted against the heap size the image scan
    /// reports, so a listing that silently stopped short would fail.
    /// </summary>
    [Fact]
    public async Task MetadataLens_GuidHeapListing_ListsEveryEntry()
    {
        using var session = AssemblyInspectionSession.Open(TestAssemblyPath);
        var overview = session.MetadataImage();
        Assert.NotNull(overview);
        int expected = overview!.Heaps.Single(h => h.Heap == HeapKind.Guid).MaxAddress;
        Assert.True(expected > 0, "The test assembly must store at least one GUID for this gate to mean anything.");

        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", MetadataSectionNames.ForHeap(HeapKind.Guid),
            "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal(expected.ToString(), output.Trim());
    }

    /// <summary>
    /// The user-string heap lists nothing and says why. This is the case a listing must not
    /// fake: no table column points into #US, so an empty table here is a blind spot, and rendering
    /// it without the explanation would report a populated heap as empty.
    /// </summary>
    [Fact]
    public async Task MetadataLens_UserStringHeapListing_ExplainsWhyItIsEmpty()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", MetadataSectionNames.ForHeap(HeapKind.UserString), "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Metadata: #US", output, StringComparison.Ordinal);
        Assert.Contains("ldstr", output, StringComparison.Ordinal);
        Assert.Contains("cannot be walked", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The heap listings join the disclosure gate: they are the largest amplification surface in
    /// the projection, so no verbosity and no <c>-S @All</c> may render one.
    /// </summary>
    [Theory]
    [InlineData("-v:q")]
    [InlineData("-v:d")]
    public async Task MetadataLens_NoVerbosity_RendersAnyHeapListing(string verbosity)
    {
        foreach (var args in new[]
                 {
                     new[] { "library", TestAssemblyPath, verbosity, "--tips", "q" },
                     new[] { "library", TestAssemblyPath, verbosity, "-S", "@All", "--tips", "q" },
                 })
        {
            var (exit, output, _) = await RunAppAsync(args);

            Assert.Equal(0, exit);
            foreach (var heap in MetadataHeapCoordinate.Heaps)
                Assert.DoesNotContain("## " + MetadataSectionNames.ForHeap(heap), output, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A well-formed coordinate that does not resolve is an error, not a successful render
    /// containing a malformed cell. Reported by adversarial review of #3497, which observed exit 0
    /// with a <c>!malformed</c> row — and, worse, <c>-D</c> still advertising the section.
    ///
    /// The distinction the product draws: a bad heap reference inside a projected table row is a
    /// fact about the image and renders as <c>!malformed</c>; a coordinate is the caller's own
    /// input naming one thing that does not exist. <c>--il-offset</c> already exits 1 here.
    /// </summary>
    [Fact]
    public async Task MetadataLens_UnresolvableHeapCoordinate_IsAnErrorNotAMalformedRow()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "--heap", "#Strings:999999999", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("#Strings", error, StringComparison.Ordinal);
        Assert.Contains("999999999", error, StringComparison.Ordinal);
        Assert.DoesNotContain("## " + MetadataSectionNames.Heap, output, StringComparison.Ordinal);

        // Discovery must not advertise a section the coordinate cannot produce.
        var (discoverExit, discoverOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", SectionCategoryNames.Metadata,
            "--heap", "#Strings:999999999", "--tsv", "--tips", "q");

        Assert.Equal(1, discoverExit);
        Assert.DoesNotContain(MetadataSectionNames.Heap, DiscoveryNames(discoverOutput));
    }

    /// <summary>
    /// A cached <c>-D</c> catalog must not short-circuit coordinate resolution. Reported by
    /// adversarial review of #3497: the cache <em>write</em> sites were guarded by
    /// <c>HasHeapCoordinate</c> but the <em>read</em> sites were not, so a warm cache returned a
    /// stale catalog and exit 0 — silently skipping the resolution that would have rejected the
    /// coordinate. Priming twice is what makes this a cache-hit test rather than a cold-path one.
    /// </summary>
    [Fact]
    public async Task MetadataLens_CachedDiscovery_DoesNotBypassHeapCoordinateResolution()
    {
        for (int i = 0; i < 2; i++)
        {
            var (primeExit, _, _) = await RunAppAsync(
                "library", TestAssemblyPath, "-D", "--tsv", "--tips", "q");
            Assert.Equal(0, primeExit);
        }

        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "--heap", "#Strings:999999999", "--tsv", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("999999999", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The point of #3468: a hex table index addresses the same section as its name. A reader who
    /// has a raw index in hand — from a spec table, a token dump, or another tool — should not
    /// have to translate it before selecting.
    ///
    /// The rendered heading must be the <em>canonical</em> one. The alias rewrites the input, so
    /// there is exactly one section and one spelling of it in the output; a hex alias registered
    /// as its own section would have produced a second heading that sorted and counted
    /// independently.
    /// </summary>
    [Fact]
    public async Task MetadataLens_HexTableSelection_RendersTheCanonicalSection()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Metadata: 0x02", "--rows", "3", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## " + MetadataSectionNames.ForTable(System.Reflection.Metadata.Ecma335.TableIndex.TypeDef), output, StringComparison.Ordinal);
        Assert.DoesNotContain("0x02", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Hex and name are the same selector, not merely two selectors that both work: identical
    /// output, and identical <c>--count</c>. Comparing whole documents is what would catch an
    /// alias that reached the right rows through a different section identity.
    ///
    /// The lowercase and uppercase prefixes are here because section matching is case-insensitive
    /// everywhere else in this CLI, so the alias must be too — a case-sensitive prefix check would
    /// make <c>metadata: 0x02</c> the one spelling that silently stopped working. Adversarial
    /// review of #3510 found that gap by mutation.
    /// </summary>
    [Theory]
    [InlineData("Metadata: 0x02")]
    [InlineData("Metadata: 0x2")]
    [InlineData("Metadata: 0X02")]
    [InlineData("metadata: 0x02")]
    [InlineData("METADATA: 0x2")]
    public async Task MetadataLens_HexTableSpellings_AreTheNameSelector(string hex)
    {
        var (hexExit, hexOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", hex, "--rows", "5", "--tips", "q");
        var (nameExit, nameOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Metadata: TypeDef", "--rows", "5", "--tips", "q");

        Assert.Equal(0, hexExit);
        Assert.Equal(nameExit, hexExit);
        Assert.Equal(nameOutput, hexOutput);

        var (hexCount, hexCountOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", hex, "--count", "--tips", "q");
        var (nameCount, nameCountOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Metadata: TypeDef", "--count", "--tips", "q");

        Assert.Equal(0, hexCount);
        Assert.Equal(nameCount, hexCount);
        Assert.Equal(nameCountOutput, hexCountOutput);
    }

    /// <summary>
    /// Selecting both spellings at once selects one section, not two. This is the property that a
    /// second registered section would break while every single-spelling test above still passed.
    ///
    /// The hex-alone precondition is what keeps this non-vacuous: without it, an implementation
    /// that simply ignored the hex selector would also produce one heading and pass. Mutation
    /// testing found exactly that hole.
    /// </summary>
    [Fact]
    public async Task MetadataLens_BothTableSpellings_SelectOneSection()
    {
        var (aloneExit, aloneOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Metadata: 0x02", "--rows", "3", "--tips", "q");
        Assert.Equal(0, aloneExit);

        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Metadata: 0x02", "-S", "Metadata: TypeDef", "--rows", "3", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal(1, output.Split('\n').Count(l => l.StartsWith("## ", StringComparison.Ordinal)));
        Assert.Equal(aloneOutput, output);
    }

    /// <summary>
    /// A bad hex index fails the whole run even alongside a selector that does match — a
    /// deliberate divergence from the unknown-<em>name</em> rule, which tolerates a miss when
    /// something else matched.
    ///
    /// The tolerance rule exists for names that may exist in one inspected assembly and not
    /// another. A hex index outside the projection is not that: no image can ever supply it, so
    /// tolerating it would silently drop a selector the caller definitely got wrong. This matches
    /// the <c>--heap</c> coordinate rule, where input that names nothing is exit 1.
    /// </summary>
    [Fact]
    public async Task MetadataLens_BadHexTable_FailsEvenBesideAMatchingSelector()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Metadata: 0x99", "-S", "Metadata: TypeDef", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Metadata: 0x99", error, StringComparison.Ordinal);
        Assert.DoesNotContain("## ", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Discovery answers in canonical names only. The hex form is an input alias, so advertising
    /// it would double the catalog and imply two sections exist. <c>-D</c> on a hex selector still
    /// works — it is the same selector — and returns exactly the name form's answer.
    /// </summary>
    [Fact]
    public async Task MetadataLens_Discovery_AnswersInCanonicalNamesOnly()
    {
        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", SectionCategoryNames.Metadata, "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain(DiscoveryNames(output), n => n.Contains("0x", StringComparison.OrdinalIgnoreCase));

        var (hexExit, hexOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "Metadata: 0x02", "--tsv", "--tips", "q");
        var (nameExit, nameOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "Metadata: TypeDef", "--tsv", "--tips", "q");

        Assert.Equal(0, hexExit);
        Assert.Equal(nameExit, hexExit);
        Assert.Equal(nameOutput, hexOutput);
    }

    /// <summary>
    /// A hex index the projection does not cover is rejected with a diagnostic that names what is
    /// available, and exits 1 — the same treatment a coordinate that names nothing gets. Silently
    /// rendering nothing would read as "this table is empty in this image", which is a different
    /// and wrong claim.
    ///
    /// <c>0x02000015</c> is the close negative: it is a well-formed metadata <em>token</em>, whose
    /// high byte is the table this test's sibling accepts. A token addresses a row, not a table,
    /// so it must not resolve.
    ///
    /// The cases whose value <em>fits</em> in a byte are the ones that matter. Adversarial review
    /// of #3510 found that a numeric-range check alone accepted <c>0x00000001</c> — a Module row
    /// token — as table <c>0x01</c>, TypeRef, because 1 fits in a byte. <c>0x02000015</c> was
    /// being rejected for overflowing, not for being a token, so it never covered the rule it was
    /// written for. Width is now checked textually: a table index is one byte, hence one or two
    /// hex digits, so <c>0x0002</c> is not a table index either.
    /// </summary>
    [Theory]
    [InlineData("Metadata: 0x99")]
    [InlineData("Metadata: 0x03")]
    [InlineData("Metadata: 0x02000015")]
    [InlineData("Metadata: 0x00000001")]
    [InlineData("Metadata: 0x02000002")]
    [InlineData("Metadata: 0x80000002")]
    [InlineData("Metadata: 0x0002")]
    [InlineData("Metadata: 0x002")]
    [InlineData("Metadata: 0x")]
    [InlineData("Metadata: 0xzz")]
    [InlineData("metadata: 0x99")]
    public async Task MetadataLens_UnprojectedHexTable_IsRejected(string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", selector, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains(selector, error, StringComparison.Ordinal);
        Assert.Contains("0x02 TypeDef", error, StringComparison.Ordinal);
        Assert.DoesNotContain("## ", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Hex must carry its <c>0x</c>. A bare <c>02</c> is a table <em>name</em> position, and
    /// inferring a radix would let one spelling mean two things; the rule matches
    /// <c>--heap</c>'s address rule. It fails as an unknown section name, not as a bad index.
    /// </summary>
    [Fact]
    public async Task MetadataLens_BareDigits_AreNotATableIndex()
    {
        var (exit, _, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Metadata: 02", "--tips", "q");

        Assert.NotEqual(0, exit);
    }

    /// <summary>
    /// The alias must be resolved before <em>every</em> reader of a selector, including the static
    /// <c>-D --schema</c> path that returns early without loading the assembly at all.
    ///
    /// Reported by adversarial review of #3510: the normalizer originally sat below that early
    /// return, so <c>-D "Metadata: 0x02" --schema</c> answered "not found" while the
    /// effective-discovery path — which runs later — resolved the same selector. The gate above
    /// missed it precisely because it supplied an input source and so took the later path.
    /// </summary>
    [Fact]
    public async Task MetadataLens_StaticSchemaDiscovery_ResolvesHexTables()
    {
        var (hexExit, hexOutput, hexError) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "Metadata: 0x02", "--schema", "--tsv", "--tips", "q");
        var (nameExit, nameOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "Metadata: TypeDef", "--schema", "--tsv", "--tips", "q");

        Assert.True(hexExit == 0, $"expected success, got {hexExit}: {hexError}");
        Assert.Equal(nameExit, hexExit);
        Assert.Equal(nameOutput, hexOutput);
    }

    /// <summary>
    /// The same early return is also taken when no input source is given at all, so the alias has
    /// to hold on a selector that is never matched against a real image.
    /// </summary>
    [Fact]
    public async Task MetadataLens_DiscoveryWithoutAnAssembly_ResolvesHexTables()
    {
        var (hexExit, hexOutput, hexError) = await RunAppAsync(
            "library", "-D", "Metadata: 0x02", "--tsv", "--tips", "q");
        var (nameExit, nameOutput, _) = await RunAppAsync(
            "library", "-D", "Metadata: TypeDef", "--tsv", "--tips", "q");

        Assert.True(hexExit == 0, $"expected success, got {hexExit}: {hexError}");
        Assert.Equal(nameExit, hexExit);
        Assert.Equal(nameOutput, hexOutput);
    }

    /// <summary>Section names from a <c>-D --tsv</c> listing, header row dropped.</summary>
    private static string[] DiscoveryNames(string output) => output
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.TrimEnd('\r').Split('\t')[0])
        .Where(n => !n.Equals("name", StringComparison.Ordinal))
        .ToArray();
}
