using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Platforms.Formats;
using ILInspector.Metadata;
using static DotnetInspector.Platforms.Installed.InstalledHiveFileSystem;

namespace DotnetInspector.Platforms.Installed;

/// <summary>
/// Realizes one manifest-defined implementation closure from an explicit
/// dotnet hive.
/// </summary>
public sealed class InstalledImplementationPlatformSource
{
    public const int DefaultMaxObservedEntries = 4096;
    public const int DefaultMaxManifestBytes = 4 * 1024 * 1024;
    public const long DefaultMaxFileBytes = 512L * 1024 * 1024;

    private static long s_nextGeneration;
    private static readonly PlatformFrameworkName DotNetRuntimeName =
        PlatformFrameworkName.Parse("Microsoft.NETCore.App");
    private static readonly PlatformFrameworkName AspNetCoreName =
        PlatformFrameworkName.Parse("Microsoft.AspNetCore.App");
    private readonly string _dotnetRoot;
    private readonly int _maxObservedEntries;
    private readonly int _maxManifestBytes;
    private readonly long _maxFileBytes;

    public InstalledImplementationPlatformSource(
        InstalledDotnetHiveIdentity hive,
        string dotnetRoot,
        int maxObservedEntries = DefaultMaxObservedEntries,
        int maxManifestBytes = DefaultMaxManifestBytes,
        long maxFileBytes = DefaultMaxFileBytes)
    {
        ArgumentNullException.ThrowIfNull(hive);
        ArgumentException.ThrowIfNullOrWhiteSpace(dotnetRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxObservedEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxManifestBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileBytes);

        Hive = hive;
        _dotnetRoot = Path.GetFullPath(dotnetRoot);
        _maxObservedEntries = maxObservedEntries;
        _maxManifestBytes = maxManifestBytes;
        _maxFileBytes = maxFileBytes;
    }

    public InstalledDotnetHiveIdentity Hive { get; }

    public async ValueTask<
        InstalledPlatformSourceOutcome<InstalledImplementationRealization>>
        RealizeAsync(
            InstalledImplementationRealizationRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        InstalledPlatformSourceGeneration generation = NextGeneration();
        if (OperatingSystem.IsBrowser())
        {
            return Unavailable(
                generation,
                InstalledPlatformSourceUnavailabilityKind.Unavailable,
                InstalledPlatformSourceDiagnosticKind.UnsupportedHost,
                "Installed implementation realization is unavailable in Browser/Wasm.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(request.Coordinate.Hive, Hive))
        {
            return Rejected(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidCoordinate,
                "The implementation coordinate belongs to a different dotnet hive.");
        }
        if (request.Work.MaxFrameworks == 0
            || request.Work.MaxResolutionSteps == 0
            || request.Work.MaxManifestLibraries == 0
            || request.Work.MaxManifestAssets == 0
            || request.Work.MaxAssemblies == 0
            || request.Work.MaxBytes == 0)
        {
            return Incomplete(
                generation,
                "The request work budget does not permit implementation realization.");
        }

        InstalledDirectoryProbe rootProbe = ProbeDirectory(_dotnetRoot);
        if (rootProbe == InstalledDirectoryProbe.Missing)
        {
            return Unavailable(
                generation,
                InstalledPlatformSourceUnavailabilityKind.Unavailable,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The explicit dotnet hive is unavailable.");
        }
        if (rootProbe == InstalledDirectoryProbe.NotDirectory)
        {
            return Rejected(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The explicit dotnet hive is not a directory.");
        }
        if (rootProbe == InstalledDirectoryProbe.Failed)
        {
            return Failed(
                generation,
                "The explicit dotnet hive could not be inspected.");
        }

        try
        {
            var attempt = new RealizationAttempt(
                this,
                request,
                generation,
                cancellationToken);
            InstalledImplementationRealization realization =
                await attempt.RunAsync().ConfigureAwait(false);
            return new InstalledPlatformSourceOutcome<
                InstalledImplementationRealization>.Succeeded(
                    generation,
                    realization);
        }
        catch (InstalledObservationLimitException)
        {
            return Incomplete(
                generation,
                "Installed implementation discovery exceeds the configured observation limit.");
        }
        catch (InstalledInvalidLayoutException)
        {
            return Rejected(
                generation,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                "The installed implementation source does not match the canonical layout.");
        }
        catch (ImplementationSourceException ex)
        {
            return ex.Outcome switch
            {
                ImplementationSourceOutcome.Unavailable =>
                    Unavailable(
                        generation,
                        InstalledPlatformSourceUnavailabilityKind.Absent,
                        ex.Kind,
                        ex.Message),
                ImplementationSourceOutcome.Rejected =>
                    Rejected(generation, ex.Kind, ex.Message),
                ImplementationSourceOutcome.Incomplete =>
                    Incomplete(generation, ex.Message),
                ImplementationSourceOutcome.Failed =>
                    Failed(generation, ex.Message),
                _ => throw new InvalidOperationException(
                    "Unknown implementation-source outcome."),
            };
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return Failed(
                generation,
                "The installed implementation source could not be read.");
        }
    }

    private InstalledPlatformSourceGeneration NextGeneration() =>
        InstalledPlatformSourceGeneration.Issue(
            $"{Hive.Name}-implementation-attempt-"
            + Interlocked.Increment(ref s_nextGeneration));

    private static InstalledPlatformSourceOutcome<
        InstalledImplementationRealization> Unavailable(
            InstalledPlatformSourceGeneration generation,
            InstalledPlatformSourceUnavailabilityKind reason,
            InstalledPlatformSourceDiagnosticKind kind,
            string summary) =>
        new InstalledPlatformSourceOutcome<
            InstalledImplementationRealization>.Unavailable(
                generation,
                reason,
                new InstalledPlatformSourceDiagnostic(kind, summary));

    private static InstalledPlatformSourceOutcome<
        InstalledImplementationRealization> Rejected(
            InstalledPlatformSourceGeneration generation,
            InstalledPlatformSourceDiagnosticKind kind,
            string summary) =>
        new InstalledPlatformSourceOutcome<
            InstalledImplementationRealization>.Rejected(
                generation,
                new InstalledPlatformSourceDiagnostic(kind, summary));

    private static InstalledPlatformSourceOutcome<
        InstalledImplementationRealization> Incomplete(
            InstalledPlatformSourceGeneration generation,
            string summary) =>
        new InstalledPlatformSourceOutcome<
            InstalledImplementationRealization>.Incomplete(
                generation,
                new InstalledPlatformSourceDiagnostic(
                    InstalledPlatformSourceDiagnosticKind.WorkLimitExceeded,
                    summary));

    private static InstalledPlatformSourceOutcome<
        InstalledImplementationRealization> Failed(
            InstalledPlatformSourceGeneration generation,
            string summary) =>
        new InstalledPlatformSourceOutcome<
            InstalledImplementationRealization>.Failed(
                generation,
                new InstalledPlatformSourceDiagnostic(
                    InstalledPlatformSourceDiagnosticKind.IoFailure,
                    summary));

    private sealed class RealizationAttempt
    {
        private readonly InstalledImplementationPlatformSource _source;
        private readonly InstalledImplementationRealizationRequest _request;
        private readonly InstalledPlatformSourceGeneration _generation;
        private readonly CancellationToken _cancellationToken;
        private readonly InstalledObservationBudget _observation;
        private readonly Dictionary<
            PlatformFrameworkName,
            FrameworkInventory> _inventories = [];
        private readonly Dictionary<
            FrameworkCoordinate,
            FrameworkSnapshot> _snapshots = [];
        private readonly Dictionary<
            FrameworkCoordinate,
            IReadOnlyDictionary<string, FrameworkDirectoryEntry>>
                _frameworkEntries = [];
        private long _remainingBytes;
        private int _resolutionSteps;
        private string? _sharedRoot;
        private IReadOnlyDictionary<string, FrameworkDirectoryEntry>?
            _dotnetRootEntries;
        private IReadOnlyDictionary<string, FrameworkDirectoryEntry>?
            _sharedFrameworkEntries;

        internal RealizationAttempt(
            InstalledImplementationPlatformSource source,
            InstalledImplementationRealizationRequest request,
            InstalledPlatformSourceGeneration generation,
            CancellationToken cancellationToken)
        {
            _source = source;
            _request = request;
            _generation = generation;
            _cancellationToken = cancellationToken;
            _observation = new InstalledObservationBudget(
                source._maxObservedEntries);
            _remainingBytes = request.Work.MaxBytes;
        }

        internal async ValueTask<InstalledImplementationRealization>
            RunAsync()
        {
            var rootReference = EffectiveFrameworkReference.Exact(
                _request.Coordinate.Version);
            PlatformFrameworkName rootName = FrameworkName(
                _request.Coordinate.Family);
            var effective = new Dictionary<
                PlatformFrameworkName,
                EffectiveFrameworkReference>
            {
                [rootName] = rootReference,
            };

            while (true)
            {
                Step();
                var selected = new Dictionary<
                    PlatformFrameworkName,
                    SelectedFramework>();
                Dictionary<
                    PlatformFrameworkName,
                    EffectiveFrameworkReference> proposed =
                    new()
                    {
                        [rootName] = rootReference,
                    };
                var declaredVersions = new Dictionary<
                    PlatformFrameworkName,
                    List<PlatformVersion>>
                {
                    [rootName] = [_request.Coordinate.Version],
                };
                var pending = new SortedSet<PlatformFrameworkName>(
                    Comparer<PlatformFrameworkName>.Create(
                        static (left, right) =>
                            string.CompareOrdinal(
                                left.Value,
                                right.Value)));
                var provisionalFailures = new Dictionary<
                    PlatformFrameworkName,
                    Exception>();
                var graphFailures = new List<
                    (PlatformFrameworkName Name, Exception Error)>();
                pending.Add(rootName);

                while (pending.Count > 0)
                {
                    Step();
                    PlatformFrameworkName name = pending.Min!;
                    pending.Remove(name);
                    EffectiveFrameworkReference selectionReference =
                        effective.TryGetValue(
                            name,
                            out EffectiveFrameworkReference? prior)
                            ? prior
                            : proposed[name];
                    SelectedFramework framework;
                    try
                    {
                        framework = ResolveSelection(
                            name,
                            selectionReference);
                        selected[name] = framework;
                    }
                    catch (Exception ex) when (
                        ex is ImplementationSourceException
                            or InstalledInvalidLayoutException)
                    {
                        selected.Remove(name);
                        provisionalFailures[name] = ex;
                        continue;
                    }

                    FrameworkSnapshot snapshot;
                    try
                    {
                        snapshot =
                            await GetFrameworkSnapshotAsync(framework)
                                .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (
                        ex is ImplementationSourceException
                            or InstalledInvalidLayoutException)
                    {
                        provisionalFailures[name] = ex;
                        continue;
                    }

                    bool propagateHighest =
                        proposed[name].RollToHighestVersion;
                    provisionalFailures.Remove(name);
                    foreach (PlatformFrameworkReference parsed
                        in snapshot.RuntimeConfiguration.Frameworks
                            .OrderBy(
                                static reference =>
                                    reference.Name.Value,
                                StringComparer.Ordinal))
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            ObserveReferenceIdentity(
                                declaredVersions,
                                parsed.Name,
                                parsed.Version);
                            EffectiveFrameworkReference parsedReference =
                                EffectiveFrameworkReference.From(parsed);
                            if (propagateHighest)
                            {
                                parsedReference =
                                    parsedReference.WithRollToHighest();
                            }

                            bool changed = MergeInto(
                                proposed,
                                parsed.Name,
                                parsedReference);
                            if (proposed.Count
                                > _request.Work.MaxFrameworks)
                            {
                                throw Incomplete(
                                    "The implementation closure exceeds the framework limit.");
                            }
                            if (changed)
                                pending.Add(parsed.Name);
                        }
                        catch (ImplementationSourceException ex)
                        {
                            graphFailures.Add((parsed.Name, ex));
                        }
                    }
                }

                if (!Equivalent(effective, proposed))
                {
                    effective = proposed;
                    continue;
                }
                if (graphFailures.Count > 0)
                {
                    throw graphFailures
                        .OrderBy(
                            static failure => failure.Name.Value,
                            StringComparer.Ordinal)
                        .First()
                        .Error;
                }
                if (provisionalFailures.Count > 0)
                {
                    throw provisionalFailures
                        .OrderBy(
                            static pair => pair.Key.Value,
                            StringComparer.Ordinal)
                        .First()
                        .Value;
                }

                IReadOnlyList<PlatformFrameworkName> order =
                    await DependencyFirstOrderAsync(selected)
                        .ConfigureAwait(false);
                return await RealizeMembersAsync(selected, order)
                    .ConfigureAwait(false);
            }
        }

        private SelectedFramework ResolveSelection(
            PlatformFrameworkName name,
            EffectiveFrameworkReference reference)
        {
            Step();
            FrameworkInventory inventory = GetInventory(name);
            PlatformVersion? version = SelectVersion(
                inventory.Versions,
                reference);
            if (version is null)
            {
                if (reference.Range
                        == FrameworkCompatibilityRange.Exact
                    && inventory.Versions.Any(
                        candidate => string.Equals(
                            candidate.Value,
                            reference.Version.Value,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InstalledInvalidLayoutException();
                }

                throw Unavailable(
                    "No installed shared-framework version satisfies a manifest reference.");
            }

            return new SelectedFramework(
                name,
                version,
                inventory.VersionDirectories[version.Value]);
        }

        private FrameworkInventory GetInventory(
            PlatformFrameworkName name)
        {
            if (_inventories.TryGetValue(
                    name,
                    out FrameworkInventory? inventory))
            {
                return inventory;
            }

            _cancellationToken.ThrowIfCancellationRequested();
            _dotnetRootEntries ??=
                EnumerateDirectoryEntries(_source._dotnetRoot);
            _sharedRoot ??= FindExactDirectory(
                _dotnetRootEntries,
                "shared")
                ?? throw Unavailable(
                    "The explicit dotnet hive has no shared-framework root.");
            _sharedFrameworkEntries ??=
                EnumerateDirectoryEntries(_sharedRoot);
            string? familyRoot = FindExactDirectory(
                _sharedFrameworkEntries,
                name.Value);
            if (familyRoot is null)
            {
                throw Unavailable(
                    "A required installed shared-framework family is absent.");
            }

            List<string> entries = EnumerateEntriesBounded(
                familyRoot,
                _observation,
                _cancellationToken);
            var versions = new List<PlatformVersion>();
            var directories = new Dictionary<string, string>(
                StringComparer.Ordinal);
            foreach (string entry in entries)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!IsDirectory(entry))
                    continue;

                string versionName = Path.GetFileName(entry);
                if (!PlatformVersion.TryParse(
                        versionName,
                        out PlatformVersion? version))
                {
                    continue;
                }

                versions.Add(version);
                directories.Add(version.Value, entry);
            }

            versions.Sort(PlatformVersion.SemanticPrecedenceComparer);
            inventory = new FrameworkInventory(
                Array.AsReadOnly(versions.ToArray()),
                directories);
            _inventories.Add(name, inventory);
            return inventory;
        }

        private async ValueTask<FrameworkSnapshot>
            GetFrameworkSnapshotAsync(SelectedFramework framework)
        {
            var coordinate = new FrameworkCoordinate(
                framework.Name.Value,
                framework.Version.Value);
            if (_snapshots.TryGetValue(
                    coordinate,
                    out FrameworkSnapshot? snapshot))
            {
                return snapshot;
            }

            string familyName = framework.Name.Value;
            IReadOnlyDictionary<string, FrameworkDirectoryEntry> entries =
                GetFrameworkEntries(framework);
            string? runtimeConfigPath = FindExactFile(
                entries,
                familyName + ".runtimeconfig.json",
                required: false);
            PlatformRuntimeConfiguration runtimeConfiguration;
            InstalledPlatformContentDigest? runtimeConfigurationDigest = null;
            if (runtimeConfigPath is null)
            {
                runtimeConfiguration =
                    PlatformRuntimeConfiguration.DependencyFree;
            }
            else
            {
                FileSnapshot runtimeConfig =
                    await ReadFileAsync(
                            runtimeConfigPath,
                            _source._maxManifestBytes,
                            InstalledPlatformSourceDiagnosticKind
                                .InvalidManifest,
                            "The runtime configuration disappeared during realization.")
                        .ConfigureAwait(false);
                runtimeConfiguration = ParseRuntimeConfiguration(
                    runtimeConfig.Content);
                runtimeConfigurationDigest = runtimeConfig.Digest;
            }

            string dependencyManifestPath = FindExactFile(
                    entries,
                    familyName + ".deps.json",
                    required: true)
                ?? throw new InvalidOperationException(
                    "A required exact file lookup returned no path.");

            FileSnapshot dependencyManifest =
                await ReadFileAsync(
                        dependencyManifestPath,
                        _source._maxManifestBytes,
                        InstalledPlatformSourceDiagnosticKind.InvalidManifest,
                        "The dependency manifest disappeared during realization.")
                    .ConfigureAwait(false);
            PlatformDependencyManifest parsedDependencyManifest =
                ParseDependencyManifest(dependencyManifest.Content);

            snapshot = new FrameworkSnapshot(
                framework,
                runtimeConfiguration,
                runtimeConfigurationDigest,
                parsedDependencyManifest,
                dependencyManifest.Digest);
            _snapshots.Add(coordinate, snapshot);
            return snapshot;
        }

        private IReadOnlyDictionary<string, FrameworkDirectoryEntry>
            GetFrameworkEntries(
            SelectedFramework framework)
        {
            var coordinate = new FrameworkCoordinate(
                framework.Name.Value,
                framework.Version.Value);
            if (_frameworkEntries.TryGetValue(
                    coordinate,
                    out IReadOnlyDictionary<
                        string,
                        FrameworkDirectoryEntry>? entries))
            {
                return entries;
            }

            entries = EnumerateDirectoryEntries(framework.Directory);
            _frameworkEntries.Add(coordinate, entries);
            return entries;
        }

        private IReadOnlyDictionary<string, FrameworkDirectoryEntry>
            EnumerateDirectoryEntries(string directory)
        {
            var entries = new Dictionary<
                string,
                FrameworkDirectoryEntry>(StringComparer.Ordinal);
            foreach (string entry in EnumerateEntriesBounded(
                directory,
                _observation,
                _cancellationToken))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                entries.Add(
                    Path.GetFileName(entry),
                    new FrameworkDirectoryEntry(
                        entry,
                        IsDirectory(entry)));
            }

            return entries;
        }

        private static string? FindExactDirectory(
            IReadOnlyDictionary<string, FrameworkDirectoryEntry> entries,
            string expectedName)
        {
            if (entries.TryGetValue(
                    expectedName,
                    out FrameworkDirectoryEntry? entry))
            {
                if (!entry.IsDirectory)
                    throw new InstalledInvalidLayoutException();
                return entry.Path;
            }

            if (entries.Keys.Any(
                    name => string.Equals(
                        name,
                        expectedName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InstalledInvalidLayoutException();
            }

            return null;
        }

        private static string? FindExactFile(
            IReadOnlyDictionary<string, FrameworkDirectoryEntry> entries,
            string expectedName,
            bool required)
        {
            if (entries.TryGetValue(
                    expectedName,
                    out FrameworkDirectoryEntry? entry))
            {
                if (entry.IsDirectory)
                    throw new InstalledInvalidLayoutException();
                return entry.Path;
            }

            if (entries.Keys.Any(
                    name => string.Equals(
                        name,
                        expectedName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InstalledInvalidLayoutException();
            }

            if (required)
            {
                throw Rejected(
                    InstalledPlatformSourceDiagnosticKind.InvalidManifest,
                    "A selected shared framework has no dependency manifest.");
            }

            return null;
        }

        private PlatformRuntimeConfiguration ParseRuntimeConfiguration(
            ReadOnlyMemory<byte> content)
        {
            PlatformManifestParseOutcome<PlatformRuntimeConfiguration>
                outcome = PlatformRuntimeConfigurationReader.Parse(
                    content,
                    ManifestBudget(),
                    _cancellationToken);
            return outcome switch
            {
                PlatformManifestParseOutcome<
                    PlatformRuntimeConfiguration>.Succeeded succeeded =>
                    succeeded.Value,
                PlatformManifestParseOutcome<
                    PlatformRuntimeConfiguration>.Rejected rejected =>
                    throw Rejected(
                        InstalledPlatformSourceDiagnosticKind.InvalidManifest,
                        rejected.Diagnostic.Summary),
                PlatformManifestParseOutcome<
                    PlatformRuntimeConfiguration>.Incomplete incomplete =>
                    throw Incomplete(incomplete.Diagnostic.Summary),
                _ => throw new InvalidOperationException(
                    "Unknown runtime-configuration parse outcome."),
            };
        }

        private PlatformDependencyManifest ParseDependencyManifest(
            ReadOnlyMemory<byte> content)
        {
            PlatformManifestParseOutcome<PlatformDependencyManifest>
                outcome = PlatformDependencyManifestReader.Parse(
                    content,
                    ManifestBudget(),
                    _cancellationToken);
            return outcome switch
            {
                PlatformManifestParseOutcome<
                    PlatformDependencyManifest>.Succeeded succeeded =>
                    succeeded.Value,
                PlatformManifestParseOutcome<
                    PlatformDependencyManifest>.Rejected rejected =>
                    throw Rejected(
                        rejected.Diagnostic.Kind
                            == PlatformManifestDiagnosticKind
                                .InvalidAssetCoordinate
                            ? InstalledPlatformSourceDiagnosticKind.InvalidMember
                            : InstalledPlatformSourceDiagnosticKind
                                .InvalidManifest,
                        rejected.Diagnostic.Summary),
                PlatformManifestParseOutcome<
                    PlatformDependencyManifest>.Incomplete incomplete =>
                    throw Incomplete(incomplete.Diagnostic.Summary),
                _ => throw new InvalidOperationException(
                    "Unknown dependency-manifest parse outcome."),
            };
        }

        private PlatformManifestParseBudget ManifestBudget() =>
            new(
                Math.Min(
                    _source._maxManifestBytes,
                    checked((int)Math.Min(int.MaxValue, _request.Work.MaxBytes))),
                maxFrameworkReferences: _request.Work.MaxFrameworks,
                maxLibraries: _request.Work.MaxManifestLibraries,
                maxAssets: _request.Work.MaxManifestAssets);

        private async ValueTask<IReadOnlyList<PlatformFrameworkName>>
            DependencyFirstOrderAsync(
                IReadOnlyDictionary<
                    PlatformFrameworkName,
                    SelectedFramework> selected)
        {
            var dependencies = new Dictionary<
                PlatformFrameworkName,
                HashSet<PlatformFrameworkName>>();
            foreach ((PlatformFrameworkName name,
                SelectedFramework framework) in selected)
            {
                FrameworkSnapshot snapshot =
                    await GetFrameworkSnapshotAsync(framework)
                        .ConfigureAwait(false);
                var familyDependencies =
                    new HashSet<PlatformFrameworkName>();
                foreach (PlatformFrameworkReference reference
                    in snapshot.RuntimeConfiguration.Frameworks)
                {
                    if (!selected.ContainsKey(reference.Name))
                    {
                        throw Rejected(
                            InstalledPlatformSourceDiagnosticKind
                                .InvalidFrameworkGraph,
                            "The selected framework graph contains an unresolved dependency.");
                    }
                    familyDependencies.Add(reference.Name);
                }
                dependencies.Add(name, familyDependencies);
            }

            var order = new List<PlatformFrameworkName>(selected.Count);
            var remaining = dependencies.ToDictionary(
                static pair => pair.Key,
                static pair => new HashSet<PlatformFrameworkName>(
                    pair.Value));
            while (remaining.Count > 0)
            {
                PlatformFrameworkName[] ready =
                    remaining
                        .Where(static pair => pair.Value.Count == 0)
                        .Select(static pair => pair.Key)
                        .OrderBy(
                            static name => name.Value,
                            StringComparer.Ordinal)
                        .ToArray();
                if (ready.Length == 0)
                {
                    throw Rejected(
                        InstalledPlatformSourceDiagnosticKind
                            .InvalidFrameworkGraph,
                        "The selected framework graph contains a cycle.");
                }

                foreach (PlatformFrameworkName name in ready)
                {
                    order.Add(name);
                    remaining.Remove(name);
                    foreach (HashSet<PlatformFrameworkName> values
                        in remaining.Values)
                    {
                        values.Remove(name);
                    }
                }
            }

            return Array.AsReadOnly(order.ToArray());
        }

        private async ValueTask<InstalledImplementationRealization>
            RealizeMembersAsync(
                IReadOnlyDictionary<
                    PlatformFrameworkName,
                    SelectedFramework> selected,
                IReadOnlyList<PlatformFrameworkName> order)
        {
            var frameworks =
                new List<InstalledImplementationFramework>(order.Count);
            var libraries = new List<InstalledImplementationLibrary>();
            var identities = new HashSet<AssemblyReferenceIdentity>(
                AssemblyReferenceIdentity.EquivalentComparer);

            foreach (PlatformFrameworkName name in order)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                SelectedFramework selectedFramework = selected[name];
                FrameworkSnapshot snapshot =
                    await GetFrameworkSnapshotAsync(selectedFramework)
                        .ConfigureAwait(false);
                frameworks.Add(
                    new InstalledImplementationFramework(
                        name,
                        selectedFramework.Version,
                        snapshot.RuntimeConfigurationDigest,
                        snapshot.DependencyManifestDigest));

                var projectedCoordinates = new HashSet<string>(
                    StringComparer.Ordinal);
                foreach (PlatformManifestAssetCoordinate asset
                    in snapshot.DependencyManifest.ManagedAssets)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (!projectedCoordinates.Add(asset.FileName))
                    {
                        throw Rejected(
                            InstalledPlatformSourceDiagnosticKind.InvalidMember,
                            "Two manifest assets project to one installed member coordinate.");
                    }
                }
            }

            foreach (PlatformFrameworkName name in order)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                SelectedFramework selectedFramework = selected[name];
                FrameworkSnapshot snapshot =
                    await GetFrameworkSnapshotAsync(selectedFramework)
                        .ConfigureAwait(false);
                foreach (PlatformManifestAssetCoordinate asset
                    in snapshot.DependencyManifest.ManagedAssets)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (libraries.Count == _request.Work.MaxAssemblies)
                    {
                        throw Incomplete(
                            "The implementation closure exceeds the assembly limit.");
                    }

                    IReadOnlyDictionary<
                        string,
                        FrameworkDirectoryEntry> entries =
                        GetFrameworkEntries(selectedFramework);
                    string? memberPath = FindExactFile(
                        entries,
                        asset.FileName,
                        required: false);
                    if (memberPath is null)
                    {
                        throw Rejected(
                            InstalledPlatformSourceDiagnosticKind.InvalidMember,
                            "A manifest-declared implementation member is absent.");
                    }

                    InstalledImplementationLibrary library =
                        await ReadAssemblyAsync(
                                selectedFramework,
                                asset,
                                memberPath)
                            .ConfigureAwait(false);
                    if (!identities.Add(library.Identity))
                    {
                        throw Rejected(
                            InstalledPlatformSourceDiagnosticKind
                                .DuplicateAssemblyIdentity,
                            "The implementation closure contains duplicate assembly identities.");
                    }

                    libraries.Add(library);
                }
            }

            libraries.Sort(InstalledImplementationLibraryComparer.Instance);
            _cancellationToken.ThrowIfCancellationRequested();
            return new InstalledImplementationRealization(
                _generation,
                _request.Coordinate,
                Array.AsReadOnly(frameworks.ToArray()),
                Array.AsReadOnly(libraries.ToArray()));
        }

        private async ValueTask<InstalledImplementationLibrary>
            ReadAssemblyAsync(
                SelectedFramework framework,
                PlatformManifestAssetCoordinate asset,
                string path)
        {
            FileSnapshot snapshot = await ReadFileAsync(
                    path,
                    _source._maxFileBytes,
                    InstalledPlatformSourceDiagnosticKind.InvalidMember,
                    "A manifest-declared implementation member disappeared during realization.")
                .ConfigureAwait(false);
            try
            {
                using var peReader = new PEReader(
                    snapshot.Content.ToImmutableArray());
                if (!peReader.HasMetadata)
                {
                    throw Rejected(
                        InstalledPlatformSourceDiagnosticKind.InvalidMember,
                        "A manifest-declared implementation member has no ECMA-335 metadata.");
                }

                MetadataReader metadata = peReader.GetMetadataReader();
                if (metadata.MetadataKind != MetadataKind.Ecma335
                    || !metadata.IsAssembly)
                {
                    throw Rejected(
                        InstalledPlatformSourceDiagnosticKind.InvalidMember,
                        "A manifest-declared implementation member is not a supported ECMA-335 assembly.");
                }

                AssemblyReferenceIdentity identity =
                    AssemblyReferenceIdentity.FromAssemblyDefinition(metadata);
                return new InstalledImplementationLibrary(
                    framework.Name,
                    framework.Version,
                    asset,
                    identity,
                    snapshot.Digest,
                    snapshot.Content);
            }
            catch (BadImageFormatException)
            {
                throw Rejected(
                    InstalledPlatformSourceDiagnosticKind.InvalidMember,
                    "A manifest-declared implementation member contains malformed metadata.");
            }
        }

        private async ValueTask<FileSnapshot> ReadFileAsync(
            string path,
            long maximumFileBytes,
            InstalledPlatformSourceDiagnosticKind missingKind,
            string missingSummary)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                long allowedBytes = Math.Min(
                    maximumFileBytes,
                    _remainingBytes);
                if (stream.Length > allowedBytes
                    || stream.Length > int.MaxValue)
                {
                    throw Incomplete(
                        "An installed implementation file exceeds the byte limit.");
                }

                byte[] content = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(
                        content,
                        _cancellationToken)
                    .ConfigureAwait(false);
                _remainingBytes -= content.LongLength;
                return new FileSnapshot(
                    content,
                    InstalledPlatformContentDigest.FromBytes(content));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is FileNotFoundException
                    or DirectoryNotFoundException)
            {
                throw Rejected(missingKind, missingSummary);
            }
            catch (OverflowException)
            {
                throw Incomplete(
                    "An installed implementation file exceeds the supported snapshot size.");
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException)
            {
                throw Failed(
                    "An installed implementation file could not be read.");
            }
        }

        private void Step()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_resolutionSteps
                == _request.Work.MaxResolutionSteps)
            {
                throw Incomplete(
                    "Implementation framework resolution exceeds the work limit.");
            }
            _resolutionSteps++;
        }

        private static bool MergeInto(
            IDictionary<
                PlatformFrameworkName,
                EffectiveFrameworkReference> references,
            PlatformFrameworkName name,
            EffectiveFrameworkReference incoming)
        {
            if (!references.TryGetValue(
                    name,
                    out EffectiveFrameworkReference? current))
            {
                references.Add(name, incoming);
                return true;
            }

            EffectiveFrameworkReference merged = Reconcile(
                current,
                incoming);
            if (merged == current)
                return false;
            references[name] = merged;
            return true;
        }

        private static void ObserveReferenceIdentity(
            IDictionary<
                PlatformFrameworkName,
                List<PlatformVersion>> declaredVersions,
            PlatformFrameworkName name,
            PlatformVersion version)
        {
            if (!declaredVersions.TryGetValue(
                    name,
                    out List<PlatformVersion>? versions))
            {
                declaredVersions.Add(name, [version]);
                return;
            }

            foreach (PlatformVersion existing in versions)
            {
                if (PlatformVersion.SemanticPrecedenceComparer.Compare(
                        existing,
                        version) == 0
                    && existing != version)
                {
                    throw Rejected(
                        InstalledPlatformSourceDiagnosticKind
                            .InvalidFrameworkGraph,
                        "Framework references with equal precedence have ambiguous exact version identities.");
                }
            }

            versions.Add(version);
        }

        private static EffectiveFrameworkReference Reconcile(
            EffectiveFrameworkReference left,
            EffectiveFrameworkReference right)
        {
            int comparison =
                PlatformVersion.SemanticPrecedenceComparer.Compare(
                    left.Version,
                    right.Version);
            if (comparison == 0
                && left.Version != right.Version)
            {
                throw Rejected(
                    InstalledPlatformSourceDiagnosticKind
                        .InvalidFrameworkGraph,
                    "Framework references with equal precedence have ambiguous exact version identities.");
            }

            EffectiveFrameworkReference lower =
                comparison <= 0 ? left : right;
            EffectiveFrameworkReference higher =
                comparison <= 0 ? right : left;
            if (!IsCompatibleWith(lower, higher.Version))
            {
                throw Rejected(
                    InstalledPlatformSourceDiagnosticKind
                        .InvalidFrameworkGraph,
                    "Framework references have incompatible version requirements.");
            }

            return new EffectiveFrameworkReference(
                higher.Version,
                (FrameworkCompatibilityRange)Math.Min(
                    (int)left.Range,
                    (int)right.Range),
                left.RollToHighestVersion
                    || right.RollToHighestVersion,
                left.ApplyPatches && right.ApplyPatches,
                left.PreferRelease || right.PreferRelease);
        }

        private static PlatformVersion? SelectVersion(
            IReadOnlyList<PlatformVersion> versions,
            EffectiveFrameworkReference reference)
        {
            if (reference.Range == FrameworkCompatibilityRange.Exact
                || reference.Range == FrameworkCompatibilityRange.Patch
                    && !reference.ApplyPatches
                    && !reference.Version.IsPrerelease)
            {
                return versions.FirstOrDefault(
                    version => version == reference.Version);
            }

            PlatformVersion? selected = reference.PreferRelease
                ? SelectVersionCore(
                    versions,
                    reference,
                    releaseOnly: true)
                : null;
            return selected
                ?? SelectVersionCore(
                    versions,
                    reference,
                    releaseOnly: false);
        }

        private static PlatformVersion? SelectVersionCore(
            IReadOnlyList<PlatformVersion> versions,
            EffectiveFrameworkReference reference,
            bool releaseOnly)
        {
            PlatformVersion? best = null;
            bool ambiguous = false;
            bool highest =
                reference.Range != FrameworkCompatibilityRange.Patch
                && reference.RollToHighestVersion;
            foreach (PlatformVersion version in versions)
            {
                if (releaseOnly && version.IsPrerelease
                    || !IsCompatibleWith(reference, version))
                {
                    continue;
                }

                ConsiderCandidate(
                    ref best,
                    ref ambiguous,
                    version,
                    highest);
            }

            if (best is null
                || best.IsPrerelease
                || reference.Range < FrameworkCompatibilityRange.Patch)
            {
                return RequireUniqueWinner(best, ambiguous);
            }

            PlatformVersion patchBase = best;
            best = null;
            ambiguous = false;
            foreach (PlatformVersion version in versions)
            {
                if (releaseOnly && version.IsPrerelease
                    || version.Major != patchBase.Major
                    || version.Minor != patchBase.Minor
                    || !reference.ApplyPatches
                        && version.Patch != patchBase.Patch
                    || PlatformVersion.SemanticPrecedenceComparer.Compare(
                        version,
                        patchBase) < 0)
                {
                    continue;
                }

                ConsiderCandidate(
                    ref best,
                    ref ambiguous,
                    version,
                    highest: true);
            }

            return RequireUniqueWinner(best, ambiguous);
        }

        private static void ConsiderCandidate(
            ref PlatformVersion? current,
            ref bool ambiguous,
            PlatformVersion candidate,
            bool highest)
        {
            if (current is null)
            {
                current = candidate;
                ambiguous = false;
                return;
            }

            int comparison =
                PlatformVersion.SemanticPrecedenceComparer.Compare(
                    current,
                    candidate);
            if (comparison == 0 && current != candidate)
            {
                ambiguous = true;
                return;
            }

            if (highest ? comparison < 0 : comparison > 0)
            {
                current = candidate;
                ambiguous = false;
            }
        }

        private static PlatformVersion? RequireUniqueWinner(
            PlatformVersion? winner,
            bool ambiguous)
        {
            if (ambiguous)
            {
                throw Rejected(
                    InstalledPlatformSourceDiagnosticKind
                        .InvalidFrameworkGraph,
                    "Installed framework versions with equal winning precedence are ambiguous.");
            }

            return winner;
        }

        private static bool IsCompatibleWith(
            EffectiveFrameworkReference reference,
            PlatformVersion candidate)
        {
            int comparison =
                PlatformVersion.SemanticPrecedenceComparer.Compare(
                    reference.Version,
                    candidate);
            if (comparison > 0)
                return false;
            if (comparison == 0)
                return reference.Range != FrameworkCompatibilityRange.Exact
                    || reference.Version == candidate;
            if (reference.Range == FrameworkCompatibilityRange.Exact)
                return false;
            if (reference.Version.Major != candidate.Major
                && reference.Range < FrameworkCompatibilityRange.Major)
            {
                return false;
            }
            if (reference.Version.Minor != candidate.Minor
                && reference.Range < FrameworkCompatibilityRange.Minor)
            {
                return false;
            }
            if (reference.Version.Patch != candidate.Patch
                && reference.Range == FrameworkCompatibilityRange.Patch
                && !reference.ApplyPatches)
            {
                return false;
            }

            return true;
        }

        private static bool Equivalent(
            IReadOnlyDictionary<
                PlatformFrameworkName,
                EffectiveFrameworkReference> left,
            IReadOnlyDictionary<
                PlatformFrameworkName,
                EffectiveFrameworkReference> right) =>
            left.Count == right.Count
            && left.All(
                pair => right.TryGetValue(
                        pair.Key,
                        out EffectiveFrameworkReference? value)
                    && value == pair.Value);

        private static ImplementationSourceException Rejected(
            InstalledPlatformSourceDiagnosticKind kind,
            string summary) =>
            new(
                ImplementationSourceOutcome.Rejected,
                kind,
                summary);

        private static ImplementationSourceException Incomplete(
            string summary) =>
            new(
                ImplementationSourceOutcome.Incomplete,
                InstalledPlatformSourceDiagnosticKind.WorkLimitExceeded,
                summary);

        private static ImplementationSourceException Unavailable(
            string summary) =>
            new(
                ImplementationSourceOutcome.Unavailable,
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
                summary);

        private static ImplementationSourceException Failed(
            string summary) =>
            new(
                ImplementationSourceOutcome.Failed,
                InstalledPlatformSourceDiagnosticKind.IoFailure,
                summary);

        private sealed record FrameworkInventory(
            IReadOnlyList<PlatformVersion> Versions,
            IReadOnlyDictionary<string, string> VersionDirectories);

        private sealed record SelectedFramework(
            PlatformFrameworkName Name,
            PlatformVersion Version,
            string Directory);

        private readonly record struct FrameworkCoordinate(
            string Name,
            string Version);

        private sealed record FrameworkSnapshot(
            SelectedFramework Framework,
            PlatformRuntimeConfiguration RuntimeConfiguration,
            InstalledPlatformContentDigest? RuntimeConfigurationDigest,
            PlatformDependencyManifest DependencyManifest,
            InstalledPlatformContentDigest DependencyManifestDigest);

        private sealed record FileSnapshot(
            byte[] Content,
            InstalledPlatformContentDigest Digest);

        private sealed record FrameworkDirectoryEntry(
            string Path,
            bool IsDirectory);

        private sealed record EffectiveFrameworkReference(
            PlatformVersion Version,
            FrameworkCompatibilityRange Range,
            bool RollToHighestVersion,
            bool ApplyPatches,
            bool PreferRelease)
        {
            internal static EffectiveFrameworkReference Exact(
                PlatformVersion version) =>
                new(
                    version,
                    FrameworkCompatibilityRange.Exact,
                    RollToHighestVersion: false,
                    ApplyPatches: false,
                    PreferRelease: !version.IsPrerelease);

            internal static EffectiveFrameworkReference From(
                PlatformFrameworkReference reference)
            {
                FrameworkCompatibilityRange range =
                    reference.RollForward switch
                    {
                        PlatformFrameworkRollForward.Disable =>
                            FrameworkCompatibilityRange.Exact,
                        PlatformFrameworkRollForward.LatestPatch =>
                            FrameworkCompatibilityRange.Patch,
                        PlatformFrameworkRollForward.Minor
                            or PlatformFrameworkRollForward.LatestMinor =>
                            FrameworkCompatibilityRange.Minor,
                        PlatformFrameworkRollForward.Major
                            or PlatformFrameworkRollForward.LatestMajor =>
                            FrameworkCompatibilityRange.Major,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(reference)),
                    };
                bool highest = reference.RollForward
                    is PlatformFrameworkRollForward.LatestMinor
                    or PlatformFrameworkRollForward.LatestMajor;
                return new EffectiveFrameworkReference(
                    reference.Version,
                    range,
                    highest,
                    reference.ApplyPatches,
                    PreferRelease: !reference.Version.IsPrerelease);
            }

            internal EffectiveFrameworkReference WithRollToHighest() =>
                this with { RollToHighestVersion = true };
        }
    }

    private static PlatformFrameworkName FrameworkName(
        InstalledPlatformFamily family) =>
        family switch
        {
            InstalledPlatformFamily.DotNetRuntime =>
                DotNetRuntimeName,
            InstalledPlatformFamily.AspNetCore =>
                AspNetCoreName,
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };

    private sealed class InstalledImplementationLibraryComparer :
        IComparer<InstalledImplementationLibrary>
    {
        internal static InstalledImplementationLibraryComparer Instance
        {
            get;
        } = new();

        public int Compare(
            InstalledImplementationLibrary? left,
            InstalledImplementationLibrary? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;

            int result = StringComparer.OrdinalIgnoreCase.Compare(
                left.Identity.Name,
                right.Identity.Name);
            if (result != 0)
                return result;
            result = Comparer<Version?>.Default.Compare(
                left.Identity.Version,
                right.Identity.Version);
            if (result != 0)
                return result;
            result = StringComparer.OrdinalIgnoreCase.Compare(
                NormalizeCulture(left.Identity.Culture),
                NormalizeCulture(right.Identity.Culture));
            if (result != 0)
                return result;
            result = StringComparer.OrdinalIgnoreCase.Compare(
                left.Identity.PublicKeyToken ?? "",
                right.Identity.PublicKeyToken ?? "");
            if (result != 0)
                return result;
            result = string.CompareOrdinal(
                left.FrameworkName.Value,
                right.FrameworkName.Value);
            if (result != 0)
                return result;
            result = string.CompareOrdinal(
                left.FrameworkVersion.Value,
                right.FrameworkVersion.Value);
            return result != 0
                ? result
                : string.CompareOrdinal(
                    left.ManifestCoordinate.Value,
                    right.ManifestCoordinate.Value);
        }

        private static string NormalizeCulture(string? culture) =>
            string.IsNullOrEmpty(culture)
                || culture.Equals(
                    "neutral",
                    StringComparison.OrdinalIgnoreCase)
                    ? ""
                    : culture;
    }

    private sealed class ImplementationSourceException : Exception
    {
        internal ImplementationSourceException(
            ImplementationSourceOutcome outcome,
            InstalledPlatformSourceDiagnosticKind kind,
            string message)
            : base(message)
        {
            Outcome = outcome;
            Kind = kind;
        }

        internal ImplementationSourceOutcome Outcome { get; }
        internal InstalledPlatformSourceDiagnosticKind Kind { get; }
    }

    private enum FrameworkCompatibilityRange
    {
        Exact,
        Patch,
        Minor,
        Major,
    }

    private enum ImplementationSourceOutcome
    {
        Unavailable,
        Rejected,
        Incomplete,
        Failed,
    }
}
