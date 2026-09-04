using DotnetInspector.Queries;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Inspectors;

internal sealed record PackageIntegrationAssembly(
    string Path,
    string? TargetFramework,
    string? ContextKey = null);

internal sealed class PackageIntegrationAcquisition
{
    readonly string? _packageId;
    readonly string? _packageVersion;

    PackageIntegrationAcquisition(
        string? packageId,
        string? packageVersion)
    {
        _packageId = packageId;
        _packageVersion = packageVersion;
    }

    internal static PackageIntegrationAcquisition Remote(
        string packageId,
        string packageVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        return new PackageIntegrationAcquisition(
            packageId.Trim(),
            packageVersion.Trim());
    }

    internal static PackageIntegrationAcquisition Remote(
        PackageExtractionResult resolution,
        string fallbackPackageId,
        string fallbackPackageVersion)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return Remote(
            resolution.PackageName ?? fallbackPackageId,
            resolution.Version ?? fallbackPackageVersion);
    }

    internal static PackageIntegrationAcquisition Local(
        string? packageId,
        string? packageVersion)
    {
        string? normalizedId = NormalizePackageId(packageId);
        string? normalizedVersion =
            NuGetVersion.TryParse(packageVersion?.Trim(), out var parsed)
                ? parsed.ToNormalizedString()
                : null;
        return normalizedId is not null && normalizedVersion is not null
            ? new PackageIntegrationAcquisition(
                normalizedId,
                normalizedVersion)
            : new PackageIntegrationAcquisition(null, null);
    }

    internal AssemblyResolutionProvenance CreateProvenance(
        string? targetFramework) =>
        _packageId is not null && _packageVersion is not null
            ? AssemblyResolutionProvenance.Package(
                _packageId,
                _packageVersion,
                targetFramework,
                rid: null)
            : AssemblyResolutionProvenance.Local(
                "local package archive");

    static string? NormalizePackageId(string? packageId)
    {
        string? candidate = packageId?.Trim();
        if (candidate is not { Length: > 0 and <= 100 })
            return null;

        bool previousWasSeparator = false;
        for (int index = 0; index < candidate.Length; index++)
        {
            char character = candidate[index];
            bool asciiAlphaNumeric =
                character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9';
            bool word = asciiAlphaNumeric || character == '_';
            bool separator = character is '.' or '-';
            if (!word && !separator)
            {
                return null;
            }

            if (separator
                && (index == 0
                    || index == candidate.Length - 1
                    || previousWasSeparator))
            {
                return null;
            }

            previousWasSeparator = separator;
        }

        return candidate;
    }
}

/// <summary>
/// Owns the binding-consistent package groups used by one all-library
/// Integrations request.
/// </summary>
internal sealed class PackageIntegrationsWorkspace :
    IDisposable,
    IAsyncDisposable
{
    readonly InspectionWorkspace _workspace;
    readonly PackageAssemblyContextRealization? _packageRealization;
    readonly Dictionary<string, ParticipantResult> _participants;
    readonly Dictionary<string, string> _preflightFailures;
    readonly bool _includeIntegrationOpportunities;
    readonly bool _asynchronous;

    PackageIntegrationsWorkspace(
        InspectionWorkspace workspace,
        PackageAssemblyContextRealization? packageRealization,
        Dictionary<string, ParticipantResult> participants,
        Dictionary<string, string> preflightFailures,
        int contextGroupCount,
        bool includeIntegrationOpportunities,
        bool asynchronous)
    {
        _workspace = workspace;
        _packageRealization = packageRealization;
        _participants = participants;
        _preflightFailures = preflightFailures;
        _includeIntegrationOpportunities =
            includeIntegrationOpportunities;
        _asynchronous = asynchronous;
        ContextGroupCount = contextGroupCount;
    }

    internal int ContextGroupCount { get; }

    internal long RetainedImageBytes =>
        _participants.Values
            .SelectMany(static participant =>
                new[]
                {
                    participant.SelectedGroup,
                    participant.QueryGroup,
                })
            .Distinct()
            .Sum(static group => group.RetainedImageBytes);

    internal static PackageIntegrationsWorkspace Create(
        IEnumerable<PackageIntegrationAssembly> assemblies,
        string packageName,
        string packageVersion,
        bool includeIntegrationOpportunities = false) =>
        Create(
            assemblies,
            PackageIntegrationAcquisition.Remote(
                packageName,
                packageVersion),
            includeIntegrationOpportunities:
                includeIntegrationOpportunities);

    internal static PackageIntegrationsWorkspace Create(
        IEnumerable<PackageIntegrationAssembly> assemblies,
        PackageIntegrationAcquisition acquisition,
        long? maxRetainedImageBytes = null,
        bool includeIntegrationOpportunities = false)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(acquisition);

        var workspace = new InspectionWorkspace();
        try
        {
            var results = new Dictionary<string, ParticipantResult>(
                StringComparer.Ordinal);
            var preflightFailures = new Dictionary<string, string>(
                StringComparer.Ordinal);
            int contextGroupCount = 0;
            foreach (IGrouping<string, PackageIntegrationAssembly> context
                in assemblies.GroupBy(
                    static assembly =>
                        assembly.ContextKey
                        ?? assembly.TargetFramework
                        ?? "",
                    StringComparer.OrdinalIgnoreCase))
            {
                List<Root> roots = [];
                foreach (PackageIntegrationAssembly assembly in context)
                {
                    var provenance = acquisition.CreateProvenance(
                        assembly.TargetFramework);
                    ResolvedAssemblyReference? reference;
                    try
                    {
                        reference =
                            ResolvedAssemblyReference
                                .CreateFromPathIfManaged(
                                    assembly.Path,
                                    provenance);
                    }
                    catch (Exception ex) when (
                        ex is BadImageFormatException
                            or ArgumentOutOfRangeException
                            or OverflowException)
                    {
                        preflightFailures.Add(
                            Path.GetFullPath(assembly.Path),
                            "The selected image contains invalid metadata.");
                        continue;
                    }
                    catch (Exception ex) when (
                        ex is IOException
                            or UnauthorizedAccessException
                            or NotSupportedException
                            or ObjectDisposedException)
                    {
                        preflightFailures.Add(
                            Path.GetFullPath(assembly.Path),
                            "The selected image could not be read.");
                        continue;
                    }

                    if (reference is null)
                        continue;

                    var policy = new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(
                            reference.Path!)
                        {
                            TargetFramework =
                                assembly.TargetFramework,
                        });
                    roots.Add(new Root(assembly, reference, policy));
                }

                if (roots.Count == 0)
                    continue;

                var groupPolicy =
                    new SourceRelativeAssemblyGroupBindingPolicy(
                        roots.Select(static root =>
                            (root.Reference,
                                (IAssemblyBindingPolicy)root.Policy)));
                List<AssemblyContextParticipant> participants =
                [
                    .. roots.Select(root =>
                        new AssemblyContextParticipant(
                            root.Reference,
                            groupPolicy)),
                ];
                AssemblyContextGroup group =
                    workspace.CreateAssemblyContextGroup(
                        participants,
                        maxRetainedImageBytes is long maxBytes
                            ? new AssemblyContextGroupOptions
                            {
                                MaxRetainedImageBytes = maxBytes,
                            }
                            : null);

                for (int index = 0; index < roots.Count; index++)
                {
                    Root root = roots[index];
                    results.Add(
                        Path.GetFullPath(root.Input.Path),
                        new ParticipantResult(
                            group,
                            participants[index],
                            group,
                            participants[index]));
                }

                contextGroupCount++;
            }

            return new PackageIntegrationsWorkspace(
                workspace,
                packageRealization: null,
                results,
                preflightFailures,
                contextGroupCount,
                includeIntegrationOpportunities,
                asynchronous: false);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    internal static async ValueTask<PackageIntegrationsWorkspace>
        CreateArtifactBackedAsync(
            IEnumerable<PackageIntegrationAssembly> assemblies,
            string extractionRoot,
            PackageRootBinding package,
            bool includeIntegrationOpportunities = false,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);
        ArgumentNullException.ThrowIfNull(package);

        PackageIntegrationAssembly[] requested = [.. assemblies];
        InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        try
        {
            PackageAssemblyContextRealization realization =
                await workspace.RealizePackageAssemblyContextRolesAsync(
                        package,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            var results = new Dictionary<string, ParticipantResult>(
                StringComparer.Ordinal);
            var preflightFailures = new Dictionary<string, string>(
                StringComparer.Ordinal);
            if (!realization.HasAssemblyContexts)
            {
                foreach (PackageIntegrationAssembly assembly in requested)
                {
                    preflightFailures.Add(
                        Path.GetFullPath(assembly.Path),
                        "The selected library has no artifact-backed compile role.");
                }

                return new PackageIntegrationsWorkspace(
                    workspace,
                    realization,
                    results,
                    preflightFailures,
                    contextGroupCount: 0,
                    includeIntegrationOpportunities,
                    asynchronous: true);
            }

            Dictionary<string, ParticipantResult> roles =
                ArtifactRoles(realization);
            foreach (PackageIntegrationAssembly assembly in requested)
            {
                string fullPath = Path.GetFullPath(assembly.Path);
                string packagePath = Path.GetRelativePath(
                        extractionRoot,
                        fullPath)
                    .Replace('\\', '/');
                if (roles.TryGetValue(
                        packagePath,
                        out ParticipantResult? participant))
                {
                    results.Add(fullPath, participant);
                }
                else
                {
                    preflightFailures.Add(
                        fullPath,
                        "The selected library is outside the artifact-backed package roles.");
                }
            }

            int contextGroupCount = results.Values
                .SelectMany(static participant =>
                    new[]
                    {
                        participant.SelectedGroup,
                        participant.QueryGroup,
                    })
                .Distinct()
                .Count();
            return new PackageIntegrationsWorkspace(
                workspace,
                realization,
                results,
                preflightFailures,
                contextGroupCount,
                includeIntegrationOpportunities,
                asynchronous: true);
        }
        catch (Exception failure)
        {
            try
            {
                await workspace.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    failure,
                    cleanupFailure);
            }

            throw;
        }
    }

    internal static async ValueTask<PackageIntegrationsWorkspace?>
        TryCreateArtifactBackedAsync(
            IEnumerable<PackageIntegrationAssembly> assemblies,
            string extractionRoot,
            PackageRootBinding package,
            bool includeIntegrationOpportunities = false,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);
        ArgumentNullException.ThrowIfNull(package);

        PackageIntegrationAssembly[] requested = [.. assemblies];
        PackageCompileAssetSelection selection =
            package.Root.AssetSelection;
        if (!selection.IsSelected
            || !HasExactSurfaceSelection(
                requested,
                extractionRoot,
                selection.Assets))
        {
            return null;
        }

        try
        {
            return await CreateArtifactBackedAsync(
                    requested,
                    extractionRoot,
                    package,
                    includeIntegrationOpportunities,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PackageAssemblyRoleCorrespondenceException)
        {
            return null;
        }
    }

    static bool HasExactSurfaceSelection(
        IReadOnlyList<PackageIntegrationAssembly> requested,
        string extractionRoot,
        IReadOnlyList<PackageCompileAsset> surfaceAssets)
    {
        if (requested.Count != surfaceAssets.Count)
            return false;

        var requestedPaths = new HashSet<string>(
            requested.Select(assembly =>
                Path.GetRelativePath(
                        extractionRoot,
                        Path.GetFullPath(assembly.Path))
                    .Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
        return requestedPaths.Count == requested.Count
            && surfaceAssets.All(asset =>
                requestedPaths.Remove(asset.Path))
            && requestedPaths.Count == 0;
    }

    static Dictionary<string, ParticipantResult> ArtifactRoles(
        PackageAssemblyContextRealization realization)
    {
        var results = new Dictionary<string, ParticipantResult>(
            StringComparer.OrdinalIgnoreCase);
        foreach (PackageAssemblyRoleParticipant surface
            in realization.SurfaceParticipants)
        {
            PackageAssemblyRoleParticipant queryParticipant =
                realization.ImplementationParticipant(surface)
                ?? surface;
            AssemblyContextGroup queryGroup =
                ReferenceEquals(queryParticipant, surface)
                    ? realization.SurfaceGroup
                    : realization.ImplementationGroup!;
            results.Add(
                surface.Asset.Path,
                new ParticipantResult(
                    realization.SurfaceGroup,
                    surface.Participant,
                    queryGroup,
                    queryParticipant.Participant));
        }

        if (realization.ImplementationGroup is { } implementationGroup)
        {
            foreach (PackageAssemblyRoleParticipant implementation
                in realization.ImplementationParticipants)
            {
                results.TryAdd(
                    implementation.Asset.Path,
                    new ParticipantResult(
                        implementationGroup,
                        implementation.Participant,
                        implementationGroup,
                        implementation.Participant));
            }
        }

        return results;
    }

    internal async Task<TResult> UseAssemblyAsync<TResult>(
        string path,
        Func<
            ResolvedAssemblyReference?,
            AssemblyIntegrationsEntry?,
            AssemblyIntegrationOpportunitiesEntry?,
            Task<TResult>> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(callback);

        if (!_participants.TryGetValue(
                Path.GetFullPath(path),
                out ParticipantResult? participant))
        {
            return await callback(null, null, null).ConfigureAwait(false);
        }

        if (ReferenceEquals(
                participant.SelectedParticipant,
                participant.QueryParticipant))
        {
            return await ExecuteQueryAsync(
                    participant.QueryGroup,
                    participant.QueryParticipant,
                    callback)
                .ConfigureAwait(false);
        }

        return await AssemblyContextIntegrationsQuery
            .ExecuteParticipantAsync(
                participant.SelectedGroup,
                participant.SelectedParticipant,
                (selectedAssembly, selectedIntegrations) =>
                    selectedAssembly is null
                        ? callback(
                            null,
                            selectedIntegrations,
                            null)
                        : ExecuteQueryAsync(
                            participant.QueryGroup,
                            participant.QueryParticipant,
                            (_, integrations, opportunities) =>
                                callback(
                                    selectedAssembly,
                                    integrations,
                                    opportunities)))
            .ConfigureAwait(false);

        Task<TResult> ExecuteQueryAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant queryParticipant,
            Func<
                ResolvedAssemblyReference?,
                AssemblyIntegrationsEntry?,
                AssemblyIntegrationOpportunitiesEntry?,
                Task<TResult>> consumer) =>
            _includeIntegrationOpportunities
                ? AssemblyContextIntegrationOpportunitiesQuery
                    .ExecuteParticipantAsync(
                        group,
                        queryParticipant,
                        consumer)
                : AssemblyContextIntegrationsQuery
                    .ExecuteParticipantAsync(
                        group,
                        queryParticipant,
                        (retained, integrations) =>
                            consumer(retained, integrations, null));
    }

    internal bool TryGetPreflightFailure(
        string path,
        out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_preflightFailures.TryGetValue(
                Path.GetFullPath(path),
                out string? failure))
        {
            reason = failure;
            return true;
        }

        reason = "";
        return false;
    }

    public void Dispose()
    {
        if (_asynchronous)
        {
            throw new InvalidOperationException(
                "An artifact-backed package Integrations workspace must be disposed asynchronously.");
        }

        _workspace.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_asynchronous)
        {
            Dispose();
            return;
        }

        List<Exception>? failures = null;
        try
        {
            _packageRealization!.Dispose();
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        try
        {
            InspectionWorkspaceCloseReport report =
                await _workspace.CloseAsync().ConfigureAwait(false);
            if (!report.ArtifactSessionCleanupFailures.IsEmpty)
            {
                (failures ??= []).AddRange(
                    report.ArtifactSessionCleanupFailures);
            }
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        if (failures is not null)
        {
            throw new AggregateException(failures);
        }
    }

    sealed record Root(
        PackageIntegrationAssembly Input,
        ResolvedAssemblyReference Reference,
        AssemblyDependencyResolver Policy);

    sealed record ParticipantResult(
        AssemblyContextGroup SelectedGroup,
        AssemblyContextParticipant SelectedParticipant,
        AssemblyContextGroup QueryGroup,
        AssemblyContextParticipant QueryParticipant);
}
