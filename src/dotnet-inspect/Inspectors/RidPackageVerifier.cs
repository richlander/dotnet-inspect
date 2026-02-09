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
        VerboseLogger logger)
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
                // Remote verification: check NuGet API with retry
                string packageId = ridPkg.PackageId.ToLowerInvariant();
                string checkVersion = version.ToLowerInvariant();
                string url = $"https://api.nuget.org/v3-flatcontainer/{packageId}/{checkVersion}/{packageId}.nuspec";

                try
                {
                    using var response = await HttpRetryHelper.HeadWithRetryAsync(client, url);
                    ridPkg.Exists = response != null && response.IsSuccessStatusCode;

                    string status = ridPkg.Exists == true ? "available" : "NOT FOUND";
                    logger.Log($"  {ridPkg.RuntimeIdentifier}: {status} ({ridPkg.PackageId} {version})");
                }
                catch
                {
                    ridPkg.Exists = false;
                    logger.Log($"  {ridPkg.RuntimeIdentifier}: ERROR checking ({ridPkg.PackageId} {version})");
                }
            }
        }
    }
}
