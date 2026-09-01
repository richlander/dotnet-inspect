using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.JsExportSurface.Tests;

internal static class MetadataAdmissionFixture
{
    public static byte[] WithUnmappableMetadataDirectory(
        string assemblyPath)
    {
        byte[] image = File.ReadAllBytes(assemblyPath);
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(
                peReader.PEHeaders.CorHeaderStartOffset + 8,
                sizeof(int)),
            int.MaxValue);
        return image;
    }

    public static byte[] ManagedWindowsMetadata()
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

    public static byte[] WithOverflowingMetadataStreamCount(
        string assemblyPath)
    {
        byte[] image = File.ReadAllBytes(assemblyPath);
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        int streamCountOffset =
            metadataStart
            + 16
            + versionLength
            + sizeof(ushort);
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(streamCountOffset, sizeof(ushort)),
            ushort.MaxValue);
        return image;
    }
}
