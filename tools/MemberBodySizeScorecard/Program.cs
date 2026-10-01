using System.Globalization;

using ILInspector.AnalysisHarness;

const string Usage =
    "usage: analysis-harness <check|time> "
    + "[--rounds N] [--samples N] [--tsv <path>] "
    + "<System.Private.CoreLib.dll> <System.Text.Json.dll>";

if (args.Length == 0
    || args[0] is not ("check" or "time"))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

int rounds = 5;
int samples = 3;
string? tsvPath = null;
var paths = new List<string>();
for (int index = 1; index < args.Length; index++)
{
    string argument = args[index];
    if (argument is "--rounds" or "--samples" or "--tsv")
    {
        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine(
                $"{argument} needs a value. {Usage}");
            return 2;
        }
        string value = args[++index];
        if (argument == "--tsv")
        {
            tsvPath = value;
            continue;
        }
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsed)
            || parsed <= 0)
        {
            Console.Error.WriteLine(
                $"{argument} needs a positive integer. {Usage}");
            return 2;
        }
        if (argument == "--rounds")
            rounds = parsed;
        else
            samples = parsed;
        continue;
    }
    paths.Add(Path.GetFullPath(argument));
}

if (paths.Count == 0
    || paths.Any(static path => !File.Exists(path)))
{
    Console.Error.WriteLine(
        $"Every assembly path must exist. {Usage}");
    return 2;
}

IReadOnlyList<MemberBodySizeScorecardScenario> scenarios =
    MemberBodySizeScorecard.DefaultScenarios(paths);
if (args[0] == "check")
{
    MemberBodySizeScorecardCheck check =
        MemberBodySizeScorecard.Check(scenarios);
    Console.WriteLine(
        $"{check.Compared} answers compared; "
        + $"{check.Mismatches.Count} mismatches.");
    foreach (MemberBodySizeScorecardAnswerHash answer
        in check.AnswerHashes)
    {
        Console.WriteLine(
            $"{answer.Scenario}\t{answer.Closing}\t"
            + $"{answer.Hash}");
    }
    foreach (MemberBodySizeScorecardMismatch mismatch
        in check.Mismatches)
    {
        Console.WriteLine(
            $"{mismatch.Scenario}\t{mismatch.Closing}\t"
            + $"{mismatch.Column}\t{mismatch.Answer}\t"
            + $"oracle={mismatch.OracleAnswer}");
    }
    return check.Agrees ? 0 : 1;
}

MemberBodySizeScorecardResult result =
    MemberBodySizeScorecard.Measure(
        scenarios,
        new(
            Rounds: rounds,
            SamplesPerRound: samples));
if (tsvPath is not null)
{
    using StreamWriter tsv = File.CreateText(tsvPath);
    MemberBodySizeScorecard.WriteTsv(result, tsv);
}
Console.Write(MemberBodySizeScorecard.Report(result));
return result.Check.Agrees ? 0 : 1;
