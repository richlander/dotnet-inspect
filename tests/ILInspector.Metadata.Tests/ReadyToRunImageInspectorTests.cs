using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class ReadyToRunImageInspectorTests
{
    static string SelfPath => typeof(ReadyToRunImageInspectorTests).Assembly.Location;

    [Fact]
    public void Describe_UnadvertisedManagedImage_ReturnsNull()
    {
        using var peReader = Open(File.ReadAllBytes(SelfPath));

        Assert.Null(ReadyToRunImageInspector.Describe(peReader));
    }

    [Fact]
    public void Describe_CurrentCoreLibrary_ExercisesCompilerProducedStandaloneImage()
    {
        using var stream = File.OpenRead(typeof(object).Assembly.Location);
        using var peReader = new PEReader(stream);

        ReadyToRunImageOverview? overview = ReadyToRunImageInspector.Describe(peReader);

        Assert.True(
            overview is not null,
            $"Expected the SDK-selected CoreLib to be a standalone R2R image: {typeof(object).Assembly.Location}");
        Assert.True(
            overview.Role == ReadyToRunImageRole.Standalone,
            $"Expected standalone CoreLib at {typeof(object).Assembly.Location}, observed {overview.Role}.");
        Assert.Equal(
            ReadyToRunAdvertisement.ManagedNativeHeader,
            overview.Advertisements);
        Assert.NotNull(overview.ManagedNativeHeaderDirectory);
        Assert.Null(overview.ExportHeaderRelativeVirtualAddress);
        Assert.Equal(ReadyToRunImageInspector.Signature, overview.Signature);
        Assert.True(overview.MajorVersion > 0);
        Assert.NotEmpty(overview.Sections);
        Assert.NotNull(overview.ManifestMetadata);
        Assert.Equal(
            overview.Sections.OrderBy(static section => (uint)section.Type),
            overview.Sections);
    }

    [Fact]
    public void Describe_ManagedNativeImage_PreservesVersionFlagsAndUnknownSections()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: false,
            majorVersion: 99,
            minorVersion: 7,
            flags: (ReadyToRunHeaderFlags)0x8000_0004,
            sections:
            [
                new(ReadyToRunSectionType.CompilerIdentifier, [1, 2]),
                new(ReadyToRunSectionType.ManifestMetadata, "BSJB"u8.ToArray()),
                new((ReadyToRunSectionType)5_000, [], RelativeVirtualAddress: int.MaxValue),
            ]);
        using var peReader = Open(image.Bytes);

        ReadyToRunImageOverview overview =
            Assert.IsType<ReadyToRunImageOverview>(ReadyToRunImageInspector.Describe(peReader));

        Assert.Equal(ReadyToRunImageRole.Standalone, overview.Role);
        Assert.Equal((ushort)99, overview.MajorVersion);
        Assert.Equal((ushort)7, overview.MinorVersion);
        Assert.Equal((ReadyToRunHeaderFlags)0x8000_0004, overview.Flags);
        Assert.Equal((ReadyToRunSectionType)5_000, overview.Sections[^1].Type);
        Assert.Equal(0, overview.Sections[^1].Size);
        Assert.Equal(int.MaxValue, overview.Sections[^1].RelativeVirtualAddress);
        Assert.Equal("BSJB"u8.Length, overview.ManifestMetadata!.Size);
    }

    [Fact]
    public void Describe_ExportOnlyImage_IsCompositeWithoutILLibrary()
    {
        SyntheticImage image = CreateImage(managedNative: false, exported: true);
        using var peReader = Open(image.Bytes);

        ReadyToRunImageOverview overview =
            Assert.IsType<ReadyToRunImageOverview>(ReadyToRunImageInspector.Describe(peReader));

        Assert.Equal(ReadyToRunImageRole.Composite, overview.Role);
        Assert.Equal(ReadyToRunAdvertisement.Export, overview.Advertisements);
        Assert.Null(overview.ManagedNativeHeaderDirectory);
        Assert.Equal(image.HeaderRva, overview.ExportHeaderRelativeVirtualAddress);
    }

    [Fact]
    public void Describe_MatchingDualAdvertisements_PreservesBothFacts()
    {
        SyntheticImage image = CreateImage(managedNative: true, exported: true);
        using var peReader = Open(image.Bytes);

        ReadyToRunImageOverview overview =
            Assert.IsType<ReadyToRunImageOverview>(ReadyToRunImageInspector.Describe(peReader));

        Assert.Equal(ReadyToRunImageRole.Composite, overview.Role);
        Assert.Equal(
            ReadyToRunAdvertisement.ManagedNativeHeader | ReadyToRunAdvertisement.Export,
            overview.Advertisements);
        Assert.Equal(image.HeaderRva, overview.ManagedNativeHeaderDirectory!.Value.RelativeVirtualAddress);
        Assert.Equal(image.HeaderRva, overview.ExportHeaderRelativeVirtualAddress);
    }

    [Fact]
    public void Describe_ConflictingDualAdvertisements_Fails()
    {
        SyntheticImage image = CreateImage(managedNative: true, exported: true);
        WriteUInt32(image.Bytes, image.ExportFunctionOffset, (uint)(image.HeaderRva + 4));
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("does not match", error.Message);
    }

    [Fact]
    public void Describe_ManagedNativeImageWithoutILLibrary_RemainsInspectable()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: false,
            setIlLibrary: false);
        using var peReader = Open(image.Bytes);

        ReadyToRunImageOverview overview =
            Assert.IsType<ReadyToRunImageOverview>(ReadyToRunImageInspector.Describe(peReader));

        Assert.Equal(ReadyToRunImageRole.Standalone, overview.Role);
        Assert.Equal(ReadyToRunAdvertisement.ManagedNativeHeader, overview.Advertisements);
    }

    [Fact]
    public void Describe_NonR2RManagedNativeHeader_ReturnsNull()
    {
        SyntheticImage image = CreateImage(managedNative: true, exported: false);
        WriteUInt32(image.Bytes, image.HeaderOffset, 0xDEADBEEF);
        using var peReader = Open(image.Bytes);

        Assert.Null(ReadyToRunImageInspector.Describe(peReader));
    }

    [Fact]
    public void Describe_NonR2RManagedNativeHeader_DoesNotValidateUnrelatedDeclaredExtent()
    {
        SyntheticImage image = CreateImage(managedNative: true, exported: false);
        WriteUInt32(image.Bytes, image.HeaderOffset, 0xDEADBEEF);
        WriteUInt32(image.Bytes, image.ManagedNativeDirectoryOffset + 4, int.MaxValue);
        using var peReader = Open(image.Bytes);

        Assert.Null(ReadyToRunImageInspector.Describe(peReader));
    }

    [Fact]
    public void Describe_InvalidExportedSignature_Fails()
    {
        SyntheticImage image = CreateImage(managedNative: false, exported: true);
        WriteUInt32(image.Bytes, image.HeaderOffset, 0xDEADBEEF);
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("signature", error.Message);
    }

    [Fact]
    public void Describe_ComponentHeader_ReportsCompositeComponentRole()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: false,
            flags: ReadyToRunHeaderFlags.SkipTypeValidation |
                ReadyToRunHeaderFlags.NonSharedPInvokeStubs |
                ReadyToRunHeaderFlags.Component |
                ReadyToRunHeaderFlags.MultiModuleVersionBubble,
            sections:
            [
                new(ReadyToRunSectionType.OwnerCompositeExecutable, "owner.r2r.dll\0"u8.ToArray()),
            ]);
        using var peReader = Open(image.Bytes);

        ReadyToRunImageOverview overview =
            Assert.IsType<ReadyToRunImageOverview>(ReadyToRunImageInspector.Describe(peReader));

        Assert.Equal(ReadyToRunImageRole.Component, overview.Role);
        Assert.Equal(
            ReadyToRunSectionType.OwnerCompositeExecutable,
            Assert.Single(overview.Sections).Type);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Describe_EitherComponentEvidence_ReportsComponentRole(bool useFlag)
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: false,
            flags: useFlag ? ReadyToRunHeaderFlags.Component : ReadyToRunHeaderFlags.None,
            sections: useFlag
                ? [new(ReadyToRunSectionType.CompilerIdentifier, [1])]
                : [new(ReadyToRunSectionType.OwnerCompositeExecutable, "owner.r2r.dll\0"u8.ToArray())]);
        using var peReader = Open(image.Bytes);

        ReadyToRunImageOverview overview =
            Assert.IsType<ReadyToRunImageOverview>(ReadyToRunImageInspector.Describe(peReader));

        Assert.Equal(ReadyToRunImageRole.Component, overview.Role);
    }

    [Fact]
    public void Describe_ComponentAssembliesSection_ReportsCompositeRole()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: false,
            sections:
            [
                new(ReadyToRunSectionType.ComponentAssemblies, [1, 2, 3, 4]),
            ]);
        using var peReader = Open(image.Bytes);

        ReadyToRunImageOverview overview =
            Assert.IsType<ReadyToRunImageOverview>(ReadyToRunImageInspector.Describe(peReader));

        Assert.Equal(ReadyToRunImageRole.Composite, overview.Role);
    }

    [Fact]
    public void Describe_ConflictingRoleEvidence_RemainsInspectableAsAmbiguous()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: true,
            flags: ReadyToRunHeaderFlags.Component,
            sections:
            [
                new(ReadyToRunSectionType.OwnerCompositeExecutable, "owner.r2r.dll\0"u8.ToArray()),
            ]);
        using var peReader = Open(image.Bytes);

        ReadyToRunImageOverview overview =
            Assert.IsType<ReadyToRunImageOverview>(ReadyToRunImageInspector.Describe(peReader));

        Assert.Equal(ReadyToRunImageRole.Ambiguous, overview.Role);
        Assert.Equal(
            ReadyToRunAdvertisement.ManagedNativeHeader | ReadyToRunAdvertisement.Export,
            overview.Advertisements);
    }

    [Fact]
    public void Describe_ManifestMetadata_ReportsCliDirectoryAliasing()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: true,
            manifestAliasesCliMetadata: true);
        using var peReader = Open(image.Bytes);

        ReadyToRunImageOverview overview =
            Assert.IsType<ReadyToRunImageOverview>(ReadyToRunImageInspector.Describe(peReader));

        Assert.True(overview.ManifestMetadata!.AliasesCliMetadataDirectory);
    }

    [Fact]
    public void Describe_SectionCountAboveBudget_FailsBeforeTraversal()
    {
        SyntheticImage image = CreateImage(managedNative: true, exported: false);
        WriteUInt32(
            image.Bytes,
            image.HeaderOffset + 12,
            ReadyToRunImageInspector.MaxSectionCount + 1u);
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("inspection bound", error.Message);
    }

    [Fact]
    public void Describe_HeaderLargerThanManagedNativeDirectory_Fails()
    {
        SyntheticImage image = CreateImage(managedNative: true, exported: false);
        WriteUInt32(image.Bytes, image.ManagedNativeDirectoryOffset + 4, 16);
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("requires", error.Message);
    }

    [Fact]
    public void Describe_DuplicateOrDescendingSectionType_Fails()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: false,
            sections:
            [
                new(ReadyToRunSectionType.CompilerIdentifier, [1]),
                new(ReadyToRunSectionType.ManifestMetadata, "BSJB"u8.ToArray()),
            ]);
        WriteUInt32(
            image.Bytes,
            image.HeaderOffset + ReadyToRunImageInspector.FixedHeaderSize +
                ReadyToRunImageInspector.SectionEntrySize,
            (uint)ReadyToRunSectionType.CompilerIdentifier);
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("strictly greater", error.Message);
    }

    [Fact]
    public void Describe_NonEmptySectionOutsideRawImage_Fails()
    {
        SyntheticImage image = CreateImage(managedNative: true, exported: false);
        int firstSection = image.HeaderOffset + ReadyToRunImageInspector.FixedHeaderSize;
        WriteUInt32(image.Bytes, firstSection + 4, int.MaxValue);
        WriteUInt32(image.Bytes, firstSection + 8, 4);
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("does not belong", error.Message);
    }

    [Fact]
    public void Describe_ExportNameCountAboveBudget_Fails()
    {
        SyntheticImage image = CreateImage(managedNative: false, exported: true);
        WriteUInt32(
            image.Bytes,
            image.ExportDirectoryOffset + 24,
            ReadyToRunImageInspector.MaxExportNameCount + 1u);
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("export directory declares", error.Message);
    }

    [Fact]
    public void Describe_InvalidExportOrdinal_Fails()
    {
        SyntheticImage image = CreateImage(managedNative: false, exported: true);
        WriteUInt16(image.Bytes, image.ExportOrdinalOffset, 1);
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("address-table index", error.Message);
    }

    [Fact]
    public void Describe_OverflowingExportAddressEntry_Fails()
    {
        SyntheticImage image = CreateImage(managedNative: false, exported: true);
        WriteUInt32(image.Bytes, image.ExportDirectoryOffset + 20, 2);
        WriteUInt32(image.Bytes, image.ExportDirectoryOffset + 28, (uint)int.MaxValue - 1);
        WriteUInt16(image.Bytes, image.ExportOrdinalOffset, 1);
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("entry RVA exceeds", error.Message);
    }

    [Fact]
    public void Describe_ForwardedHeaderExport_Fails()
    {
        SyntheticImage image = CreateImage(managedNative: false, exported: true);
        WriteUInt32(
            image.Bytes,
            image.ExportFunctionOffset,
            (uint)(image.ExportDirectoryRva + 4));
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("forwarded export", error.Message);
    }

    [Fact]
    public void Describe_UnrelatedExport_ReturnsNull()
    {
        SyntheticImage image = CreateImage(
            managedNative: false,
            exported: true,
            exportName: "OTHER");
        using var peReader = Open(image.Bytes);

        Assert.Null(ReadyToRunImageInspector.Describe(peReader));
    }

    [Fact]
    public void Describe_HalfPopulatedExportDirectory_Fails()
    {
        SyntheticImage image = CreateImage(managedNative: false, exported: true);
        WriteUInt32(image.Bytes, image.ExportDataDirectoryOffset + 4, 0);
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("partially populated", error.Message);
    }

    [Fact]
    public void Describe_HalfPopulatedManagedNativeDirectory_Fails()
    {
        SyntheticImage image = CreateImage(managedNative: true, exported: false);
        WriteUInt32(image.Bytes, image.ManagedNativeDirectoryOffset + 4, 0);
        using var peReader = Open(image.Bytes);

        BadImageFormatException error = Assert.Throws<BadImageFormatException>(
            () => ReadyToRunImageInspector.Describe(peReader));

        Assert.Contains("partially populated", error.Message);
    }

    [Fact]
    public void MetadataRoots_UnadvertisedImage_ContainsOnlyCliMetadata()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);

        MetadataRootInfo root = Assert.Single(session.MetadataRoots());

        Assert.Equal([MetadataRootSource.Cli], root.Sources);
        Assert.Null(session.MetadataImage(MetadataRootSource.ReadyToRunManifest));
    }

    [Fact]
    public void MetadataRoots_CoffMetadataRetainsLegacyProjectionWithoutInventedSource()
    {
        byte[] image = BuildCoffMetadataImage(ReadSelfMetadataRoot());
        using var session = OpenSession(image);

        MetadataImageOverview overview = Assert.IsType<MetadataImageOverview>(
            session.MetadataImage());

        Assert.Equal(60, overview.MetadataOffset);
        Assert.NotEmpty(session.MetadataTables().Tables);
        Assert.Empty(session.MetadataRoots());
        Assert.Null(session.MetadataImage(MetadataRootSource.Cli));
        Assert.Null(session.MetadataTables(MetadataRootSource.Cli));
        Assert.Null(session.MetadataImage(MetadataRootSource.ReadyToRunManifest));
        Assert.Null(session.MetadataTables(MetadataRootSource.ReadyToRunManifest));
    }

    [Fact]
    public void MetadataRoots_AbsentReferenceSourceStillValidatesArguments()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.MetadataReferences(
                MetadataRootSource.ReadyToRunManifest,
                TableIndex.TypeDef,
                0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.MetadataReferences(
                MetadataRootSource.ReadyToRunManifest,
                TableIndex.TypeDef,
                1,
                -1));
    }

    [Fact]
    public void MetadataRoots_CurrentCoreLibrary_OpensDistinctManifestMetadata()
    {
        using var session = AssemblyInspectionSession.Open(typeof(object).Assembly.Location);

        MetadataRootInfo[] roots = session.MetadataRoots().ToArray();
        MetadataRootInfo cli = roots.Single(
            static root => root.Sources.SequenceEqual([MetadataRootSource.Cli]));
        MetadataRootInfo manifest = roots.Single(
            static root => root.Sources.SequenceEqual([MetadataRootSource.ReadyToRunManifest]));
        MetadataRootValue<MetadataImageOverview> value = Assert.IsType<
            MetadataRootValue<MetadataImageOverview>>(
            session.MetadataImage(MetadataRootSource.ReadyToRunManifest));

        Assert.NotEqual(cli.RelativeVirtualAddress, manifest.RelativeVirtualAddress);
        Assert.Equal(manifest, value.Root);
        Assert.Equal(manifest.Size, value.Value.MetadataSize);
        Assert.True(value.Value.IsAssembly);
        Assert.Equal(
            1,
            value.Value.Tables.Single(
                static table => table.Index == TableIndex.TypeDef).RowCount);
    }

    [Fact]
    public void MetadataRoots_DistinctManifest_ProjectsEveryMetadataFacet()
    {
        byte[] metadata = ReadSelfMetadataRoot();
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: false,
            sections:
            [
                new(ReadyToRunSectionType.CompilerIdentifier, [1]),
                new(ReadyToRunSectionType.ManifestMetadata, metadata),
            ]);
        using var peReader = Open(image.Bytes);
        ReadyToRunSectionSummary manifestSection =
            Assert.IsType<ReadyToRunImageOverview>(
                ReadyToRunImageInspector.Describe(peReader))
            .ManifestMetadata!;
        int expectedOffset = ImageOffset(peReader, manifestSection.RelativeVirtualAddress);
        using var session = OpenSession(image.Bytes);

        MetadataRootInfo[] roots = session.MetadataRoots().ToArray();
        Assert.Equal(2, roots.Length);
        Assert.Equal([MetadataRootSource.Cli], roots[0].Sources);
        Assert.Equal([MetadataRootSource.ReadyToRunManifest], roots[1].Sources);

        MetadataRootValue<MetadataImageOverview> overview = Assert.IsType<
            MetadataRootValue<MetadataImageOverview>>(
            session.MetadataImage(MetadataRootSource.ReadyToRunManifest));
        MetadataRootValue<MetadataTableProjection> tables = Assert.IsType<
            MetadataRootValue<MetadataTableProjection>>(
            session.MetadataTables(MetadataRootSource.ReadyToRunManifest));
        MetadataRootValue<MetadataTableView> row = Assert.IsType<
            MetadataRootValue<MetadataTableView>>(
            session.MetadataTableRow(
                MetadataRootSource.ReadyToRunManifest,
                TableIndex.TypeDef,
                1));
        MetadataRootValue<MetadataRowReferenceSet> references = Assert.IsType<
            MetadataRootValue<MetadataRowReferenceSet>>(
            session.MetadataReferences(
                MetadataRootSource.ReadyToRunManifest,
                TableIndex.TypeDef,
                1));
        MetadataRootValue<MetadataValue> heapValue = Assert.IsType<
            MetadataRootValue<MetadataValue>>(
            session.MetadataHeapValue(
                MetadataRootSource.ReadyToRunManifest,
                HeapKind.String,
                0));
        MetadataRootValue<MetadataHeapEntrySet> heapEntries = Assert.IsType<
            MetadataRootValue<MetadataHeapEntrySet>>(
            session.MetadataHeapEntries(
                MetadataRootSource.ReadyToRunManifest,
                HeapKind.Guid));

        MetadataRootInfo manifest = roots[1];
        Assert.Equal(manifest, overview.Root);
        Assert.Equal(manifest, tables.Root);
        Assert.Equal(manifest, row.Root);
        Assert.Equal(manifest, references.Root);
        Assert.Equal(manifest, heapValue.Root);
        Assert.Equal(manifest, heapEntries.Root);
        Assert.Equal(expectedOffset, overview.Value.MetadataOffset);
        Assert.Equal(metadata.Length, overview.Value.MetadataSize);
        Assert.Contains(
            tables.Value.Tables,
            static table => table.Index == TableIndex.TypeDef);
        Assert.Equal(1, row.Value.Rows[0].RowId);
        Assert.IsType<MetadataValue.Nil>(heapValue.Value);
    }

    [Fact]
    public void MetadataRoots_AliasedManifest_IsOnePhysicalRoot()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: true,
            manifestAliasesCliMetadata: true);
        using var session = OpenSession(image.Bytes);

        MetadataRootInfo root = Assert.Single(session.MetadataRoots());
        MetadataRootValue<MetadataImageOverview> cli = Assert.IsType<
            MetadataRootValue<MetadataImageOverview>>(
            session.MetadataImage(MetadataRootSource.Cli));
        MetadataRootValue<MetadataImageOverview> manifest = Assert.IsType<
            MetadataRootValue<MetadataImageOverview>>(
            session.MetadataImage(MetadataRootSource.ReadyToRunManifest));

        Assert.True(root.IsAliased);
        Assert.Equal(
            [MetadataRootSource.Cli, MetadataRootSource.ReadyToRunManifest],
            root.Sources);
        Assert.Same(root, cli.Root);
        Assert.Same(cli.Root, manifest.Root);
        Assert.Equal(cli.Value.MetadataOffset, manifest.Value.MetadataOffset);
        Assert.Equal(cli.Value.MetadataSize, manifest.Value.MetadataSize);
        Assert.Equal(cli.Value.MetadataVersion, manifest.Value.MetadataVersion);
        Assert.Equal(cli.Value.Tables, manifest.Value.Tables);
    }

    [Fact]
    public void MetadataRoots_MalformedAliasedManifest_FailsWithSelectedRootProvenance()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: true,
            manifestAliasesCliMetadata: true,
            sourceImage: MetadataImageOverviewTests.SelfWithCorruptTableStream());
        using var session = OpenSession(image.Bytes);

        Assert.True(Assert.Single(session.MetadataRoots()).IsAliased);
        BadImageFormatException cliError = Assert.IsAssignableFrom<BadImageFormatException>(
            Record.Exception(
                () => session.MetadataImage(MetadataRootSource.Cli)));

        MalformedMetadataRootException imageError = Assert.Throws<
            MalformedMetadataRootException>(
            () => session.MetadataImage(
                MetadataRootSource.ReadyToRunManifest));
        MalformedMetadataRootException tableError = Assert.Throws<
            MalformedMetadataRootException>(
            () => session.MetadataTables(
                MetadataRootSource.ReadyToRunManifest));

        Assert.Equal(
            MetadataRootMalformedReason.UnreadableMetadataStructure,
            imageError.Reason);
        Assert.Equal(
            MetadataRootSource.ReadyToRunManifest,
            imageError.RootSource);
        Assert.Same(cliError, imageError.InnerException);
        Assert.Equal(imageError.Reason, tableError.Reason);
        Assert.Equal(imageError.RootSource, tableError.RootSource);
        Assert.Same(cliError, tableError.InnerException);
    }

    [Fact]
    public void MetadataRoots_MalformedManifest_FailsWithRootProvenance()
    {
        SyntheticImage image = CreateImage(managedNative: true, exported: false);
        using var session = OpenSession(image.Bytes);

        Assert.Equal(2, session.MetadataRoots().Length);
        Assert.NotEmpty(session.MetadataTables().Tables);

        var error = Assert.Throws<MalformedMetadataRootException>(
            () => session.MetadataImage(MetadataRootSource.ReadyToRunManifest));

        Assert.Equal(
            MetadataRootMalformedReason.TruncatedFixedPrefix,
            error.Reason);
        Assert.Equal(
            MetadataRootSource.ReadyToRunManifest,
            error.RootSource);
        Assert.NotEmpty(session.MetadataTables().Tables);
    }

    [Fact]
    public void MetadataRoots_MalformedReadyToRun_DoesNotBlockCliMetadata()
    {
        SyntheticImage image = CreateImage(managedNative: true, exported: false);
        int firstSection =
            image.HeaderOffset + ReadyToRunImageInspector.FixedHeaderSize;
        WriteUInt32(image.Bytes, firstSection + 4, int.MaxValue);
        using var session = OpenSession(image.Bytes);

        Assert.NotEmpty(session.MetadataTables().Tables);
        Assert.Throws<BadImageFormatException>(() => session.MetadataRoots());
        Assert.NotEmpty(session.MetadataTables().Tables);
    }

    [Fact]
    public void MetadataRoots_ManifestWithoutCliMetadata_RemainsInspectable()
    {
        byte[] metadata = ReadSelfMetadataRoot();
        SyntheticImage image = CreateImage(
            managedNative: false,
            exported: true,
            setIlLibrary: false,
            sections:
            [
                new(ReadyToRunSectionType.CompilerIdentifier, [1]),
                new(ReadyToRunSectionType.ManifestMetadata, metadata),
            ]);
        using (var peReader = Open(image.Bytes))
        {
            PEHeader peHeader = peReader.PEHeaders.PEHeader!;
            int directoryBase =
                peReader.PEHeaders.PEHeaderStartOffset
                + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
            image.Bytes.AsSpan(directoryBase + (14 * 8), 8).Clear();
        }
        using var session = OpenSession(image.Bytes);

        MetadataRootInfo root = Assert.Single(session.MetadataRoots());
        MetadataRootValue<MetadataImageOverview> manifest = Assert.IsType<
            MetadataRootValue<MetadataImageOverview>>(
            session.MetadataImage(MetadataRootSource.ReadyToRunManifest));

        Assert.Equal([MetadataRootSource.ReadyToRunManifest], root.Sources);
        Assert.Equal(root, manifest.Root);
        Assert.Null(session.MetadataImage());
    }

    [Fact]
    public void MetadataRoots_ManifestBeyondVirtualExtent_FailsBeforeCopy()
    {
        byte[] metadata = ReadSelfMetadataRoot();
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: false,
            sections:
            [
                new(ReadyToRunSectionType.CompilerIdentifier, [1]),
                new(ReadyToRunSectionType.ManifestMetadata, metadata),
            ]);
        using (var peReader = Open(image.Bytes))
        {
            ReadyToRunSectionSummary manifest =
                Assert.IsType<ReadyToRunImageOverview>(
                    ReadyToRunImageInspector.Describe(peReader))
                .ManifestMetadata!;
            int sectionIndex = peReader.PEHeaders.GetContainingSectionIndex(
                manifest.RelativeVirtualAddress);
            SectionHeader section = peReader.PEHeaders.SectionHeaders[sectionIndex];
            int sectionHeadersOffset =
                peReader.PEHeaders.PEHeaderStartOffset
                + peReader.PEHeaders.CoffHeader.SizeOfOptionalHeader;
            int sectionHeaderOffset = sectionHeadersOffset + (sectionIndex * 40);
            uint virtualSize = checked(
                (uint)(manifest.RelativeVirtualAddress - section.VirtualAddress + 1));
            WriteUInt32(image.Bytes, sectionHeaderOffset + 8, virtualSize);
        }
        using var session = OpenSession(image.Bytes);

        var error = Assert.Throws<MalformedMetadataRootException>(
            () => session.MetadataImage(MetadataRootSource.ReadyToRunManifest));

        Assert.Equal(
            MetadataRootMalformedReason.UnmappableMetadataExtent,
            error.Reason);
    }

    [Fact]
    public void MetadataRoots_FailAfterSessionDisposal()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: true,
            manifestAliasesCliMetadata: true);
        var session = OpenSession(image.Bytes);
        _ = session.MetadataRoots();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => session.MetadataImage(MetadataRootSource.ReadyToRunManifest));
    }

    [Fact]
    public void MetadataRoots_DistinctManifestProviderIsDisposedWithSession()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: false,
            sections:
            [
                new(ReadyToRunSectionType.CompilerIdentifier, [1]),
                new(ReadyToRunSectionType.ManifestMetadata, ReadSelfMetadataRoot()),
            ]);
        var session = OpenSession(image.Bytes);
        _ = session.MetadataImage(MetadataRootSource.ReadyToRunManifest);
        MetadataReaderProvider provider = GetOwnedManifestProvider(session);

        _ = provider.GetMetadataReader();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.GetMetadataReader());
    }

    [Fact]
    public void MetadataRoots_FaultedManifestProviderIsDisposedWithSession()
    {
        SyntheticImage image = CreateImage(
            managedNative: true,
            exported: false,
            sections:
            [
                new(ReadyToRunSectionType.CompilerIdentifier, [1]),
                new(
                    ReadyToRunSectionType.ManifestMetadata,
                    ReadCorruptMetadataRoot()),
            ]);
        var session = OpenSession(image.Bytes);

        MalformedMetadataRootException error = Assert.Throws<
            MalformedMetadataRootException>(
            () => session.MetadataImage(
                MetadataRootSource.ReadyToRunManifest));
        Assert.Equal(
            MetadataRootMalformedReason.UnreadableMetadataStructure,
            error.Reason);
        Assert.Equal(
            MetadataRootSource.ReadyToRunManifest,
            error.RootSource);
        Assert.IsType<BadImageFormatException>(error.InnerException);
        MetadataReaderProvider provider = GetOwnedManifestProvider(session);
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.GetMetadataReader());
    }

    static PEReader Open(byte[] bytes) => new(new MemoryStream(bytes));

    static AssemblyInspectionSession OpenSession(byte[] bytes)
        => AssemblyInspectionSession.OpenPrefetched(new MemoryStream(bytes));

    static byte[] ReadSelfMetadataRoot()
    {
        using var peReader = Open(File.ReadAllBytes(SelfPath));
        return peReader.GetMetadata().GetContent().ToArray();
    }

    static byte[] ReadCorruptMetadataRoot()
    {
        using var peReader = Open(
            MetadataImageOverviewTests.SelfWithCorruptTableStream());
        return peReader.GetMetadata().GetContent().ToArray();
    }

    static byte[] BuildCoffMetadataImage(byte[] metadata)
    {
        const int CoffHeaderSize = 20;
        const int SectionHeaderSize = 40;
        int metadataOffset = CoffHeaderSize + SectionHeaderSize;
        byte[] image = new byte[metadataOffset + metadata.Length];

        WriteUInt16(image, 0, 0x8664);
        WriteUInt16(image, 2, 1);
        ".cormeta"u8.CopyTo(image.AsSpan(CoffHeaderSize, 8));
        WriteUInt32(image, CoffHeaderSize + 16, (uint)metadata.Length);
        WriteUInt32(image, CoffHeaderSize + 20, (uint)metadataOffset);
        WriteUInt32(image, CoffHeaderSize + 36, 0x40000040);
        metadata.CopyTo(image, metadataOffset);

        return image;
    }

    static MetadataReaderProvider GetOwnedManifestProvider(
        AssemblyInspectionSession session)
    {
        var roots = Assert.IsType<Lazy<MetadataRootCatalog>>(
            typeof(AssemblyInspectionSession)
                .GetField(
                    "_metadataRoots",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(session));
        MetadataRootReader reader = Assert.IsType<MetadataRootReader>(
            typeof(MetadataRootCatalog)
                .GetField(
                    "_createdManifestReader",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(roots.Value));
        return Assert.IsType<MetadataReaderProvider>(
            typeof(MetadataRootReader)
                .GetField(
                    "_provider",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(reader));
    }

    static int ImageOffset(PEReader peReader, int relativeVirtualAddress)
    {
        SectionHeader section = peReader.PEHeaders.SectionHeaders[
            peReader.PEHeaders.GetContainingSectionIndex(relativeVirtualAddress)];
        return checked(
            section.PointerToRawData
            + relativeVirtualAddress
            - section.VirtualAddress);
    }

    static SyntheticImage CreateImage(
        bool managedNative,
        bool exported,
        bool setIlLibrary = true,
        ushort majorVersion = 25,
        ushort minorVersion = 0,
        ReadyToRunHeaderFlags flags = ReadyToRunHeaderFlags.Partial,
        SectionSpec[]? sections = null,
        string exportName = "RTR_HEADER",
        bool manifestAliasesCliMetadata = false,
        byte[]? sourceImage = null)
    {
        sections ??=
        [
            new(ReadyToRunSectionType.CompilerIdentifier, [1, 2, 3]),
            new(ReadyToRunSectionType.ManifestMetadata, "BSJB"u8.ToArray()),
        ];

        byte[] bytes = sourceImage?.ToArray() ?? File.ReadAllBytes(SelfPath);
        using var original = Open(bytes);
        PEHeaders headers = original.PEHeaders;
        PEHeader peHeader = headers.PEHeader!;

        int lastSectionIndex = headers.SectionHeaders
            .Select(static (section, index) => (Section: section, Index: index))
            .OrderBy(static item => item.Section.VirtualAddress)
            .Last()
            .Index;
        SectionHeader section = headers.SectionHeaders[lastSectionIndex];

        int sectionHeadersOffset = headers.PEHeaderStartOffset + headers.CoffHeader.SizeOfOptionalHeader;
        int sectionHeaderOffset = sectionHeadersOffset + (lastSectionIndex * 40);
        int exportDataDirectoryOffset =
            headers.PEHeaderStartOffset + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        int managedNativeDirectoryOffset = headers.CorHeaderStartOffset + 64;

        Array.Clear(bytes, exportDataDirectoryOffset, 8);
        Array.Clear(bytes, managedNativeDirectoryOffset, 8);

        if (setIlLibrary)
        {
            uint corFlags = ReadUInt32(bytes, headers.CorHeaderStartOffset + 16);
            WriteUInt32(
                bytes,
                headers.CorHeaderStartOffset + 16,
                corFlags | (uint)CorFlags.ILLibrary);
        }
        else
        {
            uint corFlags = ReadUInt32(bytes, headers.CorHeaderStartOffset + 16);
            WriteUInt32(
                bytes,
                headers.CorHeaderStartOffset + 16,
                corFlags & ~(uint)CorFlags.ILLibrary);
        }

        int payloadOffset = Align(Math.Max(bytes.Length, section.PointerToRawData + section.SizeOfRawData), 4);
        int exportPrefixSize = exported ? Align(40 + 4 + 4 + 2 + exportName.Length + 1, 4) : 0;
        int headerOffset = payloadOffset + exportPrefixSize;
        int headerSize =
            ReadyToRunImageInspector.FixedHeaderSize +
            (sections.Length * ReadyToRunImageInspector.SectionEntrySize);
        int contentOffset = Align(headerOffset + headerSize, 4);

        var sectionOffsets = new int[sections.Length];
        int cursor = contentOffset;
        for (int i = 0; i < sections.Length; i++)
        {
            sectionOffsets[i] = cursor;
            cursor = Align(cursor + sections[i].Content.Length, 4);
        }

        int rawEnd = cursor;
        int rawSize = Align(rawEnd - section.PointerToRawData, peHeader.FileAlignment);
        int imageLength = Math.Max(bytes.Length, section.PointerToRawData + rawSize);
        Array.Resize(ref bytes, imageLength);

        int virtualSize = Math.Max(section.VirtualSize, rawEnd - section.PointerToRawData);
        WriteUInt32(bytes, sectionHeaderOffset + 8, (uint)virtualSize);
        WriteUInt32(bytes, sectionHeaderOffset + 16, (uint)rawSize);
        WriteUInt32(
            bytes,
            headers.PEHeaderStartOffset + 56,
            (uint)Align(section.VirtualAddress + virtualSize, peHeader.SectionAlignment));

        int headerRva = ToRva(section, headerOffset);
        int exportDirectoryOffset = payloadOffset;
        int exportDirectoryRva = ToRva(section, exportDirectoryOffset);
        int exportFunctionOffset = exportDirectoryOffset + 40;
        int exportNamesOffset = exportFunctionOffset + 4;
        int exportOrdinalOffset = exportNamesOffset + 4;
        int exportNameOffset = exportOrdinalOffset + 2;

        if (exported)
        {
            WriteUInt32(bytes, exportDataDirectoryOffset, (uint)exportDirectoryRva);
            WriteUInt32(bytes, exportDataDirectoryOffset + 4, 40);

            WriteUInt32(bytes, exportDirectoryOffset + 20, 1);
            WriteUInt32(bytes, exportDirectoryOffset + 24, 1);
            WriteUInt32(bytes, exportDirectoryOffset + 28, (uint)ToRva(section, exportFunctionOffset));
            WriteUInt32(bytes, exportDirectoryOffset + 32, (uint)ToRva(section, exportNamesOffset));
            WriteUInt32(bytes, exportDirectoryOffset + 36, (uint)ToRva(section, exportOrdinalOffset));
            WriteUInt32(bytes, exportFunctionOffset, (uint)headerRva);
            WriteUInt32(bytes, exportNamesOffset, (uint)ToRva(section, exportNameOffset));
            WriteUInt16(bytes, exportOrdinalOffset, 0);
            System.Text.Encoding.ASCII.GetBytes(exportName, bytes.AsSpan(exportNameOffset));
            bytes[exportNameOffset + exportName.Length] = 0;
        }

        WriteUInt32(bytes, headerOffset, ReadyToRunImageInspector.Signature);
        WriteUInt16(bytes, headerOffset + 4, majorVersion);
        WriteUInt16(bytes, headerOffset + 6, minorVersion);
        WriteUInt32(bytes, headerOffset + 8, (uint)flags);
        WriteUInt32(bytes, headerOffset + 12, (uint)sections.Length);

        for (int i = 0; i < sections.Length; i++)
        {
            SectionSpec spec = sections[i];
            int entryOffset =
                headerOffset +
                ReadyToRunImageInspector.FixedHeaderSize +
                (i * ReadyToRunImageInspector.SectionEntrySize);
            bool aliasesCliMetadata =
                manifestAliasesCliMetadata &&
                spec.Type == ReadyToRunSectionType.ManifestMetadata;
            int sectionRva = aliasesCliMetadata
                ? headers.CorHeader!.MetadataDirectory.RelativeVirtualAddress
                : spec.RelativeVirtualAddress ?? ToRva(section, sectionOffsets[i]);
            int sectionSize = aliasesCliMetadata
                ? headers.CorHeader!.MetadataDirectory.Size
                : spec.Content.Length;

            WriteUInt32(bytes, entryOffset, (uint)spec.Type);
            WriteUInt32(bytes, entryOffset + 4, (uint)sectionRva);
            WriteUInt32(bytes, entryOffset + 8, (uint)sectionSize);
            if (!aliasesCliMetadata)
                spec.Content.CopyTo(bytes, sectionOffsets[i]);
        }

        if (managedNative)
        {
            WriteUInt32(bytes, managedNativeDirectoryOffset, (uint)headerRva);
            WriteUInt32(bytes, managedNativeDirectoryOffset + 4, (uint)headerSize);
        }

        return new SyntheticImage(
            bytes,
            headerOffset,
            headerRva,
            managedNativeDirectoryOffset,
            exportDataDirectoryOffset,
            exportDirectoryOffset,
            exportDirectoryRva,
            exportFunctionOffset,
            exportOrdinalOffset);
    }

    static int ToRva(SectionHeader section, int imageOffset)
        => checked(section.VirtualAddress + imageOffset - section.PointerToRawData);

    static int Align(int value, int alignment)
        => checked((value + alignment - 1) / alignment * alignment);

    static uint ReadUInt32(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));

    static void WriteUInt16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)), value);

    static void WriteUInt32(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);

    sealed record SyntheticImage(
        byte[] Bytes,
        int HeaderOffset,
        int HeaderRva,
        int ManagedNativeDirectoryOffset,
        int ExportDataDirectoryOffset,
        int ExportDirectoryOffset,
        int ExportDirectoryRva,
        int ExportFunctionOffset,
        int ExportOrdinalOffset);

    readonly record struct SectionSpec(
        ReadyToRunSectionType Type,
        byte[] Content,
        int? RelativeVirtualAddress = null);
}
