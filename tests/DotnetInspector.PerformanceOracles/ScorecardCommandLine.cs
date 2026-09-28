using System.Globalization;

namespace DotnetInspector.PerformanceOracles;

/// <summary>The scorecard runner's mode.</summary>
public enum ScorecardCommand
{
    Check,
    Time,
}

/// <summary>A parsed scorecard command line.</summary>
public sealed record ScorecardOptions(ScorecardCommand Command, ScorecardTiming Timing, string? TsvPath, IReadOnlyList<string> Assets);

/// <summary>
/// Parses <c>check|time [--rounds N] [--budget-ms N] [--tsv &lt;path&gt;] &lt;assembly&gt;...</c>.
/// A command with no assemblies is rejected: an answer check over nothing
/// compares nothing and must not read as agreement.
/// </summary>
public static class ScorecardCommandLine
{
    public const string Usage = "usage: check|time [--rounds N] [--budget-ms N] [--tsv <path>] <assembly>...";

    public static bool TryParse(IReadOnlyList<string> args, out ScorecardOptions? options, out string? error)
    {
        options = null;
        if (args.Count == 0 || args[0] is not ("check" or "time"))
        {
            error = Usage;
            return false;
        }

        var timing = new ScorecardTiming();
        string? tsvPath = null;
        var assets = new List<string>();
        for (int i = 1; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg is "--rounds" or "--budget-ms" or "--tsv")
            {
                if (i + 1 >= args.Count)
                {
                    error = $"{arg} needs a value. {Usage}";
                    return false;
                }

                string value = args[++i];
                if (arg == "--tsv")
                {
                    tsvPath = value;
                    continue;
                }

                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number <= 0)
                {
                    error = $"{arg} needs a positive integer, not '{value}'. {Usage}";
                    return false;
                }

                timing = arg == "--rounds" ? timing with { Rounds = number } : timing with { BudgetMilliseconds = number };
                continue;
            }

            assets.Add(arg);
        }

        if (assets.Count == 0)
        {
            error = $"no assemblies: a scorecard over nothing compares nothing. {Usage}";
            return false;
        }

        options = new(args[0] == "check" ? ScorecardCommand.Check : ScorecardCommand.Time, timing, tsvPath, assets);
        error = null;
        return true;
    }
}
