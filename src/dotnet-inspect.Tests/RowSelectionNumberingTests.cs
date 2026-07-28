using System.Text.Json;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins the numbering contract for <c>--row</c>: the ordinal addresses the row
/// number the reader can see in the rendered table, not the position of that row
/// inside whatever list the projection happens to have built.
///
/// The two numbering systems only diverge when a projection skips rows, which is
/// exactly when the reader cannot compensate: a section that drops rows carrying
/// no value leaves gaps, so list position and displayed number differ by however
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

        // Row 5 is on screen, so it is addressable, even though the list holds
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
}
