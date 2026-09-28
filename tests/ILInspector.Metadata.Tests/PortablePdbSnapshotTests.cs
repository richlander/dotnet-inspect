using System.Collections.Immutable;
using System.Reflection.Metadata;
using DotnetInspector.Queries.EmbeddedFixtures;

namespace ILInspector.Metadata.Tests;

// PR-fast: the production Metadata assembly and its supplied Portable PDB.
public class PortablePdbSnapshotTests
{
    [Fact]
    public void SuppliedPortablePdbSnapshotSurvivesContextDisposal()
    {
        string assemblyPath = typeof(PdbContext).Assembly.Location;
        byte[] expected = File.ReadAllBytes(Path.ChangeExtension(assemblyPath, ".pdb"));
        ImmutableArray<byte> snapshot;
        var assembly = ResolvedAssemblyReference.CreateFromPath(
            assemblyPath, AssemblyResolutionProvenance.Local("PDB snapshot"))
            .WithoutLocalPath();

        using (PdbContext context = PdbContext.OpenMetadataOnly(assembly))
        {
            Assert.Null(context.GetPortablePdbImage());
            context.LoadPdbFromStream(new MemoryStream(expected, writable: false));
            Assert.True(context.HasPdb);
            Assert.Equal(PdbLoadStatus.Loaded, context.LastPdbLoadStatus);
            Assert.Null(context.LastPdbLoadError);
            snapshot = context.GetPortablePdbImage()!.Value;
            Assert.True(expected.AsSpan().SequenceEqual(snapshot.AsSpan()));
        }

        using var provider = MetadataReaderProvider.FromPortablePdbImage(snapshot);
        Assert.NotEmpty(provider.GetMetadataReader().Documents);
        Assert.True(expected.AsSpan().SequenceEqual(snapshot.AsSpan()));
    }

    [Fact]
    public void EmbeddedPortablePdbSnapshotSurvivesContextDisposal()
    {
        ImmutableArray<byte> snapshot;
        using (PdbContext context = PdbContext.OpenEmbeddedPdbOnly(
            typeof(EmbeddedSourceFixture).Assembly.Location))
        {
            snapshot = context.GetPortablePdbImage()!.Value;
            Assert.NotEmpty(snapshot);
        }

        using var provider =
            MetadataReaderProvider.FromPortablePdbImage(snapshot);
        Assert.NotEmpty(provider.GetMetadataReader().Documents);
    }

    [Fact]
    public void MissingOrRejectedPdbDoesNotManufactureSnapshot()
    {
        string assemblyPath = typeof(PdbContext).Assembly.Location;
        using PdbContext context = PdbContext.OpenMetadataOnly(assemblyPath);
        Assert.Null(context.GetPortablePdbImage());
        context.LoadPdbFromStream(new MemoryStream([1, 2, 3, 4]));
        Assert.False(context.HasPdb);
        Assert.Equal(
            PdbLoadStatus.UnsupportedFormat,
            context.LastPdbLoadStatus);
        Assert.NotNull(context.LastPdbLoadError);
        Assert.Null(context.GetPortablePdbImage());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TruncatedPortablePdbRetainsMalformedLoadStatus(int length)
    {
        string assemblyPath = typeof(PdbContext).Assembly.Location;
        using PdbContext context =
            PdbContext.OpenMetadataOnly(assemblyPath);
        byte[] content =
            [(byte)'B', (byte)'S', (byte)'J'];

        context.LoadPdbFromStream(
            new MemoryStream(
                content[..length],
                writable: false));

        Assert.False(context.HasPdb);
        Assert.Equal(
            PdbLoadStatus.Malformed,
            context.LastPdbLoadStatus);
        Assert.NotNull(context.LastPdbLoadError);
        Assert.Null(context.GetPortablePdbImage());
    }

    [Fact]
    public void MalformedPortablePdbRetainsTypedLoadStatus()
    {
        string assemblyPath = typeof(PdbContext).Assembly.Location;
        using PdbContext context =
            PdbContext.OpenMetadataOnly(assemblyPath);

        context.LoadPdbFromStream(
            new MemoryStream(
                [(byte)'B', (byte)'S', (byte)'J', (byte)'B'],
                writable: false));

        Assert.False(context.HasPdb);
        Assert.Equal(
            PdbLoadStatus.Malformed,
            context.LastPdbLoadStatus);
        Assert.NotNull(context.LastPdbLoadError);
        Assert.Null(context.GetPortablePdbImage());
    }

    [Fact]
    public void DisposedContextCannotIssueSnapshot()
    {
        PdbContext context = PdbContext.OpenMetadataOnly(typeof(PdbContext).Assembly.Location);
        context.Dispose();
        Assert.Throws<ObjectDisposedException>(() => context.GetPortablePdbImage());
    }
}
