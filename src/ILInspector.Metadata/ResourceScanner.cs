using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Information about a manifest resource embedded in an assembly.
/// </summary>
public record ManifestResourceInfo(
    string Name,
    bool IsPublic,
    bool IsEmbedded,
    int Size);

/// <summary>
/// Scans assemblies for manifest resources.
/// </summary>
public static class ResourceScanner
{
    /// <summary>
    /// Lists all manifest resources in an assembly.
    /// </summary>
    public static List<ManifestResourceInfo> Scan(Stream peStream)
    {
        using var peReader = new PEReader(peStream);
        return Scan(peReader);
    }

    /// <summary>
    /// Lists all manifest resources in an assembly.
    /// </summary>
    public static List<ManifestResourceInfo> Scan(PEReader peReader)
    {
        List<ManifestResourceInfo> results = [];

        if (!peReader.HasMetadata)
            return results;

        var reader = peReader.GetMetadataReader();
        var resourcesDir = peReader.PEHeaders.CorHeader!.ResourcesDirectory;

        foreach (var handle in reader.ManifestResources)
        {
            var resource = reader.GetManifestResource(handle);
            string name = reader.GetString(resource.Name);
            bool isPublic = (resource.Attributes & ManifestResourceAttributes.VisibilityMask)
                == ManifestResourceAttributes.Public;
            bool isEmbedded = resource.Implementation.IsNil;

            int size = 0;
            if (isEmbedded && resourcesDir.Size > 0)
            {
                size = GetEmbeddedResourceSize(peReader, resourcesDir.RelativeVirtualAddress, resource.Offset);
            }

            results.Add(new ManifestResourceInfo(name, isPublic, isEmbedded, size));
        }

        return results;
    }

    /// <summary>
    /// Reads the 4-byte length prefix of an embedded resource to get its size.
    /// </summary>
    private static int GetEmbeddedResourceSize(PEReader peReader, int resourcesRva, long offset)
    {
        try
        {
            var sectionData = peReader.GetSectionData(resourcesRva + (int)offset);
            if (sectionData.Length < 4) return 0;
            var blobReader = sectionData.GetReader(0, 4);
            return blobReader.ReadInt32();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Extracts embedded resources to validated relative paths beneath a directory.
    /// The operation fails before writing when a resource path is unsafe, conflicts
    /// with another resource, or would overwrite an existing file.
    /// </summary>
    public static List<string> ExtractAll(Stream peStream, string outputDir)
    {
        using var peReader = new PEReader(peStream);
        return ExtractAll(peReader, outputDir);
    }

    /// <summary>
    /// Extracts embedded resources to validated relative paths beneath a directory.
    /// The operation fails before writing when a resource path is unsafe, conflicts
    /// with another resource, or would overwrite an existing file.
    /// </summary>
    public static List<string> ExtractAll(PEReader peReader, string outputDir)
        => ResourceExtractor.ExtractAll(peReader, outputDir);
}
