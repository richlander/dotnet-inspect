using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;
using Mdi;

namespace DotnetInspector.MetadataRendering.Tests;

/// <summary>
/// Tests for the <see cref="MdiCommand"/> front-end: option wiring and the
/// error contract of <see cref="MdiCommand.Execute"/> (failures stay visible on
/// the error writer and are never collapsed into success-shaped output).
/// </summary>
public class MdiCommandTests
{
    static readonly string SelfPath = typeof(MdiCommandTests).Assembly.Location;

    [Fact]
    public void Execute_MissingFile_ReturnsErrorAndReports()
    {
        var error = new StringWriter();
        int code = MdiCommand.Execute(
            "does-not-exist.dll",
            new MetadataProjectionOptions(),
            MetadataTableFormat.Markdown,
            new StringWriter(),
            error);

        Assert.Equal(1, code);
        Assert.Contains("file not found", error.ToString());
    }

    [Fact]
    public void Execute_NonManagedFile_ReportsFailure()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "this is not a PE image");

            var error = new StringWriter();
            var output = new StringWriter();
            int code = MdiCommand.Execute(
                path,
                new MetadataProjectionOptions(),
                MetadataTableFormat.Markdown,
                output,
                error);

            Assert.Equal(1, code);
            Assert.False(string.IsNullOrWhiteSpace(error.ToString()));
            Assert.Equal(string.Empty, output.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Execute_SelfAssembly_RendersTables()
    {
        var output = new StringWriter();
        int code = MdiCommand.Execute(
            SelfPath,
            new MetadataProjectionOptions(),
            MetadataTableFormat.Markdown,
            output,
            new StringWriter());

        Assert.Equal(0, code);
        Assert.Contains("## TypeDef", output.ToString());
    }

    [Fact]
    public void Execute_TableSelection_RestrictsOutput()
    {
        var output = new StringWriter();
        int code = MdiCommand.Execute(
            SelfPath,
            new MetadataProjectionOptions { Tables = [TableIndex.Module] },
            MetadataTableFormat.Markdown,
            output,
            new StringWriter());

        Assert.Equal(0, code);
        var text = output.ToString();
        Assert.Contains("## Module", text);
        Assert.DoesNotContain("## TypeDef", text);
    }

    [Fact]
    public void CreateRootCommand_ParsesKnownOptions_WithoutErrors()
    {
        var root = MdiCommand.CreateRootCommand();

        var result = root.Parse(["some.dll", "--format", "jsonl", "-t", "TypeDef,MethodDef", "-n", "10"]);

        Assert.Empty(result.Errors);
    }
}
