using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

public sealed class FileSystemPackageContentPullTests
{
    private const string EntryPath = "lib/net10.0/Sample.dll";

    [Fact]
    public void RetainedArchive_ValidatesExtractedEntryThroughEndOfStream()
    {
        byte[] expected = [1, 2, 3, 4];
        using var fixture = new FileSystemPackageFixture(
            archiveContent: expected,
            extractedContent: expected);

        Assert.True(
            fixture.Source.TryOpenPayloadRead(
                EntryPath,
                maxExpandedBytes: expected.Length,
                out Stream? stream));
        Assert.NotNull(stream);
        Assert.False(stream.CanSeek);

        using (stream)
        using (var output = new MemoryStream())
        {
            stream.CopyTo(output);
            Assert.Equal(expected, output.ToArray());
        }
    }

    [Fact]
    public void ForeignExtractedEntry_SameLengthCrcMismatchFailsAtEndOfStream()
    {
        using var fixture = new FileSystemPackageFixture(
            archiveContent: [1, 2, 3],
            extractedContent: [1, 2, 4]);

        Assert.True(
            fixture.Source.TryOpenPayloadRead(
                EntryPath,
                maxExpandedBytes: 3,
                out Stream? stream));
        Assert.NotNull(stream);

        using (stream)
        {
            Assert.Throws<InvalidDataException>(
                () => stream.CopyTo(Stream.Null));
        }
    }

    [Fact]
    public void ForeignExtractedEntry_DeclaredSizeMismatchFailsBeforeRead()
    {
        using var fixture = new FileSystemPackageFixture(
            archiveContent: [1, 2, 3],
            extractedContent: [1]);

        Assert.Throws<InvalidDataException>(
            () => fixture.Source.TryOpenPayloadRead(
                EntryPath,
                maxExpandedBytes: 3,
                out _));
    }

    [Fact]
    public void ArchiveLessContent_VisiblyDeclinesHousePullReads()
    {
        using var fixture = new FileSystemPackageFixture(
            archiveContent: null,
            extractedContent: [1, 2, 3]);

        Assert.Throws<NotSupportedException>(
            () => fixture.Source.TryOpenPayloadRead(
                EntryPath,
                maxExpandedBytes: 3,
                out _));
    }

    private sealed class FileSystemPackageFixture : IDisposable
    {
        private readonly DirectoryInfo _root =
            Directory.CreateTempSubdirectory("package-house-pull-");

        internal FileSystemPackageFixture(
            byte[]? archiveContent,
            byte[] extractedContent)
        {
            string entry = Path.Combine(
                _root.FullName,
                "lib",
                "net10.0",
                "Sample.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
            File.WriteAllBytes(entry, extractedContent);

            string? nupkg = null;
            if (archiveContent is not null)
            {
                nupkg = Path.Combine(_root.FullName, "Sample.1.0.0.nupkg");
                File.WriteAllBytes(
                    nupkg,
                    TestPackageArchive.Create((EntryPath, archiveContent)));
            }

            Content = new FileSystemPackageContent(
                _root.FullName,
                nupkg,
                fromCache: true,
                producerKey: "fixture");
            Source = (IPackageHousePayloadSource)Content;
        }

        internal FileSystemPackageContent Content { get; }

        internal IPackageHousePayloadSource Source { get; }

        public void Dispose() => _root.Delete(recursive: true);
    }
}
