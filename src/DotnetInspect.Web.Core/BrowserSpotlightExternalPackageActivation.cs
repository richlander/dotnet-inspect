using DotnetInspector.Queries;

namespace DotnetInspect.Web;

internal sealed record BrowserSpotlightFreshWorkspaceAuthority(
    long ResultGeneration,
    InspectionWorkspaceIdentity SourceWorkspace,
    WorkspaceRegistrationRevisionIdentity RegistrationRevision,
    WorkspaceScopeRevisionIdentity ScopeRevision,
    WorkspaceScopePublicationBaseIdentity ScopePublicationBase)
{
    internal static BrowserSpotlightFreshWorkspaceAuthority From(
        BrowserSpotlightActivationBasis basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        return new(
            basis.ResultGeneration,
            basis.Scope.Revision.Workspace,
            basis.Registrations.Identity,
            basis.Scope.Revision.Identity,
            basis.Scope.PublicationBase);
    }
}

internal abstract record BrowserSpotlightRetainedWorkspaceAdmissionResult<
    THostAuthority,
    THostRejection>
    where THostAuthority : class
    where THostRejection : class
{
    private protected BrowserSpotlightRetainedWorkspaceAdmissionResult()
    {
    }

    internal sealed record Admitted(THostAuthority Authority) :
        BrowserSpotlightRetainedWorkspaceAdmissionResult<
            THostAuthority,
            THostRejection>;

    internal sealed record Rejected(THostRejection Result) :
        BrowserSpotlightRetainedWorkspaceAdmissionResult<
            THostAuthority,
            THostRejection>;
}

internal delegate BrowserSpotlightRetainedWorkspaceAdmissionResult<
    THostAuthority,
    THostRejection>
    BrowserSpotlightRetainedWorkspaceAdmissionOperation<
        THostAuthority,
        THostRejection>(
        BrowserSpotlightFreshWorkspaceAuthority authority)
    where THostAuthority : class
    where THostRejection : class;

internal delegate TDefinitionsRequest
    BrowserSpotlightWorkspaceRestorationRequestFactory<
        TPackageRequest,
        THostAuthority,
        TDefinitionsRequest>(
        BrowserSpotlightPackageRequest<TPackageRequest> package,
        WorkspacePlan curatedPlan,
        THostAuthority hostAuthority)
    where TPackageRequest : class
    where THostAuthority : class
    where TDefinitionsRequest : class;

internal abstract record BrowserSpotlightWorkspaceRestorationResult<
    TCompleteActivation,
    TDefinitionsFailure>
    where TCompleteActivation : class
    where TDefinitionsFailure : class
{
    private protected BrowserSpotlightWorkspaceRestorationResult()
    {
    }

    internal sealed record Complete(TCompleteActivation Activation) :
        BrowserSpotlightWorkspaceRestorationResult<
            TCompleteActivation,
            TDefinitionsFailure>;

    internal sealed record Failed(TDefinitionsFailure Result) :
        BrowserSpotlightWorkspaceRestorationResult<
            TCompleteActivation,
            TDefinitionsFailure>;
}

internal delegate ValueTask<
    BrowserSpotlightWorkspaceRestorationResult<
        TCompleteActivation,
        TDefinitionsFailure>>
    BrowserSpotlightWorkspaceRestorationOperation<
        TDefinitionsRequest,
        TCompleteActivation,
        TDefinitionsFailure>(
        TDefinitionsRequest request,
        CancellationToken cancellationToken)
    where TDefinitionsRequest : class
    where TCompleteActivation : class
    where TDefinitionsFailure : class;

internal sealed record BrowserSpotlightRetainedWorkspacePublicationRequest<
    THostAuthority,
    TCompleteActivation>(
    BrowserSpotlightFreshWorkspaceAuthority Authority,
    THostAuthority HostAuthority,
    TCompleteActivation Activation)
    where THostAuthority : class
    where TCompleteActivation : class;

internal abstract record BrowserSpotlightRetainedWorkspacePublicationResult<
    THostPublication,
    THostRejection>
    where THostPublication : class
    where THostRejection : class
{
    private protected BrowserSpotlightRetainedWorkspacePublicationResult()
    {
    }

    internal sealed record Published(THostPublication Result) :
        BrowserSpotlightRetainedWorkspacePublicationResult<
            THostPublication,
            THostRejection>;

    internal sealed record Rejected(THostRejection Result) :
        BrowserSpotlightRetainedWorkspacePublicationResult<
            THostPublication,
            THostRejection>;
}

internal delegate BrowserSpotlightRetainedWorkspacePublicationResult<
    THostPublication,
    THostRejection>
    BrowserSpotlightRetainedWorkspacePublicationOperation<
        THostAuthority,
        TCompleteActivation,
        THostPublication,
        THostRejection>(
        BrowserSpotlightRetainedWorkspacePublicationRequest<
            THostAuthority,
            TCompleteActivation> request)
    where THostAuthority : class
    where TCompleteActivation : class
    where THostPublication : class
    where THostRejection : class;

internal delegate ValueTask<TNonPostingResult>
    BrowserSpotlightWorkspaceNonPostingOperation<
        TCompleteActivation,
        TNonPostingResult>(
        TCompleteActivation activation)
    where TCompleteActivation : class
    where TNonPostingResult : class;

internal abstract record BrowserSpotlightFreshWorkspaceBlock<THostRejection>
    where THostRejection : class
{
    private protected BrowserSpotlightFreshWorkspaceBlock()
    {
    }

    internal sealed record Source(BrowserSpotlightActivationBlock Reason) :
        BrowserSpotlightFreshWorkspaceBlock<THostRejection>;

    internal sealed record Host(THostRejection Result) :
        BrowserSpotlightFreshWorkspaceBlock<THostRejection>;
}

internal abstract record BrowserSpotlightExternalPackageActivationResult<
    TPackageRequest,
    TNavigationAction,
    TPlatformAction,
    TLibraryIntent,
    TDefinitionsRequest,
    TCompleteActivation,
    TDefinitionsFailure,
    THostPublication,
    THostRejection,
    TNonPostingResult>
    where TPackageRequest : class
    where TNavigationAction : class
    where TPlatformAction : class
    where TLibraryIntent : class
    where TDefinitionsRequest : class
    where TCompleteActivation : class
    where TDefinitionsFailure : class
    where THostPublication : class
    where THostRejection : class
    where TNonPostingResult : class
{
    private protected BrowserSpotlightExternalPackageActivationResult(
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
        BrowserSpotlightExternalPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TDefinitionsRequest,
            TCompleteActivation,
            TDefinitionsFailure,
            THostPublication,
            THostRejection,
            TNonPostingResult>
    {
        internal Blocked(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            BrowserSpotlightFreshWorkspaceBlock<THostRejection> reason)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(reason);
            Reason = reason;
        }

        internal BrowserSpotlightFreshWorkspaceBlock<THostRejection> Reason
        {
            get;
        }
    }

    internal sealed record RestorationFailed :
        BrowserSpotlightExternalPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TDefinitionsRequest,
            TCompleteActivation,
            TDefinitionsFailure,
            THostPublication,
            THostRejection,
            TNonPostingResult>
    {
        internal RestorationFailed(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            TDefinitionsRequest request,
            TDefinitionsFailure result)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(result);
            Request = request;
            Result = result;
        }

        internal TDefinitionsRequest Request { get; }

        internal TDefinitionsFailure Result { get; }
    }

    internal sealed record CompleteButNotPublished :
        BrowserSpotlightExternalPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TDefinitionsRequest,
            TCompleteActivation,
            TDefinitionsFailure,
            THostPublication,
            THostRejection,
            TNonPostingResult>
    {
        internal CompleteButNotPublished(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            TDefinitionsRequest request,
            TCompleteActivation activation,
            BrowserSpotlightFreshWorkspaceBlock<THostRejection> reason,
            TNonPostingResult nonPosting)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(activation);
            ArgumentNullException.ThrowIfNull(reason);
            ArgumentNullException.ThrowIfNull(nonPosting);
            Request = request;
            Activation = activation;
            Reason = reason;
            NonPosting = nonPosting;
        }

        internal TDefinitionsRequest Request { get; }

        internal TCompleteActivation Activation { get; }

        internal BrowserSpotlightFreshWorkspaceBlock<THostRejection> Reason
        {
            get;
        }

        internal TNonPostingResult NonPosting { get; }
    }

    internal sealed record Published :
        BrowserSpotlightExternalPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TDefinitionsRequest,
            TCompleteActivation,
            TDefinitionsFailure,
            THostPublication,
            THostRejection,
            TNonPostingResult>
    {
        internal Published(
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            TDefinitionsRequest request,
            TCompleteActivation activation,
            THostPublication publication)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(activation);
            ArgumentNullException.ThrowIfNull(publication);
            Request = request;
            Activation = activation;
            Publication = publication;
        }

        internal TDefinitionsRequest Request { get; }

        internal TCompleteActivation Activation { get; }

        internal THostPublication Publication { get; }
    }
}

internal static class BrowserSpotlightExternalPackageActivation
{
    internal static async ValueTask<
        BrowserSpotlightExternalPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TDefinitionsRequest,
            TCompleteActivation,
            TDefinitionsFailure,
            THostPublication,
            THostRejection,
            TNonPostingResult>>
        ExecuteAsync<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            THostAuthority,
            TDefinitionsRequest,
            TCompleteActivation,
            TDefinitionsFailure,
            THostPublication,
            THostRejection,
            TNonPostingResult>(
            InspectionWorkspace sourceWorkspace,
            BrowserSpotlightDestinationDescriptor<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent> descriptor,
            WorkspacePlan curatedPlan,
            BrowserSpotlightRetainedWorkspaceAdmissionOperation<
                THostAuthority,
                THostRejection> admit,
            BrowserSpotlightWorkspaceRestorationRequestFactory<
                TPackageRequest,
                THostAuthority,
                TDefinitionsRequest> createRequest,
            BrowserSpotlightWorkspaceRestorationOperation<
                TDefinitionsRequest,
                TCompleteActivation,
                TDefinitionsFailure> restore,
            BrowserSpotlightRetainedWorkspacePublicationOperation<
                THostAuthority,
                TCompleteActivation,
                THostPublication,
                THostRejection> publish,
            BrowserSpotlightWorkspaceNonPostingOperation<
                TCompleteActivation,
                TNonPostingResult> nonPosting,
            CancellationToken cancellationToken = default)
        where TPackageRequest : class
        where TNavigationAction : class
        where TPlatformAction : class
        where TLibraryIntent : class
        where THostAuthority : class
        where TDefinitionsRequest : class
        where TCompleteActivation : class
        where TDefinitionsFailure : class
        where THostPublication : class
        where THostRejection : class
        where TNonPostingResult : class
    {
        ArgumentNullException.ThrowIfNull(sourceWorkspace);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(curatedPlan);
        ArgumentNullException.ThrowIfNull(admit);
        ArgumentNullException.ThrowIfNull(createRequest);
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(nonPosting);

        if (descriptor.Plan
            is not BrowserSpotlightDestinationActivationPlan<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent>.RestoreExternalPackageWorkspace plan)
        {
            throw new ArgumentException(
                "External Package activation requires an uncovered Package restoration plan.",
                nameof(descriptor));
        }

        BrowserSpotlightActivationBasisRead initial =
            await BrowserSpotlightActivationAuthority.ReadAsync(
                sourceWorkspace,
                descriptor.Basis).ConfigureAwait(false);
        if (initial.Block is { } initialBlock)
        {
            return new BrowserSpotlightExternalPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TDefinitionsRequest,
                TCompleteActivation,
                TDefinitionsFailure,
                THostPublication,
                THostRejection,
                TNonPostingResult>.Blocked(
                    descriptor,
                    new BrowserSpotlightFreshWorkspaceBlock<THostRejection>
                        .Source(initialBlock));
        }

        BrowserSpotlightFreshWorkspaceAuthority authority =
            BrowserSpotlightFreshWorkspaceAuthority.From(descriptor.Basis);
        BrowserSpotlightRetainedWorkspaceAdmissionResult<
            THostAuthority,
            THostRejection> admission = admit(authority);
        if (admission
            is BrowserSpotlightRetainedWorkspaceAdmissionResult<
                THostAuthority,
                THostRejection>.Rejected rejectedAdmission)
        {
            ArgumentNullException.ThrowIfNull(rejectedAdmission.Result);
            return new BrowserSpotlightExternalPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TDefinitionsRequest,
                TCompleteActivation,
                TDefinitionsFailure,
                THostPublication,
                THostRejection,
                TNonPostingResult>.Blocked(
                    descriptor,
                    new BrowserSpotlightFreshWorkspaceBlock<THostRejection>
                        .Host(rejectedAdmission.Result));
        }

        if (admission
            is not BrowserSpotlightRetainedWorkspaceAdmissionResult<
                THostAuthority,
                THostRejection>.Admitted admitted)
        {
            throw new InvalidOperationException(
                "Unknown retained Workspace admission result.");
        }
        THostAuthority hostAuthority = admitted.Authority;
        ArgumentNullException.ThrowIfNull(hostAuthority);
        TDefinitionsRequest request =
            createRequest(plan.Package, curatedPlan, hostAuthority);
        ArgumentNullException.ThrowIfNull(request);

        BrowserSpotlightWorkspaceRestorationResult<
            TCompleteActivation,
            TDefinitionsFailure> restoration =
            await restore(request, cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(restoration);
        if (restoration
            is BrowserSpotlightWorkspaceRestorationResult<
                TCompleteActivation,
                TDefinitionsFailure>.Failed failed)
        {
            return new BrowserSpotlightExternalPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TDefinitionsRequest,
                TCompleteActivation,
                TDefinitionsFailure,
                THostPublication,
                THostRejection,
                TNonPostingResult>.RestorationFailed(
                    descriptor,
                    request,
                    failed.Result);
        }

        if (restoration
            is not BrowserSpotlightWorkspaceRestorationResult<
                TCompleteActivation,
                TDefinitionsFailure>.Complete complete)
        {
            throw new InvalidOperationException(
                "Unknown Workspace restoration result.");
        }
        TCompleteActivation activation = complete.Activation;
        ArgumentNullException.ThrowIfNull(activation);

        BrowserSpotlightActivationBasisRead current =
            await BrowserSpotlightActivationAuthority.ReadAsync(
                sourceWorkspace,
                descriptor.Basis).ConfigureAwait(false);
        if (current.Block is { } currentBlock)
        {
            TNonPostingResult cleanup =
                await nonPosting(activation).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(cleanup);
            return new BrowserSpotlightExternalPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TDefinitionsRequest,
                TCompleteActivation,
                TDefinitionsFailure,
                THostPublication,
                THostRejection,
                TNonPostingResult>.CompleteButNotPublished(
                    descriptor,
                    request,
                    activation,
                    new BrowserSpotlightFreshWorkspaceBlock<THostRejection>
                        .Source(currentBlock),
                    cleanup);
        }

        BrowserSpotlightRetainedWorkspacePublicationResult<
            THostPublication,
            THostRejection> publication = publish(new(
                authority,
                hostAuthority,
                activation));
        ArgumentNullException.ThrowIfNull(publication);
        if (publication
            is BrowserSpotlightRetainedWorkspacePublicationResult<
                THostPublication,
                THostRejection>.Rejected rejectedPublication)
        {
            ArgumentNullException.ThrowIfNull(rejectedPublication.Result);
            TNonPostingResult cleanup =
                await nonPosting(activation).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(cleanup);
            return new BrowserSpotlightExternalPackageActivationResult<
                TPackageRequest,
                TNavigationAction,
                TPlatformAction,
                TLibraryIntent,
                TDefinitionsRequest,
                TCompleteActivation,
                TDefinitionsFailure,
                THostPublication,
                THostRejection,
                TNonPostingResult>.CompleteButNotPublished(
                    descriptor,
                    request,
                    activation,
                    new BrowserSpotlightFreshWorkspaceBlock<THostRejection>
                        .Host(rejectedPublication.Result),
                    cleanup);
        }

        if (publication
            is not BrowserSpotlightRetainedWorkspacePublicationResult<
                THostPublication,
                THostRejection>.Published publishedResult)
        {
            throw new InvalidOperationException(
                "Unknown retained Workspace publication result.");
        }
        THostPublication published = publishedResult.Result;
        ArgumentNullException.ThrowIfNull(published);
        return new BrowserSpotlightExternalPackageActivationResult<
            TPackageRequest,
            TNavigationAction,
            TPlatformAction,
            TLibraryIntent,
            TDefinitionsRequest,
            TCompleteActivation,
            TDefinitionsFailure,
            THostPublication,
            THostRejection,
            TNonPostingResult>.Published(
                descriptor,
                request,
                activation,
                published);
    }
}
