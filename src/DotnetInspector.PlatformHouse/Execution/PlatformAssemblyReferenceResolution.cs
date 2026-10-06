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
/// Executes one source-neutral Platform assembly-reference operation.
/// </summary>
public static class PlatformHouseAssemblyReferenceResolver
{
    private const string IdentityPrefix = "platform-assembly-reference";

    /// <summary>
    /// Projects one source-terminal contribution without performing
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
                out PlatformHouseOperation.ResolveAssemblyReference?
                    operation,
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

        if (contribution is PlatformSourceContribution.Unavailable
            {
                Reason: PlatformSourceUnavailabilityKind.Absent,
            })
        {
            return CompleteMissing(
                request,
                operation!.Request,
                exact!,
                consumedWork,
                [
                    new PlatformSourceSettlement(
                        contribution,
                        PlatformSourceSettlementDisposition
                            .OutcomeRelevant),
                ],
                AssemblyBindingMissDisposition.NoNameOwner);
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

        bool exceedsBudget = PlatformHouseLibraryRealizer.ExceedsBudget(
            consumedWork,
            request);
        if (!TryValidateRequest(
                request,
                out PlatformHouseOperation.ResolveAssemblyReference? operation,
                out PlatformTargetDemand.Exact? exact)
            || request.Sources.SelectionFor(
                    PlatformSourceFacet.Reference)
                is not { } selection)
        {
            if (exceedsBudget)
            {
                return Incomplete(
                    request,
                    consumedWork,
                    $"{IdentityPrefix}.source-policy-incomplete");
            }

            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidRequest,
                $"{IdentityPrefix}.invalid-request");
        }

        var acceptedAttempts =
            new List<PlatformAssemblyReferenceSourceAttempt>();
        foreach (PlatformAssemblyReferenceSourceAttempt attempt in attempts)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            acceptedAttempts.Add(attempt);
        }
        request.CancellationToken.ThrowIfCancellationRequested();
        PlatformAssemblyReferenceSourceAttempt[] snapshot =
            [.. acceptedAttempts];
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
            if (exceedsBudget)
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

        PlatformSourcePolicyDecision<
            PlatformAssemblyReferenceSourceAttempt> decision =
                PlatformSourcePolicyReducer.Select(
            selection,
            byCapability);
        bool consumedWorkCoversAttempts =
            ConsumedWorkCoversAttempts(consumedWork, snapshot);
        if (!consumedWorkCoversAttempts && !exceedsBudget)
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidBudget,
                $"{IdentityPrefix}.invalid-source-work");
        }

        if (decision.Kind == PlatformSourcePolicyDecisionKind.Failed
            && consumedWorkCoversAttempts)
        {
            return Failed(
                request,
                consumedWork,
                decision.Settlements,
                [PlatformHouseFailureKind.Source],
                cancellationObserved: false,
                $"{IdentityPrefix}.source-policy-failed");
        }

        if (exceedsBudget)
        {
            return Incomplete(
                request,
                consumedWork,
                $"{IdentityPrefix}.source-policy-incomplete",
                retainedSettlements: consumedWorkCoversAttempts
                    ? PlatformSourcePolicyReducer.TerminalSettlements(
                        decision.Settlements)
                    : null);
        }

        if (decision.Kind == PlatformSourcePolicyDecisionKind.Unavailable
            && IsAuthoritativeAbsence(
                selection,
                decision.Settlements))
        {
            return CompleteMissing(
                request,
                operation.Request,
                exact,
                consumedWork,
                decision.Settlements,
                AssemblyBindingMissDisposition.NoNameOwner);
        }

        return decision.Kind switch
        {
            PlatformSourcePolicyDecisionKind.Selected =>
                await ResolveSelectedAsync(
                        request,
                        ((PlatformAssemblyReferenceSourceAttempt.Succeeded)
                            decision.Selected!).Materialization,
                        consumedWork,
                        decision.Settlements,
                        requireSingleCapability: false)
                    .ConfigureAwait(false),
            PlatformSourcePolicyDecisionKind.Unavailable => Unavailable(
                request,
                consumedWork,
                decision.Settlements,
                $"{IdentityPrefix}.sources-unavailable"),
            PlatformSourcePolicyDecisionKind.Ambiguous => Ambiguous(
                request,
                consumedWork,
                decision.Candidates,
                decision.Settlements),
            PlatformSourcePolicyDecisionKind.Rejected => Rejected(
                request,
                consumedWork,
                decision.RejectionKind!.Value,
                $"{IdentityPrefix}.source-policy-rejected",
                retainedSettlements: decision.Settlements),
            PlatformSourcePolicyDecisionKind.Incomplete => Incomplete(
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
                                    selection.AssemblyIdentity.Identity,
                                    selection.Demand),
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
                PlatformSourcePolicyReducer.TerminalSettlements(
                    sourceSettlements),
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
        PlatformMetadataOutcomeEvidence<AssemblyBindingDecision>
            metadataOutcome;
        PlatformAssemblyReferenceCompletionKind completionKind;
        if (decision is AssemblyBindingDecision.Missing
            {
                Disposition:
                    AssemblyBindingMissDisposition.NameOwnedNoMatch,
            })
        {
            metadataOutcome =
                new PlatformMetadataOutcomeEvidence<AssemblyBindingDecision>(
                    decision,
                    $"{IdentityPrefix}.metadata-outcome");
            completionKind =
                PlatformAssemblyReferenceCompletionKind.NameOwnedNoMatch;
        }
        else
        {
            metadataOutcome =
                new PlatformMetadataOutcomeEvidence<AssemblyBindingDecision>(
                    decision,
                    $"{IdentityPrefix}.metadata-outcome",
                    reference.Contribution);
            completionKind =
                PlatformAssemblyReferenceCompletionKind.Resolved;
        }
        var completion = new PlatformHouseCompletion.AssemblyReference(
            (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                request.Snapshot.Operation,
            completionKind,
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
                    Value: var supplied,
                }
            || !DemandMatchesRequestAndCandidate(
                supplied,
                target.Identity,
                reference.Identity)
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

    static bool DemandMatchesRequestAndCandidate(
        PlatformLibraryDemand demand,
        AssemblyReferenceIdentity request,
        AssemblyReferenceIdentity candidate) =>
        demand switch
        {
            PlatformLibraryDemand.Assembly assembly =>
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    assembly.Identity,
                    request)
                && AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    candidate,
                    request),
            PlatformLibraryDemand.AssemblyReferenceBinding binding =>
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    binding.Identity,
                    request)
                && PlatformAssemblyReferenceBindingPolicy.OwnsName(
                    binding,
                    candidate),
            _ => false,
        };

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
                    "PlatformHouse selected assembly-reference operation"))
            ?? throw new BadImageFormatException(
                "The selected Platform Library content is not a managed assembly.");
        if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                descriptor.Identity,
                state.ExpectedIdentity))
        {
            throw new BadImageFormatException(
                "Metadata decoded an identity different from the selected Platform Library.");
        }

        var policy = new SelectedAssemblyBindingPolicy(
            state.Request,
            descriptor,
            state.Demand
                is not PlatformLibraryDemand.AssemblyReferenceBinding
                    binding
                || PlatformAssemblyReferenceBindingPolicy.MatchesCandidate(
                    binding,
                    descriptor.Identity));
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
        PlatformLibraryContentSelection selection)
    {
        if (decision is AssemblyBindingDecision.Resolved resolved)
        {
            return ReferenceEquals(
                    resolved.Candidate.Registration.ArtifactRegistration,
                    selection.Content.Registration)
                && AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    resolved.Candidate.Identity,
                    selection.AssemblyIdentity.Identity);
        }

        return decision is AssemblyBindingDecision.Missing
            {
                Disposition:
                    AssemblyBindingMissDisposition.NameOwnedNoMatch,
            }
            && selection.Demand
                is PlatformLibraryDemand.AssemblyReferenceBinding binding
            && !PlatformAssemblyReferenceBindingPolicy.MatchesCandidate(
                binding,
                selection.AssemblyIdentity.Identity);
    }

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

    static bool IsAuthoritativeAbsence(
        PlatformSourceSelection selection,
        IReadOnlyList<PlatformSourceSettlement> settlements) =>
        settlements.Count == selection.Capabilities.Count
        && settlements.All(
            settlement =>
                settlement.Disposition
                    == PlatformSourceSettlementDisposition.OutcomeRelevant
                && settlement.Contribution
                    is PlatformSourceContribution.Unavailable
                    {
                        Reason: PlatformSourceUnavailabilityKind.Absent,
                    });

    static PlatformHouseOutcome<AssemblyBindingDecision> CompleteMissing(
        PlatformHouseRequest request,
        AssemblyBindingRequest bindingRequest,
        PlatformTargetDemand.Exact exact,
        PlatformHouseConsumedWork consumedWork,
        IReadOnlyList<PlatformSourceSettlement> sourceSettlements,
        AssemblyBindingMissDisposition disposition)
    {
        AssemblyBindingDecision decision = ProjectMissingDecision(
            bindingRequest,
            disposition,
            request.CancellationToken);
        var metadataOutcome =
            new PlatformMetadataOutcomeEvidence<AssemblyBindingDecision>(
                decision,
                $"{IdentityPrefix}.metadata-outcome");
        PlatformAssemblyReferenceCompletionKind kind = disposition switch
        {
            AssemblyBindingMissDisposition.NoNameOwner =>
                PlatformAssemblyReferenceCompletionKind.NoNameOwner,
            AssemblyBindingMissDisposition.NameOwnedNoMatch =>
                PlatformAssemblyReferenceCompletionKind.NameOwnedNoMatch,
            _ => throw new ArgumentOutOfRangeException(
                nameof(disposition)),
        };
        var completion = new PlatformHouseCompletion.AssemblyReference(
            (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                request.Snapshot.Operation,
            kind,
            metadataOutcome,
            sourceSettlements);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Exact(exact),
            sourceSettlements,
            consumedWork,
            completion);
        return new PlatformHouseOutcome<AssemblyBindingDecision>.Completed(
            completion.Bind(metadataOutcome),
            receipt);
    }

    static AssemblyBindingDecision ProjectMissingDecision(
        AssemblyBindingRequest request,
        AssemblyBindingMissDisposition disposition,
        CancellationToken cancellationToken)
    {
        var policy = new MissingAssemblyBindingPolicy(
            request,
            disposition);
        using var catalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions
            {
                MaxCandidates = 1,
                MaxRetainedImageBytes = 1,
                MaxConcurrentSourceOpens = 1,
                MaxForwarderHops = 0,
                MaxTypeResolutionRequests = 1,
            });
        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [request],
            requests: []);
        return context.ProjectBindingDecision(request);
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
        AssemblyReferenceIdentity ExpectedIdentity,
        PlatformLibraryDemand Demand);

    sealed class SelectedAssemblyBindingPolicy(
        AssemblyBindingRequest expectedRequest,
        ResolvedAssemblyReference assembly,
        bool compatible)
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
                    ? compatible
                        ? AssemblyBindingSelection.Found(assembly)
                        : AssemblyBindingSelection.NameOwnedButNoMatch()
                    : AssemblyBindingSelection.Invalid(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind
                                .InvalidPolicyResult)));
        }
    }

    sealed class MissingAssemblyBindingPolicy(
        AssemblyBindingRequest expectedRequest,
        AssemblyBindingMissDisposition disposition)
        : IAcquisitionFreeAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            AssemblyBindingSelection selection = disposition switch
            {
                AssemblyBindingMissDisposition.NoNameOwner =>
                    AssemblyBindingSelection.NameNotOwned(),
                AssemblyBindingMissDisposition.NameOwnedNoMatch =>
                    AssemblyBindingSelection.NameOwnedButNoMatch(),
                _ => AssemblyBindingSelection.Invalid(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.InvalidPolicyResult)),
            };
            return new(
                Version,
                ReferenceEquals(request, expectedRequest)
                    ? selection
                    : AssemblyBindingSelection.Invalid(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind
                                .InvalidPolicyResult)));
        }
    }
}
