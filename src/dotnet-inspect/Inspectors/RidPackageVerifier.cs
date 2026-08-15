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
            if (ridPkg.Exists is not null)
                continue;

            if (!PackageExtractor.IsValidPackageId(ridPkg.PackageId)
                || !PackageExtractor.TryNormalizePackageVersion(
                    version,
                    out string normalizedVersion))
            {
                logger.Log(
                    $"  {ridPkg.RuntimeIdentifier}: availability unknown "
                    + "(invalid package coordinate)");
                continue;
            }

            if (localDir != null)
            {
                // Local verification: check if sibling .nupkg file exists
                string expectedFileName =
                    $"{ridPkg.PackageId}.{normalizedVersion}.nupkg";
                string expectedPath = Path.Combine(localDir, expectedFileName);
                ridPkg.Exists = File.Exists(expectedPath);

                string status = ridPkg.Exists == true ? "found" : "NOT FOUND";
                logger.Log($"  {ridPkg.RuntimeIdentifier}: {status} ({expectedFileName})");
            }
            else
            {
                NuspecProbeResult probe =
                    await PackageExtractor.ProbeNuspecXmlAsync(
                        client,
                        ridPkg.PackageId,
                        normalizedVersion,
                        logger.Log,
                        sourceOptions);
                ridPkg.Exists = probe.Status switch
                {
                    NuspecProbeStatus.Present => true,
                    NuspecProbeStatus.Absent => false,
                    _ => null
                };

                string status = ridPkg.Exists switch
                {
                    true => "available",
                    false => "NOT FOUND",
                    null => "availability unknown"
                };
                logger.Log(
                    $"  {ridPkg.RuntimeIdentifier}: {status} "
                    + $"({ridPkg.PackageId} {normalizedVersion})");
            }
        }
    }
}
