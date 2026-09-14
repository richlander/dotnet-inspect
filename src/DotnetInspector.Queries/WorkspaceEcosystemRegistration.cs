using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Stable lower-layer identity for one Workspace ecosystem registration.
/// </summary>
public sealed record WorkspaceEcosystemRegistrationId
{
    private const string Prefix = "ecosystem.";

    private WorkspaceEcosystemRegistrationId(string value) => Value = value;

    /// <summary>Gets the canonical identity text.</summary>
    public string Value { get; }

    /// <summary>Creates a typed identity from canonical text.</summary>
    public static WorkspaceEcosystemRegistrationId Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryCreate(value, out WorkspaceEcosystemRegistrationId? id)
            ? id
            : throw new ArgumentException(
                $"'{value}' is not a canonical Workspace ecosystem identity.",
                nameof(value));
    }

    /// <summary>Tries to create a typed identity from canonical text.</summary>
    public static bool TryCreate(
        string? value,
        [NotNullWhen(true)] out WorkspaceEcosystemRegistrationId? id)
    {
        id = null;
        if (value is not { Length: > 0 and <= 80 }
            || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> name = value.AsSpan(Prefix.Length);
        if (name.IsEmpty || !char.IsAsciiLetterLower(name[0]))
            return false;

        bool previousWasHyphen = false;
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character))
            {
                previousWasHyphen = false;
                continue;
            }

            if (character != '-'
                || index == name.Length - 1
                || previousWasHyphen)
            {
                return false;
            }

            previousWasHyphen = true;
        }

        id = new WorkspaceEcosystemRegistrationId(value);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// One owner-issued population contribution retained by an ecosystem.
/// </summary>
public abstract record WorkspaceEcosystemPopulationDeclaration
{
    private protected WorkspaceEcosystemPopulationDeclaration()
    {
    }

    /// <summary>One exact package- or Platform-origin managed Library.</summary>
    public sealed record ExactLibrary :
        WorkspaceEcosystemPopulationDeclaration
    {
        public ExactLibrary(ExactLibrarySourceCoordinate coordinate)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            Coordinate = coordinate;
        }

        public ExactLibrarySourceCoordinate Coordinate { get; }
    }

    /// <summary>One logical Platform library population.</summary>
    public sealed record Platform :
        WorkspaceEcosystemPopulationDeclaration
    {
        public Platform(PlatformLibraryPopulationDeclaration population)
        {
            ArgumentNullException.ThrowIfNull(population);
            Population = population;
        }

        public PlatformLibraryPopulationDeclaration Population { get; }
    }

    /// <summary>One literal package-prefix population.</summary>
    public sealed record PackagePrefix :
        WorkspaceEcosystemPopulationDeclaration
    {
        public PackagePrefix(PackagePrefixDeclaration prefix)
        {
            ArgumentNullException.ThrowIfNull(prefix);
            Prefix = prefix;
        }

        public PackagePrefixDeclaration Prefix { get; }
    }
}

/// <summary>
/// Immutable Workspace-relevant contributions for one ecosystem registration.
/// </summary>
public sealed class WorkspaceEcosystemRegistrationDeclaration
{
    public WorkspaceEcosystemRegistrationDeclaration(
        WorkspaceEcosystemRegistrationId id,
        IEnumerable<string> namespaceRoots,
        IEnumerable<PackageCoordinate> corePackages,
        IEnumerable<WorkspaceEcosystemPopulationDeclaration> populations,
        EcosystemIntegrationScannerBinding? integrationScanner = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(namespaceRoots);
        ArgumentNullException.ThrowIfNull(corePackages);
        ArgumentNullException.ThrowIfNull(populations);

        ImmutableArray<string> retainedNamespaceRoots =
            SnapshotNamespaceRoots(id, namespaceRoots);
        ImmutableArray<PackageCoordinate> retainedCorePackages =
            SnapshotCorePackages(id, corePackages);
        ImmutableArray<WorkspaceEcosystemPopulationDeclaration>
            retainedPopulations = SnapshotPopulations(id, populations);

        if (retainedNamespaceRoots.IsEmpty
            && retainedCorePackages.IsEmpty
            && retainedPopulations.IsEmpty
            && integrationScanner is null)
        {
            throw new ArgumentException(
                "A Workspace ecosystem declaration must contain at least one contribution.");
        }

        Id = id;
        NamespaceRoots = retainedNamespaceRoots;
        CorePackages = retainedCorePackages;
        Populations = retainedPopulations;
        IntegrationScanner = integrationScanner;
    }

    public WorkspaceEcosystemRegistrationId Id { get; }

    public ImmutableArray<string> NamespaceRoots { get; }

    public ImmutableArray<PackageCoordinate> CorePackages { get; }

    public ImmutableArray<WorkspaceEcosystemPopulationDeclaration>
        Populations { get; }

    public EcosystemIntegrationScannerBinding? IntegrationScanner { get; }

    private static ImmutableArray<string> SnapshotNamespaceRoots(
        WorkspaceEcosystemRegistrationId id,
        IEnumerable<string> namespaceRoots)
    {
        ImmutableArray<string> snapshot =
            Snapshot(namespaceRoots, nameof(namespaceRoots));
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in snapshot)
        {
            if (string.IsNullOrWhiteSpace(root)
                || root.Any(character =>
                    char.IsWhiteSpace(character) || character is '*' or '?')
                || root[0] == '.'
                || root[^1] == '.'
                || root.Contains("..", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Workspace ecosystem '{id}' contains invalid namespace"
                    + $" root '{root}'.",
                    nameof(namespaceRoots));
            }

            if (!unique.Add(root))
            {
                throw new ArgumentException(
                    $"Workspace ecosystem '{id}' contains duplicate namespace"
                    + $" root '{root}'.",
                    nameof(namespaceRoots));
            }
        }

        return snapshot;
    }

    private static ImmutableArray<PackageCoordinate> SnapshotCorePackages(
        WorkspaceEcosystemRegistrationId id,
        IEnumerable<PackageCoordinate> corePackages)
    {
        ImmutableArray<PackageCoordinate> snapshot =
            Snapshot(corePackages, nameof(corePackages));
        var packageIds = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (PackageCoordinate package in snapshot)
        {
            if (package is null)
            {
                throw new ArgumentException(
                    $"Workspace ecosystem '{id}' contains a null core package.",
                    nameof(corePackages));
            }

            PackageCoordinateResolution.Invalid? invalid =
                PackageCoordinateResolver.Validate(package);
            if (invalid is not null)
            {
                throw new ArgumentException(
                    $"Workspace ecosystem '{id}' contains invalid core package"
                    + $" coordinate '{package.PackageId}': {invalid.Message}",
                    nameof(corePackages));
            }

            if (package.Version is not null
                || package.Framework is not null
                || package.RuntimeIdentifier is not null)
            {
                throw new ArgumentException(
                    $"Workspace ecosystem '{id}' contains a versioned or"
                    + " target-specific core-package coordinate.",
                    nameof(corePackages));
            }

            if (!packageIds.Add(package.PackageId))
            {
                throw new ArgumentException(
                    $"Workspace ecosystem '{id}' contains duplicate core-package"
                    + $" ID '{package.PackageId}'.",
                    nameof(corePackages));
            }
        }

        return snapshot;
    }

    private static ImmutableArray<WorkspaceEcosystemPopulationDeclaration>
        SnapshotPopulations(
            WorkspaceEcosystemRegistrationId id,
            IEnumerable<WorkspaceEcosystemPopulationDeclaration> populations)
    {
        ImmutableArray<WorkspaceEcosystemPopulationDeclaration> snapshot =
            Snapshot(populations, nameof(populations));
        var unique = new HashSet<WorkspaceEcosystemPopulationDeclaration>();
        foreach (WorkspaceEcosystemPopulationDeclaration population in snapshot)
        {
            if (population is null)
            {
                throw new ArgumentException(
                    $"Workspace ecosystem '{id}' contains a null population.",
                    nameof(populations));
            }

            if (!unique.Add(population))
            {
                throw new ArgumentException(
                    $"Workspace ecosystem '{id}' contains a duplicate"
                    + " population declaration.",
                    nameof(populations));
            }
        }

        return snapshot;
    }

    private static ImmutableArray<T> Snapshot<T>(
        IEnumerable<T> values,
        string parameterName)
    {
        if (values is ImmutableArray<T> immutable)
        {
            if (immutable.IsDefault)
            {
                throw new ArgumentException(
                    "A contribution sequence cannot be uninitialized.",
                    parameterName);
            }

            return immutable;
        }

        return [.. values];
    }
}
