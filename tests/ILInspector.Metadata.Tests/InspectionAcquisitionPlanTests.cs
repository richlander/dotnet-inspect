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

        Assert.Equal(package, samePackage);
        Assert.NotEqual(package, platform);
        Assert.Equal(
            "Example.Package",
            Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(package)
                .PackageId);
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
        int opens = 0;
        int disposals = 0;
        var descriptor = ResolvedAssemblyReference.Create(
            ReadIdentity(image),
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
                MaxRetainedImageBytes = image.LongLength - 1,
            });
        var registration = Assert.IsType<CandidateRegistrationResult.Ready>(
            plan.Register(descriptor));

        var rejected = Assert.IsType<CandidateSessionResult.Rejected>(
            plan.OpenSession(registration.Candidate));

        Assert.Equal(CandidateOpenFailureKind.ResourceBudget, rejected.Failure.Kind);
        Assert.Equal(2, opens);
        Assert.Equal(2, disposals);
        Assert.Equal(0, plan.RetainedImageBytes);
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
}
