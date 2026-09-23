using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The host's disclosure of version settlements served from prior
/// settlements: one warning per invocation naming the count and the
/// coordinates, with the explicit refresh spelling. The invocation entry
/// point flushes it from the invocation's
/// <see cref="PackageVersionServicePlan"/>. Owned by
/// <c>docs/design/package-version-service.md</c> (Receipt).
/// </summary>
internal static class PackageVersionDisclosure
{
    private const int NamedCoordinates = 3;

    /// <summary>
    /// Writes the served-prior warning for the plan's ledger when it holds
    /// any <c>ServedPrior</c> receipt; a <c>Current</c> prior or a fresh
    /// discovery is never recorded and writes nothing.
    /// </summary>
    public static void WriteServedPriorWarning(PackageVersionServicePlan plan)
    {
        IReadOnlyList<PackageVersionResolutionReceipt.Prior> served = plan.ServedPriors;
        if (served.Count == 0)
            return;

        IEnumerable<string> named = served
            .Take(NamedCoordinates)
            .Select(prior =>
                $"{prior.Coordinate.PackageId}@{prior.Coordinate.Version}"
                + (prior.Age is { } age ? $" ({DescribeAge(age)} old)" : ""));
        string list = string.Join(", ", named)
            + (served.Count > NamedCoordinates ? ", …" : "");
        string noun = served.Count == 1 ? "package was" : "packages were";
        CommandError.WriteWarning(
            $"{served.Count} {noun} served from a prior version settlement without a completed refresh: {list}.",
            "Use 'Name@latest' to require a fresh check; verbose output lists each settlement.");
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
