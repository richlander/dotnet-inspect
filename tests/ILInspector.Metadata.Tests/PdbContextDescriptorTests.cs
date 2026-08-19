using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Queries.EmbeddedFixtures;
using ILInspector.Findings;

namespace ILInspector.Metadata.Tests;

public class PdbContextDescriptorTests
{
    [Fact]
    public void MetadataOnlyAndEmbeddedOnly_KeepTheirPdbAcquisitionBoundaries()
    {
        string path = typeof(EmbeddedSourceFixture).Assembly.Location;

        using PdbContext metadataOnly = PdbContext.OpenMetadataOnly(path);
        Assert.True(metadataOnly.HasEmbeddedPdb);
        Assert.False(metadataOnly.HasPdb);

        using PdbContext embeddedOnly = PdbContext.OpenEmbeddedPdbOnly(path);
        Assert.True(embeddedOnly.HasEmbeddedPdb);
        Assert.True(embeddedOnly.HasPdb);
        Assert.Equal("Embedded", embeddedOnly.PdbLocation);
    }

    [Fact]
    public void DescriptorMetadataOnlyAndEmbeddedOnly_KeepTheirPdbAcquisitionBoundaries()
    {
        byte[] image = File.ReadAllBytes(
            typeof(EmbeddedSourceFixture).Assembly.Location);
        AssemblyReferenceIdentity identity = ReadIdentity(image);

        using PdbContext metadataOnly =
            PdbContext.OpenMetadataOnly(CreateDescriptor(image, identity));
        Assert.True(metadataOnly.HasEmbeddedPdb);
        Assert.False(metadataOnly.HasPdb);

        using PdbContext embeddedOnly =
            PdbContext.OpenEmbeddedPdbOnly(CreateDescriptor(image, identity));
        Assert.True(embeddedOnly.HasEmbeddedPdb);
        Assert.True(embeddedOnly.HasPdb);
        Assert.Equal("Embedded", embeddedOnly.PdbLocation);
    }

    [Fact]
    public void EmbeddedPdbAndSourceLinkLimits_PrecedePayloadMaterialization()
    {
        string path = typeof(EmbeddedSourceFixture).Assembly.Location;

        Assert.Throws<PdbResourceLimitException>(
            () => PdbContext.OpenEmbeddedPdbOnly(
                path,
                maxEmbeddedPdbBytes: 0));

        using (PdbContext context = PdbContext.OpenEmbeddedPdbOnly(path))
        {
            PdbCustomDebugInformationResult result =
                context.ReadModuleCustomDebugInformation(
                    new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A"),
                    maxValueBytes: 1);
            Assert.True(result.LimitExceeded);
            Assert.Null(result.Value);
            Assert.True(result.ValueLength > 1);
        }

        using (SourceLinkService source = SourceLinkService.OpenEmbeddedPdbOnly(
            path,
            new SourceLinkReadLimits(
                maxEmbeddedPdbBytes: int.MaxValue,
                maxMapBytes: 1,
                maxMappings: int.MaxValue)))
        {
            SourceLinkMapAudit audit = source.InspectSourceLinkMap();
            Assert.Equal(SourceLinkMapLimitKind.EncodedBytes, audit.LimitKind);
            Assert.Empty(audit.Entries);
            Assert.True(audit.EncodedBytes > 1);
        }

        using (SourceLinkService source = SourceLinkService.OpenEmbeddedPdbOnly(
            path,
            new SourceLinkReadLimits(
                maxEmbeddedPdbBytes: int.MaxValue,
                maxMapBytes: int.MaxValue,
                maxMappings: 0)))
        {
            SourceLinkMapAudit audit = source.InspectSourceLinkMap();
            Assert.Equal(SourceLinkMapLimitKind.Mappings, audit.LimitKind);
            Assert.Empty(audit.Entries);
        }
    }

    [Fact]
    public void EmbeddedPdbLimit_ReadsTheFilePointerUsedByTheDecoder()
    {
        const int Limit = 1024 * 1024;
        byte[] image = File.ReadAllBytes(
            typeof(EmbeddedSourceFixture).Assembly.Location);
        byte[] divergent = PointEmbeddedPdbAtDeclaredSize(
            image,
            declaredSize: Limit + 1);
        var descriptor = CreateDescriptor(
            divergent,
            ReadIdentity(divergent));

        PdbResourceLimitException error =
            Assert.Throws<PdbResourceLimitException>(
                () => PdbContext.OpenEmbeddedPdbOnly(
                    descriptor,
                    maxEmbeddedPdbBytes: Limit));

        Assert.Equal(Limit + 1, error.ActualBytes);
        Assert.Equal(Limit, error.LimitBytes);
    }

    [Fact]
    public void EmbeddedPdbExpansionBudget_IsSharedAcrossOpens()
    {
        byte[] image = File.ReadAllBytes(
            typeof(EmbeddedSourceFixture).Assembly.Location);
        var descriptor = CreateDescriptor(
            image,
            ReadIdentity(image));
        int embeddedPdbSize;
        using (PdbContext baseline =
               PdbContext.OpenEmbeddedPdbOnly(descriptor))
        {
            embeddedPdbSize = baseline.EmbeddedPdbSize;
        }
        Assert.True(embeddedPdbSize > 0);

        var budget = new PdbExpansionBudget(embeddedPdbSize);
        using PdbContext first =
            PdbContext.OpenEmbeddedPdbOnly(
                descriptor,
                maxEmbeddedPdbBytes: embeddedPdbSize,
                expansionBudget: budget);

        PdbResourceLimitException error =
            Assert.Throws<PdbResourceLimitException>(
                () => PdbContext.OpenEmbeddedPdbOnly(
                    descriptor,
                    maxEmbeddedPdbBytes: embeddedPdbSize,
                    expansionBudget: budget));

        Assert.Equal(embeddedPdbSize, budget.ReservedBytes);
        Assert.Equal(embeddedPdbSize, error.ActualBytes);
        Assert.Equal(0, error.LimitBytes);
    }

    [Fact]
    public void MalformedEmbeddedPdb_ConsumesExpansionBudgetBeforeDecode()
    {
        const int DeclaredSize = 2 * 1024 * 1024;
        byte[] image = File.ReadAllBytes(
            typeof(EmbeddedSourceFixture).Assembly.Location);
        byte[] malformed = PointEmbeddedPdbAtDeclaredSize(
            image,
            DeclaredSize);
        var descriptor = CreateDescriptor(
            malformed,
            ReadIdentity(malformed));
        var budget = new PdbExpansionBudget(DeclaredSize);

        Assert.Throws<BadImageFormatException>(
            () => PdbContext.OpenEmbeddedPdbOnly(
                descriptor,
                maxEmbeddedPdbBytes: DeclaredSize,
                expansionBudget: budget));

        Assert.Equal(DeclaredSize, budget.ReservedBytes);
    }

    [Fact]
    public void OpenDescriptor_UsesAuthoritativeStreamInsteadOfPath()
    {
        string authoritativePath = typeof(PdbContextDescriptorTests).Assembly.Location;
        string informationalPath = typeof(PdbContext).Assembly.Location;
        byte[] authoritativeImage = File.ReadAllBytes(authoritativePath);
        AssemblyReferenceIdentity identity = ReadIdentity(authoritativeImage);
        DateTime authoritativeTimestamp = new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            informationalPath,
            () => new MemoryStream(authoritativeImage, writable: false),
            AssemblyResolutionProvenance.Local("test"),
            authoritativeTimestamp);

        using var context = PdbContext.Open(descriptor);

        Assert.Equal(identity.Name, context.ExtractAssemblyInfo().AssemblyName);
        Assert.Equal(informationalPath, context.AssemblyPathOrNull);
        Assert.Equal(
            authoritativeTimestamp,
            context.LastWriteTimeUtc);
    }

    [Fact]
    public void OpenPrefetchedDescriptor_UsesAuthoritativeStreamInsteadOfPath()
    {
        string authoritativePath = typeof(PdbContextDescriptorTests).Assembly.Location;
        string informationalPath = typeof(PdbContext).Assembly.Location;
        byte[] authoritativeImage = File.ReadAllBytes(authoritativePath);
        AssemblyReferenceIdentity identity = ReadIdentity(authoritativeImage);
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            informationalPath,
            () => new MemoryStream(authoritativeImage, writable: false),
            AssemblyResolutionProvenance.Local("test"));

        using var context = PdbContext.OpenPrefetched(descriptor);

        Assert.Equal(identity.Name, context.ExtractAssemblyInfo().AssemblyName);
        Assert.Equal(informationalPath, context.AssemblyPathOrNull);
    }

    [Fact]
    public void OpenDescriptor_StreamOnlyImageRemainsUsable()
    {
        byte[] image = File.ReadAllBytes(
            typeof(PdbContextDescriptorTests).Assembly.Location);
        AssemblyReferenceIdentity identity = ReadIdentity(image);
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("test"));

        using var context = PdbContext.Open(descriptor);

        Assert.Equal(identity.Name, context.ExtractAssemblyInfo().AssemblyName);
        Assert.Null(context.AssemblyPathOrNull);
        Assert.Throws<InvalidOperationException>(() => context.AssemblyPath);
    }

    [Theory]
    [InlineData(DescriptorOpenKind.PdbContext, false)]
    [InlineData(DescriptorOpenKind.PdbContext, true)]
    [InlineData(DescriptorOpenKind.AssemblyImage, false)]
    [InlineData(DescriptorOpenKind.AssemblyImage, true)]
    [InlineData(DescriptorOpenKind.PrefetchedSession, false)]
    [InlineData(DescriptorOpenKind.PrefetchedSession, true)]
    public void DescriptorOpenPrimaryFailure_IsNotMaskedByCleanupFailure(
        DescriptorOpenKind openKind,
        bool fatalFailure)
    {
        byte[] image =
            File.ReadAllBytes(
                typeof(PdbContextDescriptorTests).Assembly.Location);
        Exception primaryFailure =
            fatalFailure
                ? new OutOfMemoryException(
                    "Synthetic fatal descriptor-open failure.")
                : new OperationCanceledException(
                    "Synthetic descriptor-open cancellation.");
        var cleanupFailure =
            new IOException(
                "Synthetic descriptor cleanup failure.");
        var stream =
            new PrimaryAndCleanupFailureStream(
                image,
                primaryFailure,
                cleanupFailure);
        var descriptor =
            ResolvedAssemblyReference.Create(
                ReadIdentity(image),
                path: null,
                () => stream,
                AssemblyResolutionProvenance.Local("test"));

        Action operation =
            openKind switch
            {
                DescriptorOpenKind.PdbContext =>
                    () => PdbContext.Open(descriptor),
                DescriptorOpenKind.AssemblyImage =>
                    () => AssemblyImage.Open(descriptor),
                DescriptorOpenKind.PrefetchedSession =>
                    () => AssemblyInspectionSession
                        .OpenPrefetched(stream),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(openKind)),
            };
        Exception error =
            fatalFailure
                ? Assert.Throws<OutOfMemoryException>(operation)
                : Assert.Throws<OperationCanceledException>(operation);

        Assert.Same(primaryFailure, error);
        Assert.Equal(1, stream.DisposeCount);
    }

    public enum DescriptorOpenKind
    {
        PdbContext,
        AssemblyImage,
        PrefetchedSession,
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PdbContextConstructionPrimaryFailure_ReleasesOwnedStream(
        bool fatalFailure)
    {
        byte[] image =
            File.ReadAllBytes(
                typeof(PdbContextDescriptorTests).Assembly.Location);
        Exception primaryFailure =
            fatalFailure
                ? new OutOfMemoryException(
                    "Synthetic fatal context-construction failure.")
                : new OperationCanceledException(
                    "Synthetic context-construction cancellation.");
        var stream =
            new SecondLengthAndCleanupFailureStream(
                image,
                primaryFailure,
                new IOException(
                    "Synthetic context-construction cleanup failure."));
        var descriptor =
            ResolvedAssemblyReference.Create(
                ReadIdentity(image),
                path: null,
                () => stream,
                AssemblyResolutionProvenance.Local("test"));

        Exception error =
            fatalFailure
                ? Assert.Throws<OutOfMemoryException>(
                    () => PdbContext.Open(descriptor))
                : Assert.Throws<OperationCanceledException>(
                    () => PdbContext.Open(descriptor));

        Assert.Same(primaryFailure, error);
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public void DeclarationInventory_UsesAuthoritativeDescriptorStream()
    {
        string authoritativePath =
            typeof(PdbContextDescriptorTests).Assembly.Location;
        string informationalPath = typeof(PdbContext).Assembly.Location;
        byte[] authoritativeImage = File.ReadAllBytes(authoritativePath);
        AssemblyReferenceIdentity identity = ReadIdentity(authoritativeImage);
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            informationalPath,
            () => new MemoryStream(authoritativeImage, writable: false),
            AssemblyResolutionProvenance.Local("test"));

        var read = Assert.IsType<
            AssemblyTypeDeclarationInventoryOutcome.Read>(
                AssemblyTypeDeclarationInventoryReader.Read(descriptor));

        Assert.Equal(identity, read.Inventory.Identity);
        Assert.Contains(
            read.Inventory.Definitions,
            name => name.ToMetadataFullName()
                == typeof(PdbContextDescriptorTests).FullName);
    }

    [Fact]
    public void DeclarationInventory_RejectsDescriptorIdentityMismatch()
    {
        byte[] authoritativeImage = File.ReadAllBytes(
            typeof(PdbContextDescriptorTests).Assembly.Location);
        AssemblyReferenceIdentity wrongIdentity =
            ReadIdentity(File.ReadAllBytes(typeof(PdbContext).Assembly.Location));
        var descriptor = ResolvedAssemblyReference.Create(
            wrongIdentity,
            path: null,
            () => new MemoryStream(authoritativeImage, writable: false),
            AssemblyResolutionProvenance.Local("test"));

        var rejected = Assert.IsType<
            AssemblyTypeDeclarationInventoryOutcome.Rejected>(
                AssemblyTypeDeclarationInventoryReader.Read(descriptor));

        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
    }

    [Fact]
    public void DeclarationInventory_IncludesNestedForwardedTypes()
    {
        byte[] image = BuildNestedForwarderAssembly();
        AssemblyReferenceIdentity identity = ReadIdentity(image);
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("test"));

        var read = Assert.IsType<
            AssemblyTypeDeclarationInventoryOutcome.Read>(
                AssemblyTypeDeclarationInventoryReader.Read(descriptor));

        Assert.Contains(
            read.Inventory.Forwarders,
            name => name.Namespace == "N"
                && name.Segments.SequenceEqual(["Outer", "Inner"]));
    }

    [Fact]
    public void SurfaceClassification_ProjectsSuccessAndFailureToFindings()
    {
        string path = typeof(PdbContextDescriptorTests).Assembly.Location;
        AssemblySurfaceClassificationOutcome classified =
            AssemblySurfaceClassifier.Classify(
                path,
                AssemblyResolutionProvenance.Local("test"));
        var subject = new FindingSubject(path, Path.GetFileName(path));

        var complete = Assert.IsType<
            FindingInspection<AssemblySurfaceClassification>.Complete>(
                MetadataFindings
                    .InspectAssemblySurface(classified, subject).Value);
        Assert.Single(complete.Findings);
        Assert.Equal(
            AssemblySurfaceKind.Implementation,
            complete.Findings[0].Payload.Kind);

        var rejected = new AssemblySurfaceClassificationOutcome.Rejected(
            new CandidateOpenFailure(
                CandidateOpenFailureKind.InvalidImage,
                "Invalid metadata."));
        Assert.IsType<
            FindingInspection<AssemblySurfaceClassification>.Failed>(
                MetadataFindings
                    .InspectAssemblySurface(rejected, subject).Value);
    }

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            peReader.GetMetadataReader());
    }

    static ResolvedAssemblyReference CreateDescriptor(
        byte[] image,
        AssemblyReferenceIdentity identity)
        => ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("test"));

    static byte[] PointEmbeddedPdbAtDeclaredSize(
        byte[] image,
        int declaredSize)
    {
        const int DebugDirectoryEntrySize = 28;
        const int TypeOffset = 12;
        const int DataSizeOffset = 16;
        const int DataPointerOffset = 24;
        const uint EmbeddedPortablePdbSignature = 0x4244504D;

        using var stream = new MemoryStream(image, writable: false);
        using var reader = new PEReader(stream);
        DirectoryEntry directory =
            reader.PEHeaders.PEHeader!.DebugTableDirectory;
        int directoryOffset = RvaToFileOffset(
            reader.PEHeaders,
            directory.RelativeVirtualAddress);
        int entryCount = directory.Size / DebugDirectoryEntrySize;
        int embeddedEntryOffset = -1;
        for (int index = 0; index < entryCount; index++)
        {
            int entryOffset =
                directoryOffset + index * DebugDirectoryEntrySize;
            int type = BinaryPrimitives.ReadInt32LittleEndian(
                image.AsSpan(entryOffset + TypeOffset, sizeof(int)));
            if (type == (int)DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                embeddedEntryOffset = entryOffset;
                break;
            }
        }
        Assert.True(
            embeddedEntryOffset >= 0,
            "Expected an embedded portable-PDB debug-directory entry.");

        byte[] patched = new byte[image.Length + 8];
        image.CopyTo(patched, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            patched.AsSpan(image.Length, sizeof(uint)),
            EmbeddedPortablePdbSignature);
        BinaryPrimitives.WriteInt32LittleEndian(
            patched.AsSpan(image.Length + sizeof(uint), sizeof(int)),
            declaredSize);
        BinaryPrimitives.WriteInt32LittleEndian(
            patched.AsSpan(embeddedEntryOffset + DataSizeOffset, sizeof(int)),
            8);
        BinaryPrimitives.WriteInt32LittleEndian(
            patched.AsSpan(embeddedEntryOffset + DataPointerOffset, sizeof(int)),
            image.Length);
        return patched;
    }

    static int RvaToFileOffset(PEHeaders headers, int rva)
    {
        foreach (SectionHeader section in headers.SectionHeaders)
        {
            int sectionSize = Math.Max(
                section.VirtualSize,
                section.SizeOfRawData);
            if (rva >= section.VirtualAddress
                && rva - section.VirtualAddress < sectionSize)
            {
                return section.PointerToRawData
                    + rva
                    - section.VirtualAddress;
            }
        }

        throw new InvalidOperationException(
            $"RVA 0x{rva:X8} is not mapped by a PE section.");
    }

    static byte[] BuildNestedForwarderAssembly()
    {
        const TypeAttributes Forwarder = (TypeAttributes)0x00200000;
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("NestedForwarder.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("NestedForwarder"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        ExportedTypeHandle outer = metadata.AddExportedType(
            TypeAttributes.Public | Forwarder,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer"),
            target,
            typeDefinitionId: 0);
        metadata.AddExportedType(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("Inner"),
            outer,
            typeDefinitionId: 0);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    sealed class PrimaryAndCleanupFailureStream(
        byte[] bytes,
        Exception primaryFailure,
        Exception cleanupFailure)
        : MemoryStream(bytes, writable: false)
    {
        internal int DisposeCount { get; private set; }

        public override bool CanRead =>
            throw primaryFailure;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
                base.Dispose(disposing);
                throw cleanupFailure;
            }
            base.Dispose(disposing);
        }
    }

    sealed class SecondLengthAndCleanupFailureStream(
        byte[] bytes,
        Exception primaryFailure,
        Exception cleanupFailure)
        : MemoryStream(bytes, writable: false)
    {
        int _lengthReads;

        internal int DisposeCount { get; private set; }

        public override long Length
        {
            get
            {
                _lengthReads++;
                if (_lengthReads == 2)
                    throw primaryFailure;
                return base.Length;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
                base.Dispose(disposing);
                throw cleanupFailure;
            }
            base.Dispose(disposing);
        }
    }
}
