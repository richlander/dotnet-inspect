using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataAdmissionCleanupTests
{
    [Fact]
    public void TypeDeclarationInventory_CleanupCannotReplaceFormatRejection()
    {
        ThrowingDisposeMemoryStream? opened = null;
        AssemblyTypeDeclarationInventoryOutcome outcome =
            AssemblyTypeDeclarationInventoryReader.Read(
                ResolvedAssemblyReference.Create(
                    Identity(),
                    path: null,
                    () => opened = new ThrowingDisposeMemoryStream(
                        BuildManagedWindowsMetadata()),
                    AssemblyResolutionProvenance.Local(
                        "format admission test")));

        var rejected =
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.UnsupportedMetadataFormat,
            rejected.Failure.Kind);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void TypeDeclarationInventory_PreservesMalformedRootReason()
    {
        ThrowingDisposeMemoryStream? opened = null;
        AssemblyTypeDeclarationInventoryOutcome outcome =
            AssemblyTypeDeclarationInventoryReader.Read(
                ResolvedAssemblyReference.Create(
                    Identity(),
                    path: null,
                    () => opened = new ThrowingDisposeMemoryStream(
                        BuildMalformedMetadataRoot()),
                    AssemblyResolutionProvenance.Local(
                        "format admission test")));

        var rejected =
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            rejected.Failure.MetadataRootReason);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void TypeDeclarationInventory_CleanupCannotReplaceNoMetadataRejection()
    {
        AssemblyTypeDeclarationInventoryOutcome outcome =
            AssemblyTypeDeclarationInventoryReader.Read(
                Descriptor(BuildNoMetadataImage()));

        var rejected =
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
    }

    [Fact]
    public void ApiSurface_CleanupCannotReplaceFormatRejection()
    {
        var stream = new DisposeCountingMemoryStream(
            BuildManagedWindowsMetadata());

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => AssemblyReader.ExtractApiSurface(
                stream));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public void ApiSummary_CleanupCannotReplaceFormatRejection()
    {
        Assert.Throws<UnsupportedMetadataFormatException>(
            () => AssemblyReader.ExtractApiSummarySurface(
                new ThrowingDisposeMemoryStream(
                    BuildManagedWindowsMetadata())));
    }

    [Fact]
    public void ApiSurface_NoMetadataCleanupCannotReplaceNoResult()
    {
        Assert.Null(
            AssemblyReader.ExtractApiSurface(
                new ThrowingDisposeMemoryStream(
                    BuildNoMetadataImage())));
    }

    [Fact]
    public void ApiSummary_NoMetadataCleanupCannotReplaceNoResult()
    {
        Assert.Null(
            AssemblyReader.ExtractApiSummarySurface(
                new ThrowingDisposeMemoryStream(
                    BuildNoMetadataImage())));
    }

    [Fact]
    public void ApiSurface_ReaderConstructionFailureDisposesStreamOnce()
    {
        var stream = new UnreadableDisposeCountingStream();

        Assert.Null(AssemblyReader.ExtractApiSurface(stream));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public void ApiSummary_ReaderConstructionFailureDisposesStreamOnce()
    {
        var stream = new UnreadableDisposeCountingStream();

        Assert.Null(AssemblyReader.ExtractApiSummarySurface(stream));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public void FallbackIdentity_CleanupCannotPreventFallback()
    {
        AssertFallback(BuildManagedWindowsMetadata());
    }

    [Fact]
    public void FallbackIdentity_NoMetadataCleanupCannotPreventFallback()
    {
        AssertFallback(BuildNoMetadataImage());
    }

    [Fact]
    public void FallbackIdentity_ModuleCleanupCannotPreventFallback()
    {
        AssertFallback(BuildManagedModule());
    }

    [Fact]
    public void StreamIfManaged_NoMetadataCleanupCannotReplaceRejection()
    {
        ThrowingDisposeMemoryStream? opened = null;
        ResolvedAssemblyReference? assembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => opened = new ThrowingDisposeMemoryStream(
                    BuildNoMetadataImage()),
                AssemblyResolutionProvenance.Local(
                    "format admission test"));

        Assert.Null(assembly);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void Snapshot_CleanupCannotReplaceDirectRejection()
    {
        ThrowingDisposeMemoryStream? opened = null;
        AssemblyImageSnapshotResult outcome =
            AssemblyImageSnapshot.Open(
                ResolvedAssemblyReference.Create(
                    Identity(),
                    path: null,
                    () => opened = new ThrowingDisposeMemoryStream(
                        BuildManagedWindowsMetadata()),
                    AssemblyResolutionProvenance.Local(
                        "format admission test")),
                static _ => false,
                static _ => { });

        var rejected =
            Assert.IsType<AssemblyImageSnapshotResult.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            rejected.Failure.Kind);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void Snapshot_PreservesMalformedRootReason()
    {
        ThrowingDisposeMemoryStream? opened = null;
        AssemblyImageSnapshotResult outcome =
            AssemblyImageSnapshot.Open(
                ResolvedAssemblyReference.Create(
                    Identity(),
                    path: null,
                    () => opened = new ThrowingDisposeMemoryStream(
                        BuildMalformedMetadataRoot()),
                    AssemblyResolutionProvenance.Local(
                        "format admission test")),
                static _ => true,
                static _ => { });

        var rejected =
            Assert.IsType<AssemblyImageSnapshotResult.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            rejected.Failure.MetadataRootReason);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void RetainedSnapshot_PreservesMalformedRootReason()
    {
        byte[] image = BuildMalformedMetadataRoot();
        AssemblyImageSnapshotResult outcome =
            AssemblyImageSnapshot.FromRetainedContent(
                ResolvedAssemblyReference.Create(
                    Identity(),
                    path: null,
                    () => new MemoryStream(image, writable: false),
                    AssemblyResolutionProvenance.Local(
                        "format admission test")),
                ImmutableArray.Create(image));

        var rejected =
            Assert.IsType<AssemblyImageSnapshotResult.Rejected>(
                outcome);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            rejected.Failure.MetadataRootReason);
    }

    [Fact]
    public void CandidateOpenFailure_PreservesTwoPositionRecordContract()
    {
        Type type = typeof(CandidateOpenFailure);

        Assert.NotNull(type.GetConstructor(
            [typeof(CandidateOpenFailureKind), typeof(string)]));
        System.Reflection.MethodInfo deconstruct = Assert.Single(
            type.GetMethods(),
            method => method.Name == "Deconstruct");
        Assert.Equal(2, deconstruct.GetParameters().Length);
    }

    [Fact]
    public void OpenPrefetched_FormatRejectionDisposesStreamOnce()
    {
        var stream = new DisposeCountingMemoryStream(
            BuildManagedWindowsMetadata());

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => AssemblyInspectionSession.OpenPrefetched(stream));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public void SurfaceClassification_PreservesMalformedRootReason()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"malformed-surface-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, BuildMalformedMetadataRoot());
        try
        {
            var rejected =
                Assert.IsType<AssemblySurfaceClassificationOutcome.Rejected>(
                    AssemblySurfaceClassifier.Classify(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "format admission test")));
            Assert.Equal(
                MetadataRootMalformedReason.InvalidSignature,
                rejected.Failure.MetadataRootReason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AssemblyInspector_CleanupCannotReplaceFormatRejection()
    {
        ThrowingDisposeMemoryStream? opened = null;
        var assembly = ResolvedAssemblyReference.Create(
            Identity(),
            path: null,
            () => opened = new ThrowingDisposeMemoryStream(
                BuildManagedWindowsMetadata()),
            AssemblyResolutionProvenance.Local(
                "format admission test"));

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => AssemblyInspector.ExtractReferenceIdentitiesAndCompany(
                assembly));
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void AssemblyInspector_NoMetadataCleanupCannotReplaceNoResult()
    {
        ThrowingDisposeMemoryStream? opened = null;
        var assembly = ResolvedAssemblyReference.Create(
            Identity(),
            path: null,
            () => opened = new ThrowingDisposeMemoryStream(
                BuildNoMetadataImage()),
            AssemblyResolutionProvenance.Local(
                "format admission test"));

        var (references, company) =
            AssemblyInspector.ExtractReferenceIdentitiesAndCompany(
                assembly);

        Assert.Empty(references);
        Assert.Null(company);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void StreamScanners_CleanupCannotReplaceFormatRejection()
    {
        Action<Stream>[] scans =
        [
            stream => _ = ResourceScanner.Scan(stream),
            stream => _ = ResourceScanner.ExtractAll(
                stream,
                Path.GetTempPath()),
            stream => _ = MethodClassificationScanner.Scan(stream),
            stream => _ = ExtensionMethodScanner.FindAllExtensions(
                stream).ToList(),
            stream => _ = ExtensionMethodScanner.FindExtensions(
                stream,
                "System.String").ToList(),
        ];

        foreach (Action<Stream> scan in scans)
            AssertFormatRejectionSurvivesCleanup(scan);
    }

    [Fact]
    public void StreamScanners_NoMetadataCleanupCannotReplaceNoResult()
    {
        Action<Stream>[] scans =
        [
            stream => Assert.Empty(ResourceScanner.Scan(stream)),
            stream => Assert.Empty(ResourceScanner.ExtractAll(
                stream,
                Path.GetTempPath())),
            stream => Assert.Empty(MethodClassificationScanner.Scan(stream)),
            stream => Assert.Empty(
                ExtensionMethodScanner.FindAllExtensions(stream)),
            stream => Assert.Empty(
                ExtensionMethodScanner.FindExtensions(
                    stream,
                    "System.String")),
        ];

        foreach (Action<Stream> scan in scans)
            AssertNoMetadataResultSurvivesCleanup(scan);
    }

    static void AssertFormatRejectionSurvivesCleanup(
        Action<Stream> scan)
    {
        var stream = new ThrowingDisposeMemoryStream(
            BuildManagedWindowsMetadata());

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => scan(stream));
        Assert.Equal(1, stream.DisposeCount);
    }

    static void AssertNoMetadataResultSurvivesCleanup(
        Action<Stream> scan)
    {
        var stream = new ThrowingDisposeMemoryStream(
            BuildNoMetadataImage());

        scan(stream);

        Assert.Equal(1, stream.DisposeCount);
    }

    static void AssertFallback(byte[] image)
    {
        AssemblyReferenceIdentity fallback = Identity();
        ThrowingDisposeMemoryStream? opened = null;
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromStreamWithFallbackIdentity(
                () => opened = new ThrowingDisposeMemoryStream(image),
                fallback,
                AssemblyResolutionProvenance.Local("format admission test"),
                out bool usedFallback);
        Assert.True(usedFallback);
        Assert.Equal(fallback, assembly.Identity);
        Assert.Equal(1, opened!.DisposeCount);
    }

    static ResolvedAssemblyReference Descriptor(byte[] image)
    {
        return ResolvedAssemblyReference.Create(
            Identity(),
            path: null,
            () => new ThrowingDisposeMemoryStream(image),
            AssemblyResolutionProvenance.Local("format admission test"));
    }

    static AssemblyReferenceIdentity Identity() =>
        new(
            "Unsupported",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);

    internal static byte[] BuildManagedWindowsMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Unsupported.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Unsupported"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0),
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

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                "WindowsRuntime 1.4;CLR v4.0.30319",
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildManagedModule()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Module.netmodule"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
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
        return image.ToArray();
    }

    internal static byte[] BuildNoMetadataImage()
    {
        byte[] image = BuildManagedModule();
        using var peReader = new PEReader(
            ImmutableArray.Create(image));
        PEHeader peHeader = peReader.PEHeaders.PEHeader!;
        int directoryBase =
            peReader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        image.AsSpan(directoryBase + (14 * 8), 8).Clear();
        return image;
    }

    internal static byte[] BuildMalformedMetadataRoot()
    {
        byte[] image = BuildManagedWindowsMetadata();
        using var peReader = new PEReader(
            ImmutableArray.Create(image));
        image.AsSpan(
            peReader.PEHeaders.MetadataStartOffset,
            sizeof(uint)).Clear();
        return image;
    }

    internal sealed class ThrowingDisposeMemoryStream(byte[] image)
        : MemoryStream(image, writable: false)
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
            if (disposing)
            {
                throw new InvalidOperationException(
                    "Synthetic disposal failure.");
            }
        }
    }

    sealed class DisposeCountingMemoryStream(byte[] image)
        : MemoryStream(image, writable: false)
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }

    sealed class UnreadableDisposeCountingStream : MemoryStream
    {
        public int DisposeCount { get; private set; }

        public override bool CanRead => false;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
