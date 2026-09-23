using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The host's disclosure of a version settlement served from a prior
/// settlement: one warning per invocation naming what was served and how
/// old it is, with the explicit refresh spelling. Owned by
/// <c>docs/design/package-version-service.md</c> (Receipt).
/// </summary>
internal static class PackageVersionDisclosure
{
    /// <summary>
    /// Writes the served-prior warning when the acquisition's House decision
    /// carries a <c>ServedPrior</c> receipt; a <c>Current</c> prior or a
    /// fresh discovery writes nothing.
    /// </summary>
    public static void WriteServedPriorWarning(PackageExtractionResult resolution)
    {
        if (resolution.HouseSettlement?.Result.Decision?.VersionResolution
            is not PackageVersionResolutionReceipt.Prior
            {
                Freshness: PackageVersionDiscoveryFreshness.ServedPrior,
            } prior)
        {
            return;
        }

        string coordinate = $"{prior.Coordinate.PackageId}@{prior.Coordinate.Version}";
        string age = prior.Age is { } value ? $" ({DescribeAge(value)} old)" : "";
        CommandError.WriteWarning(
            $"1 package was served from a prior version settlement because its sources could not be refreshed: {coordinate}{age}.",
            $"Use '{prior.Coordinate.PackageId}@latest' to require a fresh check.");
    }

    private static string DescribeAge(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1))
            return "under a minute";
        if (age < TimeSpan.FromHours(1))
            return $"{(int)age.TotalMinutes} min";
        if (age < TimeSpan.FromDays(1))
            return $"{age.TotalHours:F1} h";
        return $"{age.TotalDays:F1} d";
    }
}
