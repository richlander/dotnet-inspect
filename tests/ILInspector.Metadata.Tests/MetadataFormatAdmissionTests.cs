using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataFormatAdmissionTests
{
    [Fact]
    public void AdmitImageRejectsNullReader()
        => Assert.Throws<ArgumentNullException>(
            () => MetadataFormatAdmission.AdmitImage(null!));

    [Fact]
    public void SupportedEcma335IsAdmitted()
    {
        using var peReader = Open(BuildImage("v4.0.30319"));

        Assert.True(MetadataFormatAdmission.AdmitImage(peReader));
    }

    [Fact]
    public void ImageWithoutMetadataIsNotAdmittedAndDoesNotThrow()
    {
        byte[] image = BuildImage("v4.0.30319");
        RemoveMetadataDirectory(image);
        using var peReader = Open(image);

        Assert.False(MetadataFormatAdmission.AdmitImage(peReader));
    }

    [Fact]
    public void NativeImageDeclaresNoClrHeader()
    {
        byte[] native = BuildImage("v4.0.30319");
        RemoveMetadataDirectory(native);
        byte[] withoutMetadata = BuildImage("v4.0.30319");
        ClearCorHeaderMetadataDirectory(withoutMetadata);
        byte[] sizeless = BuildImage("v4.0.30319");
        ClearCorHeaderDirectorySize(sizeless);
        using var managedReader = Open(BuildImage("v4.0.30319"));
        using var nativeReader = Open(native);
        using var withoutMetadataReader = Open(withoutMetadata);
        using var sizelessReader = Open(sizeless);
        using var textReader = Open("not a portable executable"u8.ToArray());

        Assert.False(MetadataFormatAdmission.IsNativeImage(managedReader));
        Assert.True(MetadataFormatAdmission.IsNativeImage(nativeReader));
        // A declared CLR header without readable metadata is malformed, not
        // native: admission classifies it, and the PE headers reject it.
        Assert.Throws<MalformedMetadataRootException>(
            () => MetadataFormatAdmission.AdmitImage(withoutMetadataReader));
        Assert.Throws<BadImageFormatException>(
            () => MetadataFormatAdmission.IsNativeImage(withoutMetadataReader));
        Assert.Throws<BadImageFormatException>(
            () => MetadataFormatAdmission.IsNativeImage(sizelessReader));
        Assert.Throws<BadImageFormatException>(
            () => MetadataFormatAdmission.IsNativeImage(textReader));
    }

    [Fact]
    public void WindowsMetadataIsRejectedWithoutLeakingTheMarker()
    {
        byte[] image = BuildImage("WindowsRuntime 1.4;CLR v4.0.30319");
        TruncateMetadataAfterVersionField(image);
        using var peReader = Open(image);

        var error = Assert.Throws<UnsupportedMetadataFormatException>(
            () => MetadataFormatAdmission.AdmitImage(peReader));
        Assert.DoesNotContain(
            "WindowsRuntime",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedRootIsRejectedWithItsTypedReason()
    {
        byte[] image = BuildImage("v4.0.30319");
        BinaryPrimitives.WriteUInt32LittleEndian(
            image.AsSpan(MetadataStart(image), sizeof(uint)),
            0xDEADBEEF);
        using var peReader = Open(image);

        var error = Assert.Throws<MalformedMetadataRootException>(
            () => MetadataFormatAdmission.AdmitImage(peReader));
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            error.Reason);
    }

    [Fact]
    public void GetMetadataReaderReturnsAReaderForSupportedMetadata()
    {
        using var peReader = Open(BuildImage("v4.0.30319"));

        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);

        Assert.Equal(
            "Probe",
            reader.GetString(reader.GetAssemblyDefinition().Name));
    }

    [Fact]
    public void GetMetadataReaderRejectsAnImageWithoutMetadata()
    {
        byte[] image = BuildImage("v4.0.30319");
        RemoveMetadataDirectory(image);
        using var peReader = Open(image);

        var error = Assert.Throws<BadImageFormatException>(
            () => MetadataFormatAdmission.GetMetadataReader(peReader));
        Assert.IsNotType<MalformedMetadataRootException>(error);
    }

    [Fact]
    public void GetMetadataReaderWithOptionsAppliesTheSameAdmission()
    {
        byte[] image = BuildImage("WindowsRuntime 1.4;CLR v4.0.30319");
        TruncateMetadataAfterVersionField(image);
        using var peReader = Open(image);

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => MetadataFormatAdmission.GetMetadataReader(
                peReader,
                MetadataReaderOptions.None));
    }

    static PEReader Open(byte[] image)
        => new(ImmutableArray.Create(image));

    internal static byte[] BuildImage(string metadataVersion)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Probe.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Probe"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                metadataVersion,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    static int MetadataStart(byte[] image)
    {
        using var peReader = Open(image);
        return peReader.PEHeaders.MetadataStartOffset;
    }

    static int CorHeaderStart(byte[] image)
    {
        using var peReader = Open(image);
        return peReader.PEHeaders.CorHeaderStartOffset;
    }

    internal static void RemoveMetadataDirectory(byte[] image)
    {
        using var peReader = Open(image);
        PEHeader peHeader = peReader.PEHeaders.PEHeader!;
        int directoryBase =
            peReader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        image.AsSpan(directoryBase + (14 * 8), 8).Clear();
    }

    static void ClearCorHeaderDirectorySize(byte[] image)
    {
        using var peReader = Open(image);
        PEHeader peHeader = peReader.PEHeaders.PEHeader!;
        int directoryBase =
            peReader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        image.AsSpan(directoryBase + (14 * 8) + 4, 4).Clear();
    }

    static void ClearCorHeaderMetadataDirectory(byte[] image)
        => image.AsSpan(CorHeaderStart(image) + 8, 8).Clear();

    internal static void TruncateMetadataAfterVersionField(byte[] image)
    {
        int metadataStart = MetadataStart(image);
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(CorHeaderStart(image) + 12, sizeof(int)),
            MetadataImageFormatClassifier.FixedPrefixLength
                + versionLength);
    }
}
