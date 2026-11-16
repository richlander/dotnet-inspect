using System.Text.Json;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Package endpoints of <c>diff -S "Changed Types"</c>: each side compares its
/// one implementation assembly (finding-diff-any.md#adoption, step 9).
/// </summary>
public partial class CommandExecutionTests
{
    [Fact]
    public async Task Diff_ChangedTypes_Package_ApiChangedTypesMatchLibraryApiChanges()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@9.0.0..10.0.0",
            "-S", "Changed Types");
        var (plainExit, plainOutput, plainError) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@9.0.0..10.0.0", "--json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(0, plainExit);
        Assert.Empty(plainError);
        string[][] rows = DataRows(output);
        Assert.Contains(rows, row =>
            row[0] == "System.Runtime.InteropServices.JsonMarshal"
            && row[1] == "Changed" && row[2] == "Changed");
        Assert.Contains(rows, row =>
            row[0] == "System.Text.Json.JsonDocument"
            && row[1] == "Unchanged" && row[2] == "Changed");

        string[] apiChanged =
        [
            .. rows.Where(row => row[1] == "Changed")
                .Select(row => row[0])
                .Order(StringComparer.Ordinal),
        ];
        using JsonDocument plain = JsonDocument.Parse(plainOutput);
        string[] libraryApiChanges =
        [
            .. plain.RootElement.GetProperty("document").GetProperty("rows")
                .EnumerateArray()
                .Select(row => row.GetProperty("display").GetString()!)
                .Order(StringComparer.Ordinal),
        ];
        Assert.NotEmpty(apiChanged);
        Assert.Equal(libraryApiChanges, apiChanged);
    }

    [Fact]
    public async Task Diff_ChangedTypes_PackageWithReferenceAssemblies_ComparesImplementations()
    {
        // Unsafe 5.0.0 ships ref/ and lib/ for netstandard2.0; its reference
        // stubs carry nullable attributes its implementation does not.
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Runtime.CompilerServices.Unsafe@5.0.0..6.0.0",
            "--tfm", "netstandard2.0", "-S", "Changed Types");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(
            [
                "Microsoft.CodeAnalysis.EmbeddedAttribute",
                "System.Runtime.CompilerServices.NativeIntegerAttribute",
                "System.Runtime.CompilerServices.Unsafe",
            ],
            DataRows(output).Select(row => row[0]));
    }

    [Fact]
    public async Task Diff_ChangedTypes_PackageWithSeveralLibraries_IsRejected()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.ServiceModel.Primitives@4.10.0..6.0.0",
            "-S", "Changed Types");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("requires exactly one Library at each endpoint", error);
    }

    static string[][] DataRows(string tsv) =>
    [
        .. tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.TrimEnd('\r').Split('\t')),
    ];
}
