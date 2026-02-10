using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

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
    /// Extracts all embedded resources from an assembly to a directory.
    /// Returns the list of extracted file paths.
    /// </summary>
    public static List<string> ExtractAll(Stream peStream, string outputDir)
    {
        using var peReader = new PEReader(peStream);
        return ExtractAll(peReader, outputDir);
    }

    /// <summary>
    /// Extracts all embedded resources from an assembly to a directory.
    /// Returns the list of extracted file paths.
    /// </summary>
    public static List<string> ExtractAll(PEReader peReader, string outputDir)
    {
        List<string> extracted = [];

        if (!peReader.HasMetadata)
            return extracted;

        var reader = peReader.GetMetadataReader();
        var resourcesDir = peReader.PEHeaders.CorHeader!.ResourcesDirectory;
        if (resourcesDir.Size == 0)
            return extracted;

        Directory.CreateDirectory(outputDir);

        foreach (var handle in reader.ManifestResources)
        {
            var resource = reader.GetManifestResource(handle);
            if (!resource.Implementation.IsNil)
                continue; // skip external resources

            string name = reader.GetString(resource.Name);
            int rva = resourcesDir.RelativeVirtualAddress + (int)resource.Offset;

            try
            {
                var sectionData = peReader.GetSectionData(rva);
                if (sectionData.Length < 4) continue;

                var blobReader = sectionData.GetReader(0, 4);
                int size = blobReader.ReadInt32();
                if (size <= 0 || size + 4 > sectionData.Length) continue;

                var data = sectionData.GetReader(4, size);
                var filePath = Path.Combine(outputDir, name);

                // Create subdirectories if resource name contains dots that look like paths
                var dir = Path.GetDirectoryName(filePath);
                if (dir != null) Directory.CreateDirectory(dir);

                File.WriteAllBytes(filePath, data.ReadBytes(size));
                extracted.Add(filePath);
            }
            catch
            {
                // Skip resources that can't be read
            }
        }

        return extracted;
    }
}
