using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace DotnetInspector.Queries;

public sealed partial class InspectionWorkspace
{
    /// <summary>
    /// Issues one finite analysis-universe description and its authenticated
    /// Workspace binding route.
    /// </summary>
    public AnalysisUniverseOffer CreateAnalysisUniverseOffer(
        IAnalysisUniverseIdentity identity,
        IAnalysisUniverseBoundary requestedBoundary,
        IAnalysisUniverseBoundary realizedBoundary,
        IEnumerable<AnalysisUniverseCapabilityDescriptor> capabilities,
        IAnalysisUniverseCompleteness completeness,
        IEnumerable<AnalysisUniverseCapabilityRegistration> registrations,
        IEnumerable<IAnalysisUniverseFailure>? failures = null)
    {
        var description = new AnalysisUniverseDescription(
            identity,
            requestedBoundary,
            realizedBoundary,
            isFinite: true,
            capabilities,
            completeness,
            failures);
        var offer = new AnalysisUniverseOffer(
            this,
            description,
            registrations);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            return offer;
        }
    }

    internal AnalysisUniverseIssuanceResult
        IssueAnalysisUniverseExecutionAccess(
            AnalysisUniverseOffer offer,
            AnalysisRequestPlan plan,
            CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(offer.Provider, this))
        {
            return AnalysisUniverseExecutionAccess.Rejected(
                AnalysisUniverseIssuanceRejectionReason
                    .ForeignProviderOffer);
        }

        if (!ReferenceEquals(plan.Universe, offer.Description))
        {
            return AnalysisUniverseExecutionAccess.Rejected(
                AnalysisUniverseIssuanceRejectionReason
                    .DescriptionMismatch);
        }

        if (!IsOpen())
        {
            return AnalysisUniverseExecutionAccess.Rejected(
                AnalysisUniverseIssuanceRejectionReason
                    .WorkspaceUnavailable);
        }

        if (cancellationToken.IsCancellationRequested)
            return new AnalysisUniverseIssuanceResult.Cancelled();

        var registrationByCapability = new Dictionary<
            AnalysisUniverseCapabilityDescriptor,
            AnalysisUniverseCapabilityRegistration>(
                ReferenceEqualityComparer.Instance);
        foreach (AnalysisUniverseRequirementDescriptor requirement
            in plan.UniverseRequirements)
        {
            AnalysisUniverseCapabilityDescriptor capability =
                requirement.Capability;
            if (registrationByCapability.ContainsKey(capability))
                continue;

            AnalysisUniverseCapabilityRegistration[] exact =
            [
                .. offer.Realization.Registrations.Where(registration =>
                    ReferenceEquals(
                        registration.Capability,
                        capability)),
            ];
            if (exact.Length == 0)
            {
                bool hasLookalike =
                    offer.Realization.Registrations.Any(registration =>
                        registration.Capability.Id == capability.Id);
                return AnalysisUniverseExecutionAccess.Rejected(
                    hasLookalike
                        ? AnalysisUniverseIssuanceRejectionReason
                            .WrongCapabilityIdentity
                        : AnalysisUniverseIssuanceRejectionReason
                            .MissingExecutableBinding,
                    requirement,
                    capability);
            }

            if (exact.Length > 1)
            {
                return AnalysisUniverseExecutionAccess.Rejected(
                    AnalysisUniverseIssuanceRejectionReason
                        .DuplicateExecutableBinding,
                    requirement,
                    capability);
            }

            registrationByCapability.Add(capability, exact[0]);
        }

        var handles = new List<AnalysisUniverseCapabilityHandle>();
        var handleByCapability = new Dictionary<
            AnalysisUniverseCapabilityDescriptor,
            AnalysisUniverseCapabilityHandle>(
                ReferenceEqualityComparer.Instance);
        try
        {
            foreach (AnalysisUniverseRequirementDescriptor requirement
                in plan.UniverseRequirements)
            {
                AnalysisUniverseCapabilityDescriptor capability =
                    requirement.Capability;
                if (handleByCapability.ContainsKey(capability))
                    continue;

                if (cancellationToken.IsCancellationRequested)
                {
                    ReleaseHandles(handles);
                    return new AnalysisUniverseIssuanceResult.Cancelled();
                }

                AnalysisUniverseCapabilityAcquisition acquisition =
                    registrationByCapability[capability].Acquire(
                        plan,
                        cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    if (acquisition
                        is AnalysisUniverseCapabilityAcquisition.Ready
                            cancelledReady)
                    {
                        handles.Add(cancelledReady.Handle);
                    }

                    ReleaseHandles(handles);
                    return new AnalysisUniverseIssuanceResult.Cancelled();
                }

                switch (acquisition)
                {
                    case AnalysisUniverseCapabilityAcquisition.Ready ready:
                        handles.Add(ready.Handle);
                        handleByCapability.Add(
                            capability,
                            ready.Handle);
                        break;

                    case AnalysisUniverseCapabilityAcquisition.Rejected
                        rejected:
                        ReleaseHandles(handles);
                        return AnalysisUniverseExecutionAccess.Rejected(
                            AnalysisUniverseIssuanceRejectionReason
                                .CapabilityRejected,
                            requirement,
                            capability,
                            rejected.Rejection);

                    case AnalysisUniverseCapabilityAcquisition.Cancelled:
                        ReleaseHandles(handles);
                        return new AnalysisUniverseIssuanceResult.Cancelled();

                    default:
                        throw new InvalidOperationException(
                            "The capability registration returned an unknown acquisition outcome.");
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            ReleaseHandles(handles);
            return new AnalysisUniverseIssuanceResult.Cancelled();
        }
        catch (Exception ex)
        {
            List<Exception>? cleanupFailures =
                TryReleaseHandles(handles);
            if (cleanupFailures is null)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
                throw new InvalidOperationException(
                    "Unreachable after rethrow.");
            }

            cleanupFailures.Insert(0, ex);
            throw new AggregateException(cleanupFailures);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            ReleaseHandles(handles);
            return new AnalysisUniverseIssuanceResult.Cancelled();
        }

        if (!IsOpen())
        {
            ReleaseHandles(handles);
            return AnalysisUniverseExecutionAccess.Rejected(
                AnalysisUniverseIssuanceRejectionReason
                    .WorkspaceUnavailable);
        }

        var state = new AnalysisUniverseAccessState();
        ImmutableArray<AnalysisUniverseCapabilityHandle>
            immutableHandles = [.. handles];
        ImmutableArray<AnalysisUniverseRequirementBinding> bindings =
        [
            .. plan.UniverseRequirements.Select(requirement =>
                handleByCapability[requirement.Capability]
                    .CreateBinding(requirement, state)),
        ];
        return AnalysisUniverseExecutionAccess.Create(
            plan,
            offer.Realization,
            bindings,
            immutableHandles,
            state);
    }

    bool IsOpen()
    {
        lock (_gate)
            return _state == InspectionWorkspaceState.Open;
    }

    static void ReleaseHandles(
        List<AnalysisUniverseCapabilityHandle> handles)
    {
        List<Exception>? failures = TryReleaseHandles(handles);
        if (failures is not null)
            throw new AggregateException(failures);
    }

    static List<Exception>? TryReleaseHandles(
        List<AnalysisUniverseCapabilityHandle> handles)
    {
        List<Exception>? failures = null;
        for (int index = handles.Count - 1; index >= 0; index--)
        {
            try
            {
                handles[index].Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        return failures;
    }
}
