using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Verifies existence of RID-specific packages referenced by pointer packages.
/// </summary>
public static class RidPackageVerifier
{
    public static async Task VerifyAsync(
        HttpClient client,
        InspectionResult result,
        string version,
        string? localDir,
        VerboseLogger logger,
        NuGetSourceOptions? sourceOptions = null)
    {
        if (result.RuntimeIdentifierPackages == null)
            return;

        foreach (var ridPkg in result.RuntimeIdentifierPackages)
        {
            if (localDir != null)
            {
                // Local verification: check if sibling .nupkg file exists
                string expectedFileName = $"{ridPkg.PackageId}.{version}.nupkg";
                string expectedPath = Path.Combine(localDir, expectedFileName);
                ridPkg.Exists = File.Exists(expectedPath);

                string status = ridPkg.Exists == true ? "found" : "NOT FOUND";
                logger.Log($"  {ridPkg.RuntimeIdentifier}: {status} ({expectedFileName})");
            }
            else
            {
                try
                {
                    string? nuspec = await PackageExtractor.TryGetNuspecXmlAsync(
                        client,
                        ridPkg.PackageId,
                        version,
                        logger.Log,
                        sourceOptions);
                    ridPkg.Exists = nuspec is not null;

                    string status = ridPkg.Exists == true ? "available" : "NOT FOUND";
                    logger.Log($"  {ridPkg.RuntimeIdentifier}: {status} ({ridPkg.PackageId} {version})");
                }
                catch (Exception ex)
                {
                    ridPkg.Exists = false;
                    logger.Log(
                        $"  {ridPkg.RuntimeIdentifier}: ERROR checking ({ridPkg.PackageId} {version}): {ex.Message}");
                }
            }
        }
    }
}
