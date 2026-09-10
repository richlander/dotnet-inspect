using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

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
        DirectoryProbe rootProbe = ProbeDirectory(_dotnetRoot);
        if (rootProbe == DirectoryProbe.Missing)
        {
            return Unavailable<InstalledReferenceTargetInventory>(
                generation,
                InstalledPlatformSourceUnavailabilityKind.Unavailable,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The explicit dotnet hive is unavailable.");
        }
        if (rootProbe == DirectoryProbe.NotDirectory)
        {
            return Rejected<InstalledReferenceTargetInventory>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The explicit dotnet hive is not a directory.");
        }
        if (rootProbe == DirectoryProbe.Failed)
        {
            return Failed<InstalledReferenceTargetInventory>(
                generation,
                "The explicit dotnet hive could not be inspected.");
        }

        int observedEntries = 0;
        List<string> versionEntries;
        try
        {
            string? packsRoot = FindExactChild(
                _dotnetRoot,
                "packs",
                LocalEntryKind.Directory,
                ref observedEntries,
                cancellationToken);
            string? packRoot = packsRoot is null
                ? null
                : FindExactChild(
                    packsRoot,
                    PackName(request.Family),
                    LocalEntryKind.Directory,
                    ref observedEntries,
                    cancellationToken);
            if (packRoot is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return EmptyInventory(generation);
            }

            versionEntries = EnumerateEntriesBounded(
                packRoot,
                ref observedEntries,
                cancellationToken);
        }
        catch (InvalidLayoutException)
        {
            return Rejected<InstalledReferenceTargetInventory>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The installed reference-pack root does not match the canonical layout.");
        }
        catch (ObservedEntryLimitException)
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
                    LocalEntryKind.Directory,
                    ref observedEntries,
                    cancellationToken);
                if (referenceRoot is null)
                    continue;

                string? referenceDirectory = FindExactChild(
                    referenceRoot,
                    request.TargetFramework.ToString(),
                    LocalEntryKind.Directory,
                    ref observedEntries,
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
        catch (InvalidLayoutException)
        {
            return Rejected<InstalledReferenceTargetInventory>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "An installed reference-pack coordinate does not match the canonical layout.");
        }
        catch (ObservedEntryLimitException)
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

        DirectoryProbe rootProbe = ProbeDirectory(_dotnetRoot);
        if (rootProbe == DirectoryProbe.Missing)
        {
            return Unavailable<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceUnavailabilityKind.Unavailable,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The explicit dotnet hive is unavailable.");
        }
        if (rootProbe == DirectoryProbe.NotDirectory)
        {
            return Rejected<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The explicit dotnet hive is not a directory.");
        }
        if (rootProbe == DirectoryProbe.Failed)
        {
            return Failed<InstalledReferenceRealization>(
                generation,
                "The explicit dotnet hive could not be inspected.");
        }

        int observedEntries = 0;
        string? referenceDirectory;
        try
        {
            referenceDirectory = FindReferenceDirectory(
                request.Coordinate,
                ref observedEntries,
                cancellationToken);
        }
        catch (InvalidLayoutException)
        {
            return Rejected<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The exact installed reference-pack coordinate does not match the canonical layout.");
        }
        catch (ObservedEntryLimitException)
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
                        observedEntries,
                        cancellationToken)
                    .ConfigureAwait(false),
            InstalledReferencePopulationDemand.CompletePopulation =>
                await RealizePopulationAsync(
                        generation,
                        request,
                        referenceDirectory,
                        observedEntries,
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
            int observedEntries,
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
                LocalEntryKind.File,
                ref observedEntries,
                cancellationToken);
        }
        catch (InvalidLayoutException)
        {
            return Rejected<InstalledReferenceRealization>(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The installed reference assembly coordinate does not match the canonical layout.");
        }
        catch (ObservedEntryLimitException)
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
            int observedEntries,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<string> entries;
        try
        {
            entries = EnumerateEntriesBounded(
                referenceDirectory,
                ref observedEntries,
                cancellationToken);
        }
        catch (ObservedEntryLimitException)
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

    List<string> EnumerateEntriesBounded(
        string path,
        ref int observedEntries,
        CancellationToken cancellationToken)
    {
        var entries = new List<string>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observedEntries == _maxObservedEntries)
                throw new ObservedEntryLimitException();
            observedEntries++;
            entries.Add(entry);
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    string? FindReferenceDirectory(
        InstalledReferencePackCoordinate coordinate,
        ref int observedEntries,
        CancellationToken cancellationToken)
    {
        string? packsRoot = FindExactChild(
            _dotnetRoot,
            "packs",
            LocalEntryKind.Directory,
            ref observedEntries,
            cancellationToken);
        if (packsRoot is null)
            return null;
        string? packRoot = FindExactChild(
            packsRoot,
            PackName(coordinate.Family),
            LocalEntryKind.Directory,
            ref observedEntries,
            cancellationToken);
        if (packRoot is null)
            return null;
        string? versionRoot = FindExactChild(
            packRoot,
            coordinate.Version.Value,
            LocalEntryKind.Directory,
            ref observedEntries,
            cancellationToken);
        if (versionRoot is null)
            return null;
        string? referenceRoot = FindExactChild(
            versionRoot,
            "ref",
            LocalEntryKind.Directory,
            ref observedEntries,
            cancellationToken);
        if (referenceRoot is null)
            return null;
        return FindExactChild(
            referenceRoot,
            coordinate.TargetFramework.ToString(),
            LocalEntryKind.Directory,
            ref observedEntries,
            cancellationToken);
    }

    string? FindExactChild(
        string parent,
        string expectedName,
        LocalEntryKind expectedKind,
        ref int observedEntries,
        CancellationToken cancellationToken)
    {
        bool foundCaseVariant = false;
        foreach (string entry in Directory.EnumerateFileSystemEntries(parent))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observedEntries == _maxObservedEntries)
                throw new ObservedEntryLimitException();
            observedEntries++;

            string actualName = Path.GetFileName(entry);
            if (string.Equals(
                    actualName,
                    expectedName,
                    StringComparison.Ordinal))
            {
                bool isDirectory = IsDirectory(entry);
                if (isDirectory != (expectedKind == LocalEntryKind.Directory))
                    throw new InvalidLayoutException();
                return entry;
            }

            foundCaseVariant |= string.Equals(
                actualName,
                expectedName,
                StringComparison.OrdinalIgnoreCase);
        }

        if (foundCaseVariant)
            throw new InvalidLayoutException();
        return null;
    }

    static bool IsDirectory(string path) =>
        (File.GetAttributes(path) & FileAttributes.Directory) != 0;

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

    static DirectoryProbe ProbeDirectory(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0
                ? DirectoryProbe.Directory
                : DirectoryProbe.NotDirectory;
        }
        catch (Exception ex) when (
            ex is FileNotFoundException
                or DirectoryNotFoundException)
        {
            return DirectoryProbe.Missing;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return DirectoryProbe.Failed;
        }
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

    private sealed class ObservedEntryLimitException : Exception
    {
    }

    private sealed class InvalidLayoutException : Exception
    {
    }

    private enum LocalEntryKind
    {
        File,
        Directory,
    }

    private enum DirectoryProbe
    {
        Missing,
        Directory,
        NotDirectory,
        Failed,
    }
}
