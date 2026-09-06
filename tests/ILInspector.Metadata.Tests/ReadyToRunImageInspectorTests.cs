using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
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

    static PEReader Open(byte[] bytes) => new(new MemoryStream(bytes));

    /// <summary>
    /// A thin mapping wrapper over the shared byte builder in
    /// <see cref="ReadyToRunImageFixture"/>. The geometry lives there so the CLI's public-command
    /// tests build the same images without referencing this test executable; only the enum
    /// spelling — which the fixture project deliberately does not know — is applied here.
    /// </summary>
    internal static SyntheticImage CreateImage(
        bool managedNative,
        bool exported,
        bool setIlLibrary = true,
        ushort majorVersion = 25,
        ushort minorVersion = 0,
        ReadyToRunHeaderFlags flags = ReadyToRunHeaderFlags.Partial,
        SectionSpec[]? sections = null,
        string exportName = "RTR_HEADER",
        bool manifestAliasesCliMetadata = false)
    {
        SyntheticReadyToRunSection[]? specs = sections?
            .Select(static section => new SyntheticReadyToRunSection(
                (uint)section.Type, section.Content, section.RelativeVirtualAddress))
            .ToArray();

        SyntheticReadyToRunImage image = ReadyToRunImageFixture.Create(
            SelfPath,
            managedNative,
            exported,
            setIlLibrary,
            majorVersion,
            minorVersion,
            (uint)flags,
            specs,
            exportName,
            manifestAliasesCliMetadata);

        return new SyntheticImage(
            image.Bytes,
            image.HeaderOffset,
            image.HeaderRva,
            image.ManagedNativeDirectoryOffset,
            image.ExportDataDirectoryOffset,
            image.ExportDirectoryOffset,
            image.ExportDirectoryRva,
            image.ExportFunctionOffset,
            image.ExportOrdinalOffset);
    }

    static void WriteUInt16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)), value);

    static void WriteUInt32(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);

    internal sealed record SyntheticImage(
        byte[] Bytes,
        int HeaderOffset,
        int HeaderRva,
        int ManagedNativeDirectoryOffset,
        int ExportDataDirectoryOffset,
        int ExportDirectoryOffset,
        int ExportDirectoryRva,
        int ExportFunctionOffset,
        int ExportOrdinalOffset);

    internal readonly record struct SectionSpec(
        ReadyToRunSectionType Type,
        byte[] Content,
        int? RelativeVirtualAddress = null);
}
