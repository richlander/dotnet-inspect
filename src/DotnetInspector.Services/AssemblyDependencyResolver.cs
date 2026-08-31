using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
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
public sealed partial class AssemblyDependencyResolver :
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
        AssemblyDescriptorKey,
        Lazy<AssemblyDescriptorResolution>> _descriptors =
            [];
    readonly ConcurrentDictionary<
        string,
        Lazy<SnapshotImageResolution>> _snapshotImages =
            new(StringComparer.Ordinal);
    IReadOnlyList<ResolvedAssemblyDependency>? _resolved;
    IReadOnlyList<ResolvedAssemblyDependency>? _allCandidates;
    readonly ConcurrentDictionary<
        AssemblyBindingRequestKey,
        Lazy<AssemblyBindingSelection>> _bindingSelections = [];
    readonly Lazy<PlatformFrameworkSnapshot> _installedPlatformFrameworkSnapshot =
        new(
            PlatformResolver.GetInstalledFrameworkSnapshot,
            LazyThreadSafetyMode.ExecutionAndPublication);
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
        if (ResolveDesignatedOverlay(
                candidates,
                identity,
                scope)
            is { } overlayAttempt)
        {
            return overlayAttempt;
        }

        CandidateOpenFailureKind? candidateFailure = null;
        CandidateTier? activeTier = null;

        foreach (var dependency in candidates)
        {
            if (!Path.GetFileNameWithoutExtension(dependency.Path).Equals(identity.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (scope == AssemblyResolutionScope.Platform
                && dependency.Provenance is not (AssemblyDependencyProvenance.TrustedPlatformAssembly or AssemblyDependencyProvenance.SharedFramework or AssemblyDependencyProvenance.CorpusAssembly))
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
                        candidateFailure,
                        MissDisposition:
                            AssemblyBindingMissDisposition.NameOwnedNoMatch);
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
                candidateFailure,
                MissDisposition:
                    AssemblyBindingMissDisposition.NameOwnedNoMatch);
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
        bool probeInstalledPlatform =
            useInstalledPlatformFallback
            && PlatformResolver.IsPlatformCandidate(identity.Name);
        bool installedPlatformOwnsName = false;
        if (probeInstalledPlatform)
        {
            var (path, framework, _, _) =
                _options.PreferImplementationAssemblies
                    ? PlatformResolver.ResolveAssembly(
                        identity.Name,
                        useRuntimeAssemblies: true)
                    : PlatformResolver.ResolveAssemblyFromSnapshot(
                        identity.Name,
                        _installedPlatformFrameworkSnapshot.Value);
            installedPlatformOwnsName = path is not null;
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
            candidateFailure,
            MissDisposition: activeTier is not null
                || installedPlatformOwnsName
                    ? AssemblyBindingMissDisposition.NameOwnedNoMatch
                    : AssemblyBindingMissDisposition.NoNameOwner);
    }

    AssemblyResolutionAttempt? ResolveDesignatedOverlay(
        IReadOnlyList<ResolvedAssemblyDependency> candidates,
        AssemblyReferenceIdentity identity,
        AssemblyResolutionScope scope)
    {
        static bool PathNameMatches(
            ResolvedAssemblyDependency dependency,
            AssemblyReferenceIdentity identity) =>
            Path.GetFileNameWithoutExtension(dependency.Path).Equals(
                identity.Name,
                StringComparison.OrdinalIgnoreCase);

        ResolvedAssemblyDependency? nameOwner = candidates.FirstOrDefault(
            dependency =>
                PathNameMatches(dependency, identity)
                && (scope != AssemblyResolutionScope.Platform
                    || IsEntitled(dependency.Provenance)));
        if ((nameOwner is not null
                && !IsEntitled(nameOwner.Provenance))
            || !candidates.Any(dependency =>
                dependency.Provenance
                    is AssemblyDependencyProvenance.CorpusAssembly))
        {
            return null;
        }
        var entitled = new List<ResolvedAssemblyReference>();
        CandidateOpenFailureKind? budgetFailure = null;
        foreach (ResolvedAssemblyDependency dependency in candidates)
        {
            bool designated =
                dependency.Provenance
                    is AssemblyDependencyProvenance.CorpusAssembly;
            if (!designated
                && (!IsEntitled(dependency.Provenance)
                    || !PathNameMatches(dependency, identity)))
            {
                continue;
            }

            AssemblyDescriptorResolution descriptor = DescriptorResult(
                dependency.Path,
                ResolutionProvenance(dependency));
            if (descriptor.Assembly is { } assembly)
            {
                entitled.Add(assembly);
            }
            else if (descriptor.FailureKind
                    is CandidateOpenFailureKind.ResourceBudget
                && (designated
                    || PathNameMatches(dependency, identity)))
            {
                budgetFailure =
                    CandidateOpenFailureKind.ResourceBudget;
            }
        }

        bool allowPlatformVersionRollForward =
            scope == AssemblyResolutionScope.Platform
            && _options.AllowPlatformAssemblyVersionRollForward;
        AssemblyBindingSelection? selection =
            DesignatedAssemblyBindingPrecedence.TrySelect(
                identity,
                entitled,
                allowPlatformVersionRollForward,
                _options.IgnoreAssemblyVersion);
        if (budgetFailure is not null)
        {
            return new AssemblyResolutionAttempt(
                Assembly: null,
                budgetFailure);
        }
        if (selection is null)
            return null;

        bool useInstalledPlatformFallback =
            scope == AssemblyResolutionScope.Platform
            || scope == AssemblyResolutionScope.Any
                && _options.IncludeInstalledPlatformFallback
                && nameOwner is null;
        bool hasEligiblePlatform = entitled.Any(candidate =>
            candidate.Provenance
                is AssemblyResolutionProvenance.PlatformAsset
            && identity.MatchesCandidate(
                candidate.Identity,
                allowPlatformVersionRollForward,
                _options.IgnoreAssemblyVersion));
        if (useInstalledPlatformFallback
            && !hasEligiblePlatform
            && InstalledPlatformDescriptor(identity)
                is { } installedPlatform)
        {
            if (installedPlatform.Assembly is { } assembly
                && identity.MatchesCandidate(
                    assembly.Identity,
                    _options.AllowPlatformAssemblyVersionRollForward,
                    _options.IgnoreAssemblyVersion)
                && selection
                    is AssemblyBindingSelection.Selected selected)
            {
                selection = AssemblyBindingSelection.Found(
                    selected.Assembly,
                    selected.ShadowedAssemblies.Add(assembly));
            }
        }

        return selection switch
        {
            AssemblyBindingSelection.Selected selected =>
                new AssemblyResolutionAttempt(
                    selected.Assembly,
                    CandidateFailure: null,
                    selected.ShadowedAssemblies),
            AssemblyBindingSelection.Ambiguous ambiguous =>
                new AssemblyResolutionAttempt(
                    Assembly: null,
                    CandidateFailure: null,
                    AmbiguousAssemblies: ambiguous.Assemblies),
            _ => null,
        };
    }

    AssemblyDescriptorResolution? InstalledPlatformDescriptor(
        AssemblyReferenceIdentity identity)
    {
        if (!PlatformResolver.IsPlatformCandidate(identity.Name))
            return null;

        var (path, framework, _, _) = PlatformResolver.ResolveAssembly(
            identity.Name,
            useRuntimeAssemblies: _options.PreferImplementationAssemblies);
        return path is null
            ? null
            : DescriptorResult(
                path,
                AssemblyResolutionProvenance.Platform(
                    framework ?? "InstalledPlatform",
                    frameworkVersion: null,
                    AssemblyDependencyProvenance
                        .InstalledPlatformAssembly
                        .ToString()));
    }

    static bool IsEntitled(
        AssemblyDependencyProvenance provenance) =>
        provenance is
            AssemblyDependencyProvenance.TrustedPlatformAssembly
            or AssemblyDependencyProvenance.SharedFramework
            or AssemblyDependencyProvenance.CorpusAssembly;

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
        if (!attempt.AmbiguousAssemblies.IsDefaultOrEmpty)
            return AssemblyBindingSelection.Multiple(
                attempt.AmbiguousAssemblies);
        if (attempt.Assembly is { } assembly)
        {
            return AssemblyBindingSelection.Found(
                assembly,
                attempt.ShadowedAssemblies);
        }
        return attempt.CandidateFailure is { } candidateFailure
            ? AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable,
                    candidateFailure))
            : attempt.MissDisposition switch
            {
                null or AssemblyBindingMissDisposition.Undifferentiated =>
                    AssemblyBindingSelection.NotFound(),
                AssemblyBindingMissDisposition.NoNameOwner =>
                    AssemblyBindingSelection.NameNotOwned(),
                AssemblyBindingMissDisposition.NameOwnedNoMatch =>
                    AssemblyBindingSelection.NameOwnedButNoMatch(),
                _ => throw new InvalidOperationException(
                    "Unknown assembly-binding miss disposition."),
            };
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
            // Designation is the honest label: the caller named this exact
            // file, which entitles it to core-library identity, but it says
            // nothing about where the file came from. Reporting Platform would
            // claim a coherent closure — a hive or pack — for what may be one
            // loose file, and that claim is consumed beyond trust: it selects
            // symbol-server PDB acquisition and is printed as ResolvedFrom.
            AssemblyResolutionProvenance.Designated(
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
            new AssemblyDescriptorKey(path, provenance),
            static (key, resolver) =>
                new Lazy<AssemblyDescriptorResolution>(
                    () => resolver.CreateDescriptor(
                        key.Path,
                        key.Provenance),
                    LazyThreadSafetyMode.ExecutionAndPublication),
            this).Value;

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

        SnapshotImageResolution snapshot =
            _snapshotImages.GetOrAdd(
                path,
                static (path, resolver) =>
                    new Lazy<SnapshotImageResolution>(
                        () => resolver.CreateSnapshotImage(path),
                        LazyThreadSafetyMode.ExecutionAndPublication),
                this).Value;
        if (snapshot.Image is null
            || snapshot.Identity is null)
        {
            return new(
                Assembly: null,
                snapshot.FailureKind
                ?? CandidateOpenFailureKind.Unreadable);
        }

        byte[] image = snapshot.Image;
        return new(
            ResolvedAssemblyReference.Create(
                snapshot.Identity,
                Path.GetFullPath(path),
                () => new MemoryStream(image, writable: false),
                provenance),
            FailureKind: null);
    }

    SnapshotImageResolution CreateSnapshotImage(string path)
    {
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
                    Identity: null,
                    Image: null,
                    CandidateOpenFailureKind.InvalidImage);
            }

            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader.GetMetadataReader());
            reservedBytes = 0;
            return new(identity, image, FailureKind: null);
        }
        catch (AssemblyDependencySnapshotBudgetExceededException)
        {
            return new(
                Identity: null,
                Image: null,
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
                Identity: null,
                Image: null,
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
        CandidateOpenFailureKind? CandidateFailure,
        ImmutableArray<ResolvedAssemblyReference> ShadowedAssemblies = default,
        ImmutableArray<ResolvedAssemblyReference> AmbiguousAssemblies = default,
        AssemblyBindingMissDisposition? MissDisposition = null);

    readonly record struct AssemblyDescriptorKey(
        string Path,
        AssemblyResolutionProvenance Provenance);

    sealed record AssemblyDescriptorResolution(
        ResolvedAssemblyReference? Assembly,
        CandidateOpenFailureKind? FailureKind);

    sealed record SnapshotImageResolution(
        AssemblyReferenceIdentity? Identity,
        byte[]? Image,
        CandidateOpenFailureKind? FailureKind);

}
