using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Artifacts;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class InspectionAcquisitionPlanTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;
    static string SelfPath => typeof(InspectionAcquisitionPlanTests).Assembly.Location;

    [Fact]
    public void DescriptorFactory_MintsOpaqueReferenceIdentityRegistration()
    {
        AssemblyReferenceIdentity identity = ReadIdentity(SelfBytes());
        ResolvedAssemblyReference first = Descriptor(identity, SelfBytes);
        ResolvedAssemblyReference second = Descriptor(identity, SelfBytes);

        Assert.NotSame(first, second);
        Assert.NotEqual(first, second);
        Assert.NotSame(first.Registration, second.Registration);
        Assert.Same(first.Registration, first.Registration);
        Assert.Equal(identity, first.Identity);
        Assert.IsType<AssemblyResolutionProvenance.LocalAsset>(first.Provenance);
    }

    [Fact]
    public void StructuredProvenance_HasClosedTypedArms()
    {
        var package = AssemblyResolutionProvenance.Package(
            "Example.Package",
            "1.2.3",
            "net10.0",
            "linux-x64");
        var samePackage = AssemblyResolutionProvenance.Package(
            "Example.Package",
            "1.2.3",
            "net10.0",
            "linux-x64");
        var platform = AssemblyResolutionProvenance.Platform(
            "Microsoft.NETCore.App",
            "10.0.0",
            "test");
        var embedded = AssemblyResolutionProvenance.Embedded(
            "assemblies/Example.dll",
            "sha256-example",
            "Example");

        Assert.Equal(package, samePackage);
        Assert.NotEqual(package, platform);
        Assert.Equal(
            "Example.Package",
            Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(package)
                .PackageId);
        Assert.Equal(
            "assemblies/Example.dll",
            Assert.IsType<AssemblyResolutionProvenance.EmbeddedAsset>(embedded)
                .ContentRef);
    }

    [Fact]
    public void CreateFromPath_CapturesSelectedImageIdentity()
    {
        ResolvedAssemblyReference descriptor =
            ResolvedAssemblyReference.CreateFromPath(
                SelfPath,
                AssemblyResolutionProvenance.Local("test"));

        Assert.Equal(ReadIdentity(SelfBytes()), descriptor.Identity);
        Assert.Equal(Path.GetFullPath(SelfPath), descriptor.Path);
        Assert.Equal(
            File.GetLastWriteTimeUtc(SelfPath),
            descriptor.LastWriteTimeUtc);
    }

    [Fact]
    public void TryCreateFromPath_UnreadableOrInvalidImage_ReturnsFalse()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.dll");
        string invalid = Path.GetTempFileName();
        try
        {
            Assert.False(ResolvedAssemblyReference.TryCreateFromPath(
                missing,
                AssemblyResolutionProvenance.Local("test"),
                out _));
            Assert.False(ResolvedAssemblyReference.TryCreateFromPath(
                invalid,
                AssemblyResolutionProvenance.Local("test"),
                out _));
        }
        finally
        {
            File.Delete(invalid);
        }
    }

    [Fact]
    public void PathFactories_BlankAssemblyName_ReturnNoDescriptor()
    {
        string path = Path.GetTempFileName();
        try
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                0,
                metadata.GetOrAddString("BlankName.dll"),
                metadata.GetOrAddGuid(Guid.NewGuid()),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString(" "),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            metadata.AddTypeDefinition(
                default,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            var pe = new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                new BlobBuilder(),
                flags: CorFlags.ILOnly);
            var image = new BlobBuilder();
            pe.Serialize(image);
            File.WriteAllBytes(path, image.ToArray());

            Assert.Null(
                ResolvedAssemblyReference.CreateFromPathIfManaged(
                    path,
                    AssemblyResolutionProvenance.Local("test")));
            Assert.False(ResolvedAssemblyReference.TryCreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("test"),
                out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateFromPathIfManaged_NonPeImage_ReturnsNull()
    {
        string invalid = Path.GetTempFileName();
        try
        {
            Assert.Null(
                ResolvedAssemblyReference.CreateFromPathIfManaged(
                    invalid,
                    AssemblyResolutionProvenance.Local("test")));
        }
        finally
        {
            File.Delete(invalid);
        }
    }

    [Fact]
    public void ArtifactDescriptor_PreservesRegistrationAndBindsNonEmptyMvid()
    {
        Guid mvid = Guid.NewGuid();
        byte[] image =
            BuildSimpleAssembly("ArtifactBound", "Type", mvid);
        ArtifactAcquisitionRegistration artifactRegistration =
            RegisterArtifact(
                () => new MemoryStream(image, writable: false));

        ResolvedAssemblyReference descriptor =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                artifactRegistration,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local("test"))
            ?? throw new InvalidOperationException(
                "The managed assembly was not recognized.");

        Assert.Same(
            artifactRegistration,
            descriptor.Registration.ArtifactRegistration);
        Assert.Equal(
            mvid,
            descriptor.Registration.ModuleVersionId);

        using AssemblyInspectionSession first =
            AssemblyInspectionSession.Open(descriptor);
        using AssemblyInspectionSession second =
            AssemblyInspectionSession.Open(descriptor);
        Assert.Equal("ArtifactBound", first.AssemblyInfo().AssemblyName);
        Assert.Equal("ArtifactBound", second.AssemblyInfo().AssemblyName);
    }

    [Fact]
    public void ArtifactDescriptor_RejectsSameIdentityFromDifferentModuleGeneration()
    {
        byte[] selected =
            BuildSimpleAssembly(
                "ArtifactBound",
                "Type",
                Guid.NewGuid());
        ArtifactAcquisitionRegistration artifactRegistration =
            RegisterArtifact(
                () => new MemoryStream(selected, writable: false));
        ResolvedAssemblyReference descriptor =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                artifactRegistration,
                () => new MemoryStream(selected, writable: false),
                AssemblyResolutionProvenance.Local("test"))
            ?? throw new InvalidOperationException(
                "The managed assembly was not recognized.");

        selected =
            BuildSimpleAssembly(
                "ArtifactBound",
                "Type",
                Guid.NewGuid());

        Assert.Throws<BadImageFormatException>(
            () => AssemblyImage.Open(descriptor));
        Assert.Throws<BadImageFormatException>(
            () => PdbContext.OpenMetadataOnly(descriptor));
        var snapshot =
            Assert.IsType<AssemblyImageSnapshotResult.Rejected>(
                AssemblyImageSnapshot.Open(
                    descriptor,
                    static _ => true,
                    static _ => { }));
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            snapshot.Failure.Kind);
    }

    [Fact]
    public void ArtifactDescriptor_RejectsEmptyMvidModuleAndMalformedImage()
    {
        byte[] emptyMvid =
            BuildSimpleAssembly(
                "ArtifactBound",
                "Type",
                Guid.Empty);
        ArtifactAcquisitionRegistration emptyMvidRegistration =
            RegisterArtifact(
                () => new MemoryStream(emptyMvid, writable: false));

        Assert.Throws<BadImageFormatException>(
            () => ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                emptyMvidRegistration,
                () => new MemoryStream(emptyMvid, writable: false),
                AssemblyResolutionProvenance.Local("test")));

        byte[] module = BuildModuleImage();
        Assert.Null(
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                RegisterArtifact(
                    () => new MemoryStream(module, writable: false)),
                () => new MemoryStream(module, writable: false),
                AssemblyResolutionProvenance.Local("test")));

        byte[] malformed = [0x01, 0x02, 0x03];
        Assert.Null(
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                RegisterArtifact(
                    () => new MemoryStream(malformed, writable: false)),
                () => new MemoryStream(malformed, writable: false),
                AssemblyResolutionProvenance.Local("test")));
    }

    [Fact]
    public void ArtifactFallbackDescriptor_PreservesExactRegistrationAndValidIdentity()
    {
        Guid mvid = Guid.NewGuid();
        byte[] image =
            BuildSimpleAssembly("ArtifactBound", "Type", mvid);
        ArtifactAcquisitionRegistration artifactRegistration =
            RegisterArtifact(
                () => new MemoryStream(image, writable: false));
        var fallbackIdentity = new AssemblyReferenceIdentity(
            "RejectedArtifact",
            Version: null,
            Culture: null,
            PublicKeyToken: null);

        ResolvedAssemblyReference descriptor =
            ResolvedAssemblyReference
                .CreateFromArtifactWithFallbackIdentity(
                    artifactRegistration,
                    () => new MemoryStream(image, writable: false),
                    fallbackIdentity,
                    AssemblyResolutionProvenance.Local("test"),
                    out bool usedFallbackIdentity);

        Assert.False(usedFallbackIdentity);
        Assert.Equal("ArtifactBound", descriptor.Identity.Name);
        Assert.Same(
            artifactRegistration,
            descriptor.Registration.ArtifactRegistration);
        Assert.Equal(mvid, descriptor.Registration.ModuleVersionId);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(descriptor);
        Assert.Equal(
            "ArtifactBound",
            session.AssemblyInfo().AssemblyName);
    }

    [Fact]
    public void ArtifactFallbackDescriptor_RetainsRejectedSelectedImages()
    {
        var fallbackIdentity = new AssemblyReferenceIdentity(
            "RejectedArtifact",
            Version: null,
            Culture: null,
            PublicKeyToken: null);
        AssertRejected([0x01, 0x02, 0x03]);
        AssertRejected(BuildModuleImage());
        byte[] nativeImage = BuildNativePeImage();
        using (var peReader = new PEReader(
            new MemoryStream(nativeImage, writable: false)))
        {
            Assert.False(peReader.HasMetadata);
        }
        AssertRejected(nativeImage);

        byte[] emptyMvid =
            BuildSimpleAssembly(
                "EmptyMvid",
                "Type",
                Guid.Empty);
        ArtifactAcquisitionRegistration emptyMvidRegistration =
            RegisterArtifact(
                () => new MemoryStream(
                    emptyMvid,
                    writable: false));
        ResolvedAssemblyReference emptyMvidDescriptor =
            ResolvedAssemblyReference
                .CreateFromArtifactWithFallbackIdentity(
                    emptyMvidRegistration,
                    () => new MemoryStream(
                        emptyMvid,
                        writable: false),
                    fallbackIdentity,
                    AssemblyResolutionProvenance.Local("test"),
                    out bool emptyMvidUsedFallback);
        Assert.False(emptyMvidUsedFallback);
        Assert.Equal(
            "EmptyMvid",
            emptyMvidDescriptor.Identity.Name);
        BadImageFormatException emptyMvidFailure =
            Assert.Throws<BadImageFormatException>(
                () => AssemblyImage.Open(emptyMvidDescriptor));
        Assert.Contains(
            "empty module version identifier",
            emptyMvidFailure.Message,
            StringComparison.Ordinal);
        Assert.Null(
            emptyMvidDescriptor.Registration.ModuleVersionId);

        void AssertRejected(byte[] image)
        {
            ArtifactAcquisitionRegistration artifactRegistration =
                RegisterArtifact(
                    () => new MemoryStream(image, writable: false));
            ResolvedAssemblyReference descriptor =
                ResolvedAssemblyReference
                    .CreateFromArtifactWithFallbackIdentity(
                        artifactRegistration,
                        () => new MemoryStream(image, writable: false),
                        fallbackIdentity,
                        AssemblyResolutionProvenance.Local("test"),
                        out bool usedFallbackIdentity);

            Assert.True(usedFallbackIdentity);
            Assert.Same(fallbackIdentity, descriptor.Identity);
            Assert.Same(
                artifactRegistration,
                descriptor.Registration.ArtifactRegistration);
            Assert.Null(descriptor.Registration.ModuleVersionId);
            Assert.Throws<BadImageFormatException>(
                () => AssemblyImage.Open(descriptor));
            Assert.Null(descriptor.Registration.ModuleVersionId);
        }
    }

    [Fact]
    public void Register_SameDescriptor_IsOneCandidateAndOneInventoryRead()
    {
        byte[] image = SelfBytes();
        AssemblyReferenceIdentity identity = ReadIdentity(image);
        int opens = 0;
        var descriptor = Descriptor(
            identity,
            () =>
            {
                Interlocked.Increment(ref opens);
                return image;
            });
        using var plan = new InspectionAcquisitionPlan();

        var first = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(descriptor));
        var second = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(descriptor));

        Assert.Same(first, second);
        Assert.Same(first.Candidate, second.Candidate);
        Assert.Same(first.Inventory, second.Inventory);
        Assert.Equal(1, opens);
        Assert.Equal(1, plan.CandidateCount);
    }

    [Fact]
    public void RootAndStrictRegistration_ShareOneImmutableImage()
    {
        byte[] firstImage =
            BuildSimpleAssembly("Changing", "First", Guid.NewGuid());
        byte[] secondImage =
            BuildSimpleAssembly("Changing", "Second", Guid.NewGuid());
        int opens = 0;
        var descriptor = Descriptor(
            ReadIdentity(firstImage),
            () => Interlocked.Increment(ref opens) <= 2
                ? firstImage
                : secondImage);
        using var plan = new InspectionAcquisitionPlan();

        var root = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.RegisterRoot(descriptor));
        Assert.IsType<CandidateSessionResult.Ready>(
            plan.OpenSession(root.Candidate));
        var strict = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(descriptor));

        Assert.Same(root.Candidate, strict.Candidate);
        Assert.Same(root.Inventory, strict.Inventory);
        Assert.Equal(2, opens);
        Assert.Equal(1, plan.CandidateCount);
    }

    [Fact]
    public void Register_EqualDescriptorFieldsWithFreshRegistrations_StayDistinct()
    {
        byte[] image = SelfBytes();
        AssemblyReferenceIdentity identity = ReadIdentity(image);
        using var plan = new InspectionAcquisitionPlan();

        var first = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(Descriptor(identity, () => image)));
        var second = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(Descriptor(identity, () => image)));

        Assert.NotSame(first.Candidate, second.Candidate);
        Assert.Equal(2, plan.CandidateCount);
    }

    [Fact]
    public void Inventory_CopiesIdentityReferencesAndForwarderTargets()
    {
        byte[] image = SelfBytes();
        using var plan = new InspectionAcquisitionPlan();

        var ready = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(Descriptor(ReadIdentity(image), () => image)));

        Assert.Equal("ILInspector.Metadata.Tests", ready.Inventory.Identity.Name);
        Assert.Contains(
            ready.Inventory.AssemblyReferences,
            identity => identity.Name == "ILInspector.Metadata");
        Assert.Contains(
            ready.Inventory.ForwarderTargets,
            identity => identity.Name == "ILInspector.Metadata");
        Assert.Equal(
            ReadModuleVersionId(image),
            ready.Inventory.ModuleVersionId);
        Assert.Equal(image.LongLength, ready.Inventory.ImageSize);
    }

    [Fact]
    public void Inventory_DeduplicatesRepeatedForwarderTargets()
    {
        byte[] image = BuildValidForwarderImage(
            forwarderCount: 1_000,
            assemblyReferenceCount: 1_000);
        using var plan = new InspectionAcquisitionPlan();

        var ready = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(Descriptor(ReadIdentity(image), () => image)));

        Assert.Single(ready.Inventory.AssemblyReferences);
        Assert.Single(ready.Inventory.ForwarderTargets);
        Assert.Equal(
            ready.Inventory.AssemblyReferences[0],
            ready.Inventory.ForwarderTargets[0]);
    }

    [Fact]
    public void Register_DescriptorIdentityMismatch_IsTypedInvalidImage()
    {
        byte[] image = SelfBytes();
        AssemblyReferenceIdentity identity = ReadIdentity(image) with
        {
            Name = "Different",
        };
        using var plan = new InspectionAcquisitionPlan();

        var rejected = Assert.IsType<CandidateRegistrationResult.Rejected>(
            plan.Register(Descriptor(identity, () => image)));

        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
    }

    [Fact]
    public void Register_NonSeekableSource_IsTypedUnreadable()
    {
        byte[] image = SelfBytes();
        int disposals = 0;
        using var plan = new InspectionAcquisitionPlan();
        var descriptor = ResolvedAssemblyReference.Create(
            ReadIdentity(image),
            path: null,
            openRead: () => new NonSeekableReadStream(
                new DisposeTrackingMemoryStream(
                    image,
                    () => Interlocked.Increment(ref disposals))),
            provenance: AssemblyResolutionProvenance.Local("test"));

        var rejected = Assert.IsType<CandidateRegistrationResult.Rejected>(
            plan.Register(descriptor));

        Assert.Equal(CandidateOpenFailureKind.Unreadable, rejected.Failure.Kind);
        Assert.Equal(1, disposals);
    }

    [Fact]
    public void WithoutLocalPath_PreservesRegistrationAndAcquisition()
    {
        ResolvedAssemblyReference descriptor =
            ResolvedAssemblyReference.CreateFromPath(
                SelfPath,
                AssemblyResolutionProvenance.Local("test"));

        ResolvedAssemblyReference contentOnly =
            descriptor.WithoutLocalPath();

        Assert.Same(
            descriptor.Registration,
            contentOnly.Registration);
        Assert.Equal(descriptor.Identity, contentOnly.Identity);
        Assert.Null(contentOnly.Path);
        Assert.Same(descriptor.OpenRead, contentOnly.OpenRead);
        Assert.Same(descriptor.Provenance, contentOnly.Provenance);
        Assert.Equal(
            descriptor.LastWriteTimeUtc,
            contentOnly.LastWriteTimeUtc);
        using Stream stream = contentOnly.OpenRead();
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData(StreamCancellationPoint.Open)]
    [InlineData(StreamCancellationPoint.CanRead)]
    [InlineData(StreamCancellationPoint.Read)]
    [InlineData(StreamCancellationPoint.FlushAsync)]
    [InlineData(StreamCancellationPoint.ReadAsyncArray)]
    [InlineData(StreamCancellationPoint.ReadAsyncMemory)]
    [InlineData(StreamCancellationPoint.WriteAsyncArray)]
    [InlineData(StreamCancellationPoint.WriteAsyncMemory)]
    [InlineData(StreamCancellationPoint.CopyTo)]
    [InlineData(StreamCancellationPoint.CopyToAsync)]
    [InlineData(StreamCancellationPoint.BeginRead)]
    [InlineData(StreamCancellationPoint.EndRead)]
    [InlineData(StreamCancellationPoint.BeginWrite)]
    [InlineData(StreamCancellationPoint.EndWrite)]
    [InlineData(StreamCancellationPoint.FlushAsyncCompletion)]
    [InlineData(StreamCancellationPoint.ReadAsyncArrayCompletion)]
    [InlineData(StreamCancellationPoint.ReadAsyncMemoryCompletion)]
    [InlineData(StreamCancellationPoint.WriteAsyncArrayCompletion)]
    [InlineData(StreamCancellationPoint.WriteAsyncMemoryCompletion)]
    [InlineData(StreamCancellationPoint.CopyToAsyncCompletion)]
    [InlineData(StreamCancellationPoint.DisposeAsyncCompletion)]
    public async Task ObserveOpenReadCancellation_PreservesRegistrationAndReportsStreamOperationCancellation(
        StreamCancellationPoint cancellationPoint)
    {
        var cancellation =
            new OperationCanceledException("test");
        OperationCanceledException? observed = null;
        byte[] image = SelfBytes();
        Func<Stream> openRead =
            cancellationPoint == StreamCancellationPoint.Open
                ? () => throw cancellation
                : () => new CancellationOnOperationStream(
                    image,
                    cancellation,
                    cancellationPoint);
        var descriptor =
            ResolvedAssemblyReference.Create(
                ReadIdentity(image),
                path: null,
                openRead,
                provenance:
                    AssemblyResolutionProvenance.Local("test"));

        ResolvedAssemblyReference decorated =
            descriptor.ObserveOpenReadCancellation(
                error => observed = error);

        Assert.Same(
            descriptor.Registration,
            decorated.Registration);
        if (cancellationPoint == StreamCancellationPoint.Open)
        {
            Assert.Same(
                cancellation,
                Assert.Throws<OperationCanceledException>(
                    () => decorated.OpenRead()));
        }
        else
        {
            using Stream stream = decorated.OpenRead();
            Assert.Same(
                cancellation,
                await Assert.ThrowsAsync<OperationCanceledException>(
                    () => InvokeCancellationAsync(
                        stream,
                        cancellationPoint)));
        }
        Assert.Same(cancellation, observed);

        static async Task InvokeCancellationAsync(
            Stream stream,
            StreamCancellationPoint cancellationPoint)
        {
            byte[] buffer = new byte[1];
            switch (cancellationPoint)
            {
                case StreamCancellationPoint.CanRead:
                    _ = stream.CanRead;
                    break;
                case StreamCancellationPoint.Read:
                    Assert.Equal(
                        1,
                        stream.Read(
                            buffer,
                            offset: 0,
                            count: 1));
                    break;
                case StreamCancellationPoint.FlushAsync:
                case StreamCancellationPoint.FlushAsyncCompletion:
                    await stream.FlushAsync(
                        TestContext.Current.CancellationToken);
                    break;
                case StreamCancellationPoint.ReadAsyncArray:
                case StreamCancellationPoint.ReadAsyncArrayCompletion:
                    Assert.Equal(
                        1,
                        await stream.ReadAsync(
                            buffer,
                            offset: 0,
                            count: 1,
                            TestContext.Current.CancellationToken));
                    break;
                case StreamCancellationPoint.ReadAsyncMemory:
                case StreamCancellationPoint.ReadAsyncMemoryCompletion:
                    Assert.Equal(
                        1,
                        await stream.ReadAsync(
                            buffer.AsMemory(),
                            TestContext.Current.CancellationToken));
                    break;
                case StreamCancellationPoint.WriteAsyncArray:
                case StreamCancellationPoint.WriteAsyncArrayCompletion:
                    await stream.WriteAsync(
                        buffer,
                        offset: 0,
                        count: 1,
                        TestContext.Current.CancellationToken);
                    break;
                case StreamCancellationPoint.WriteAsyncMemory:
                case StreamCancellationPoint.WriteAsyncMemoryCompletion:
                    await stream.WriteAsync(
                        buffer.AsMemory(),
                        TestContext.Current.CancellationToken);
                    break;
                case StreamCancellationPoint.CopyTo:
                    using (var destination = new MemoryStream())
                    {
                        stream.CopyTo(
                            destination,
                            bufferSize: 1);
                    }
                    break;
                case StreamCancellationPoint.CopyToAsync:
                case StreamCancellationPoint.CopyToAsyncCompletion:
                    using (var destination = new MemoryStream())
                    {
                        await stream.CopyToAsync(
                            destination,
                            bufferSize: 1,
                            TestContext.Current.CancellationToken);
                    }
                    break;
                case StreamCancellationPoint.BeginRead:
                case StreamCancellationPoint.EndRead:
                    IAsyncResult read =
                        stream.BeginRead(
                            buffer,
                            offset: 0,
                            count: 1,
                            callback: null,
                            state: null);
                    stream.EndRead(read);
                    break;
                case StreamCancellationPoint.BeginWrite:
                case StreamCancellationPoint.EndWrite:
                    IAsyncResult write =
                        stream.BeginWrite(
                            buffer,
                            offset: 0,
                            count: 1,
                            callback: null,
                            state: null);
                    stream.EndWrite(write);
                    break;
                case StreamCancellationPoint.DisposeAsyncCompletion:
                    await stream.DisposeAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(cancellationPoint));
            }
        }
    }

    [Theory]
    [InlineData(StreamCancellationPoint.FlushAsync)]
    [InlineData(StreamCancellationPoint.ReadAsyncArray)]
    [InlineData(StreamCancellationPoint.ReadAsyncMemory)]
    [InlineData(StreamCancellationPoint.WriteAsyncArray)]
    [InlineData(StreamCancellationPoint.WriteAsyncMemory)]
    [InlineData(StreamCancellationPoint.CopyToAsync)]
    [InlineData(StreamCancellationPoint.DisposeAsync)]
    public void ObserveOpenReadCancellation_PreservesSynchronousAsyncOperationCancellation(
        StreamCancellationPoint cancellationPoint)
    {
        var cancellation =
            new OperationCanceledException("test");
        OperationCanceledException? observed = null;
        byte[] image = SelfBytes();
        var descriptor =
            ResolvedAssemblyReference.Create(
                ReadIdentity(image),
                path: null,
                () => new CancellationOnOperationStream(
                    image,
                    cancellation,
                    cancellationPoint),
                provenance:
                    AssemblyResolutionProvenance.Local("test"));
        ResolvedAssemblyReference decorated =
            descriptor.ObserveOpenReadCancellation(
                error => observed = error);
        using Stream stream = decorated.OpenRead();
        using var destination = new MemoryStream();
        byte[] buffer = new byte[1];

        Assert.Same(
            cancellation,
            Assert.Throws<OperationCanceledException>(
                () =>
                {
                    _ = cancellationPoint switch
                    {
                        StreamCancellationPoint.FlushAsync =>
                            stream.FlushAsync(
                                TestContext.Current.CancellationToken),
                        StreamCancellationPoint.ReadAsyncArray =>
                            stream.ReadAsync(
                                buffer,
                                offset: 0,
                                count: 1,
                                TestContext.Current.CancellationToken),
                        StreamCancellationPoint.ReadAsyncMemory =>
                            stream.ReadAsync(
                                    buffer.AsMemory(),
                                    TestContext.Current.CancellationToken)
                                .AsTask(),
                        StreamCancellationPoint.WriteAsyncArray =>
                            stream.WriteAsync(
                                buffer,
                                offset: 0,
                                count: 1,
                                TestContext.Current.CancellationToken),
                        StreamCancellationPoint.WriteAsyncMemory =>
                            stream.WriteAsync(
                                    buffer.AsMemory(),
                                    TestContext.Current.CancellationToken)
                                .AsTask(),
                        StreamCancellationPoint.CopyToAsync =>
                            stream.CopyToAsync(
                                destination,
                                bufferSize: 1,
                                TestContext.Current.CancellationToken),
                        StreamCancellationPoint.DisposeAsync =>
                            stream.DisposeAsync().AsTask(),
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(cancellationPoint)),
                    };
                }));
        Assert.Same(cancellation, observed);
    }

    [Fact]
    public void Register_MalformedForwarderInventory_IsTypedInvalidImage()
    {
        byte[] image = BuildInvalidForwarderImage();
        using var plan = new InspectionAcquisitionPlan();

        var rejected = Assert.IsType<CandidateRegistrationResult.Rejected>(
            plan.Register(
                Descriptor(
                    new AssemblyReferenceIdentity(
                        "Synthetic",
                        new Version(1, 0, 0, 0),
                        Culture: null,
                        PublicKeyToken: null),
                    () => image)));

        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
    }

    [Fact]
    public void Register_OutOfRangeForwarderTarget_IsTypedInvalidImage()
    {
        byte[] image = BuildForwarderImage(
            TypeAttributes.Public | Forwarder,
            targetRow: 4);
        using var plan = new InspectionAcquisitionPlan();

        var rejected = Assert.IsType<CandidateRegistrationResult.Rejected>(
            plan.Register(Descriptor(ReadIdentity(image), () => image)));

        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
    }

    [Fact]
    public void Register_UnreadableSource_IsTypedFailureAndCached()
    {
        byte[] image = SelfBytes();
        int opens = 0;
        var descriptor = ResolvedAssemblyReference.Create(
            ReadIdentity(image),
            path: null,
            openRead: () =>
            {
                Interlocked.Increment(ref opens);
                throw new IOException("test");
            },
            provenance: AssemblyResolutionProvenance.Local("test"));
        using var plan = new InspectionAcquisitionPlan();

        var first = Assert.IsType<CandidateRegistrationResult.Rejected>(
            plan.Register(descriptor));
        var second = Assert.IsType<CandidateRegistrationResult.Rejected>(
            plan.Register(descriptor));

        Assert.Same(first, second);
        Assert.Equal(CandidateOpenFailureKind.Unreadable, first.Failure.Kind);
        Assert.Equal(1, opens);
    }

    [Fact]
    public void Register_CandidateBudgetRejectsBeforeOpeningAnotherSource()
    {
        byte[] image = SelfBytes();
        AssemblyReferenceIdentity identity = ReadIdentity(image);
        int secondOpens = 0;
        using var plan = new InspectionAcquisitionPlan(
            new InspectionAcquisitionPlanOptions
            {
                MaxCandidates = 1,
            });

        Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(Descriptor(identity, () => image)));
        var rejected = Assert.IsType<CandidateRegistrationResult.Rejected>(
            plan.Register(
                Descriptor(
                    identity,
                    () =>
                    {
                        Interlocked.Increment(ref secondOpens);
                        return image;
                    })));

        Assert.Equal(CandidateOpenFailureKind.ResourceBudget, rejected.Failure.Kind);
        Assert.Equal(0, secondOpens);
        Assert.Equal(1, plan.CandidateCount);
    }

    [Fact]
    public void Register_ImageBudgetRejectsBeforeReadingSource()
    {
        byte[] image = SelfBytes();
        var stream = new CountingLengthStream(image.LongLength);
        var descriptor = ResolvedAssemblyReference.Create(
            ReadIdentity(image),
            path: null,
            openRead: () => stream,
            provenance: AssemblyResolutionProvenance.Local("test"));
        using var plan = new InspectionAcquisitionPlan(
            new InspectionAcquisitionPlanOptions
            {
                MaxInventoryImageBytes = image.LongLength - 1,
            });

        var rejected =
            Assert.IsType<CandidateRegistrationResult.Rejected>(
                plan.Register(descriptor));

        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            rejected.Failure.Kind);
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public async Task Register_ConcurrentSameDescriptor_IsSingleFlight()
    {
        byte[] image = SelfBytes();
        AssemblyReferenceIdentity identity = ReadIdentity(image);
        using var release = new ManualResetEventSlim();
        int opens = 0;
        var descriptor = Descriptor(
            identity,
            () =>
            {
                Interlocked.Increment(ref opens);
                release.Wait();
                return image;
            });
        using var plan = new InspectionAcquisitionPlan();

        Task<CandidateRegistrationResult>[] tasks =
            [.. Enumerable.Range(0, 12).Select(
                _ => StartConcurrent(() => plan.Register(descriptor)))];
        bool entered = SpinWait.SpinUntil(
            () => Volatile.Read(ref opens) == 1,
            TimeSpan.FromSeconds(5));
        release.Set();
        Assert.True(entered);
        CandidateRegistrationResult[] results = await Task.WhenAll(tasks);

        var first = Assert.IsType<CandidateRegistrationResult.Ready>(results[0]);
        Assert.All(
            results,
            result => Assert.Same(
                first,
                Assert.IsType<CandidateRegistrationResult.Ready>(result)));
        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task Register_SourceOpenConcurrencyNeverExceedsPlanLimit()
    {
        byte[] image = SelfBytes();
        AssemblyReferenceIdentity identity = ReadIdentity(image);
        const int DescriptorCount = 6;
        const int ExpectedQueuedOpens = DescriptorCount - 2;
        using var twoOpensEntered = new CountdownEvent(2);
        using var remainingOpensQueued =
            new CountdownEvent(ExpectedQueuedOpens);
        using var release = new ManualResetEventSlim();
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        int entered = 0;
        int queued = 0;
        int active = 0;
        int maximum = 0;
        var descriptors = Enumerable.Range(0, DescriptorCount)
            .Select(_ => Descriptor(
                identity,
                () =>
                {
                    int entrance = Interlocked.Increment(ref entered);
                    int current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximum, current);
                    if (entrance <= 2)
                        twoOpensEntered.Signal();
                    try
                    {
                        release.Wait();
                        return image;
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                }))
            .ToArray();
        using var plan = new InspectionAcquisitionPlan(
            new InspectionAcquisitionPlanOptions
            {
                MaxConcurrentSourceOpens = 2,
                TestHooks = new InspectionAcquisitionPlan.TestHooks
                {
                    SourceOpenWaitStarted = () =>
                    {
                        int wait = Interlocked.Increment(ref queued);
                        if (wait <= ExpectedQueuedOpens)
                            remainingOpensQueued.Signal();
                    },
                },
            });

        Task<CandidateRegistrationResult>[] tasks =
            [.. descriptors.Select(
                descriptor => StartConcurrent(() => plan.Register(descriptor)))];
        bool reachedLimit = false;
        bool allRemainingQueued = false;
        int observedMaximum = 0;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? cancellation =
            null;
        try
        {
            reachedLimit = twoOpensEntered.Wait(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            if (reachedLimit)
            {
                allRemainingQueued = remainingOpensQueued.Wait(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
                observedMaximum = Volatile.Read(ref maximum);
            }
        }
        catch (OperationCanceledException exception)
        {
            cancellation =
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                    exception);
        }
        finally
        {
            release.Set();
        }

        CandidateRegistrationResult[] results =
            await Task.WhenAll(tasks);
        cancellation?.Throw();

        Assert.True(reachedLimit);
        Assert.True(allRemainingQueued);
        Assert.Equal(2, observedMaximum);
        Assert.All(
            results,
            result => Assert.IsType<CandidateRegistrationResult.Ready>(result));
        Assert.Equal(descriptors.Length, entered);
        Assert.Equal(ExpectedQueuedOpens, queued);
        Assert.Equal(2, maximum);
    }

    [Fact]
    public void Session_IsLazySingleFlightPrefetchedAndPlanOwned()
    {
        byte[] image = SelfBytes();
        AssemblyReferenceIdentity identity = ReadIdentity(image);
        int opens = 0;
        int disposals = 0;
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            path: null,
            openRead: () =>
            {
                Interlocked.Increment(ref opens);
                return new DisposeTrackingMemoryStream(
                    image,
                    () => Interlocked.Increment(ref disposals));
            },
            provenance: AssemblyResolutionProvenance.Local("test"));
        var plan = new InspectionAcquisitionPlan();
        var registration = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(descriptor));

        Assert.Equal(1, opens);
        Assert.Equal(1, disposals);
        Assert.Equal(0, plan.RetainedImageBytes);

        var first = Assert.IsType<CandidateSessionResult.Ready>(
            plan.OpenSession(registration.Candidate));
        var second = Assert.IsType<CandidateSessionResult.Ready>(
            plan.OpenSession(registration.Candidate));

        Assert.Same(first, second);
        Assert.Same(first.Session, second.Session);
        Assert.Equal(2, opens);
        Assert.Equal(2, disposals);
        Assert.Equal(image.LongLength, plan.RetainedImageBytes);
        Assert.Equal(
            "ILInspector.Metadata.Tests",
            first.Session.AssemblyInfo().AssemblyName);
        Assert.IsType<TypeDeclarationResult.Forwarded>(
            first.Session.ProbeDeclaration(
                Name("ILInspector.Metadata", "MetadataTableProjector")));
        ResolvedAssemblyReference retained =
            Assert.IsType<ResolvedAssemblyReference>(
                plan.RetainAssemblyReference(
                    registration.Candidate));
        Assert.Same(
            descriptor.Registration,
            retained.Registration);
        using (Stream retainedStream = retained.OpenRead())
        {
            var retainedBytes = new byte[image.Length];
            retainedStream.ReadExactly(retainedBytes);
            Assert.Equal(image, retainedBytes);
        }
        Assert.Equal(2, opens);
        Assert.Equal(
            image.LongLength,
            plan.RetainedImageBytes);

        MethodBodySource methodBodies = first.Session.MethodBodies;
        plan.Dispose();

        Assert.Equal(0, plan.CandidateCount);
        Assert.Equal(0, plan.RetainedImageBytes);
        Assert.Throws<ObjectDisposedException>(
            () => methodBodies.EnumerateMethods());
        Assert.Throws<ObjectDisposedException>(
            () => plan.OpenSession(registration.Candidate));
        using Stream retainedAfterDispose =
            retained.OpenRead();
        var retainedAfterDisposeBytes =
            new byte[image.Length];
        retainedAfterDispose.ReadExactly(
            retainedAfterDisposeBytes);
        Assert.Equal(
            image,
            retainedAfterDisposeBytes);
    }

    [Fact]
    public void RetainedSnapshot_IsRegisteredWithoutReopeningOrCopyingSource()
    {
        byte[] image = SelfBytes();
        ImmutableArray<byte> content = [.. image];
        AssemblyReferenceIdentity identity = ReadIdentity(image);
        int opens = 0;
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            path: null,
            openRead: () =>
            {
                opens++;
                return new MemoryStream(image, writable: false);
            },
            provenance: AssemblyResolutionProvenance.Local("test"));
        AssemblyImageSnapshot snapshot =
            Assert.IsType<AssemblyImageSnapshotResult.Ready>(
                AssemblyImageSnapshot.FromRetainedContent(
                    descriptor,
                    content))
            .Snapshot;
        using var plan = new InspectionAcquisitionPlan();

        plan.RegisterRetainedSnapshot(descriptor, snapshot);
        var registration =
            Assert.IsType<CandidateRegistrationResult.Ready>(
                plan.Register(descriptor));
        var session = Assert.IsType<CandidateSessionResult.Ready>(
            plan.OpenSession(registration.Candidate));

        Assert.Equal(0, opens);
        Assert.Equal(content.Length, plan.RetainedImageBytes);
        Assert.Same(snapshot, session.Snapshot);
        Assert.Equal(
            "ILInspector.Metadata.Tests",
            session.Session.AssemblyInfo().AssemblyName);
    }

    [Fact]
    public void Session_RetainedImageBudgetReturnsTypedFailure()
    {
        byte[] image = SelfBytes();
        AssemblyReferenceIdentity identity =
            ReadIdentity(image);
        int opens = 0;
        int disposals = 0;
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            path: null,
            openRead: () =>
            {
                Interlocked.Increment(ref opens);
                return new DisposeTrackingMemoryStream(
                    image,
                    () => Interlocked.Increment(ref disposals));
            },
            provenance: AssemblyResolutionProvenance.Local("test"));
        using var plan = new InspectionAcquisitionPlan(
            new InspectionAcquisitionPlanOptions
            {
                MaxRetainedImageBytes = image.LongLength,
            });
        var firstRegistration = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(descriptor));
        var secondRegistration = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(
                ResolvedAssemblyReference.Create(
                    identity,
                    path: null,
                    openRead: () =>
                    {
                        Interlocked.Increment(ref opens);
                        return new DisposeTrackingMemoryStream(
                            image,
                            () => Interlocked.Increment(ref disposals));
                    },
                    provenance: AssemblyResolutionProvenance.Local("test"))));

        Assert.IsType<CandidateSessionResult.Ready>(
            plan.OpenSession(firstRegistration.Candidate));
        var rejected = Assert.IsType<CandidateSessionResult.Rejected>(
            plan.OpenSession(secondRegistration.Candidate));

        Assert.Equal(CandidateOpenFailureKind.ResourceBudget, rejected.Failure.Kind);
        Assert.Equal(4, opens);
        Assert.Equal(4, disposals);
        Assert.Equal(image.LongLength, plan.RetainedImageBytes);
    }

    [Fact]
    public void Session_ParsesTheBytesCopiedBeforeSourceMutation()
    {
        Guid mvid = Guid.NewGuid();
        byte[] first =
            BuildSimpleAssembly("Changing", "First", mvid);
        byte[] changed =
            BuildSimpleAssembly("Changing", "Other", mvid);
        Assert.Equal(first.Length, changed.Length);
        var descriptor = ResolvedAssemblyReference.Create(
            ReadIdentity(first),
            path: null,
            openRead: () =>
                new RewindSwitchingStream(
                    first,
                    changed),
            provenance: AssemblyResolutionProvenance.Local("test"));
        using var plan = new InspectionAcquisitionPlan();
        var registration =
            Assert.IsType<CandidateRegistrationResult.Ready>(
                plan.Register(descriptor));

        AssemblyInspectionSession session =
            Assert.IsType<CandidateSessionResult.Ready>(
                    plan.OpenSession(registration.Candidate))
                .Session;

        Assert.IsType<TypeDeclarationResult.Defined>(
            session.ProbeDeclaration(Name("", "First")));
        Assert.IsType<TypeDeclarationResult.Missing>(
            session.ProbeDeclaration(Name("", "Other")));
    }

    [Fact]
    public void Session_DistinctDeclarationRequestsDoNotRescanTypeTable()
    {
        const int TypeCount = 40_000;
        byte[] image = BuildManyTypesAssembly(TypeCount);
        MetadataTypeDefinitionName[] names =
        [
            .. Enumerable.Range(0, TypeCount)
                .Select(index => Name("N", $"Type{index}")),
        ];
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(image, writable: false));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        foreach (MetadataTypeDefinitionName name in names)
        {
            Assert.IsType<TypeDeclarationResult.Defined>(
                session.ProbeDeclaration(name));
        }
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Resolving {TypeCount} distinct declarations took "
                + $"{stopwatch.Elapsed}.");
    }

    [Fact]
    public void DeclarationIndex_UniqueLeafNamesUseCompactEntryStorage()
    {
        const int TypeCount = 40_000;
        byte[] image = BuildManyTypesAssembly(TypeCount);
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();

        long before = GC.GetAllocatedBytesForCurrentThread();
        MetadataTypeDeclarationProbe.Index index =
            MetadataTypeDeclarationProbe.CreateIndex(reader);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsType<TypeDeclarationResult.Defined>(
            index.Probe(Name("N", $"Type{TypeCount - 1}")));
        Assert.InRange(allocated, 0, 3 * 1024 * 1024);
    }

    [Fact]
    public void Session_WhenSourceChangesAfterInventory_RejectsImage()
    {
        byte[] inventoried = BuildValidForwarderImage();
        byte[] changed = BuildValidForwarderImage();
        int opens = 0;
        using var plan = new InspectionAcquisitionPlan();
        var descriptor = Descriptor(
            ReadIdentity(inventoried),
            () => Interlocked.Increment(ref opens) == 1
                ? inventoried
                : changed);
        var registration = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(descriptor));

        var rejected = Assert.IsType<CandidateSessionResult.Rejected>(
            plan.OpenSession(registration.Candidate));

        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
        Assert.Equal(0, plan.RetainedImageBytes);
    }

    [Fact]
    public void Session_WhenPostOpenIdentityReadThrows_ReleasesImage()
    {
        byte[] inventoried = BuildValidForwarderImage();
        byte[] changed = BuildModuleImage();
        int opens = 0;
        using var plan = new InspectionAcquisitionPlan();
        var descriptor = Descriptor(
            ReadIdentity(inventoried),
            () => Interlocked.Increment(ref opens) == 1
                ? inventoried
                : changed);
        var registration = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(descriptor));

        var rejected = Assert.IsType<CandidateSessionResult.Rejected>(
            plan.OpenSession(registration.Candidate));

        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
        Assert.Equal(0, plan.RetainedImageBytes);
    }

    [Fact]
    public void Session_WhenUnexpectedOpenThrows_ReleasesImage()
    {
        byte[] image = SelfBytes();
        int opens = 0;
        using var plan = new InspectionAcquisitionPlan();
        var descriptor = ResolvedAssemblyReference.Create(
            ReadIdentity(image),
            path: null,
            openRead: () =>
                Interlocked.Increment(ref opens) == 1
                    ? new MemoryStream(image, writable: false)
                    : new ThrowingDisposeMemoryStream(image),
            provenance: AssemblyResolutionProvenance.Local("test"));
        var registration =
            Assert.IsType<CandidateRegistrationResult.Ready>(
                plan.Register(descriptor));

        Assert.Throws<InvalidOperationException>(
            () => plan.OpenSession(registration.Candidate));

        Assert.Equal(0, plan.RetainedImageBytes);
    }

    [Fact]
    public async Task Session_ConcurrentRequestsShareOneOpen()
    {
        byte[] image = SelfBytes();
        int opens = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var plan = new InspectionAcquisitionPlan();
        var descriptor = Descriptor(
            ReadIdentity(image),
            () =>
            {
                if (Interlocked.Increment(ref opens) == 2)
                {
                    entered.Set();
                    release.Wait();
                }

                return image;
            });
        var registration = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(descriptor));

        Task<CandidateSessionResult>[] tasks =
        [
            .. Enumerable.Range(0, 8)
                .Select(_ => StartConcurrent(
                    () => plan.OpenSession(registration.Candidate))),
        ];
        bool sharedOpenStarted = entered.Wait(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        release.Set();
        Assert.True(sharedOpenStarted);
        CandidateSessionResult[] results = await Task.WhenAll(tasks);

        CandidateSessionResult.Ready first =
            Assert.IsType<CandidateSessionResult.Ready>(results[0]);
        Assert.All(
            results,
            result => Assert.Same(
                first,
                Assert.IsType<CandidateSessionResult.Ready>(result)));
        Assert.Equal(2, opens);
    }

    [Fact]
    public async Task Dispose_WaitsForInFlightSessionAndOwnsItsResult()
    {
        byte[] image = SelfBytes();
        int opens = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var plan = new InspectionAcquisitionPlan();
        var descriptor = Descriptor(
            ReadIdentity(image),
            () =>
            {
                if (Interlocked.Increment(ref opens) == 2)
                {
                    entered.Set();
                    release.Wait();
                }

                return image;
            });
        var registration = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(descriptor));
        Task<CandidateSessionResult> openTask = StartConcurrent(
            () => plan.OpenSession(registration.Candidate));
        bool openStarted = entered.Wait(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        if (!openStarted)
        {
            release.Set();
            await openTask;
            plan.Dispose();
        }
        Assert.True(openStarted);

        Task disposeTask = StartConcurrent(plan.Dispose);
        try
        {
            // Rejection is the observable that Dispose has set _disposed before waiting.
            bool disposeStarted = SpinWait.SpinUntil(
                () =>
                {
                    try
                    {
                        plan.Register(descriptor);
                        return false;
                    }
                    catch (ObjectDisposedException)
                    {
                        return true;
                    }
                },
                TimeSpan.FromSeconds(5));
            Assert.True(disposeStarted);
            Assert.False(disposeTask.IsCompleted);
        }
        finally
        {
            release.Set();
            await Task.WhenAll(openTask, disposeTask);
        }

        var ready = Assert.IsType<CandidateSessionResult.Ready>(
            await openTask);
        Assert.Throws<ObjectDisposedException>(
            () => ready.Session.MethodBodies);
        plan.Dispose();
    }

    [Fact]
    public void Session_RejectsCandidateFromAnotherPlan()
    {
        byte[] image = SelfBytes();
        using var firstPlan = new InspectionAcquisitionPlan();
        using var secondPlan = new InspectionAcquisitionPlan();
        var registration = Assert.IsType<CandidateRegistrationResult.Ready>(
            firstPlan.Register(
                Descriptor(ReadIdentity(image), () => image)));

        Assert.Throws<ArgumentException>(
            () => secondPlan.OpenSession(registration.Candidate));
    }

    [Fact]
    public void AcquisitionResults_DoNotExposeReadersOrHandles()
    {
        Type[] types =
        [
            typeof(AssemblyInventorySnapshot),
            typeof(ResolvedAssemblyCandidate),
            typeof(ResolvedAssemblyReference),
            typeof(AssemblyAcquisitionRegistration),
            typeof(CandidateOpenFailure),
        ];

        foreach (Type type in types)
        {
            foreach (PropertyInfo property in type.GetProperties())
                AssertClosedPropertyType(property.PropertyType);
        }
    }

    static ResolvedAssemblyReference Descriptor(
        AssemblyReferenceIdentity identity,
        Func<byte[]> image) =>
        ResolvedAssemblyReference.Create(
            identity,
            path: null,
            openRead: () => new MemoryStream(image(), writable: false),
            provenance: AssemblyResolutionProvenance.Local("test"));

    static ArtifactAcquisitionRegistration RegisterArtifact(
        Func<Stream> openRead)
    {
        var authority = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            authority.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
               authority.BeginContribution(admission))
        {
            contribution = scope.Register(
                TestArtifactProvenance.Instance,
                _ => openRead());
        }

        authority.CreateRetainedContent(
            contribution.Registration,
            _ => openRead());
        authority.CompleteAdmission(admission);
        return contribution.Registration;
    }

    sealed class TestArtifactProvenance : IArtifactProvenance
    {
        public static TestArtifactProvenance Instance { get; } = new();
    }

    // These callers intentionally block on test gates, so dedicated threads keep the
    // test independent of ThreadPool injection timing on low-core CI runners.
    static Task StartConcurrent(Action action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    static Task<T> StartConcurrent<T>(Func<T> action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    static byte[] SelfBytes() => File.ReadAllBytes(SelfPath);

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            peReader.GetMetadataReader());
    }

    static Guid ReadModuleVersionId(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [.. segments])).Name;

    static byte[] BuildInvalidForwarderImage() =>
        BuildForwarderImage(TypeAttributes.Public);

    static byte[] BuildValidForwarderImage(
        int forwarderCount = 1,
        int assemblyReferenceCount = 1) =>
        BuildForwarderImage(
            TypeAttributes.Public | Forwarder,
            forwarderCount,
            assemblyReferenceCount);

    static byte[] BuildForwarderImage(
        TypeAttributes attributes,
        int forwarderCount = 1,
        int assemblyReferenceCount = 1,
        int? targetRow = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
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
        AssemblyReferenceHandle target = default;
        for (int i = 0; i < assemblyReferenceCount; i++)
        {
            AssemblyReferenceHandle added = metadata.AddAssemblyReference(
                metadata.GetOrAddString("Target"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
            if (i == 0)
                target = added;
        }

        if (targetRow is int row)
            target = MetadataTokens.AssemblyReferenceHandle(row);
        for (int i = 0; i < forwarderCount; i++)
        {
            metadata.AddExportedType(
                attributes,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(i == 0 ? "Type" : $"Type{i}"),
                target,
                typeDefinitionId: 0);
        }

        return Serialize(metadata);
    }

    static byte[] BuildModuleImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Changed.netmodule"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildSimpleAssembly(
        string assemblyName,
        string typeName,
        Guid mvid)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString(
                    $"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(mvid),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString(typeName),
            baseType: default,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildManyTypesAssembly(int typeCount)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString("ManyTypes.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ManyTypes"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        StringHandle typeNamespace =
            metadata.GetOrAddString("N");
        for (int i = 0; i < typeCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                typeNamespace,
                metadata.GetOrAddString($"Type{i}"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }

        return Serialize(metadata);
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildNativePeImage()
    {
        var image = new byte[0x400];
        using var stream = new MemoryStream(image, writable: true);
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0x5A4D);
        stream.Position = 0x3C;
        writer.Write(0x80);
        stream.Position = 0x80;
        writer.Write(0x00004550u);
        writer.Write((ushort)0x8664);
        writer.Write((ushort)1);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((ushort)0xF0);
        writer.Write((ushort)0x2022);
        writer.Write((ushort)0x20B);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0x200u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0x1000u);
        writer.Write(0x140000000ul);
        writer.Write(0x1000u);
        writer.Write(0x200u);
        writer.Write((ushort)6);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)6);
        writer.Write((ushort)0);
        writer.Write(0u);
        writer.Write(0x2000u);
        writer.Write(0x200u);
        writer.Write(0u);
        writer.Write((ushort)3);
        writer.Write((ushort)0x8160);
        writer.Write(0x100000ul);
        writer.Write(0x1000ul);
        writer.Write(0x100000ul);
        writer.Write(0x1000ul);
        writer.Write(0u);
        writer.Write(16u);
        for (int i = 0; i < 16; i++)
        {
            writer.Write(0u);
            writer.Write(0u);
        }

        writer.Write(
            new byte[]
            {
                (byte)'.',
                (byte)'t',
                (byte)'e',
                (byte)'x',
                (byte)'t',
                0,
                0,
                0,
            });
        writer.Write(1u);
        writer.Write(0x1000u);
        writer.Write(0x200u);
        writer.Write(0x200u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0x60000020u);
        image[0x200] = 0xC3;
        return image;
    }

    static void UpdateMaximum(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (current >= value)
                return;
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    static void AssertClosedPropertyType(Type type)
    {
        Assert.NotEqual(typeof(MetadataReader), type);
        Assert.NotEqual(typeof(PEReader), type);
        Assert.False(
            type.Namespace == "System.Reflection.Metadata"
            && type.Name.EndsWith("Handle", StringComparison.Ordinal));

        if (type.HasElementType)
            AssertClosedPropertyType(type.GetElementType()!);
        foreach (Type argument in type.GetGenericArguments())
            AssertClosedPropertyType(argument);
    }

    sealed class DisposeTrackingMemoryStream(
        byte[] image,
        Action disposed) : MemoryStream(image, writable: false)
    {
        bool _disposed;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                disposed();
            }
            base.Dispose(disposing);
        }
    }

    sealed class ThrowingDisposeMemoryStream(byte[] image)
        : MemoryStream(image, writable: false)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                throw new InvalidOperationException(
                    "Synthetic disposal failure.");
            }
        }
    }

    public enum StreamCancellationPoint
    {
        Open,
        CanRead,
        Read,
        FlushAsync,
        ReadAsyncArray,
        ReadAsyncMemory,
        WriteAsyncArray,
        WriteAsyncMemory,
        CopyTo,
        CopyToAsync,
        DisposeAsync,
        BeginRead,
        EndRead,
        BeginWrite,
        EndWrite,
        FlushAsyncCompletion,
        ReadAsyncArrayCompletion,
        ReadAsyncMemoryCompletion,
        WriteAsyncArrayCompletion,
        WriteAsyncMemoryCompletion,
        CopyToAsyncCompletion,
        DisposeAsyncCompletion,
    }

    sealed class CancellationOnOperationStream(
        byte[] image,
        OperationCanceledException cancellation,
        StreamCancellationPoint cancellationPoint)
        : MemoryStream(image, writable: true)
    {
        public override bool CanRead =>
            cancellationPoint == StreamCancellationPoint.CanRead
                ? throw cancellation
                : base.CanRead;

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
            => cancellationPoint == StreamCancellationPoint.Read
                ? throw cancellation
                : base.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) =>
            cancellationPoint == StreamCancellationPoint.Read
                ? throw cancellation
                : base.Read(buffer);

        public override Task FlushAsync(
            CancellationToken cancellationToken)
        {
            if (cancellationPoint == StreamCancellationPoint.FlushAsync)
                throw cancellation;
            if (cancellationPoint
                == StreamCancellationPoint.FlushAsyncCompletion)
            {
                return Task.FromException(cancellation);
            }
            return base.FlushAsync(cancellationToken);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            if (cancellationPoint
                == StreamCancellationPoint.ReadAsyncArray)
            {
                throw cancellation;
            }
            if (cancellationPoint
                == StreamCancellationPoint.ReadAsyncArrayCompletion)
            {
                return Task.FromException<int>(cancellation);
            }
            return base.ReadAsync(
                buffer,
                offset,
                count,
                cancellationToken);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (cancellationPoint
                == StreamCancellationPoint.ReadAsyncMemory)
            {
                throw cancellation;
            }
            if (cancellationPoint
                == StreamCancellationPoint.ReadAsyncMemoryCompletion)
            {
                return ValueTask.FromException<int>(cancellation);
            }
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            if (cancellationPoint
                == StreamCancellationPoint.WriteAsyncArray)
            {
                throw cancellation;
            }
            if (cancellationPoint
                == StreamCancellationPoint.WriteAsyncArrayCompletion)
            {
                return Task.FromException(cancellation);
            }
            return base.WriteAsync(
                buffer,
                offset,
                count,
                cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (cancellationPoint
                == StreamCancellationPoint.WriteAsyncMemory)
            {
                throw cancellation;
            }
            if (cancellationPoint
                == StreamCancellationPoint.WriteAsyncMemoryCompletion)
            {
                return ValueTask.FromException(cancellation);
            }
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override void CopyTo(
            Stream destination,
            int bufferSize)
        {
            if (cancellationPoint == StreamCancellationPoint.CopyTo)
                throw cancellation;
            base.CopyTo(destination, bufferSize);
        }

        public override Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            if (cancellationPoint == StreamCancellationPoint.CopyToAsync)
                throw cancellation;
            if (cancellationPoint
                == StreamCancellationPoint.CopyToAsyncCompletion)
            {
                return Task.FromException(cancellation);
            }
            return base.CopyToAsync(
                destination,
                bufferSize,
                cancellationToken);
        }

        public override ValueTask DisposeAsync()
        {
            if (cancellationPoint == StreamCancellationPoint.DisposeAsync)
                throw cancellation;
            if (cancellationPoint
                == StreamCancellationPoint.DisposeAsyncCompletion)
            {
                return ValueTask.FromException(cancellation);
            }
            return base.DisposeAsync();
        }

        public override IAsyncResult BeginRead(
            byte[] buffer,
            int offset,
            int count,
            AsyncCallback? callback,
            object? state) =>
            cancellationPoint == StreamCancellationPoint.BeginRead
                ? throw cancellation
                : base.BeginRead(
                    buffer,
                    offset,
                    count,
                    callback,
                    state);

        public override int EndRead(
            IAsyncResult asyncResult) =>
            cancellationPoint == StreamCancellationPoint.EndRead
                ? throw cancellation
                : base.EndRead(asyncResult);

        public override IAsyncResult BeginWrite(
            byte[] buffer,
            int offset,
            int count,
            AsyncCallback? callback,
            object? state) =>
            cancellationPoint == StreamCancellationPoint.BeginWrite
                ? throw cancellation
                : base.BeginWrite(
                    buffer,
                    offset,
                    count,
                    callback,
                    state);

        public override void EndWrite(
            IAsyncResult asyncResult)
        {
            if (cancellationPoint == StreamCancellationPoint.EndWrite)
                throw cancellation;
            base.EndWrite(asyncResult);
        }
    }

    sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    sealed class CountingLengthStream(long length) : Stream
    {
        long _position;

        internal long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            int read =
                (int)Math.Min(
                    count,
                    length - _position);
            Array.Clear(buffer, offset, read);
            _position += read;
            BytesRead += read;
            return read;
        }

        public override long Seek(
            long offset,
            SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(origin)),
            };
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    sealed class RewindSwitchingStream(
        byte[] initial,
        byte[] changed) : Stream
    {
        long _position;
        bool _reachedEnd;
        bool _changed;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => initial.LongLength;
        public override long Position
        {
            get => _position;
            set
            {
                if (_reachedEnd && value == 0)
                    _changed = true;
                _position = value;
            }
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            byte[] source =
                _changed
                    ? changed
                    : initial;
            int read =
                (int)Math.Min(
                    count,
                    source.LongLength - _position);
            source.AsSpan((int)_position, read)
                .CopyTo(buffer.AsSpan(offset, read));
            _position += read;
            _reachedEnd |= _position == source.LongLength;
            return read;
        }

        public override long Seek(
            long offset,
            SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(origin)),
            };
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}
