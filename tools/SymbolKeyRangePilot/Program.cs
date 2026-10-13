using System.Globalization;

using SymbolKeyRangePilot;

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
        foreach (NameAsset asset in assets)
        {
            KeyCheck check = asset.Check();
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"CHECK\t{asset.Name}\t"
                    + $"keys={check.KeyCount}\t"
                    + $"key-bytes={check.KeyBytes}\t"
                    + $"query-bytes={check.QueryBytes}\t"
                    + $"key-hash={check.KeyHash:X8}\t"
                    + $"query-matches={check.QueryMatches}"));
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
            + "keys\tkey_bytes\telapsed_us\tallocated_b\t"
            + "retained_b\tchecksum");
    }

    UserSearchValue? userText = options.Command == PilotCommand.Present
        ? new(options.UserText!)
        : null;
    foreach (NameAsset asset in assets)
    {
        int copies = options.Copies
            ?? Math.Clamp(
                (options.TargetKeys + asset.KeyCount - 1)
                    / asset.KeyCount,
                1,
                256);
        if (options.Command == PilotCommand.Measure)
        {
            PilotResult result = NamePilot.Measure(
                asset,
                options.Rounds,
                copies,
                samples);
            Console.Write(NamePilot.Report(result));
        }
        else
        {
            UserTextPilot.Validate(asset, userText!, copies);
            UserTextPilotResult result = UserTextPilot.Measure(
                asset,
                options.Rounds,
                copies,
                userText!,
                samples);
            Console.Write(UserTextPilot.Report(result));
        }
    }

    return 0;
}
finally
{
    foreach (NameAsset asset in assets)
        asset.Dispose();
}
