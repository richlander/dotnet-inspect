using System.Collections.Immutable;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Platforms.Packages;

/// <summary>One source attempt, independent of package-content generation.</summary>
public sealed class PackagePlatformSourceGeneration
{
    private static long s_next;

    internal PackagePlatformSourceGeneration() =>
        Name = $"package-platform-{Interlocked.Increment(ref s_next)}";

    public string Name { get; }
}

/// <summary>An exact Platform target's derived reference distribution coordinate.</summary>
public sealed record PackageReferencePackCoordinate
{
    public PackageReferencePackCoordinate(PlatformFamilyTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
    }

    public PlatformFamilyTarget Target { get; }
    public string PackageId => ReferencePackageId(Target.Family);

    internal static string ReferencePackageId(PlatformFamily family) => family switch
    {
        PlatformFamily.DotNetRuntime => "microsoft.netcore.app.ref",
        PlatformFamily.AspNetCore => "microsoft.aspnetcore.app.ref",
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };
}

public sealed record PackageReferenceDiscoveryRequest
{
    public PackageReferenceDiscoveryRequest(
        PlatformFamily family, PlatformTargetFramework targetFramework, int maxCandidates)
    {
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));
        ArgumentNullException.ThrowIfNull(targetFramework);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCandidates);
        Family = family;
        TargetFramework = targetFramework;
        MaxCandidates = maxCandidates;
    }

    public PlatformFamily Family { get; }
    public PlatformTargetFramework TargetFramework { get; }
    public int MaxCandidates { get; }
}

public abstract class PackageReferencePopulationDemand
{
    private protected PackageReferencePopulationDemand() { }

    public sealed class Assembly : PackageReferencePopulationDemand
    {
        public Assembly(AssemblyReferenceIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            Identity = identity;
        }

        public AssemblyReferenceIdentity Identity { get; }
    }

    public sealed class CompletePopulation : PackageReferencePopulationDemand;
}

public sealed record PackageReferenceWorkBudget
{
    public PackageReferenceWorkBudget(int maxAssemblies, long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxAssemblies);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        MaxAssemblies = maxAssemblies;
        MaxBytes = maxBytes;
    }

    public int MaxAssemblies { get; }
    public long MaxBytes { get; }
}

/// <summary>Source maximums, intersected with each request's work allowance.</summary>
public sealed record PackagePlatformSourceLimits
{
    public PackagePlatformSourceLimits(
        int maxCandidates = 1024,
        int maxObservedEntries = 32768,
        int maxAssemblies = 4096,
        long maxEntryBytes = 64L * 1024 * 1024,
        long maxBytes = 512L * 1024 * 1024,
        int maxFrameworks = 16,
        int maxResolutionSteps = 256,
        int maxManifestLibraries = 4096,
        int maxManifestAssets = 8192,
        int maxManifestBytes = 4 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCandidates);
        ArgumentOutOfRangeException.ThrowIfNegative(maxObservedEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxFrameworks);
        ArgumentOutOfRangeException.ThrowIfNegative(maxResolutionSteps);
        ArgumentOutOfRangeException.ThrowIfNegative(maxManifestLibraries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxManifestAssets);
        ArgumentOutOfRangeException.ThrowIfNegative(maxManifestBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxAssemblies);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntryBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxEntryBytes, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        MaxCandidates = maxCandidates;
        MaxObservedEntries = maxObservedEntries;
        MaxFrameworks = maxFrameworks;
        MaxResolutionSteps = maxResolutionSteps;
        MaxManifestLibraries = maxManifestLibraries;
        MaxManifestAssets = maxManifestAssets;
        MaxManifestBytes = maxManifestBytes;
        MaxAssemblies = maxAssemblies;
        MaxEntryBytes = maxEntryBytes;
        MaxBytes = maxBytes;
    }

    public int MaxCandidates { get; }
    public int MaxObservedEntries { get; }
    public int MaxFrameworks { get; }
    public int MaxResolutionSteps { get; }
    public int MaxManifestLibraries { get; }
    public int MaxManifestAssets { get; }
    public int MaxManifestBytes { get; }
    public int MaxAssemblies { get; }
    public long MaxEntryBytes { get; }
    public long MaxBytes { get; }
}

/// <summary>Source-issued target/candidate association; not a selection policy.</summary>
public sealed class PackagePlatformTargetSelection
{
    internal PackagePlatformTargetSelection(
        object source, PackagePlatformSourceGeneration generation,
        PackageReferencePackCoordinate coordinate, PackageAcquisitionCandidate candidate)
    {
        Source = source;
        Generation = generation;
        Coordinate = coordinate;
        Candidate = candidate;
    }

    internal object Source { get; }
    public PackagePlatformSourceGeneration Generation { get; }
    public PackageReferencePackCoordinate Coordinate { get; }
    public PlatformFamilyTarget Target => Coordinate.Target;
    public PackageAcquisitionCandidate Candidate { get; }
}

public sealed class PackagePlatformTargetInventory
{
    internal PackagePlatformTargetInventory(
        PackagePlatformSourceGeneration generation,
        ImmutableArray<PackagePlatformTargetSelection> targets)
    {
        Generation = generation;
        Targets = targets;
    }

    public PackagePlatformSourceGeneration Generation { get; }
    public ImmutableArray<PackagePlatformTargetSelection> Targets { get; }

    public PackagePlatformTargetSelection SelectTarget(PlatformFamilyTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        foreach (PackagePlatformTargetSelection selection in Targets)
            if (selection.Target == target)
                return selection;
        throw new ArgumentException("The target is not in this source inventory.", nameof(target));
    }
}

/// <summary>
/// Private immutable compiled-XML bytes, readable after Package Source
/// settlement.
/// </summary>
public sealed class PackageReferenceDocumentation
{
    private readonly byte[] _content;

    internal PackageReferenceDocumentation(
        string path,
        byte[] content)
    {
        Path = path;
        _content = content;
    }

    public string Path { get; }
    public long ContentLength => _content.LongLength;
    public Stream OpenRead() =>
        new MemoryStream(_content, writable: false);
}

/// <summary>Private immutable bytes, readable after Package Source settlement.</summary>
public sealed class PackageReferenceLibrary
{
    private readonly byte[] _content;

    internal PackageReferenceLibrary(
        string path,
        AssemblyReferenceIdentity identity,
        byte[] content,
        PackageReferenceDocumentation? documentation = null)
    {
        Path = path;
        Identity = identity;
        _content = content;
        Documentation = documentation;
    }

    public string Path { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public long ContentLength => _content.LongLength;
    public PackageReferenceDocumentation? Documentation { get; }
    public long TotalContentLength =>
        ContentLength + (Documentation?.ContentLength ?? 0);
    public Stream OpenRead() => new MemoryStream(_content, writable: false);
}

public sealed class PackageReferenceRealization
{
    internal PackageReferenceRealization(
        PackagePlatformSourceGeneration generation,
        PackageReferencePackCoordinate coordinate,
        PackageReferencePopulationDemand population,
        PackageAcquisitionCandidate candidate,
        ConfiguredPackageAuthority authority,
        PackageSourceResultIdentity source,
        PackageContentGenerationIdentity contentGeneration,
        PackagePayloadOrigin origin,
        ImmutableArray<PackageAuthorityFailure> packageFailures,
        ImmutableArray<PackageReferenceLibrary> libraries)
    {
        Generation = generation;
        Coordinate = coordinate;
        Population = population;
        Candidate = candidate;
        Authority = authority;
        Source = source;
        ContentGeneration = contentGeneration;
        Origin = origin;
        PackageFailures = packageFailures;
        Libraries = libraries;
    }

    public PackagePlatformSourceGeneration Generation { get; }
    public PackageReferencePackCoordinate Coordinate { get; }
    public PackageReferencePopulationDemand Population { get; }
    public PackageAcquisitionCandidate Candidate { get; }
    public ConfiguredPackageAuthority Authority { get; }
    public PackageSourceResultIdentity Source { get; }
    public PackageContentGenerationIdentity ContentGeneration { get; }
    public PackagePayloadOrigin Origin { get; }
    public ImmutableArray<PackageAuthorityFailure> PackageFailures { get; }
    public ImmutableArray<PackageReferenceLibrary> Libraries { get; }
}

public enum PackagePlatformSourceDiagnosticKind
{
    UnsupportedTarget,
    InvalidCoordinate,
    InvalidSelection,
    AuthorizationDenied,
    PackageUnavailable,
    MemberUnavailable,
    InvalidLayout,
    MalformedAssembly,
    DuplicateAssemblyIdentity,
    AssemblyIdentityMismatch,
    WorkLimitExceeded,
    Timeout,
    PackageFailure,
    ContentReadFailure,
    InvalidManifest,
    InvalidFrameworkGraph,
    InvalidMember,
    DuplicateLogicalCoordinate,
}

public sealed record PackagePlatformSourceDiagnostic(
    PackagePlatformSourceDiagnosticKind Kind,
    string Summary,
    ImmutableArray<PackageAuthorityFailure> PackageFailures);

public abstract record PackagePlatformSourceOutcome<T> where T : notnull
{
    private protected PackagePlatformSourceOutcome(PackagePlatformSourceGeneration generation) =>
        Generation = generation;

    public PackagePlatformSourceGeneration Generation { get; }

    public sealed record Succeeded : PackagePlatformSourceOutcome<T>
    {
        internal Succeeded(PackagePlatformSourceGeneration generation, T value) : base(generation) =>
            Value = value;

        public T Value { get; }
    }

    public abstract record NotSucceeded : PackagePlatformSourceOutcome<T>
    {
        private protected NotSucceeded(
            PackagePlatformSourceGeneration generation, PackagePlatformSourceDiagnostic diagnostic)
            : base(generation) => Diagnostic = diagnostic;

        public PackagePlatformSourceDiagnostic Diagnostic { get; private init; }

        internal NotSucceeded WithPackageFailures(ImmutableArray<PackageAuthorityFailure> failures) =>
            this with { Diagnostic = Diagnostic with { PackageFailures = failures } };
    }

    public sealed record Unavailable : NotSucceeded
    {
        internal Unavailable(PackagePlatformSourceGeneration generation, PackagePlatformSourceDiagnostic diagnostic)
            : base(generation, diagnostic) { }
    }

    public sealed record Rejected : NotSucceeded
    {
        internal Rejected(PackagePlatformSourceGeneration generation, PackagePlatformSourceDiagnostic diagnostic)
            : base(generation, diagnostic) { }
    }

    public sealed record Incomplete : NotSucceeded
    {
        internal Incomplete(PackagePlatformSourceGeneration generation, PackagePlatformSourceDiagnostic diagnostic)
            : base(generation, diagnostic) { }
    }

    public sealed record Failed : NotSucceeded
    {
        internal Failed(PackagePlatformSourceGeneration generation, PackagePlatformSourceDiagnostic diagnostic)
            : base(generation, diagnostic) { }
    }
}
