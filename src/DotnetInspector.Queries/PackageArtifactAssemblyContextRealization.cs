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
            ImmutableArray<AcquiredRoleArtifact> acquired =
                await AcquirePackageArtifactsAsync(
                        session,
                        package,
                        artifacts,
                        preparation.Options,
                        cancellationToken)
                    .ConfigureAwait(false);
            var admissionByArtifact =
                new Dictionary<ArtifactIdentity, ArtifactAssemblyProjectionOutcome>();
            ArtifactSetPublicationOutcome publication =
                await session.SealWithProjectionAsync(
                        (view, token) =>
                        {
                            admissionByArtifact.Add(
                                view.Artifact,
                                ArtifactAssemblyInspection.Project(view, token));
                            // Non-projectable images still publish as compatibility carriers.
                            return null;
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            if (publication
                is ArtifactSetPublicationOutcome.NotPublished rejected)
            {
                throw PublicationFailure(rejected);
            }

            ArtifactQueryAuthorization authorization =
                session.CreateQueryAuthorization();
            queryLease = session.IssueLease(authorization);
            Dictionary<RoleAsset, ArtifactContentReference> contentByAsset =
                PublishedContentByAsset(
                    session,
                    queryLease,
                    acquired);
            ImmutableArray<RoleAssembly> surfaceRole =
                CreateArtifactRole(
                    preparation.SurfaceAssets,
                    contentByAsset,
                    admissionByArtifact,
                    cancellationToken);
            ImmutableArray<RoleAssembly> implementationRole =
                preparation.Shared
                    ? surfaceRole
                    : CreateArtifactRole(
                        preparation.ImplementationAssets,
                        contentByAsset,
                        admissionByArtifact,
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

    static async ValueTask<ImmutableArray<AcquiredRoleArtifact>>
        AcquirePackageArtifactsAsync(
            ArtifactSetSession session,
            PackageRootBinding package,
            ImmutableArray<RoleAsset> artifacts,
            PackageAssemblyContextRealizationOptions options,
            CancellationToken cancellationToken)
    {
        ImmutableArray<AcquiredRoleArtifact> acquired = [];
        await session.AddRequiredAcquisitionAsync(
                (scope, generationEnd) =>
                {
                    var result =
                        ImmutableArray.CreateBuilder<AcquiredRoleArtifact>(
                            artifacts.Length);
                    foreach (RoleAsset artifact in artifacts)
                    {
                        generationEnd.ThrowIfCancellationRequested();
                        var provenance =
                            new PackageAssemblyArtifactProvenance(
                                package.Coordinate,
                                package.ContentGenerationIdentity,
                                package.SelectionIdentity,
                                artifact.Asset);
                        ArtifactContribution contribution = scope.Register(
                            provenance,
                            token => OpenEntry(
                                artifact,
                                options.MaxAssemblyEntryBytes,
                                token),
                            kind: "package-assembly");
                        result.Add(new AcquiredRoleArtifact(
                            artifact,
                            contribution));
                    }

                    acquired = result.MoveToImmutable();
                    return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                        new ArtifactAcquisitionOutcome.Acquired(
                            acquired.Select(entry => entry.Contribution),
                            ArtifactAcquisitionLeases.None));
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return acquired;
    }

    static Dictionary<RoleAsset, ArtifactContentReference>
        PublishedContentByAsset(
            ArtifactSetSession session,
            ArtifactQueryLease queryLease,
            ImmutableArray<AcquiredRoleArtifact> acquired)
    {
        var result = new Dictionary<RoleAsset, ArtifactContentReference>(
            acquired.Length,
            RoleAssetIdentityComparer.Instance);
        foreach (AcquiredRoleArtifact artifact in acquired)
        {
            result.Add(
                artifact.Asset,
                session.GetContentReference(
                    artifact.Contribution.Descriptor.Identity,
                    queryLease));
        }

        return result;
    }

    static ImmutableArray<RoleAssembly> CreateArtifactRole(
        ImmutableArray<RoleAsset> assets,
        IReadOnlyDictionary<RoleAsset, ArtifactContentReference>
            contentByAsset,
        IReadOnlyDictionary<ArtifactIdentity, ArtifactAssemblyProjectionOutcome>
            admissionByArtifact,
        CancellationToken cancellationToken)
    {
        var result =
            ImmutableArray.CreateBuilder<RoleAssembly>(assets.Length);
        for (int index = 0; index < assets.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RoleAsset asset = assets[index];
            ArtifactContentReference content = contentByAsset[asset];
            ResolvedAssemblyReference assembly;
            bool usedFallbackIdentity;
            if (admissionByArtifact[content.Registration.Artifact]
                is ArtifactAssemblyProjectionOutcome.Projected projected
                && !string.IsNullOrWhiteSpace(projected.Value.Identity.Name))
            {
                assembly = ResolvedAssemblyReference.CreateFromArtifactProjection(
                    content.Registration,
                    projected.Value,
                    content.OpenRead,
                    PackageProvenance(asset));
                usedFallbackIdentity = false;
            }
            else
            {
                // Compatibility carriers can retain partially decoded identity,
                // including an assembly name whose MVID was empty or unreadable.
                assembly = ResolvedAssemblyReference.CreateFromArtifactWithFallbackIdentity(
                    content.Registration,
                    content.OpenRead,
                    RejectionCarrierIdentity(index),
                    PackageProvenance(asset),
                    out usedFallbackIdentity);
            }
            result.Add(new RoleAssembly(
                asset.PackageIndex,
                asset.Package,
                asset.Asset,
                assembly,
                IdentityDecoded: !usedFallbackIdentity));
        }

        return result.MoveToImmutable();
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
        PackageAssemblyContextRealization? realization,
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
            primary.Data[
                "DotnetInspector.Artifacts.Workspaces.CleanupFailures"] =
                failures.AsReadOnly();
        }
    }

    sealed record AcquiredRoleArtifact(
        RoleAsset Asset,
        ArtifactContribution Contribution);

    readonly record struct ArtifactBackedBudgets(
        long ArtifactBudget,
        long GroupBudget);
}
