using System.Collections.Immutable;
using System.Globalization;

namespace MetadataNameRangePilot;

internal enum PilotCommand
{
    Check,
    Measure,
}

internal sealed record Options(
    PilotCommand Command,
    int Rounds,
    int? Copies,
    int TargetNames,
    string? TsvPath,
    ImmutableArray<string> Paths)
{
    const string Usage =
        "usage: check <assembly>... | measure [--rounds N] "
        + "[--copies N | --target-names N] [--tsv path] <assembly>...";

    internal static bool TryParse(
        string[] args,
        out Options? options,
        out string? error)
    {
        options = null;
        error = null;
        if (args.Length == 0
            || args[0] is not ("check" or "measure"))
        {
            error = Usage;
            return false;
        }

        PilotCommand command = args[0] == "check"
            ? PilotCommand.Check
            : PilotCommand.Measure;
        int rounds = 30;
        int? copies = null;
        int targetNames = 50_000;
        bool targetNamesSpecified = false;
        string? tsvPath = null;
        var paths = ImmutableArray.CreateBuilder<string>();
        for (int index = 1; index < args.Length; index++)
        {
            string arg = args[index];
            switch (arg)
            {
                case "--rounds":
                    if (!TryReadPositive(args, ref index, out rounds)
                        || rounds < 2)
                    {
                        error =
                            "--rounds requires an integer of at least 2.";
                        return false;
                    }
                    break;
                case "--copies":
                    if (!TryReadPositive(
                            args,
                            ref index,
                            out int parsedCopies))
                    {
                        error = "--copies requires a positive integer.";
                        return false;
                    }
                    copies = parsedCopies;
                    break;
                case "--target-names":
                    if (!TryReadPositive(
                            args,
                            ref index,
                            out targetNames))
                    {
                        error =
                            "--target-names requires a positive integer.";
                        return false;
                    }
                    targetNamesSpecified = true;
                    break;
                case "--tsv" when index + 1 < args.Length:
                    tsvPath = args[++index];
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"Unknown option '{arg}'.";
                        return false;
                    }
                    paths.Add(Path.GetFullPath(arg));
                    break;
            }
        }

        if (command == PilotCommand.Check
            && (rounds != 30
                || copies is not null
                || targetNames != 50_000
                || tsvPath is not null))
        {
            error = "Measurement options are valid only for measure.";
            return false;
        }
        if (copies is not null && targetNamesSpecified)
        {
            error =
                "--copies and --target-names cannot be used together.";
            return false;
        }
        if (paths.Count == 0)
        {
            error = $"At least one assembly is required. {Usage}";
            return false;
        }
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                error = $"Assembly does not exist: {path}";
                return false;
            }
        }

        options = new(
            command,
            rounds,
            copies,
            targetNames,
            tsvPath,
            paths.ToImmutable());
        return true;
    }

    static bool TryReadPositive(
        string[] args,
        ref int index,
        out int value)
    {
        value = 0;
        return index + 1 < args.Length
            && int.TryParse(
                args[++index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value)
            && value > 0;
    }
}
