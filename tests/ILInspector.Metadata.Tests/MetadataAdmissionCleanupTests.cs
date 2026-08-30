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
        AssemblyTypeDeclarationInventoryOutcome outcome =
            AssemblyTypeDeclarationInventoryReader.Read(Descriptor());

        var rejected =
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.UnsupportedMetadataFormat,
            rejected.Failure.Kind);
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
        ResolvedAssemblyReference? assembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => new ThrowingDisposeMemoryStream(
                    BuildNoMetadataImage()),
                AssemblyResolutionProvenance.Local(
                    "format admission test"));

        Assert.Null(assembly);
    }

    static void AssertFallback(byte[] image)
    {
        AssemblyReferenceIdentity fallback = Identity();
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromStreamWithFallbackIdentity(
                () => new ThrowingDisposeMemoryStream(image),
                fallback,
                AssemblyResolutionProvenance.Local("format admission test"),
                out bool usedFallback);
        Assert.True(usedFallback);
        Assert.Equal(fallback, assembly.Identity);
    }

    static ResolvedAssemblyReference Descriptor(byte[]? image = null)
    {
        image ??= BuildManagedWindowsMetadata();
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

    static byte[] BuildNoMetadataImage()
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

    internal sealed class ThrowingDisposeMemoryStream(byte[] image)
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
}
