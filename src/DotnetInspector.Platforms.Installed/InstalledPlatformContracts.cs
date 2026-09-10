using ILInspector.Metadata;

namespace DotnetInspector.Platforms.Installed;

/// <summary>An installed platform family understood by the dotnet hive layout.</summary>
public enum InstalledPlatformFamily
{
    DotNetRuntime,
    AspNetCore,
}

/// <summary>Opaque identity for one host-selected dotnet hive.</summary>
public sealed class InstalledDotnetHiveIdentity
{
    private InstalledDotnetHiveIdentity(string name) => Name = name;

    public string Name { get; }

    public static InstalledDotnetHiveIdentity Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(name);
    }

    public override string ToString() => Name;
}

/// <summary>Opaque identity for one immutable installed-source attempt.</summary>
public sealed class InstalledPlatformSourceGeneration
{
    private InstalledPlatformSourceGeneration(string name) => Name = name;

    public string Name { get; }

    internal static InstalledPlatformSourceGeneration Issue(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(name);
    }

    public override string ToString() => Name;
}

/// <summary>
/// One exact installed reference-pack coordinate. It is a source coordinate,
/// not a platform target identity.
/// </summary>
public sealed record InstalledReferencePackCoordinate
{
    public InstalledReferencePackCoordinate(
        InstalledDotnetHiveIdentity hive,
        InstalledPlatformFamily family,
        PlatformTargetFramework targetFramework,
        PlatformVersion version)
    {
        ArgumentNullException.ThrowIfNull(hive);
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));
        ArgumentNullException.ThrowIfNull(targetFramework);
        ArgumentNullException.ThrowIfNull(version);

        Hive = hive;
        Family = family;
        TargetFramework = targetFramework;
        Version = version;
    }

    public InstalledDotnetHiveIdentity Hive { get; }
    public InstalledPlatformFamily Family { get; }
    public PlatformTargetFramework TargetFramework { get; }
    public PlatformVersion Version { get; }
}

/// <summary>One exact target discovered from an installed reference pack.</summary>
public sealed record InstalledReferenceTarget(
    InstalledReferencePackCoordinate Coordinate);

/// <summary>Bounded discovery request for one installed reference-pack family.</summary>
public sealed record InstalledReferenceDiscoveryRequest
{
    public InstalledReferenceDiscoveryRequest(
        InstalledPlatformFamily family,
        PlatformTargetFramework targetFramework,
        int maxCandidates)
    {
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));
        ArgumentNullException.ThrowIfNull(targetFramework);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCandidates);

        Family = family;
        TargetFramework = targetFramework;
        MaxCandidates = maxCandidates;
    }

    public InstalledPlatformFamily Family { get; }
    public PlatformTargetFramework TargetFramework { get; }
    public int MaxCandidates { get; }
}

/// <summary>One-library or complete-population installed reference demand.</summary>
public abstract class InstalledReferencePopulationDemand
{
    private protected InstalledReferencePopulationDemand()
    {
    }

    public sealed class Assembly : InstalledReferencePopulationDemand
    {
        public Assembly(AssemblyReferenceIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            Identity = identity;
        }

        public AssemblyReferenceIdentity Identity { get; }
    }

    public sealed class CompletePopulation : InstalledReferencePopulationDemand
    {
        public CompletePopulation()
        {
        }
    }
}

/// <summary>Finite work allowed for one reference-pack realization.</summary>
public sealed record InstalledReferenceWorkBudget
{
    public InstalledReferenceWorkBudget(
        int maxAssemblies,
        long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxAssemblies);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        MaxAssemblies = maxAssemblies;
        MaxBytes = maxBytes;
    }

    public int MaxAssemblies { get; }
    public long MaxBytes { get; }
}

/// <summary>One exact installed reference-pack realization request.</summary>
public sealed record InstalledReferenceRealizationRequest
{
    public InstalledReferenceRealizationRequest(
        InstalledReferencePackCoordinate coordinate,
        InstalledReferencePopulationDemand population,
        InstalledReferenceWorkBudget work)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(work);
        Coordinate = coordinate;
        Population = population;
        Work = work;
    }

    public InstalledReferencePackCoordinate Coordinate { get; }
    public InstalledReferencePopulationDemand Population { get; }
    public InstalledReferenceWorkBudget Work { get; }
}

/// <summary>One immutable reference assembly copied from an installed pack.</summary>
public sealed class InstalledReferenceLibrary
{
    private readonly byte[] _content;

    internal InstalledReferenceLibrary(
        string fileName,
        AssemblyReferenceIdentity identity,
        byte[] content)
    {
        FileName = fileName;
        Identity = identity;
        _content = content;
    }

    public string FileName { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public long ContentLength => _content.LongLength;

    public Stream OpenRead() =>
        new MemoryStream(_content, writable: false);
}

/// <summary>Complete immutable inventory from one installed discovery attempt.</summary>
public sealed record InstalledReferenceTargetInventory
{
    internal InstalledReferenceTargetInventory(
        InstalledPlatformSourceGeneration generation,
        IReadOnlyList<InstalledReferenceTarget> targets)
    {
        Generation = generation;
        Targets = targets;
    }

    public InstalledPlatformSourceGeneration Generation { get; }
    public IReadOnlyList<InstalledReferenceTarget> Targets { get; }
}

/// <summary>
/// Demand-complete immutable reference-pack realization from one source
/// generation.
/// </summary>
public sealed record InstalledReferenceRealization
{
    internal InstalledReferenceRealization(
        InstalledPlatformSourceGeneration generation,
        InstalledReferencePackCoordinate coordinate,
        InstalledReferencePopulationDemand population,
        IReadOnlyList<InstalledReferenceLibrary> libraries)
    {
        Generation = generation;
        Coordinate = coordinate;
        Population = population;
        Libraries = libraries;
    }

    public InstalledPlatformSourceGeneration Generation { get; }
    public InstalledReferencePackCoordinate Coordinate { get; }
    public InstalledReferencePopulationDemand Population { get; }
    public IReadOnlyList<InstalledReferenceLibrary> Libraries { get; }
}

public enum InstalledPlatformSourceDiagnosticKind
{
    UnsupportedHost,
    InvalidRequest,
    InvalidCoordinate,
    InvalidLayout,
    InvalidManifest,
    InvalidFrameworkGraph,
    InvalidMember,
    WorkLimitExceeded,
    MalformedAssembly,
    DuplicateAssemblyIdentity,
    AssemblyIdentityMismatch,
    IoFailure,
}

/// <summary>Typed source-owner diagnostic for one terminal attempt.</summary>
public sealed record InstalledPlatformSourceDiagnostic(
    InstalledPlatformSourceDiagnosticKind Kind,
    string Summary);

public enum InstalledPlatformSourceUnavailabilityKind
{
    Absent,
    Unavailable,
}

/// <summary>Closed installed-source outcome retaining typed failure evidence.</summary>
public abstract record InstalledPlatformSourceOutcome<T>
    where T : notnull
{
    private protected InstalledPlatformSourceOutcome(
        InstalledPlatformSourceGeneration generation) =>
        Generation = generation;

    public InstalledPlatformSourceGeneration Generation { get; }

    public sealed record Succeeded : InstalledPlatformSourceOutcome<T>
    {
        public Succeeded(
            InstalledPlatformSourceGeneration generation,
            T value)
            : base(generation)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public T Value { get; }
    }

    public sealed record Unavailable : InstalledPlatformSourceOutcome<T>
    {
        public Unavailable(
            InstalledPlatformSourceGeneration generation,
            InstalledPlatformSourceUnavailabilityKind reason,
            InstalledPlatformSourceDiagnostic diagnostic)
            : base(generation)
        {
            if (!Enum.IsDefined(reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            ArgumentNullException.ThrowIfNull(diagnostic);
            Reason = reason;
            Diagnostic = diagnostic;
        }

        public InstalledPlatformSourceUnavailabilityKind Reason { get; }
        public InstalledPlatformSourceDiagnostic Diagnostic { get; }
    }

    public sealed record Rejected : InstalledPlatformSourceOutcome<T>
    {
        public Rejected(
            InstalledPlatformSourceGeneration generation,
            InstalledPlatformSourceDiagnostic diagnostic)
            : base(generation)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            Diagnostic = diagnostic;
        }

        public InstalledPlatformSourceDiagnostic Diagnostic { get; }
    }

    public sealed record Failed : InstalledPlatformSourceOutcome<T>
    {
        public Failed(
            InstalledPlatformSourceGeneration generation,
            InstalledPlatformSourceDiagnostic diagnostic)
            : base(generation)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            Diagnostic = diagnostic;
        }

        public InstalledPlatformSourceDiagnostic Diagnostic { get; }
    }

    public sealed record Incomplete : InstalledPlatformSourceOutcome<T>
    {
        public Incomplete(
            InstalledPlatformSourceGeneration generation,
            InstalledPlatformSourceDiagnostic diagnostic)
            : base(generation)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            Diagnostic = diagnostic;
        }

        public InstalledPlatformSourceDiagnostic Diagnostic { get; }
    }
}
