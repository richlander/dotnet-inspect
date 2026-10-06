using DotnetInspect.Cli.Output;
using Markout;

namespace DotnetInspect.Cli.Tests;

public class MarkoutHierarchySinkTests
{
    [Fact]
    public void WriteNode_StreamsNestedHierarchy()
    {
        using var output = new StringWriter();
        var writer =
            new MarkoutWriter(
                output,
                new MarkdownFormatter());
        var sink =
            new MarkoutHierarchySink<string>(
                writer,
                static node => node);

        sink.WriteNode(
            "First",
            isLastSibling: false,
            children =>
            {
                children.WriteNode(
                    "First child",
                    isLastSibling: true);
            });
        sink.WriteNode(
            "Last",
            isLastSibling: true,
            children =>
            {
                children.WriteNode(
                    "Earlier child",
                    isLastSibling: false);
                children.WriteNode(
                    "Last child",
                    isLastSibling: true);
            });
        writer.Flush();

        Assert.Equal(
            """
            ├─ First
            │  └─ First child
            └─ Last
               ├─ Earlier child
               └─ Last child

            """.ReplaceLineEndings(),
            output.ToString());
    }

    [Fact]
    public void WriteNode_InvokesChildrenBeforeReturning()
    {
        using var output = new StringWriter();
        var writer =
            new MarkoutWriter(
                output,
                new MarkdownFormatter());
        var events = new List<string>();
        var sink =
            new MarkoutHierarchySink<string>(
                writer,
                node =>
                {
                    events.Add($"format:{node}");
                    return node;
                });

        sink.WriteNode(
            "Parent",
            isLastSibling: true,
            children =>
            {
                events.Add("children:start");
                children.WriteNode(
                    "Child",
                    isLastSibling: true);
                events.Add("children:end");
            });
        events.Add("returned");

        Assert.Equal(
            [
                "format:Parent",
                "children:start",
                "format:Child",
                "children:end",
                "returned",
            ],
            events);
    }

    [Fact]
    public void WriteNode_UnwindsDepthWhenChildrenFail()
    {
        using var output = new StringWriter();
        var writer =
            new MarkoutWriter(
                output,
                new MarkdownFormatter());
        var sink =
            new MarkoutHierarchySink<string>(
                writer,
                static node => node);

        Assert.Throws<InvalidOperationException>(() =>
            sink.WriteNode(
                "Failed",
                isLastSibling: false,
                _ => throw new InvalidOperationException("failed")));
        sink.WriteNode(
            "Recovered root",
            isLastSibling: true);
        writer.Flush();

        Assert.Contains(
            "└─ Recovered root",
            output.ToString());
        Assert.DoesNotContain(
            "│  └─ Recovered root",
            output.ToString());
    }
}
