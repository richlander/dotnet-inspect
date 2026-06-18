using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Convenience layer for common assembly reading operations.
/// Hides PEReader boilerplate from callers.
/// </summary>
public static class AssemblyReader
{
    /// <summary>
    /// Extracts the public API surface from a DLL file on disk.
    /// Returns null if the file cannot be read or has no metadata.
    /// </summary>
    public static ApiSurface? ExtractApiSurface(string dllPath, bool includeAll = false, bool typesOnly = false)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            var surface = ExtractApiSurface(stream, includeAll, typesOnly);
            if (surface != null)
            {
                foreach (var type in surface.Types)
                    type.SourceAssemblyPath = dllPath;
            }
            return surface;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the public API surface from a stream containing a PE image.
    /// Returns null if the stream cannot be read or has no metadata.
    /// </summary>
    public static ApiSurface? ExtractApiSurface(Stream stream, bool includeAll = false, bool typesOnly = false)
    {
        try
        {
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
                return null;

            return ApiSurfaceExtractor.Extract(peReader, includeAll, typesOnly);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds all types in an assembly that implement or extend the target type.
    /// </summary>
    public static IEnumerable<TypeRelationship> FindImplementers(string dllPath, string targetType, bool includeHidden = false)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            // Must materialize results before PEReader is disposed
            return TypeHierarchyScanner.FindImplementers(peReader, targetType, includeHidden).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Counts the number of public types in an assembly.
    /// Returns 0 if the file cannot be read.
    /// </summary>
    public static int CountPublicTypes(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
                return 0;

            var reader = peReader.GetMetadataReader();
            int count = 0;

            foreach (var typeDefHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeDefHandle);

                if (typeDef.IsPublic)
                {
                    var name = reader.GetString(typeDef.Name);
                    if (!TypeFilters.IsCompilerGenerated(name))
                    {
                        count++;
                    }
                }
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }
}
