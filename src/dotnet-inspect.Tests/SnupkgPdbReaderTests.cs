using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

public class SnupkgPdbReaderTests
{
    [Fact]
    public void ExtractPortablePdb_MatchingGuid_ReturnsPdbBytes()
    {
        var guid = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) = BuildPortablePdb(guid);
        var snupkg = MakeSnupkg(("lib/net8.0/Foo.pdb", pdbBytes));

        using var stream = new MemoryStream(snupkg);
        var result = SnupkgPdbReader.ExtractPortablePdb(stream, "Foo", guid);

        Assert.NotNull(result.PdbBytes);
        Assert.False(result.WindowsPdbDetected);
        Assert.Equal(pdbBytes, result.PdbBytes);
    }

    [Fact]
    public void ExtractPortablePdb_MismatchedGuid_ReturnsNull()
    {
        var guid = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) = BuildPortablePdb(guid);
        var snupkg = MakeSnupkg(("lib/net8.0/Foo.pdb", pdbBytes));

        using var stream = new MemoryStream(snupkg);
        var result = SnupkgPdbReader.ExtractPortablePdb(stream, "Foo", Guid.NewGuid());

        Assert.Null(result.PdbBytes);
        Assert.False(result.WindowsPdbDetected);
    }

    [Fact]
    public void ExtractPortablePdb_WindowsPdb_FlagsWindowsDetectedAndReturnsNull()
    {
        var windowsPdb = new byte[] { (byte)'M', (byte)'i', (byte)'c', (byte)'r', 0, 0, 0, 0 };
        var snupkg = MakeSnupkg(("lib/net8.0/Bar.pdb", windowsPdb));

        using var stream = new MemoryStream(snupkg);
        var result = SnupkgPdbReader.ExtractPortablePdb(stream, "Bar", Guid.NewGuid());

        Assert.Null(result.PdbBytes);
        Assert.True(result.WindowsPdbDetected);
    }

    [Fact]
    public void ExtractPortablePdb_NoEntryForAssembly_ReturnsNull()
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(guid);
        var snupkg = MakeSnupkg(("lib/net8.0/Other.pdb", pdbBytes));

        using var stream = new MemoryStream(snupkg);
        var result = SnupkgPdbReader.ExtractPortablePdb(stream, "Foo", guid);

        Assert.Null(result.PdbBytes);
    }

    [Fact]
    public void ExtractPortablePdb_MatchesNestedEntryByFileName()
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(guid);
        // Entry in an arbitrary nested directory; match is by file name.
        var snupkg = MakeSnupkg(("lib/netstandard2.0/subdir/Foo.pdb", pdbBytes));

        using var stream = new MemoryStream(snupkg);
        var result = SnupkgPdbReader.ExtractPortablePdb(stream, "Foo", guid);

        Assert.Equal(pdbBytes, result.PdbBytes);
    }

    internal static (byte[] Bytes, Guid Guid) BuildPortablePdb(Guid id)
    {
        var metadata = new MetadataBuilder();
        var contentId = new BlobContentId(id, 0x04030201u);
        var rowCounts = ImmutableArray.CreateRange(new int[MetadataTokens.TableCount]);
        var pdbBuilder = new PortablePdbBuilder(
            metadata,
            rowCounts,
            entryPoint: default,
            idProvider: _ => contentId);
        var blob = new BlobBuilder();
        pdbBuilder.Serialize(blob);
        return (blob.ToArray(), id);
    }

    internal static byte[] MakeSnupkg(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        return ms.ToArray();
    }
}
