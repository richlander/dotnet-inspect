using System.Runtime.Versioning;
using DotnetInspector.Queries;

namespace DotnetInspect.Web;

internal delegate ValueTask<NavigationOperationResult>
    BrowserSpotlightCurrentNavigationOperation<TNavigationAction>(
        TNavigationAction action,
        CancellationToken cancellationToken)
    where TNavigationAction : class;

internal abstract record BrowserSpotlightRetainedCurrentActivationResult<
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
    private protected BrowserSpotlightRetainedCurrentActivationResult(
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

    internal sealed record NotAdmitted :
        BrowserSpotlightRetainedCurrentActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>
    {
        internal NotAdmitted(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            WorkspaceRealizationOperationAdmission.Unavailable admission)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(admission);
            Admission = admission;
        }

        internal WorkspaceRealizationOperationAdmission.Unavailable Admission
        {
            get;
        }
    }

    internal sealed record Blocked :
        BrowserSpotlightRetainedCurrentActivationResult<
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

    internal sealed record Navigated :
        BrowserSpotlightRetainedCurrentActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>
    {
        internal Navigated(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            NavigationOperationResult result)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(result);
            Result = result;
        }

        internal NavigationOperationResult Result { get; }
    }

    internal sealed record PackageOperation :
        BrowserSpotlightRetainedCurrentActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>
    {
        internal PackageOperation(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            BrowserSpotlightCurrentPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TPackageFailure> result)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(result);
            Result = result;
        }

        internal BrowserSpotlightCurrentPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure> Result { get; }
    }
}

[SupportedOSPlatform("browser")]
internal static class BrowserSpotlightRetainedCurrentActivation
{
    internal static async ValueTask<
        BrowserSpotlightRetainedCurrentActivationResult<
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
            BrowserRetainedWorkspaceActivationOwner owner,
            string sourceRetainedDefinitionId,
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            BrowserSpotlightCurrentNavigationOperation<TNavigationAction>
                navigate,
            BrowserSpotlightPackageAcquisitionOperation<
                TPackageRequest,
                TPackageFailure> acquirePackage,
            BrowserSpotlightPackageFocusOperation<
                TPackageRequest,
                TLibraryIntent> focusPackage,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        where TPackageRequest : class
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class
        where TPackageFailure : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourceRetainedDefinitionId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(navigate);
        ArgumentNullException.ThrowIfNull(acquirePackage);
        ArgumentNullException.ThrowIfNull(focusPackage);

        bool isNavigation =
            descriptor.Plan
                is BrowserSpotlightDestinationActivationPlan<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent>.NavigateCurrent;
        bool isPackage =
            descriptor.Plan
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
        if (!isNavigation && !isPackage)
        {
            throw new ArgumentException(
                "Retained current Spotlight activation requires a captured current-subject or current-Package plan.",
                nameof(descriptor));
        }

        WorkspaceRealizationOperationAdmission admission =
            await owner.EnterOperationAsync(
                    sourceRetainedDefinitionId,
                    cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is WorkspaceRealizationOperationAdmission.Unavailable unavailable)
        {
            return new BrowserSpotlightRetainedCurrentActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TPackageFailure>.NotAdmitted(
                    descriptor,
                    unavailable);
        }

        using WorkspaceRealizationOperationLease operation =
            ((WorkspaceRealizationOperationAdmission.Admitted)admission).Lease;

        if (descriptor.Plan
            is BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.NavigateCurrent navigation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BrowserSpotlightActivationBasisRead authority =
                await BrowserSpotlightActivationAuthority.ReadAsync(
                        operation.Workspace,
                        descriptor.Basis)
                    .ConfigureAwait(false);
            if (authority.Block is { } block)
            {
                return new BrowserSpotlightRetainedCurrentActivationResult<
                    TPackageRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent,
                    TPackageFailure>.Blocked(
                        descriptor,
                        block);
            }

            NavigationOperationResult result =
                await navigate(
                        navigation.Action,
                        cancellationToken)
                    .ConfigureAwait(false);
            return new BrowserSpotlightRetainedCurrentActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TPackageFailure>.Navigated(
                    descriptor,
                    result);
        }

        BrowserSpotlightCurrentPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure> packageResult =
                await BrowserSpotlightCurrentPackageActivation.ExecuteAsync(
                        operation.Workspace,
                        descriptor,
                        acquirePackage,
                        focusPackage,
                        expiresAt,
                        cancellationToken)
                    .ConfigureAwait(false);
        return new BrowserSpotlightRetainedCurrentActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TPackageFailure>.PackageOperation(
                descriptor,
                packageResult);
    }
}
