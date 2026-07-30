using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using Mdi;

namespace DotnetInspector.MetadataRendering.Tests;

/// <summary>
/// Tests for rendering an image overview and a single heap value, and for the
/// <c>mdi</c> flags that select them (issue #3341, gap 5).
///
/// The rendering claim under test is the same one the table renderer makes: a
/// limit is never silent. An overview lists only tables that carry rows, so the
/// count it omitted and any table it cannot dump must both surface as caveats —
/// inline in Markdown, and on the error writer for the machine formats, which
/// are pure row streams with nowhere to put prose.
/// </summary>
public class MetadataImageOverviewRendererTests
{
    static readonly string SelfPath = typeof(MetadataImageOverviewRendererTests).Assembly.Location;

    static MetadataImageOverview SelfOverview()
    {
        using var peReader = new PEReader(new MemoryStream(File.ReadAllBytes(SelfPath)));
        var overview = MetadataImageInspector.Describe(peReader);
        Assert.NotNull(overview);
        return overview;
    }

    static string Render(MetadataImageOverview overview, MetadataTableFormat format)
    {
        var output = new StringWriter();
        MetadataProjectionRenderer.Render(overview, output, format);
        return output.ToString();
    }

    // --- Overview rendering ------------------------------------------------

    [Fact]
    public void Markdown_RendersASectionForEachPart()
    {
        string markdown = Render(SelfOverview(), MetadataTableFormat.Markdown);

        Assert.Contains("## Image", markdown);
        Assert.Contains("## Heaps", markdown);
        Assert.Contains("## Tables", markdown);
        Assert.Contains("Metadata version", markdown);
    }

    [Fact]
    public void Markdown_NamesTheGuidHeapsAddressingSoAnAddressIsNotMisread()
    {
        string markdown = Render(SelfOverview(), MetadataTableFormat.Markdown);

        Assert.Contains("| Guid | ", markdown);
        Assert.Contains("index", markdown);
        Assert.Contains("byte offset", markdown);
    }

    [Fact]
    public void Markdown_CarriesTheOmittedTableCountInline()
    {
        var overview = SelfOverview();
        string markdown = Render(overview, MetadataTableFormat.Markdown);

        int empty = overview.Tables.Count(static table => table.RowCount == 0);
        Assert.True(empty > 0, "this assembly leaves no table empty, so the caveat cannot be exercised");
        Assert.Contains($"{empty} of {overview.Tables.Length} ECMA-335 tables carry no rows", markdown);
    }

    [Fact]
    public void Markdown_NamesTablesWithRowsTheProjectionCannotDump()
    {
        var overview = SelfOverview();
        string markdown = Render(overview, MetadataTableFormat.Markdown);

        var unprojected = overview.Tables
            .Where(static table => table.RowCount > 0 && !table.IsProjected)
            .Select(static table => table.Name)
            .ToList();

        Assert.NotEmpty(unprojected);
        foreach (string name in unprojected)
            Assert.Contains(name, markdown);
    }

    [Fact]
    public void Markdown_ListsEveryTableThatCarriesRows()
    {
        var overview = SelfOverview();
        string markdown = Render(overview, MetadataTableFormat.Markdown);

        var withRows = overview.Tables.Where(static table => table.RowCount > 0).ToList();
        Assert.NotEmpty(withRows);

        foreach (var table in withRows)
            Assert.Contains($"| {table.Name} | {table.RowCount} |", markdown);
    }

    [Fact]
    public void Markdown_OmitsEmptyTablesFromTheTableList()
    {
        var overview = SelfOverview();
        string markdown = Render(overview, MetadataTableFormat.Markdown);

        var empty = overview.Tables.First(static table => table.RowCount == 0);

        Assert.DoesNotContain($"| {empty.Name} | 0 |", markdown);
    }

    [Theory]
    [InlineData(MetadataTableFormat.Tsv)]
    [InlineData(MetadataTableFormat.Jsonl)]
    public void MachineFormats_KeepEveryRowSelfIdentifying(MetadataTableFormat format)
    {
        string rendered = Render(SelfOverview(), format);

        Assert.Contains("image", rendered);
        Assert.Contains("heap", rendered);
        Assert.Contains("table", rendered);

        if (format == MetadataTableFormat.Jsonl)
        {
            foreach (string line in rendered.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Assert.Contains("\"section\":", line);
        }
    }

    [Fact]
    public void Jsonl_EmitsOneObjectPerHeapAndPopulatedTable()
    {
        var overview = SelfOverview();
        string jsonl = Render(overview, MetadataTableFormat.Jsonl);

        var lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(overview.Heaps.Length, lines.Count(static line => line.Contains("\"section\":\"heap\"")));
        Assert.Equal(
            overview.Tables.Count(static table => table.RowCount > 0),
            lines.Count(static line => line.Contains("\"section\":\"table\"")));
    }

    [Fact]
    public void Caveats_AreEmptyOnlyWhenNothingWasLeftOut()
    {
        var complete = new MetadataImageOverview(
            "v4.0.30319",
            MetadataKind.Ecma335,
            IsAssembly: true,
            MetadataOffset: 1,
            MetadataSize: 2,
            [new MetadataHeapSummary(HeapKind.String, 4, MetadataHeapAddressing.ByteOffset)],
            [new MetadataTableSummary(TableIndex.TypeDef, "TypeDef", 3, IsProjected: true)],
            new MetadataImageHeaders(
                Machine.Amd64,
                Characteristics.Dll,
                Subsystem.WindowsCui,
                DllCharacteristics.DynamicBase,
                IsPE32Plus: true,
                new MetadataCorHeaderSummary(2, 5, CorFlags.ILOnly, 0)));

        Assert.Empty(MetadataProjectionRenderer.Caveats(complete));
    }

    [Fact]
    public void Markdown_ReportsAnAbsentCliHeaderRatherThanBlankFields()
    {
        var native = new MetadataImageOverview(
            "v4.0.30319",
            MetadataKind.Ecma335,
            IsAssembly: false,
            MetadataOffset: 1,
            MetadataSize: 2,
            [new MetadataHeapSummary(HeapKind.String, 4, MetadataHeapAddressing.ByteOffset)],
            [new MetadataTableSummary(TableIndex.TypeDef, "TypeDef", 3, IsProjected: true)],
            new MetadataImageHeaders(
                Machine.Amd64,
                Characteristics.Dll,
                Subsystem.WindowsCui,
                DllCharacteristics.DynamicBase,
                IsPE32Plus: true,
                Cor: null));

        string markdown = Render(native, MetadataTableFormat.Markdown);

        Assert.Contains("| CLI header | absent |", markdown);
        Assert.DoesNotContain("CLI flags", markdown);
    }

    [Theory]
    [InlineData(0, CorFlags.ILOnly, "none")]
    [InlineData(0x06000001, CorFlags.ILOnly, "token 0x06000001")]
    [InlineData(0x00001234, CorFlags.NativeEntryPoint, "native RVA 0x00001234")]
    public void Markdown_DistinguishesAManagedEntryPointFromANativeOne(int raw, CorFlags flags, string expected)
    {
        var overview = new MetadataImageOverview(
            "v4.0.30319",
            MetadataKind.Ecma335,
            IsAssembly: true,
            MetadataOffset: 1,
            MetadataSize: 2,
            [new MetadataHeapSummary(HeapKind.String, 4, MetadataHeapAddressing.ByteOffset)],
            [new MetadataTableSummary(TableIndex.TypeDef, "TypeDef", 3, IsProjected: true)],
            new MetadataImageHeaders(
                Machine.Amd64,
                Characteristics.Dll,
                Subsystem.WindowsCui,
                DllCharacteristics.DynamicBase,
                IsPE32Plus: true,
                new MetadataCorHeaderSummary(2, 5, flags, raw)));

        Assert.Contains($"| Entry point | {expected} |", Render(overview, MetadataTableFormat.Markdown));
    }

    [Fact]
    public void Render_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => MetadataProjectionRenderer.Render((MetadataImageOverview)null!, new StringWriter()));
        Assert.Throws<ArgumentNullException>(() => MetadataProjectionRenderer.Render(SelfOverview(), null!));
    }

    // --- Heap value rendering ----------------------------------------------

    static string RenderHeap(MetadataValue value, HeapKind heap, int address, MetadataTableFormat format)
    {
        var output = new StringWriter();
        MetadataProjectionRenderer.Render(value, heap, address, output, format);
        return output.ToString();
    }

    [Fact]
    public void HeapValue_MarkdownEchoesTheAddressItAnswered()
    {
        var value = new MetadataValue.HeapReference(HeapKind.String, 42, 5, "hello", "hello", Truncated: false);

        string markdown = RenderHeap(value, HeapKind.String, 42, MetadataTableFormat.Markdown);

        Assert.Contains("## #Strings heap at 42", markdown);
        Assert.Contains("| #Strings | 42 | 5 | no | hello |", markdown);
    }

    [Fact]
    public void HeapValue_MarksATruncatedPreviewSoItIsNotReadAsWhole()
    {
        var value = new MetadataValue.HeapReference(HeapKind.Blob, 7, 64, null, "0011", Truncated: true);

        string markdown = RenderHeap(value, HeapKind.Blob, 7, MetadataTableFormat.Markdown);

        Assert.Contains("| #Blob | 7 | 64 | yes |", markdown);
        Assert.Contains("0011\u2026", markdown);
    }

    [Fact]
    public void HeapValue_KeepsAMalformedReadVisible()
    {
        var value = new MetadataValue.Malformed("past the end");

        string markdown = RenderHeap(value, HeapKind.Guid, 99, MetadataTableFormat.Markdown);

        Assert.Contains("!malformed: past the end", markdown);
    }

    [Fact]
    public void HeapValue_RendersNilWithoutClaimingALength()
    {
        string tsv = RenderHeap(new MetadataValue.Nil(), HeapKind.String, 0, MetadataTableFormat.Tsv);

        Assert.Contains("nil", tsv);
        Assert.Contains("#Strings\t0\t\tno\tnil", tsv);
    }

    [Fact]
    public void HeapValue_JsonlIsSelfDescribing()
    {
        var value = new MetadataValue.HeapReference(HeapKind.UserString, 1, 3, "abc", "abc", Truncated: false);

        string jsonl = RenderHeap(value, HeapKind.UserString, 1, MetadataTableFormat.Jsonl);

        Assert.Contains("\"heap\":\"#US\"", jsonl);
        Assert.Contains("\"address\":\"1\"", jsonl);
        Assert.Contains("\"value\":\"abc\"", jsonl);
    }

    // --- mdi wiring --------------------------------------------------------

    [Fact]
    public void ExecuteOverview_RendersTheImage()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int code = MdiCommand.ExecuteOverview(SelfPath, MetadataTableFormat.Markdown, output, error);

        Assert.Equal(0, code);
        Assert.Contains("## Image", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void ExecuteOverview_MovesCaveatsToTheErrorWriterForMachineFormats()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int code = MdiCommand.ExecuteOverview(SelfPath, MetadataTableFormat.Jsonl, output, error);

        Assert.Equal(0, code);
        Assert.Contains("Note:", error.ToString());
        Assert.Contains("carry no rows", error.ToString());
        Assert.DoesNotContain("Note:", output.ToString());
    }

    [Fact]
    public void ExecuteOverview_MissingFile_ReturnsErrorAndReports()
    {
        var error = new StringWriter();

        int code = MdiCommand.ExecuteOverview(
            "does-not-exist.dll", MetadataTableFormat.Markdown, new StringWriter(), error);

        Assert.Equal(1, code);
        Assert.Contains("file not found", error.ToString());
    }

    [Fact]
    public void ExecuteOverview_NonManagedFile_ReportsFailureWithoutOutput()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "this is not a PE image");

            var output = new StringWriter();
            var error = new StringWriter();

            int code = MdiCommand.ExecuteOverview(path, MetadataTableFormat.Markdown, output, error);

            Assert.Equal(1, code);
            Assert.False(string.IsNullOrWhiteSpace(error.ToString()));
            Assert.Equal(string.Empty, output.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExecuteHeapValue_ReadsAGuidByIndex()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int code = MdiCommand.ExecuteHeapValue(
            SelfPath, HeapKind.Guid, 1, new MetadataProjectionOptions(), MetadataTableFormat.Markdown, output, error);

        Assert.Equal(0, code);
        Assert.Contains("## #GUID heap at 1", output.ToString());
        Assert.DoesNotContain("malformed", output.ToString());
    }

    [Fact]
    public void ExecuteHeapValue_ReportsAnUnreadableAddressWithoutFailingTheCommand()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int code = MdiCommand.ExecuteHeapValue(
            SelfPath, HeapKind.Guid, 9999, new MetadataProjectionOptions(), MetadataTableFormat.Markdown, output, error);

        // An unreadable address is an answer, not a command failure — but it must
        // never render as an empty success.
        Assert.Equal(0, code);
        Assert.Contains("!malformed", output.ToString());
        Assert.Contains("Note:", error.ToString());
    }

    [Fact]
    public void ExecuteHeapValue_RejectsANegativeAddress()
    {
        var error = new StringWriter();

        int code = MdiCommand.ExecuteHeapValue(
            SelfPath, HeapKind.String, -1, new MetadataProjectionOptions(), MetadataTableFormat.Markdown, new StringWriter(), error);

        Assert.Equal(1, code);
        Assert.Contains("must be zero or greater", error.ToString());
    }

    [Theory]
    [InlineData("String:1234", HeapKind.String, 1234)]
    [InlineData("guid:1", HeapKind.Guid, 1)]
    [InlineData(" UserString : 0 ", HeapKind.UserString, 0)]
    [InlineData("Blob:0", HeapKind.Blob, 0)]
    [InlineData("#Strings:1234", HeapKind.String, 1234)]
    [InlineData("#GUID:1", HeapKind.Guid, 1)]
    [InlineData("#US:0", HeapKind.UserString, 0)]
    [InlineData("#Blob:0", HeapKind.Blob, 0)]
    [InlineData("#Strings:0x1a4", HeapKind.String, 0x1a4)]
    [InlineData("#Strings:0X1A4", HeapKind.String, 0x1a4)]
    [InlineData("Strings:10", HeapKind.String, 10)]
    [InlineData("us:5", HeapKind.UserString, 5)]
    public void TryParseHeapLocation_AcceptsAHeapAndAddress(string spec, HeapKind heap, int address)
    {
        Assert.True(MetadataHeapCoordinate.TryParse(spec, out var parsedHeap, out int parsedAddress, out string? error));
        Assert.Equal(heap, parsedHeap);
        Assert.Equal(address, parsedAddress);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("String", "not a heap reference")]
    [InlineData("Nope:1", "unknown heap")]
    [InlineData("#Str:1", "unknown heap")]
    [InlineData("String:abc", "not a heap address")]
    [InlineData("String:-1", "not a heap address")]
    [InlineData("String:0x", "not a heap address")]
    [InlineData("String:", "not a heap address")]
    // Hex is opt-in, never inferred: a bare "1a4" would otherwise silently address a different
    // entry than the "0x1a4" a metadata dump printed.
    [InlineData("String:1a4", "not a heap address")]
    // NumberStyles.AllowHexSpecifier on a signed int *wraps* rather than overflows, so these
    // parsed successfully as -2147483648 and -1 and produced a malformed heap read that still
    // exited 0. An address that cannot exist is a parse error.
    [InlineData("String:0x80000000", "not a heap address")]
    [InlineData("String:0xFFFFFFFF", "not a heap address")]
    [InlineData("String:0x100000000", "not a heap address")]
    [InlineData("String:2147483648", "not a heap address")]
    // Reported by adversarial review of #3497: a last-colon split handed the stray colon to the
    // heap half and reported `unknown heap '#Strings:0x1a4'` -- blaming the half the caller got
    // right. No accepted heap spelling contains a colon, so the first one is the separator.
    [InlineData("#Strings:0x1a4:0x1b4", "not a heap address")]
    public void TryParseHeapLocation_NamesTheHalfThatIsWrong(string spec, string expected)
    {
        Assert.False(MetadataHeapCoordinate.TryParse(spec, out _, out _, out string? error));
        Assert.NotNull(error);
        Assert.Contains(expected, error);
    }

    /// <summary>
    /// The boundary the overflow rejection must not overshoot: <c>int.MaxValue</c> is still a
    /// representable address and stays accepted in both radixes.
    /// </summary>
    [Theory]
    [InlineData("String:0x7FFFFFFF")]
    [InlineData("String:2147483647")]
    public void TryParseHeapLocation_AcceptsTheLargestRepresentableAddress(string spec)
    {
        Assert.True(MetadataHeapCoordinate.TryParse(spec, out _, out int address, out string? error));
        Assert.Equal(int.MaxValue, address);
        Assert.Null(error);
    }

    [Fact]
    public void RootCommand_ExposesTheOverviewAndHeapOptions()
    {
        var root = MdiCommand.CreateRootCommand();

        var names = root.Options.SelectMany(option => new[] { option.Name }.Concat(option.Aliases)).ToList();

        Assert.Contains("--overview", names);
        Assert.Contains("-i", names);
        Assert.Contains("--heap", names);
    }

    [Fact]
    public void RootCommand_RejectsCombiningTheQueryModes()
    {
        var root = MdiCommand.CreateRootCommand();
        var error = new StringWriter();

        // Capturing the stream is the point of the test, so the stderr-ownership
        // rule (#3319) is suppressed here rather than switched off for the
        // project: mdi is a separate entry point outside the CLI's reference
        // closure, but the rest of this project should stay covered.
#pragma warning disable RS0030
        var original = Console.Error;
        try
        {
            Console.SetError(error);
            int code = root.Parse([SelfPath, "--overview", "--heap", "Guid:1"]).Invoke();

            Assert.Equal(1, code);
            Assert.Contains("cannot be combined", error.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
#pragma warning restore RS0030
    }
}
