using System.Globalization;

using MetadataNameRangePilot;

if (!Options.TryParse(args, out Options? options, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var assets = new List<NameAsset>();
try
{
    foreach (string path in options!.Paths)
        assets.Add(NameAsset.Open(path));

    if (options.Command == PilotCommand.Check)
    {
        NameAsset.CheckIllFormedKnownAnswers();
        foreach (NameAsset asset in assets)
        {
            NameCheck check = asset.Check();
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"CHECK\t{asset.Name}\t"
                    + $"names={check.NameCount}\t"
                    + $"utf8-bytes={check.Utf8Bytes}\t"
                    + $"query-bytes={check.QueryBytes}\t"
                    + $"query-matches={check.QueryMatches}\t"
                    + $"display-hash={check.DisplayHash:X16}"));
        }
        return 0;
    }

    using TextWriter samples = options.TsvPath is null
        ? TextWriter.Null
        : File.CreateText(options.TsvPath);
    if (options.TsvPath is not null)
    {
        samples.WriteLine(
            "asset\tround\torder\tscenario\tstrategy\tcopies\t"
            + "names\tutf8_bytes\tquery_matches\telapsed_us\t"
            + "allocated_b\tretained_b\tchecksum");
    }

    foreach (NameAsset asset in assets)
    {
        int copies = options.Copies
            ?? Math.Clamp(
                (options.TargetNames + asset.NameCount - 1)
                    / asset.NameCount,
                1,
                256);
        PilotResult result = NamePilot.Measure(
            asset,
            options.Rounds,
            copies,
            samples);
        Console.Write(NamePilot.Report(result));
    }

    return 0;
}
finally
{
    foreach (NameAsset asset in assets)
        asset.Dispose();
}
