namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Gates the CI runner split between the high-frequency GA lanes and
/// path-gated Ubuntu preview coverage.
/// </summary>
public class CiWorkflowTests
{
    static readonly string Workflow = File.ReadAllText(
        Path.Combine(FindRepoRoot(), ".github", "workflows", "ci.yml"));

    [Fact]
    public void PrimaryLinuxJobs_UseGeneralAvailabilityRunner()
    {
        Assert.Contains("runs-on: ubuntu-24.04", JobHeader("changes"));
        Assert.Contains("- os: ubuntu-24.04", JobHeader("test"));
        Assert.Contains("runs-on: ubuntu-24.04", JobHeader("ci-required"));
    }

    [Fact]
    public void PathGatedJobs_RetainUbuntu2604Coverage()
    {
        Assert.Contains("runs-on: ubuntu-26.04", JobHeader("markdownlint"));
        Assert.Contains("runs-on: ubuntu-26.04", JobHeader("decompiler-gates"));
        Assert.Contains("runs-on: ubuntu-26.04", JobHeader("pack"));
    }

    [Fact]
    public void PrimaryLinuxTestJob_DefinesTheApprovedParallelShards()
    {
        string testHeader = JobHeader("test");
        string[] shards =
        [
            "cli-a-c",
            "cli-d-i",
            "cli-ma",
            "cli-mem",
            "cli-q-z",
            "cli-rest",
            "contracts",
            "analysis",
            "host-policy",
        ];

        Assert.Equal(shards.Length, CountOccurrences(testHeader, "shard: "));
        foreach (string shard in shards)
            Assert.Contains($"shard: {shard}", testHeader);
    }

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
    public void DecompilerFastSuites_RunInTheParallelPathGatedJob()
    {
        string job = Job("decompiler-gates");

        Assert.Contains("- name: Run decompiler unit tests (fast)", job);
        Assert.Contains("--no-build -- --gate fast", job);
        Assert.Contains("- name: Run DecompilerHarness tests", job);
        Assert.Contains(
            "dotnet run --project tests/DecompilerHarness.Tests -c Release --no-build",
            job);
        Assert.DoesNotContain(
            "- name: Run decompiler unit tests (fast)",
            Job("test"));
    }

    [Fact]
    public void HostedPackageFixture_UsesOneStepScopedReadToken()
    {
        string testHeader = JobHeader("test");

        Assert.Contains("contents: read", testHeader);
        Assert.Contains("packages: read", testHeader);
        Assert.Equal(
            1,
            CountOccurrences(Workflow, "packages: read"));

        string fixtureStep = NamedStep(
            "Run GitHub Packages fixture test");
        Assert.Contains(
            "DOTNET_INSPECT_PACKAGE_FIXTURE_USER: ${{ github.actor }}",
            fixtureStep);
        Assert.Contains(
            "DOTNET_INSPECT_PACKAGE_FIXTURE_TOKEN: ${{ github.token }}",
            fixtureStep);
        Assert.DoesNotContain(
            "DOTNET_INSPECT_PACKAGE_FIXTURE_TOKEN",
            testHeader);
        Assert.Equal(
            1,
            CountOccurrences(
                Workflow,
                "DOTNET_INSPECT_PACKAGE_FIXTURE_TOKEN: ${{ github.token }}"));
        Assert.Contains(
            "--filter-method '*Package_Manifest_RendersToolManifestRows*'",
            fixtureStep);
        Assert.Contains(
            "Package fixture test skipped authenticated execution.",
            fixtureStep);
        Assert.Contains(
            "grep -Eq '<assembly[^>]+skipped=\"0\"'",
            fixtureStep);
        Assert.Contains("--report-xunit-xml", fixtureStep);
        Assert.Contains(
            "--report-xunit-xml-filename \"$results_name\"",
            fixtureStep);
        Assert.Contains(
            "--results-directory \"$results_dir\"",
            fixtureStep);
        Assert.Contains("continue-on-error: true", fixtureStep);
        Assert.Contains("id: package_fixture", fixtureStep);
        Assert.Contains(
            "if: matrix.shard == 'host-policy' && steps.package_fixture.outcome == 'failure'",
            NamedStep("Check GitHub Packages fixture result"));
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

    static string JobHeader(string jobName)
    {
        int jobStart = Workflow.IndexOf($"\n  {jobName}:\n", StringComparison.Ordinal);
        Assert.True(jobStart >= 0, $"CI workflow does not define the '{jobName}' job.");

        int stepsStart = Workflow.IndexOf("\n    steps:\n", jobStart, StringComparison.Ordinal);
        Assert.True(stepsStart > jobStart, $"CI job '{jobName}' does not define steps.");
        return Workflow[jobStart..stepsStart];
    }

    static string Job(string jobName)
    {
        int jobStart = Workflow.IndexOf(
            $"\n  {jobName}:\n",
            StringComparison.Ordinal);
        Assert.True(
            jobStart >= 0,
            $"CI workflow does not define the '{jobName}' job.");

        int nextJob = Workflow.IndexOf(
            "\n  ",
            jobStart + $"\n  {jobName}:\n".Length,
            StringComparison.Ordinal);
        while (nextJob >= 0
            && (nextJob + 3 >= Workflow.Length
                || char.IsWhiteSpace(Workflow[nextJob + 3])))
        {
            nextJob = Workflow.IndexOf(
                "\n  ",
                nextJob + 3,
                StringComparison.Ordinal);
        }

        return nextJob >= 0
            ? Workflow[jobStart..nextJob]
            : Workflow[jobStart..];
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
