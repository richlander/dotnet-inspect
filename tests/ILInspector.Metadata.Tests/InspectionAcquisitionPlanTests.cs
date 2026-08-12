using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
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
        using var release = new ManualResetEventSlim();
        int entered = 0;
        int active = 0;
        int maximum = 0;
        var descriptors = Enumerable.Range(0, 6)
            .Select(_ => Descriptor(
                identity,
                () =>
                {
                    Interlocked.Increment(ref entered);
                    int current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximum, current);
                    release.Wait();
                    Interlocked.Decrement(ref active);
                    return image;
                }))
            .ToArray();
        using var plan = new InspectionAcquisitionPlan(
            new InspectionAcquisitionPlanOptions
            {
                MaxConcurrentSourceOpens = 2,
            });

        Task<CandidateRegistrationResult>[] tasks =
            [.. descriptors.Select(
                descriptor => StartConcurrent(() => plan.Register(descriptor)))];
        bool reachedLimit = SpinWait.SpinUntil(
            () => Volatile.Read(ref entered) == 2,
            TimeSpan.FromSeconds(5));
        int observedMaximum = Volatile.Read(ref maximum);
        release.Set();
        Assert.True(reachedLimit);
        Assert.Equal(2, observedMaximum);
        CandidateRegistrationResult[] results = await Task.WhenAll(tasks);

        Assert.All(
            results,
            result => Assert.IsType<CandidateRegistrationResult.Ready>(result));
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

        MethodBodySource methodBodies = first.Session.MethodBodies;
        plan.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => methodBodies.EnumerateMethods());
        Assert.Throws<ObjectDisposedException>(
            () => plan.OpenSession(registration.Candidate));
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
