using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Cross-implementation oracle for <see cref="MetadataTableProjector"/>.
///
/// <para>
/// mdv is Roslyn's independent metadata viewer. It and this projection are two
/// unrelated readers of the same ECMA-335 image, so agreement is strong,
/// external evidence that the projection enumerates tables and decodes heaps
/// correctly. mdv is an optional external tool; every test here skips when it is
/// not installed, exactly like <c>ILDisassemblerComparisonTests</c>.
/// </para>
///
/// <para>
/// Harness boundary: these tests read mdv's output and the product projection
/// and compare them. They do not reconstruct, normalise, or substitute for any
/// product behaviour. The comparison is deliberately restricted to facts both
/// tools expose robustly (per-table physical row counts and a few scalar heap
/// decodes) so a formatting quirk in either tool cannot masquerade as a
/// projection defect.
/// </para>
///
/// <para>
/// Scope note: the installed mdv build does not emit a standalone
/// <c>MethodDef (0x06)</c> table — it folds methods into the TypeDef MethodList
/// ranges — so MethodDef is excluded from the row-count comparison. That is an
/// mdv limitation, not a projection gap; the projection's MethodDef table is
/// covered by <see cref="MetadataTableProjectionTests"/>. The
/// <see cref="MethodDef_IsProjected_ButOmittedByThisMdv"/> test pins this
/// asymmetry so a future mdv that starts emitting the table forces us to widen
/// the comparison.
/// </para>
/// </summary>
public class MdvOracleComparisonTests
{
    // The product assembly under test: a real, self-describing image rich enough
    // to exercise every supported table (hundreds of types, thousands of custom
    // attributes and params).
    static string TargetAssembly => typeof(MetadataTableProjector).Assembly.Location;

    // Tables this mdv build does not emit as standalone tables; see class remarks.
    static readonly string[] TablesMdvOmits = ["MethodDef"];

    // Run mdv at most once per test run and cache its output. Any failure to
    // locate or run mdv yields null, which turns every test into a skip.
    static readonly Lazy<string?> MdvOutput = new(RunMdvOnce);

    static readonly Regex RowLine = new(@"^\s*[0-9a-fA-F]+:\s", RegexOptions.Compiled);
    static readonly Regex TableHeader = new(@"^(\w+) \(0x[0-9a-fA-F]+\):\s*$", RegexOptions.Compiled);
    static readonly Regex QuotedName = new(@"'([^']*)'", RegexOptions.Compiled);
    static readonly Regex Guid = new(
        @"\{([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\}",
        RegexOptions.Compiled);

    // A hand-built fragment in mdv's exact layout, used to calibrate the parser
    // deterministically without mdv installed. It deliberately includes the
    // traps the parser must survive: a Debug Directory preamble with its own
    // guid={...} before any table, blank-line table separators, `===` rules,
    // space-aligned column-name rows, hex row ids (1, 2, a, 10), and data cells
    // that themselves contain 0x tokens and quoted strings. Module has 1 row;
    // TypeRef has 4.
    static readonly string[] SampleMdvLines =
    [
        "Debug Directory:",
        "  CodeView guid={53815ba4-16c4-4933-b051-cef6b66d6d76}, age=1",
        "",
        "MetadataVersion: v4.0.30319",
        "",
        "Module (0x00):",
        "=====================================",
        "   Gen  Name          Mvid",
        "=====================================",
        "1: 0    'Sample.dll'  {b8b45219-176b-49dc-9fbd-58559e8a925c} (#1)",
        "",
        "TypeRef (0x01):",
        "=====================================",
        "     Scope                     Name",
        "=====================================",
        "  1: 0x23000001 (AssemblyRef)  'Object' (#10a51)",
        "  2: 0x23000001 (AssemblyRef)  'Attribute' (#0020)",
        "  a: 0x23000001 (AssemblyRef)  'Console' (#0030)",
        " 10: 0x01000004 (TypeRef)      'Modes' (#0040)",
        "",
    ];

    static string SampleMdv => string.Join("\n", SampleMdvLines);

    [Fact]
    public void RowCounts_MatchMdv_ForSharedTables()
    {
        string mdv = SkipUnlessMdv();
        var mdvCounts = ParseMdvTableRowCounts(mdv);
        Assert.NotEmpty(mdvCounts);

        var projection = Project();

        int compared = 0;
        foreach (var table in projection.Tables)
        {
            if (TablesMdvOmits.Contains(table.Name))
                continue;

            // Only tables both tools expose participate; the projection and mdv
            // dump overlapping-but-different table sets.
            if (!mdvCounts.TryGetValue(table.Name, out int mdvCount))
                continue;

            Assert.Equal(mdvCount, table.RowCount);
            compared++;
        }

        // Guard against a vacuous pass: a parsing regression that found zero (or
        // too few) shared tables must fail loudly rather than silently agree.
        Assert.True(
            compared >= 5,
            $"Expected to compare at least 5 shared tables against mdv, compared {compared}.");
    }

    [Fact]
    public void MethodDef_IsProjected_ButOmittedByThisMdv()
    {
        string mdv = SkipUnlessMdv();
        var mdvCounts = ParseMdvTableRowCounts(mdv);

        var projection = Project();
        var methodDef = Assert.Single(projection.Tables, table => table.Name == "MethodDef");
        Assert.True(methodDef.RowCount > 0, "Expected the projection to enumerate MethodDef rows.");

        // Pin the asymmetry that justifies excluding MethodDef from the row-count
        // comparison. If a future mdv build emits the table, this fails and tells
        // us to fold MethodDef back into RowCounts_MatchMdv_ForSharedTables.
        Assert.DoesNotContain("MethodDef", mdvCounts.Keys);
    }

    [Fact]
    public void ModuleMvid_MatchesMdv()
    {
        string mdv = SkipUnlessMdv();
        string mdvRow = Assert.Single(MdvTableRows(mdv, "Module"));
        var guidMatch = Guid.Match(mdvRow);
        Assert.True(guidMatch.Success, $"Could not read an MVID from mdv's Module row: '{mdvRow}'.");

        var module = Assert.Single(Project().Tables, table => table.Name == "Module");
        var row = Assert.Single(module.Rows);
        var mvid = Assert.IsType<MetadataValue.HeapReference>(Cell(module, row, "Mvid"));
        Assert.Equal(HeapKind.Guid, mvid.Heap);

        // Two independent Guid-heap decodes of the same image must agree.
        Assert.Equal(guidMatch.Groups[1].Value, mvid.Text!.Value.ToString(), ignoreCase: true);
    }

    [Fact]
    public void AssemblyRefNames_MatchMdv()
    {
        string mdv = SkipUnlessMdv();
        var mdvNames = MdvTableRows(mdv, "AssemblyRef")
            .Select(row => QuotedName.Match(row))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(mdvNames);

        var assemblyRef = Assert.Single(Project().Tables, table => table.Name == "AssemblyRef");
        var names = assemblyRef.Rows
            .Select(row => (Cell(assemblyRef, row, "Name") as MetadataValue.HeapReference)?.Text?.ToString())
            .Where(text => text is not null)
            .Select(text => text!)
            .ToHashSet(StringComparer.Ordinal);

        // Multi-row String-heap decode: every AssemblyRef name must round-trip
        // identically through both readers.
        Assert.Equal(mdvNames, names);
    }

    // ---------------------------------------------------------------------
    // Calibration / mutation checks.
    //
    // A cross-implementation oracle is only trustworthy if its measurement
    // apparatus (the mdv text parser) both measures correctly AND fails when
    // the measured value diverges. These tests pin that the parser is neither
    // wrong nor a tautology. The synthetic-fixture tests need no mdv, so they
    // run everywhere including CI; the seeded-discrepancy test runs the real
    // comparison used above against perturbed real mdv output.
    // ---------------------------------------------------------------------

    [Fact]
    public void ParseRowCounts_CountsDataRows_IgnoringPreambleRulesAndHeaders()
    {
        var counts = ParseMdvTableRowCounts(SampleMdv);

        // Only the two table headers become keys; the Debug Directory and
        // MetadataVersion preamble lines do not.
        Assert.Equal(new[] { "Module", "TypeRef" }, counts.Keys.Order().ToArray());

        // Rule lines, the column-name row, and the preamble contribute no rows.
        Assert.Equal(1, counts["Module"]);
        Assert.Equal(4, counts["TypeRef"]);
    }

    [Fact]
    public void ParseRowCounts_DetectsASingleDroppedRow()
    {
        int baseline = ParseMdvTableRowCounts(SampleMdv)["TypeRef"];

        // Remove exactly one TypeRef data row from the fixture.
        string perturbed = SampleMdv.Replace(
            "\n  2: 0x23000001 (AssemblyRef)  'Attribute' (#0020)", string.Empty);
        int mutated = ParseMdvTableRowCounts(perturbed)["TypeRef"];

        // A one-row difference must move the measured count by exactly one, so
        // the RowCounts_MatchMdv_ForSharedTables equality check cannot silently
        // agree across a real enumeration discrepancy.
        Assert.Equal(baseline - 1, mutated);
    }

    [Fact]
    public void ModuleRowExtraction_ScopesToTable_ExcludingPreambleGuid()
    {
        string moduleRow = Assert.Single(MdvTableRows(SampleMdv, "Module"));
        var match = Guid.Match(moduleRow);

        Assert.True(match.Success);
        // The MVID from the Module row, not the earlier Debug Directory guid.
        Assert.Equal("b8b45219-176b-49dc-9fbd-58559e8a925c", match.Groups[1].Value);
        Assert.DoesNotContain("53815ba4", moduleRow);
    }

    [Fact]
    public void RowCountComparison_DetectsSeededDiscrepancy()
    {
        string mdv = SkipUnlessMdv();
        const string table = "TypeRef";

        var view = Assert.Single(Project().Tables, t => t.Name == table);
        var truthful = ParseMdvTableRowCounts(mdv);

        // Baseline: the real comparison agrees.
        Assert.Equal(view.RowCount, truthful[table]);

        // Seed a one-row discrepancy in the real mdv output; the same comparison
        // must now disagree by exactly one — proving a green result reflects true
        // agreement, not an insensitive check.
        var seeded = ParseMdvTableRowCounts(RemoveFirstRow(mdv, table));
        Assert.Equal(view.RowCount - 1, seeded[table]);
        Assert.NotEqual(view.RowCount, seeded[table]);
    }

    static MetadataTableProjection Project()
    {
        using var session = AssemblyInspectionSession.Open(TargetAssembly);
        return session.MetadataTables();
    }

    static int ColumnIndex(MetadataTableView table, string name)
    {
        for (int i = 0; i < table.Columns.Length; i++)
        {
            if (table.Columns[i].Name == name)
                return i;
        }

        throw new Xunit.Sdk.XunitException($"Column '{name}' not found in table '{table.Name}'.");
    }

    static MetadataValue Cell(MetadataTableView table, MetadataRow row, string column)
        => row.Cells[ColumnIndex(table, column)];

    static string SkipUnlessMdv()
    {
        Assert.SkipUnless(
            MdvOutput.Value is not null,
            "mdv (Roslyn metadata viewer) is not installed or did not run; " +
            "install it with `dotnet tool install --global Microsoft.Metadata.Visualizer`.");
        return MdvOutput.Value!;
    }

    /// <summary>
    /// Per-table physical row counts, keyed by mdv's table name. A table is a
    /// <c>Name (0xNN):</c> header; its rows are the <c>&lt;hexRid&gt;: …</c>
    /// lines up to the next blank line. Rule lines and the column-name row do
    /// not match <see cref="RowLine"/>, so they are not counted.
    /// </summary>
    static Dictionary<string, int> ParseMdvTableRowCounts(string stdout)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        string? current = null;

        foreach (var raw in stdout.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            var header = TableHeader.Match(line);
            if (header.Success)
            {
                current = header.Groups[1].Value;
                counts[current] = 0;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                current = null;
                continue;
            }

            if (current is not null && RowLine.IsMatch(line))
                counts[current]++;
        }

        return counts;
    }

    /// <summary>The raw row lines of one named mdv table (empty if absent).</summary>
    static List<string> MdvTableRows(string stdout, string tableName)
    {
        var rows = new List<string>();
        var header = new Regex(@"^" + Regex.Escape(tableName) + @" \(0x[0-9a-fA-F]+\):\s*$");
        var lines = stdout.Split('\n');

        int i = 0;
        for (; i < lines.Length; i++)
        {
            if (header.IsMatch(lines[i].TrimEnd('\r')))
                break;
        }

        for (i++; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                break;
            if (RowLine.IsMatch(line))
                rows.Add(line);
        }

        return rows;
    }

    /// <summary>
    /// Returns <paramref name="stdout"/> with the first data row of the named
    /// table removed — a minimal, realistic perturbation for the seeded
    /// discrepancy test. Throws if the table has no row to remove.
    /// </summary>
    static string RemoveFirstRow(string stdout, string tableName)
    {
        var header = new Regex(@"^" + Regex.Escape(tableName) + @" \(0x[0-9a-fA-F]+\):\s*$");
        var lines = stdout.Split('\n');

        int i = 0;
        for (; i < lines.Length; i++)
        {
            if (header.IsMatch(lines[i].TrimEnd('\r')))
                break;
        }

        for (i++; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                break;
            if (RowLine.IsMatch(line))
            {
                var kept = new List<string>(lines);
                kept.RemoveAt(i);
                return string.Join('\n', kept);
            }
        }

        throw new Xunit.Sdk.XunitException($"No data row to remove in table '{tableName}'.");
    }

    static string? RunMdvOnce()
    {
        string? mdv = FindMdv();
        if (mdv is null)
            return null;

        try
        {
            var psi = new ProcessStartInfo(mdv)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(TargetAssembly);
            psi.ArgumentList.Add("/md+");
            psi.ArgumentList.Add("/il-");
            psi.ArgumentList.Add("/peHeaders-");
            psi.ArgumentList.Add("/stats-");
            psi.ArgumentList.Add("/assemblyRefs-");

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }

            stderr.GetAwaiter().GetResult();
            string output = stdout.GetAwaiter().GetResult();
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    static string? FindMdv()
    {
        string exe = OperatingSystem.IsWindows() ? "mdv.exe" : "mdv";

        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar is not null)
        {
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;

                string candidate;
                try { candidate = Path.Combine(dir, exe); }
                catch (ArgumentException) { continue; }

                if (File.Exists(candidate))
                    return candidate;
            }
        }

        string tools = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet", "tools", exe);
        return File.Exists(tools) ? tools : null;
    }
}
