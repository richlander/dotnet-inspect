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
                    probe = new NuspecProbeResult(
                        null,
                        NuspecProbeStatus.Absent);
                    var candidateBudget =
                        new LocalCaseVariantProbeBudget();
                    var probedPaths =
                        new HashSet<string>(StringComparer.Ordinal);
                    foreach (string versionSpelling in
                             LocalVersionSpellings(
                                 version,
                                 normalizedVersion))
                    {
                        string expectedFileName =
                            $"{ridPkg.PackageId}.{versionSpelling}.nupkg";
                        NuspecProbeResult spellingProbe =
                            await ProbeLocalPackageArchiveAsync(
                                localSnapshot,
                                expectedFileName,
                                ridPkg.PackageId,
                                normalizedVersion,
                                logger.Log,
                                candidateBudget,
                                probedPaths).ConfigureAwait(false);
                        probe = new NuspecProbeResult(
                            spellingProbe.Xml ?? probe.Xml,
                            CombineEvidence(
                                probe.Status,
                                spellingProbe.Status));
                        if (probe.Status == NuspecProbeStatus.Present)
                            break;
                    }
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
            Action<string>? log,
            LocalCaseVariantProbeBudget? candidateBudget = null,
            HashSet<string>? probedPaths = null)
    {
        candidateBudget ??= new LocalCaseVariantProbeBudget();
            probedPaths ??= new HashSet<string>(StringComparer.Ordinal);
        string expectedPath =
            Path.Combine(snapshot.LocalDirectory, expectedFileName);
        probedPaths.Add(expectedPath);
        NuspecProbeResult exact =
            await PackageExtractor.ProbeLocalPackageArchiveAsync(
                expectedPath,
                packageId,
                version,
                log).ConfigureAwait(false);
        if (exact.Status == NuspecProbeStatus.Present)
            return exact;

        NuspecProbeStatus status = exact.Status;
        LocalSiblingCandidates candidates =
            snapshot.GetCandidates(expectedFileName);
        foreach (string candidatePath in candidates.Paths)
        {
            if (!probedPaths.Add(candidatePath))
                continue;
            if (exact.Status != NuspecProbeStatus.Absent
                && candidates.Paths.Count == 1
                && string.Equals(
                    expectedPath,
                    candidatePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!candidateBudget.TryConsume())
            {
                status = CombineEvidence(
                    status,
                    NuspecProbeStatus.Indeterminate);
                break;
            }

            NuspecProbeResult candidate =
                await PackageExtractor.ProbeLocalPackageArchiveAsync(
                    candidatePath,
                    packageId,
                    version,
                    log).ConfigureAwait(false);
            if (candidate.Status == NuspecProbeStatus.Present)
                return candidate;

            status = CombineEvidence(status, candidate.Status);
        }

        if (candidates.Paths.Count > 0 || !candidates.Complete)
        {
            status = CombineEvidence(
                status,
                NuspecProbeStatus.Indeterminate);
        }

        return new NuspecProbeResult(null, status);
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

                    if (paths.Count < MaxLocalCaseVariantCandidates + 1)
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

    internal sealed class LocalCaseVariantProbeBudget
    {
        private int _remaining = MaxLocalCaseVariantCandidates;

        internal bool TryConsume()
        {
            if (_remaining == 0)
                return false;

            _remaining--;
            return true;
        }
    }

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

    private static IEnumerable<string> LocalVersionSpellings(
        string version,
        string normalizedVersion)
    {
        yield return version;
        if (!string.Equals(
                version,
                normalizedVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            yield return normalizedVersion;
        }
    }
}
