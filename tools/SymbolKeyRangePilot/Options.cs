using System.Collections.Immutable;
using System.Globalization;

namespace SymbolKeyRangePilot;

internal enum PilotCommand
{
    Check,
    Measure,
    Present,
}

internal sealed record Options(
    PilotCommand Command,
    int Rounds,
    int? Copies,
    int TargetKeys,
    string? TsvPath,
    string? UserText,
    ImmutableArray<string> Paths)
{
    const string Usage =
        "usage: check <assembly>... | "
        + "measure [--rounds N] [--copies N | --target-keys N] "
        + "[--tsv path] <assembly>... | "
        + "present --user-text text [--rounds N] "
        + "[--copies N | --target-keys N] [--tsv path] <assembly>...";

    internal static bool TryParse(
        string[] args,
        out Options? options,
        out string? error)
    {
        options = null;
        error = null;
        if (args.Length == 0
            || args[0] is not ("check" or "measure" or "present"))
        {
            error = Usage;
            return false;
        }

        PilotCommand command = args[0] switch
        {
            "check" => PilotCommand.Check,
            "measure" => PilotCommand.Measure,
            _ => PilotCommand.Present,
        };
        int rounds = 30;
        int? copies = null;
        int targetKeys = 50_000;
        bool targetKeysSpecified = false;
        string? tsvPath = null;
        string? userText = null;
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
                case "--target-keys":
                    if (!TryReadPositive(
                            args,
                            ref index,
                            out targetKeys))
                    {
                        error =
                            "--target-keys requires a positive integer.";
                        return false;
                    }
                    targetKeysSpecified = true;
                    break;
                case "--tsv" when index + 1 < args.Length:
                    tsvPath = args[++index];
                    break;
                case "--user-text" when index + 1 < args.Length:
                    userText = args[++index];
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
                || targetKeys != 50_000
                || tsvPath is not null
                || userText is not null))
        {
            error =
                "Measurement options are valid only for measure or present.";
            return false;
        }
        if (command == PilotCommand.Measure && userText is not null)
        {
            error = "--user-text is valid only for present.";
            return false;
        }
        if (command == PilotCommand.Present && userText is null)
        {
            error = "present requires --user-text.";
            return false;
        }
        if (copies is not null && targetKeysSpecified)
        {
            error =
                "--copies and --target-keys cannot be used together.";
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
            targetKeys,
            tsvPath,
            userText,
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
