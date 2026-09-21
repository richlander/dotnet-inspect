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
