using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Web;

internal sealed record BrowserSpotlightExternalPackageWorkspaceRequest(
    PackageSourceCoordinate Package,
    WorkspacePlan CuratedPlan,
    BrowserRetainedWorkspaceActivationRequest Activation);

internal static class BrowserSpotlightExternalPackageWorkspaceRequestFactory
{
    const int SchemaVersion = InspectionDefinitionSchema.Version3;
    const string WorkspaceId = "spotlight-workspace";
    const string ContextId = "spotlight-package";
    const string NavigationId = "spotlight-navigation";
    const string PackageNavigationId = "spotlight-package";
    const string ViewId = "spotlight-view";
    const string ScenarioId = "spotlight-scenario";

    internal static BrowserSpotlightExternalPackageWorkspaceRequest Create(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        PackageSourceCoordinate package,
        WorkspacePlan curatedPlan)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(curatedPlan);
        if (!curatedPlan.Contexts.IsEmpty)
        {
            throw new ArgumentException(
                "The Spotlight curated plan must contain registrations only.",
                nameof(curatedPlan));
        }
        if (!ReferenceEquals(
                curatedPlan.TraversalTargetPolicy,
                TraversalTargetFrameworkPolicy.ProductDefault))
        {
            throw new ArgumentException(
                "The Spotlight curated plan must retain the product-default traversal target policy.",
                nameof(curatedPlan));
        }

        string framework =
            curatedPlan.TraversalTargetPolicy.TargetFramework;
        var coordinate = new DefinitionMemberCoordinate.PackageCoordinate(
            package.PackageId,
            package.Version,
            framework);
        InspectionDefinitionRecord[] records =
        [
            new WorkspaceDefinition(
                SchemaVersion,
                WorkspaceId,
                [
                    new WorkspaceContextDefinition(
                        ContextId,
                        framework,
                        members: [coordinate]),
                ],
                title: label,
                registrations: curatedPlan.Registrations),
            new CommittedNavigationDefinition(
                SchemaVersion,
                NavigationId,
                [
                    new NavigationTabDefinition(
                        PackageNavigationId,
                        coordinate: coordinate),
                ],
                focus: PackageNavigationId),
            new CommittedViewDefinition(
                SchemaVersion,
                ViewId,
                [
                    new CommittedViewStateDefinition(
                        navigation: null,
                        new PortableSubjectRequest.Workspace()),
                    new CommittedViewStateDefinition(
                        PackageNavigationId,
                        new PortableSubjectRequest.Package(),
                        new PortableRetainedSubjectContext.Package()),
                ]),
            new ScenarioDefinition(
                SchemaVersion,
                ScenarioId,
                workspace: WorkspaceId,
                context: ContextId,
                view: ViewId,
                navigation: NavigationId),
        ];
        var restoration = new CompleteRestorationRequestBasis.DefinitionInput(
            ScenarioId,
            records);
        return new(
            package,
            curatedPlan,
            new BrowserRetainedWorkspaceActivationRequest(
                retainedDefinitionId,
                label,
                canonicalLocation,
                restoration));
    }
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserSpotlightRetainedWorkspaceHostAuthority
{
    CancellationTokenRegistration _cancellationRegistration;
    int _cancellationBound;
    int _completed;

    internal BrowserSpotlightRetainedWorkspaceHostAuthority(
        BrowserRetainedWorkspaceActivationOwner owner,
        BrowserRetainedWorkspaceActivationIntent intent,
        BrowserRetainedWorkspacePosting sourcePosting,
        BrowserSpotlightFreshWorkspaceAuthority authority)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        SourcePosting = sourcePosting
            ?? throw new ArgumentNullException(nameof(sourcePosting));
        Authority = authority
            ?? throw new ArgumentNullException(nameof(authority));
    }

    internal BrowserRetainedWorkspaceActivationOwner Owner { get; }

    internal BrowserRetainedWorkspaceActivationIntent Intent { get; }

    internal BrowserRetainedWorkspacePosting SourcePosting
    {
        get;
    }

    internal BrowserSpotlightFreshWorkspaceAuthority Authority { get; }

    internal void BindCancellation(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _cancellationBound, 1) != 0)
        {
            throw new InvalidOperationException(
                "Spotlight cancellation is already bound.");
        }

        _cancellationRegistration = cancellationToken.UnsafeRegister(
            static state =>
                ((BrowserRetainedWorkspaceActivationIntent)state!).Cancel(),
            Intent);
    }

    internal void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _cancellationRegistration.Dispose();
            Owner.CompleteSpotlightActivation(this);
        }
    }
}

[SupportedOSPlatform("browser")]
internal static class BrowserSpotlightRetainedWorkspaceActivation
{
    internal static async ValueTask<
        BrowserSpotlightExternalPackageActivationResult<
            BrowserSpotlightExternalPackageWorkspaceRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            BrowserRetainedWorkspaceActivationRequest,
            BrowserPreparedWorkspaceActivation,
            CompleteRestorationFailure,
            BrowserRetainedWorkspacePosting,
            BrowserRetainedWorkspaceActivationRejection,
            BrowserRetainedWorkspaceNonPostingResult>>
        ExecuteAsync<TNavigationAction, TPlatformAction, TLibraryIntent>(
            BrowserRetainedWorkspaceActivationOwner owner,
            string sourceRetainedDefinitionId,
            BrowserSpotlightDestinationDescriptor<
                BrowserSpotlightExternalPackageWorkspaceRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            CancellationToken cancellationToken = default,
            Action? preparedObserver = null)
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourceRetainedDefinitionId);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.Plan
            is not BrowserSpotlightDestinationActivationPlan<
                BrowserSpotlightExternalPackageWorkspaceRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.RestoreExternalPackageWorkspace plan)
        {
            throw new ArgumentException(
                "Retained Spotlight activation requires a captured external-Package plan.",
                nameof(descriptor));
        }

        BrowserSpotlightExternalPackageWorkspaceRequest external =
            plan.Package.Request;
        if (!Equals(external.Package, plan.Package.Coordinate))
        {
            throw new ArgumentException(
                "The captured Spotlight request must retain the exact Package coordinate.",
                nameof(descriptor));
        }

        WorkspaceRealizationOperationAdmission admission =
            await owner.EnterOperationAsync(
                    sourceRetainedDefinitionId,
                    cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            var unavailable =
                (WorkspaceRealizationOperationAdmission.Unavailable)admission;
            return Blocked<TNavigationAction, TPlatformAction, TLibraryIntent>(
                descriptor,
                $"The captured Spotlight source Workspace is unavailable: "
                    + $"{unavailable.Reason}.");
        }

        using WorkspaceRealizationOperationLease source = admitted.Lease;
        BrowserSpotlightRetainedWorkspaceHostAuthority? hostAuthority = null;
        BrowserPreparedWorkspaceActivation? prepared = null;
        try
        {
            BrowserSpotlightExternalPackageActivationResult<
                BrowserSpotlightExternalPackageWorkspaceRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                BrowserRetainedWorkspaceActivationRequest,
                BrowserPreparedWorkspaceActivation,
                CompleteRestorationFailure,
                BrowserRetainedWorkspacePosting,
                BrowserRetainedWorkspaceActivationRejection,
                BrowserRetainedWorkspaceNonPostingResult> result =
                await BrowserSpotlightExternalPackageActivation.ExecuteAsync<
                    BrowserSpotlightExternalPackageWorkspaceRequest,
                    TNavigationAction,
                    TPlatformAction,
                    TLibraryIntent,
                    BrowserSpotlightRetainedWorkspaceHostAuthority,
                    BrowserRetainedWorkspaceActivationRequest,
                    BrowserPreparedWorkspaceActivation,
                    CompleteRestorationFailure,
                    BrowserRetainedWorkspacePosting,
                    BrowserRetainedWorkspaceActivationRejection,
                    BrowserRetainedWorkspaceNonPostingResult>(
                        source.Workspace,
                        descriptor,
                        external.CuratedPlan,
                        authority =>
                        {
                            BrowserSpotlightRetainedWorkspaceAdmissionResult<
                                BrowserSpotlightRetainedWorkspaceHostAuthority,
                                BrowserRetainedWorkspaceActivationRejection>
                                hostAdmission =
                                    owner.AdmitSpotlightActivation(
                                        sourceRetainedDefinitionId,
                                        authority,
                                        external.Activation);
                            if (hostAdmission
                                is BrowserSpotlightRetainedWorkspaceAdmissionResult<
                                    BrowserSpotlightRetainedWorkspaceHostAuthority,
                                    BrowserRetainedWorkspaceActivationRejection>
                                    .Admitted admittedHost)
                            {
                                hostAuthority = admittedHost.Authority;
                                hostAuthority.BindCancellation(
                                    cancellationToken);
                            }
                            return hostAdmission;
                        },
                        (package, curatedPlan, authority) =>
                        {
                            if (!ReferenceEquals(
                                    package.Request,
                                    external)
                                || !ReferenceEquals(
                                    curatedPlan,
                                    external.CuratedPlan)
                                || !ReferenceEquals(
                                    authority,
                                    hostAuthority))
                            {
                                throw new InvalidOperationException(
                                    "Spotlight restoration did not retain its captured request and host authority.");
                            }
                            return external.Activation;
                        },
                        async (_, token) =>
                        {
                            BrowserSpotlightWorkspaceRestorationResult<
                                BrowserPreparedWorkspaceActivation,
                                CompleteRestorationFailure> restoration =
                                    await owner.RestoreSpotlightAsync(
                                            hostAuthority
                                            ?? throw new InvalidOperationException(
                                                "Spotlight restoration requires admitted host authority."),
                                            token)
                                        .ConfigureAwait(false);
                            if (restoration
                                is BrowserSpotlightWorkspaceRestorationResult<
                                    BrowserPreparedWorkspaceActivation,
                                    CompleteRestorationFailure>.Complete
                                    complete)
                            {
                                prepared = complete.Activation;
                                preparedObserver?.Invoke();
                            }
                            return restoration;
                        },
                        owner.PublishSpotlightActivation,
                        static activation => activation.SettleAsync(),
                        cancellationToken)
                    .ConfigureAwait(false);

            if (prepared is { IsPending: true })
            {
                BrowserRetainedWorkspaceNonPostingResult cleanup =
                    await prepared.SettleAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    cleanup.Failure is null
                        ? "Spotlight activation returned without publishing or settling its candidate."
                        : cleanup.Failure.Message);
            }
            return result;
        }
        catch (Exception failure)
        {
            if (prepared is { IsPending: true })
            {
                BrowserRetainedWorkspaceNonPostingResult cleanup =
                    await prepared.SettleAsync().ConfigureAwait(false);
                if (cleanup.Failure is not null)
                {
                    throw new AggregateException(
                        failure,
                        new InvalidOperationException(
                            cleanup.Failure.Message));
                }
            }
            throw;
        }
        finally
        {
            hostAuthority?.Complete();
        }
    }

    static BrowserSpotlightExternalPackageActivationResult<
        BrowserSpotlightExternalPackageWorkspaceRequest,
        TNavigationAction,
        TPlatformAction,
        TLibraryIntent,
        BrowserRetainedWorkspaceActivationRequest,
        BrowserPreparedWorkspaceActivation,
        CompleteRestorationFailure,
        BrowserRetainedWorkspacePosting,
        BrowserRetainedWorkspaceActivationRejection,
        BrowserRetainedWorkspaceNonPostingResult>
        Blocked<TNavigationAction, TPlatformAction, TLibraryIntent>(
            BrowserSpotlightDestinationDescriptor<
                BrowserSpotlightExternalPackageWorkspaceRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            string message)
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class =>
        new BrowserSpotlightExternalPackageActivationResult<
            BrowserSpotlightExternalPackageWorkspaceRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            BrowserRetainedWorkspaceActivationRequest,
            BrowserPreparedWorkspaceActivation,
            CompleteRestorationFailure,
            BrowserRetainedWorkspacePosting,
            BrowserRetainedWorkspaceActivationRejection,
            BrowserRetainedWorkspaceNonPostingResult>.Blocked(
                descriptor,
                new BrowserSpotlightFreshWorkspaceBlock<
                    BrowserRetainedWorkspaceActivationRejection>.Host(
                        new(message)));
}
