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

    static byte[] BuildImage(string metadataVersion)
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

    static void RemoveMetadataDirectory(byte[] image)
    {
        using var peReader = Open(image);
        PEHeader peHeader = peReader.PEHeaders.PEHeader!;
        int directoryBase =
            peReader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        image.AsSpan(directoryBase + (14 * 8), 8).Clear();
    }

    static void TruncateMetadataAfterVersionField(byte[] image)
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
