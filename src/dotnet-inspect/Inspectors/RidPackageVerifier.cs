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
                string expectedFileName =
                    $"{ridPkg.PackageId}.{normalizedVersion}.nupkg";
                NuspecProbeResult probe =
                    await ProbeLocalPackageArchiveAsync(
                        localDir,
                        expectedFileName,
                        ridPkg.PackageId,
                        normalizedVersion,
                        logger.Log);
                ridPkg.Exists = ToAvailability(probe.Status);

                string status = ridPkg.Exists switch
                {
                    true => "available",
                    false => "NOT FOUND",
                    null => "availability unknown"
                };
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
                ridPkg.Exists = ToAvailability(probe.Status);

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

    private static async Task<NuspecProbeResult>
        ProbeLocalPackageArchiveAsync(
            string localDir,
            string expectedFileName,
            string packageId,
            string version,
            Action<string>? log)
    {
        string expectedPath = Path.Combine(localDir, expectedFileName);
        NuspecProbeResult exact =
            await PackageExtractor.ProbeLocalPackageArchiveAsync(
                expectedPath,
                packageId,
                version,
                log).ConfigureAwait(false);
        if (exact.Status != NuspecProbeStatus.Absent)
            return exact;

        bool sawIndeterminate = false;
        try
        {
            foreach (string candidatePath in Directory.EnumerateFiles(
                         localDir,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                string candidateName = Path.GetFileName(candidatePath);
                if (string.Equals(
                        candidateName,
                        expectedFileName,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        candidateName,
                        expectedFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                NuspecProbeResult candidate =
                    await PackageExtractor.ProbeLocalPackageArchiveAsync(
                        candidatePath,
                        packageId,
                        version,
                        log).ConfigureAwait(false);
                if (candidate.Status == NuspecProbeStatus.Present)
                    return candidate;
                sawIndeterminate |=
                    candidate.Status == NuspecProbeStatus.Indeterminate;
            }
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException)
        {
            log?.Invoke(
                $"Local RID package directory could not be inspected: "
                + ex.GetType().Name);
            sawIndeterminate = true;
        }

        return sawIndeterminate
            ? new NuspecProbeResult(
                null,
                NuspecProbeStatus.Indeterminate)
            : exact;
    }

    private static bool? ToAvailability(NuspecProbeStatus status) =>
        status switch
        {
            NuspecProbeStatus.Present => true,
            NuspecProbeStatus.Absent => false,
            _ => null
        };
}
