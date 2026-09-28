using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace ILInspector.TypeScriptGeneration;

internal static class JsExportSurfaceLoader
{
    public static bool TryLoad(
        string assemblyPath,
        ApiAssemblyIdentity jsonContractIdentity,
        string toolName,
        TextWriter error,
        out global::ILInspector.JsExportSurface.JsExportSurface? surface) =>
        TryLoad(
            assemblyPath,
            searchLocations: [],
            jsonContractIdentity,
            toolName,
            error,
            out surface);

    public static bool TryLoad(
        string assemblyPath,
        IReadOnlyList<string> searchLocations,
        ApiAssemblyIdentity jsonContractIdentity,
        string toolName,
        TextWriter error,
        out global::ILInspector.JsExportSurface.JsExportSurface? surface)
    {
        surface = null;
        if (!File.Exists(assemblyPath))
        {
            error.WriteLine($"{toolName}: assembly not found: {assemblyPath}");
            return false;
        }

        ImmutableArray<byte> image;
        try
        {
            image = ImmutableArray.CreateRange(File.ReadAllBytes(assemblyPath));
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException)
        {
            error.WriteLine(
                $"{toolName}: could not read '{assemblyPath}': {ex.Message}");
            return false;
        }

        ApiSurface apiSurface;
        IReadOnlySet<string> referencedAssemblyNames;
        try
        {
            using var peReader = new PEReader(image);
            apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            referencedAssemblyNames =
                AssemblyInspector.ExtractReferences(peReader)
                    .Select(reference => reference.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or IOException
                or UnauthorizedAccessException)
        {
            error.WriteLine(
                $"{toolName}: could not read '{assemblyPath}' as a .NET assembly: "
                    + ex.Message);
            return false;
        }

        ApiSurfaceInspectionFailure? incompleteExtraction =
            apiSurface.InspectionFailures.FirstOrDefault(
                static failure =>
                    failure.Operation != ApiSurface.ConstraintResolutionOperation);
        if (incompleteExtraction is not null)
        {
            string location = incompleteExtraction.SubjectToken == 0
                ? "assembly metadata"
                : $"metadata token 0x{incompleteExtraction.SubjectToken:X8}";
            error.WriteLine(
                $"{toolName}: {location}: metadata extraction did not produce "
                    + "a complete surface.");
            return false;
        }

        try
        {
            LibraryBodyIndex bodyIndex =
                LibraryBodyIndex.OpenFromPrefetchedImage(
                    assemblyPath,
                    image,
                    LibraryBodyAnalysisFeatures.MethodEvidence
                        | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
            if (!TryLoadReferencedTypeDefinitions(
                    assemblyPath,
                    searchLocations,
                    referencedAssemblyNames,
                    toolName,
                    error,
                    out IReadOnlyDictionary<
                        ApiTypeReferenceIdentity,
                        ApiType> referencedTypeDefinitions,
                    out IReadOnlyDictionary<
                        ApiType,
                        LibraryBodyIndex> referencedBodyIndexes))
            {
                return false;
            }

            surface = JsExportSurfaceBuilder.Build(
                apiSurface,
                bodyIndex,
                referencedTypeDefinitions,
                referencedBodyIndexes,
                jsonContractIdentity);
            return true;
        }
        catch (UnsupportedJsExportSurfaceException ex)
        {
            error.WriteLine($"{toolName}: {ex.Message}");
            return false;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or IOException
                or UnauthorizedAccessException)
        {
            error.WriteLine(
                $"{toolName}: could not read IL bodies from '{assemblyPath}' "
                    + $"for wire-contract resolution: {ex.Message}");
            return false;
        }
    }

    static bool TryLoadReferencedTypeDefinitions(
        string rootAssemblyPath,
        IReadOnlyList<string> searchLocations,
        IReadOnlySet<string> referencedAssemblyNames,
        string toolName,
        TextWriter error,
        out IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            definitions,
        out IReadOnlyDictionary<ApiType, LibraryBodyIndex> bodyIndexes)
    {
        var byIdentity =
            new Dictionary<ApiTypeReferenceIdentity, ApiType>();
        var bodiesByType = new Dictionary<ApiType, LibraryBodyIndex>();
        if (searchLocations.Count == 0)
        {
            definitions = byIdentity;
            bodyIndexes = bodiesByType;
            return true;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.GetFullPath(rootAssemblyPath),
        };
        foreach (string location in searchLocations)
        {
            if (File.Exists(location))
            {
                paths.Add(Path.GetFullPath(location));
            }
            else if (Directory.Exists(location))
            {
                foreach (string path in Directory.EnumerateFiles(
                    location,
                    "*.dll",
                    SearchOption.TopDirectoryOnly))
                {
                    paths.Add(Path.GetFullPath(path));
                }
            }
            else
            {
                error.WriteLine(
                    $"{toolName}: assembly search location not found: "
                        + location);
                definitions =
                    new Dictionary<ApiTypeReferenceIdentity, ApiType>();
                bodyIndexes =
                    new Dictionary<ApiType, LibraryBodyIndex>();
                return false;
            }
        }

        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            if (string.Equals(
                    path,
                    Path.GetFullPath(rootAssemblyPath),
                    StringComparison.Ordinal))
            {
                continue;
            }

            ApiSurface dependencySurface;
            ImmutableArray<byte> dependencyImage;
            try
            {
                dependencyImage =
                    ImmutableArray.CreateRange(File.ReadAllBytes(path));
                using var image = new PEReader(dependencyImage);
                dependencySurface =
                    ApiSurfaceExtractor.Extract(image, includeAll: true);
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or IOException
                    or UnauthorizedAccessException)
            {
                continue;
            }

            ApiAssemblyIdentity? identity = dependencySurface.AssemblyIdentity;
            if (identity is null
                || !referencedAssemblyNames.Contains(identity.Name))
                continue;

            if (dependencySurface.InspectionFailures.Any(
                    static failure =>
                        failure.Operation !=
                            ApiSurface.ConstraintResolutionOperation))
            {
                continue;
            }

            LibraryBodyIndex? dependencyBodyIndex =
                dependencySurface.Types.Any(type =>
                    type.BaseType
                        == "System.Text.Json.Serialization.JsonSerializerContext")
                    ? LibraryBodyIndex.OpenFromPrefetchedImage(
                        path,
                        dependencyImage,
                        LibraryBodyAnalysisFeatures.MethodEvidence
                            | LibraryBodyAnalysisFeatures.JsonWireContractFlow)
                    : null;
            foreach (ApiType type in dependencySurface.Types)
            {
                var typeIdentity = new ApiTypeReferenceIdentity(
                    identity,
                    type.FullName,
                    type.DefinitionName);
                if (byIdentity.TryGetValue(typeIdentity, out ApiType? existing)
                    && !ReferenceEquals(existing, type))
                {
                    error.WriteLine(
                        $"{toolName}: referenced type identity is ambiguous "
                            + $"for '{typeIdentity}'.");
                    definitions =
                        new Dictionary<ApiTypeReferenceIdentity, ApiType>();
                    bodyIndexes =
                        new Dictionary<ApiType, LibraryBodyIndex>();
                    return false;
                }

                byIdentity[typeIdentity] = type;
                if (dependencyBodyIndex is not null)
                    bodiesByType[type] = dependencyBodyIndex;
            }
        }

        definitions = byIdentity;
        bodyIndexes = bodiesByType;
        return true;
    }
}
