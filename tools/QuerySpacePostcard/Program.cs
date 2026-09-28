using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.PortableExecutable;
using DotnetInspector.PerformanceOracles;

// queryspace-postcard check <assembly>...
//   Checks that every column answers every closing as the NLinq oracle does.
// queryspace-postcard time [--rounds N] [--budget-ms N] [--tsv <path>] <assembly>...
//   Times every column with rotated rounds and prints the summary table.
if (args.Length < 2 || args[0] is not ("check" or "time"))
{
    Console.Error.WriteLine("usage: queryspace-postcard check|time [--rounds N] [--budget-ms N] [--tsv <path>] <assembly>...");
    return 2;
}

var timing = new PostcardTiming();
string? tsvPath = null;
var paths = new List<string>();
for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rounds":
            timing = timing with { Rounds = int.Parse(args[++i], CultureInfo.InvariantCulture) };
            break;
        case "--budget-ms":
            timing = timing with { BudgetMilliseconds = int.Parse(args[++i], CultureInfo.InvariantCulture) };
            break;
        case "--tsv":
            tsvPath = args[++i];
            break;
        default:
            paths.Add(args[i]);
            break;
    }
}

var shape = new PostcardShape();
PostcardColumn<PEReader, MethodTextRow> oracle = PublicMethods.NLinqColumn(shape);
PostcardColumn<PEReader, MethodTextRow>[] columns = [PublicMethods.LinqColumn(shape), oracle];

// Images are read into memory once, so timing excludes file I/O.
var readers = new List<PEReader>();
var assets = new List<PostcardAsset<PEReader>>();
foreach (string path in paths)
{
    var reader = new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
    readers.Add(reader);
    assets.Add(new(Path.GetFileNameWithoutExtension(path), reader));
}

try
{
    IReadOnlyList<PostcardMismatch> mismatches = Postcard.Check(assets, oracle, columns, PublicMethods.RowText);
    foreach (PostcardMismatch mismatch in mismatches)
        Console.WriteLine($"mismatch\t{mismatch.Asset}\t{mismatch.Closing}\t{mismatch.Column}\t{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    Console.WriteLine($"# answers: {assets.Count * Postcard.Closings.Count * (columns.Length - 1)} compared, {mismatches.Count} mismatches");
    if (mismatches.Count != 0)
        return 1;
    if (args[0] == "check")
        return 0;

    IReadOnlyList<PostcardCell> cells = Postcard.Measure(assets, columns, timing, progress => Console.Error.WriteLine(progress));
    if (tsvPath is not null)
    {
        using StreamWriter tsv = File.CreateText(tsvPath);
        Postcard.WriteTsv(cells, tsv);
    }

    Console.Write(Postcard.SummaryTable(cells, oracle.Name));
    return 0;
}
finally
{
    foreach (PEReader reader in readers)
        reader.Dispose();
}
