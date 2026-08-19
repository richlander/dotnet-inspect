using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using System.Reflection.Metadata;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Services;

public enum AssemblyDependencyProvenance
{
    PackageDependency,
    TrustedPlatformAssembly,
    SharedFramework,
    SiblingAssembly,
    DepsJsonAsset,
    ProjectAsset,
    CorpusAssembly,
    InstalledPlatformAssembly,
}

public sealed record ResolvedAssemblyDependency(
    string Path,
    AssemblyDependencyProvenance Provenance,
    string? PackageId = null,
    string? PackageVersion = null,
    string? FrameworkName = null);

public sealed record AssemblyDependencyResolutionOptions(string TargetAssemblyPath)
{
    public IReadOnlyList<string>? PackageRoots { get; init; }
    public IReadOnlyList<string>? CorpusAssemblyPaths { get; init; }
    public string? ProjectAssetsPath { get; init; }
    public string? TargetFramework { get; init; }
    public bool IncludeTrustedPlatformAssemblies { get; init; } = true;
    public bool IncludeAspNetCoreSharedFramework { get; init; } = true;
    public bool IncludeSiblingAssemblies { get; init; } = true;
    public bool IncludeDepsJsonAssets { get; init; } = true;
    /// <summary>
    /// Allows Any-scope resolution to use an installed platform assembly only
    /// when no enabled candidate tier owns the requested simple name.
    /// </summary>
    public bool IncludeInstalledPlatformFallback { get; init; }
    public bool PreferImplementationAssemblies { get; init; }
    public bool AllowPlatformAssemblyVersionRollForward { get; init; }
    /// <summary>
    /// Treats the metadata version as descriptive while retaining the resolver's
    /// existing name, culture, and public-key-token matching. Omitted culture
    /// and token values retain their existing wildcard behavior.
    /// </summary>
    public bool IgnoreAssemblyVersion { get; init; }
    public bool ExcludeTargetAssembly { get; init; }
    /// <summary>
    /// Retains the bytes acquired for each descriptor so later opens observe
    /// the same image even if its source path changes.
    /// </summary>
    public bool SnapshotAssemblyImages { get; init; }
    public long MaxSnapshotImageBytes { get; init; } =
        AssemblyImageSnapshot.DefaultMaxRetainedImageBytes;
}

public sealed class AssemblyDependencySnapshotBudgetExceededException(
    long maxSnapshotImageBytes) : InvalidOperationException(
        $"The assembly dependency snapshot budget of "
        + $"{maxSnapshotImageBytes} bytes was exhausted.")
{
    public long MaxSnapshotImageBytes { get; } =
        maxSnapshotImageBytes;
}

/// <summary>
/// Product-side assembly dependency resolver. It owns rich input probing
/// (packages, project assets, deps.json, shared frameworks, sibling/corpus
/// assemblies) and exposes only paths/descriptors plus the metadata identity
/// callback needed by Metadata/Decompiler/Research.
/// </summary>
public sealed class AssemblyDependencyResolver :
    IAssemblyReferenceResolver,
    IAssemblyBindingPolicy
{
    enum CandidateTier
    {
        Sibling,
        Package,
        TrustedPlatform,
        SharedFramework,
        DepsJson,
        ProjectAssets,
        Corpus,
        InstalledPlatform,
    }

    readonly AssemblyDependencyResolutionOptions _options;
    readonly ConcurrentDictionary<
        string,
        Lazy<AssemblyDescriptorResolution>> _descriptors =
            new(StringComparer.Ordinal);
    IReadOnlyList<ResolvedAssemblyDependency>? _resolved;
    IReadOnlyList<ResolvedAssemblyDependency>? _allCandidates;
    readonly ConcurrentDictionary<
        AssemblyBindingRequestKey,
        Lazy<AssemblyBindingSelection>> _bindingSelections = [];
    readonly object _snapshotBudgetLock = new();
    long _snapshotImageBytes;

    public AssemblyDependencyResolver(AssemblyDependencyResolutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(
            options.MaxSnapshotImageBytes);
        _options = options;
    }

    /// <summary>
    /// The resolution options this resolver was constructed with. Exposed for tests that pin how
    /// callers forward inputs (e.g. project-assets/TFM) into the resolver.
    /// </summary>
    internal AssemblyDependencyResolutionOptions Options => _options;

    public AssemblyBindingPolicyVersion Version { get; } = new();

    public AssemblyBindingSelection Select(
        AssemblyBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = AssemblyBindingRequestKey.From(request);
        return _bindingSelections.GetOrAdd(
            key,
            _ => new Lazy<AssemblyBindingSelection>(
                () => SelectCore(request),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public IReadOnlyList<ResolvedAssemblyDependency> ResolveAll()
    {
        if (_resolved is not null)
            return _resolved;

        _resolved = CollectDependencies(deduplicate: true);
        return _resolved;
    }

    /// <summary>
    /// Acquires the structured descriptor for an entry returned by
    /// <see cref="ResolveAll"/>.
    /// </summary>
    public ResolvedAssemblyReference? Acquire(
        ResolvedAssemblyDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        return Descriptor(
            dependency.Path,
            ResolutionProvenance(dependency));
    }

    /// <summary>
    /// Acquires the target assembly in this resolver's acquisition generation.
    /// The target remains excluded from <see cref="ResolveAll"/> when requested.
    /// </summary>
    public ResolvedAssemblyReference? AcquireTargetAssembly() =>
        Descriptor(
            Path.GetFullPath(_options.TargetAssemblyPath),
            AssemblyResolutionProvenance.Local("target assembly"));

    IReadOnlyList<ResolvedAssemblyDependency> CollectDependencies(bool deduplicate)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<ResolvedAssemblyDependency>();
        string targetPath = Path.GetFullPath(_options.TargetAssemblyPath);
        string targetName = Path.GetFileNameWithoutExtension(targetPath);
        var targetDirectory = Path.GetDirectoryName(targetPath);

        void Add(string path, AssemblyDependencyProvenance provenance, string? packageId = null, string? packageVersion = null, string? frameworkName = null)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                return;

            string simpleName = Path.GetFileNameWithoutExtension(path);
            if (_options.ExcludeTargetAssembly && simpleName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                return;
            if (deduplicate && !seen.Add(simpleName))
                return;

            resolved.Add(new ResolvedAssemblyDependency(
                Path.GetFullPath(path),
                provenance,
                packageId,
                packageVersion,
                frameworkName));
        }

        if (targetDirectory is not null && Directory.Exists(targetDirectory) && _options.IncludeSiblingAssemblies)
            foreach (var path in Directory.EnumerateFiles(targetDirectory, "*.dll"))
                Add(path, AssemblyDependencyProvenance.SiblingAssembly);

        foreach (var path in PackageDependencyReferencePaths(
            targetPath,
            _options.PackageRoots,
            preferImplementationAssemblies: _options.PreferImplementationAssemblies))
        {
            var package = TryReadPackageIdentity(path, _options.PackageRoots);
            Add(path, AssemblyDependencyProvenance.PackageDependency, package.Id, package.Version);
        }

        if (_options.IncludeTrustedPlatformAssemblies)
        {
            foreach (var path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                Add(path, AssemblyDependencyProvenance.TrustedPlatformAssembly, frameworkName: "TRUSTED_PLATFORM_ASSEMBLIES");
        }

        if (_options.IncludeAspNetCoreSharedFramework)
            AddSharedFrameworkReferences("Microsoft.AspNetCore.App", path => Add(path, AssemblyDependencyProvenance.SharedFramework, frameworkName: "Microsoft.AspNetCore.App"));

        if (targetDirectory is not null && Directory.Exists(targetDirectory))
        {
            if (_options.IncludeDepsJsonAssets)
                AddDepsJsonReferences(targetDirectory, targetName, path =>
                {
                    var package = TryReadPackageIdentity(path, _options.PackageRoots);
                    Add(path, AssemblyDependencyProvenance.DepsJsonAsset, package.Id, package.Version);
                });
        }

        if (_options.ProjectAssetsPath is { Length: > 0 } assetsPath && File.Exists(assetsPath))
        {
            foreach (var (path, packageName, version) in ProjectAssetsParser.Parse(assetsPath, _options.TargetFramework, log: null))
                Add(path, AssemblyDependencyProvenance.ProjectAsset, packageName, version);
        }

        if (_options.CorpusAssemblyPaths is not null)
            foreach (var path in _options.CorpusAssemblyPaths)
                Add(path, AssemblyDependencyProvenance.CorpusAssembly);

        return resolved;
    }

    public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        => ResolveCore(identity, scope).Assembly;

    AssemblyResolutionAttempt ResolveCore(
        AssemblyReferenceIdentity identity,
        AssemblyResolutionScope scope)
    {
        var candidates =
            _allCandidates ??= CollectDependencies(deduplicate: false);
        CandidateOpenFailureKind? candidateFailure = null;
        CandidateTier? activeTier = null;

        foreach (var dependency in candidates)
        {
            if (!Path.GetFileNameWithoutExtension(dependency.Path).Equals(identity.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (scope == AssemblyResolutionScope.Platform
                && dependency.Provenance is not (AssemblyDependencyProvenance.TrustedPlatformAssembly or AssemblyDependencyProvenance.SharedFramework))
                continue;

            CandidateTier tier = TierFor(dependency.Provenance);
            if (activeTier is { } previousTier
                && tier != previousTier)
            {
                if (candidateFailure is not null
                    || scope != AssemblyResolutionScope.Platform)
                {
                    return new AssemblyResolutionAttempt(
                        Assembly: null,
                        candidateFailure);
                }
            }
            activeTier = tier;

            bool allowVersionRollForward = scope == AssemblyResolutionScope.Platform
                && _options.AllowPlatformAssemblyVersionRollForward;
            AssemblyDescriptorResolution descriptor = DescriptorResult(
                dependency.Path,
                ResolutionProvenance(dependency));
            ResolvedAssemblyReference? selected = descriptor.Assembly;
            if (selected is null)
            {
                candidateFailure ??=
                    descriptor.FailureKind
                    ?? CandidateOpenFailureKind.Unreadable;
                continue;
            }
            if (!identity.MatchesCandidate(
                    selected.Identity,
                    allowVersionRollForward,
                    _options.IgnoreAssemblyVersion))
            {
                continue;
            }

            return new AssemblyResolutionAttempt(
                selected,
                CandidateFailure: null);
        }

        if (candidateFailure is not null)
        {
            return new AssemblyResolutionAttempt(
                Assembly: null,
                candidateFailure);
        }

        // The target may reference an older platform contract than the running
        // inspector, and TPA contains only assemblies in the tool's own closure.
        // Resolve the remaining platform name from installed packs/runtimes.
        // Callers may opt into one-way platform roll-forward or version-insensitive
        // descriptive selection while retaining culture and public-key-token checks.
        bool useInstalledPlatformFallback =
            scope == AssemblyResolutionScope.Platform
            || scope == AssemblyResolutionScope.Any
                && _options.IncludeInstalledPlatformFallback
                && activeTier is null;
        if (useInstalledPlatformFallback
            && PlatformResolver.IsPlatformCandidate(identity.Name))
        {
            var (path, framework, _, _) = PlatformResolver.ResolveAssembly(
                identity.Name,
                useRuntimeAssemblies: _options.PreferImplementationAssemblies);
            if (path is not null)
            {
                AssemblyDescriptorResolution descriptor = DescriptorResult(
                    path,
                    AssemblyResolutionProvenance.Platform(
                        framework ?? "InstalledPlatform",
                        frameworkVersion: null,
                        AssemblyDependencyProvenance.InstalledPlatformAssembly.ToString()));
                ResolvedAssemblyReference? selected = descriptor.Assembly;
                if (selected is null)
                {
                    candidateFailure ??=
                        descriptor.FailureKind
                        ?? CandidateOpenFailureKind.Unreadable;
                }
                else if (identity.MatchesCandidate(
                        selected.Identity,
                        _options.AllowPlatformAssemblyVersionRollForward,
                        _options.IgnoreAssemblyVersion))
                {
                    return new AssemblyResolutionAttempt(
                        selected,
                        CandidateFailure: null);
                }
            }
        }

        return new AssemblyResolutionAttempt(
            Assembly: null,
            candidateFailure);
    }

    static CandidateTier TierFor(
        AssemblyDependencyProvenance provenance) =>
        provenance switch
        {
            AssemblyDependencyProvenance.SiblingAssembly =>
                CandidateTier.Sibling,
            AssemblyDependencyProvenance.PackageDependency =>
                CandidateTier.Package,
            AssemblyDependencyProvenance.TrustedPlatformAssembly =>
                CandidateTier.TrustedPlatform,
            AssemblyDependencyProvenance.SharedFramework =>
                CandidateTier.SharedFramework,
            AssemblyDependencyProvenance.DepsJsonAsset =>
                CandidateTier.DepsJson,
            AssemblyDependencyProvenance.ProjectAsset =>
                CandidateTier.ProjectAssets,
            AssemblyDependencyProvenance.CorpusAssembly =>
                CandidateTier.Corpus,
            AssemblyDependencyProvenance.InstalledPlatformAssembly =>
                CandidateTier.InstalledPlatform,
            _ => throw new ArgumentOutOfRangeException(
                nameof(provenance),
                provenance,
                "Unknown assembly dependency provenance."),
        };

    AssemblyBindingSelection SelectCore(
        AssemblyBindingRequest request)
    {
        try
        {
            return request.Target switch
            {
                AssemblyBindingTarget.AssemblyReference reference =>
                    SelectReference(reference.Identity, request.Scope),
                AssemblyBindingTarget.IntrinsicCoreLibrary =>
                    SelectIntrinsicCoreLibrary(request.Scope),
                _ => AssemblyBindingSelection.Invalid(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.InvalidPolicyResult)),
            };
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or OverflowException
                or InvalidOperationException
                or NotSupportedException
                or ArgumentException
                or System.Security.SecurityException)
        {
            return AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable,
                    ClassifyCandidateOpenFailure(ex)));
        }
    }

    AssemblyBindingSelection SelectReference(
        AssemblyReferenceIdentity identity,
        AssemblyResolutionScope scope)
    {
        AssemblyResolutionAttempt attempt = ResolveCore(identity, scope);
        if (attempt.Assembly is { } assembly)
            return AssemblyBindingSelection.Found(assembly);
        return attempt.CandidateFailure is { } candidateFailure
            ? AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable,
                    candidateFailure))
            : AssemblyBindingSelection.NotFound();
    }

    AssemblyBindingSelection SelectIntrinsicCoreLibrary(
        AssemblyResolutionScope scope)
    {
        string targetPath = Path.GetFullPath(
            _options.TargetAssemblyPath);
        AssemblyDescriptorResolution target = DescriptorResult(
            targetPath,
            // Only returned as a binding when this file IS the core library
            // facade, and it is the caller's designated target either way.
            // Reporting it as a local asset understated the acquisition, which
            // matters now that core-library identity is granted on acquisition.
            AssemblyResolutionProvenance.Platform(
                "Platform",
                frameworkVersion: null,
                "intrinsic core library"));
        return target.Assembly is null
            ? AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable,
                    target.FailureKind))
            : IntrinsicCoreLibraryBinding.Select(
                target.Assembly,
                facade => SelectReference(facade, scope));
    }

    static AssemblyResolutionProvenance ResolutionProvenance(
        ResolvedAssemblyDependency dependency) =>
        dependency.Provenance switch
        {
            AssemblyDependencyProvenance.PackageDependency
                or AssemblyDependencyProvenance.DepsJsonAsset
                or AssemblyDependencyProvenance.ProjectAsset
                when dependency.PackageId is { Length: > 0 } packageId
                    && dependency.PackageVersion is { Length: > 0 } packageVersion =>
                AssemblyResolutionProvenance.Package(
                    packageId,
                    packageVersion,
                    tfm: null,
                    rid: null),
            AssemblyDependencyProvenance.TrustedPlatformAssembly
                or AssemblyDependencyProvenance.SharedFramework =>
                AssemblyResolutionProvenance.Platform(
                    dependency.FrameworkName ?? "Platform",
                    frameworkVersion: null,
                    dependency.Provenance.ToString()),
            // Corpus paths are enumerated by the caller, not discovered beside
            // the target, so they carry the caller's designation.
            AssemblyDependencyProvenance.CorpusAssembly =>
                AssemblyResolutionProvenance.Designated(
                    dependency.Provenance.ToString()),
            _ => AssemblyResolutionProvenance.Local(
                dependency.Provenance.ToString()),
        };

    ResolvedAssemblyReference? Descriptor(
        string path,
        AssemblyResolutionProvenance provenance)
    {
        AssemblyDescriptorResolution result =
            DescriptorResult(path, provenance);
        if (result.FailureKind
            is CandidateOpenFailureKind.ResourceBudget)
        {
            throw new AssemblyDependencySnapshotBudgetExceededException(
                _options.MaxSnapshotImageBytes);
        }

        return result.Assembly;
    }

    AssemblyDescriptorResolution DescriptorResult(
        string path,
        AssemblyResolutionProvenance provenance) =>
        _descriptors.GetOrAdd(
            path,
            (path, provenance) =>
                new Lazy<AssemblyDescriptorResolution>(
                    () => CreateDescriptor(path, provenance),
                    LazyThreadSafetyMode.ExecutionAndPublication),
            provenance).Value;

    AssemblyDescriptorResolution CreateDescriptor(
        string path,
        AssemblyResolutionProvenance provenance)
    {
        if (!_options.SnapshotAssemblyImages)
        {
            bool created = ResolvedAssemblyReference.TryCreateFromPath(
                path,
                provenance,
                out ResolvedAssemblyReference? reference,
                out Exception? failure);
            return created
                ? new(reference, FailureKind: null)
                : new(
                    Assembly: null,
                    ClassifyCandidateOpenFailure(
                        failure
                        ?? new BadImageFormatException()));
        }

        long reservedBytes = 0;
        try
        {
            using var source = File.OpenRead(path);
            long length = source.Length;
            if (length > int.MaxValue
                || !TryReserveSnapshotBytes(length))
            {
                throw new AssemblyDependencySnapshotBudgetExceededException(
                    _options.MaxSnapshotImageBytes);
            }
            reservedBytes = length;

            byte[] image =
                GC.AllocateUninitializedArray<byte>((int)length);
            source.ReadExactly(image);

            using var stream = new MemoryStream(image, writable: false);
            using var reader =
                new System.Reflection.PortableExecutable.PEReader(stream);
            if (!reader.HasMetadata)
            {
                return new(
                    Assembly: null,
                    CandidateOpenFailureKind.InvalidImage);
            }

            ResolvedAssemblyReference result =
                ResolvedAssemblyReference.Create(
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader.GetMetadataReader()),
                Path.GetFullPath(path),
                () => new MemoryStream(image, writable: false),
                provenance);
            reservedBytes = 0;
            return new(result, FailureKind: null);
        }
        catch (AssemblyDependencySnapshotBudgetExceededException)
        {
            return new(
                Assembly: null,
                CandidateOpenFailureKind.ResourceBudget);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ObjectDisposedException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return new(
                Assembly: null,
                ClassifyCandidateOpenFailure(ex));
        }
        finally
        {
            if (reservedBytes != 0)
                ReleaseSnapshotBytes(reservedBytes);
        }
    }

    bool TryReserveSnapshotBytes(long bytes)
    {
        lock (_snapshotBudgetLock)
        {
            if (bytes
                > _options.MaxSnapshotImageBytes - _snapshotImageBytes)
            {
                return false;
            }

            _snapshotImageBytes += bytes;
            return true;
        }
    }

    void ReleaseSnapshotBytes(long bytes)
    {
        lock (_snapshotBudgetLock)
            _snapshotImageBytes -= bytes;
    }

    static CandidateOpenFailureKind ClassifyCandidateOpenFailure(
        Exception exception) =>
        exception switch
        {
            BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException =>
                CandidateOpenFailureKind.InvalidImage,
            AssemblyDependencySnapshotBudgetExceededException =>
                CandidateOpenFailureKind.ResourceBudget,
            _ => CandidateOpenFailureKind.Unreadable,
        };

    readonly record struct AssemblyBindingRequestKey(
        AssemblyBindingTarget Target,
        AssemblyAcquisitionRegistration? Origin,
        bool GlobalOrigin,
        AssemblyResolutionScope Scope)
    {
        internal static AssemblyBindingRequestKey From(
            AssemblyBindingRequest request) =>
            request.Origin switch
            {
                AssemblyBindingOrigin.GlobalOrigin =>
                    new(request.Target, null, true, request.Scope),
                AssemblyBindingOrigin.RequestingAssembly requesting =>
                    new(
                        request.Target,
                        requesting.Registration,
                        false,
                        request.Scope),
                _ => throw new InvalidOperationException(
                    "Unknown assembly-binding origin."),
            };
    }

    readonly record struct AssemblyResolutionAttempt(
        ResolvedAssemblyReference? Assembly,
        CandidateOpenFailureKind? CandidateFailure);

    sealed record AssemblyDescriptorResolution(
        ResolvedAssemblyReference? Assembly,
        CandidateOpenFailureKind? FailureKind);

    public static IReadOnlyList<string> PackageDependencyReferencePaths(string targetPath)
        => PackageDependencyReferencePaths(targetPath, packageRoots: null);

    public static IReadOnlyList<string> PackageDependencyReferencePaths(string targetPath, IReadOnlyList<string>? packageRoots)
        => PackageDependencyReferencePaths(targetPath, packageRoots, preferImplementationAssemblies: false);

    public static IReadOnlyList<string> PackageDependencyReferencePaths(
        string targetPath,
        IReadOnlyList<string>? packageRoots,
        bool preferImplementationAssemblies)
    {
        if (NuGetPackageContext(targetPath, packageRoots) is not { } context)
            return [];

        var nuspec = Directory.EnumerateFiles(context.PackageDirectory, "*.nuspec").FirstOrDefault();
        if (nuspec is null)
            return [];

        var packageDirectories = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var dependencyGraph = new Dictionary<string, List<PackageDependency>>(StringComparer.OrdinalIgnoreCase);
        List<PackageDependency> rootDependencies;
        try
        {
            rootDependencies = CollectPackageDependencies(
                context.PackageDirectory,
                context.TargetFramework,
                packageRoots,
                packageDirectories,
                dependencyGraph,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return [];
        }

        var selectedVersions = packageDirectories.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Keys.OrderByDescending(version => version, PackageVersionComparer.Instance).First(),
            StringComparer.OrdinalIgnoreCase);
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in rootDependencies)
            AddResolvedPackage(dependency.Id);

        var references = new List<string>();
        foreach (string id in resolved.Order(StringComparer.OrdinalIgnoreCase))
            foreach (var path in ProbeNuGetPackageVersionDlls(
                packageDirectories[id][selectedVersions[id]],
                context.TargetFramework,
                preferImplementationAssemblies))
                references.Add(path);
        return references;

        void AddResolvedPackage(string id)
        {
            if (!selectedVersions.TryGetValue(id, out string? version) || !resolved.Add(id))
                return;
            if (!dependencyGraph.TryGetValue(PackageKey(id, version), out var dependencies))
                return;
            foreach (var dependency in dependencies)
                AddResolvedPackage(dependency.Id);
        }
    }

    static List<PackageDependency> CollectPackageDependencies(
        string packageDirectory,
        string targetFramework,
        IReadOnlyList<string>? packageRoots,
        Dictionary<string, Dictionary<string, string>> packageDirectories,
        Dictionary<string, List<PackageDependency>> dependencyGraph,
        HashSet<string> visiting)
    {
        var nuspec = Directory.EnumerateFiles(packageDirectory, "*.nuspec").FirstOrDefault();
        if (nuspec is null)
            return [];

        var document = XDocument.Load(nuspec);
        var dependencies = new List<PackageDependency>();
        foreach (var dependency in SelectNuGetDependencies(document, targetFramework))
        {
            string? id = dependency.Attribute("id")?.Value;
            string? version = DependencyExactVersion(dependency.Attribute("version")?.Value);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
                continue;

            foreach (var root in NuGetPackageRoots(packageRoots))
            {
                var dependencyDirectory = Path.Combine(root, id.ToLowerInvariant(), version.ToLowerInvariant());
                if (!Directory.Exists(dependencyDirectory))
                    continue;

                if (!packageDirectories.TryGetValue(id, out var versions))
                    packageDirectories[id] = versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                versions[version] = dependencyDirectory;
                dependencies.Add(new PackageDependency { Id = id, Version = version });

                string visitKey = $"{id}/{version}";
                if (!dependencyGraph.ContainsKey(PackageKey(id, version)) && visiting.Add(visitKey))
                {
                    dependencyGraph[PackageKey(id, version)] = CollectPackageDependencies(
                        dependencyDirectory,
                        targetFramework,
                        packageRoots,
                        packageDirectories,
                        dependencyGraph,
                        visiting);
                    visiting.Remove(visitKey);
                }
                break;
            }
        }
        return dependencies;
    }

    sealed class PackageVersionComparer : IComparer<string>
    {
        public static readonly PackageVersionComparer Instance = new();

        public int Compare(string? left, string? right)
        {
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return 0;
            if (NuGetVersion.TryParse(left, out var leftVersion) && NuGetVersion.TryParse(right, out var rightVersion))
                return leftVersion.CompareTo(rightVersion);
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    static string PackageKey(string id, string version) => $"{id}/{version}";

    static NuGetReferenceContext? NuGetPackageContext(string targetPath, IReadOnlyList<string>? packageRoots = null)
    {
        string fullPath = Path.GetFullPath(targetPath);
        foreach (var root in NuGetPackageRoots(packageRoots))
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = fullRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = fullPath[prefix.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length < 5
                || (!parts[2].Equals("lib", StringComparison.OrdinalIgnoreCase)
                    && !parts[2].Equals("ref", StringComparison.OrdinalIgnoreCase)))
                return null;

            return new(
                Path.Combine(fullRoot, parts[0], parts[1]),
                parts[3],
                parts[0],
                parts[1]);
        }

        return null;
    }

    static IEnumerable<string> NuGetPackageRoots(IReadOnlyList<string>? packageRoots = null)
    {
        if (packageRoots is not null)
        {
            foreach (var root in packageRoots)
                yield return root;
            yield break;
        }

        if (Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } envRoot)
            yield return envRoot;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            yield return Path.Combine(home, ".nuget", "packages");
    }

    static IEnumerable<XElement> SelectNuGetDependencies(XDocument document, string? tfm)
    {
        var dependencies = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "dependencies");
        if (dependencies is null)
            yield break;

        var directDependencies = dependencies.Elements().Where(e => e.Name.LocalName == "dependency").ToArray();
        var groups = dependencies.Elements().Where(e => e.Name.LocalName == "group").ToArray();
        string? normalizedTfm = NormalizeNuGetFramework(tfm);
        var selectedGroup = normalizedTfm is null
            ? null
            : groups
                .Select(group => new
                {
                    Group = group,
                    Score = NuGetFrameworkCompatibilityScore(normalizedTfm, NormalizeNuGetFramework(group.Attribute("targetFramework")?.Value)),
                })
                .Where(candidate => candidate.Score is not null)
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Group)
                .FirstOrDefault();

        if (selectedGroup is not null)
        {
            foreach (var dependency in selectedGroup.Elements().Where(e => e.Name.LocalName == "dependency"))
                yield return dependency;
            yield break;
        }

        foreach (var dependency in directDependencies)
            yield return dependency;
    }

    static string? NormalizeNuGetFramework(string? tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm))
            return null;

        tfm = tfm.Trim().ToLowerInvariant();
        if (tfm.StartsWith(".netstandard", StringComparison.Ordinal))
            return "netstandard" + tfm[".netstandard".Length..];
        if (tfm.StartsWith(".netcoreapp", StringComparison.Ordinal))
            return "netcoreapp" + tfm[".netcoreapp".Length..];
        if (tfm.StartsWith(".netframework", StringComparison.Ordinal))
            return "net" + tfm[".netframework".Length..].Replace(".", "", StringComparison.Ordinal);
        return tfm;
    }

    static int? NuGetFrameworkCompatibilityScore(string target, string? candidate)
    {
        if (candidate is null)
            return null;
        if (string.Equals(target, candidate, StringComparison.OrdinalIgnoreCase))
            return 100_000;
        if (!TryParseNuGetFramework(target, out var targetFramework) ||
            !TryParseNuGetFramework(candidate, out var candidateFramework))
            return null;

        int versionScore = candidateFramework.Major * 100 + candidateFramework.Minor;
        return targetFramework.Family switch
        {
            "net" when candidateFramework.Family == "net" && candidateFramework.VersionScore <= targetFramework.VersionScore
                => 90_000 + versionScore,
            "net" when candidateFramework.Family == "netcoreapp" && candidateFramework.VersionScore <= targetFramework.VersionScore
                => 80_000 + versionScore,
            "net" when candidateFramework.Family == "netstandard" && candidateFramework.VersionScore <= 201
                => 70_000 + versionScore,
            "netcoreapp" when candidateFramework.Family == "netcoreapp" && candidateFramework.VersionScore <= targetFramework.VersionScore
                => 90_000 + versionScore,
            "netcoreapp" when candidateFramework.Family == "netstandard" && candidateFramework.VersionScore <= 201
                => 80_000 + versionScore,
            "netstandard" when candidateFramework.Family == "netstandard" && candidateFramework.VersionScore <= targetFramework.VersionScore
                => 90_000 + versionScore,
            _ => null,
        };
    }

    static bool TryParseNuGetFramework(string tfm, out NuGetFramework framework)
    {
        framework = default;
        if (tfm.StartsWith("netstandard", StringComparison.Ordinal))
            return TryParseVersionedFramework("netstandard", tfm["netstandard".Length..], out framework);
        if (tfm.StartsWith("netcoreapp", StringComparison.Ordinal))
            return TryParseVersionedFramework("netcoreapp", tfm["netcoreapp".Length..], out framework);
        if (tfm.StartsWith("net", StringComparison.Ordinal) && tfm[3..].Contains('.', StringComparison.Ordinal))
            return TryParseVersionedFramework("net", tfm["net".Length..], out framework);
        return false;
    }

    static bool TryParseVersionedFramework(string family, string version, out NuGetFramework framework)
    {
        framework = default;
        var parts = version.Split('.', 3);
        if (parts.Length == 0 || !int.TryParse(parts[0], out int major))
            return false;
        int minor = 0;
        if (parts.Length >= 2 && !int.TryParse(parts[1], out minor))
            return false;
        framework = new(family, major, minor);
        return true;
    }

    // Delimiters that mark a NuGet version range rather than a single version;
    // cached to avoid allocating the delimiter array on every call.
    static readonly SearchValues<char> s_versionRangeDelimiters = SearchValues.Create("[](),");

    static string? DependencyExactVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        version = version.Trim();
        if (version.Length >= 5 && version[0] == '[' && version[^1] == ']')
        {
            string inner = version[1..^1].Trim();
            var range = inner.Split(',', 2);
            if (range.Length == 1)
                return inner;
            if (range.Length == 2
                && string.Equals(range[0].Trim(), range[1].Trim(), StringComparison.OrdinalIgnoreCase))
                return range[0].Trim();
        }

        return version.AsSpan().ContainsAny(s_versionRangeDelimiters) ? null : version;
    }

    static IEnumerable<string> ProbeNuGetPackageVersionDlls(string packageDir, string? tfm, bool preferImplementationAssemblies)
    {
        if (!Directory.Exists(packageDir))
            yield break;

        var assetKinds = preferImplementationAssemblies
            ? (string[])["lib", "ref"]
            : (string[])["ref", "lib"];

        foreach (string assetKind in assetKinds)
        {
            if (tfm is not null && AssetDirectory(packageDir, assetKind, tfm) is { } exactAssetDir)
            {
                foreach (var path in Directory.EnumerateFiles(exactAssetDir, "*.dll"))
                    yield return path;
                yield break;
            }
        }

        foreach (string assetKind in assetKinds)
        {
            if (tfm is not null && CompatibleAssetDirectory(packageDir, assetKind, tfm) is { } compatibleAssetDir)
            {
                foreach (var path in Directory.EnumerateFiles(compatibleAssetDir, "*.dll"))
                    yield return path;
                yield break;
            }
        }
    }

    static string? AssetDirectory(string packageDir, string assetKind, string tfm)
    {
        string assetDir = Path.Combine(packageDir, assetKind, tfm);
        return Directory.Exists(assetDir) ? assetDir : null;
    }

    static string? CompatibleAssetDirectory(string packageDir, string assetKind, string targetTfm)
    {
        string assetRoot = Path.Combine(packageDir, assetKind);
        if (!Directory.Exists(assetRoot))
            return null;

        string? normalizedTarget = NormalizeNuGetFramework(targetTfm);
        if (normalizedTarget is null)
            return null;

        return Directory.EnumerateDirectories(assetRoot)
            .Select(dir => new
            {
                Directory = dir,
                Score = NuGetFrameworkCompatibilityScore(normalizedTarget, NormalizeNuGetFramework(Path.GetFileName(dir))),
            })
            .Where(candidate => candidate.Score is not null)
            .OrderByDescending(candidate => candidate.Score)
            .Select(candidate => candidate.Directory)
            .FirstOrDefault();
    }

    static void AddSharedFrameworkReferences(string frameworkName, Action<string> add)
    {
        string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string runtimeVersion = Path.GetFileName(runtimeDirectory);
        string? runtimeFrameworkDirectory = Path.GetDirectoryName(runtimeDirectory);
        if (string.IsNullOrEmpty(runtimeVersion)
            || runtimeFrameworkDirectory is null
            || !Path.GetFileName(runtimeFrameworkDirectory).Equals("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase))
            return;

        string? sharedRoot = Path.GetDirectoryName(runtimeFrameworkDirectory);
        if (sharedRoot is null)
            return;

        string frameworkRoot = Path.Combine(sharedRoot, frameworkName);
        if (!Directory.Exists(frameworkRoot))
            return;

        string? selected = SelectSharedFrameworkDirectory(frameworkRoot, runtimeVersion);
        if (selected is null)
            return;

        foreach (var path in Directory.EnumerateFiles(selected, "*.dll"))
            add(path);
    }

    public static string? SelectSharedFrameworkDirectory(string frameworkRoot, string runtimeVersion)
    {
        string exactDirectory = Path.Combine(frameworkRoot, runtimeVersion);
        if (Directory.Exists(exactDirectory))
            return exactDirectory;
        if (!System.Version.TryParse(
            VersionCore(runtimeVersion),
            out var runtime))
            return null;

        return Directory.EnumerateDirectories(frameworkRoot)
            .Select(directory => new
            {
                Directory = directory,
                Version = System.Version.TryParse(
                    VersionCore(Path.GetFileName(directory)),
                    out var version)
                        ? version
                        : null,
            })
            .Where(candidate => candidate.Version is not null
                && candidate.Version.Major == runtime.Major
                && candidate.Version.Minor == runtime.Minor)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.Directory)
            .FirstOrDefault();
    }

    static string VersionCore(string version) => version.Split('-', 2)[0];

    static void AddDepsJsonReferences(string targetDirectory, string targetName, Action<string> addReference)
    {
        var depsPath = Path.Combine(targetDirectory, $"{targetName}.deps.json");
        if (!File.Exists(depsPath))
            return;

        try
        {
            using var doc = HardenedJson.Parse(File.ReadAllText(depsPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("targets", out var targets) ||
                targets.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("libraries", out var libraries) ||
                libraries.ValueKind != JsonValueKind.Object)
                return;

            var libraryPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var library in libraries.EnumerateObject())
            {
                if (library.Value.ValueKind == JsonValueKind.Object &&
                    library.Value.TryGetProperty("path", out var pathElement) &&
                    pathElement.ValueKind == JsonValueKind.String &&
                    pathElement.GetString() is { Length: > 0 } path)
                    libraryPaths[library.Name] = path;
            }

            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var library in target.Value.EnumerateObject())
                {
                    AddAssetGroup(targetDirectory, libraryPaths, library, "compile", addReference);
                    AddAssetGroup(targetDirectory, libraryPaths, library, "runtime", addReference);
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
    }

    static void AddAssetGroup(
        string targetDirectory,
        IReadOnlyDictionary<string, string> libraryPaths,
        JsonProperty library,
        string groupName,
        Action<string> addReference)
    {
        if (!library.Value.TryGetProperty(groupName, out var assets))
            return;
        if (assets.ValueKind != JsonValueKind.Object)
            return;

        foreach (var asset in assets.EnumerateObject())
        {
            if (asset.Name == "_._")
                continue;

            if (asset.Value.ValueKind == JsonValueKind.Object &&
                asset.Value.TryGetProperty("localPath", out var localPathElement) &&
                localPathElement.ValueKind == JsonValueKind.String &&
                localPathElement.GetString() is { Length: > 0 } localPath &&
                StorePath.TryResolveUnderRoot(
                    targetDirectory,
                    localPath,
                    out string? resolvedLocalPath))
            {
                addReference(resolvedLocalPath);
            }

            if (libraryPaths.TryGetValue(library.Name, out var packagePath)
                && StorePath.TryResolveUnderRoot(
                    GlobalPackagesRoot(),
                    packagePath,
                    out string? packageDirectory)
                && StorePath.TryResolveUnderRoot(
                    packageDirectory,
                    asset.Name,
                    out string? resolvedAssetPath))
            {
                addReference(resolvedAssetPath);
            }
        }
    }

    static string GlobalPackagesRoot()
    {
        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(packagesRoot))
            return packagesRoot;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
    }

    static (string? Id, string? Version) TryReadPackageIdentity(string path, IReadOnlyList<string>? packageRoots)
    {
        if (NuGetPackageContext(path, packageRoots) is { } context)
            return (context.PackageId, context.PackageVersion);
        return (null, null);
    }

    readonly record struct NuGetFramework(string Family, int Major, int Minor)
    {
        public int VersionScore => Major * 100 + Minor;
    }

    sealed record NuGetReferenceContext(
        string PackageDirectory,
        string TargetFramework,
        string PackageId,
        string PackageVersion);
}
