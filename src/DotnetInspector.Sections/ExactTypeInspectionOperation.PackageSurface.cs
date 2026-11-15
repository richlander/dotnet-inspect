using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

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
        => await ExecuteFirstAvailableSurfaceWithLiveTargetAsync(
                requests,
                house,
                issueSourceOperation,
                scope,
                selectedMember: null,
                use,
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Resolves the first exact Type through a selection-only House and uses a
    /// semantic-content House when conservative selection requires the complete
    /// endpoint.
    /// </summary>
    public static async Task<TResult>
        ExecuteFirstAvailableSurfaceWithLiveTargetAsync<TResult>(
            IReadOnlyList<ExactTypeInspectionRequest> requests,
            PackageHouse house,
            Func<PackageHouse> createFallbackHouse,
            Func<CancellationToken, PackageSourceOperationLease>
                issueSourceOperation,
            ApiSurfaceScope scope,
            Func<ExactTypeInspectionRequest, string?>? selectedMember,
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
        ArgumentNullException.ThrowIfNull(createFallbackHouse);
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
            houseRequest = PackageHouseRequest.CompileSelection(
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
                    selectedMember,
                    use,
                    cancellationToken)
                .ConfigureAwait(false);
            if (result is SurfaceExecution<TResult>.Completed completed)
                return completed.Value;
        }

        PackageHouse fallbackHouse =
            createFallbackHouse()
            ?? throw new InvalidOperationException(
                "The complete endpoint requires a fallback House.");
        return await ExecuteFirstAvailableWithLiveTargetAsync(
                requests,
                fallbackHouse,
                issueSourceOperation(cancellationToken),
                scope,
                use,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the first exact Type and optionally narrows surface extraction
    /// to that Type when the selected member cannot require a receiver-projected
    /// extension declaration.
    /// </summary>
    public static async Task<TResult>
        ExecuteFirstAvailableSurfaceWithLiveTargetAsync<TResult>(
            IReadOnlyList<ExactTypeInspectionRequest> requests,
            PackageHouse house,
            Func<CancellationToken, PackageSourceOperationLease>
                issueSourceOperation,
            ApiSurfaceScope scope,
            Func<ExactTypeInspectionRequest, string?>? selectedMember,
            Func<
                InspectionEnvelope<ExactTypeInspectionResult>,
                SelectedContextExactTypeLiveTarget?,
                CancellationToken,
                ValueTask<TResult>> use,
            CancellationToken cancellationToken = default)
        => await ExecuteFirstAvailableSurfaceWithLiveTargetAsync(
                requests,
                house,
                () => house,
                issueSourceOperation,
                scope,
                selectedMember,
                use,
                cancellationToken)
            .ConfigureAwait(false);

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
        Func<ExactTypeInspectionRequest, string?>? selectedMember,
        Func<
            InspectionEnvelope<ExactTypeInspectionResult>,
            SelectedContextExactTypeLiveTarget?,
            CancellationToken,
            ValueTask<TResult>> use,
        CancellationToken cancellationToken)
    {
        if (scope is not (
            ApiSurfaceScope.Public
            or ApiSurfaceScope.IncludeAll))
        {
            return SurfaceExecution<TResult>.Fallback.Instance;
        }

        SurfaceSelection? selection =
            SelectSurface(
                assemblies,
                requests,
                selectedMember,
                cancellationToken);
        if (selection is null)
            return SurfaceExecution<TResult>.Fallback.Instance;

        IAssemblyBindingPolicy bindingPolicy =
            PackageAssemblyContextRoles.CreateSurfaceBindingPolicy(
                assemblies);
        ApiSurface surface;
        try
        {
            using var catalog = new TypeResolutionCatalog();
            ResolutionAwareApiSurfaceOutcome outcome =
                catalog.ExtractApiSurface(
                    selection.Assembly,
                    bindingPolicy,
                    includeAll:
                        scope == ApiSurfaceScope.IncludeAll,
                    includeType:
                        selection.ProjectSelectedType
                            ? handle =>
                                MetadataTokens.GetToken(handle)
                                    == selection.TypeToken
                            : null);
            if (outcome
                is not ResolutionAwareApiSurfaceOutcome.Read read)
            {
                return SurfaceExecution<TResult>.Fallback.Instance;
            }
            surface = read.Surface;
        }
        catch (Exception failure) when (
            failure is IOException
                or BadImageFormatException
                or UnsupportedMetadataFormatException
                or OverflowException)
        {
            return SurfaceExecution<TResult>.Fallback.Instance;
        }

        (ApiType Type, MetadataTypeDefinitionName Definition)[] types =
        [
            .. surface.Types
                .Where(static type => type.DefinitionName is not null)
                .Select(static type => (
                    type,
                    type.DefinitionName
                        ?? throw new InvalidOperationException())),
        ];
        MetadataTypeLookupResult<(
            ApiType Type,
            MetadataTypeDefinitionName Definition)> lookup =
            selection.Request.SelectionKind
                == ExactTypeSelectionKind.DefinitionIdentity
                    ? MetadataTypeLookup.Definition(
                        types,
                        static type => type.Definition,
                        selection.Request.Type)
                    : MetadataTypeLookup.Query(
                        types,
                        static type => type.Definition,
                        selection.Request.Type);
        if (lookup.Matches.Length != 1)
        {
            return SurfaceExecution<TResult>.Fallback.Instance;
        }

        (ApiType type, MetadataTypeDefinitionName definition) =
            lookup.Matches[0];
        Guid moduleVersionId;
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(selection.Assembly);
            moduleVersionId = session.ModuleVersionId();
        }
        catch (Exception failure) when (
            failure is IOException
                or BadImageFormatException
                or UnsupportedMetadataFormatException
                or OverflowException)
        {
            return SurfaceExecution<TResult>.Fallback.Instance;
        }

        var assemblyIdentity = new ExactTypeAssemblyIdentity(
            selection.Assembly.Identity,
            moduleVersionId);
        var inspection = new ExactTypeInspectionResult(
            ExactTypeInspectionOutcome.Available,
            selection.Request.Type,
            lookup.MatchedName ?? definition.ToEscapedFullName(),
            ExactTypeApi.From(type),
            assemblyIdentity,
            assemblyIdentity,
            ForwardingHops: [],
            Suggestions: [],
            [
                .. surface.InspectionFailures.Select(
                    ExactTypeApiInspectionFailure.From),
            ],
            Failures: []);
        string? packageAssetPath =
            selection.Assembly.Provenance
                is AssemblyResolutionProvenance.PackageAsset provenance
                    ? provenance.AssetPath
                    : null;
        var target = new SelectedContextExactTypeLiveTarget(
            surface,
            type,
            selection.Assembly,
            AssemblyBindingOccurrence.Seed(selection.Assembly),
            bindingPolicy,
            selection.Assembly.Path,
            packageAssetPath,
            contribution.Binding.Root.Content.RootPath);
        TResult result = await use(
                Envelope(
                    inspection,
                    selection.Request),
                target,
                cancellationToken)
            .ConfigureAwait(false);
        return new SurfaceExecution<TResult>.Completed(result);
    }

    static SurfaceSelection? SelectSurface(
        ImmutableArray<ResolvedAssemblyReference> assemblies,
        IReadOnlyList<ExactTypeInspectionRequest> requests,
        Func<ExactTypeInspectionRequest, string?>? selectedMember,
        CancellationToken cancellationToken)
    {
        foreach (ExactTypeInspectionRequest request in requests)
        {
            ResolvedAssemblyReference? selected = null;
            int selectedTypeToken = 0;
            bool projectSelectedType = false;
            foreach (ResolvedAssemblyReference assembly in assemblies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using AssemblyInspectionSession session =
                        AssemblyInspectionSession.Open(assembly);
                    TypeDefinitionLookupProbe probe =
                        session.MethodBodies.ProbeUniqueLookupTypeToken(
                            request.Type);
                    if (probe.Outcome
                        == TypeDefinitionLookupProbeOutcome.Inconclusive)
                    {
                        return null;
                    }
                    if (probe.Outcome
                        == TypeDefinitionLookupProbeOutcome.Absent)
                    {
                        continue;
                    }
                    int typeToken = probe.TypeToken;
                    string? member = selectedMember?.Invoke(request);
                    projectSelectedType =
                        !string.IsNullOrWhiteSpace(member)
                        && member.IndexOfAny(['*', '?']) < 0
                        && !session.MethodBodies
                            .DeclaresExtensionMethodOutsideType(
                                member,
                                typeToken);
                    selectedTypeToken = typeToken;
                }
                catch (Exception failure) when (
                    failure is IOException
                        or BadImageFormatException
                        or UnsupportedMetadataFormatException
                        or OverflowException)
                {
                    return null;
                }

                if (selected is not null)
                    return null;
                selected = assembly;
            }

            if (selected is not null)
            {
                return new(
                    request,
                    selected,
                    selectedTypeToken,
                    projectSelectedType);
            }
        }

        return null;
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

    sealed record SurfaceSelection(
        ExactTypeInspectionRequest Request,
        ResolvedAssemblyReference Assembly,
        int TypeToken,
        bool ProjectSelectedType);
}
