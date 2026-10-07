using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspector.Presentation;

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

    [Theory]
    [InlineData("System.Text", "--mermaid")]
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
    public async Task TypeHierarchy_MalformedRootAdjacency_RejectsMermaidWithoutOutput()
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
            Assert.Contains("metadata failures", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeHierarchy_BoundedPreliminaryInventory_DoesNotPublishOutput()
    {
        var options = new TypeOptions
        {
            TypeName = "DotnetInspect.Cli.Tests.SampleClassForTesting",
            AssemblyPath = TestAssemblyPath,
        };
        var (source, sourceError) = await ApiSourceResolver.ResolveAsync(options);
        Assert.Null(sourceError);

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            async () => await TypeOverviewHierarchyCommand.TryExecuteAsync(
                source,
                options,
                TypeOverviewHierarchyPresentationFormat.Mermaid,
                CancellationToken.None,
                maximumInventoryRows: 0) ?? 0);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("unavailable", error);
    }
}
