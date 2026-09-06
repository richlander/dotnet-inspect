using System.Collections.Immutable;

using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Workspaces;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

internal sealed record PackageAssemblyArtifactProvenance(
    RealizedMemberCoordinate.Package Coordinate,
    PackageContentGenerationIdentity ContentGenerationIdentity,
    PackageRootSelectionIdentity SelectionIdentity,
    PackageCompileAsset Asset) : IArtifactProvenance;

public sealed partial class InspectionWorkspace
{
    /// <summary>
    /// Realizes one acquisition-bound package through a retained artifact
    /// generation into reference-preferred surface and implementation roles.
    /// </summary>
    /// <remarks>
    /// The workspace must be created by <see cref="CreateAsynchronous"/>.
    /// Distinct selected assets are materialized once under
    /// <see cref="PackageAssemblyContextRealizationOptions.MaxAggregateRetainedImageBytes"/>,
    /// then the artifact session is transferred to the exact resulting role
    /// groups. Calls must be serialized with other group admissions in the
    /// same workspace. The workspace retains the artifact session until it
    /// closes, even if the returned realization is disposed earlier. Gated by
    /// <c>ArtifactBackedPackageRealization_PreservesMixedParticipantsAndExactLifetime</c>
    /// and
    /// <c>ArtifactBackedPackageRealization_RejectsAggregateBudgetWithoutPartialGroup</c>.
    /// </remarks>
    public async ValueTask<PackageAssemblyContextRealization>
        RealizePackageAssemblyContextRolesAsync(
            PackageRootBinding package,
            PackageAssemblyContextRealizationOptions? options = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_lifetimeMode
            != InspectionWorkspaceLifetimeMode.Asynchronous)
        {
            throw new InvalidOperationException(
                "Artifact-backed package realization requires a workspace created by CreateAsynchronous.");
        }

        PackageRoleRealizationPreparation preparation =
            PreparePackageRoleRealization(
                [package.Root],
                options,
                cancellationToken);
        if (preparation.SurfaceAssets.IsEmpty)
        {
            return new PackageAssemblyContextRealization(
                roles: null,
                [],
                []);
        }

        ArtifactBackedBudgets budgets =
            ApplyArtifactBackedBudget(preparation);
        preparation = preparation with
        {
            GroupBudget = budgets.GroupBudget,
        };
        ImmutableArray<RoleAsset> artifacts =
            DistinctSelectedAssets(preparation);
        ArtifactSetSessionLimits limits =
            ArtifactLimits(
                artifacts.Length,
                budgets.ArtifactBudget,
                preparation.Options);
        var session = new ArtifactSetSession(limits);
        ArtifactQueryLease? queryLease = null;
        PackageAssemblyContextRealization? realization = null;
        bool transferred = false;
        try
        {
            PackageArtifactPublication publication =
                await PublishPackageArtifactsAsync(
                        session,
                        [.. artifacts.Select(artifact =>
                            new PackageArtifactSource(
                                new PackageAssemblyArtifactProvenance(
                                    package.Coordinate,
                                    package.ContentGenerationIdentity,
                                    package.SelectionIdentity,
                                    artifact.Asset),
                                token => OpenEntry(
                                    artifact,
                                    preparation.Options.MaxAssemblyEntryBytes,
                                    token)))],
                        cancellationToken)
                    .ConfigureAwait(false);
            if (publication.Rejection is { } rejected)
            {
                throw PublicationFailure(rejected);
            }

            queryLease = publication.Lease!;
            var contentByAsset = new Dictionary<RoleAsset, ProjectedPackageArtifact>(
                RoleAssetIdentityComparer.Instance);
            for (int index = 0; index < artifacts.Length; index++)
                contentByAsset.Add(artifacts[index], publication.Artifacts[index]);
            ImmutableArray<RoleAssembly> surfaceRole =
                CreateArtifactRole(
                    preparation.SurfaceAssets,
                    contentByAsset,
                    cancellationToken);
            ImmutableArray<RoleAssembly> implementationRole =
                preparation.Shared
                    ? surfaceRole
                    : CreateArtifactRole(
                        preparation.ImplementationAssets,
                        contentByAsset,
                        cancellationToken);
            realization = CreatePackageAssemblyContextRealization(
                preparation,
                surfaceRole,
                implementationRole,
                cancellationToken);
            RegisterArtifactSession(
                session,
                queryLease,
                DependentGroups(realization));
            transferred = true;
            return realization;
        }
        catch (Exception failure)
        {
            if (!transferred)
            {
                await CleanupFailedArtifactRealizationAsync(
                        realization,
                        queryLease,
                        session,
                        failure)
                    .ConfigureAwait(false);
            }

            throw;
        }
    }

    static ArtifactBackedBudgets ApplyArtifactBackedBudget(
        PackageRoleRealizationPreparation preparation)
    {
        long totalBudget =
            preparation.Options.MaxAggregateRetainedImageBytes;
        if (totalBudget < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(
                    PackageAssemblyContextRealizationOptions
                        .MaxAggregateRetainedImageBytes),
                totalBudget,
                "Artifact-backed package realization requires at least two retained bytes.");
        }

        long artifactBudget = totalBudget / 2;
        if (artifactBudget > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(
                    PackageAssemblyContextRealizationOptions
                        .MaxAggregateRetainedImageBytes),
                totalBudget,
                "The artifact share of the retained-byte budget cannot exceed Int32.MaxValue.");
        }
        if (preparation.Options.MaxAssemblyEntryBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(
                    PackageAssemblyContextRealizationOptions
                        .MaxAssemblyEntryBytes),
                preparation.Options.MaxAssemblyEntryBytes,
                "Artifact-backed package realization requires a positive per-entry byte limit.");
        }

        long roleBudget = totalBudget - artifactBudget;
        bool hasSeparateImplementation =
            !preparation.Shared
            && !preparation.ImplementationAssets.IsEmpty;
        long groupBudget = hasSeparateImplementation
            ? roleBudget / 2
            : roleBudget;
        ValidateAssets(
            preparation.SurfaceAssets,
            groupBudget,
            preparation.Options);
        if (hasSeparateImplementation)
        {
            ValidateAssets(
                preparation.ImplementationAssets,
                groupBudget,
                preparation.Options);
        }

        return new ArtifactBackedBudgets(
            artifactBudget,
            groupBudget);
    }

    static ArtifactSetSessionLimits ArtifactLimits(
        int artifactCount,
        long artifactBudget,
        PackageAssemblyContextRealizationOptions options)
    {
        long artifactBytes = Math.Min(
            artifactBudget,
            options.MaxAssemblyEntryBytes);

        return new ArtifactSetSessionLimits
        {
            MaxArtifacts = artifactCount,
            MaxArtifactBytes = artifactBytes,
            MaxRetainedBytes = artifactBudget,
        };
    }

    static async ValueTask<PackageArtifactPublication>
        PublishPackageArtifactsAsync(
            ArtifactSetSession session,
            ImmutableArray<PackageArtifactSource> artifacts,
            CancellationToken cancellationToken)
    {
        ImmutableArray<ArtifactContribution> acquired = [];
        await session.AddRequiredAcquisitionAsync(
                (scope, generationEnd) =>
                {
                    var result =
                        ImmutableArray.CreateBuilder<ArtifactContribution>(
                            artifacts.Length);
                    foreach (PackageArtifactSource artifact in artifacts)
                    {
                        generationEnd.ThrowIfCancellationRequested();
                        result.Add(scope.Register(
                            artifact.Provenance,
                            artifact.OpenRead,
                            kind: "package-assembly"));
                    }

                    acquired = result.MoveToImmutable();
                    return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                        new ArtifactAcquisitionOutcome.Acquired(
                            acquired,
                            ArtifactAcquisitionLeases.None));
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var admissions =
            new Dictionary<ArtifactIdentity, ArtifactAssemblyProjectionOutcome>();
        ArtifactSetPublicationOutcome publication =
            await session.SealWithProjectionAsync(
                (view, token) =>
                {
                    admissions.Add(
                        view.Artifact,
                        ArtifactAssemblyInspection.Project(view, token));
                    // Non-projectable images still publish as compatibility carriers.
                    return null;
                },
                cancellationToken).ConfigureAwait(false);
        if (publication is ArtifactSetPublicationOutcome.NotPublished rejected)
            return new PackageArtifactPublication(null, [], rejected);

        ArtifactQueryLease lease = session.IssueLease(
            session.CreateQueryAuthorization());
        try
        {
            return new PackageArtifactPublication(
                lease,
                [.. acquired.Select(artifact => new ProjectedPackageArtifact(
                    session.GetContentReference(artifact.Descriptor.Identity, lease),
                    admissions[artifact.Descriptor.Identity]))],
                null);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    static ImmutableArray<RoleAssembly> CreateArtifactRole(
        ImmutableArray<RoleAsset> assets,
        IReadOnlyDictionary<RoleAsset, ProjectedPackageArtifact>
            contentByAsset,
        CancellationToken cancellationToken)
    {
        var result =
            ImmutableArray.CreateBuilder<RoleAssembly>(assets.Length);
        for (int index = 0; index < assets.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RoleAsset asset = assets[index];
            ResolvedAssemblyReference assembly = CreatePackageArtifactAssembly(
                contentByAsset[asset],
                PackageProvenance(asset),
                index,
                out bool identityDecoded);
            result.Add(new RoleAssembly(
                asset.PackageIndex,
                asset.Package,
                asset.Asset,
                assembly,
                identityDecoded));
        }

        return result.MoveToImmutable();
    }

    static ResolvedAssemblyReference CreatePackageArtifactAssembly(
        ProjectedPackageArtifact artifact,
        AssemblyResolutionProvenance provenance,
        int index,
        out bool identityDecoded)
    {
        ArtifactContentReference content = artifact.Content;
        if (artifact.Projection is ArtifactAssemblyProjectionOutcome.Projected projected
            && !string.IsNullOrWhiteSpace(projected.Value.Identity.Name))
        {
            identityDecoded = true;
            return ResolvedAssemblyReference.CreateFromArtifactProjection(
                content.Registration, projected.Value, content.OpenRead, provenance);
        }

        // Preserve partially decoded identity as well as Metadata's rejection carrier.
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromArtifactWithFallbackIdentity(
                content.Registration, content.OpenRead, RejectionCarrierIdentity(index),
                provenance, out bool usedFallbackIdentity);
        identityDecoded = !usedFallbackIdentity;
        return assembly;
    }

    static ImmutableArray<RoleAsset> DistinctSelectedAssets(
        PackageRoleRealizationPreparation preparation)
    {
        var seen = new HashSet<RoleAsset>(
            RoleAssetIdentityComparer.Instance);
        var result = ImmutableArray.CreateBuilder<RoleAsset>();
        Add(preparation.SurfaceAssets);
        Add(preparation.ImplementationAssets);
        return result.ToImmutable();

        void Add(ImmutableArray<RoleAsset> assets)
        {
            foreach (RoleAsset asset in assets)
            {
                if (seen.Add(asset))
                    result.Add(asset);
            }
        }
    }

    static ImmutableArray<AssemblyContextGroup> DependentGroups(
        PackageAssemblyContextRealization realization)
    {
        AssemblyContextGroup surface = realization.SurfaceGroup;
        AssemblyContextGroup? implementation =
            realization.ImplementationGroup;
        return implementation is null
            || ReferenceEquals(surface, implementation)
                ? [surface]
                : [surface, implementation];
    }

    static InvalidOperationException PublicationFailure(
        ArtifactSetPublicationOutcome.NotPublished publication)
    {
        var failure = new InvalidOperationException(
            "Artifact-backed package realization could not publish the selected assets.");
        if (publication.Failures.Count > 0)
        {
            failure.Data[
                "DotnetInspector.Artifacts.Workspaces.AdmissionFailures"] =
                publication.Failures;
        }

        return failure;
    }

    static async ValueTask CleanupFailedArtifactRealizationAsync(
        IDisposable? realization,
        ArtifactQueryLease? queryLease,
        ArtifactSetSession session,
        Exception primary)
    {
        var failures = new List<Exception>();
        try
        {
            realization?.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        try
        {
            queryLease?.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        failures.AddRange(session.CleanupFailures);
        if (failures.Count > 0)
        {
            if (primary.Data[
                    "DotnetInspector.Artifacts.Workspaces.CleanupFailures"]
                is IEnumerable<Exception> previous)
                failures.InsertRange(0, previous);
            primary.Data[
                "DotnetInspector.Artifacts.Workspaces.CleanupFailures"] =
                failures.AsReadOnly();
        }
    }

    sealed record PackageArtifactSource(
        IArtifactProvenance Provenance,
        Func<CancellationToken, Stream> OpenRead);

    sealed record ProjectedPackageArtifact(
        ArtifactContentReference Content,
        ArtifactAssemblyProjectionOutcome Projection);

    sealed record PackageArtifactPublication(
        ArtifactQueryLease? Lease,
        ImmutableArray<ProjectedPackageArtifact> Artifacts,
        ArtifactSetPublicationOutcome.NotPublished? Rejection);

    readonly record struct ArtifactBackedBudgets(
        long ArtifactBudget,
        long GroupBudget);
}
