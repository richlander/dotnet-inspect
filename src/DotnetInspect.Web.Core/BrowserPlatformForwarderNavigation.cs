using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspect.Web.Core;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Web;

internal sealed record BrowserPlatformForwarderRowInfo(
    string Action,
    LibraryTypeShape Declaration);

internal sealed record BrowserPlatformForwarderViewInfo(
    string Id,
    BrowserPackageSurfaceInfo Surface,
    RealizedMemberCoordinate.Platform Coordinate,
    ImmutableArray<BrowserPlatformForwarderRowInfo> Forwarders,
    string? SelectedTypeId);

internal abstract record BrowserPlatformForwarderNavigationResult
{
    internal sealed record Opened(
        BrowserPlatformForwarderViewInfo View,
        PlatformTypeDefinitionResolutionResult.Resolved? Resolution = null)
        : BrowserPlatformForwarderNavigationResult;

    internal sealed record Blocked(
        string Status,
        string Message,
        PlatformHouseReceipt? Receipt = null,
        PlatformSourceContribution? Contribution = null,
        PlatformTypeDefinitionResolutionResult? Resolution = null)
        : BrowserPlatformForwarderNavigationResult;
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserPlatformForwarderNavigation : IDisposable
{
    readonly Lock _gate = new();
    readonly HttpClient _networkClient;
    readonly IPackageSourceAuthorization _authorization;
    readonly BrowserPlatformForwarderSource _source;
    readonly TimeSpan _timeout;
    Publication? _current;
    long _generation;
    bool _disposed;

    internal BrowserPlatformForwarderNavigation(
        HttpClient networkClient,
        IPackageSourceClient packageClient,
        IPackageSourceAuthorization authorization,
        TimeSpan timeout)
    {
        _networkClient = networkClient;
        _authorization = authorization;
        _source = new(packageClient, authorization, timeout);
        _timeout = timeout;
    }

    internal async Task<BrowserPlatformForwarderNavigationResult> OpenAsync(
        string framework,
        string version,
        string assembly,
        string pack,
        CancellationToken cancellationToken = default)
    {
        Publication publication;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            publication = Begin();
        }
        PreparedLibrary prepared = await PrepareAsync(
            framework, version, assembly, pack, cancellationToken);
        lock (_gate)
        {
            if (!Current(publication))
                return Stale();
            return new BrowserPlatformForwarderNavigationResult.Opened(
                Publish(publication, prepared, selectedType: null));
        }
    }

    internal async Task<BrowserPlatformForwarderNavigationResult> ActivateAsync(
        string action,
        CancellationToken cancellationToken = default)
    {
        Publication publication;
        BrowserPlatformForwarderAction managedAction;
        lock (_gate)
        {
            if (_disposed || _current is not { } current
                || !current.Actions.TryGetValue(action, out managedAction!))
            {
                return Stale();
            }
            publication = current;
        }

        BrowserPlatformForwarderActivationResult.Activated? activated = null;
        try
        {
            BrowserPlatformForwarderActivationResult result =
                await publication.Owner.ActivateAsync(managedAction, cancellationToken);
            if (result is BrowserPlatformForwarderActivationResult.Blocked blocked)
                return Blocked(blocked.Block);
            activated = (BrowserPlatformForwarderActivationResult.Activated)result;
            if (!IsCurrent(publication))
                return Stale(activated.Resolution);

            PackageImplementationLibrary destination =
                await _source.RealizeDestinationAsync(activated, cancellationToken);
            if (!IsCurrent(publication))
                return Stale(activated.Resolution);

            string family = Family(destination.Framework.Family);
            PreparedLibrary prepared = await PrepareAsync(
                activated.Target.TargetFramework.ToString(),
                destination.Framework.Version.Value,
                $"{destination.Identity.Name}.dll",
                BrowserPlatformWorkspace.Pack(family),
                cancellationToken);
            PlatformTypeResolutionAssemblyEvidence expectedAssembly = activated.Destination switch
            {
                BrowserPlatformForwarderDestination.Forwarder forwarder => forwarder.Library,
                BrowserPlatformForwarderDestination.Definition definition => definition.Value.Assembly.Assembly,
                _ => throw new InvalidOperationException("Unknown forwarded Type destination."),
            };
            if (!prepared.Identity.IsEquivalentTo(expectedAssembly.Identity))
            {
                throw new BrowserPlatformForwarderOperationException(
                    "refused", "The destination Library does not match the resolved assembly identity.");
            }
            if (prepared.Document.ModuleVersionId != expectedAssembly.ModuleVersionId)
            {
                throw new BrowserPlatformForwarderOperationException(
                    "refused", "The destination Library does not match the resolved module.");
            }
            // Workspace coordinates still use the cache spelling of the selected configured authority.
            string expectedProducer = NuGetCache.GetSourceKey(destination.Framework.Authority.Source.Url);
            if (prepared.Coordinate.Producer != expectedProducer)
            {
                throw new BrowserPlatformForwarderOperationException(
                    "refused",
                    "The destination Library was not acquired from the resolved configured source.");
            }

            MetadataTypeDefinitionName selectedType = VerifyDestination(activated.Destination, prepared);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!Current(publication))
                    return Stale(activated.Resolution);
                return new BrowserPlatformForwarderNavigationResult.Opened(
                    Publish(Begin(), prepared, selectedType),
                    activated.Resolution);
            }
        }
        catch (BrowserPlatformForwarderOperationException failure)
        {
            if (!IsCurrent(publication))
                return Stale(activated?.Resolution);
            return new BrowserPlatformForwarderNavigationResult.Blocked(
                failure.Status,
                failure.Message,
                failure.Receipt,
                failure.Contribution,
                activated?.Resolution);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new BrowserPlatformForwarderNavigationResult.Blocked(
                "canceled", "Forwarded Type navigation was canceled.",
                Resolution: activated?.Resolution);
        }
        catch (Exception failure) when (
            failure is InvalidOperationException or IOException or BadImageFormatException
                or AggregateException or HttpRequestException or TimeoutException)
        {
            if (!IsCurrent(publication))
                return Stale(activated?.Resolution);
            return new BrowserPlatformForwarderNavigationResult.Blocked(
                failure is TimeoutException ? "incomplete" : "failed",
                failure.Message,
                Resolution: activated?.Resolution);
        }
    }

    internal bool Close(string view)
    {
        lock (_gate)
        {
            if (_current?.Id != view)
                return false;
            _current.Owner.Dispose();
            _current = null;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _current?.Owner.Dispose();
            _current = null;
        }
    }

    async Task<PreparedLibrary> PrepareAsync(
        string framework,
        string version,
        string assembly,
        string pack,
        CancellationToken cancellationToken)
    {
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework, version, assembly, pack,
                _networkClient, _authorization, _timeout, cancellationToken);
        LibraryDocument document = await BrowserPlatformSurfaceProjection.ReadForwardersAsync(
            resolution.Scope, resolution.Participant, cancellationToken);
        if (document.Types.Rows is not LibraryTypePopulationRowsOutcome.Read { IsComplete: true })
        {
            throw new BrowserPlatformForwarderOperationException(
                document.Types.Rows is LibraryTypePopulationRowsOutcome.Incomplete
                    or LibraryTypePopulationRowsOutcome.Read ? "incomplete" : "failed",
                $"Forwarded Type inventory could not be completed ({document.Types.Rows}).");
        }
        BrowserPlatformProjectionInfo projection = BrowserPlatformSurfaceProjection.Project(
            resolution.Scope, resolution.Participant, resolution.Coordinate);
        return new(
            projection.Surface,
            document,
            resolution.Coordinate,
            resolution.Participant.Participant.Assembly.Identity);
    }

    Publication Begin()
    {
        _current?.Owner.Dispose();
        return _current = new(
            Guid.NewGuid().ToString("N"),
            ++_generation,
            new BrowserPlatformForwarderActivation(_source.ResolveAsync));
    }

    BrowserPlatformForwarderViewInfo Publish(
        Publication publication,
        PreparedLibrary library,
        MetadataTypeDefinitionName? selectedType)
    {
        var target = new PlatformFamilyTarget(
            library.Coordinate.Family switch
            {
                BrowserPlatformWorkspace.RuntimeFamily => PlatformFamily.DotNetRuntime,
                BrowserPlatformWorkspace.AspNetCoreFamily => PlatformFamily.AspNetCore,
                _ => throw new InvalidOperationException("Unsupported Browser Platform family."),
            },
            PlatformTargetFramework.Parse(library.Coordinate.Framework),
            PlatformVersion.Parse(library.Coordinate.Version));
        if (publication.Owner.BeginResult(
                publication.Generation,
                target,
                WorkspaceContextLoader.RepresentativeRuntimeIdentifier,
                _source.Sources,
                library.Identity,
                library.Document.ModuleVersionId)
            is not BrowserPlatformForwarderResultAdmission.Admitted admitted)
        {
            throw new InvalidOperationException("The current forwarder view could not be admitted.");
        }
        var rows = (LibraryTypePopulationRowsOutcome.Read)library.Document.Types.Rows!;
        var forwarders = ImmutableArray.CreateBuilder<BrowserPlatformForwarderRowInfo>(rows.Items.Length);
        foreach (LibraryTypeShape row in rows.Items)
        {
            if (publication.Owner.Publish(admitted.Authority, row)
                is not BrowserPlatformForwarderActionPublication.Published published)
            {
                throw new InvalidOperationException("The selected Library rejected its forwarding declaration.");
            }
            string action = Guid.NewGuid().ToString("N");
            publication.Actions.Add(action, published.Action);
            forwarders.Add(new(action, row));
        }
        return new(
            publication.Id,
            library.Surface,
            library.Coordinate,
            forwarders.MoveToImmutable(),
            selectedType is null ? null : $"{library.Identity.Name}:{selectedType.ToEscapedFullName()}");
    }

    static MetadataTypeDefinitionName VerifyDestination(
        BrowserPlatformForwarderDestination destination,
        PreparedLibrary library)
    {
        if (destination is BrowserPlatformForwarderDestination.Forwarder forwarder)
        {
            var rows = (LibraryTypePopulationRowsOutcome.Read)library.Document.Types.Rows!;
            LibraryTypeShape? row = rows.Items.SingleOrDefault(item => item.Identity == forwarder.Type);
            if (row?.Forwarding is not { } forwarding
                || !forwarding.Declarations.SequenceEqual(forwarder.Declarations)
                || !new AssemblyReferenceIdentity(
                    forwarding.TargetAssembly.Name.ToString(),
                    forwarding.TargetAssembly.Version,
                    forwarding.TargetAssembly.Culture?.ToString(),
                    forwarding.TargetAssembly.PublicKeyToken?.ToString())
                    .IsEquivalentTo(forwarder.TargetAssembly))
            {
                throw new BrowserPlatformForwarderOperationException(
                    "refused", "The destination does not contain the resolved forwarding declaration.");
            }
            return forwarder.Type;
        }
        var definition = (BrowserPlatformForwarderDestination.Definition)destination;
        if (!library.Surface.Types.Any(
                type => type.DefinitionId == definition.Value.Type.ToEscapedFullName()))
        {
            throw new BrowserPlatformForwarderOperationException(
                "unavailable", "The resolved defining Type is not available in the destination Library surface.");
        }
        return definition.Value.Type;
    }

    bool Current(Publication publication) =>
        !_disposed && ReferenceEquals(_current, publication);

    bool IsCurrent(Publication publication)
    {
        lock (_gate)
            return Current(publication);
    }

    static BrowserPlatformForwarderNavigationResult.Blocked Stale(
        PlatformTypeDefinitionResolutionResult? resolution = null) =>
        new("stale", "The forwarded Type action no longer belongs to the current Library view.",
            Resolution: resolution);

    static BrowserPlatformForwarderNavigationResult.Blocked Blocked(
        BrowserPlatformForwarderActivationBlock block) =>
        block switch
        {
            BrowserPlatformForwarderActivationBlock.Stale stale =>
                new("stale", "The forwarding result is no longer current.", stale.Receipt, Resolution: stale.Resolution),
            BrowserPlatformForwarderActivationBlock.Refused refused =>
                new("refused", $"Forwarding was refused ({refused.Reason}).", refused.Receipt, Resolution: refused.Resolution),
            BrowserPlatformForwarderActivationBlock.Unavailable unavailable =>
                new("unavailable", $"The forwarded Type is unavailable ({unavailable.Reason}).", unavailable.Receipt, Resolution: unavailable.Resolution),
            BrowserPlatformForwarderActivationBlock.Ambiguous ambiguous =>
                new("ambiguous", "The forwarded Type has multiple admissible destinations.", ambiguous.Receipt, Resolution: ambiguous.Resolution),
            BrowserPlatformForwarderActivationBlock.Incomplete incomplete =>
                new("incomplete", "The forwarding operation reached its work limit.", incomplete.Receipt, Resolution: incomplete.Resolution),
            BrowserPlatformForwarderActivationBlock.Failed failed =>
                new("failed", $"The forwarding operation failed ({failed.Reason}).", failed.Receipt),
            BrowserPlatformForwarderActivationBlock.Canceled =>
                new("canceled", "Forwarded Type navigation was canceled."),
            _ => throw new InvalidOperationException("Unknown forwarding activation result."),
        };

    static string Family(PlatformFamily family) => family switch
    {
        PlatformFamily.DotNetRuntime => BrowserPlatformWorkspace.RuntimeFamily,
        PlatformFamily.AspNetCore => BrowserPlatformWorkspace.AspNetCoreFamily,
        _ => throw new InvalidOperationException("Unsupported Browser Platform family."),
    };

    sealed record PreparedLibrary(
        BrowserPackageSurfaceInfo Surface,
        LibraryDocument Document,
        RealizedMemberCoordinate.Platform Coordinate,
        AssemblyReferenceIdentity Identity);

    sealed record Publication(string Id, long Generation, BrowserPlatformForwarderActivation Owner)
    {
        internal Dictionary<string, BrowserPlatformForwarderAction> Actions { get; } = [];
    }
}
