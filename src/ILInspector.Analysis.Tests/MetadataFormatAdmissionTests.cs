using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class MetadataFormatAdmissionTests
{
    [Fact]
    public void LibraryBodyIndex_PathRejectsWindowsMetadata()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-analysis-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, BuildManagedWindowsMetadata());
        try
        {
            Assert.Throws<UnsupportedMetadataFormatException>(
                () => LibraryBodyIndex.Open(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LibraryBodyIndex_PrefetchedImageRejectsWindowsMetadata()
    {
        Assert.Throws<UnsupportedMetadataFormatException>(
            () => LibraryBodyIndex.OpenFromPrefetchedImage(
                "Unsupported.dll",
                ImmutableArray.Create(BuildManagedWindowsMetadata()),
                LibraryBodyAnalysisFeatures.MethodEvidence));
    }

    [Fact]
    public void UnsafeEvidencePresence_RejectsWindowsMetadata()
    {
        Assert.Throws<UnsupportedMetadataFormatException>(
            () => LibraryBodyIndex.HasUnsafeEvidence(
                "Unsupported.dll",
                ImmutableArray.Create(BuildManagedWindowsMetadata())));
    }

    [Fact]
    public void StructuralClone_RejectsWindowsMetadata()
    {
        using var peReader = new PEReader(
            ImmutableArray.Create(BuildManagedWindowsMetadata()));

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => StructuralCloneAnalysis.Compare(
                peReader,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2)));
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
}
