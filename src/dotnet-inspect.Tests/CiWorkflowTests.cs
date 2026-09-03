namespace DotnetInspector.Tests;

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
        Assert.Contains("--report-xunit", fixtureStep);
        Assert.Contains(
            "--report-xunit-filename \"$results_name\"",
            fixtureStep);
        Assert.Contains(
            "--results-directory \"$results_dir\"",
            fixtureStep);
        Assert.Contains("continue-on-error: true", fixtureStep);
        Assert.Contains("id: package_fixture", fixtureStep);
        Assert.Contains(
            "if: steps.package_fixture.outcome == 'failure'",
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
            Assert.Contains($" -method '*{method}*'", step);
            Assert.Contains($"method=\\\"$method\\\"", step);
        }
        Assert.Contains("total=\"[1-9][0-9]*\"", step);
    }

    static string JobHeader(string jobName)
    {
        int jobStart = Workflow.IndexOf($"\n  {jobName}:\n", StringComparison.Ordinal);
        Assert.True(jobStart >= 0, $"CI workflow does not define the '{jobName}' job.");

        int stepsStart = Workflow.IndexOf("\n    steps:\n", jobStart, StringComparison.Ordinal);
        Assert.True(stepsStart > jobStart, $"CI job '{jobName}' does not define steps.");
        return Workflow[jobStart..stepsStart];
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
