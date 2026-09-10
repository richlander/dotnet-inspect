using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using static DotnetInspector.Platforms.Installed.InstalledHiveFileSystem;

namespace DotnetInspector.Platforms.Installed;

/// <summary>
/// Discovers and snapshots reference packs from one explicit dotnet hive.
/// </summary>
public sealed class InstalledReferencePackSource
{
    public const int DefaultMaxObservedEntries = 1024;
    public const long DefaultMaxFileBytes = 512L * 1024 * 1024;

    private static long s_nextGeneration;
    private readonly string _dotnetRoot;
    private readonly int _maxObservedEntries;
    private readonly long _maxFileBytes;

    public InstalledReferencePackSource(
        InstalledDotnetHiveIdentity hive,
        string dotnetRoot,
        int maxObservedEntries = DefaultMaxObservedEntries,
        long maxFileBytes = DefaultMaxFileBytes)
    {
        ArgumentNullException.ThrowIfNull(hive);
        ArgumentException.ThrowIfNullOrWhiteSpace(dotnetRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxObservedEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileBytes);

        Hive = hive;
        _dotnetRoot = Path.GetFullPath(dotnetRoot);
        _maxObservedEntries = maxObservedEntries;
        _maxFileBytes = maxFileBytes;
    }

    public InstalledDotnetHiveIdentity Hive { get; }

    public InstalledPlatformSourceOutcome<InstalledReferenceTargetInventory>
        Discover(
            InstalledReferenceDiscoveryRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        InstalledPlatformSourceGeneration generation = NextGeneration();
        if (OperatingSystem.IsBrowser())
        {
            return Unavailable<InstalledReferenceTargetInventory>(
                generation,
                InstalledPlatformSourceUnavailabilityKind.Unavailable,
                InstalledPlatformSourceDiagnosticKind.UnsupportedHost,
                "Installed reference-pack discovery is unavailable in Browser/Wasm.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        InstalledDirectoryProbe rootProbe = ProbeDirectory(_dotnetRoot);
        if (rootProbe == InstalledDirectoryProbe.Missing)
        {
            return Unavailable<InstalledReferenceTargetInventory>(
                generation,
                InstalledPlatformSourceUnavailabilityKind.Unavailable,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The explicit dotnet hive is unavailable.");
        }
        if (rootProbe == InstalledDirectoryProbe.NotDirectory)
        {
            return Rejected<InstalledReferenceTargetInventory>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The explicit dotnet hive is not a directory.");
        }
        if (rootProbe == InstalledDirectoryProbe.Failed)
        {
            return Failed<InstalledReferenceTargetInventory>(
                generation,
                "The explicit dotnet hive could not be inspected.");
        }

        var observation = new InstalledObservationBudget(
            _maxObservedEntries);
        List<string> versionEntries;
        try
        {
            string? packsRoot = FindExactChild(
                _dotnetRoot,
                "packs",
                InstalledEntryKind.Directory,
                observation,
                cancellationToken);
            string? packRoot = packsRoot is null
                ? null
                : FindExactChild(
                    packsRoot,
                    PackName(request.Family),
                    InstalledEntryKind.Directory,
                    observation,
                    cancellationToken);
            if (packRoot is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return EmptyInventory(generation);
            }

            versionEntries = EnumerateEntriesBounded(
                packRoot,
                observation,
                cancellationToken);
        }
        catch (InstalledInvalidLayoutException)
        {
            return Rejected<InstalledReferenceTargetInventory>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The installed reference-pack root does not match the canonical layout.");
        }
        catch (InstalledObservationLimitException)
        {
            return Incomplete<InstalledReferenceTargetInventory>(
                generation,
                "The installed reference-pack inventory exceeds the configured observation limit.");
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return Failed<InstalledReferenceTargetInventory>(
                generation,
                "The installed reference-pack inventory could not be read.");
        }

        var targets = new List<InstalledReferenceTarget>();
        try
        {
            foreach (string versionEntry in versionEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsDirectory(versionEntry))
                    continue;

                string? referenceRoot = FindExactChild(
                    versionEntry,
                    "ref",
                    InstalledEntryKind.Directory,
                    observation,
                    cancellationToken);
                if (referenceRoot is null)
                    continue;

                string? referenceDirectory = FindExactChild(
                    referenceRoot,
                    request.TargetFramework.ToString(),
                    InstalledEntryKind.Directory,
                    observation,
                    cancellationToken);
                if (referenceDirectory is null)
                    continue;

                string versionName = Path.GetFileName(versionEntry);
                if (!PlatformVersion.TryParse(
                        versionName,
                        out PlatformVersion? version))
                {
                    return Rejected<InstalledReferenceTargetInventory>(
                        generation,
                        InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                        "An installed reference-pack directory with the requested target framework has a non-canonical version.");
                }

                if (version.Major != request.TargetFramework.Major
                    || version.Minor != request.TargetFramework.Minor)
                {
                    continue;
                }

                if (targets.Count == request.MaxCandidates)
                {
                    return Incomplete<InstalledReferenceTargetInventory>(
                        generation,
                        "The installed reference-pack target inventory exceeds the request candidate limit.");
                }

                targets.Add(
                    new InstalledReferenceTarget(
                        new InstalledReferencePackCoordinate(
                            Hive,
                            request.Family,
                            request.TargetFramework,
                            version)));
            }
        }
        catch (InstalledInvalidLayoutException)
        {
            return Rejected<InstalledReferenceTargetInventory>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "An installed reference-pack coordinate does not match the canonical layout.");
        }
        catch (InstalledObservationLimitException)
        {
            return Incomplete<InstalledReferenceTargetInventory>(
                generation,
                "The installed reference-pack inventory exceeds the configured observation limit.");
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return Failed<InstalledReferenceTargetInventory>(
                generation,
                "The installed reference-pack inventory could not be read.");
        }

        targets.Sort(
            static (left, right) =>
            {
                int precedence =
                    PlatformVersion.SemanticPrecedenceComparer.Compare(
                        left.Coordinate.Version,
                        right.Coordinate.Version);
                return precedence != 0
                    ? precedence
                    : string.CompareOrdinal(
                        left.Coordinate.Version.Value,
                        right.Coordinate.Version.Value);
            });

        IReadOnlyList<InstalledReferenceTarget> snapshot =
            Array.AsReadOnly(targets.ToArray());
        cancellationToken.ThrowIfCancellationRequested();
        return new InstalledPlatformSourceOutcome<
            InstalledReferenceTargetInventory>.Succeeded(
                generation,
                new InstalledReferenceTargetInventory(generation, snapshot));
    }

    public async ValueTask<
        InstalledPlatformSourceOutcome<InstalledReferenceRealization>>
        RealizeAsync(
            InstalledReferenceRealizationRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        InstalledPlatformSourceGeneration generation = NextGeneration();
        if (OperatingSystem.IsBrowser())
        {
            return Unavailable<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceUnavailabilityKind.Unavailable,
                InstalledPlatformSourceDiagnosticKind.UnsupportedHost,
                "Installed reference-pack realization is unavailable in Browser/Wasm.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(request.Coordinate.Hive, Hive))
        {
            return Rejected<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidCoordinate,
                "The installed reference-pack coordinate belongs to a different dotnet hive.");
        }

        InstalledDirectoryProbe rootProbe = ProbeDirectory(_dotnetRoot);
        if (rootProbe == InstalledDirectoryProbe.Missing)
        {
            return Unavailable<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceUnavailabilityKind.Unavailable,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The explicit dotnet hive is unavailable.");
        }
        if (rootProbe == InstalledDirectoryProbe.NotDirectory)
        {
            return Rejected<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The explicit dotnet hive is not a directory.");
        }
        if (rootProbe == InstalledDirectoryProbe.Failed)
        {
            return Failed<InstalledReferenceRealization>(
                generation,
                "The explicit dotnet hive could not be inspected.");
        }

        var observation = new InstalledObservationBudget(
            _maxObservedEntries);
        string? referenceDirectory;
        try
        {
            referenceDirectory = FindReferenceDirectory(
                request.Coordinate,
                observation,
                cancellationToken);
        }
        catch (InstalledInvalidLayoutException)
        {
            return Rejected<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The exact installed reference-pack coordinate does not match the canonical layout.");
        }
        catch (InstalledObservationLimitException)
        {
            return Incomplete<InstalledReferenceRealization>(
                generation,
                "Resolving the exact installed reference-pack coordinate exceeds the configured observation limit.");
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return Failed<InstalledReferenceRealization>(
                generation,
                "The exact installed reference-pack coordinate could not be inspected.");
        }

        if (referenceDirectory is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Unavailable<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceUnavailabilityKind.Absent,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The exact installed reference-pack coordinate is absent.");
        }

        return request.Population switch
        {
            InstalledReferencePopulationDemand.Assembly assembly =>
                await RealizeAssemblyAsync(
                        generation,
                        request,
                        referenceDirectory,
                        assembly.Identity,
                        observation,
                        cancellationToken)
                    .ConfigureAwait(false),
            InstalledReferencePopulationDemand.CompletePopulation =>
                await RealizePopulationAsync(
                        generation,
                        request,
                        referenceDirectory,
                        observation,
                        cancellationToken)
                    .ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "Unknown installed reference population demand."),
        };
    }

    async ValueTask<
        InstalledPlatformSourceOutcome<InstalledReferenceRealization>>
        RealizeAssemblyAsync(
            InstalledPlatformSourceGeneration generation,
            InstalledReferenceRealizationRequest request,
            string referenceDirectory,
            AssemblyReferenceIdentity requestedIdentity,
            InstalledObservationBudget observation,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetAssemblyFileName(
                requestedIdentity.Name,
                out string? fileName))
        {
            return Rejected<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidCoordinate,
                "The requested assembly identity cannot be projected to an installed reference-pack member.");
        }

        if (request.Work.MaxAssemblies == 0)
        {
            return Incomplete<InstalledReferenceRealization>(
                generation,
                "The request assembly budget does not permit reference-pack realization.");
        }

        string? path;
        try
        {
            path = FindExactChild(
                referenceDirectory,
                fileName!,
                InstalledEntryKind.File,
                observation,
                cancellationToken);
        }
        catch (InstalledInvalidLayoutException)
        {
            return Rejected<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The installed reference assembly coordinate does not match the canonical layout.");
        }
        catch (InstalledObservationLimitException)
        {
            return Incomplete<InstalledReferenceRealization>(
                generation,
                "Resolving the installed reference assembly exceeds the configured observation limit.");
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return Failed<InstalledReferenceRealization>(
                generation,
                "The installed reference assembly coordinate could not be inspected.");
        }

        if (path is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Unavailable<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceUnavailabilityKind.Absent,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The requested installed reference assembly is absent.");
        }

        InstalledPlatformSourceOutcome<InstalledReferenceLibrary>
            libraryOutcome = await SnapshotAssemblyAsync(
                    generation,
                    path,
                    request.Work.MaxBytes,
                    cancellationToken)
                .ConfigureAwait(false);
        if (libraryOutcome is not InstalledPlatformSourceOutcome<
                InstalledReferenceLibrary>.Succeeded succeeded)
        {
            return ProjectLibraryOutcome(
                generation,
                libraryOutcome);
        }

        if (!requestedIdentity.IsEquivalentTo(succeeded.Value.Identity))
        {
            return Rejected<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceDiagnosticKind.AssemblyIdentityMismatch,
                "The installed reference-pack member does not match the requested assembly identity.");
        }

        return Success(
            generation,
            request,
            [succeeded.Value]);
    }

    async ValueTask<
        InstalledPlatformSourceOutcome<InstalledReferenceRealization>>
        RealizePopulationAsync(
            InstalledPlatformSourceGeneration generation,
            InstalledReferenceRealizationRequest request,
            string referenceDirectory,
            InstalledObservationBudget observation,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<string> entries;
        try
        {
            entries = EnumerateEntriesBounded(
                referenceDirectory,
                observation,
                cancellationToken);
        }
        catch (InstalledObservationLimitException)
        {
            return Incomplete<InstalledReferenceRealization>(
                generation,
                "The installed reference-pack population exceeds the configured observation limit.");
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return Failed<InstalledReferenceRealization>(
                generation,
                "The installed reference-pack population could not be enumerated.");
        }

        var assemblyPaths = new List<string>();
        try
        {
            foreach (string entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsDirectory(entry)
                    && string.Equals(
                        Path.GetExtension(entry),
                        ".dll",
                        StringComparison.OrdinalIgnoreCase))
                {
                    assemblyPaths.Add(entry);
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return Failed<InstalledReferenceRealization>(
                generation,
                "The installed reference-pack population could not be classified.");
        }
        if (assemblyPaths.Count > request.Work.MaxAssemblies)
        {
            return Incomplete<InstalledReferenceRealization>(
                generation,
                "The installed reference-pack population exceeds the request assembly budget.");
        }

        var libraries = new List<InstalledReferenceLibrary>(
            assemblyPaths.Count);
        var identities = new HashSet<AssemblyReferenceIdentity>(
            AssemblyReferenceIdentity.EquivalentComparer);
        long remainingBytes = request.Work.MaxBytes;
        foreach (string assemblyPath in assemblyPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstalledPlatformSourceOutcome<InstalledReferenceLibrary>
                libraryOutcome = await SnapshotAssemblyAsync(
                        generation,
                        assemblyPath,
                        remainingBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (libraryOutcome is not InstalledPlatformSourceOutcome<
                    InstalledReferenceLibrary>.Succeeded succeeded)
            {
                return ProjectLibraryOutcome(
                    generation,
                    libraryOutcome);
            }

            if (!identities.Add(succeeded.Value.Identity))
            {
                return Rejected<InstalledReferenceRealization>(
                    generation,
                    InstalledPlatformSourceDiagnosticKind
                        .DuplicateAssemblyIdentity,
                    "The installed reference pack contains duplicate assembly identities.");
            }

            libraries.Add(succeeded.Value);
            remainingBytes -= succeeded.Value.ContentLength;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Success(
            generation,
            request,
            libraries);
    }

    async ValueTask<
        InstalledPlatformSourceOutcome<InstalledReferenceLibrary>>
        SnapshotAssemblyAsync(
            InstalledPlatformSourceGeneration generation,
            string path,
            long remainingBytes,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            long allowedBytes = Math.Min(_maxFileBytes, remainingBytes);
            if (stream.Length > allowedBytes)
            {
                return Incomplete<InstalledReferenceLibrary>(
                    generation,
                    "An installed reference assembly exceeds the request byte budget.");
            }

            byte[] content = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(
                    content,
                    cancellationToken)
                .ConfigureAwait(false);

            using var peReader = new PEReader(
                content.ToImmutableArray());
            if (!peReader.HasMetadata)
            {
                return Rejected<InstalledReferenceLibrary>(
                    generation,
                    InstalledPlatformSourceDiagnosticKind.MalformedAssembly,
                    "An installed reference-pack DLL does not contain ECMA-335 metadata.");
            }

            MetadataReader metadata = peReader.GetMetadataReader();
            if (metadata.MetadataKind != MetadataKind.Ecma335
                || !metadata.IsAssembly)
            {
                return Rejected<InstalledReferenceLibrary>(
                    generation,
                    InstalledPlatformSourceDiagnosticKind.MalformedAssembly,
                    "An installed reference-pack DLL is not a supported ECMA-335 assembly.");
            }

            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(metadata);
            cancellationToken.ThrowIfCancellationRequested();
            return new InstalledPlatformSourceOutcome<
                InstalledReferenceLibrary>.Succeeded(
                    generation,
                    new InstalledReferenceLibrary(
                        Path.GetFileName(path),
                        identity,
                        content));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BadImageFormatException)
        {
            return Rejected<InstalledReferenceLibrary>(
                generation,
                InstalledPlatformSourceDiagnosticKind.MalformedAssembly,
                "An installed reference-pack DLL contains malformed metadata.");
        }
        catch (Exception ex) when (
            ex is FileNotFoundException
                or DirectoryNotFoundException)
        {
            return Unavailable<InstalledReferenceLibrary>(
                generation,
                InstalledPlatformSourceUnavailabilityKind.Absent,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The requested installed reference assembly is absent.");
        }
        catch (OverflowException)
        {
            return Incomplete<InstalledReferenceLibrary>(
                generation,
                "An installed reference assembly exceeds the supported snapshot size.");
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return Failed<InstalledReferenceLibrary>(
                generation,
                "An installed reference assembly could not be read.");
        }
    }

    InstalledPlatformSourceOutcome<InstalledReferenceRealization> Success(
        InstalledPlatformSourceGeneration generation,
        InstalledReferenceRealizationRequest request,
        IEnumerable<InstalledReferenceLibrary> libraries)
    {
        IReadOnlyList<InstalledReferenceLibrary> snapshot =
            Array.AsReadOnly(libraries.ToArray());
        return new InstalledPlatformSourceOutcome<
            InstalledReferenceRealization>.Succeeded(
                generation,
                new InstalledReferenceRealization(
                    generation,
                    request.Coordinate,
                    request.Population,
                    snapshot));
    }

    InstalledPlatformSourceOutcome<InstalledReferenceRealization>
        ProjectLibraryOutcome(
            InstalledPlatformSourceGeneration generation,
            InstalledPlatformSourceOutcome<InstalledReferenceLibrary> outcome)
        => outcome switch
        {
            InstalledPlatformSourceOutcome<
                InstalledReferenceLibrary>.Unavailable unavailable =>
                new InstalledPlatformSourceOutcome<
                    InstalledReferenceRealization>.Unavailable(
                        generation,
                        unavailable.Reason,
                        unavailable.Diagnostic),
            InstalledPlatformSourceOutcome<
                InstalledReferenceLibrary>.Rejected rejected =>
                new InstalledPlatformSourceOutcome<
                    InstalledReferenceRealization>.Rejected(
                        generation,
                        rejected.Diagnostic),
            InstalledPlatformSourceOutcome<
                InstalledReferenceLibrary>.Incomplete incomplete =>
                new InstalledPlatformSourceOutcome<
                    InstalledReferenceRealization>.Incomplete(
                        generation,
                        incomplete.Diagnostic),
            InstalledPlatformSourceOutcome<
                InstalledReferenceLibrary>.Failed failed =>
                new InstalledPlatformSourceOutcome<
                    InstalledReferenceRealization>.Failed(
                        generation,
                        failed.Diagnostic),
            _ => throw new InvalidOperationException(
                "Successful library outcomes must be handled before projection."),
        };

    InstalledPlatformSourceOutcome<InstalledReferenceTargetInventory>
        EmptyInventory(InstalledPlatformSourceGeneration generation) =>
        new InstalledPlatformSourceOutcome<
            InstalledReferenceTargetInventory>.Succeeded(
                generation,
                new InstalledReferenceTargetInventory(
                    generation,
                    Array.Empty<InstalledReferenceTarget>()));

    string? FindReferenceDirectory(
        InstalledReferencePackCoordinate coordinate,
        InstalledObservationBudget observation,
        CancellationToken cancellationToken)
    {
        string? packsRoot = FindExactChild(
            _dotnetRoot,
            "packs",
            InstalledEntryKind.Directory,
            observation,
            cancellationToken);
        if (packsRoot is null)
            return null;
        string? packRoot = FindExactChild(
            packsRoot,
            PackName(coordinate.Family),
            InstalledEntryKind.Directory,
            observation,
            cancellationToken);
        if (packRoot is null)
            return null;
        string? versionRoot = FindExactChild(
            packRoot,
            coordinate.Version.Value,
            InstalledEntryKind.Directory,
            observation,
            cancellationToken);
        if (versionRoot is null)
            return null;
        string? referenceRoot = FindExactChild(
            versionRoot,
            "ref",
            InstalledEntryKind.Directory,
            observation,
            cancellationToken);
        if (referenceRoot is null)
            return null;
        return FindExactChild(
            referenceRoot,
            coordinate.TargetFramework.ToString(),
            InstalledEntryKind.Directory,
            observation,
            cancellationToken);
    }

    static string PackName(InstalledPlatformFamily family) =>
        family switch
        {
            InstalledPlatformFamily.DotNetRuntime =>
                "Microsoft.NETCore.App.Ref",
            InstalledPlatformFamily.AspNetCore =>
                "Microsoft.AspNetCore.App.Ref",
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };

    static bool TryGetAssemblyFileName(
        string assemblyName,
        out string? fileName)
    {
        fileName = null;
        if (string.IsNullOrWhiteSpace(assemblyName)
            || assemblyName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || assemblyName.Contains(Path.DirectorySeparatorChar)
            || assemblyName.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        fileName = assemblyName + ".dll";
        return true;
    }

    static InstalledPlatformSourceOutcome<T> Unavailable<T>(
        InstalledPlatformSourceGeneration generation,
        InstalledPlatformSourceUnavailabilityKind reason,
        InstalledPlatformSourceDiagnosticKind kind,
        string summary)
        where T : notnull =>
        new InstalledPlatformSourceOutcome<T>.Unavailable(
            generation,
            reason,
            new InstalledPlatformSourceDiagnostic(kind, summary));

    static InstalledPlatformSourceOutcome<T> Rejected<T>(
        InstalledPlatformSourceGeneration generation,
        InstalledPlatformSourceDiagnosticKind kind,
        string summary)
        where T : notnull =>
        new InstalledPlatformSourceOutcome<T>.Rejected(
            generation,
            new InstalledPlatformSourceDiagnostic(kind, summary));

    static InstalledPlatformSourceOutcome<T> Incomplete<T>(
        InstalledPlatformSourceGeneration generation,
        string summary)
        where T : notnull =>
        new InstalledPlatformSourceOutcome<T>.Incomplete(
            generation,
            new InstalledPlatformSourceDiagnostic(
                InstalledPlatformSourceDiagnosticKind.WorkLimitExceeded,
                summary));

    static InstalledPlatformSourceOutcome<T> Failed<T>(
        InstalledPlatformSourceGeneration generation,
        string summary)
        where T : notnull =>
        new InstalledPlatformSourceOutcome<T>.Failed(
            generation,
            new InstalledPlatformSourceDiagnostic(
                InstalledPlatformSourceDiagnosticKind.IoFailure,
                summary));

    InstalledPlatformSourceGeneration NextGeneration() =>
        InstalledPlatformSourceGeneration.Issue(
            $"{Hive.Name}-attempt-"
            + Interlocked.Increment(ref s_nextGeneration));

}
