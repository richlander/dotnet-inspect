using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataRootInspectionTests
{
    [Fact]
    public void SeparateManifest_ProjectsItsOwnRootAndProvenance()
    {
        using var pe = Open(ManifestImage());
        var root = Assert.IsType<MetadataRootInspection>(
            MetadataRootInspection.Open(pe, MetadataRootKind.ReadyToRunManifest));
        var cli = Assert.IsType<MetadataRootInspection>(MetadataRootInspection.Open(pe));
        var extent = Assert.IsType<ReadyToRunSectionSummary>(
            ReadyToRunImageInspector.Describe(pe)!.ManifestMetadata);

        Assert.Equal(MetadataRootKind.ReadyToRunManifest, root.RequestedRoot);
        Assert.Equal(
            new MetadataRootIdentity(MetadataRootKind.ReadyToRunManifest, extent.RelativeVirtualAddress, extent.Size),
            root.Identity);
        Assert.NotEqual(cli.Identity, root.Identity);
        Assert.False(root.Image().IsAssembly);
        Assert.Equal(extent.Size, root.Image().MetadataSize);
        Assert.NotEqual(cli.Image().MetadataOffset, root.Image().MetadataOffset);
        Assert.Equal(2, root.Image().Tables.Single(t => t.Index == TableIndex.AssemblyRef).RowCount);
        Assert.Equal("Manifest.Dependency", Name(root.Row(TableIndex.AssemblyRef, 1)!));
        Assert.NotEqual("Manifest.Dependency", Name(cli.Row(TableIndex.AssemblyRef, 1)!));
    }

    [Fact]
    public void ManifestNavigation_UsesTheSelectedRootAndExistingBudgets()
    {
        using var pe = Open(ManifestImage());
        var root = MetadataRootInspection.Open(pe, MetadataRootKind.ReadyToRunManifest)!;
        var options = new MetadataProjectionOptions
        {
            Tables = [TableIndex.AssemblyRef],
            StartRowId = 2,
            MaxRowsPerTable = 1,
        };
        var table = Assert.Single(root.Tables(options).Tables);
        Assert.Equal(2, Assert.Single(table.Rows).RowId);
        Assert.NotNull(table.Truncation);
        Assert.Equal(2, table.RowCount);

        var type = Assert.IsType<MetadataTableView>(root.Row(TableIndex.TypeRef, 1, options));
        var reference = Assert.IsType<MetadataValue.Handle>(type.Rows[0].Cells[0]).Reference;
        Assert.Equal(TableIndex.AssemblyRef, reference.TargetTable);
        Assert.Equal(1, reference.TargetRowId);
        Assert.Equal("Manifest.Dependency", Name(root.Row(reference.TargetTable, reference.TargetRowId)!));

        var references = root.References(TableIndex.AssemblyRef, 1);
        Assert.True(references.TargetExists);
        Assert.Equal(TableIndex.TypeRef, Assert.Single(references.References).Source.Table);
        Assert.False(root.References(TableIndex.AssemblyRef, 3).TargetExists);

        var name = NameValue(root.Row(TableIndex.AssemblyRef, 1)!);
        Assert.Equal(name, root.HeapValue(name.Heap, name.Offset));
        Assert.Contains(root.HeapEntries(HeapKind.String).Entries, entry => entry.Offset == name.Offset);
        var clipped = NameValue(root.Row(
            TableIndex.AssemblyRef, 1, new MetadataProjectionOptions { MaxStringChars = 4 })!);
        Assert.True(clipped.Truncated);
    }

    [Fact]
    public void CliAlias_ReusesCanonicalIdentityWithoutLosingRequestedProvenance()
    {
        var image = ReadyToRunImageInspectorTests.CreateImage(
            managedNative: true, exported: true, manifestAliasesCliMetadata: true);
        using var pe = Open(image.Bytes);
        var cli = MetadataRootInspection.Open(pe)!;
        var manifest = MetadataRootInspection.Open(pe, MetadataRootKind.ReadyToRunManifest)!;

        Assert.Equal(MetadataRootKind.ReadyToRunManifest, manifest.RequestedRoot);
        Assert.Equal(MetadataRootKind.Cli, manifest.Identity.Kind);
        Assert.Equal(cli.Identity, manifest.Identity);
        Assert.Equal(cli.Image().MetadataOffset, manifest.Image().MetadataOffset);
        Assert.Equal(cli.Image().MetadataSize, manifest.Image().MetadataSize);
        Assert.Equal(Name(cli.Row(TableIndex.AssemblyRef, 1)!), Name(manifest.Row(TableIndex.AssemblyRef, 1)!));
    }

    [Fact]
    public void UnadvertisedAndMissingManifest_ReturnNull()
    {
        using var ordinary = Open(File.ReadAllBytes(typeof(MetadataRootInspectionTests).Assembly.Location));
        Assert.Null(MetadataRootInspection.Open(ordinary, MetadataRootKind.ReadyToRunManifest));

        var image = ReadyToRunImageInspectorTests.CreateImage(
            managedNative: true, exported: false,
            sections: [new(ReadyToRunSectionType.CompilerIdentifier, [1])]);
        using var withoutManifest = Open(image.Bytes);
        Assert.Null(MetadataRootInspection.Open(withoutManifest, MetadataRootKind.ReadyToRunManifest));
        Assert.NotNull(MetadataRootInspection.Open(withoutManifest));
    }

    [Fact]
    public void ExportDiscoveredManifest_DoesNotRequireCliMetadata()
    {
        var image = ReadyToRunImageInspectorTests.CreateImage(
            managedNative: false, exported: true,
            sections: [new(ReadyToRunSectionType.ManifestMetadata, BuildManifest())]);
        Array.Clear(image.Bytes, image.ExportDataDirectoryOffset + (14 * 8), 8);
        using var pe = Open(image.Bytes);

        Assert.Null(MetadataRootInspection.Open(pe));
        var root = MetadataRootInspection.Open(pe, MetadataRootKind.ReadyToRunManifest)!;
        Assert.Null(root.Image().Headers.Cor);
        Assert.Equal("Manifest.Dependency", Name(root.Row(TableIndex.AssemblyRef, 1)!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedOrEmptyManifest_IsNotAbsenceOrCliFallback(bool empty)
    {
        var image = ReadyToRunImageInspectorTests.CreateImage(
            managedNative: true, exported: false,
            sections: [new(ReadyToRunSectionType.ManifestMetadata, empty ? [] : "BSJB"u8.ToArray())]);
        using var pe = Open(image.Bytes);
        Assert.NotNull(ReadyToRunImageInspector.Describe(pe));
        Assert.Throws<MalformedMetadataRootException>(
            () => MetadataRootInspection.Open(pe, MetadataRootKind.ReadyToRunManifest));
        Assert.NotNull(MetadataImageInspector.Describe(pe));
        Assert.NotEmpty(MetadataTableProjector.Project(pe).Tables);
    }

    [Fact]
    public void TruncatedManifest_DoesNotReadTheRemainingBytesInItsPeSection()
    {
        var image = ReadyToRunImageInspectorTests.CreateImage(
            managedNative: true, exported: false,
            sections: [new(ReadyToRunSectionType.ManifestMetadata, BuildManifest())]);
        int sizeOffset = image.HeaderOffset + ReadyToRunImageInspector.FixedHeaderSize + 8;
        BinaryPrimitives.WriteInt32LittleEndian(image.Bytes.AsSpan(sizeOffset, 4), 32);
        using var pe = Open(image.Bytes);
        var root = MetadataRootInspection.Open(pe, MetadataRootKind.ReadyToRunManifest)!;

        Assert.Equal(32, root.Identity.Size);
        Assert.Throws<BadImageFormatException>(() => root.Image());
        Assert.Throws<BadImageFormatException>(() => root.Tables());
        Assert.NotNull(MetadataImageInspector.Describe(pe));
    }

    [Fact]
    public void RawBackedManifest_CanExtendBeyondTheSectionsVirtualSize()
    {
        var image = ReadyToRunImageInspectorTests.CreateImage(
            managedNative: true, exported: false,
            sections: [new(ReadyToRunSectionType.ManifestMetadata, BuildManifest())]);
        int rawSize;
        using (var original = Open(image.Bytes))
        {
            var manifest = ReadyToRunImageInspector.Describe(original)!.ManifestMetadata!;
            var section = original.PEHeaders.SectionHeaders[
                original.PEHeaders.GetContainingSectionIndex(manifest.RelativeVirtualAddress)];
            rawSize = section.SizeOfRawData - (manifest.RelativeVirtualAddress - section.VirtualAddress);
            Assert.True(rawSize > original.GetSectionData(manifest.RelativeVirtualAddress).Length);
        }

        int sizeOffset = image.HeaderOffset + ReadyToRunImageInspector.FixedHeaderSize + 8;
        BinaryPrimitives.WriteInt32LittleEndian(image.Bytes.AsSpan(sizeOffset, 4), rawSize);
        using var pe = Open(image.Bytes);
        Assert.Equal(rawSize, ReadyToRunImageInspector.Describe(pe)!.ManifestMetadata!.Size);

        var root = MetadataRootInspection.Open(pe, MetadataRootKind.ReadyToRunManifest)!;
        Assert.Equal(rawSize, root.Identity.Size);
        Assert.Equal(rawSize, root.Image().MetadataSize);
        Assert.Equal("Manifest.Dependency", Name(root.Row(TableIndex.AssemblyRef, 1)!));
    }

    [Fact]
    public void CoffMetadataWithoutCliDirectory_HasATypedRootFailure()
    {
        byte[] metadata = BuildManifest();
        const int coffHeaderSize = 20;
        const int sectionHeaderSize = 40;
        const int metadataOffset = coffHeaderSize + sectionHeaderSize;
        byte[] bytes = new byte[metadataOffset + metadata.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0, 2), (ushort)Machine.Amd64);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), 1);
        ".cormeta"u8.CopyTo(bytes.AsSpan(coffHeaderSize, 8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(coffHeaderSize + 16, 4), metadata.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(coffHeaderSize + 20, 4), metadataOffset);
        metadata.CopyTo(bytes, metadataOffset);
        using var pe = Open(bytes);

        Assert.True(pe.PEHeaders.IsCoffOnly);
        Assert.True(pe.HasMetadata);
        Assert.Null(MetadataImageInspector.Describe(pe)!.Headers.Cor);
        Assert.NotEmpty(MetadataTableProjector.Project(pe).Tables);
        Assert.Throws<BadImageFormatException>(() => MetadataRootInspection.Open(pe));
        using var session = AssemblyInspectionSession.OpenPrefetched(new MemoryStream(bytes));
        Assert.Throws<BadImageFormatException>(() => session.MetadataRoot());
    }

    [Fact]
    public void MalformedR2rAdvertisement_DoesNotChangeDefaultCliProjection()
    {
        var image = ReadyToRunImageInspectorTests.CreateImage(managedNative: true, exported: false);
        BinaryPrimitives.WriteUInt32LittleEndian(image.Bytes.AsSpan(image.HeaderOffset + 12, 4), uint.MaxValue);
        using var pe = Open(image.Bytes);

        Assert.Throws<BadImageFormatException>(
            () => MetadataRootInspection.Open(pe, MetadataRootKind.ReadyToRunManifest));
        Assert.NotNull(MetadataRootInspection.Open(pe)!.Image());
        Assert.NotEmpty(MetadataTableProjector.Project(pe).Tables);
    }

    [Fact]
    public void CapturedRoot_RemainsReadableAfterSourceReaderDisposal()
    {
        MetadataRootInspection root;
        using (var pe = Open(ManifestImage()))
            root = MetadataRootInspection.Open(pe, MetadataRootKind.ReadyToRunManifest)!;

        Assert.Equal("Manifest.Dependency", Name(root.Row(TableIndex.AssemblyRef, 1)!));
        Assert.False(root.Image().IsAssembly);
    }

    [Fact]
    public void SessionFacet_CapturesRootBeforeSessionDisposal()
    {
        var session = AssemblyInspectionSession.Open(typeof(MetadataRootInspectionTests).Assembly.Location);
        var root = Assert.IsType<MetadataRootInspection>(session.MetadataRoot());
        session.Dispose();

        Assert.Equal(MetadataRootKind.Cli, root.Identity.Kind);
        Assert.True(root.Image().IsAssembly);
        Assert.Throws<ObjectDisposedException>(() => session.MetadataRoot());
    }

    [Fact]
    public void RuntimeCoreLib_ProjectsCompilerProducedManifest()
    {
        using var pe = Open(File.ReadAllBytes(typeof(object).Assembly.Location));
        var root = Assert.IsType<MetadataRootInspection>(
            MetadataRootInspection.Open(pe, MetadataRootKind.ReadyToRunManifest));

        Assert.Equal(MetadataRootKind.ReadyToRunManifest, root.RequestedRoot);
        Assert.True(root.Image().MetadataSize > 0);
        Assert.NotEqual(MetadataRootInspection.Open(pe)!.Identity, root.Identity);
        Assert.Contains(root.Tables().Tables, table => table.Index == TableIndex.Module);
    }

    static PEReader Open(byte[] bytes) => new(new MemoryStream(bytes));

    static byte[] ManifestImage() => ReadyToRunImageInspectorTests.CreateImage(
        managedNative: true, exported: false,
        sections: [new(ReadyToRunSectionType.ManifestMetadata, BuildManifest())]).Bytes;

    static byte[] BuildManifest() => ReadyToRunImageFixture.BuildManifestMetadata();

    static MetadataValue.HeapReference NameValue(MetadataTableView table)
    {
        int column = table.Columns.IndexOf(table.Columns.Single(c => c.Name == "Name"));
        return Assert.IsType<MetadataValue.HeapReference>(Assert.Single(table.Rows).Cells[column]);
    }

    static string Name(MetadataTableView table) => NameValue(table).Text!.Value.ToString();
}
