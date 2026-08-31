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
        try
        {
            using var peReader = new PEReader(image);
            apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        }
        catch (Exception ex) when (
            ex is not MalformedMetadataRootException
                and (BadImageFormatException
                    or OverflowException
                    or IOException
                    or UnauthorizedAccessException))
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
            surface = JsExportSurfaceBuilder.Build(apiSurface, bodyIndex);
            return true;
        }
        catch (UnsupportedJsExportSurfaceException ex)
        {
            error.WriteLine($"{toolName}: {ex.Message}");
            return false;
        }
        catch (Exception ex) when (
            ex is not MalformedMetadataRootException
                and (BadImageFormatException
                    or OverflowException
                    or IOException
                    or UnauthorizedAccessException))
        {
            error.WriteLine(
                $"{toolName}: could not read IL bodies from '{assemblyPath}' "
                    + $"for wire-contract resolution: {ex.Message}");
            return false;
        }
    }
}
