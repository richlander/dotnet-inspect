namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task LibraryQuery_DirectoryUsesConjunctiveReferenceTerms()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-query-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "A.dll"),
                "Alpha.Library",
                "System.Runtime",
                "Contoso.Dependency");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "B.dll"),
                "Beta.Library",
                "System.Runtime");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "C.dll"),
                "Gamma.Library",
                "Contoso.Dependency");

            var result = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=system.runtime",
                "--where",
                "references=CONTOSO.DEPENDENCY",
                "--format=table");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains("Alpha.Library", result.Output);
            Assert.Contains("System.Runtime", result.Output);
            Assert.Contains("Contoso.Dependency", result.Output);
            Assert.DoesNotContain("Beta.Library", result.Output);
            Assert.DoesNotContain("Gamma.Library", result.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryQuery_CandidateBoundPrecedesResultSelection()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-query-bound-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "A.dll"),
                "Alpha.Library",
                "System.Runtime");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "B.dll"),
                "Beta.Library",
                "System.Runtime");

            var incompleteCount = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=System.Runtime",
                "--take",
                "1",
                "--count");
            Assert.Equal(1, incompleteCount.Exit);
            Assert.Empty(incompleteCount.Output);
            Assert.Contains(
                "candidate limit reached",
                incompleteCount.Error,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "Cannot count Library Query rows",
                incompleteCount.Error);

            var closedHead = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=System.Runtime",
                "--take",
                "1",
                "-n",
                "1",
                "--count");
            Assert.Equal(0, closedHead.Exit);
            Assert.Equal("1" + Environment.NewLine, closedHead.Output);
            Assert.Contains(
                "candidate limit reached",
                closedHead.Error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryQuery_IncompleteReferencesRemainVisible()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-query-malformed-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteMalformedAssemblyReferenceNameAssembly(
                Path.Combine(tempDir, "Root.dll"));

            var result = await RunAppAsync(
                "library",
                "query",
                tempDir,
                "--where",
                "references=Missing.Reference");

            Assert.Equal(1, result.Exit);
            Assert.Contains("Query Summary", result.Output);
            Assert.Contains(
                "IncompleteReferences",
                result.Error);
            Assert.Contains(
                "cannot be treated as absent",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryQuery_DiscoveryRequiresNoPopulation()
    {
        var result = await RunAppAsync(
            "library",
            "query",
            "/missing/library-query",
            "-D",
            "--format=json");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Contains("\"name\":\"Libraries\"", result.Output);
        Assert.Contains("\"name\":\"Query Summary\"", result.Output);
    }

    [Fact]
    public async Task LibraryQuery_PlatformRuntimeUsesSameReferenceFacet()
    {
        var result = await RunAppAsync(
            "library",
            "query",
            "--platform",
            "runtime",
            "--where",
            "references=System.Runtime",
            "--take",
            "256",
            "-n",
            "1",
            "--format=json");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Contains("\"library\":", result.Output);
        Assert.Contains("\"System.Runtime\"", result.Output);
    }

    [Fact]
    public async Task LibraryQuery_RejectsParentInspectionOptions()
    {
        var result = await RunAppAsync(
            "library",
            "--package",
            "System.Text.Json",
            "query",
            ".");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--package is not available with library query",
            result.Error);
    }
}
