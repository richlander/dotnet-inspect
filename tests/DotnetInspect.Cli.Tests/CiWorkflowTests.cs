namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Gates CI wiring whose source-level shape prevents required evidence from
/// silently selecting no tests.
/// </summary>
public class CiWorkflowTests
{
    static readonly string Workflow = File.ReadAllText(
        Path.Combine(FindRepoRoot(), ".github", "workflows", "ci.yml"));

    [Fact]
    public void CliFastShards_AreDisjointAndCollectivelyExhaustive()
    {
        AssertCliShard(
            "Run CLI tests A-C (fast)",
            "cli-a-c",
            "filter-class",
            'A',
            'C');
        AssertCliShard(
            "Run CLI tests D-I (fast)",
            "cli-d-i",
            "filter-class",
            'D',
            'I');
        AssertCliShard(
            "Run CLI Markout and Match tests (fast)",
            "cli-ma",
            "filter-class",
            ["Ma"]);
        AssertCliShard(
            "Run CLI Member tests (fast)",
            "cli-mem",
            "filter-class",
            ["Mem"]);
        AssertCliShard(
            "Run CLI tests Q-Z (fast)",
            "cli-q-z",
            "filter-class",
            'Q',
            'Z');
        AssertCliShard(
            "Run remaining CLI tests (fast)",
            "cli-rest",
            "filter-not-class",
            [
                "A", "B", "C", "D", "E", "F", "G", "H", "I",
                "Ma", "Mem",
                "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            ]);
    }

    [Fact]
    public void JsExportAsyncWireGate_RunsBothParityFormsAndCloseNegative()
    {
        string step = NamedStep("Run JSExport runtime-async wire gates");
        string[] methods =
        [
            "Build_ProducesEqualWireFactsAcrossAsyncLoweringsForDirectSerializerResult",
            "Build_ProducesEqualWireFactsAcrossAsyncLoweringsForSerializerStoredAcrossSuspension",
            "Build_RejectsConditionalSerializerStoreAcrossAsyncLowerings",
        ];

        foreach (string method in methods)
        {
            Assert.Contains($" --filter-method '*{method}*'", step);
            Assert.Contains($"method=\\\"$method\\\"", step);
        }
        Assert.Contains("total=\"[1-9][0-9]*\"", step);
        Assert.Contains("--report-xunit-xml", step);
        Assert.Contains(
            "--report-xunit-xml-filename \"$results_name\"",
            step);
        Assert.Contains(
            "--results-directory \"$results_dir\"",
            step);
    }

    static string NamedStep(string stepName)
    {
        string marker = $"\n      - name: {stepName}\n";
        int stepStart = Workflow.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(stepStart >= 0, $"Step '{stepName}' not found.");
        int nextStep = Workflow.IndexOf(
            "\n      - ",
            stepStart + marker.Length,
            StringComparison.Ordinal);
        return nextStep >= 0
            ? Workflow[stepStart..nextStep]
            : Workflow[stepStart..];
    }

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal))
            >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    static void AssertCliShard(
        string stepName,
        string shard,
        string filter,
        char first,
        char last)
        => AssertCliShard(
            stepName,
            shard,
            filter,
            Enumerable.Range(first, last - first + 1)
                .Select(value => ((char)value).ToString())
                .ToArray());

    static void AssertCliShard(
        string stepName,
        string shard,
        string filter,
        string[] prefixes)
    {
        string step = NamedStep(stepName);
        Assert.Contains($"if: matrix.shard == '{shard}'", step);
        Assert.Contains($"--{filter} ", step);
        Assert.Contains("--filter-not-trait \"Speed=Slow\"", step);
        foreach (string prefix in prefixes)
        {
            Assert.Contains(
                $"'DotnetInspect.Cli.Tests.{prefix}*'",
                step);
        }

        Assert.Equal(
            prefixes.Length,
            CountOccurrences(step, "'DotnetInspect.Cli.Tests."));
    }

    static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
