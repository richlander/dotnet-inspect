using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using CSharpText;

namespace ILInspector.Metadata;

/// <summary>
/// Convenience layer for common assembly reading operations.
/// Hides PEReader boilerplate from callers.
/// </summary>
public static class AssemblyReader
{
    /// <summary>
    /// Extracts an API surface only when the selected managed PE is a module
    /// without an assembly manifest.
    /// </summary>
    public static ApiSurface? ExtractModuleApiSurface(
        string path,
        bool includeAll = false,
        bool typesOnly = false)
    {
        try
        {
            ApiSurface? surface = OwnedResourceCleanup.ReadAdmittedPeImage(
                () => File.OpenRead(path),
                peReader =>
                {
                    if (MetadataFormatAdmission.GetMetadataReader(peReader)
                            .IsAssembly)
                    {
                        return null;
                    }

                    return ApiSurfaceExtractor.Extract(
                        peReader,
                        includeAll,
                        typesOnly);
                },
                noMetadataResult: null);
            if (surface is null)
                return null;

            SetSourceAssemblyPath(surface, path);
            return surface;
        }
        catch (Exception ex) when (
            ex is not MalformedMetadataRootException
                and (IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException
                    or OverflowException
                    or ArgumentException))
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the public API surface from a DLL file on disk.
    /// Returns null if the file cannot be read or has no metadata.
    /// </summary>
    public static ApiSurface? ExtractApiSurface(string dllPath, bool includeAll = false, bool typesOnly = false)
    {
        try
        {
            var surface = ExtractApiSurface(
                File.OpenRead(dllPath),
                includeAll,
                typesOnly);
            if (surface != null)
                SetSourceAssemblyPath(surface, dllPath);
            return surface;
        }

        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    static void SetSourceAssemblyPath(
        ApiSurface surface,
        string path) =>
        surface.SetInspectionSourceAssemblyPath(path);

    /// <summary>
    /// Extracts the public API surface from a stream containing a PE image.
    /// Returns null if the stream cannot be read or has no metadata.
    /// </summary>
    public static ApiSurface? ExtractApiSurface(Stream stream, bool includeAll = false, bool typesOnly = false)
    {
        Stream? ownedStream = null;
        PEReader? peReader = null;
        bool noResultEstablished = false;
        try
        {
            ownedStream = stream;
            peReader = new PEReader(
                stream,
                PEStreamOptions.LeaveOpen);

            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                noResultEstablished = true;
                return null;
            }

            return ApiSurfaceExtractor.Extract(peReader, includeAll, typesOnly);
        }
        catch (BadImageFormatException ex)
            when (ex is not MalformedMetadataRootException)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref ownedStream,
                ex);
            return null;
        }
        catch (ArgumentException ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref ownedStream,
                ex);
            return null;
        }
        catch (OverflowException ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref ownedStream,
                ex);
            return null;
        }
        catch (Exception ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref ownedStream,
                ex);
            throw;
        }
        finally
        {
            if (noResultEstablished)
            {
                OwnedResourceCleanup.DisposeWithoutReplacingOutcome(
                    ref peReader,
                    ref ownedStream);
            }
            else
            {
                peReader?.Dispose();
                ownedStream?.Dispose();
            }
        }
    }

    /// <summary>
    /// Extracts the compact public API summary used for trusted platform assemblies.
    /// Type identities and member-kind counts are retained without decoding full signatures.
    /// </summary>
    public static ApiSurface? ExtractApiSummarySurface(string dllPath)
    {
        try
        {
            var surface = ExtractApiSummarySurface(
                File.OpenRead(dllPath));
            if (surface != null)
            {
                foreach (var type in surface.Types)
                    type.SourceAssemblyPath = dllPath;
            }
            return surface;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the compact public API summary from a trusted platform image stream.
    /// </summary>
    public static ApiSurface? ExtractApiSummarySurface(Stream stream)
    {
        Stream? ownedStream = null;
        PEReader? peReader = null;
        bool noResultEstablished = false;
        try
        {
            ownedStream = stream;
            peReader = new PEReader(
                stream,
                PEStreamOptions.LeaveOpen);

            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                noResultEstablished = true;
                return null;
            }

            return ApiSurfaceExtractor.ExtractSummary(peReader);
        }
        catch (BadImageFormatException ex)
            when (ex is not MalformedMetadataRootException)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref ownedStream,
                ex);
            return null;
        }
        catch (ArgumentException ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref ownedStream,
                ex);
            return null;
        }
        catch (OverflowException ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref ownedStream,
                ex);
            return null;
        }
        catch (Exception ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref ownedStream,
                ex);
            throw;
        }
        finally
        {
            if (noResultEstablished)
            {
                OwnedResourceCleanup.DisposeWithoutReplacingOutcome(
                    ref peReader,
                    ref ownedStream);
            }
            else
            {
                peReader?.Dispose();
                ownedStream?.Dispose();
            }
        }
    }

    /// <summary>
    /// Finds the unique public type matching a full, simple, or generic-aware type query.
    /// Returns null when the assembly cannot be read, has no metadata, has no match,
    /// or has multiple matches.
    /// </summary>
    public static string? FindUniquePublicType(string dllPath, string typeName)
    {
        try
        {
            return OwnedResourceCleanup.ReadAdmittedPeImage(
                () => File.OpenRead(dllPath),
                peReader =>
                    FindUniquePublicType(
                        MetadataFormatAdmission.GetMetadataReader(peReader),
                        typeName),
                noMetadataResult: null);
        }
        catch (Exception ex)
            when (ex is not UnsupportedMetadataFormatException
                and not MalformedMetadataRootException)
        {
            return null;
        }
    }

    private static string? FindUniquePublicType(MetadataReader reader, string typeName)
    {
        var normalized = FqnParser.NormalizeTypeName(typeName);

        List<string> publicTypes = [];
        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (!typeDef.IsPublic)
                continue;

            publicTypes.Add(reader.GetFullTypeName(typeDef));
        }

        var normalizedLookup = normalized.Replace('+', '.');
        var exactMatches = publicTypes.Where(fullName =>
        {
            var normalizedFullName =
                FqnParser.NormalizeTypeName(fullName).Replace('+', '.');
            return normalizedFullName.Equals(
                       normalizedLookup,
                       StringComparison.OrdinalIgnoreCase)
                   || (normalizedFullName.Length > normalizedLookup.Length
                       && normalizedFullName[
                           normalizedFullName.Length - normalizedLookup.Length - 1] == '.'
                       && normalizedFullName.EndsWith(
                           normalizedLookup,
                           StringComparison.OrdinalIgnoreCase));
        }).ToList();
        if (exactMatches.Count == 1)
            return exactMatches[0];
        if (exactMatches.Count > 1)
            return null;

        if (TypeMatcher.HasExplicitGenericNotation(typeName))
            return null;

        string? match = null;
        foreach (var fullName in publicTypes)
        {
            if (!TypeMatcher.Matches(fullName, normalized))
                continue;

            if (match != null)
                return null;

            match = fullName;
        }

        return match;
    }

    /// <summary>
    /// Finds all types in an assembly that implement or extend the target type.
    /// </summary>
    public static IEnumerable<TypeRelationship> FindImplementers(string dllPath, string targetType, bool includeHidden = false)
    {
        try
        {
            return OwnedResourceCleanup.ReadAdmittedPeImage(
                () => File.OpenRead(dllPath),
                peReader =>
                    TypeHierarchyScanner.FindImplementers(
                            peReader,
                            targetType,
                            includeHidden)
                        .ToList(),
                noMetadataResult: []);
        }
        catch (Exception ex)
            when (ex is not UnsupportedMetadataFormatException
                and not MalformedMetadataRootException)
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
            return OwnedResourceCleanup.ReadAdmittedPeImage(
                () => File.OpenRead(dllPath),
                peReader =>
                {
                    var reader =
                        MetadataFormatAdmission.GetMetadataReader(peReader);
                    int count = 0;

                    foreach (var typeDefHandle in reader.TypeDefinitions)
                    {
                        var typeDef =
                            reader.GetTypeDefinition(typeDefHandle);

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
                },
                noMetadataResult: 0);
        }
        catch (Exception ex)
            when (ex is not UnsupportedMetadataFormatException
                and not MalformedMetadataRootException)
        {
            return 0;
        }
    }
}
