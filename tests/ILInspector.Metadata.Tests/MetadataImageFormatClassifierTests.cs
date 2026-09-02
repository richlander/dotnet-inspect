using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataImageFormatClassifierTests
{

    [Fact]
    public void ClassifyRejectsNullReader()
        => Assert.Throws<ArgumentNullException>(
            () => MetadataImageFormatClassifier.Classify(null!));

    [Fact]
    public void Mdp017_OrdinaryEcma335IsSupported()
    {
        using var peReader = Open(BuildImage("v4.0.30319"));

        Assert.IsType<MetadataImageFormatResult.SupportedEcma335>(
            MetadataImageFormatClassifier.Classify(peReader));
    }

    [Fact]
    public void Mdp017_CompilerProducedAssemblyIsSupported()
    {
        using var stream = File.OpenRead(
            typeof(MetadataImageFormatClassifierTests).Assembly.Location);
        using var peReader = new PEReader(stream);

        Assert.IsType<MetadataImageFormatResult.SupportedEcma335>(
            MetadataImageFormatClassifier.Classify(peReader));
    }

    [Theory]
    [InlineData("WindowsRuntime 1.4;CLR v4.0.30319")]
    [InlineData("prefix WindowsRuntime suffix")]
    public void Mdp017_ExactWindowsRuntimeMarkerIsUnsupported(
        string metadataVersion)
    {
        byte[] image = BuildImage(metadataVersion);
        TruncateMetadataAfterVersionField(image);
        using var peReader = Open(image);

        Assert.Throws<BadImageFormatException>(
            () => peReader.GetMetadataReader());
        Assert.IsType<MetadataImageFormatResult.UnsupportedWindowsMetadata>(
            MetadataImageFormatClassifier.Classify(peReader));
    }


    [Theory]
    [InlineData("windowsRuntime 1.4")]
    [InlineData("WINDOWSRUNTIME 1.4")]
    [InlineData("v4.0.30319 Windowsruntime")]
    public void Mdp017_MarkerComparisonIsOrdinalAndCaseSensitive(
        string metadataVersion)
    {
        using var peReader = Open(BuildImage(metadataVersion));

        Assert.IsType<MetadataImageFormatResult.SupportedEcma335>(
            MetadataImageFormatClassifier.Classify(peReader));
    }

    [Fact]
    public void Mdp017_MarkerAfterFirstNullIsNotExamined()
    {
        byte[] image = BuildImage("v4\0WindowsRuntime");
        using var peReader = Open(image);

        Assert.IsType<MetadataImageFormatResult.SupportedEcma335>(
            MetadataImageFormatClassifier.Classify(peReader));
    }

    [Fact]
    public void Mdp017_MarkerOutsideDeclaredVersionFieldIsNotExamined()
    {
        byte[] image = BuildImage("v4");
        int metadataStart = MetadataStart(image);
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        "WindowsRuntime"u8.CopyTo(
            image.AsSpan(
                metadataStart
                    + MetadataImageFormatClassifier.FixedPrefixLength
                    + versionLength));
        using var peReader = Open(image);

        Assert.IsType<MetadataImageFormatResult.SupportedEcma335>(
            MetadataImageFormatClassifier.Classify(peReader));
    }

    [Fact]
    public void Mdp017_MaximumPaddedVersionLengthIsAccepted()
    {
        byte[] image = BuildImage(
            "v4.0.30319",
            additionalTypeCount: 20);
        int versionStart =
            MetadataStart(image)
            + MetadataImageFormatClassifier.FixedPrefixLength;
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(versionStart - sizeof(int), sizeof(int)),
            MetadataImageFormatClassifier.MaximumPaddedVersionLength);
        Span<byte> version = image.AsSpan(
            versionStart,
            MetadataImageFormatClassifier.MaximumPaddedVersionLength);
        version.Fill((byte)'A');
        version[254] = 0;
        using var peReader = Open(image);

        Assert.IsType<MetadataImageFormatResult.SupportedEcma335>(
            MetadataImageFormatClassifier.Classify(peReader));
    }

    [Fact]
    public void Mdp017_PaddedByteCannotTerminateMaximumVersionString()
    {
        byte[] image = BuildImage(
            "v4.0.30319",
            additionalTypeCount: 20);
        int versionStart =
            MetadataStart(image)
            + MetadataImageFormatClassifier.FixedPrefixLength;
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(versionStart - sizeof(int), sizeof(int)),
            MetadataImageFormatClassifier.MaximumPaddedVersionLength);
        Span<byte> version = image.AsSpan(
            versionStart,
            MetadataImageFormatClassifier.MaximumPaddedVersionLength);
        version.Fill((byte)'A');
        version[MetadataImageFormatClassifier.MaximumVersionStringLength] = 0;
        using var peReader = Open(image);

        AssertMalformed(
            peReader,
            MetadataRootMalformedReason.MissingVersionTerminator);
    }

    [Fact]
    public void Mdp017_NoMetadataDoesNotRequestAMetadataBlock()
    {
        byte[] image = BuildImage("v4.0.30319");
        using (var intact = Open(image))
        {
            PEHeader peHeader = intact.PEHeaders.PEHeader!;
            int directoryBase =
                intact.PEHeaders.PEHeaderStartOffset
                + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
            image.AsSpan(directoryBase + (14 * 8), 8).Clear();
        }
        using var peReader = Open(image);

        Assert.False(peReader.HasMetadata);
        Assert.Throws<InvalidOperationException>(() => peReader.GetMetadata());
        Assert.IsType<MetadataImageFormatResult.NoMetadata>(
            MetadataImageFormatClassifier.Classify(peReader));
    }

    [Fact]
    public void Mdp017_UnmappableMetadataDirectoryIsTypedMalformed()
    {
        byte[] image = BuildImage("v4.0.30319");
        int corHeaderStart = CorHeaderStart(image);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(corHeaderStart + 8, sizeof(int)),
            int.MaxValue);
        using var peReader = Open(image);

        AssertMalformed(
            peReader,
            MetadataRootMalformedReason.UnmappableMetadataDirectory);
    }

    [Fact]
    public void Mdp017_TruncatedFixedPrefixIsTypedMalformed()
    {
        byte[] image = BuildImage("v4.0.30319");
        SetMetadataDirectorySize(image, 12);
        using var peReader = Open(image);

        AssertMalformed(
            peReader,
            MetadataRootMalformedReason.TruncatedFixedPrefix);
    }

    [Fact]
    public void Mdp017_InvalidSignatureIsTypedMalformed()
    {
        byte[] image = BuildImage("v4.0.30319");
        BinaryPrimitives.WriteUInt32LittleEndian(
            image.AsSpan(MetadataStart(image), sizeof(uint)),
            0xDEADBEEF);
        using var peReader = Open(image);

        AssertMalformed(
            peReader,
            MetadataRootMalformedReason.InvalidSignature);
    }

    [Theory]
    [InlineData(-4)]
    [InlineData(2)]
    [InlineData(260)]
    public void Mdp017_InvalidVersionLengthIsTypedMalformed(
        int versionLength)
    {
        byte[] image = BuildImage("v4.0.30319");
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(MetadataStart(image) + 12, sizeof(int)),
            versionLength);
        using var peReader = Open(image);

        AssertMalformed(
            peReader,
            MetadataRootMalformedReason.InvalidVersionLength);
    }

    [Fact]
    public void Mdp017_VersionFieldBeyondBlockIsTypedMalformed()
    {
        byte[] image = BuildImage("v4.0.30319");
        int metadataStart = MetadataStart(image);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)),
            8);
        SetMetadataDirectorySize(image, 20);
        using var peReader = Open(image);

        AssertMalformed(
            peReader,
            MetadataRootMalformedReason.TruncatedVersionField);
    }

    [Fact]
    public void Mdp017_MissingVersionTerminatorIsTypedMalformed()
    {
        byte[] image = BuildImage("v4.0.30319");
        int metadataStart = MetadataStart(image);
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        image.AsSpan(
                metadataStart
                    + MetadataImageFormatClassifier.FixedPrefixLength,
                versionLength)
            .Fill((byte)'A');
        using var peReader = Open(image);

        AssertMalformed(
            peReader,
            MetadataRootMalformedReason.MissingVersionTerminator);
    }

    [Fact]
    public void Mdp017_LazyMetadataIoFailureRemainsAcquisitionFailure()
    {
        byte[] image = BuildImage("v4.0.30319");
        using var stream = new ArmableReadFailureStream(image);
        using var peReader = new PEReader(
            stream,
            PEStreamOptions.LeaveOpen);
        Assert.True(peReader.HasMetadata);
        stream.Arm();

        Assert.Throws<IOException>(() =>
        {
            _ = MetadataImageFormatClassifier.Classify(peReader);
        });
    }

    [Fact]
    public void Mdp017_ClassificationAllocationDoesNotScaleWithRows()
    {
        using var small = Open(BuildImage("v4.0.30319"));
        using var large = Open(
            BuildImage("v4.0.30319", additionalTypeCount: 20_000));

        _ = MetadataImageFormatClassifier.Classify(small);
        _ = MetadataImageFormatClassifier.Classify(large);

        long smallAllocation = MeasureAllocation(small);
        long largeAllocation = MeasureAllocation(large);

        Assert.InRange(smallAllocation, 0, 64 * 1024);
        Assert.InRange(largeAllocation, 0, 64 * 1024);
        Assert.InRange(
            Math.Abs(largeAllocation - smallAllocation),
            0,
            4 * 1024);
    }

    static long MeasureAllocation(PEReader reader)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        MetadataImageFormatResult? last = null;
        for (int i = 0; i < 1_000; i++)
            last = MetadataImageFormatClassifier.Classify(reader);
        GC.KeepAlive(last);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    static void AssertMalformed(
        PEReader peReader,
        MetadataRootMalformedReason expected)
    {
        var malformed =
            Assert.IsType<MetadataImageFormatResult.MalformedRoot>(
                MetadataImageFormatClassifier.Classify(peReader));
        Assert.Equal(expected, malformed.Reason);
    }

    static PEReader Open(byte[] image)
        => new(ImmutableArray.Create(image));

    static byte[] BuildImage(
        string metadataVersion,
        int additionalTypeCount = 0)
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
        for (int i = 0; i < additionalTypeCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString($"Type{i}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

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

    static void SetMetadataDirectorySize(
        byte[] image,
        int size)
    {
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(CorHeaderStart(image) + 12, sizeof(int)),
            size);
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
        SetMetadataDirectorySize(
            image,
            MetadataImageFormatClassifier.FixedPrefixLength
                + versionLength);
    }

    sealed class ArmableReadFailureStream(byte[] image)
        : MemoryStream(image, writable: false)
    {
        bool _armed;

        public void Arm() => _armed = true;

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
            => _armed
                ? throw new IOException("Injected metadata acquisition failure.")
                : base.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer)
            => _armed
                ? throw new IOException("Injected metadata acquisition failure.")
                : base.Read(buffer);
    }
}
