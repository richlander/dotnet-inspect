using DotnetInspector.Queries;

namespace DotnetInspect.Web;

internal abstract record BrowserSpotlightPackageAcquisitionResult<TFailure>
    where TFailure : class
{
    private protected BrowserSpotlightPackageAcquisitionResult()
    {
    }

    internal sealed record Acquired :
        BrowserSpotlightPackageAcquisitionResult<TFailure>
    {
        internal Acquired(PackageRootBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            Binding = binding;
        }

        internal PackageRootBinding Binding { get; }
    }

    internal sealed record NotAcquired :
        BrowserSpotlightPackageAcquisitionResult<TFailure>
    {
        internal NotAcquired(TFailure result)
        {
            ArgumentNullException.ThrowIfNull(result);
            Result = result;
        }

        internal TFailure Result { get; }
    }
}

internal delegate ValueTask<
    BrowserSpotlightPackageAcquisitionResult<TFailure>>
    BrowserSpotlightPackageAcquisitionOperation<TPackageRequest, TFailure>(
        BrowserSpotlightPackageRequest<TPackageRequest> package,
        CancellationToken cancellationToken)
    where TPackageRequest : class
    where TFailure : class;

internal abstract record BrowserSpotlightPackageMembershipResult
{
    private protected BrowserSpotlightPackageMembershipResult()
    {
    }

    internal sealed record AlreadyCurrent(
        WorkspaceScopeSnapshot Snapshot,
        WorkspacePackageOccurrence Occurrence) :
        BrowserSpotlightPackageMembershipResult;

    internal sealed record Committed(
        WorkspaceScopeOperationResult.Committed Result,
        WorkspacePackageOccurrence Occurrence) :
        BrowserSpotlightPackageMembershipResult;

    internal sealed record NoEffect(
        WorkspaceScopeOperationResult.NoEffect Result,
        WorkspacePackageOccurrence Occurrence) :
        BrowserSpotlightPackageMembershipResult;

    internal sealed record NotCommitted :
        BrowserSpotlightPackageMembershipResult
    {
        internal NotCommitted(WorkspaceScopeOperationResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (result is WorkspaceScopeOperationResult.Committed
                or WorkspaceScopeOperationResult.NoEffect)
            {
                throw new ArgumentException(
                    "A successful Scope result requires its exact Package occurrence.",
                    nameof(result));
            }

            Result = result;
        }

        internal WorkspaceScopeOperationResult Result { get; }
    }
}

internal abstract record BrowserSpotlightPackageFocusRequest<
    TPackageRequest,
    TLibraryIntent>
    where TPackageRequest : class
    where TLibraryIntent : class
{
    private protected BrowserSpotlightPackageFocusRequest(
        WorkspaceScopeSnapshot scope,
        WorkspacePackageOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(occurrence);
        if (!scope.Packages.Any(package =>
                ReferenceEquals(package.Occurrence, occurrence)))
        {
            throw new ArgumentException(
                "The focus occurrence must belong to the supplied Scope snapshot.",
                nameof(occurrence));
        }

        Scope = scope;
        Occurrence = occurrence;
    }

    internal WorkspaceScopeSnapshot Scope { get; }

    internal WorkspacePackageOccurrence Occurrence { get; }

    internal sealed record AfterScope :
        BrowserSpotlightPackageFocusRequest<
            TPackageRequest,
            TLibraryIntent>
    {
        internal AfterScope(
            WorkspaceScopeSnapshot scope,
            WorkspacePackageOccurrence occurrence,
            BrowserSpotlightPackageRequest<TPackageRequest> package,
            PackageRootBinding binding,
            WorkspaceScopeOperationResult scopeResult,
            TLibraryIntent? libraryIntent)
            : base(scope, occurrence)
        {
            ArgumentNullException.ThrowIfNull(package);
            ArgumentNullException.ThrowIfNull(binding);
            ArgumentNullException.ThrowIfNull(scopeResult);
            if (scopeResult is not WorkspaceScopeOperationResult.Committed
                and not WorkspaceScopeOperationResult.NoEffect)
            {
                throw new ArgumentException(
                    "Package focus requires a committed or no-effect Scope result.",
                    nameof(scopeResult));
            }
            if (!ReferenceEquals(
                    scope.FindPackageOccurrence(binding)?.Occurrence,
                    occurrence))
            {
                throw new ArgumentException(
                    "The Package binding must identify the exact focus occurrence.",
                    nameof(binding));
            }

            Package = package;
            Binding = binding;
            ScopeResult = scopeResult;
            LibraryIntent = libraryIntent;
        }

        internal BrowserSpotlightPackageRequest<TPackageRequest> Package
            { get; }

        internal PackageRootBinding Binding { get; }

        internal WorkspaceScopeOperationResult ScopeResult { get; }

        internal TLibraryIntent? LibraryIntent { get; }
    }

    internal sealed record CurrentPackageLibrary :
        BrowserSpotlightPackageFocusRequest<
            TPackageRequest,
            TLibraryIntent>
    {
        internal CurrentPackageLibrary(
            WorkspaceScopeSnapshot scope,
            WorkspacePackageOccurrence occurrence,
            BrowserSpotlightPackageRequest<TPackageRequest> package,
            TLibraryIntent intent)
            : base(scope, occurrence)
        {
            ArgumentNullException.ThrowIfNull(package);
            ArgumentNullException.ThrowIfNull(intent);
            Package = package;
            Intent = intent;
        }

        internal BrowserSpotlightPackageRequest<TPackageRequest> Package
            { get; }

        internal TLibraryIntent Intent { get; }
    }
}

internal delegate ValueTask<NavigationOperationResult>
    BrowserSpotlightPackageFocusOperation<
        TPackageRequest,
        TLibraryIntent>(
        BrowserSpotlightPackageFocusRequest<
            TPackageRequest,
            TLibraryIntent> request,
        CancellationToken cancellationToken)
    where TPackageRequest : class
    where TLibraryIntent : class;

internal abstract record BrowserSpotlightPackageFocusResult
{
    private protected BrowserSpotlightPackageFocusResult()
    {
    }

    internal sealed record Completed :
        BrowserSpotlightPackageFocusResult
    {
        internal Completed(NavigationOperationResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            Result = result;
        }

        internal NavigationOperationResult Result { get; }
    }

    internal sealed record NotAttempted :
        BrowserSpotlightPackageFocusResult
    {
        internal NotAttempted(WorkspaceScopeOperationResult scopeResult)
        {
            ArgumentNullException.ThrowIfNull(scopeResult);
            if (scopeResult is WorkspaceScopeOperationResult.Committed
                or WorkspaceScopeOperationResult.NoEffect)
            {
                throw new ArgumentException(
                    "A successful Scope result requires a focus attempt or an explicit block.",
                    nameof(scopeResult));
            }

            ScopeResult = scopeResult;
        }

        internal WorkspaceScopeOperationResult ScopeResult { get; }
    }

    internal sealed record Blocked :
        BrowserSpotlightPackageFocusResult
    {
        internal Blocked(BrowserSpotlightActivationBlock reason)
        {
            ArgumentNullException.ThrowIfNull(reason);
            Reason = reason;
        }

        internal BrowserSpotlightActivationBlock Reason { get; }
    }
}

internal abstract record BrowserSpotlightCurrentPackageActivationResult<
    TPackageRequest,
    TNavigationAction,
    TPlatformAction,
    TLibraryIntent,
    TPackageFailure>
    where TPackageRequest : class
    where TNavigationAction : class
    where TPlatformAction : class
    where TLibraryIntent : class
    where TPackageFailure : class
{
    private protected BrowserSpotlightCurrentPackageActivationResult(
        BrowserSpotlightDestinationDescriptor<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
    }

    internal BrowserSpotlightDestinationDescriptor<
        TPackageRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent> Descriptor { get; }

    internal sealed record Blocked :
        BrowserSpotlightCurrentPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>
    {
        internal Blocked(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            BrowserSpotlightActivationBlock reason)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(reason);
            Reason = reason;
        }

        internal BrowserSpotlightActivationBlock Reason { get; }
    }

    internal sealed record PackageNotAcquired :
        BrowserSpotlightCurrentPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>
    {
        internal PackageNotAcquired(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            TPackageFailure result)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(result);
            Result = result;
        }

        internal TPackageFailure Result { get; }
    }

    internal sealed record PackageAcquiredButBlocked :
        BrowserSpotlightCurrentPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>
    {
        internal PackageAcquiredButBlocked(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            PackageRootBinding binding,
            BrowserSpotlightActivationBlock reason)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(binding);
            ArgumentNullException.ThrowIfNull(reason);
            Binding = binding;
            Reason = reason;
        }

        internal PackageRootBinding Binding { get; }

        internal BrowserSpotlightActivationBlock Reason { get; }
    }

    internal sealed record Settled :
        BrowserSpotlightCurrentPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>
    {
        internal Settled(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            BrowserSpotlightPackageMembershipResult membership,
            BrowserSpotlightPackageFocusResult focus)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(membership);
            ArgumentNullException.ThrowIfNull(focus);
            Membership = membership;
            Focus = focus;
        }

        internal BrowserSpotlightPackageMembershipResult Membership
            { get; }

        internal BrowserSpotlightPackageFocusResult Focus { get; }
    }
}

internal static class BrowserSpotlightCurrentPackageActivation
{
    internal static async ValueTask<
        BrowserSpotlightCurrentPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>>
        ExecuteAsync<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>(
            InspectionWorkspace workspace,
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            BrowserSpotlightPackageAcquisitionOperation<
                TPackageRequest,
                TPackageFailure> acquire,
            BrowserSpotlightPackageFocusOperation<
                TPackageRequest,
                TLibraryIntent> focus,
            DateTimeOffset deadline,
            CancellationToken cancellationToken = default)
        where TPackageRequest : class
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class
        where TPackageFailure : class
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(acquire);
        ArgumentNullException.ThrowIfNull(focus);

        bool supported = descriptor.Plan
            is BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.AddCurrentPackage
            or BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.ActivateCurrentPackageLibrary;
        if (!supported)
        {
            throw new ArgumentException(
                "Current Package activation requires a Package admission or current Package Library plan.",
                nameof(descriptor));
        }

        BrowserSpotlightActivationBasisRead initial =
            await BrowserSpotlightActivationAuthority.ReadAsync(
                workspace,
                descriptor.Basis).ConfigureAwait(false);
        if (initial.Block is { } initialBlock)
        {
            return new BrowserSpotlightCurrentPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TPackageFailure>.Blocked(
                    descriptor,
                    initialBlock);
        }

        return descriptor.Plan switch
        {
            BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.ActivateCurrentPackageLibrary current =>
                await FocusCurrentLibraryAsync<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent,
                    TPackageFailure>(
                    descriptor,
                    current,
                    initial.Scope!,
                    focus,
                    cancellationToken).ConfigureAwait(false),

            BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.AddCurrentPackage add =>
                await AddAndFocusAsync(
                    workspace,
                    descriptor,
                    add,
                    acquire,
                    focus,
                    deadline,
                    cancellationToken).ConfigureAwait(false),

            _ => throw new InvalidOperationException(
                "Unknown current Package activation plan."),
        };
    }

    private static async ValueTask<
        BrowserSpotlightCurrentPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>>
        FocusCurrentLibraryAsync<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.ActivateCurrentPackageLibrary plan,
            WorkspaceScopeSnapshot currentScope,
            BrowserSpotlightPackageFocusOperation<
                TPackageRequest,
                TLibraryIntent> focus,
            CancellationToken cancellationToken)
        where TPackageRequest : class
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class
        where TPackageFailure : class
    {
        if (!currentScope.Packages.Any(package =>
                ReferenceEquals(package.Occurrence, plan.Package)))
        {
            return new BrowserSpotlightCurrentPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TPackageFailure>.Blocked(
                    descriptor,
                    new BrowserSpotlightActivationBlock.Stale(
                        BrowserSpotlightActivationStaleReason
                            .PackageOccurrence,
                        currentScope,
                        descriptor.Basis.Registrations));
        }
        if (descriptor.Destination
            is not BrowserSpotlightDestination<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.CurrentPackageLibrary destination)
        {
            throw new InvalidOperationException(
                "A current Package Library plan requires its exact projected destination.");
        }

        var membership =
            new BrowserSpotlightPackageMembershipResult.AlreadyCurrent(
                currentScope,
                plan.Package);
        NavigationOperationResult navigation = await focus(
            new BrowserSpotlightPackageFocusRequest<
                TPackageRequest,
                TLibraryIntent>.CurrentPackageLibrary(
                    currentScope,
                    plan.Package,
                    destination.PackageRequest,
                    plan.Intent),
            cancellationToken).ConfigureAwait(false);
        return new BrowserSpotlightCurrentPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>.Settled(
                descriptor,
                membership,
                new BrowserSpotlightPackageFocusResult.Completed(
                    navigation));
    }

    private static async ValueTask<
        BrowserSpotlightCurrentPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>>
        AddAndFocusAsync<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>(
            InspectionWorkspace workspace,
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.AddCurrentPackage plan,
            BrowserSpotlightPackageAcquisitionOperation<
                TPackageRequest,
                TPackageFailure> acquire,
            BrowserSpotlightPackageFocusOperation<
                TPackageRequest,
                TLibraryIntent> focus,
            DateTimeOffset deadline,
            CancellationToken cancellationToken)
        where TPackageRequest : class
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class
        where TPackageFailure : class
    {
        BrowserSpotlightPackageAcquisitionResult<TPackageFailure>
            acquisition = await acquire(
                plan.Package,
                cancellationToken).ConfigureAwait(false);
        if (acquisition
            is BrowserSpotlightPackageAcquisitionResult<
                TPackageFailure>.NotAcquired notAcquired)
        {
            return new BrowserSpotlightCurrentPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TPackageFailure>.PackageNotAcquired(
                    descriptor,
                    notAcquired.Result);
        }

        PackageRootBinding binding =
            ((BrowserSpotlightPackageAcquisitionResult<
                TPackageFailure>.Acquired)acquisition).Binding;
        ValidateBinding(plan.Package, binding);

        BrowserSpotlightActivationBasisRead beforeScope =
            await BrowserSpotlightActivationAuthority.ReadAsync(
                workspace,
                descriptor.Basis).ConfigureAwait(false);
        if (beforeScope.Block is { } beforeScopeBlock)
        {
            return new BrowserSpotlightCurrentPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TPackageFailure>.PackageAcquiredButBlocked(
                    descriptor,
                    binding,
                    beforeScopeBlock);
        }

        WorkspaceScopeOperationResult scopeResult =
            await workspace.AddPackagesAsync(
                descriptor.Basis.Scope.Revision,
                descriptor.Basis.Scope.PublicationBase,
                [binding],
                deadline,
                cancellationToken).ConfigureAwait(false);

        WorkspaceScopeSnapshot? scope = scopeResult switch
        {
            WorkspaceScopeOperationResult.Committed committed =>
                committed.Snapshot,
            WorkspaceScopeOperationResult.NoEffect noEffect =>
                noEffect.Snapshot,
            _ => null,
        };
        if (scope is null)
        {
            return new BrowserSpotlightCurrentPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TPackageFailure>.Settled(
                    descriptor,
                    new BrowserSpotlightPackageMembershipResult.NotCommitted(
                        scopeResult),
                    new BrowserSpotlightPackageFocusResult.NotAttempted(
                        scopeResult));
        }

        WorkspacePackageOccurrence occurrence =
            scope.FindPackageOccurrence(binding)?.Occurrence
            ?? throw new InvalidOperationException(
                "A successful Scope admission did not retain its exact Package occurrence.");
        BrowserSpotlightPackageMembershipResult membership =
            scopeResult switch
            {
                WorkspaceScopeOperationResult.Committed committed =>
                    new BrowserSpotlightPackageMembershipResult.Committed(
                        committed,
                        occurrence),
                WorkspaceScopeOperationResult.NoEffect noEffect =>
                    new BrowserSpotlightPackageMembershipResult.NoEffect(
                        noEffect,
                        occurrence),
                _ => throw new InvalidOperationException(
                    "Unknown successful Scope result."),
            };

        var committedBasis = new BrowserSpotlightActivationBasis(
            descriptor.Basis.ResultGeneration,
            scope,
            descriptor.Basis.Registrations);
        BrowserSpotlightActivationBasisRead beforeFocus =
            await BrowserSpotlightActivationAuthority.ReadAsync(
                workspace,
                committedBasis).ConfigureAwait(false);
        if (beforeFocus.Block is { } beforeFocusBlock)
        {
            return new BrowserSpotlightCurrentPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TPackageFailure>.Settled(
                    descriptor,
                    membership,
                    new BrowserSpotlightPackageFocusResult.Blocked(
                        beforeFocusBlock));
        }

        NavigationOperationResult navigation = await focus(
            new BrowserSpotlightPackageFocusRequest<
                TPackageRequest,
                TLibraryIntent>.AfterScope(
                    scope,
                    occurrence,
                    plan.Package,
                    binding,
                    scopeResult,
                    plan.LibraryIntent),
            cancellationToken).ConfigureAwait(false);
        return new BrowserSpotlightCurrentPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>.Settled(
                descriptor,
                membership,
                new BrowserSpotlightPackageFocusResult.Completed(
                    navigation));
    }

    private static void ValidateBinding<TPackageRequest>(
        BrowserSpotlightPackageRequest<TPackageRequest> request,
        PackageRootBinding binding)
        where TPackageRequest : class
    {
        if (!request.Coordinate.PackageId.Equals(
                binding.Coordinate.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || !request.Coordinate.Version.Equals(
                binding.Coordinate.Version,
                StringComparison.OrdinalIgnoreCase)
            || request.WorkspaceCoordinate is { } expected
                && expected != binding.Coordinate)
        {
            throw new ArgumentException(
                "The acquired Package binding does not identify the exact Spotlight request.",
                nameof(binding));
        }
    }

}
