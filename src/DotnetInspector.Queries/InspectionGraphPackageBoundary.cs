using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Queries;

/// <summary>
/// The subject lens used to project workspace package ownership.
/// </summary>
public enum InspectionGraphPackageBoundaryLens
{
    PackageGroups,
    PackageNodes,
    Mixed,
}

/// <summary>
/// Typed package ownership retained from realized workspace members.
/// </summary>
/// <remarks>
/// <c>WorkspaceContextLoaderTests.PackageBoundary_ProjectsLoadedPackageAsGroupAndNode</c>
/// gates the real package-acquisition path.
/// </remarks>
public sealed class InspectionGraphPackageBoundary
{
    readonly ImmutableArray<Artifact> _artifacts;
    readonly ImmutableArray<Package> _packages;
    readonly IReadOnlyDictionary<
        AssemblyAcquisitionRegistration,
        InspectionGraphSubject.PackageSubject> _packagesByRegistration;

    InspectionGraphPackageBoundary(
        ImmutableArray<Artifact> artifacts,
        ImmutableArray<Package> packages,
        IReadOnlyDictionary<
            AssemblyAcquisitionRegistration,
            InspectionGraphSubject.PackageSubject> packagesByRegistration)
    {
        _artifacts = artifacts;
        _packages = packages;
        _packagesByRegistration = packagesByRegistration;
    }

    /// <summary>
    /// Captures package ownership from one loaded workspace context.
    /// </summary>
    public static InspectionGraphPackageBoundary Create(
        WorkspaceContextLoadOutcome.Loaded context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var groupRegistrations = context.Group.Participants
            .Select(static participant =>
                participant.Assembly.Registration)
            .ToHashSet();
        var memberRegistrations = context.Members
            .Select(static member =>
                member.Participant.Assembly.Registration)
            .ToHashSet();
        if (!groupRegistrations.SetEquals(memberRegistrations))
        {
            throw new ArgumentException(
                "Package ownership requires every participant in the loaded workspace context.",
                nameof(context));
        }

        return Create(context.Members);
    }

    internal static InspectionGraphPackageBoundary Create(
        IEnumerable<WorkspaceContextMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        var artifacts = ImmutableArray.CreateBuilder<Artifact>();
        var artifactsByRegistration =
            new Dictionary<AssemblyAcquisitionRegistration, Artifact>();
        var packages = ImmutableArray.CreateBuilder<Package>();
        var packagesByCoordinate =
            new Dictionary<RealizedMemberCoordinate.Package, Package>();
        var packagesByRegistration = new Dictionary<
            AssemblyAcquisitionRegistration,
            InspectionGraphSubject.PackageSubject>();

        foreach (WorkspaceContextMember member in members)
        {
            ArgumentNullException.ThrowIfNull(member);
            ResolvedAssemblyReference assembly =
                member.Participant.Assembly;
            AssemblyAcquisitionRegistration registration =
                assembly.Registration;

            if (artifactsByRegistration.TryGetValue(
                    registration,
                    out Artifact? existing))
            {
                if (existing.Realized != member.Realized)
                {
                    throw new ArgumentException(
                        "One acquired assembly cannot belong to more than one realized workspace member.",
                        nameof(members));
                }

                continue;
            }

            InspectionGraphSubject.PackageSubject? packageSubject = null;
            if (member.Realized
                is RealizedMemberCoordinate.Package packageCoordinate)
            {
                ValidatePackageProvenance(
                    packageCoordinate,
                    assembly.Provenance,
                    nameof(members));
                if (!packagesByCoordinate.TryGetValue(
                        packageCoordinate,
                        out Package? package))
                {
                    packageSubject = (InspectionGraphSubject.PackageSubject)
                        InspectionGraphSubject.ForRealizedPackage(
                            packageCoordinate);
                    package = new Package(
                        packageCoordinate,
                        packageSubject);
                    packagesByCoordinate.Add(packageCoordinate, package);
                    packages.Add(package);
                }
                else
                {
                    packageSubject = package.Subject;
                }

                packagesByRegistration.Add(
                    registration,
                    packageSubject);
            }
            else if (assembly.Provenance
                is AssemblyResolutionProvenance.PackageAsset)
            {
                throw new ArgumentException(
                    "Package assembly provenance requires a realized package coordinate.",
                    nameof(members));
            }

            var artifact = new Artifact(
                member.Realized,
                (InspectionGraphSubject.AssemblySubject)
                    InspectionGraphSubject.ForAcquiredAssembly(assembly),
                packageSubject);
            artifactsByRegistration.Add(registration, artifact);
            artifacts.Add(artifact);
        }

        return new InspectionGraphPackageBoundary(
            artifacts.ToImmutable(),
            packages.ToImmutable(),
            packagesByRegistration);
    }

    /// <summary>
    /// Gets the realized package that owns one acquired assembly.
    /// </summary>
    public bool TryGetPackageSubject(
        AssemblyAcquisitionRegistration registration,
        [NotNullWhen(true)]
        out InspectionGraphSubject.PackageSubject? package)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return _packagesByRegistration.TryGetValue(
            registration,
            out package);
    }

    /// <summary>
    /// Projects package ownership as structured groups, package nodes, or both.
    /// </summary>
    public InspectionGraphDocument Project(
        InspectionGraphPackageBoundaryLens lens)
    {
        InspectionGraphCollections.RequireDefined(lens, nameof(lens));

        bool includeArtifacts =
            lens is InspectionGraphPackageBoundaryLens.PackageGroups
                or InspectionGraphPackageBoundaryLens.Mixed;
        bool includePackageNodes =
            lens is InspectionGraphPackageBoundaryLens.PackageNodes
                or InspectionGraphPackageBoundaryLens.Mixed;

        var groups = includeArtifacts
            ? _packages
                .Select((package, id) =>
                    new InspectionGraphGroup(
                        id,
                        package.Subject,
                        parentId: null))
                .ToImmutableArray()
            : [];
        Dictionary<InspectionGraphSubject.PackageSubject, int> groupIds =
            groups.ToDictionary(
                group => (InspectionGraphSubject.PackageSubject)
                    group.Subject,
                static group => group.Id);

        var nodes = ImmutableArray.CreateBuilder<InspectionGraphNode>();
        if (includePackageNodes)
        {
            foreach (Package package in _packages)
            {
                nodes.Add(
                    new InspectionGraphNode(
                        nodes.Count,
                        package.Subject,
                        InspectionGraphNodeRole.Ordinary,
                        []));
            }
        }

        if (includeArtifacts)
        {
            foreach (Artifact artifact in _artifacts)
            {
                nodes.Add(
                    new InspectionGraphNode(
                        nodes.Count,
                        artifact.Subject,
                        InspectionGraphNodeRole.Ordinary,
                        artifact.Package is null
                            ? []
                            : [groupIds[artifact.Package]]));
            }
        }

        return new InspectionGraphDocument(
            includeArtifacts
                ? InspectionGraphDocumentScope.SessionBound
                : InspectionGraphDocumentScope.Portable,
            nodes,
            groups,
            [],
            [],
            [],
            [],
            [],
            []);
    }

    static void ValidatePackageProvenance(
        RealizedMemberCoordinate.Package coordinate,
        AssemblyResolutionProvenance provenance,
        string parameterName)
    {
        if (provenance
                is not AssemblyResolutionProvenance.PackageAsset package
            || !StringComparer.OrdinalIgnoreCase.Equals(
                coordinate.PackageId,
                package.PackageId)
            || !NuGetVersion.TryParse(
                package.PackageVersion,
                out NuGetVersion? version)
            || !StringComparer.Ordinal.Equals(
                coordinate.Version,
                version.ToNormalizedString().ToLowerInvariant())
            || !StringComparer.OrdinalIgnoreCase.Equals(
                coordinate.Framework,
                package.Tfm)
            || !StringComparer.OrdinalIgnoreCase.Equals(
                coordinate.RuntimeIdentifier,
                package.Rid))
        {
            throw new ArgumentException(
                "A realized package coordinate must match its assembly provenance.",
                parameterName);
        }
    }

    sealed record Artifact(
        RealizedMemberCoordinate Realized,
        InspectionGraphSubject.AssemblySubject Subject,
        InspectionGraphSubject.PackageSubject? Package);

    sealed record Package(
        RealizedMemberCoordinate.Package Coordinate,
        InspectionGraphSubject.PackageSubject Subject);
}
