using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.PortableExecutable;
using DotnetInspector.PerformanceOracles;

// queryspace-postcard check <assembly>...
//   Checks that every column answers every closing as the NLinq oracle does.
// queryspace-postcard time [--rounds N] [--budget-ms N] [--tsv <path>] <assembly>...
//   Times every column with rotated rounds and prints the summary table.
if (!PostcardCommandLine.TryParse(args, out PostcardOptions? options, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new PostcardShape();
PostcardColumn<PEReader, MethodTextRow> oracle = PublicMethods.NLinqColumn(shape);
PostcardColumn<PEReader, MethodTextRow>[] columns = [PublicMethods.LinqColumn(shape), oracle];

// Images are read into memory once, so timing excludes file I/O.
var readers = new List<PEReader>();
var assets = new List<PostcardAsset<PEReader>>();
foreach (string path in options!.Assets)
{
    var reader = new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
    readers.Add(reader);
    assets.Add(new(Path.GetFileNameWithoutExtension(path), reader));
}

try
{
    PostcardCheck check = Postcard.Check(assets, oracle, columns, PublicMethods.RowText);
    foreach (PostcardMismatch mismatch in check.Mismatches)
        Console.WriteLine($"mismatch\t{mismatch.Asset}\t{shape.Label(mismatch.Closing)}\t{mismatch.Column}\t{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    Console.WriteLine($"# answers: {check.Compared} compared, {check.Mismatches.Count} mismatches, {check.WindowFailures.Count} strict-window failures");
    if (!check.Agrees)
        return 1;

    // Every column agreed, so each listed window failed in every column; it is
    // an outcome, never timed or scored.
    foreach (string asset in check.WindowFailures)
        Console.WriteLine($"window-failed\t{asset}\t{shape.Label(PostcardClosing.Window)}\tevery column fails: the window does not exist");
    if (options.Command == PostcardCommand.Check)
        return 0;

    IReadOnlyList<PostcardCell> cells = Postcard.Measure(assets, columns, options.Timing, progress => Console.Error.WriteLine(progress));
    if (options.TsvPath is { } tsvPath)
    {
        using StreamWriter tsv = File.CreateText(tsvPath);
        Postcard.WriteTsv(cells, tsv);
    }

    Console.Write(Postcard.Report(cells, oracle.Name, shape));
    return 0;
}
finally
{
    foreach (PEReader reader in readers)
        reader.Dispose();
}
