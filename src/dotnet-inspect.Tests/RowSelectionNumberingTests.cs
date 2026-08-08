using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins the numbering contract for <c>--row</c>: the ordinal addresses a row by
/// its position in the rendered section, not the position of that row
/// inside whatever list the projection happens to have built.
///
/// The two numbering systems only diverge when a projection skips rows, which is
/// exactly when the reader cannot compensate: a section that drops rows carrying
/// no value leaves gaps, so list position and rendered number differ by however
/// many rows were skipped before the target. Rows are constructed here with
/// deliberate gaps (2 and 5, as a source-location projection produces when rows
/// 1, 3, and 4 carry no value) because contiguous rows cannot tell a correct
/// implementation from a positional one.
/// </summary>
public class RowSelectionNumberingTests
{
    private static IReadOnlyList<ShapeProjectionRow> GappedRows() =>
    [
        new ShapeProjectionRow(2, "Source Locations", "alpha"),
        new ShapeProjectionRow(5, "Source Locations", "beta"),
    ];

    private static ShapeProjectionOptions Options(RowSelector? row) =>
        new(ShapeProjectionKind.Value, row, JsonOutput: false, Jsonl: false, JsonArray: false);

    [Fact]
    public async Task Row_SelectsDisplayedNumber_NotListPosition()
    {
        var (exit, output, error) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(ShapeProjectionOutput.Write(GappedRows(), Options(RowSelector.FromIndex(2)))));

        // Row 2 is displayed as row 2. Selecting it positionally would return
        // "beta", the row displayed as 5, and would do so silently.
        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("alpha", output.Trim());
    }

    [Fact]
    public async Task Row_AddressesDisplayedNumberBeyondListCount()
    {
        var (exit, output, error) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(ShapeProjectionOutput.Write(GappedRows(), Options(RowSelector.FromIndex(5)))));

        // Row 5 was rendered, so it is addressable, even though the list holds
        // only two entries and a positional bound check would reject it.
        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("beta", output.Trim());
    }

    [Fact]
    public async Task Row_ThatIsNotDisplayed_IsRejectedByNumber()
    {
        var (exit, output, error) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(ShapeProjectionOutput.Write(GappedRows(), Options(RowSelector.FromIndex(3)))));

        // Row 3 carries no value in this section, so it is absent from the
        // projection. The error has to name the rows that do exist rather than
        // quote a 1..N range that includes 3 and excludes 5.
        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("row 3", error);
        Assert.Contains("2, 5", error);
    }

    [Fact]
    public async Task First_And_Last_ResolveToDisplayedEndpoints()
    {
        var (firstExit, firstOutput, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(ShapeProjectionOutput.Write(GappedRows(), Options(RowSelector.First))));
        var (lastExit, lastOutput, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(ShapeProjectionOutput.Write(GappedRows(), Options(RowSelector.Last))));

        Assert.Equal(0, firstExit);
        Assert.Equal("alpha", firstOutput.Trim());
        Assert.Equal(0, lastExit);
        Assert.Equal("beta", lastOutput.Trim());
    }

    [Fact]
    public async Task SelectedRow_ReportsItsDisplayedNumberInJson()
    {
        var options = new ShapeProjectionOptions(
            ShapeProjectionKind.Value, RowSelector.FromIndex(5),
            JsonOutput: true, Jsonl: false, JsonArray: false);
        var (exit, output, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(ShapeProjectionOutput.Write(GappedRows(), options)));

        // The payload's row number and the number used to select it must agree,
        // so that a scripted round-trip through --row is stable.
        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(5, document.RootElement.GetProperty("row").GetInt32());
    }

    // --- the --print selection path -------------------------------------------
    //
    // These exercise ApiCommand.SelectPrintableRow directly. The end-to-end
    // --print tests need a package restore and a network fetch, and none of the
    // packages they use has a source-location row that lacks a URL, so the case
    // that motivated the whole change cannot be reached from there.

    private static List<(int Row, string? Label, string? Url)> RowsWithAGap() =>
    [
        (1, "first", null),
        (2, "second", "https://example.invalid/second.cs"),
        (3, "third", null),
    ];

    [Fact]
    public void PrintSelection_AddressesRowsThatCarryNoPayload()
    {
        var selected = ApiCommand.SelectPrintableRow(RowsWithAGap(), RowSelector.FromIndex(2), out var error);

        Assert.NotNull(selected);
        Assert.Equal(2, selected!.Value.Row);
        Assert.Empty(error);
    }

    [Fact]
    public void PrintSelection_ReportsRowWithNoPayloadInsteadOfSliding()
    {
        // Row 1 exists and was addressed correctly; it simply has nothing behind
        // it. Before this contract, numbering skipped it and --row 1 returned
        // row 2's document without saying so.
        var selected = ApiCommand.SelectPrintableRow(RowsWithAGap(), RowSelector.FromIndex(1), out var error);

        Assert.Null(selected);

        // The message is bare: the "Error: " prefix belongs to CommandError,
        // which is also where the message is contained (issue #3319).
        Assert.Equal(
            "row 1 has no printable document.", error);
    }

    [Fact]
    public void PrintSelection_LastMeansTheLastRenderedRow_NotTheLastPrintableOne()
    {
        // 'last' is an endpoint of the rendered section. Row 3 carries no
        // payload, so the honest answer is to say so rather than to fall back to
        // row 2, which the reader did not ask for.
        var selected = ApiCommand.SelectPrintableRow(RowsWithAGap(), RowSelector.Last, out var error);

        Assert.Null(selected);
        Assert.Equal(
            "row 3 has no printable document.", error);
    }

    [Fact]
    public void PrintSelection_RejectsRowNumbersThatWereNeverRendered()
    {
        var selected = ApiCommand.SelectPrintableRow(RowsWithAGap(), RowSelector.FromIndex(9), out var error);

        Assert.Null(selected);
        Assert.Equal(
            "row 9 is not in this section. Use --row 1 through 3, first, or last.", error);
    }

    [Fact]
    public void PrintSelection_RequiresRowWhenSectionHasMany()
    {
        var selected = ApiCommand.SelectPrintableRow(RowsWithAGap(), selector: null, out var error);

        Assert.Null(selected);
        Assert.Equal(
            "selected section has 3 rows; use --row N|first|last to choose one row.", error);
    }

    [Fact]
    public void PrintSelection_CountsEveryRenderedRow_NotJustPrintableOnes()
    {
        // The guidance error has to count what the section rendered. Counting only
        // printable rows would say "1 row" for a three-row section and then
        // print it without being asked.
        var selected = ApiCommand.SelectPrintableRow(RowsWithAGap(), selector: null, out var error);

        Assert.Null(selected);
        Assert.Contains("has 3 rows", error);
    }

    [Fact]
    public async Task Print_AcquiresOnlyTheRowItEmits()
    {
        // One --print authorizes one payload fetch. Documents can be expensive to acquire, so a
        // request that resolves to a single row must not read the rest of the section to find it.
        var reads = new List<int>();
        var rows = new List<PrintableRow>
        {
            new(1, "Docs", "alpha", "alpha.md", null),
            new(2, "Docs", "beta", "beta.md", null),
            new(3, "Docs", "gamma", "gamma.md", null)
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(() => Task.FromResult(
            PrintProjectionOutput.Write(
                rows,
                row => { reads.Add(row.Row); return row.Label; },
                Print(RowSelector.FromIndex(2)))));

        Assert.Equal(0, exit);
        Assert.Equal("beta", output);
        Assert.Equal([2], reads);
    }

    [Fact]
    public async Task Print_AcquiresNothingWhenTheRequestIsRefused()
    {
        // An ambiguous request is answered by guidance, not by a document, so it authorizes no
        // fetch at all. Reading first and refusing afterwards would pay for work never emitted.
        var reads = new List<int>();
        var rows = new List<PrintableRow>
        {
            new(1, "Docs", "alpha", "alpha.md", null),
            new(2, "Docs", "beta", "beta.md", null)
        };

        var (ambiguousExit, _, ambiguousError) = await ConsoleCapture.RunAsync(() => Task.FromResult(
            PrintProjectionOutput.Write(
                rows,
                row => { reads.Add(row.Row); return row.Label; },
                Print(null))));

        Assert.Equal(1, ambiguousExit);
        Assert.Contains("2 printable rows", ambiguousError, StringComparison.Ordinal);
        Assert.Empty(reads);

        var (missingExit, _, missingError) = await ConsoleCapture.RunAsync(() => Task.FromResult(
            PrintProjectionOutput.Write(
                rows,
                row => { reads.Add(row.Row); return row.Label; },
                Print(RowSelector.FromIndex(9)))));

        Assert.Equal(1, missingExit);
        Assert.Contains("row 9 is not in this section", missingError, StringComparison.Ordinal);
        Assert.Empty(reads);
    }

    private static PrintProjectionOptions Print(RowSelector? row) =>
        new(row, JsonOutput: false, Jsonl: false, JsonArray: false, Bare: false, OutputPath: null);

    [Fact]
    public void PrintSelection_ReportsEmptySection()
    {
        var selected = ApiCommand.SelectPrintableRow([], RowSelector.First, out var error);

        Assert.Null(selected);
        Assert.Equal(
            "selected section has no rows.", error);
    }
}
