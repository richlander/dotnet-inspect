using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

public sealed class AnalysisIndexCacheAdmissionTests
{
    [Fact]
    public void ForAssembly_UnsupportedSnapshotIsTyped()
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
                "research format admission test"));

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => AnalysisIndexCache.ForAssembly(assembly));
    }

    [Fact]
    public void ForAssembly_MalformedSnapshotPreservesReason()
    {
        byte[] image = BuildMalformedMetadataRoot();
        var assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Malformed",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local(
                "research format admission test"));

        MalformedMetadataRootException exception =
            Assert.Throws<MalformedMetadataRootException>(
                () => AnalysisIndexCache.ForAssembly(assembly));
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            exception.Reason);
    }

    [Fact]
    public void ResearchMatch_RejectsWindowsMetadata()
    {
        byte[] image = BuildManagedWindowsMetadata();
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => ResearchMatch.Compare(
                "Unsupported.winmd",
                peReader,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2)));
    }

    static byte[] BuildManagedWindowsMetadata()
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

    static byte[] BuildMalformedMetadataRoot()
    {
        byte[] image = BuildManagedWindowsMetadata();
        using var peReader = new PEReader(
            System.Collections.Immutable.ImmutableArray.Create(image));
        image.AsSpan(
            peReader.PEHeaders.MetadataStartOffset,
            sizeof(uint)).Clear();
        return image;
    }
}
