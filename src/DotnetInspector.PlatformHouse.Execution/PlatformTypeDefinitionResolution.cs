using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Executes one exact implementation-view Platform type-definition operation.
/// </summary>
public static class PlatformHouseTypeDefinitionResolver
{
    private const string IdentityPrefix =
        "platform-type-definition-implementation";

    public static async ValueTask<PlatformHouseOutcome<
        PlatformTypeDefinitionValue.Implementation<TypeResolutionOutcome>>>
        ResolveImplementationAsync(
            PlatformHouseRequest request,
            PlatformPopulationArtifactMaterializationOutcome.Completed
                implementationPopulation,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(implementationPopulation);
        ArgumentNullException.ThrowIfNull(consumedWork);

        var stopwatch = Stopwatch.StartNew();
        bool exceedsBudget = PlatformHouseLibraryRealizer.ExceedsBudget(
            consumedWork,
            request);
        bool valid = TryValidate(
            request,
            implementationPopulation,
            consumedWork,
            out PlatformHouseOperation.ResolveTypeDefinition
                .FromImplementation<PlatformPopulationMember>? operation,
            out PlatformTargetDemand.Exact? exact);
        var failureKinds = new List<PlatformHouseFailureKind>();
        var cleanupFailures = new List<Exception>();
        TypeResolutionOutcome? metadataOutcome = null;
        OperationCanceledException? cancellation = null;
        Exception? unexpected = null;

        if (request.CancellationToken.IsCancellationRequested)
        {
            cancellation = new OperationCanceledException(
                request.CancellationToken);
        }
        else if (valid && !exceedsBudget)
        {
            try
            {
                metadataOutcome = Resolve(
                    request,
                    operation!,
                    implementationPopulation,
                    request.Work.MaxForwardingHops
                        - consumedWork.ForwardingHops);
            }
            catch (OperationCanceledException failure)
                when (request.CancellationToken.IsCancellationRequested)
            {
                cancellation = failure;
            }
            catch (IOException)
            {
                failureKinds.Add(PlatformHouseFailureKind.Metadata);
            }
            catch (UnsupportedMetadataFormatException)
            {
                failureKinds.Add(PlatformHouseFailureKind.Metadata);
            }
            catch (MalformedMetadataRootException)
            {
                failureKinds.Add(PlatformHouseFailureKind.Metadata);
            }
            catch (BadImageFormatException)
            {
                failureKinds.Add(PlatformHouseFailureKind.Metadata);
            }
            catch (ObjectDisposedException)
            {
                failureKinds.Add(PlatformHouseFailureKind.LibraryBorrow);
            }
            catch (ArgumentException)
            {
                failureKinds.Add(PlatformHouseFailureKind.LibraryBorrow);
            }
            catch (InvalidOperationException)
            {
                failureKinds.Add(PlatformHouseFailureKind.LibraryBorrow);
            }
            catch (Exception failure)
            {
                unexpected = failure;
            }
        }

        PlatformPopulationAuthorityRetirementResult retirement =
            await PlatformPopulationAuthorityRetirement
                .RetireAsync(implementationPopulation)
                .ConfigureAwait(false);
        failureKinds.AddRange(retirement.FailureKinds);
        cleanupFailures.AddRange(retirement.Failures);
        stopwatch.Stop();

        if (cancellation is null
            && request.CancellationToken.IsCancellationRequested)
        {
            cancellation = new OperationCanceledException(
                request.CancellationToken);
        }

        PlatformHouseConsumedWork finalWork;
        try
        {
            finalWork = AddResolutionWork(
                consumedWork,
                ObservedForwardingHops(metadataOutcome),
                stopwatch.Elapsed);
        }
        catch (OverflowException failure)
        {
            unexpected ??= failure;
            finalWork = consumedWork;
        }

        if (unexpected is not null)
        {
            ArtifactSetSession.AttachCleanupFailures(
                unexpected,
                cleanupFailures);
            ExceptionDispatchInfo.Capture(unexpected).Throw();
        }

        if (cancellation is not null && failureKinds.Count == 0)
            ExceptionDispatchInfo.Capture(cancellation).Throw();

        if (failureKinds.Count != 0)
        {
            return Failed(
                request,
                finalWork,
                failureKinds,
                cancellation is not null,
                $"{IdentityPrefix}.execution-failed");
        }

        if (!valid)
        {
            return exceedsBudget
                ? Incomplete(
                    request,
                    finalWork,
                    $"{IdentityPrefix}.work-incomplete")
                : Rejected(
                    request,
                    finalWork,
                    PlatformHouseRejectionKind.InvalidRequest,
                    $"{IdentityPrefix}.invalid-request");
        }

        if (exceedsBudget
            || PlatformHouseLibraryRealizer.ExceedsBudget(
                finalWork,
                request))
        {
            return Incomplete(
                request,
                finalWork,
                $"{IdentityPrefix}.work-incomplete");
        }

        if (metadataOutcome is null)
        {
            return Failed(
                request,
                finalWork,
                [PlatformHouseFailureKind.Metadata],
                cancellationObserved: false,
                $"{IdentityPrefix}.metadata-failed");
        }

        var outcomeEvidence =
            new PlatformMetadataOutcomeEvidence<TypeResolutionOutcome>(
                metadataOutcome,
                $"{IdentityPrefix}.metadata-outcome");
        var completion = new PlatformHouseCompletion.TypeDefinition(
            (PlatformHouseOperationSnapshot.ResolveTypeDefinition)
                request.Snapshot.Operation,
            outcomeEvidence,
            implementationOutcome: null,
            selectedContributions: [],
            correspondence: null);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Exact(exact!),
            [],
            finalWork,
            completion);
        return new PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Completed(
                    completion.BindImplementation(outcomeEvidence),
                    receipt);
    }

    static TypeResolutionOutcome Resolve(
        PlatformHouseRequest request,
        PlatformHouseOperation.ResolveTypeDefinition
            .FromImplementation<PlatformPopulationMember> operation,
        PlatformPopulationArtifactMaterializationOutcome.Completed
            population,
        int maximumForwardingHops)
    {
        PopulationAssemblySnapshots snapshots =
            SnapshotAssemblies(
                population.Population,
                request.CancellationToken);
        ImmutableArray<ResolvedAssemblyReference> assemblies =
            snapshots.Assemblies;
        PlatformPopulationMember startingMember =
            operation.StartingImplementation;
        LibraryContentReference startingContent =
            startingMember.Library.ImplementationAssembly
            ?? throw new InvalidOperationException(
                "An implementation population member requires implementation content.");
        ResolvedAssemblyReference startingAssembly = assemblies.Single(
            assembly => ReferenceEquals(
                assembly.Registration.ArtifactRegistration,
                startingContent.Registration));
        if (operation.Request.Start
                is not TypeResolutionStart.Assembly start
            || start.Scope != AssemblyResolutionScope.Platform
            || !ReferenceEquals(
                start.Value.Registration.ArtifactRegistration,
                startingContent.Registration)
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                start.Value.Identity,
                startingAssembly.Identity)
            || start.Value.Registration.ModuleVersionId
                != startingAssembly.Registration.ModuleVersionId)
        {
            throw new ArgumentException(
                "The Metadata request must start from the exact owner-issued implementation candidate.",
                nameof(operation));
        }

        TypeResolutionRequest executionRequest =
            TypeResolutionRequest.FromAssembly(
                startingAssembly,
                start.Scope,
                operation.Request.Type);
        var policy = new PlatformPopulationBindingPolicy(assemblies);
        using var catalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions
            {
                MaxCandidates = assemblies.Length,
                MaxRetainedImageBytes = snapshots.TotalBytes,
                MaxConcurrentSourceOpens = 1,
                MaxForwarderHops = Math.Max(0, maximumForwardingHops),
                MaxTypeResolutionRequests = 1,
            });
        using TypeResolutionContext context =
            catalog.CreateContextWithCancellation(
                policy,
                assemblies,
                bindingRequests: [],
                requests: [executionRequest],
                request.CancellationToken);
        return context.Resolve(executionRequest);
    }

    static PopulationAssemblySnapshots SnapshotAssemblies(
        PlatformPopulationRealizationResult.Completed population,
        CancellationToken cancellationToken)
    {
        var assemblies = ImmutableArray.CreateBuilder<
            ResolvedAssemblyReference>(population.Value.Members.Count);
        long totalBytes = 0;
        for (int index = 0;
            index < population.Value.Members.Count;
            index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlatformPopulationMember member =
                population.Value.Members[index];
            LibraryContentOwner owner = population.Owners[index];
            LibraryContentReference content =
                member.Library.ImplementationAssembly
                ?? throw new InvalidOperationException(
                    "An implementation population member requires implementation content.");
            if (owner.IssueOperationLease(member.Library)
                is not LibraryOperationLeaseIssueOutcome.Issued issued)
            {
                throw new ObjectDisposedException(
                    nameof(LibraryContentOwner));
            }

            using LibraryOperationLease lease = issued.Lease;
            AssemblySnapshot snapshot = lease.Snapshot(
                content,
                static (view, _) => CreateAssembly(view),
                cancellationToken);
            totalBytes = checked(totalBytes + snapshot.Length);
            assemblies.Add(snapshot.Assembly);
        }
        return new(assemblies.MoveToImmutable(), totalBytes);
    }

    static AssemblySnapshot CreateAssembly(
        scoped LibraryContentView view)
    {
        byte[] content = view.Content.ToArray();
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                view.Reference.Registration,
                () => new MemoryStream(content, writable: false),
                AssemblyResolutionProvenance.Designated(
                    "PlatformHouse implementation type resolution"))
            ?? throw new BadImageFormatException(
                "The Platform implementation population contains a non-managed Library.");
        if (view.Reference.AssemblyIdentity is not { } expected
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                assembly.Identity,
                expected.Identity))
        {
            throw new BadImageFormatException(
                "Metadata decoded an identity different from the Platform population member.");
        }
        return new(assembly, content.LongLength);
    }

    static bool TryValidate(
        PlatformHouseRequest request,
        PlatformPopulationArtifactMaterializationOutcome.Completed
            population,
        PlatformHouseConsumedWork consumedWork,
        out PlatformHouseOperation.ResolveTypeDefinition
            .FromImplementation<PlatformPopulationMember>? operation,
        out PlatformTargetDemand.Exact? exact)
    {
        operation = request.Operation
            as PlatformHouseOperation.ResolveTypeDefinition
                .FromImplementation<PlatformPopulationMember>;
        exact = request.Target as PlatformTargetDemand.Exact;
        PlatformHouseReceipt populationReceipt =
            population.Population.Receipt.HouseReceipt;
        PlatformPopulationMember? starting =
            operation?.StartingImplementation;
        LibraryContentReference? startingContent =
            starting?.Library.ImplementationAssembly;
        PlatformSourcePlan populationSources =
            populationReceipt.Request.Sources;
        return operation is not null
            && exact is not null
            && operation.StartingView
                == PlatformViewDemand.Implementation
            && operation.RequiredView
                == PlatformViewDemand.Implementation
            && operation.Request.Start
                is TypeResolutionStart.Assembly
            {
                Scope: AssemblyResolutionScope.Platform,
            } requestStart
            && starting is not null
            && startingContent is not null
            && population.Population.Value.Members.Any(
                member => ReferenceEquals(member, starting))
            && starting.Target == exact.Target
            && ReferenceEquals(
                requestStart.Value.Registration.ArtifactRegistration,
                startingContent.Registration)
            && AssemblyReferenceIdentity.EquivalentComparer.Equals(
                requestStart.Value.Identity,
                startingContent.AssemblyIdentity!.Identity)
            && populationReceipt.SettlementKind
                == PlatformHouseSettlementKind.Completed
            && populationReceipt.TargetSettlement.SettledTarget
                == exact.Target
            && populationReceipt.Request.Operation
                is PlatformHouseOperationSnapshot.Realize
            {
                Population:
                        PlatformPopulationDemand.CompletePopulation,
                View: PlatformViewDemand.Implementation,
            }
            && ReferenceEquals(populationSources, request.Sources)
            && Covers(consumedWork, populationReceipt.ConsumedWork);
    }

    static bool Covers(
        PlatformHouseConsumedWork current,
        PlatformHouseConsumedWork prior) =>
        current.SourceOperations >= prior.SourceOperations
        && current.TargetCandidates >= prior.TargetCandidates
        && current.Assemblies >= prior.Assemblies
        && current.XmlDocuments >= prior.XmlDocuments
        && current.PortablePdbs >= prior.PortablePdbs
        && current.SourceDocuments >= prior.SourceDocuments
        && current.Bytes >= prior.Bytes
        && current.ForwardingHops >= prior.ForwardingHops
        && current.TargetComparisons >= prior.TargetComparisons
        && current.Elapsed >= prior.Elapsed;

    static PlatformHouseConsumedWork AddResolutionWork(
        PlatformHouseConsumedWork work,
        int forwardingHops,
        TimeSpan elapsed) =>
        new(
            work.SourceOperations,
            work.TargetCandidates,
            work.Assemblies,
            work.XmlDocuments,
            work.PortablePdbs,
            work.SourceDocuments,
            work.Bytes,
            checked(work.ForwardingHops + forwardingHops),
            work.TargetComparisons,
            work.Elapsed + elapsed);

    static int ObservedForwardingHops(TypeResolutionOutcome? outcome) =>
        outcome is TypeResolutionOutcome.Rejected
        {
            Failure: TypeResolutionFailure.HopBudgetExceeded,
        }
            ? outcome.Hops.Length - 1
            : outcome?.Hops.Length ?? 0;

    static PlatformHouseOutcome<
        PlatformTypeDefinitionValue.Implementation<TypeResolutionOutcome>>
        Rejected(
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
            new PlatformTargetSettlement.Exact(
                (PlatformTargetDemand.Exact)request.Target),
            [],
            consumedWork,
            termination: termination);
        return new PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Rejected(
                    termination,
                    receipt);
    }

    static PlatformHouseOutcome<
        PlatformTypeDefinitionValue.Implementation<TypeResolutionOutcome>>
        Incomplete(
            PlatformHouseRequest request,
            PlatformHouseConsumedWork consumedWork,
            string evidenceName)
    {
        var termination = new PlatformHouseTermination.Incomplete(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Exact(
                (PlatformTargetDemand.Exact)request.Target),
            [],
            consumedWork,
            termination: termination);
        return new PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Incomplete(
                    termination,
                    receipt);
    }

    static PlatformHouseOutcome<
        PlatformTypeDefinitionValue.Implementation<TypeResolutionOutcome>>
        Failed(
            PlatformHouseRequest request,
            PlatformHouseConsumedWork consumedWork,
            IEnumerable<PlatformHouseFailureKind> failures,
            bool cancellationObserved,
            string evidenceName)
    {
        var termination = new PlatformHouseTermination.Failed(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName),
            failures.Distinct(),
            cancellationObserved);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Exact(
                (PlatformTargetDemand.Exact)request.Target),
            [],
            consumedWork,
            termination: termination);
        return new PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Failed(
                    termination,
                    receipt);
    }

    sealed class PlatformPopulationBindingPolicy(
        ImmutableArray<ResolvedAssemblyReference> assemblies)
        : IAcquisitionFreeAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            AssemblyBindingSelection selection;
            if (request.Scope != AssemblyResolutionScope.Platform
                || request.Origin
                    is AssemblyBindingOrigin.RequestingAssembly origin
                    && !assemblies.Any(
                        assembly => ReferenceEquals(
                            assembly.Registration,
                            origin.Registration)))
            {
                selection = AssemblyBindingSelection.Invalid(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.InvalidPolicyResult));
            }
            else if (request.Target
                is AssemblyBindingTarget.AssemblyReference reference)
            {
                ImmutableArray<ResolvedAssemblyReference> matches =
                [
                    .. assemblies.Where(
                        assembly =>
                            reference.Identity.MatchesCandidate(
                                assembly.Identity,
                                allowVersionRollForward: false,
                                ignoreVersion: true)),
                ];
                selection = matches.Length switch
                {
                    0 => assemblies.Any(
                        assembly => string.Equals(
                            assembly.Identity.Name,
                            reference.Identity.Name,
                            StringComparison.OrdinalIgnoreCase))
                        ? AssemblyBindingSelection.NameOwnedButNoMatch()
                        : AssemblyBindingSelection.NameNotOwned(),
                    1 => AssemblyBindingSelection.Found(matches[0]),
                    _ => AssemblyBindingSelection.Multiple(matches),
                };
            }
            else if (request.Target
                is AssemblyBindingTarget.IntrinsicCoreLibrary)
            {
                ImmutableArray<ResolvedAssemblyReference> matches =
                [
                    .. assemblies.Where(
                        static assembly =>
                            string.Equals(
                                assembly.Identity.Name,
                                "System.Private.CoreLib",
                                StringComparison.OrdinalIgnoreCase)
                            || string.Equals(
                                assembly.Identity.Name,
                                "mscorlib",
                                StringComparison.OrdinalIgnoreCase)),
                ];
                selection = matches.Length switch
                {
                    0 => AssemblyBindingSelection.NameNotOwned(),
                    1 => AssemblyBindingSelection.Found(matches[0]),
                    _ => AssemblyBindingSelection.Multiple(matches),
                };
            }
            else
            {
                selection = AssemblyBindingSelection.Invalid(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.InvalidPolicyResult));
            }

            return new(Version, selection);
        }
    }

    readonly record struct AssemblySnapshot(
        ResolvedAssemblyReference Assembly,
        long Length);

    readonly record struct PopulationAssemblySnapshots(
        ImmutableArray<ResolvedAssemblyReference> Assemblies,
        long TotalBytes);
}
