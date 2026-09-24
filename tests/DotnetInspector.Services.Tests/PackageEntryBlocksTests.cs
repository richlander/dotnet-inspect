using System.IO.Compression;
using DotnetInspector.Packages;
using ZipFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Gate 8 of <c>docs/design/package-read-demand.md#pathological-cases-and-gates</c>:
/// the aligned-block planner over a synthetic directory.
/// </summary>
public sealed class PackageEntryBlocksTests
{
    private const long Budget = 1_000;

    /// <summary>
    /// Contiguous extents of 300, 300, 300, 2500 (above the budget), 100,
    /// 950, 60, and 40 bytes.
    /// </summary>
    private static readonly PackageEntryExtent[] Folder = Contiguous(
        ("a.dll", 300),
        ("a.xml", 300),
        ("b.dll", 300),
        ("huge.dll", 2_500),
        ("c.dll", 100),
        ("d.dll", 950),
        ("e.dll", 60),
        ("e.xml", 40));

    [Fact]
    public void Plan_EntryAboveTheBudgetIsABlockAlone()
    {
        IReadOnlyList<IReadOnlyList<PackageEntryExtent>> blocks =
            PackageEntryBlocks.Plan(Folder, Budget);

        Assert.Equal(
            [
                ["a.dll", "a.xml", "b.dll"],
                ["huge.dll"],
                ["c.dll"],
                ["d.dll"],
                ["e.dll", "e.xml"],
            ],
            blocks.Select(block => block.Select(entry => entry.Name).ToArray()));
        Assert.All(
            blocks.Where(block => block.Count > 1),
            block => Assert.True(block.Sum(entry => entry.Length) <= Budget));
    }

    [Fact]
    public void Plan_BlocksTileTheFolderWithoutGapOrOverlap()
    {
        IReadOnlyList<IReadOnlyList<PackageEntryExtent>> blocks =
            PackageEntryBlocks.Plan(Folder, Budget);

        Assert.Equal(
            Folder.Select(entry => entry.Name),
            blocks.SelectMany(block => block).Select(entry => entry.Name));
        Assert.Equal(Folder[0].Offset, blocks[0][0].Offset);
        for (int i = 1; i < blocks.Count; i++)
            Assert.Equal(blocks[i - 1][^1].End, blocks[i][0].Offset);
        Assert.Equal(Folder[^1].End, blocks[^1][^1].End);
    }

    [Fact]
    public void Plan_IsDeterministicWhateverTheInputOrder()
    {
        IReadOnlyList<IReadOnlyList<PackageEntryExtent>> expected =
            PackageEntryBlocks.Plan(Folder, Budget);

        for (int seed = 0; seed < 8; seed++)
        {
            PackageEntryExtent[] shuffled = [.. Folder];
            new Random(seed).Shuffle(shuffled);
            Assert.Equal(
                expected.Select(block => block.ToArray()),
                PackageEntryBlocks.Plan(shuffled, Budget).Select(block => block.ToArray()));
        }
    }

    [Fact]
    public void Plan_ZeroBudgetMakesEveryEntryABlock()
    {
        Assert.All(
            PackageEntryBlocks.Plan(Folder, 0),
            block => Assert.Single(block));
    }

    /// <summary>
    /// Over a real directory, a folder's entries are measured to the folder's
    /// next local header, so an entry interleaved with another folder's
    /// carries that folder's bytes, and the folder's last entry is measured to
    /// the next header in the archive. Only direct entries join the folder.
    /// </summary>
    [Fact]
    public void FolderExtents_MeasureToTheNextHeaderOfTheFolder()
    {
        ZipDirectory directory = Directory(
            ("lib/x/a.dll", 100),
            ("lib/y/a.dll", 200),
            ("lib/x/b.dll", 50),
            ("lib/x/sub/c.dll", 10),
            ("tail.txt", 5));

        IReadOnlyList<PackageEntryExtent> extents =
            PackageEntryBlocks.FolderExtents(directory, "lib/x/");

        Assert.Equal(["lib/x/a.dll", "lib/x/b.dll"], extents.Select(entry => entry.Name));
        ZipEntry a = directory.Find("lib/x/a.dll")!;
        ZipEntry b = directory.Find("lib/x/b.dll")!;
        ZipEntry sub = directory.Find("lib/x/sub/c.dll")!;
        Assert.Equal(b.LocalHeaderOffset - a.LocalHeaderOffset, extents[0].Length);
        Assert.Equal(sub.LocalHeaderOffset - b.LocalHeaderOffset, extents[1].Length);
    }

    /// <summary>
    /// A selection plans each anchor's block once, and an anchor's block is
    /// required whole; exact entries are kept as named.
    /// </summary>
    [Fact]
    public void PlanSelection_RequiresEachAnchorsBlockOnce()
    {
        ZipDirectory directory = Directory(
            ("ref/a.dll", 10),
            ("lib/a.dll", 10),
            ("lib/b.dll", 10),
            ("lib/c.dll", 10));

        PackageRangedPlan plan = PackageEntryBlocks.PlanSelection(
            directory,
            new PackageRangedSelection(["ref/a.dll"], ["lib/b.dll", "lib/a.dll"]),
            budget: 1_000_000);

        IReadOnlyList<string> block = Assert.Single(plan.Blocks);
        Assert.Equal(["lib/a.dll", "lib/b.dll", "lib/c.dll"], block);
        Assert.Equal(["ref/a.dll", "lib/a.dll", "lib/b.dll", "lib/c.dll"], plan.Required);
    }

    private static PackageEntryExtent[] Contiguous(params (string Name, long Length)[] entries)
    {
        var extents = new PackageEntryExtent[entries.Length];
        long offset = 4_096;
        for (int i = 0; i < entries.Length; i++)
        {
            extents[i] = new PackageEntryExtent(entries[i].Name, offset, entries[i].Length);
            offset += entries[i].Length;
        }
        return extents;
    }

    private static ZipDirectory Directory(params (string Name, int Length)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, int length) in entries)
            {
                using Stream stream = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
                stream.Write(new byte[length]);
            }
        }
        byte[] bytes = buffer.ToArray();
        return ZipArchiveReader.ReadDirectoryFromRegion(
            bytes,
            bytes.Length,
            ZipReadLimits.Default);
    }
}
