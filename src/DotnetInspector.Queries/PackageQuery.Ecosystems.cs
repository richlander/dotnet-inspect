using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries;

/// <summary>The owner-issued rule that established package membership.</summary>
public enum PackageQueryEcosystemMembershipBasis
{
    ExactPackage,
    PackagePrefix,
}

/// <summary>
/// Resource-free package-population membership for one canonical ecosystem.
/// </summary>
public sealed class PackageQueryEcosystemMembershipDeclaration
{
    private readonly IReadOnlyDictionary<string, string> _exactPackageIds;

    public PackageQueryEcosystemMembershipDeclaration(
        WorkspaceEcosystemRegistrationId id,
        IEnumerable<PackageCoordinate> exactPackages,
        IEnumerable<PackagePrefixDeclaration> packagePrefixes)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
        ExactPackages = SnapshotExactPackages(id, exactPackages);
        PackagePrefixes = SnapshotPackagePrefixes(id, packagePrefixes);
        _exactPackageIds = ExactPackages.ToDictionary(
            package => package.PackageId,
            package => package.PackageId,
            StringComparer.OrdinalIgnoreCase);
    }

    public WorkspaceEcosystemRegistrationId Id { get; }

    public ImmutableArray<PackageCoordinate> ExactPackages { get; }

    public ImmutableArray<PackagePrefixDeclaration> PackagePrefixes { get; }

    public bool HasPackagePopulation =>
        !ExactPackages.IsEmpty || !PackagePrefixes.IsEmpty;

    internal bool TryMatch(
        string packageId,
        out PackageQueryEcosystemMembershipMatch match)
    {
        if (_exactPackageIds.TryGetValue(packageId, out string? exactPackageId))
        {
            match = new(
                PackageQueryEcosystemMembershipBasis.ExactPackage,
                exactPackageId);
            return true;
        }

        foreach (PackagePrefixDeclaration prefix in PackagePrefixes)
        {
            if (packageId.StartsWith(
                    prefix.Prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                match = new(
                    PackageQueryEcosystemMembershipBasis.PackagePrefix,
                    prefix.Prefix);
                return true;
            }
        }

        match = default;
        return false;
    }

    private static ImmutableArray<PackageCoordinate> SnapshotExactPackages(
        WorkspaceEcosystemRegistrationId id,
        IEnumerable<PackageCoordinate> exactPackages)
    {
        ArgumentNullException.ThrowIfNull(exactPackages);
        PackageCoordinate[] snapshot = [.. exactPackages];
        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PackageCoordinate package in snapshot)
        {
            ArgumentNullException.ThrowIfNull(package);
            PackageCoordinateResolution.Invalid? invalid =
                PackageCoordinateResolver.Validate(package);
            if (invalid is not null
                || package.Version is not null
                || package.Framework is not null
                || package.RuntimeIdentifier is not null)
            {
                throw new ArgumentException(
                    $"Ecosystem '{id}' contains an invalid exact package"
                    + $" registration '{package.PackageId}'.",
                    nameof(exactPackages));
            }

            if (!packageIds.Add(package.PackageId))
            {
                throw new ArgumentException(
                    $"Ecosystem '{id}' contains duplicate exact package"
                    + $" registration '{package.PackageId}'.",
                    nameof(exactPackages));
            }
        }

        return [.. snapshot];
    }

    private static ImmutableArray<PackagePrefixDeclaration>
        SnapshotPackagePrefixes(
            WorkspaceEcosystemRegistrationId id,
            IEnumerable<PackagePrefixDeclaration> packagePrefixes)
    {
        ArgumentNullException.ThrowIfNull(packagePrefixes);
        PackagePrefixDeclaration[] snapshot = [.. packagePrefixes];
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PackagePrefixDeclaration prefix in snapshot)
        {
            ArgumentNullException.ThrowIfNull(prefix);
            if (!prefixes.Add(prefix.Prefix))
            {
                throw new ArgumentException(
                    $"Ecosystem '{id}' contains duplicate package-prefix"
                    + $" registration '{prefix.Prefix}'.",
                    nameof(packagePrefixes));
            }
        }

        return [.. snapshot];
    }
}

/// <summary>
/// Immutable application binding from canonical ecosystem identities to
/// resource-free package-population declarations.
/// </summary>
public sealed class PackageQueryEcosystemMembershipCatalog
{
    private readonly IReadOnlyDictionary<
        WorkspaceEcosystemRegistrationId,
        PackageQueryEcosystemMembershipDeclaration> _byId;

    public PackageQueryEcosystemMembershipCatalog(
        IEnumerable<PackageQueryEcosystemMembershipDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        PackageQueryEcosystemMembershipDeclaration[] snapshot = [.. declarations];
        var byId = new Dictionary<
            WorkspaceEcosystemRegistrationId,
            PackageQueryEcosystemMembershipDeclaration>();
        foreach (PackageQueryEcosystemMembershipDeclaration declaration in snapshot)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            if (!byId.TryAdd(declaration.Id, declaration))
            {
                throw new ArgumentException(
                    $"Ecosystem membership '{declaration.Id}' is registered more than once.",
                    nameof(declarations));
            }
        }

        Declarations = [.. snapshot];
        _byId = byId;
    }

    public ImmutableArray<PackageQueryEcosystemMembershipDeclaration>
        Declarations { get; }

    internal bool TryGet(
        WorkspaceEcosystemRegistrationId id,
        [NotNullWhen(true)]
        out PackageQueryEcosystemMembershipDeclaration? declaration) =>
        _byId.TryGetValue(id, out declaration);
}

internal readonly record struct PackageQueryEcosystemMembershipMatch(
    PackageQueryEcosystemMembershipBasis Basis,
    string Registration);
