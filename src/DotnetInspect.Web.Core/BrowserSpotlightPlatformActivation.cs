using DotnetInspector.Queries;

namespace DotnetInspect.Web;

internal delegate ValueTask<TPlatformResult>
    BrowserSpotlightPlatformActivationOperation<
        TPlatformAction,
        TPlatformResult>(
        BrowserSpotlightPlatformAction<TPlatformAction> action,
        CancellationToken cancellationToken)
    where TPlatformAction : class
    where TPlatformResult : class;

internal abstract record BrowserSpotlightCurrentPlatformActivationResult<
    TPackageRequest,
    TNavigationAction,
    TPlatformAction,
    TLibraryIntent,
    TPlatformResult>
    where TPackageRequest : class
    where TNavigationAction : class
    where TPlatformAction : class
    where TLibraryIntent : class
    where TPlatformResult : class
{
    private protected BrowserSpotlightCurrentPlatformActivationResult(
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
        BrowserSpotlightCurrentPlatformActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPlatformResult>
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

    internal sealed record Settled :
        BrowserSpotlightCurrentPlatformActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPlatformResult>
    {
        internal Settled(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            BrowserSpotlightPlatformAction<TPlatformAction> action,
            TPlatformResult result)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(result);
            Action = action;
            Result = result;
        }

        internal BrowserSpotlightPlatformAction<TPlatformAction> Action
        {
            get;
        }

        internal TPlatformResult Result { get; }
    }
}

internal static class BrowserSpotlightCurrentPlatformActivation
{
    internal static async ValueTask<
        BrowserSpotlightCurrentPlatformActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPlatformResult>>
        ExecuteAsync<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPlatformResult>(
            InspectionWorkspace workspace,
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            BrowserSpotlightPlatformActivationOperation<
                TPlatformAction,
                TPlatformResult> activate,
            CancellationToken cancellationToken = default)
        where TPackageRequest : class
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class
        where TPlatformResult : class
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(activate);

        if (descriptor.Plan
            is not BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.ActivateCurrentPlatformDestination plan)
        {
            throw new ArgumentException(
                "Current Platform activation requires a Platform destination plan.",
                nameof(descriptor));
        }

        BrowserSpotlightActivationBasisRead current =
            await BrowserSpotlightActivationAuthority.ReadAsync(
                workspace,
                descriptor.Basis).ConfigureAwait(false);
        if (current.Block is { } block)
        {
            return new BrowserSpotlightCurrentPlatformActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TPlatformResult>.Blocked(
                    descriptor,
                    block);
        }

        TPlatformResult result = await activate(
            plan.Action,
            cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(result);
        return new BrowserSpotlightCurrentPlatformActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPlatformResult>.Settled(
                descriptor,
                plan.Action,
                result);
    }
}
