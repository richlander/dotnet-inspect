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

    public static async ValueTask<
        PlatformHouseOutcome<AssemblyBindingDecision>> ResolveAsync(
            PlatformHouseRequest request,
            PlatformLibraryArtifactMaterializationItem reference,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(consumedWork);
        request.CancellationToken.ThrowIfCancellationRequested();

        if (!TryValidate(
                request,
                reference,
                consumedWork,
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
                reference.Contribution,
                failures,
                cancellation is not null,
                $"{IdentityPrefix}.execution-failed");
        }

        var sourceSettlement = new PlatformSourceSettlement(
            reference.Contribution,
            PlatformSourceSettlementDisposition.Selected);
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
            [sourceSettlement],
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
        out PlatformHouseOperation.ResolveAssemblyReference? operation,
        out PlatformTargetDemand.Exact? exact)
    {
        operation = request.Operation
            as PlatformHouseOperation.ResolveAssemblyReference
                .WithPrerequisites<PlatformAssemblyReferenceRoute>;
        exact = request.Target as PlatformTargetDemand.Exact;
        if (operation
                is not PlatformHouseOperation.ResolveAssemblyReference
                    .WithPrerequisites<PlatformAssemblyReferenceRoute>
                        routed
            || exact is null
            || !ReferenceEquals(
                routed.Prerequisites.Request,
                ((PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                    operation.Snapshot).Request)
            || routed.Prerequisites.Target != exact.Target
            || !ReferenceEquals(
                routed.Prerequisites.Origin,
                request.Origin)
            || !ReferenceEquals(
                routed.Prerequisites.SourcePlan,
                request.Sources.Identity)
            || !ReferenceEquals(
                routed.Prerequisites.SourcePolicy,
                request.Sources.Generation)
            || operation.RequiredView != PlatformViewDemand.Reference
            || operation.Request.Target
                is not AssemblyBindingTarget.AssemblyReference target
            || operation.Request.Origin
                is not AssemblyBindingOrigin.GlobalOrigin
            || operation.Request.Scope != AssemblyResolutionScope.Platform
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
                is not { Capabilities.Count: 1 } sourceSelection
            || !ReferenceEquals(
                sourceSelection.Capabilities[0],
                reference.Contribution.Capability)
            || consumedWork.SourceOperations < 1
            || consumedWork.Assemblies < 1
            || consumedWork.Bytes < reference.ContentLength)
        {
            return false;
        }

        return true;
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
        string evidenceName)
    {
        var termination = new PlatformHouseTermination.Rejected(
            new PlatformHouseRejection.OwnerEvidence(
                kind,
                PlatformHouseTerminalEvidenceIdentity.Create(
                    evidenceName)));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            PlatformHouseLibraryRealizer.TargetSettlement(request.Target),
            [],
            consumedWork,
            termination: termination);
        return new PlatformHouseOutcome<AssemblyBindingDecision>.Rejected(
            termination,
            receipt);
    }

    static PlatformHouseOutcome<AssemblyBindingDecision> Incomplete(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        string evidenceName)
    {
        var termination = new PlatformHouseTermination.Incomplete(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            PlatformHouseLibraryRealizer.TargetSettlement(request.Target),
            [],
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
        string evidenceName)
    {
        PlatformHouseFailureKind[] failureSnapshot =
            [.. failures.Distinct()];
        var termination = new PlatformHouseTermination.Failed(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName),
            failureSnapshot,
            cancellationObserved);
        var settlement = new PlatformSourceSettlement(
            contribution,
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            PlatformHouseLibraryRealizer.TargetSettlement(request.Target),
            [settlement],
            consumedWork,
            termination: termination);
        return new PlatformHouseOutcome<AssemblyBindingDecision>.Failed(
            termination,
            receipt);
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
