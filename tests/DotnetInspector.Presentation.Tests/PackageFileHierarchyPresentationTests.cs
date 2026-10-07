using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspector.Presentation.Tests;

public sealed class PackageFileHierarchyPresentationTests
{
    [Fact]
    public void HierarchyKeepsFileIdentityAndDirectoryContextAcrossCollapsedChains()
    {
        PackageFileInventoryEntry[] files =
        [
            new("runtimes/win/lib/net8.0/A.dll", 2),
            new("lib/net8.0/A.xml", 3),
            new("lib/net8.0/A.dll", 4),
        ];
        var sink = new RecordingSink();
        PackageFileHierarchyProjection.Write(files, sink);
        Assert.Equal(
            ["lib/net8.0/A.dll", "lib/net8.0/A.xml", "runtimes/win/lib/net8.0/A.dll"],
            sink.Nodes.Where(node => node.File is not null).Select(node => node.Path.ToString()));
        Assert.Equal(
            ["lib/net8.0", "runtimes/win/lib/net8.0"],
            sink.Nodes.Where(node => node.File is null).Select(node => node.Path.ToString()));
        Assert.Equal([false, false, true, true, true], sink.LastSiblings);
        var writer = new StringWriter();
        PackageFileHierarchyPresentation.WriteTree(files, "Package", writer);
        string text = writer.ToString();
        Assert.Contains("├─ lib/net8.0", text);
        Assert.Contains("└─ runtimes/win/lib/net8.0", text);
        Assert.DoesNotContain("| Path |", text);
    }

    [Fact]
    public void DestinationFailurePropagates()
    {
        Assert.Throws<IOException>(() => PackageFileHierarchyPresentation.WriteTree(
            [new("lib/net8.0/A.dll", 1)], null, new FailingWriter()));
    }

    private sealed class RecordingSink : IInspectionHierarchySink<PackageFileHierarchyNode>
    {
        public List<PackageFileHierarchyNode> Nodes { get; } = [];
        public List<bool> LastSiblings { get; } = [];

        public void WriteNode(PackageFileHierarchyNode node, bool isLastSibling,
            Action<IInspectionHierarchySink<PackageFileHierarchyNode>>? writeChildren = null)
        {
            Nodes.Add(node);
            LastSiblings.Add(isLastSibling);
            writeChildren?.Invoke(this);
        }
    }

    private sealed class FailingWriter : StringWriter
    {
        public override void Write(string? value) => throw new IOException("Destination failed.");
        public override void WriteLine(string? value) => throw new IOException("Destination failed.");
    }
}
