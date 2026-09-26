using System.Collections.Immutable;

using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Web.Core;

internal delegate ValueTask<PlatformHouseOutcome<
    PlatformTypeDefinitionValue.Implementation<
        PlatformTypeDefinitionResolutionResult>>>
    BrowserPlatformForwarderResolutionOperation(
        BrowserPlatformForwarderResolutionRequest request,
        CancellationToken cancellationToken);

internal sealed record BrowserPlatformForwarderResolutionRequest(
    PlatformFamilyTarget Target,
    string RuntimeIdentifier,
    PlatformSourcePlan Sources,
    AssemblyReferenceIdentity SourceAssembly,
    Guid SourceModuleVersionId,
    MetadataTypeDefinitionName Type,
    ImmutableArray<ExportedTypeToken> Declarations,
    AssemblyReferenceIdentity TargetAssembly);

internal sealed class BrowserPlatformForwarderResultAuthority
{
    internal BrowserPlatformForwarderResultAuthority(
        object issuer,
        object publication,
        long generation) =>
        (Issuer, Publication, Generation) =
            (issuer, publication, generation);

    internal object Issuer { get; }
    internal object Publication { get; }
    internal long Generation { get; }
}

internal sealed class BrowserPlatformForwarderAction
{
    internal BrowserPlatformForwarderAction(
        object issuer,
        object publication,
        object token) =>
        (Issuer, Publication, Token) =
            (issuer, publication, token);

    internal object Issuer { get; }
    internal object Publication { get; }
    internal object Token { get; }
}

internal enum BrowserPlatformForwarderPublicationRefusal
{
    ForeignAuthority,
    StaleAuthority,
    SourceModuleVersionIdMismatch,
}

internal abstract record BrowserPlatformForwarderResultAdmission
{
    private protected BrowserPlatformForwarderResultAdmission()
    {
    }

    internal sealed record Admitted(
        BrowserPlatformForwarderResultAuthority Authority)
        : BrowserPlatformForwarderResultAdmission;

    internal sealed record Blocked(
        BrowserPlatformForwarderActivationBlock Block)
        : BrowserPlatformForwarderResultAdmission;
}

internal abstract record BrowserPlatformForwarderActionPublication
{
    private protected BrowserPlatformForwarderActionPublication()
    {
    }

    internal sealed record Published(
        BrowserPlatformForwarderAction Action)
        : BrowserPlatformForwarderActionPublication;

    internal sealed record NotApplicable
        : BrowserPlatformForwarderActionPublication;

    internal sealed record Refused(
        BrowserPlatformForwarderPublicationRefusal Reason)
        : BrowserPlatformForwarderActionPublication;
}

internal abstract record BrowserPlatformForwarderDestination
{
    private protected BrowserPlatformForwarderDestination()
    {
    }

    internal sealed record Forwarder(
        MetadataTypeDefinitionName Type,
        PlatformTypeResolutionAssemblyEvidence Library,
        PlatformTypeResolutionOccurrenceEvidence Occurrence,
        ImmutableArray<ExportedTypeToken> Declarations,
        AssemblyReferenceIdentity TargetAssembly)
        : BrowserPlatformForwarderDestination;

    internal sealed record Definition(
        PlatformTypeResolutionDefinitionEvidence Value)
        : BrowserPlatformForwarderDestination;
}

internal enum BrowserPlatformForwarderRefusedReason
{
    ForeignAction,
    EvidenceMismatch,
    ResolutionRejected,
}

internal enum BrowserPlatformForwarderFailureReason
{
    ResolutionFailed,
}

internal enum BrowserPlatformForwarderUnavailableReason
{
    OwnerClosed,
    PlatformHouse,
    Resolution,
}

internal enum BrowserPlatformForwarderStaleReason
{
    Result,
}

internal abstract record BrowserPlatformForwarderActivationBlock
{
    private protected BrowserPlatformForwarderActivationBlock()
    {
    }

    internal sealed record Stale(
        BrowserPlatformForwarderStaleReason Reason,
        PlatformHouseReceipt? Receipt = null,
        PlatformTypeDefinitionResolutionResult? Resolution = null)
        : BrowserPlatformForwarderActivationBlock;

    internal sealed record Refused(
        BrowserPlatformForwarderRefusedReason Reason,
        PlatformHouseReceipt? Receipt = null,
        PlatformTypeDefinitionResolutionResult? Resolution = null)
        : BrowserPlatformForwarderActivationBlock;

    internal sealed record Unavailable(
        BrowserPlatformForwarderUnavailableReason Reason,
        PlatformHouseReceipt? Receipt,
        PlatformTypeDefinitionResolutionResult? Resolution)
        : BrowserPlatformForwarderActivationBlock;

    internal sealed record Ambiguous(
        PlatformHouseReceipt? Receipt,
        PlatformTypeDefinitionResolutionResult? Resolution)
        : BrowserPlatformForwarderActivationBlock;

    internal sealed record Incomplete(
        PlatformHouseReceipt? Receipt,
        PlatformTypeDefinitionResolutionResult? Resolution)
        : BrowserPlatformForwarderActivationBlock;

    internal sealed record Failed(
        BrowserPlatformForwarderFailureReason Reason,
        PlatformHouseReceipt Receipt)
        : BrowserPlatformForwarderActivationBlock;

    internal sealed record Canceled
        : BrowserPlatformForwarderActivationBlock;
}

internal abstract record BrowserPlatformForwarderActivationResult
{
    private protected BrowserPlatformForwarderActivationResult()
    {
    }

    internal sealed record Activated(
        BrowserPlatformForwarderAction Action,
        PlatformFamilyTarget Target,
        string RuntimeIdentifier,
        PlatformSourcePlan Sources,
        BrowserPlatformForwarderDestination Destination,
        PlatformTypeDefinitionResolutionResult.Resolved Resolution)
        : BrowserPlatformForwarderActivationResult;

    internal sealed record Blocked(
        BrowserPlatformForwarderAction Action,
        BrowserPlatformForwarderActivationBlock Block)
        : BrowserPlatformForwarderActivationResult;
}

internal sealed class BrowserPlatformForwarderActivation : IDisposable
{
    private readonly Lock _gate = new();
    private readonly object _issuer = new();
    private readonly BrowserPlatformForwarderResolutionOperation _resolve;
    private readonly Dictionary<object, ActionEntry> _actions = [];
    private PublicationState? _current;
    private bool _disposed;

    internal BrowserPlatformForwarderActivation(
        BrowserPlatformForwarderResolutionOperation resolve) =>
        _resolve = resolve
            ?? throw new ArgumentNullException(nameof(resolve));

    internal BrowserPlatformForwarderResultAdmission BeginResult(
        long generation,
        PlatformFamilyTarget target,
        string runtimeIdentifier,
        PlatformSourcePlan sources,
        AssemblyReferenceIdentity sourceAssembly,
        Guid sourceModuleVersionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(sourceAssembly);
        if (sourceModuleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A Platform Library authority requires a source MVID.",
                nameof(sourceModuleVersionId));
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return new BrowserPlatformForwarderResultAdmission.Blocked(
                    new BrowserPlatformForwarderActivationBlock.Unavailable(
                        BrowserPlatformForwarderUnavailableReason.OwnerClosed,
                        Receipt: null,
                        Resolution: null));
            }
            if (_current is { } current
                && generation <= current.Generation)
            {
                return new BrowserPlatformForwarderResultAdmission.Blocked(
                    new BrowserPlatformForwarderActivationBlock.Stale(
                        BrowserPlatformForwarderStaleReason.Result));
            }

            _actions.Clear();
            _current = new(
                new object(),
                generation,
                target,
                runtimeIdentifier,
                sources,
                sourceAssembly,
                sourceModuleVersionId);
            return new BrowserPlatformForwarderResultAdmission.Admitted(
                new(
                    _issuer,
                    _current.Identity,
                    generation));
        }
    }

    internal BrowserPlatformForwarderActionPublication Publish(
        BrowserPlatformForwarderResultAuthority authority,
        LibraryTypeShape type)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(type);

        lock (_gate)
        {
            if (!ReferenceEquals(authority.Issuer, _issuer))
            {
                return new BrowserPlatformForwarderActionPublication.Refused(
                    BrowserPlatformForwarderPublicationRefusal
                        .ForeignAuthority);
            }
            if (_disposed
                || _current is not { } current
                || !ReferenceEquals(
                    authority.Publication,
                    current.Identity)
                || authority.Generation != current.Generation)
            {
                return new BrowserPlatformForwarderActionPublication.Refused(
                    BrowserPlatformForwarderPublicationRefusal
                        .StaleAuthority);
            }
            if (type.DeclarationKind
                is not LibraryTypeDeclarationKind.Forwarder)
            {
                return new BrowserPlatformForwarderActionPublication
                    .NotApplicable();
            }

            LibraryTypeForwardingEvidence forwarding =
                type.Forwarding
                ?? throw new InvalidOperationException(
                    "A forwarder row omitted forwarding evidence.");
            if (forwarding.SourceModuleVersionId
                != current.SourceModuleVersionId)
            {
                return new BrowserPlatformForwarderActionPublication.Refused(
                    BrowserPlatformForwarderPublicationRefusal
                        .SourceModuleVersionIdMismatch);
            }

            var token = new object();
            _actions.Add(
                token,
                new(
                    current,
                    type.Identity,
                    forwarding.Declarations,
                    AssemblyIdentity(forwarding.TargetAssembly)));
            return new BrowserPlatformForwarderActionPublication.Published(
                new(_issuer, current.Identity, token));
        }
    }

    internal async ValueTask<
        BrowserPlatformForwarderActivationResult> ActivateAsync(
            BrowserPlatformForwarderAction action,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (cancellationToken.IsCancellationRequested)
        {
            return Blocked(
                action,
                new BrowserPlatformForwarderActivationBlock.Canceled());
        }

        ActionEntry entry;
        lock (_gate)
        {
            if (!ReferenceEquals(action.Issuer, _issuer))
            {
                return Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Refused(
                        BrowserPlatformForwarderRefusedReason.ForeignAction));
            }
            if (_disposed
                || _current is not { } current
                || !ReferenceEquals(
                    action.Publication,
                    current.Identity)
                || !_actions.TryGetValue(action.Token, out entry!))
            {
                return Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Stale(
                        BrowserPlatformForwarderStaleReason.Result));
            }
        }

        PlatformHouseOutcome<
            PlatformTypeDefinitionValue.Implementation<
                PlatformTypeDefinitionResolutionResult>> outcome;
        try
        {
            outcome = await _resolve(
                    new(
                        entry.Publication.Target,
                        entry.Publication.RuntimeIdentifier,
                        entry.Publication.Sources,
                        entry.Publication.SourceAssembly,
                        entry.Publication.SourceModuleVersionId,
                        entry.Type,
                        entry.Declarations,
                        entry.TargetAssembly),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return Blocked(
                action,
                new BrowserPlatformForwarderActivationBlock.Canceled());
        }

        lock (_gate)
        {
            if (_disposed
                || !_actions.TryGetValue(
                    action.Token,
                    out ActionEntry? current)
                || !ReferenceEquals(current, entry))
            {
                return Blocked(
                    action,
                    Stale(outcome));
            }
        }

        return Project(action, entry, outcome);
    }

    private static BrowserPlatformForwarderActivationBlock.Stale Stale(
        PlatformHouseOutcome<
            PlatformTypeDefinitionValue.Implementation<
                PlatformTypeDefinitionResolutionResult>> outcome) =>
        new(
            BrowserPlatformForwarderStaleReason.Result,
            outcome.Receipt,
            outcome is PlatformHouseOutcome<
                PlatformTypeDefinitionValue.Implementation<
                    PlatformTypeDefinitionResolutionResult>>.Completed
                        completed
                ? completed.Value.Outcome
                : null);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _actions.Clear();
            _current = null;
        }
    }

    private static BrowserPlatformForwarderActivationResult Project(
        BrowserPlatformForwarderAction action,
        ActionEntry entry,
        PlatformHouseOutcome<
            PlatformTypeDefinitionValue.Implementation<
                PlatformTypeDefinitionResolutionResult>> outcome) =>
        outcome switch
        {
            PlatformHouseOutcome<
                PlatformTypeDefinitionValue.Implementation<
                    PlatformTypeDefinitionResolutionResult>>.Completed
                        completed =>
                Project(
                    action,
                    entry,
                    completed.Receipt,
                    completed.Value.Outcome),
            PlatformHouseOutcome<
                PlatformTypeDefinitionValue.Implementation<
                    PlatformTypeDefinitionResolutionResult>>.Unavailable
                        unavailable =>
                Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Unavailable(
                        BrowserPlatformForwarderUnavailableReason.PlatformHouse,
                        unavailable.Receipt,
                        Resolution: null)),
            PlatformHouseOutcome<
                PlatformTypeDefinitionValue.Implementation<
                    PlatformTypeDefinitionResolutionResult>>.Ambiguous
                        ambiguous =>
                Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Ambiguous(
                        ambiguous.Receipt,
                        Resolution: null)),
            PlatformHouseOutcome<
                PlatformTypeDefinitionValue.Implementation<
                    PlatformTypeDefinitionResolutionResult>>.Rejected
                        rejected =>
                Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Refused(
                        BrowserPlatformForwarderRefusedReason
                            .ResolutionRejected,
                        rejected.Receipt)),
            PlatformHouseOutcome<
                PlatformTypeDefinitionValue.Implementation<
                    PlatformTypeDefinitionResolutionResult>>.Incomplete
                        incomplete =>
                Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Incomplete(
                        incomplete.Receipt,
                        Resolution: null)),
            PlatformHouseOutcome<
                PlatformTypeDefinitionValue.Implementation<
                    PlatformTypeDefinitionResolutionResult>>.Failed
                        failed =>
                Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Failed(
                        BrowserPlatformForwarderFailureReason
                            .ResolutionFailed,
                        failed.Receipt)),
            _ => throw new InvalidOperationException(
                "Unknown PlatformHouse forwarder-resolution outcome."),
        };

    private static BrowserPlatformForwarderActivationResult Project(
        BrowserPlatformForwarderAction action,
        ActionEntry entry,
        PlatformHouseReceipt receipt,
        PlatformTypeDefinitionResolutionResult resolution)
    {
        if (resolution.Hops.IsDefaultOrEmpty
            || !Matches(entry, resolution.Hops[0]))
        {
            return Blocked(
                action,
                new BrowserPlatformForwarderActivationBlock.Refused(
                    BrowserPlatformForwarderRefusedReason
                        .EvidenceMismatch,
                    Receipt: receipt,
                    Resolution: resolution));
        }

        return resolution switch
        {
            PlatformTypeDefinitionResolutionResult.Resolved resolved =>
                Project(action, entry, resolved),
            PlatformTypeDefinitionResolutionResult.NotFound
                or PlatformTypeDefinitionResolutionResult.UnboundBinding
                or PlatformTypeDefinitionResolutionResult.Unavailable =>
                Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Unavailable(
                        BrowserPlatformForwarderUnavailableReason.Resolution,
                        receipt,
                        resolution)),
            PlatformTypeDefinitionResolutionResult.Ambiguous =>
                Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Ambiguous(
                        receipt,
                        resolution)),
            PlatformTypeDefinitionResolutionResult.Rejected rejected
                when rejected.Failure
                    is PlatformTypeResolutionFailureEvidence
                        .DeclarationBudgetExceeded
                    or PlatformTypeResolutionFailureEvidence
                        .HopBudgetExceeded
                    or PlatformTypeResolutionFailureEvidence
                        .RequestBudgetExceeded
                    or PlatformTypeResolutionFailureEvidence
                        .DiscoveryBudgetExceeded =>
                Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Incomplete(
                        receipt,
                        resolution)),
            PlatformTypeDefinitionResolutionResult.Rejected =>
                Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Refused(
                        BrowserPlatformForwarderRefusedReason
                            .ResolutionRejected,
                        Receipt: receipt,
                        Resolution: resolution)),
            _ => throw new InvalidOperationException(
                "Unknown Platform type-definition result."),
        };
    }

    private static BrowserPlatformForwarderActivationResult Project(
        BrowserPlatformForwarderAction action,
        ActionEntry entry,
        PlatformTypeDefinitionResolutionResult.Resolved resolution)
    {
        if (resolution.Definition.Type != entry.Type)
        {
            return Blocked(
                action,
                new BrowserPlatformForwarderActivationBlock.Refused(
                    BrowserPlatformForwarderRefusedReason
                        .EvidenceMismatch,
                    Resolution: resolution));
        }

        BrowserPlatformForwarderDestination destination;
        if (resolution.Hops.Length > 1)
        {
            PlatformTypeForwardingHopEvidence next = resolution.Hops[1];
            if (next.SourceAssembly.Assembly.ModuleVersionId is null)
            {
                return Blocked(
                    action,
                    new BrowserPlatformForwarderActivationBlock.Refused(
                        BrowserPlatformForwarderRefusedReason
                            .EvidenceMismatch,
                        Resolution: resolution));
            }
            destination = new BrowserPlatformForwarderDestination.Forwarder(
                entry.Type,
                next.SourceAssembly.Assembly,
                next.SourceOccurrence,
                next.Declarations,
                next.TargetReference);
        }
        else
        {
            destination =
                new BrowserPlatformForwarderDestination.Definition(
                    resolution.Definition);
        }
        return new BrowserPlatformForwarderActivationResult.Activated(
            action,
            entry.Publication.Target,
            entry.Publication.RuntimeIdentifier,
            entry.Publication.Sources,
            destination,
            resolution);
    }

    private static bool Matches(
        ActionEntry entry,
        PlatformTypeForwardingHopEvidence hop) =>
        entry.Publication.SourceAssembly.IsEquivalentTo(
            hop.SourceAssembly.Assembly.Identity)
        && hop.SourceAssembly.Assembly.ModuleVersionId
            == entry.Publication.SourceModuleVersionId
        && entry.Declarations.SequenceEqual(hop.Declarations)
        && entry.TargetAssembly.IsEquivalentTo(hop.TargetReference);

    private static AssemblyReferenceIdentity AssemblyIdentity(
        LibraryAssemblyIdentity identity) =>
        new(
            identity.Name.ToString(),
            identity.Version,
            identity.Culture?.ToString(),
            identity.PublicKeyToken?.ToString());

    private static BrowserPlatformForwarderActivationResult.Blocked Blocked(
        BrowserPlatformForwarderAction action,
        BrowserPlatformForwarderActivationBlock block) =>
        new(action, block);

    private sealed record PublicationState(
        object Identity,
        long Generation,
        PlatformFamilyTarget Target,
        string RuntimeIdentifier,
        PlatformSourcePlan Sources,
        AssemblyReferenceIdentity SourceAssembly,
        Guid SourceModuleVersionId);

    private sealed record ActionEntry(
        PublicationState Publication,
        MetadataTypeDefinitionName Type,
        ImmutableArray<ExportedTypeToken> Declarations,
        AssemblyReferenceIdentity TargetAssembly);
}
