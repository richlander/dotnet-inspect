using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// CLI host policy for the two endpoints of a pairwise
/// <c>diff --package ID@A..B</c> API view opened as package endpoint scopes
/// (docs/design/package-endpoint-scope.md, adoption step 2): ranged House
/// access, the per-authority durable store, and the admission rule that keeps
/// the output identical to the legacy extraction path.
/// </summary>
internal sealed class PackageEndpointDiffSession : IAsyncDisposable
{
    readonly DesktopPackageSourceComposition _composition;
    readonly SearchPackageStores _stores;
    bool _closed;

    PackageEndpointDiffSession(
        DesktopPackageSourceComposition composition,
        SearchPackageStores stores,
        PackageEndpointScope from,
        PackageEndpointScope to,
        bool referencesClose)
    {
        _composition = composition;
        _stores = stores;
        From = from;
        To = to;
        ReferencesClose = referencesClose;
    }

    internal PackageEndpointScope From { get; }

    internal PackageEndpointScope To { get; }

    /// <summary>
    /// Whether every assembly reference of every surface participant, on
    /// both endpoints, names another participant of its endpoint or a
    /// trusted platform assembly of this process.
    /// </summary>
    /// <remarks>
    /// The legacy merged API surface resolves generic constraints through a
    /// path-based dependency resolver whose other tiers (package dependencies
    /// in local caches, shared frameworks) depend on machine state, and it
    /// reports a constraint it cannot bind as an inspection failure. A merged
    /// endpoint surface (API changes, API Finding Transitions) is admitted
    /// only when the references close over the two deterministic tiers, where
    /// legacy binding succeeds and adds no row. Library API Diff reads no
    /// constraint resolution on either path and does not need this.
    /// </remarks>
    internal bool ReferencesClose { get; }

    /// <summary>
    /// Whether one pairwise package diff request may take the scope route:
    /// both endpoints exact, one explicit target framework, online.
    /// </summary>
    internal static bool IsEligible(
        string packageId,
        string fromVersion,
        string toVersion,
        string? targetFramework) =>
        !DotnetInspector.Networking.HttpClientFactory.IsOffline
        && !string.IsNullOrWhiteSpace(targetFramework)
        && !string.Equals(targetFramework, "all", StringComparison.OrdinalIgnoreCase)
        && DotnetInspector.Packages.PackageExtractor.IsValidPackageId(packageId)
        && IsExactVersion(fromVersion)
        && IsExactVersion(toVersion);

    static bool IsExactVersion(string version) =>
        !version.Contains('*')
        && !version.Equals("latest", StringComparison.OrdinalIgnoreCase)
        && NuGetVersion.TryParse(version, out _);

    /// <summary>
    /// Opens both endpoints with the <c>Surface</c> demand by range, or
    /// returns <see langword="null"/> when either endpoint must take the
    /// legacy path: it did not open, or the legacy selector would pick other
    /// assemblies from its archive than the scope's surface participants.
    /// </summary>
    internal static async Task<PackageEndpointDiffSession?> TryOpenAsync(
        HttpClient httpClient,
        string packageId,
        string fromVersion,
        string toVersion,
        string targetFramework,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        CancellationToken cancellationToken = default)
    {
        var composition = new DesktopPackageSourceComposition(httpClient.Timeout);
        var stores = new SearchPackageStores("inspect-diff");
        PackageEndpointScope? from = null;
        PackageEndpointScope? to = null;
        try
        {
            PackageHouse house = composition.CreateRealizationHouse(
                new PackagePayloadAcquisitionPlan(
                    stores.GetStore,
                    PackagePayloadLimits.Default,
                    log: logger.Log,
                    access: PackagePayloadAccess.Ranged),
                sourceOptions,
                logger.Log);
            from = await OpenAsync(
                    composition, house, packageId, fromVersion, targetFramework, logger, cancellationToken)
                .ConfigureAwait(false);
            if (from is not null)
            {
                to = await OpenAsync(
                        composition, house, packageId, toVersion, targetFramework, logger, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (from is null || to is null)
            {
                await CloseAsync(from, to, composition, stores).ConfigureAwait(false);
                return null;
            }

            bool referencesClose =
                await ReferencesCloseAsync(from, cancellationToken).ConfigureAwait(false)
                && await ReferencesCloseAsync(to, cancellationToken).ConfigureAwait(false);
            return new PackageEndpointDiffSession(
                composition, stores, from, to, referencesClose);
        }
        catch
        {
            await CloseAsync(from, to, composition, stores).ConfigureAwait(false);
            throw;
        }
    }

    static async Task<PackageEndpointScope?> OpenAsync(
        DesktopPackageSourceComposition composition,
        PackageHouse house,
        string packageId,
        string version,
        string targetFramework,
        VerboseLogger logger,
        CancellationToken cancellationToken)
    {
        PackageEndpointScopeRequest request;
        try
        {
            request = new PackageEndpointScopeRequest(
                PackageSourceCoordinate.Create(packageId, version),
                targetFramework,
                PackageAssetDemand.Surface);
            _ = PackageHouseTargetContext.Exact(targetFramework);
        }
        catch (ArgumentException exception)
        {
            logger.Log(
                $"Package endpoint {packageId}@{version} takes the legacy path: {exception.Message}");
            return null;
        }

        PackageEndpointScopeOutcome outcome =
            await PackageEndpointScope.OpenAsync(
                    house,
                    composition.IssueSettlementOperation(cancellationToken),
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
        if (outcome is PackageEndpointScopeOutcome.Failed failed)
        {
            // The legacy path owns the user-visible report of every endpoint
            // failure, so its diagnostics stay unchanged.
            logger.Log(
                $"Package endpoint {packageId}@{version} takes the legacy path "
                    + $"({failed.Kind}): {failed.Message}");
            return null;
        }

        PackageEndpointScope scope = ((PackageEndpointScopeOutcome.Opened)outcome).Scope;
        string[] legacy =
        [
            .. scope.Root.Root.UseContent(content =>
                TfmSelector.SelectAssembliesByTfmFromEntries(
                    content.EnumerateEntries(),
                    targetFramework)),
        ];
        string[] selected =
        [
            .. scope.SurfaceParticipants
                .Select(static participant => participant.Asset.Path)
                .Order(StringComparer.Ordinal),
        ];
        if (!legacy.SequenceEqual(selected, StringComparer.Ordinal))
        {
            logger.Log(
                $"Package endpoint {packageId}@{version} takes the legacy path: the legacy "
                    + $"selector picks [{string.Join(", ", legacy)}], the endpoint scope's "
                    + $"surface is [{string.Join(", ", selected)}].");
            await scope.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        logger.Log(
            $"Using package endpoint scope for {packageId}@{version} "
                + $"({selected.Length} surface assemblies; payload {scope.Payload.Origin}).");
        return scope;
    }

    static async Task<bool> ReferencesCloseAsync(
        PackageEndpointScope scope,
        CancellationToken cancellationToken)
    {
        HashSet<string> platform = TrustedPlatformAssemblyNames.Value;
        ArtifactRootResult<bool> result = await scope.UseSurfaceAsync(
                (group, participants, token) =>
                {
                    var names = new HashSet<string>(
                        participants.Select(static participant =>
                            participant.Participant.Assembly.Identity.Name),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (PackageEndpointBoundParticipant participant in participants)
                    {
                        token.ThrowIfCancellationRequested();
                        if (AssemblyContextReferencesQuery.ExecuteParticipant(
                                group,
                                participant.Participant)
                            is not AssemblyContextEntry<
                                System.Collections.Immutable.ImmutableArray<
                                    AssemblyReferenceIdentity>>.Available available
                            || available.Value.Any(reference =>
                                !names.Contains(reference.Name)
                                && !platform.Contains(reference.Name)))
                        {
                            return ValueTask.FromResult(false);
                        }
                    }
                    return ValueTask.FromResult(true);
                },
                cancellationToken)
            .ConfigureAwait(false);
        return result is ArtifactRootResult<bool>.Available { Value: true };
    }

    static readonly Lazy<HashSet<string>> TrustedPlatformAssemblyNames = new(() =>
        new HashSet<string>(
            (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>(),
            StringComparer.OrdinalIgnoreCase));

    /// <summary>Projects one endpoint's merged Library-population API surface.</summary>
    internal static ApiSurface ExtractSurface(
        PackageEndpointScope scope,
        string name,
        string targetFramework,
        bool includeAll,
        VerboseLogger logger)
    {
        ArtifactRootResult<ApiSurface> result =
            PackageEndpointApiSurface.ExtractAsync(
                    scope,
                    includeAll ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.Public,
                    name,
                    targetFramework,
                    logger.Log)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        return result switch
        {
            ArtifactRootResult<ApiSurface>.Available available => available.Value,
            ArtifactRootResult<ApiSurface>.Rejected rejected =>
                throw new InvalidOperationException(
                    "The package endpoint rejected its API surface query: " + rejected.Failure),
            _ => throw new InvalidOperationException("Unknown package Root query outcome."),
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_closed)
            return;
        _closed = true;
        await CloseAsync(From, To, _composition, _stores).ConfigureAwait(false);
    }

    static async Task CloseAsync(
        PackageEndpointScope? from,
        PackageEndpointScope? to,
        DesktopPackageSourceComposition composition,
        SearchPackageStores stores)
    {
        try
        {
            try
            {
                if (from is not null)
                    await from.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (to is not null)
                    await to.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // Temporary authority content stays readable until both
            // Workspaces that admitted it have closed.
            stores.Dispose();
            await composition.DisposeAsync().ConfigureAwait(false);
        }
    }
}
