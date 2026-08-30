using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public sealed class MetadataFormatAdmissionTests
{
    [Fact]
    public void PlatformHasType_RejectsWindowsMetadata()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-platform-{Guid.NewGuid():N}.winmd");
        File.WriteAllBytes(path, BuildManagedWindowsMetadata());
        try
        {
            Assert.Throws<UnsupportedMetadataFormatException>(
                () => PlatformResolver.HasType(
                    path,
                    "System.Object"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IntrinsicBinding_RejectsWindowsMetadata()
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
            AssemblyResolutionProvenance.Local(
                "services format admission test"));

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => IntrinsicCoreLibraryBinding.Select(
                assembly,
                static _ => AssemblyBindingSelection.NotFound()));
    }

    [Fact]
    public void IntrinsicBinding_CleanupCannotReplaceFormatRejection()
    {
        ThrowingDisposeMemoryStream? opened = null;
        var assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Unsupported",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => opened = new ThrowingDisposeMemoryStream(
                BuildManagedWindowsMetadata()),
            AssemblyResolutionProvenance.Local(
                "services format admission test"));

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => IntrinsicCoreLibraryBinding.Select(
                assembly,
                static _ => AssemblyBindingSelection.NotFound()));
        Assert.Equal(1, opened!.DisposeCount);
    }

    static byte[] BuildManagedWindowsMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Unsupported.winmd"),
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
