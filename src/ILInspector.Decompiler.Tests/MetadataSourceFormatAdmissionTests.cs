using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public sealed class MetadataSourceFormatAdmissionTests
{
    [Fact]
    public void OpenFromPrefetchedImage_RejectsWindowsMetadata()
    {
        byte[] image = BuildManagedWindowsMetadata();

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => MetadataSource.OpenFromPrefetchedImage(
                "Unsupported.dll",
                ImmutableArray.Create(image)));
    }

    [Fact]
    public void OpenFromPath_RejectsWindowsMetadata()
    {
        byte[] image = BuildManagedWindowsMetadata();
        string path = Path.Combine(
            Path.GetTempPath(),
            $"unsupported-metadata-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image);
        try
        {
            Assert.Throws<UnsupportedMetadataFormatException>(
                () => MetadataSource.OpenWithoutSymbols(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OpenFromResolvedAssembly_RejectsWindowsMetadata()
    {
        byte[] image = BuildManagedWindowsMetadata();
        var assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Unsupported",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("format admission test"));

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => MetadataSource.OpenWithoutSymbols(
                assembly,
                TestAssemblyReferenceResolvers.None));
    }

    [Fact]
    public void OpenFromResolvedAssembly_CleanupCannotReplaceFormatRejection()
    {
        byte[] image = BuildManagedWindowsMetadata();
        ThrowingDisposeMemoryStream? opened = null;
        var assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Unsupported",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => opened = new ThrowingDisposeMemoryStream(image),
            AssemblyResolutionProvenance.Local("format admission test"));

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => MetadataSource.OpenWithoutSymbols(
                assembly,
                TestAssemblyReferenceResolvers.None));
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void ReferencedAssembly_CleanupCannotReplaceFormatRejection()
    {
        byte[] image = BuildManagedWindowsMetadata();
        ThrowingDisposeMemoryStream? opened = null;

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => OpenedAssembly.TryOpen(
                () => opened =
                    new ThrowingDisposeMemoryStream(image)));
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void ReferencedAssembly_NoMetadataCleanupCannotReplaceNoResult()
    {
        ThrowingDisposeMemoryStream? opened = null;

        OpenedAssembly? assembly =
            OpenedAssembly.TryOpen(
                () => opened =
                    new ThrowingDisposeMemoryStream(
                        BuildNoMetadataImage()));

        Assert.Null(assembly);
        Assert.Equal(1, opened!.DisposeCount);
    }

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

    static byte[] BuildNoMetadataImage()
    {
        byte[] image = BuildManagedWindowsMetadata();
        using var peReader = new PEReader(
            ImmutableArray.Create(image));
        PEHeader peHeader = peReader.PEHeaders.PEHeader!;
        int directoryBase =
            peReader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        image.AsSpan(directoryBase + (14 * 8), 8).Clear();
        return image;
    }

    sealed class ThrowingDisposeMemoryStream(byte[] image)
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
}
