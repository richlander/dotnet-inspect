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
        Assert.DoesNotContain(
            "packages: read",
            JobHeader("test-windows"));
        Assert.Equal(
            1,
            CountOccurrences(
                Workflow,
                "DOTNET_INSPECT_PACKAGE_FIXTURE_TOKEN: ${{ github.token }}"));
        Assert.Contains(
            "-method '*Package_Manifest_RendersToolManifestRows*'",
            Workflow);
        Assert.Contains(
            "Package fixture test filter selected no tests.",
            Workflow);
        Assert.Contains(
            "Package fixture test skipped authenticated execution.",
            Workflow);
    }

    static string JobHeader(string jobName)
    {
        int jobStart = Workflow.IndexOf($"\n  {jobName}:\n", StringComparison.Ordinal);
        Assert.True(jobStart >= 0, $"CI workflow does not define the '{jobName}' job.");

        int stepsStart = Workflow.IndexOf("\n    steps:\n", jobStart, StringComparison.Ordinal);
        Assert.True(stepsStart > jobStart, $"CI job '{jobName}' does not define steps.");
        return Workflow[jobStart..stepsStart];
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
