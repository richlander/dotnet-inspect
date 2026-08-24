using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

static class SemanticSpacingFixture
{
    public static int Grouped(string first, string second, int kind)
    {
        if (first is null)
            throw new ArgumentNullException(nameof(first));

        first = first.Trim();
        second = second.Trim();
        int length = first.Length + second.Length;
        if (length == 0)
            return -1;

        switch (kind)
        {
            case 0:
                GC.KeepAlive(first);
                break;
            case 1:
                GC.KeepAlive(second);
                break;
            case 2:
                GC.KeepAlive(length);
                break;
            case 3:
                GC.KeepAlive(kind);
                break;
            default:
                GC.KeepAlive(null);
                break;
        }
        return length;
    }

    public static int Compact(int value)
    {
        if (value < 0)
            return -1;
        return value + 1;
    }

    public static int SiblingControlFlow(string first, string second, int kind)
    {
        first = first.Trim();
        second = second.Trim();
        int length = first.Length + second.Length;
        if (length > 10)
            GC.KeepAlive(length);
        switch (kind)
        {
            case 0:
                GC.KeepAlive(first);
                break;
            default:
                GC.KeepAlive(second);
                break;
        }
        return length;
    }
}

[Trait("Area", "Printer")]
public class CSharpPrinterSemanticSpacingTests
{
    static (string Output, PrintedRangeMap Ranges) Print(string methodName)
    {
        using var source = MetadataSource.Open(typeof(SemanticSpacingFixture).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(SemanticSpacingFixture).FullName!,
            methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, out var ranges);
        Assert.NotNull(result.Output);
        return (result.Output!, ranges);
    }

    [Fact]
    public void LongMethod_SeparatesCompletedConditionalGroupsButKeepsSetupCompact()
    {
        var (output, _) = Print(nameof(SemanticSpacingFixture.Grouped));

        Assert.Contains(
            "throw new ArgumentNullException(\"first\");\n" +
            "}\n\n" +
            "first = first.Trim();",
            output);
        Assert.Contains(
            "int length = first.Length + second.Length;\n" +
            "if (length == 0)",
            output);
        Assert.Contains(
            "return -1;\n" +
            "}\n\n" +
            "switch (kind)",
            output);
        Assert.Equal(2, output.Split("\n\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void LongMethod_SeparatesSiblingControlFlowGroups()
    {
        var (output, _) = Print(nameof(SemanticSpacingFixture.SiblingControlFlow));

        Assert.Contains(
            "int length = first.Length + second.Length;\n" +
            "if (length > 10)",
            output);
        Assert.Contains(
            "GC.KeepAlive(length);\n" +
            "}\n\n" +
            "if (kind == 0)",
            output);
        Assert.Equal(1, output.Split("\n\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void CompactMethod_KeepsAdjacentStatementsCompact()
    {
        var (output, _) = Print(nameof(SemanticSpacingFixture.Compact));

        Assert.Contains(
            "return -1;\n" +
            "}\n" +
            "return value + 1;",
            output);
        Assert.DoesNotContain("}\n\nreturn value + 1;", output);
    }

    [Fact]
    public void InsertedBlankLines_StayOutsideStatementRangesAndPortableCoordinatesRemainExact()
    {
        var (output, ranges) = Print(nameof(SemanticSpacingFixture.Grouped));
        var map = PrintedBodyMap.Create(ranges);
        var switchStatement = Assert.Single(
            ranges,
            range => range.Node is Switch);

        int start = switchStatement.Characters.Start.GetOffset(output.Length);
        Assert.Equal("switch (kind)", output[start..].Split('\n')[0].TrimStart());
        Assert.NotEqual('\n', output[start]);

        Assert.True(ranges.TryGetExtent(switchStatement.Node, out var extent));
        Assert.Equal(output[..start].Count(character => character == '\n'), extent.StartLine);
        Assert.Equal("switch (kind)", map.Lines[extent.StartLine].TrimStart());
        Assert.Equal("", map.Lines[extent.StartLine - 1]);
    }

    [Fact]
    public void InsertedBlankLines_RebaseAnnotatedSourceDocumentSpans()
    {
        using var source = MetadataSource.Open(typeof(SemanticSpacingFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(SemanticSpacingFixture).FullName!,
            nameof(SemanticSpacingFixture.Grouped),
            SourceDocument: true));
        Assert.Null(projection.SourceDocumentFailure);
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);
        var switchNode = Assert.Single(
            document.Nodes,
            node => node.Medium == SourceLineKind.CSharp
                && node.Kind == "SwitchStatement");

        string selected = string.Concat(
            switchNode.Spans.Select(
                span => document.Text.Substring(span.Start, span.Length)));
        Assert.StartsWith("switch (kind)\n{", selected, StringComparison.Ordinal);
        Assert.Equal(
            document.Text.IndexOf("switch (kind)", StringComparison.Ordinal),
            switchNode.Spans[0].Start);
    }
}
