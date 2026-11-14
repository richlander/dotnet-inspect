using System.Collections.Immutable;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Sections;

public static partial class ExactTypeInspectionOperation
{
    /// <summary>
    /// Resolves the first exact Type through the House's frozen compile
    /// selection without admitting a package Root into a Workspace.
    /// </summary>
    public static async Task<TResult>
        ExecuteFirstAvailableSurfaceWithLiveTargetAsync<TResult>(
            IReadOnlyList<ExactTypeInspectionRequest> requests,
            PackageHouse house,
            Func<CancellationToken, PackageSourceOperationLease>
                issueSourceOperation,
            ApiSurfaceScope scope,
            Func<
                InspectionEnvelope<ExactTypeInspectionResult>,
                SelectedContextExactTypeLiveTarget?,
                CancellationToken,
                ValueTask<TResult>> use,
            CancellationToken cancellationToken = default)
    {
        ExactTypeInspectionRequest first =
            ValidateCandidateRequests(requests);
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(issueSourceOperation);
        ArgumentNullException.ThrowIfNull(use);
        cancellationToken.ThrowIfCancellationRequested();

        PackageSourceOperationLease sourceOperation =
            issueSourceOperation(cancellationToken);
        PackageHouseRequest houseRequest;
        try
        {
            PackageEndpointScopeRequest endpointRequest =
                PackageEndpointScopeRequest.ForCompileLibraries(
                    PackageSourceCoordinate.Create(
                        first.PackageId,
                        first.Version),
                    first.TargetFramework,
                    PackageAssetDemand.Surface);
            PackageHouseOperation operation =
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Realize,
                    sourceOperation.RequestTimeout,
                    sourceOperation.OperationTimeout);
            houseRequest = PackageHouseRequest.CompileLibraries(
                endpointRequest.Demand,
                operation,
                endpointRequest.Target,
                PackageAssetDemand.Surface);
        }
        catch
        {
            sourceOperation.Dispose();
            throw;
        }

        PackageHouseSettlement settlement =
            await house.ExecuteAsync(houseRequest, sourceOperation)
                .ConfigureAwait(false);
        PackageHouseRootContributionOutcome contributionOutcome =
            PackageHouseRootContributionAdapter.Create(settlement);
        if (contributionOutcome
                is PackageHouseRootContributionOutcome.Contributed
                    contributed
            && contributed.Contribution.SelectionReceipt.Selection
                is { IsSelected: true } selection
            && TryCreateSurfaceAssemblies(
                    contributed.Contribution,
                    selection,
                    cancellationToken,
                    out ImmutableArray<ResolvedAssemblyReference> assemblies))
        {
            SurfaceExecution<TResult> result =
                await TryExecuteSurfaceAsync(
                    contributed.Contribution,
                    assemblies,
                    requests,
                    scope,
                    use,
                    cancellationToken)
                .ConfigureAwait(false);
            if (result is SurfaceExecution<TResult>.Completed completed)
                return completed.Value;
        }

        return await ExecuteFirstAvailableWithLiveTargetAsync(
                requests,
                house,
                issueSourceOperation(cancellationToken),
                scope,
                use,
                cancellationToken)
            .ConfigureAwait(false);
    }

    static bool TryCreateSurfaceAssemblies(
        PackageHouseRootContribution contribution,
        PackageCompileAssetSelection selection,
        CancellationToken cancellationToken,
        out ImmutableArray<ResolvedAssemblyReference> assemblies)
    {
        var selected =
            ImmutableArray.CreateBuilder<ResolvedAssemblyReference>(
                selection.Assets.Count);
        var defaults = new PackageAssemblyContextRealizationOptions();
        PackageRootBinding binding = contribution.Binding;
        if (!ReferenceEquals(
                binding.Root.Content.GenerationIdentity,
                binding.ContentGenerationIdentity))
        {
            assemblies = [];
            return false;
        }

        foreach (PackageCompileAsset asset in selection.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ResolvedAssemblyReference? assembly =
                    CreateSurfaceAssembly(
                        binding,
                        asset,
                        defaults.MaxAssemblyEntryBytes);
                if (assembly is null)
                {
                    assemblies = [];
                    return false;
                }
                selected.Add(assembly);
            }
            catch (Exception failure) when (
                failure is IOException
                    or BadImageFormatException
                    or UnsupportedMetadataFormatException
                    or OverflowException)
            {
                assemblies = [];
                return false;
            }
        }

        assemblies = selected.MoveToImmutable();
        return !assemblies.IsEmpty;
    }

    static ResolvedAssemblyReference? CreateSurfaceAssembly(
        PackageRootBinding binding,
        PackageCompileAsset asset,
        long maxExpandedBytes)
    {
        IPackageContent content = binding.Root.Content;
        if (content is IPackageContentEntryManifest manifest
            && (!manifest.TryGetEntryLength(
                    asset.Path,
                    out long declaredLength)
                || declaredLength < 0
                || declaredLength > maxExpandedBytes))
        {
            throw new IOException(
                $"The selected package entry '{asset.Path}' exceeds "
                    + "the Surface assembly limit.");
        }

        AssemblyResolutionProvenance provenance =
            AssemblyResolutionProvenance.Package(
                binding.Root.PackageId,
                binding.Root.PackageVersion,
                asset.TargetFramework,
                rid: null,
                assetPath: asset.Path);
        if (content.RootPath is { } root)
        {
            string path = StorePath.ResolveUnderRoot(
                root,
                asset.Path);
            return ResolvedAssemblyReference.CreateFromPathIfManaged(
                path,
                provenance);
        }

        byte[] image = ReadSelectedEntry(
            binding,
            asset,
            maxExpandedBytes);
        return ResolvedAssemblyReference.CreateFromStreamIfManaged(
            () => new MemoryStream(
                image,
                writable: false),
            provenance,
            lastWriteTimeUtc: null,
            assetFileName: Path.GetFileName(asset.Path));
    }

    static async Task<SurfaceExecution<TResult>>
        TryExecuteSurfaceAsync<TResult>(
        PackageHouseRootContribution contribution,
        ImmutableArray<ResolvedAssemblyReference> assemblies,
        IReadOnlyList<ExactTypeInspectionRequest> requests,
        ApiSurfaceScope scope,
        Func<
            InspectionEnvelope<ExactTypeInspectionResult>,
            SelectedContextExactTypeLiveTarget?,
            CancellationToken,
            ValueTask<TResult>> use,
        CancellationToken cancellationToken)
    {
        await using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRoles roles =
            workspace.CreatePackageAssemblyContextRoles(
                assemblies,
                implementationAssemblies: null,
                correspondences: []);
        ExactTypeInspectionRequest first = requests[0];
        FirstAvailableExactTypeInspectionExecution retained =
            ExecuteFirstAvailableExecutionCore(
                workspace,
                Loaded(
                    workspace.Identity,
                    roles.SurfaceGroup,
                    roles.SurfaceParticipants,
                    contribution.Binding,
                    first),
                requests,
                scope,
                projectionLimits: null);
        if (retained.Execution.Result.Outcome
                == ExactTypeInspectionOutcome.Unavailable
            || retained.Execution.Target is null
                && retained.Execution.Result.Outcome
                    == ExactTypeInspectionOutcome.Available)
        {
            return SurfaceExecution<TResult>.Fallback.Instance;
        }

        TResult result = await use(
                Envelope(
                    retained.Execution.Result,
                    retained.Request),
                LiveTarget(retained.Execution.Target),
                cancellationToken)
            .ConfigureAwait(false);
        return new SurfaceExecution<TResult>.Completed(result);
    }

    static byte[] ReadSelectedEntry(
        PackageRootBinding binding,
        PackageCompileAsset asset,
        long maxExpandedBytes)
    {
        if (!binding.Root.Content.TryOpenEntry(
                asset.Path,
                maxExpandedBytes,
                out Stream? stream))
        {
            throw new IOException(
                $"The selected package entry '{asset.Path}' is unavailable.");
        }

        using (stream)
        using (var image = new MemoryStream())
        {
            stream.CopyTo(image);
            return image.ToArray();
        }
    }

    abstract record SurfaceExecution<TResult>
    {
        internal sealed record Completed(TResult Value)
            : SurfaceExecution<TResult>;

        internal sealed record Fallback
            : SurfaceExecution<TResult>
        {
            internal static Fallback Instance { get; } = new();
        }
    }
}
