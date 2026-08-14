namespace DotnetInspector.Tests;

/// <summary>
/// Gates the byte-identical Azure Artifacts mirror in the stable release
/// workflow.
/// </summary>
public sealed class AzureArtifactsMirrorWorkflowTests
{
    static readonly string Workflow = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        ".github",
        "workflows",
        "release.yml"));

    [Fact]
    public void BothDestinationsConsumeTheSameBuiltArtifacts()
    {
        string azureJob = Job("publish-azure");
        string nugetJob = Job("publish");

        Assert.DoesNotContain("dotnet pack ", azureJob);
        Assert.Equal(
            1,
            azureJob.Split('\n').Count(static line =>
                line.Trim() == "uses: actions/download-artifact@v8"));
        Assert.Equal(
            1,
            nugetJob.Split('\n').Count(static line =>
                line.Trim() == "uses: actions/download-artifact@v8"));
        Assert.Contains("pattern: package-*", azureJob);
        Assert.Contains("pattern: package-*", nugetJob);
        Assert.Contains("publish-azure", JobHeader("publish"));
    }

    [Fact]
    public void AzurePublicationUsesTheReleaseVersionAndRunsFirst()
    {
        string azureJob = Job("publish-azure");

        Assert.Contains(
            "https://pkgs.dev.azure.com/richlander/dotnet-inspect/_packaging/dotnet-inspect/nuget/v3/index.json",
            azureJob);
        Assert.Contains(
            "AZURE_DEVOPS_PAT: ${{ secrets.AZURE_DEVOPS_PAT }}",
            azureJob);
        Assert.Contains("- name: Validate reach packaging", azureJob);
        Assert.DoesNotContain("VersionSuffix", azureJob);
        Assert.DoesNotContain("VersionPrefix=", azureJob);

        int native = azureJob.IndexOf(
            "for package in packages/dotnet-inspect.win-*.nupkg",
            StringComparison.Ordinal);
        int any = azureJob.IndexOf(
            "push_package packages/dotnet-inspect.any.*.nupkg",
            StringComparison.Ordinal);
        int pointer = azureJob.IndexOf(
            "push_package packages/dotnet-inspect.[0-9]*.nupkg",
            StringComparison.Ordinal);

        Assert.True(native >= 0);
        Assert.True(native < any);
        Assert.True(any < pointer);
    }

    static string Job(string name)
    {
        int start = Workflow.IndexOf($"\n  {name}:\n", StringComparison.Ordinal);
        Assert.True(start >= 0);

        int search = start + 1;
        while (true)
        {
            int candidate = Workflow.IndexOf("\n  ", search, StringComparison.Ordinal);
            if (candidate < 0)
                return Workflow[start..];

            int afterIndent = candidate + 3;
            if (afterIndent < Workflow.Length && Workflow[afterIndent] != ' ')
                return Workflow[start..candidate];

            search = afterIndent;
        }
    }

    static string JobHeader(string name)
    {
        string job = Job(name);
        int steps = job.IndexOf("\n    steps:\n", StringComparison.Ordinal);
        Assert.True(steps >= 0);
        return job[..steps];
    }

    static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing dotnet-inspect.slnx.");
    }
}
