namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Theory]
    [InlineData("System.Math")]
    [InlineData("Math")]
    public async Task TypeHierarchy_Mermaid_UsesStandaloneSharedPresentation(
        string target)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", target, "--platform", "System.Private.CoreLib",
            "--mermaid");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("graph TD\n  n0[\"static class System.Math\"]", output);
        Assert.Contains("n0 --> n1", output);
        Assert.Contains("Methods (", output);
        Assert.Contains("overloads)", output);
        Assert.DoesNotContain("```", output);
    }

    [Fact]
    public async Task TypeHierarchy_DefaultAndExplicitTree_UseSameCompactProfile()
    {
        var normal = await RunAppAsync(
            "type", "System.Math", "--platform", "System.Private.CoreLib");
        var explicitTree = await RunAppAsync(
            "type", "System.Math", "--platform", "System.Private.CoreLib",
            "--tree");

        Assert.Equal(0, normal.Exit);
        Assert.Equal(0, explicitTree.Exit);
        Assert.Equal(normal.Output, explicitTree.Output);
        Assert.StartsWith("static class System.Math\n", normal.Output);
        Assert.Contains("Methods (", normal.Output);
        Assert.Contains("overloads)", normal.Output);
    }

    [Fact]
    public async Task TypeHierarchy_Mermaid_AllResolvesNonPublicExactType()
    {
        string target = typeof(InternalTopLevelSurfaceFixture).FullName!;
        var hidden = await RunAppAsync(
            "type", target, "--library", TestAssemblyPath, "--mermaid");
        var visible = await RunAppAsync(
            "type", target, "--library", TestAssemblyPath, "--all",
            "--mermaid");

        Assert.Equal(1, hidden.Exit);
        Assert.Empty(hidden.Output);
        Assert.Contains("was not found", hidden.Error);
        Assert.Equal(0, visible.Exit);
        Assert.Empty(visible.Error);
        Assert.StartsWith(
            $"graph TD\n  n0[\"class {target}\"]",
            visible.Output);
    }

    [Theory]
    [InlineData("System.Text", "--mermaid")]
    [InlineData("System.Private.CoreLib.DefinitelyNotAType9620", "--mermaid")]
    [InlineData("System.Text", "--platform", "System.Private.CoreLib", "--mermaid")]
    [InlineData("System.Text.Json.Serialization", "--platform", "System.Text.Json", "--mermaid")]
    [InlineData("System.Math", "--mermaid", "--member", "Abs")]
    [InlineData("System.Math", "--mermaid", "--schema", "--discover")]
    [InlineData("--platform", "System.Private.CoreLib", "--mermaid")]
    [InlineData("System.Math*", "--platform", "System.Private.CoreLib", "--mermaid")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "--match")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "--json")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "--markdown")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "--table")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "--tree")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "-v:n")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "--rows", "1..2")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "-n", "2")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "-S", "Methods")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "--details")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "-Q")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "--columns", "Name")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "--fields", "Name")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "--row", "1")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "--top", "0")]
    [InlineData("System.Math", "--platform", "System.Private.CoreLib", "--mermaid", "-t", "System.*")]
    public async Task TypeHierarchy_Mermaid_RejectsOtherRoutesBeforeOutput(
        params string[] args)
    {
        var (exit, output, error) = await RunAppAsync(
            ["type", .. args]);

        Assert.NotEqual(0, exit);
        Assert.Empty(output);
        Assert.Contains("--mermaid", error);
        Assert.DoesNotContain("best-effort platform prefix", error);
    }

    [Theory]
    [InlineData("--top", "0")]
    [InlineData("--tree")]
    public async Task TypeHierarchy_TargetFreeMermaidCompetingRequest_UsesStandaloneDiagnostic(
        params string[] competingArgs)
    {
        var (exit, output, error) = await RunAppAsync(
            ["type", "--mermaid", .. competingArgs]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--mermaid requires a standalone exact Type",
            error);
    }

    [Fact]
    public async Task TypeHierarchy_ExplicitZeroTopKeepsOrdinaryTypeRoute()
    {
        var compact = await RunAppAsync(
            "type", "System.Math",
            "--platform", "System.Private.CoreLib");
        var ordinary = await RunAppAsync(
            "type", "System.Math",
            "--platform", "System.Private.CoreLib",
            "--top", "0");

        Assert.Equal(0, ordinary.Exit);
        Assert.Empty(ordinary.Error);
        Assert.NotEqual(compact.Output, ordinary.Output);
        Assert.Contains("Inherits", ordinary.Output);
        Assert.Contains("double Acos(double d)", ordinary.Output);
    }

    [Fact]
    public async Task TypeHierarchy_Mermaid_RejectsManagedNetmodule_WithoutChangingOrdinaryType()
    {
        string directory = Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            $"type-hierarchy-netmodule-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Widget.dll");
        try
        {
            WriteNetmodule(path);
            var mermaid = await RunAppAsync(
                "type", "N.Widget", "--library", path, "--mermaid");
            var normal = await RunAppAsync(
                "type", "N.Widget", "--library", path);

            Assert.Equal(1, mermaid.Exit);
            Assert.Empty(mermaid.Output);
            Assert.Contains("descriptor", mermaid.Error);
            Assert.Equal(0, normal.Exit);
            Assert.Contains("N.Widget", normal.Output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeHierarchy_Mermaid_IgnoresXmlDocumentationSidecar()
    {
        string directory = Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            $"type-hierarchy-docs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Examples.dll");
        try
        {
            File.Copy(TestAssemblyPath, path);
            File.WriteAllText(
                Path.ChangeExtension(path, ".xml"),
                "<doc><members><member name=\"T:DotnetInspect.Cli.Tests.SampleClassForTesting\"><summary>Local documentation</summary></member></members></doc>");

            var (exit, output, error) = await RunAppAsync(
                "type",
                "DotnetInspect.Cli.Tests.SampleClassForTesting",
                "--library", path, "--mermaid");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.StartsWith("graph TD\n", output);
            Assert.DoesNotContain("Local documentation", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeHierarchy_MalformedRootAdjacency_FailsMermaidWithoutOutput()
    {
        string directory = Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            $"type-hierarchy-malformed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Malformed.dll");
        try
        {
            WriteMalformedAdjacencyAssembly(
                path,
                malformedAssemblyReference: true);
            var (exit, output, error) = await RunAppAsync(
                "type", "N.Healthy", "--library", path, "--mermaid");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("--mermaid", error);
            Assert.Contains("invalid AssemblyRef", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

}
