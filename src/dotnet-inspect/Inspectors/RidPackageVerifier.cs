using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Verifies existence of RID-specific packages referenced by pointer packages.
/// </summary>
public static class RidPackageVerifier
{
    internal const int MaxDistinctPackageProbes = 64;
    internal const int MaxLocalDirectoryEntries = 4096;
    internal const int MaxLocalCaseVariantCandidates = 8;

    public static async Task VerifyAsync(
        HttpClient client,
        InspectionResult result,
        string version,
        string? localDir,
        VerboseLogger logger,
        NuGetSourceOptions? sourceOptions = null)
        => await VerifyAsync(
            client,
            result,
            version,
            localDir,
            logger,
            sourceOptions,
            acquiredEvidence: null).ConfigureAwait(false);

    internal static async Task VerifyAsync(
        HttpClient client,
        InspectionResult result,
        string version,
        string? localDir,
        VerboseLogger logger,
        NuGetSourceOptions? sourceOptions,
        IReadOnlyDictionary<string, NuspecProbeStatus>? acquiredEvidence)
    {
        if (result.RuntimeIdentifierPackages == null)
            return;

        if (!PackageExtractor.TryNormalizePackageVersion(
                version,
                out string normalizedVersion))
        {
            logger.Log("  RID package availability unknown (invalid package version)");
            return;
        }

        Dictionary<string, NuspecProbeStatus> observed =
            new(StringComparer.OrdinalIgnoreCase);
        LocalPackageDirectorySnapshot? localSnapshot =
            localDir is null
                ? null
                : new LocalPackageDirectorySnapshot(localDir, logger.Log);
        int distinctProbeCount = 0;
        bool probeLimitLogged = false;

        foreach (var ridPkg in result.RuntimeIdentifierPackages)
        {
            if (ridPkg.Exists is not null)
            {
                if (PackageExtractor.IsValidPackageId(ridPkg.PackageId))
                {
                    observed.TryAdd(
                        ridPkg.PackageId,
                        ridPkg.Exists == true
                            ? NuspecProbeStatus.Present
                            : NuspecProbeStatus.Absent);
                }
                continue;
            }

            if (!PackageExtractor.IsValidPackageId(ridPkg.PackageId))
            {
                logger.Log(
                    $"  {ridPkg.RuntimeIdentifier}: availability unknown "
                    + "(invalid package coordinate)");
                continue;
            }

            if (!observed.TryGetValue(
                    ridPkg.PackageId,
                    out NuspecProbeStatus status))
            {
                if (distinctProbeCount >= MaxDistinctPackageProbes)
                {
                    if (!probeLimitLogged)
                    {
                        logger.Log(
                            "  RID package availability probe limit reached; "
                            + "remaining mappings are unknown.");
                        probeLimitLogged = true;
                    }

                    continue;
                }

                distinctProbeCount++;
                NuspecProbeResult probe;
                if (localSnapshot is not null)
                {
                    string expectedFileName =
                        $"{ridPkg.PackageId}.{normalizedVersion}.nupkg";
                    probe = await ProbeLocalPackageArchiveAsync(
                        localSnapshot,
                        expectedFileName,
                        ridPkg.PackageId,
                        normalizedVersion,
                        logger.Log).ConfigureAwait(false);
                }
                else
                {
                    probe = await PackageExtractor.ProbeNuspecXmlAsync(
                        client,
                        ridPkg.PackageId,
                        normalizedVersion,
                        logger.Log,
                        sourceOptions).ConfigureAwait(false);
                }

                status = acquiredEvidence is not null
                    && acquiredEvidence.TryGetValue(
                        ridPkg.PackageId,
                        out NuspecProbeStatus acquiredStatus)
                        ? CombineEvidence(acquiredStatus, probe.Status)
                        : probe.Status;
                observed.Add(ridPkg.PackageId, status);
            }

            ridPkg.Exists = ToAvailability(status);
            string statusText = ridPkg.Exists switch
            {
                true => "available",
                false => "NOT FOUND",
                null => "availability unknown"
            };
            logger.Log(
                $"  {ridPkg.RuntimeIdentifier}: {statusText} "
                + $"({ridPkg.PackageId} {normalizedVersion})");
        }
    }

    internal static async Task<NuspecProbeResult>
        ProbeLocalPackageArchiveAsync(
            LocalPackageDirectorySnapshot snapshot,
            string expectedFileName,
            string packageId,
            string version,
            Action<string>? log)
    {
        string expectedPath =
            Path.Combine(snapshot.LocalDirectory, expectedFileName);
        NuspecProbeResult exact =
            await PackageExtractor.ProbeLocalPackageArchiveAsync(
                expectedPath,
                packageId,
                version,
                log).ConfigureAwait(false);
        if (exact.Status != NuspecProbeStatus.Absent)
            return exact;

        LocalSiblingCandidates candidates =
            snapshot.GetCandidates(expectedFileName);
        foreach (string candidatePath in candidates.Paths)
        {
            NuspecProbeResult candidate =
                await PackageExtractor.ProbeLocalPackageArchiveAsync(
                    candidatePath,
                    packageId,
                    version,
                    log).ConfigureAwait(false);
            if (candidate.Status == NuspecProbeStatus.Present)
                return candidate;
        }

        return candidates.Paths.Count > 0 || !candidates.Complete
            ? new NuspecProbeResult(
                null,
                NuspecProbeStatus.Indeterminate)
            : exact;
    }

    internal sealed class LocalPackageDirectorySnapshot(
        string localDirectory,
        Action<string>? log)
    {
        private readonly Dictionary<string, List<string>> _paths =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _overflow =
            new(StringComparer.OrdinalIgnoreCase);
        private bool _captured;
        private bool _complete;

        public string LocalDirectory { get; } = localDirectory;

        internal void Capture()
        {
            if (_captured)
                return;

            _captured = true;
            _complete = true;
            try
            {
                int entryCount = 0;
                foreach (string path in Directory.EnumerateFileSystemEntries(
                             LocalDirectory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    entryCount++;
                    if (entryCount > MaxLocalDirectoryEntries)
                    {
                        _complete = false;
                        log?.Invoke(
                            "Local RID package directory exceeded the "
                            + $"{MaxLocalDirectoryEntries} entry probe limit.");
                        break;
                    }

                    string fileName = Path.GetFileName(path);
                    if (!_paths.TryGetValue(fileName, out List<string>? paths))
                    {
                        paths = [];
                        _paths.Add(fileName, paths);
                    }

                    if (paths.Count < MaxLocalCaseVariantCandidates)
                        paths.Add(path);
                    else
                        _overflow.Add(fileName);
                }
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException)
            {
                _complete = false;
                log?.Invoke(
                    $"Local RID package directory could not be inspected: "
                    + ex.GetType().Name);
            }
        }

        internal LocalSiblingCandidates GetCandidates(
            string expectedFileName)
        {
            Capture();
            _paths.TryGetValue(
                expectedFileName,
                out List<string>? paths);
            return new LocalSiblingCandidates(
                paths ?? [],
                _complete && !_overflow.Contains(expectedFileName));
        }
    }

    internal readonly record struct LocalSiblingCandidates(
        IReadOnlyList<string> Paths,
        bool Complete);

    private static bool? ToAvailability(NuspecProbeStatus status) =>
        status switch
        {
            NuspecProbeStatus.Present => true,
            NuspecProbeStatus.Absent => false,
            _ => null
        };

    internal static NuspecProbeStatus CombineEvidence(
        NuspecProbeStatus left,
        NuspecProbeStatus right)
    {
        if (left == NuspecProbeStatus.Present
            || right == NuspecProbeStatus.Present)
        {
            return NuspecProbeStatus.Present;
        }

        return left == NuspecProbeStatus.Indeterminate
            || right == NuspecProbeStatus.Indeterminate
                ? NuspecProbeStatus.Indeterminate
                : NuspecProbeStatus.Absent;
    }
}
