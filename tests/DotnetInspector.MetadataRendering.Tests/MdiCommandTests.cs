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

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(BadImageFormatException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(NotSupportedException))]
    public void IsExpectedReadFailure_TreatsFileAccessFailuresAsExpected(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;
        Assert.True(MdiCommand.IsExpectedReadFailure(ex));
    }

    [Theory]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(StackOverflowException))]
    public void IsExpectedReadFailure_LetsUnexpectedErrorsPropagate(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;
        Assert.False(MdiCommand.IsExpectedReadFailure(ex));
    }

    [Fact]
    public void Execute_TruncatedTable_MachineFormat_ReportsTruncationOnError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = MdiCommand.Execute(
            SelfPath,
            new MetadataProjectionOptions { MaxRowsPerTable = 1 },
            MetadataTableFormat.Jsonl,
            output,
            error);

        Assert.Equal(0, code);
        // Machine output stays a pure stream on stdout; the truncation stays
        // visible as a diagnostic on stderr rather than being silently dropped.
        Assert.False(string.IsNullOrWhiteSpace(output.ToString()));
        Assert.Contains("shows rows 1 to 1 of", error.ToString());
    }

    [Fact]
    public void Execute_WindowedTable_MachineFormat_NamesTheWindowNotJustItsSize()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = MdiCommand.Execute(
            SelfPath,
            new MetadataProjectionOptions { StartRowId = 3, MaxRowsPerTable = 2 },
            MetadataTableFormat.Jsonl,
            output,
            error);

        Assert.Equal(0, code);
        // "2 of N rows" would read as the first two rows; the note must locate the window.
        Assert.Contains("shows rows 3 to 4 of", error.ToString());
    }

    [Fact]
    public void Execute_WindowedTable_Markdown_HeadingNamesTheRowRange()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = MdiCommand.Execute(
            SelfPath,
            new MetadataProjectionOptions { Tables = [TableIndex.TypeDef], StartRowId = 3, MaxRowsPerTable = 2 },
            MetadataTableFormat.Markdown,
            output,
            error);

        Assert.Equal(0, code);
        Assert.Contains("showing rows 3\u20134 of", output.ToString());
        Assert.True(string.IsNullOrWhiteSpace(error.ToString()));
    }

    [Fact]
    public void CreateRootCommand_RejectsAStartRowBelowOne()
    {
        int code = MdiCommand.CreateRootCommand().Parse([SelfPath, "--start-row", "0"]).Invoke();

        Assert.NotEqual(0, code);
    }

    [Fact]
    public void Execute_TruncatedTable_Markdown_ShowsInHeadingNotOnError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = MdiCommand.Execute(
            SelfPath,
            new MetadataProjectionOptions { MaxRowsPerTable = 1 },
            MetadataTableFormat.Markdown,
            output,
            error);

        Assert.Equal(0, code);
        Assert.Contains("showing 1 of", output.ToString());
        // Markdown carries truncation inline, so it does not also emit the note.
        Assert.DoesNotContain("truncated to", error.ToString());
    }

    [Fact]
    public void ExecuteReferences_SelfAssembly_FindsTheDeclaringTypeOfAField()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        // Field[1] has no back-pointer to its declaring type in ECMA-335; only a
        // TypeDef.FieldList run covers it, so this proves range edges are searched.
        int code = MdiCommand.ExecuteReferences(
            SelfPath,
            TableIndex.Field,
            1,
            MetadataRowReferenceSet.DefaultMaxReferences,
            MetadataTableFormat.Markdown,
            output,
            error);

        Assert.Equal(0, code);
        string markdown = output.ToString();
        Assert.Contains("## References to Field[1]", markdown);
        Assert.Contains("FieldList", markdown);
        Assert.Contains("Range", markdown);
        Assert.True(string.IsNullOrWhiteSpace(error.ToString()));
    }

    [Fact]
    public void ExecuteReferences_MachineFormat_ReportsTruncationOnError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = MdiCommand.ExecuteReferences(
            SelfPath,
            TableIndex.Field,
            1,
            maxReferences: 1,
            MetadataTableFormat.Jsonl,
            output,
            error);

        Assert.Equal(0, code);
        // A pure row stream cannot show a stopped scan, so the caveat must reach stderr.
        Assert.Contains("budget stopped this scan", error.ToString());
    }

    [Fact]
    public void ExecuteReferences_Markdown_KeepsCaveatsInline_NotOnError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = MdiCommand.ExecuteReferences(
            SelfPath,
            TableIndex.Field,
            1,
            maxReferences: 1,
            MetadataTableFormat.Markdown,
            output,
            error);

        Assert.Equal(0, code);
        Assert.Contains("budget stopped this scan", output.ToString());
        Assert.True(string.IsNullOrWhiteSpace(error.ToString()));
    }

    [Fact]
    public void ExecuteReferences_MissingFile_ReturnsErrorAndReports()
    {
        var error = new StringWriter();
        int code = MdiCommand.ExecuteReferences(
            "does-not-exist.dll",
            TableIndex.TypeDef,
            1,
            MetadataRowReferenceSet.DefaultMaxReferences,
            MetadataTableFormat.Markdown,
            new StringWriter(),
            error);

        Assert.Equal(1, code);
        Assert.Contains("file not found", error.ToString());
    }

    [Fact]
    public void ExecuteReferences_RowIdBelowOne_IsRejectedBeforeOpeningTheFile()
    {
        var error = new StringWriter();
        int code = MdiCommand.ExecuteReferences(
            "does-not-exist.dll",
            TableIndex.TypeDef,
            0,
            MetadataRowReferenceSet.DefaultMaxReferences,
            MetadataTableFormat.Markdown,
            new StringWriter(),
            error);

        Assert.Equal(1, code);
        Assert.Contains("row id", error.ToString());
    }

    [Fact]
    public void ExecuteReferences_NonexistentRow_QualifiesTheEmptyAnswer()
    {
        // A row id past the end of its table is a well-formed question about a
        // row that is not there, so it stays an answer rather than an error. It
        // must not read as "nothing points at this row" though: no such row
        // exists to be pointed at, and an unqualified empty answer would say the
        // image was searched and came back clean.
        var output = new StringWriter();
        var error = new StringWriter();
        int code = MdiCommand.ExecuteReferences(
            SelfPath,
            TableIndex.TypeDef,
            int.MaxValue,
            MetadataRowReferenceSet.DefaultMaxReferences,
            MetadataTableFormat.Markdown,
            output,
            error);

        Assert.Equal(0, code);
        Assert.Contains("past the end of its table", output.ToString());
    }

    [Theory]
    [InlineData("TypeDef")]
    [InlineData("TypeDef:")]
    [InlineData("TypeDef:0")]
    [InlineData("TypeDef:-3")]
    [InlineData("TypeDef:abc")]
    [InlineData("NoSuchTable:1")]
    public void CreateRootCommand_RejectsAMalformedReferenceSpec(string spec)
    {
        int code = MdiCommand.CreateRootCommand().Parse([SelfPath, "--references", spec]).Invoke();

        Assert.NotEqual(0, code);
    }

    [Fact]
    public void CreateRootCommand_RejectsANegativeMaxReferences()
    {
        int code = MdiCommand.CreateRootCommand()
            .Parse([SelfPath, "--references", "TypeDef:1", "--max-references", "-1"])
            .Invoke();

        Assert.NotEqual(0, code);
    }

    [Fact]
    public void CreateRootCommand_AcceptsAWellFormedReferenceSpec()
    {
        int code = MdiCommand.CreateRootCommand().Parse([SelfPath, "-r", "typedef:1"]).Invoke();

        Assert.Equal(0, code);
    }
}
