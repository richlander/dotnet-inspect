using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using DotnetInspector.Libraries;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Executes one exact source-neutral Platform assembly-reference operation.
/// </summary>
public static class PlatformHouseAssemblyReferenceResolver
{
    private const string IdentityPrefix = "platform-assembly-reference";

    /// <summary>
    /// Projects one exact source-terminal contribution without performing
    /// source, Artifact, Library, or Metadata work.
    /// </summary>
    public static PlatformHouseOutcome<AssemblyBindingDecision>
        ProjectSourceTerminal(
            PlatformHouseRequest request,
            PlatformSourceContribution contribution,
            PlatformHouseConsumedWork consumedWork,
            PlatformHouseRejectionKind? sourceRejectionKind = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(consumedWork);
        request.CancellationToken.ThrowIfCancellationRequested();

        if (!TryValidateRequest(
                request,
                out _,
                out PlatformTargetDemand.Exact? exact))
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidOwnerResult,
                $"{IdentityPrefix}.invalid-source-terminal");
        }

        bool validSourceTerminal = ValidSourceTerminal(
                request,
                exact,
                contribution,
                sourceRejectionKind);

        if (validSourceTerminal
            && contribution is PlatformSourceContribution.Failed)
        {
            return Failed(
                request,
                consumedWork,
                contribution,
                [PlatformHouseFailureKind.Source],
                cancellationObserved: false,
                $"{IdentityPrefix}.source-failed");
        }

        if (PlatformHouseLibraryRealizer.ExceedsBudget(
                consumedWork,
                request)
            || (validSourceTerminal
                && contribution
                    is PlatformSourceContribution.Incomplete))
        {
            return Incomplete(
                request,
                consumedWork,
                $"{IdentityPrefix}.source-incomplete",
                validSourceTerminal ? contribution : null);
        }

        if (!validSourceTerminal)
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidOwnerResult,
                $"{IdentityPrefix}.invalid-source-terminal");
        }

        return contribution switch
        {
            PlatformSourceContribution.Unavailable => Unavailable(
                request,
                consumedWork,
                contribution,
                $"{IdentityPrefix}.source-unavailable"),
            PlatformSourceContribution.Rejected => Rejected(
                request,
                consumedWork,
                sourceRejectionKind!.Value,
                $"{IdentityPrefix}.source-rejected",
                contribution),
            _ => throw new InvalidOperationException(
                "Validated source-terminal evidence has an unknown kind."),
        };
    }

    /// <summary>
    /// Settles source-prepared assembly-reference attempts under the captured
    /// Reference source policy.
    /// </summary>
    public static async ValueTask<
        PlatformHouseOutcome<AssemblyBindingDecision>> ResolveAsync(
            PlatformHouseRequest request,
            IEnumerable<PlatformAssemblyReferenceSourceAttempt> attempts,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(consumedWork);
        request.CancellationToken.ThrowIfCancellationRequested();

        if (!TryValidateRequest(
                request,
                out PlatformHouseOperation.ResolveAssemblyReference? operation,
                out PlatformTargetDemand.Exact? exact)
            || request.Sources.SelectionFor(
                    PlatformSourceFacet.Reference)
                is not { } selection)
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidRequest,
                $"{IdentityPrefix}.invalid-request");
        }

        PlatformAssemblyReferenceSourceAttempt[] snapshot = [.. attempts];
        if (!TryValidateAttempts(
                request,
                operation,
                exact,
                selection,
                snapshot,
                out Dictionary<
                    PlatformSourceCapabilityIdentity,
                    PlatformAssemblyReferenceSourceAttempt>? byCapability))
        {
            if (PlatformHouseLibraryRealizer.ExceedsBudget(
                    consumedWork,
                    request))
            {
                return Incomplete(
                    request,
                    consumedWork,
                    $"{IdentityPrefix}.source-policy-incomplete");
            }

            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidOwnerResult,
                $"{IdentityPrefix}.invalid-source-attempts");
        }

        SourcePolicyDecision decision = SelectSourcePolicy(
            selection,
            byCapability);
        if (!ConsumedWorkCoversAttempts(consumedWork, snapshot))
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidBudget,
                $"{IdentityPrefix}.invalid-source-work");
        }

        if (decision.Kind == SourcePolicyDecisionKind.Failed)
        {
            return Failed(
                request,
                consumedWork,
                decision.Settlements,
                [PlatformHouseFailureKind.Source],
                cancellationObserved: false,
                $"{IdentityPrefix}.source-policy-failed");
        }

        if (PlatformHouseLibraryRealizer.ExceedsBudget(
                consumedWork,
                request))
        {
            return Incomplete(
                request,
                consumedWork,
                $"{IdentityPrefix}.source-policy-incomplete",
                retainedSettlements:
                    TerminalSettlements(decision.Settlements));
        }

        return decision.Kind switch
        {
            SourcePolicyDecisionKind.Selected =>
                await ResolveSelectedAsync(
                        request,
                        decision.Selected!.Materialization,
                        consumedWork,
                        decision.Settlements,
                        requireSingleCapability: false)
                    .ConfigureAwait(false),
            SourcePolicyDecisionKind.Unavailable => Unavailable(
                request,
                consumedWork,
                decision.Settlements,
                $"{IdentityPrefix}.sources-unavailable"),
            SourcePolicyDecisionKind.Ambiguous => Ambiguous(
                request,
                consumedWork,
                decision.Candidates,
                decision.Settlements),
            SourcePolicyDecisionKind.Rejected => Rejected(
                request,
                consumedWork,
                decision.RejectionKind!.Value,
                $"{IdentityPrefix}.source-policy-rejected",
                retainedSettlements: decision.Settlements),
            SourcePolicyDecisionKind.Incomplete => Incomplete(
                request,
                consumedWork,
                $"{IdentityPrefix}.source-policy-incomplete",
                retainedSettlements: decision.Settlements),
            _ => throw new InvalidOperationException(
                "Unknown assembly-reference source-policy decision."),
        };
    }

    public static ValueTask<
        PlatformHouseOutcome<AssemblyBindingDecision>> ResolveAsync(
            PlatformHouseRequest request,
            PlatformLibraryArtifactMaterializationItem reference,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var settlement = new PlatformSourceSettlement(
            reference.Contribution,
            PlatformSourceSettlementDisposition.Selected);
        return ResolveSelectedAsync(
            request,
            reference,
            consumedWork,
            [settlement],
            requireSingleCapability: true);
    }

    static async ValueTask<
        PlatformHouseOutcome<AssemblyBindingDecision>> ResolveSelectedAsync(
            PlatformHouseRequest request,
            PlatformLibraryArtifactMaterializationItem reference,
            PlatformHouseConsumedWork consumedWork,
            IReadOnlyList<PlatformSourceSettlement> sourceSettlements,
            bool requireSingleCapability)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(consumedWork);
        ArgumentNullException.ThrowIfNull(sourceSettlements);
        request.CancellationToken.ThrowIfCancellationRequested();

        if (!TryValidate(
                request,
                reference,
                consumedWork,
                requireSingleCapability,
                out PlatformHouseOperation.ResolveAssemblyReference? operation,
                out PlatformTargetDemand.Exact? exact))
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidRequest,
                $"{IdentityPrefix}.invalid-request");
        }

        if (PlatformHouseLibraryRealizer.ExceedsBudget(
                consumedWork,
                request)
            || reference.ContentLength > int.MaxValue)
        {
            return Incomplete(
                request,
                consumedWork,
                $"{IdentityPrefix}.work-incomplete");
        }

        var failures = new List<PlatformHouseFailureKind>();
        var cleanupExceptions = new List<Exception>();
        ArtifactSetSession? artifacts = null;
        ArtifactQueryLease? queryLease = null;
        ArtifactContentLease? contentLease = null;
        LibraryContentOwner? libraryOwner = null;
        LibraryOperationLease? operationLease = null;
        AssemblyBindingDecision? decision = null;
        OperationCanceledException? cancellation = null;
        Exception? unexpected = null;

        try
        {
            artifacts = new ArtifactSetSession(
                new ArtifactSetSessionLimits
                {
                    MaxArtifacts = 1,
                    MaxArtifactBytes = checked((int)reference.ContentLength),
                    MaxRetainedBytes = reference.ContentLength,
                });

            ArtifactIdentity? artifactIdentity = null;
            await artifacts.AddRequiredAcquisitionAsync(
                    (scope, _) =>
                    {
                        ArtifactContribution contribution = scope.Register(
                            new PlatformLibraryArtifactProvenance(
                                reference.Contribution,
                                reference.Provenance),
                            reference.OpenRead);
                        artifactIdentity = contribution.Descriptor.Identity;
                        return ValueTask.FromResult<
                            ArtifactAcquisitionOutcome>(
                                new ArtifactAcquisitionOutcome.Acquired(
                                    [contribution],
                                    ArtifactAcquisitionLeases.None));
                    },
                    cancellationToken: request.CancellationToken)
                .ConfigureAwait(false);

            ArtifactAssemblyProjection? projection = null;
            ArtifactSetPublicationOutcome publication =
                await artifacts.SealWithProjectionAsync(
                        (view, cancellationToken) =>
                        {
                            ArtifactAssemblyProjectionOutcome outcome =
                                ArtifactAssemblyInspection.Project(
                                    view,
                                    cancellationToken);
                            if (outcome
                                is not ArtifactAssemblyProjectionOutcome
                                    .Projected projected)
                            {
                                return Failure(
                                    $"{IdentityPrefix}.metadata-rejected",
                                    "Metadata could not project the Platform reference assembly.");
                            }
                            if (!AssemblyReferenceIdentity
                                .EquivalentComparer.Equals(
                                    reference.Identity,
                                    projected.Value.Identity))
                            {
                                return Failure(
                                    $"{IdentityPrefix}.identity-mismatch",
                                    "Metadata projected an identity different from the Platform source identity.");
                            }

                            projection = projected.Value;
                            return null;
                        },
                        request.CancellationToken)
                    .ConfigureAwait(false);

            if (publication
                is ArtifactSetPublicationOutcome.NotPublished notPublished)
            {
                failures.Add(
                    notPublished.Failures.Any(
                        failure => failure.Diagnostic.Code.StartsWith(
                            $"{IdentityPrefix}.metadata",
                            StringComparison.Ordinal)
                            || failure.Diagnostic.Code.EndsWith(
                                "identity-mismatch",
                                StringComparison.Ordinal))
                        ? PlatformHouseFailureKind.Metadata
                        : PlatformHouseFailureKind.ArtifactPublication);
                if (notPublished.CleanupFailures.Count != 0)
                {
                    failures.Add(
                        PlatformHouseFailureKind.ArtifactRetirement);
                }
            }
            else
            {
                queryLease = artifacts.IssueLease(
                    artifacts.CreateQueryAuthorization());
                ArtifactContentReference content =
                    artifacts.GetContentReference(
                        artifactIdentity!,
                        queryLease);
                var selection = new PlatformLibraryContentSelection(
                    content,
                    projection!);
                contentLease = artifacts.IssueContentLease(
                    content,
                    queryLease);
                ReleaseQueryLease(
                    ref queryLease,
                    failures,
                    cleanupExceptions);

                if (failures.Count == 0)
                {
                    LibraryReference library =
                        LibraryReference.CreateFromSource(
                            new ExactLibrarySourceCoordinate.Platform(
                                new PlatformLibraryPopulationDeclaration(
                                    exact!.Target.Family),
                                selection.AssemblyIdentity),
                            new LibraryAssemblyCorrespondence(
                                selection.Content,
                                selection.AssemblyIdentity,
                                implementationAssembly: null,
                                implementationIdentity: null));
                    libraryOwner = new LibraryContentOwner(
                        library,
                        [contentLease]);
                    contentLease = null;

                    if (libraryOwner.IssueOperationLease(library)
                        is not LibraryOperationLeaseIssueOutcome.Issued issued)
                    {
                        failures.Add(
                            PlatformHouseFailureKind.LibraryBorrow);
                    }
                    else
                    {
                        operationLease = issued.Lease;
                        try
                        {
                            decision = operationLease.Snapshot(
                                library.ApiAssembly,
                                new BindingSnapshotState(
                                    operation!.Request,
                                    selection.AssemblyIdentity.Identity),
                                static (view, state, cancellationToken) =>
                                    ProjectDecision(
                                        view,
                                        state,
                                        cancellationToken),
                                request.CancellationToken);
                        }
                        catch (OperationCanceledException ex)
                            when (request.CancellationToken
                                .IsCancellationRequested)
                        {
                            cancellation = ex;
                        }
                        catch (IOException)
                        {
                            failures.Add(
                                PlatformHouseFailureKind.Metadata);
                        }
                        catch (UnsupportedMetadataFormatException)
                        {
                            failures.Add(
                                PlatformHouseFailureKind.Metadata);
                        }
                        catch (MalformedMetadataRootException)
                        {
                            failures.Add(
                                PlatformHouseFailureKind.Metadata);
                        }
                        catch (BadImageFormatException)
                        {
                            failures.Add(
                                PlatformHouseFailureKind.Metadata);
                        }
                        catch (ObjectDisposedException)
                        {
                            failures.Add(
                                PlatformHouseFailureKind.LibraryBorrow);
                        }
                        catch (ArgumentException)
                        {
                            failures.Add(
                                PlatformHouseFailureKind.LibraryBorrow);
                        }
                        catch (InvalidOperationException)
                        {
                            failures.Add(
                                PlatformHouseFailureKind.LibraryBorrow);
                        }
                    }
                }

                if (decision is not null
                    && !ValidDecision(decision, selection))
                {
                    decision = null;
                    failures.Add(PlatformHouseFailureKind.Metadata);
                }
                request.CancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException ex)
            when (request.CancellationToken.IsCancellationRequested)
        {
            cancellation = ex;
            IReadOnlyList<Exception> artifactCleanupFailures =
                ArtifactSetSession.GetCleanupFailures(ex);
            if (artifactCleanupFailures.Count != 0)
            {
                failures.Add(
                    PlatformHouseFailureKind.ArtifactPublication);
                foreach (Exception failure in artifactCleanupFailures)
                {
                    if (!cleanupExceptions.Contains(failure))
                        cleanupExceptions.Add(failure);
                }
            }
        }
        catch (Exception ex)
        {
            unexpected = ex;
        }

        SettleOperationLease(
            ref operationLease,
            failures,
            cleanupExceptions);
        await RetireLibraryAsync(
                libraryOwner,
                failures,
                cleanupExceptions)
            .ConfigureAwait(false);
        ReleaseArtifactLease(
            ref contentLease,
            failures,
            cleanupExceptions);
        ReleaseQueryLease(
            ref queryLease,
            failures,
            cleanupExceptions);
        await RetireArtifactsAsync(
                artifacts,
                failures,
                cleanupExceptions)
            .ConfigureAwait(false);

        if (cancellation is null
            && request.CancellationToken.IsCancellationRequested)
        {
            cancellation = new OperationCanceledException(
                request.CancellationToken);
        }

        if (unexpected is not null)
        {
            ArtifactSetSession.AttachCleanupFailures(
                unexpected,
                cleanupExceptions);
            ExceptionDispatchInfo.Capture(unexpected).Throw();
        }

        if (cancellation is not null && failures.Count == 0)
            ExceptionDispatchInfo.Capture(cancellation).Throw();

        if (failures.Count != 0 || decision is null)
        {
            if (decision is null
                && cancellation is null
                && failures.Count == 0)
            {
                failures.Add(PlatformHouseFailureKind.Metadata);
            }
            return Failed(
                request,
                consumedWork,
                TerminalSettlements(sourceSettlements),
                failures,
                cancellation is not null,
                $"{IdentityPrefix}.execution-failed");
        }

        PlatformSourceSettlement sourceSettlement =
            sourceSettlements.Single(
                settlement =>
                    ReferenceEquals(
                        settlement.Contribution,
                        reference.Contribution)
                    && settlement.Disposition
                        == PlatformSourceSettlementDisposition.Selected);
        var metadataOutcome =
            new PlatformMetadataOutcomeEvidence<AssemblyBindingDecision>(
                decision,
                $"{IdentityPrefix}.metadata-outcome",
                reference.Contribution);
        var completion = new PlatformHouseCompletion.AssemblyReference(
            (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                request.Snapshot.Operation,
            PlatformAssemblyReferenceCompletionKind.Resolved,
            metadataOutcome,
            [sourceSettlement]);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Exact(exact!),
            sourceSettlements,
            consumedWork,
            completion);
        return new PlatformHouseOutcome<AssemblyBindingDecision>.Completed(
            completion.Bind(metadataOutcome),
            receipt);
    }

    static bool TryValidate(
        PlatformHouseRequest request,
        PlatformLibraryArtifactMaterializationItem reference,
        PlatformHouseConsumedWork consumedWork,
        bool requireSingleCapability,
        out PlatformHouseOperation.ResolveAssemblyReference? operation,
        out PlatformTargetDemand.Exact? exact)
    {
        if (!TryValidateRequest(request, out operation, out exact)
            || !ValidMaterialization(
                request,
                operation!,
                exact!,
                reference,
                requireSingleCapability)
            || consumedWork.SourceOperations < 1
            || consumedWork.Assemblies < 1
            || consumedWork.Bytes < reference.ContentLength)
        {
            return false;
        }

        return true;
    }

    static bool ValidMaterialization(
        PlatformHouseRequest request,
        PlatformHouseOperation.ResolveAssemblyReference operation,
        PlatformTargetDemand.Exact exact,
        PlatformLibraryArtifactMaterializationItem reference,
        bool requireSingleCapability)
    {
        if (operation.Request.Target
                is not AssemblyBindingTarget.AssemblyReference target
            || reference.ContentLength <= 0
            || reference.Contribution.Facet
                != PlatformSourceFacet.Reference
            || reference.Contribution.RealizationCompleteness
                != PlatformSourceContributionCompleteness.Authoritative
            || !ReferenceEquals(
                reference.Contribution.Request,
                request.Snapshot)
            || reference.Contribution.Target != exact.Target
            || reference.Contribution.Population
                is not PlatformPopulationDemand.Library
                {
                    Value: PlatformLibraryDemand.Assembly supplied,
                }
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                supplied.Identity,
                target.Identity)
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                reference.Identity,
                target.Identity)
            || !request.Sources.Authorizes(
                PlatformSourceFacet.Reference,
                reference.Contribution.Capability)
            || request.Sources.SelectionFor(
                    PlatformSourceFacet.Reference)
                is not { } sourceSelection
            || requireSingleCapability
                && (sourceSelection.Capabilities.Count != 1
                    || !ReferenceEquals(
                        sourceSelection.Capabilities[0],
                        reference.Contribution.Capability)))
        {
            return false;
        }

        return true;
    }

    static bool TryValidateAttempts(
        PlatformHouseRequest request,
        PlatformHouseOperation.ResolveAssemblyReference operation,
        PlatformTargetDemand.Exact exact,
        PlatformSourceSelection selection,
        IReadOnlyList<PlatformAssemblyReferenceSourceAttempt> attempts,
        [NotNullWhen(true)]
        out Dictionary<
            PlatformSourceCapabilityIdentity,
            PlatformAssemblyReferenceSourceAttempt>? byCapability)
    {
        byCapability = new(
            ReferenceEqualityComparer.Instance);
        var candidates = new HashSet<PlatformHouseCandidateIdentity>(
            ReferenceEqualityComparer.Instance);
        foreach (PlatformAssemblyReferenceSourceAttempt attempt in attempts)
        {
            if (attempt is null
                || !selection.Capabilities.Any(
                    capability => ReferenceEquals(
                        capability,
                        attempt.Contribution.Capability))
                || !byCapability.TryAdd(
                    attempt.Contribution.Capability,
                    attempt))
            {
                byCapability = null;
                return false;
            }

            bool valid = attempt switch
            {
                PlatformAssemblyReferenceSourceAttempt.Succeeded success =>
                    candidates.Add(success.Candidate)
                    && ValidMaterialization(
                        request,
                        operation,
                        exact,
                        success.Materialization,
                        requireSingleCapability: false),
                PlatformAssemblyReferenceSourceAttempt.NotSucceeded
                    terminal => ValidSourceTerminal(
                        request,
                        exact,
                        terminal.Contribution,
                        terminal.RejectionKind,
                        requireSingleCapability: false),
                _ => false,
            };
            if (!valid)
            {
                byCapability = null;
                return false;
            }
        }
        return true;
    }

    static bool ConsumedWorkCoversAttempts(
        PlatformHouseConsumedWork consumedWork,
        IReadOnlyList<PlatformAssemblyReferenceSourceAttempt> attempts)
    {
        int successes = 0;
        long bytes = 0;
        try
        {
            foreach (PlatformAssemblyReferenceSourceAttempt.Succeeded success
                in attempts.OfType<
                    PlatformAssemblyReferenceSourceAttempt.Succeeded>())
            {
                successes = checked(successes + 1);
                bytes = checked(
                    bytes + success.Materialization.ContentLength);
            }
        }
        catch (OverflowException)
        {
            return false;
        }
        return consumedWork.SourceOperations >= attempts.Count
            && consumedWork.Assemblies >= successes
            && consumedWork.Bytes >= bytes;
    }

    static SourcePolicyDecision SelectSourcePolicy(
        PlatformSourceSelection selection,
        IReadOnlyDictionary<
            PlatformSourceCapabilityIdentity,
            PlatformAssemblyReferenceSourceAttempt> attempts) =>
        selection.Mode == PlatformSourceSelectionMode.Aggregation
            ? SelectAggregation(selection, attempts)
            : SelectOrdered(selection, attempts);

    static SourcePolicyDecision SelectOrdered(
        PlatformSourceSelection selection,
        IReadOnlyDictionary<
            PlatformSourceCapabilityIdentity,
            PlatformAssemblyReferenceSourceAttempt> attempts)
    {
        bool sawFallbackFailure = false;
        for (int index = 0;
            index < selection.Capabilities.Count;
            index++)
        {
            PlatformSourceCapabilityIdentity capability =
                selection.Capabilities[index];
            if (!attempts.TryGetValue(capability, out var attempt))
            {
                return SourcePolicyDecision.ForIncomplete(
                    BuildTerminalSettlements(
                        selection,
                        attempts,
                        index - 1));
            }

            if (attempt
                is PlatformAssemblyReferenceSourceAttempt.Succeeded success)
            {
                return SourcePolicyDecision.ForSelected(
                    success,
                    BuildSelectedSettlements(
                        selection,
                        attempts,
                        index,
                        aggregation: false));
            }

            var terminal =
                (PlatformAssemblyReferenceSourceAttempt.NotSucceeded)
                    attempt;
            switch (terminal.Contribution)
            {
                case PlatformSourceContribution.Rejected:
                    return SourcePolicyDecision.ForRejected(
                        terminal.RejectionKind!.Value,
                        BuildTerminalSettlements(
                            selection,
                            attempts,
                            index));
                case PlatformSourceContribution.Incomplete:
                    return SourcePolicyDecision.ForIncomplete(
                        BuildTerminalSettlements(
                            selection,
                            attempts,
                            index));
                case PlatformSourceContribution.Failed
                    when selection.Mode
                        == PlatformSourceSelectionMode.Precedence:
                    return SourcePolicyDecision.ForFailed(
                        BuildTerminalSettlements(
                            selection,
                            attempts,
                            index));
                case PlatformSourceContribution.Failed:
                    sawFallbackFailure = true;
                    break;
            }
        }

        IReadOnlyList<PlatformSourceSettlement> settlements =
            BuildTerminalSettlements(
                selection,
                attempts,
                selection.Capabilities.Count - 1);
        return sawFallbackFailure
            ? SourcePolicyDecision.ForFailed(settlements)
            : SourcePolicyDecision.ForUnavailable(settlements);
    }

    static SourcePolicyDecision SelectAggregation(
        PlatformSourceSelection selection,
        IReadOnlyDictionary<
            PlatformSourceCapabilityIdentity,
            PlatformAssemblyReferenceSourceAttempt> attempts)
    {
        IReadOnlyList<PlatformSourceSettlement> terminalSettlements =
            BuildAggregationTerminalSettlements(selection, attempts);
        if (attempts.Values.Any(
                attempt => attempt.Contribution
                    is PlatformSourceContribution.Failed))
        {
            return SourcePolicyDecision.ForFailed(terminalSettlements);
        }
        if (selection.Capabilities.Any(
                capability => !attempts.ContainsKey(capability)))
        {
            return SourcePolicyDecision.ForIncomplete(
                terminalSettlements);
        }

        foreach (PlatformSourceCapabilityIdentity capability
            in selection.Capabilities)
        {
            if (attempts[capability]
                    is PlatformAssemblyReferenceSourceAttempt.NotSucceeded
                    {
                        Contribution:
                            PlatformSourceContribution.Rejected,
                    } rejected)
            {
                return SourcePolicyDecision.ForRejected(
                    rejected.RejectionKind!.Value,
                    terminalSettlements);
            }
        }
        if (attempts.Values.Any(
                attempt => attempt.Contribution
                    is PlatformSourceContribution.Incomplete))
        {
            return SourcePolicyDecision.ForIncomplete(
                terminalSettlements);
        }
        PlatformAssemblyReferenceSourceAttempt.Succeeded[] successes =
            [.. selection.Capabilities
                .Select(capability => attempts[capability])
                .OfType<
                    PlatformAssemblyReferenceSourceAttempt.Succeeded>()];
        if (successes.Length > 1)
        {
            return SourcePolicyDecision.ForAmbiguous(
                [.. successes.Select(success => success.Candidate)],
                terminalSettlements);
        }
        if (successes.Length == 1
            && attempts.Values
                .Where(attempt => !ReferenceEquals(
                    attempt,
                    successes[0]))
                .All(
                    attempt => attempt.Contribution
                        is PlatformSourceContribution.Unavailable
                        {
                            Reason:
                                PlatformSourceUnavailabilityKind.Absent,
                        }))
        {
            int selectedIndex = IndexOf(
                selection.Capabilities,
                successes[0].Contribution.Capability);
            return SourcePolicyDecision.ForSelected(
                successes[0],
                BuildSelectedSettlements(
                    selection,
                    attempts,
                    selectedIndex,
                    aggregation: true));
        }
        return SourcePolicyDecision.ForUnavailable(terminalSettlements);
    }

    static IReadOnlyList<PlatformSourceSettlement>
        BuildSelectedSettlements(
            PlatformSourceSelection selection,
            IReadOnlyDictionary<
                PlatformSourceCapabilityIdentity,
                PlatformAssemblyReferenceSourceAttempt> attempts,
            int selectedIndex,
            bool aggregation)
    {
        var settlements = new List<PlatformSourceSettlement>(
            attempts.Count);
        for (int index = 0;
            index < selection.Capabilities.Count;
            index++)
        {
            if (!attempts.TryGetValue(
                    selection.Capabilities[index],
                    out var attempt))
            {
                continue;
            }
            PlatformSourceSettlementDisposition disposition =
                index == selectedIndex
                    ? PlatformSourceSettlementDisposition.Selected
                    : aggregation || index < selectedIndex
                        ? PlatformSourceSettlementDisposition.OutcomeRelevant
                        : PlatformSourceSettlementDisposition.Shadowed;
            settlements.Add(
                new PlatformSourceSettlement(
                    attempt.Contribution,
                    disposition));
        }
        return settlements.AsReadOnly();
    }

    static IReadOnlyList<PlatformSourceSettlement>
        BuildTerminalSettlements(
            PlatformSourceSelection selection,
            IReadOnlyDictionary<
                PlatformSourceCapabilityIdentity,
                PlatformAssemblyReferenceSourceAttempt> attempts,
            int terminalIndex)
    {
        var settlements = new List<PlatformSourceSettlement>(
            attempts.Count);
        for (int index = 0;
            index < selection.Capabilities.Count;
            index++)
        {
            if (!attempts.TryGetValue(
                    selection.Capabilities[index],
                    out var attempt))
            {
                continue;
            }
            settlements.Add(
                new PlatformSourceSettlement(
                    attempt.Contribution,
                    index <= terminalIndex
                        ? PlatformSourceSettlementDisposition.OutcomeRelevant
                        : PlatformSourceSettlementDisposition.Shadowed));
        }
        return settlements.AsReadOnly();
    }

    static IReadOnlyList<PlatformSourceSettlement>
        BuildAggregationTerminalSettlements(
            PlatformSourceSelection selection,
            IReadOnlyDictionary<
                PlatformSourceCapabilityIdentity,
                PlatformAssemblyReferenceSourceAttempt> attempts)
    {
        var settlements = new List<PlatformSourceSettlement>(
            attempts.Count);
        foreach (PlatformSourceCapabilityIdentity capability
            in selection.Capabilities)
        {
            if (attempts.TryGetValue(capability, out var attempt))
            {
                settlements.Add(
                    new PlatformSourceSettlement(
                        attempt.Contribution,
                        PlatformSourceSettlementDisposition
                            .OutcomeRelevant));
            }
        }
        return settlements.AsReadOnly();
    }

    static IReadOnlyList<PlatformSourceSettlement> TerminalSettlements(
        IEnumerable<PlatformSourceSettlement> settlements) =>
        Array.AsReadOnly(
            settlements.Select(
                settlement => new PlatformSourceSettlement(
                    settlement.Contribution,
                    settlement.Disposition
                        == PlatformSourceSettlementDisposition.Selected
                            ? PlatformSourceSettlementDisposition
                                .OutcomeRelevant
                            : settlement.Disposition))
                .ToArray());

    static bool TryValidateRequest(
        PlatformHouseRequest request,
        [NotNullWhen(true)]
        out PlatformHouseOperation.ResolveAssemblyReference? operation,
        [NotNullWhen(true)] out PlatformTargetDemand.Exact? exact)
    {
        operation = request.Operation
            as PlatformHouseOperation.ResolveAssemblyReference
                .WithPrerequisites<PlatformAssemblyReferenceRoute>;
        exact = request.Target as PlatformTargetDemand.Exact;
        return operation
                is PlatformHouseOperation.ResolveAssemblyReference
                    .WithPrerequisites<PlatformAssemblyReferenceRoute>
                        routed
            && exact is not null
            && ReferenceEquals(
                routed.Prerequisites.Request,
                ((PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                    operation.Snapshot).Request)
            && routed.Prerequisites.Target == exact.Target
            && ReferenceEquals(
                routed.Prerequisites.Origin,
                request.Origin)
            && ReferenceEquals(
                routed.Prerequisites.SourcePlan,
                request.Sources.Identity)
            && ReferenceEquals(
                routed.Prerequisites.SourcePolicy,
                request.Sources.Generation)
            && operation.RequiredView == PlatformViewDemand.Reference
            && operation.Request.Target
                is AssemblyBindingTarget.AssemblyReference
            && operation.Request.Origin
                is AssemblyBindingOrigin.GlobalOrigin
            && operation.Request.Scope == AssemblyResolutionScope.Platform;
    }

    static bool ValidSourceTerminal(
        PlatformHouseRequest request,
        PlatformTargetDemand.Exact exact,
        PlatformSourceContribution contribution,
        PlatformHouseRejectionKind? sourceRejectionKind,
        bool requireSingleCapability = true)
    {
        bool isRejected =
            contribution is PlatformSourceContribution.Rejected;
        return contribution is PlatformSourceContribution.Unavailable
                or PlatformSourceContribution.Rejected
                or PlatformSourceContribution.Incomplete
                or PlatformSourceContribution.Failed
            && isRejected == sourceRejectionKind.HasValue
            && (!sourceRejectionKind.HasValue
                || Enum.IsDefined(sourceRejectionKind.Value))
            && contribution.Facet == PlatformSourceFacet.Reference
            && ReferenceEquals(
                contribution.Request,
                request.Snapshot)
            && contribution.ExactTarget == exact.Target
            && request.Sources.Authorizes(
                PlatformSourceFacet.Reference,
                contribution.Capability)
            && request.Sources.SelectionFor(
                    PlatformSourceFacet.Reference)
                is { } sourceSelection
            && sourceSelection.Capabilities.Any(
                capability => ReferenceEquals(
                    capability,
                    contribution.Capability))
            && (!requireSingleCapability
                || sourceSelection.Capabilities.Count == 1
                    && ReferenceEquals(
                        sourceSelection.Capabilities[0],
                        contribution.Capability));
    }

    static AssemblyBindingDecision ProjectDecision(
        scoped LibraryContentView view,
        BindingSnapshotState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] content = view.Content.ToArray();
        ResolvedAssemblyReference descriptor =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                view.Reference.Registration,
                () => new MemoryStream(content, writable: false),
                AssemblyResolutionProvenance.Designated(
                    "PlatformHouse exact assembly-reference operation"))
            ?? throw new BadImageFormatException(
                "The selected Platform Library content is not a managed assembly.");
        if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                descriptor.Identity,
                state.ExpectedIdentity))
        {
            throw new BadImageFormatException(
                "Metadata decoded an identity different from the selected Platform Library.");
        }

        var policy = new ExactAssemblyBindingPolicy(
            state.Request,
            descriptor);
        using var catalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions
            {
                MaxCandidates = 1,
                MaxRetainedImageBytes = content.LongLength,
                MaxConcurrentSourceOpens = 1,
                MaxForwarderHops = 0,
                MaxTypeResolutionRequests = 1,
            });
        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [state.Request],
            requests: []);
        return context.ProjectBindingDecision(state.Request);
    }

    static bool ValidDecision(
        AssemblyBindingDecision decision,
        PlatformLibraryContentSelection selection) =>
        decision is AssemblyBindingDecision.Resolved resolved
        && ReferenceEquals(
            resolved.Candidate.Registration.ArtifactRegistration,
            selection.Content.Registration)
        && AssemblyReferenceIdentity.EquivalentComparer.Equals(
            resolved.Candidate.Identity,
            selection.AssemblyIdentity.Identity);

    static void SettleOperationLease(
        ref LibraryOperationLease? lease,
        ICollection<PlatformHouseFailureKind> failures,
        ICollection<Exception> cleanupExceptions)
    {
        if (lease is null)
            return;
        try
        {
            lease.Dispose();
            lease = null;
        }
        catch (Exception ex)
        {
            failures.Add(
                PlatformHouseFailureKind.LibraryLeaseSettlement);
            cleanupExceptions.Add(ex);
        }
    }

    static async ValueTask RetireLibraryAsync(
        LibraryContentOwner? owner,
        ICollection<PlatformHouseFailureKind> failures,
        ICollection<Exception> cleanupExceptions)
    {
        if (owner is null)
            return;
        try
        {
            await owner.DisposeAsync().ConfigureAwait(false);
            if (owner.CleanupFailures.Count != 0)
            {
                failures.Add(
                    owner.ReleaseFailures.Count == 0
                        ? PlatformHouseFailureKind.LibraryRetirement
                        : PlatformHouseFailureKind.LibraryChildRelease);
                foreach (Exception failure in owner.CleanupFailures)
                {
                    if (!cleanupExceptions.Contains(failure))
                        cleanupExceptions.Add(failure);
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add(
                owner.ReleaseFailures.Count == 0
                    ? PlatformHouseFailureKind.LibraryRetirement
                    : PlatformHouseFailureKind.LibraryChildRelease);
            cleanupExceptions.Add(ex);
        }
    }

    static void ReleaseArtifactLease(
        ref ArtifactContentLease? lease,
        ICollection<PlatformHouseFailureKind> failures,
        ICollection<Exception> cleanupExceptions)
    {
        if (lease is null)
            return;
        try
        {
            lease.Dispose();
            lease = null;
        }
        catch (Exception ex)
        {
            failures.Add(PlatformHouseFailureKind.ArtifactRetirement);
            cleanupExceptions.Add(ex);
        }
    }

    static void ReleaseQueryLease(
        ref ArtifactQueryLease? lease,
        ICollection<PlatformHouseFailureKind> failures,
        ICollection<Exception> cleanupExceptions)
    {
        if (lease is null)
            return;
        try
        {
            lease.Dispose();
            lease = null;
        }
        catch (Exception ex)
        {
            failures.Add(PlatformHouseFailureKind.ArtifactRetirement);
            cleanupExceptions.Add(ex);
        }
    }

    static async ValueTask RetireArtifactsAsync(
        ArtifactSetSession? artifacts,
        ICollection<PlatformHouseFailureKind> failures,
        ICollection<Exception> cleanupExceptions)
    {
        if (artifacts is null)
            return;
        try
        {
            await artifacts.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failures.Add(PlatformHouseFailureKind.ArtifactRetirement);
            cleanupExceptions.Add(ex);
        }
        foreach (Exception failure in artifacts.CleanupFailures)
        {
            if (!cleanupExceptions.Contains(failure))
                cleanupExceptions.Add(failure);
        }
        if (artifacts.CleanupFailures.Count != 0)
            failures.Add(PlatformHouseFailureKind.ArtifactRetirement);
    }

    static PlatformHouseOutcome<AssemblyBindingDecision> Rejected(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformHouseRejectionKind kind,
        string evidenceName,
        PlatformSourceContribution? contribution = null,
        IEnumerable<PlatformSourceSettlement>? retainedSettlements = null)
    {
        if (contribution is not null && retainedSettlements is not null)
        {
            throw new ArgumentException(
                "Rejected evidence must use either one contribution or an explicit settlement set.");
        }
        var termination = new PlatformHouseTermination.Rejected(
            new PlatformHouseRejection.OwnerEvidence(
                kind,
                PlatformHouseTerminalEvidenceIdentity.Create(
                    evidenceName)));
        PlatformSourceSettlement[] settlements =
            retainedSettlements is not null
                ? [.. retainedSettlements]
                : contribution is null
                    ? []
                    :
                    [
                        new PlatformSourceSettlement(
                            contribution,
                            PlatformSourceSettlementDisposition
                                .OutcomeRelevant),
                    ];
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            PlatformHouseLibraryRealizer.TargetSettlement(request.Target),
            settlements,
            consumedWork,
            termination: termination);
        return new PlatformHouseOutcome<AssemblyBindingDecision>.Rejected(
            termination,
            receipt);
    }

    static PlatformHouseOutcome<AssemblyBindingDecision> Unavailable(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformSourceContribution contribution,
        string evidenceName) =>
        Unavailable(
            request,
            consumedWork,
            [
                new PlatformSourceSettlement(
                    contribution,
                    PlatformSourceSettlementDisposition.OutcomeRelevant),
            ],
            evidenceName);

    static PlatformHouseOutcome<AssemblyBindingDecision> Unavailable(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        IEnumerable<PlatformSourceSettlement> settlements,
        string evidenceName)
    {
        var termination = new PlatformHouseTermination.Unavailable(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            PlatformHouseLibraryRealizer.TargetSettlement(request.Target),
            settlements,
            consumedWork,
            termination: termination);
        return new PlatformHouseOutcome<AssemblyBindingDecision>.Unavailable(
            termination,
            receipt);
    }

    static PlatformHouseOutcome<AssemblyBindingDecision> Incomplete(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        string evidenceName,
        PlatformSourceContribution? contribution = null,
        IEnumerable<PlatformSourceSettlement>? retainedSettlements = null)
    {
        if (contribution is not null && retainedSettlements is not null)
        {
            throw new ArgumentException(
                "Incomplete evidence must use either one contribution or an explicit settlement set.");
        }
        var termination = new PlatformHouseTermination.Incomplete(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));
        PlatformSourceSettlement[] settlements =
            retainedSettlements is not null
                ? [.. retainedSettlements]
                : contribution is null
                    ? []
                    :
                    [
                        new PlatformSourceSettlement(
                            contribution,
                            PlatformSourceSettlementDisposition
                                .OutcomeRelevant),
                    ];
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            PlatformHouseLibraryRealizer.TargetSettlement(request.Target),
            settlements,
            consumedWork,
            termination: termination);
        return new PlatformHouseOutcome<AssemblyBindingDecision>.Incomplete(
            termination,
            receipt);
    }

    static PlatformHouseOutcome<AssemblyBindingDecision> Failed(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformSourceContribution contribution,
        IEnumerable<PlatformHouseFailureKind> failures,
        bool cancellationObserved,
        string evidenceName) =>
        Failed(
            request,
            consumedWork,
            [
                new PlatformSourceSettlement(
                    contribution,
                    PlatformSourceSettlementDisposition.OutcomeRelevant),
            ],
            failures,
            cancellationObserved,
            evidenceName);

    static PlatformHouseOutcome<AssemblyBindingDecision> Failed(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        IEnumerable<PlatformSourceSettlement> settlements,
        IEnumerable<PlatformHouseFailureKind> failures,
        bool cancellationObserved,
        string evidenceName)
    {
        PlatformHouseFailureKind[] failureSnapshot =
            [.. failures.Distinct()];
        var termination = new PlatformHouseTermination.Failed(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName),
            failureSnapshot,
            cancellationObserved);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            PlatformHouseLibraryRealizer.TargetSettlement(request.Target),
            settlements,
            consumedWork,
            termination: termination);
        return new PlatformHouseOutcome<AssemblyBindingDecision>.Failed(
            termination,
            receipt);
    }

    static PlatformHouseOutcome<AssemblyBindingDecision> Ambiguous(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        IEnumerable<PlatformHouseCandidateIdentity> candidates,
        IEnumerable<PlatformSourceSettlement> settlements)
    {
        var termination = new PlatformHouseTermination.Ambiguous(candidates);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            PlatformHouseLibraryRealizer.TargetSettlement(request.Target),
            settlements,
            consumedWork,
            termination: termination);
        return new PlatformHouseOutcome<AssemblyBindingDecision>.Ambiguous(
            termination,
            receipt);
    }

    static int IndexOf<T>(IReadOnlyList<T> values, T value)
        where T : class
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (ReferenceEquals(values[index], value))
                return index;
        }
        return -1;
    }

    static ArtifactSetAdmissionFailure Failure(
        string code,
        string summary) =>
        new(
            ArtifactSetAdmissionFailureKind.Failed,
            new MaterializationDiagnostic(code, summary));

    sealed record MaterializationDiagnostic(
        string Code,
        string Summary) : IArtifactAcquisitionDiagnostic;

    sealed record BindingSnapshotState(
        AssemblyBindingRequest Request,
        AssemblyReferenceIdentity ExpectedIdentity);

    enum SourcePolicyDecisionKind
    {
        Selected,
        Unavailable,
        Ambiguous,
        Rejected,
        Incomplete,
        Failed,
    }

    sealed record SourcePolicyDecision(
        SourcePolicyDecisionKind Kind,
        IReadOnlyList<PlatformSourceSettlement> Settlements,
        PlatformAssemblyReferenceSourceAttempt.Succeeded? Selected = null,
        IReadOnlyList<PlatformHouseCandidateIdentity>? AmbiguousCandidates =
            null,
        PlatformHouseRejectionKind? RejectionKind = null)
    {
        internal IReadOnlyList<PlatformHouseCandidateIdentity> Candidates =>
            AmbiguousCandidates
            ?? Array.Empty<PlatformHouseCandidateIdentity>();

        internal static SourcePolicyDecision ForSelected(
            PlatformAssemblyReferenceSourceAttempt.Succeeded selected,
            IReadOnlyList<PlatformSourceSettlement> settlements) =>
            new(
                SourcePolicyDecisionKind.Selected,
                settlements,
                Selected: selected);

        internal static SourcePolicyDecision ForUnavailable(
            IReadOnlyList<PlatformSourceSettlement> settlements) =>
            new(SourcePolicyDecisionKind.Unavailable, settlements);

        internal static SourcePolicyDecision ForAmbiguous(
            IReadOnlyList<PlatformHouseCandidateIdentity> candidates,
            IReadOnlyList<PlatformSourceSettlement> settlements) =>
            new(
                SourcePolicyDecisionKind.Ambiguous,
                settlements,
                AmbiguousCandidates: candidates);

        internal static SourcePolicyDecision ForRejected(
            PlatformHouseRejectionKind rejectionKind,
            IReadOnlyList<PlatformSourceSettlement> settlements) =>
            new(
                SourcePolicyDecisionKind.Rejected,
                settlements,
                RejectionKind: rejectionKind);

        internal static SourcePolicyDecision ForIncomplete(
            IReadOnlyList<PlatformSourceSettlement> settlements) =>
            new(SourcePolicyDecisionKind.Incomplete, settlements);

        internal static SourcePolicyDecision ForFailed(
            IReadOnlyList<PlatformSourceSettlement> settlements) =>
            new(SourcePolicyDecisionKind.Failed, settlements);
    }

    sealed class ExactAssemblyBindingPolicy(
        AssemblyBindingRequest expectedRequest,
        ResolvedAssemblyReference assembly)
        : IAcquisitionFreeAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return new(
                Version,
                ReferenceEquals(request, expectedRequest)
                    ? AssemblyBindingSelection.Found(assembly)
                    : AssemblyBindingSelection.Invalid(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind
                                .InvalidPolicyResult)));
        }
    }
}
