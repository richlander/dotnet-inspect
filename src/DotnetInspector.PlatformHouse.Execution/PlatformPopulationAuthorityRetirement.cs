using DotnetInspector.Libraries;

namespace DotnetInspector.PlatformHouse;

internal sealed class PlatformPopulationAuthorityRetirementResult
{
    internal PlatformPopulationAuthorityRetirementResult(
        IReadOnlyList<PlatformHouseFailureKind> failureKinds,
        IReadOnlyList<Exception> failures)
    {
        FailureKinds = failureKinds;
        Failures = failures;
    }

    internal IReadOnlyList<PlatformHouseFailureKind> FailureKinds { get; }
    internal IReadOnlyList<Exception> Failures { get; }
}

internal static class PlatformPopulationAuthorityRetirement
{
    internal static async ValueTask<
        PlatformPopulationAuthorityRetirementResult> RetireAsync(
            PlatformPopulationArtifactMaterializationOutcome.Completed
                completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        var kinds = new List<PlatformHouseFailureKind>();
        var failures = new List<Exception>();

        foreach (LibraryContentOwner owner in completed.Population.Owners)
        {
            try
            {
                await owner.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                kinds.Add(
                    owner.ReleaseFailures.Count == 0
                        ? PlatformHouseFailureKind.LibraryRetirement
                        : PlatformHouseFailureKind.LibraryChildRelease);
                failures.Add(failure);
            }

            foreach (Exception failure in owner.CleanupFailures)
            {
                if (!failures.Contains(failure))
                    failures.Add(failure);
            }
            if (owner.CleanupFailures.Count != 0)
            {
                kinds.Add(
                    owner.ReleaseFailures.Count == 0
                        ? PlatformHouseFailureKind.LibraryRetirement
                        : PlatformHouseFailureKind.LibraryChildRelease);
            }
        }

        try
        {
            await completed.Artifacts.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            kinds.Add(PlatformHouseFailureKind.ArtifactRetirement);
            failures.Add(failure);
        }
        foreach (Exception failure in completed.Artifacts.CleanupFailures)
        {
            if (!failures.Contains(failure))
                failures.Add(failure);
        }
        if (completed.Artifacts.CleanupFailures.Count != 0)
            kinds.Add(PlatformHouseFailureKind.ArtifactRetirement);

        return new(
            [.. kinds.Distinct()],
            failures.AsReadOnly());
    }
}
