using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class IntegrationCensusExecutorTests
{
    static readonly Version Version = new(1, 0, 0, 0);

    [Fact]
    public void IntegrationCensusExecutor_ExecutesSparseUniverseSequentiallyAndSuppressesWithResolvedSourceEvidence()
    {
        IntegrationSourceParticipantIdentity sourceA = Participant("source-a");
        IntegrationSourceParticipantIdentity sourceB = Participant("source-b");
        IntegrationSourceParticipantIdentity peerA = Participant("peer-a");
        IntegrationSourceParticipantIdentity peerB = Participant("peer-b");
        Context context1 = new("net8");
        Context context2 = new("net9");

        MetadataTypeDefinitionName adapterType =
            TypeName("Adapters", "ClientExtensions");
        MetadataTypeDefinitionName opportunitySourceType =
            TypeName("Sdk", "Client");
        MetadataTypeDefinitionName sourceBType =
            TypeName("Source", "B");
        MetadataTypeDefinitionName peerAType =
            TypeName("Peer", "A");
        MetadataTypeDefinitionName peerBType =
            TypeName("Peer", "B");
        IntegrationCandidatePeerIdentity.NamedType peerALookup =
            AssemblyPeer("Peer.A", peerAType);
        IntegrationCandidatePeerIdentity.NamedType peerBLookup =
            AssemblyPeer("Peer.B", peerBType);
        IntegrationCandidatePeerIdentity.NamedType fulfillmentSourceLookup =
            NamedPeer(
                new MetadataTypeReferenceScope.CurrentAssembly(),
                opportunitySourceType);

        IntegrationCandidateIdentity observedA = new(
            InspectionGraphIntegrationsCatalog.IntegrationObserved,
            IntegrationConceptCatalog.AI,
            new IntegrationCandidateSourceIdentity(
                sourceA,
                new IntegrationCandidateSourceElement.Member(
                    adapterType,
                    Anchor(adapterType))),
            peerALookup);
        IntegrationCandidateIdentity opportunityA = new(
            InspectionGraphIntegrationsCatalog.IntegrationOpportunity,
            IntegrationConceptCatalog.AI,
            new IntegrationCandidateSourceIdentity(
                sourceA,
                new IntegrationCandidateSourceElement.Type(
                    opportunitySourceType)),
            new IntegrationCandidatePeerIdentity.PolicyTarget(
                new IntegrationOpportunityTarget("Peer.A", peerAType)));
        IntegrationCandidateIdentity observedB = new(
            InspectionGraphIntegrationsCatalog.IntegrationObserved,
            IntegrationConceptCatalog.Aspire,
            new IntegrationCandidateSourceIdentity(
                sourceB,
                new IntegrationCandidateSourceElement.Type(sourceBType)),
            peerBLookup);

        var operations = new List<string>();
        var harness = new Harness(
            [sourceA, sourceB, peerA, peerB],
            [
                new IntegrationTypeIdentity(sourceA, adapterType),
                new IntegrationTypeIdentity(sourceA, opportunitySourceType),
                new IntegrationTypeIdentity(sourceB, sourceBType),
                new IntegrationTypeIdentity(peerA, peerAType),
            ],
            [context1, context2],
            [
                new IntegrationSourceBindingContextIncidence(
                    sourceA,
                    [context1, context2]),
                new IntegrationSourceBindingContextIncidence(
                    sourceB,
                    [context1]),
                new IntegrationSourceBindingContextIncidence(
                    peerA,
                    [context1]),
                new IntegrationSourceBindingContextIncidence(
                    peerB,
                    [context1]),
            ])
        {
            Record = operations.Add,
            Produce = address =>
            {
                if (address.Participant.Equals(sourceA)
                    && ReferenceEquals(
                        address.Policy,
                        IntegrationAnalysisCatalog.EcosystemObserved))
                {
                    return IntegrationProducerPolicyAttempt.Completed
                        .WithEvidence(
                            address,
                            [
                                new IntegrationCandidateEvidence(
                                    observedA,
                                    [fulfillmentSourceLookup]),
                            ]);
                }
                if (address.Participant.Equals(sourceA)
                    && ReferenceEquals(
                        address.Policy,
                        IntegrationAnalysisCatalog.Opportunity))
                {
                    return new IntegrationProducerPolicyAttempt.Completed(
                        address,
                        [opportunityA]);
                }
                if (address.Participant.Equals(sourceB)
                    && ReferenceEquals(
                        address.Policy,
                        IntegrationAnalysisCatalog.EcosystemObserved))
                {
                    return new IntegrationProducerPolicyAttempt.Completed(
                        address,
                        [observedB]);
                }
                return new IntegrationProducerPolicyAttempt.Completed(
                    address,
                    []);
            },
            Resolve = binding =>
            {
                IntegrationCandidateAttemptAddress address =
                    binding.Address;
                if (address.Candidate.Equals(observedA))
                {
                    return new IntegrationCandidateResolutionAttempt.Resolved(
                        binding,
                        Resolved(observedA.Peer, peerA, peerAType),
                        [
                            Resolved(
                                fulfillmentSourceLookup,
                                sourceA,
                                opportunitySourceType),
                        ]);
                }
                if (address.Candidate.Equals(opportunityA))
                {
                    return new IntegrationCandidateResolutionAttempt.Resolved(
                        binding,
                        Resolved(opportunityA.Peer, peerA, peerAType));
                }
                return new IntegrationCandidateResolutionAttempt.Resolved(
                    binding,
                    Resolved(observedB.Peer, peerB, peerBType));
            },
        };

        IntegrationCensusSnapshot snapshot =
            Assert.IsType<IntegrationCensusExecutionResult.Ready>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken)).Snapshot;

        Assert.Equal(3, snapshot.Candidates.Length);
        Assert.Equal(5, snapshot.CandidateAttempts.Length);
        Assert.Equal(2, snapshot.SuppressedAttempts.Length);
        Assert.Equal(3, snapshot.ClassifiedAttempts.Length);
        Assert.Equal(
            2,
            snapshot.ClassifiedAttempts.Count(attempt =>
                attempt.Disposition is IntegrationCandidateDisposition.In));
        Assert.Single(
            snapshot.ClassifiedAttempts,
            attempt =>
                attempt.Disposition is IntegrationCandidateDisposition.Out);
        Assert.True(snapshot.IsComplete);
        Assert.Equal(
            [
                "produce:source-a:producer.integration.ecosystem-observed",
                "produce:source-a:producer.integration.opentelemetry-observed",
                "produce:source-a:producer.integration.opportunity",
                "produce:source-b:producer.integration.ecosystem-observed",
                "produce:source-b:producer.integration.opentelemetry-observed",
                "produce:source-b:producer.integration.opportunity",
                "produce:peer-a:producer.integration.ecosystem-observed",
                "produce:peer-a:producer.integration.opentelemetry-observed",
                "produce:peer-a:producer.integration.opportunity",
                "produce:peer-b:producer.integration.ecosystem-observed",
                "produce:peer-b:producer.integration.opentelemetry-observed",
                "produce:peer-b:producer.integration.opportunity",
                "bind:net8:3",
                "resolve:net8:3",
                "bind:net9:2",
                "resolve:net9:2",
            ],
            operations);
        Assert.Equal(9, harness.Releases);
    }

    [Fact]
    public void IntegrationCensusExecutor_RejectsCrossCapabilityMismatchBeforeProducerExecution()
    {
        IntegrationSourceParticipantIdentity participant = Participant("source");
        Context context = new("one");
        var harness = new Harness(
            [participant],
            [],
            [context],
            [
                new IntegrationSourceBindingContextIncidence(
                    participant,
                    [context]),
            ])
        {
            ResolutionIdentityDomain = new IdentityDomain(),
        };

        IntegrationCensusExecutionResult.ExecutionRejected rejected =
            Assert.IsType<IntegrationCensusExecutionResult.ExecutionRejected>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            IntegrationCensusExecutionRejectionReason
                .TypeIdentityDomainMismatch,
            rejected.Rejection.Reason);
        Assert.Equal(0, harness.ProducerCalls);
        Assert.Equal(9, harness.Releases);
    }

    [Fact]
    public void IntegrationCensusExecutor_RejectsWrongExecutablePayloadType()
    {
        Harness harness = BasicHarness();
        harness.WrongSelectedAccessType = true;

        IntegrationCensusExecutionResult.ExecutionRejected rejected =
            Assert.IsType<IntegrationCensusExecutionResult.ExecutionRejected>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            IntegrationCensusExecutionRejectionReason
                .ExecutableBindingTypeMismatch,
            rejected.Rejection.Reason);
        Assert.Same(
            IntegrationAnalysisCatalog.SelectedTypesRequirement,
            rejected.Rejection.Requirement);
        Assert.Equal(0, harness.ProducerCalls);
    }

    [Fact]
    public void IntegrationCensusExecutor_RejectsParticipantIncidenceMismatchBeforeProducerExecution()
    {
        IntegrationSourceParticipantIdentity participant = Participant("source");
        IntegrationSourceParticipantIdentity foreign = Participant("foreign");
        Context context = new("one");
        var harness = new Harness(
            [participant],
            [],
            [context],
            [
                new IntegrationSourceBindingContextIncidence(
                    foreign,
                    [context]),
            ]);

        IntegrationCensusExecutionResult.ExecutionRejected rejected =
            Assert.IsType<IntegrationCensusExecutionResult.ExecutionRejected>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            IntegrationCensusExecutionRejectionReason
                .ParticipantContextMismatch,
            rejected.Rejection.Reason);
        Assert.Equal(0, harness.ProducerCalls);
    }

    [Fact]
    public void IntegrationCensusExecutor_RejectsSelectedTypeOutsideParticipantRoster()
    {
        IntegrationSourceParticipantIdentity participant = Participant("source");
        IntegrationSourceParticipantIdentity foreign = Participant("foreign");
        Context context = new("one");
        var harness = new Harness(
            [participant],
            [new IntegrationTypeIdentity(foreign, TypeName("Foreign", "Type"))],
            [context],
            [
                new IntegrationSourceBindingContextIncidence(
                    participant,
                    [context]),
            ]);

        IntegrationCensusExecutionResult.ExecutionRejected rejected =
            Assert.IsType<IntegrationCensusExecutionResult.ExecutionRejected>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            IntegrationCensusExecutionRejectionReason
                .SelectedTypeParticipantMismatch,
            rejected.Rejection.Reason);
        Assert.Equal(0, harness.ProducerCalls);
    }

    [Fact]
    public void IntegrationCensusExecutor_RejectsContextAndCompletenessDomainMismatch()
    {
        Harness contextHarness = BasicHarness();
        contextHarness.OperationBindingContexts = [new Context("foreign")];
        IntegrationCensusExecutionResult.ExecutionRejected contextRejected =
            Assert.IsType<IntegrationCensusExecutionResult.ExecutionRejected>(
                contextHarness.Run(
                    Xunit.TestContext.Current.CancellationToken));
        Assert.Equal(
            IntegrationCensusExecutionRejectionReason
                .BindingContextDomainMismatch,
            contextRejected.Rejection.Reason);
        Assert.Equal(0, contextHarness.ProducerCalls);

        Harness completenessHarness = BasicHarness();
        completenessHarness.UseForeignCompleteness = true;
        IntegrationCensusExecutionResult.ExecutionRejected
            completenessRejected =
            Assert.IsType<IntegrationCensusExecutionResult.ExecutionRejected>(
                completenessHarness.Run(
                    Xunit.TestContext.Current.CancellationToken));
        Assert.Equal(
            IntegrationCensusExecutionRejectionReason.CompletenessMismatch,
            completenessRejected.Rejection.Reason);
        Assert.Equal(0, completenessHarness.ProducerCalls);
    }

    [Fact]
    public void IntegrationCensusExecutor_RejectsMismatchedProducerPolicyAccess()
    {
        Harness harness = BasicHarness();
        harness.MismatchProducerPolicy = true;

        IntegrationCensusExecutionResult.ExecutionRejected rejected =
            Assert.IsType<IntegrationCensusExecutionResult.ExecutionRejected>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            IntegrationCensusExecutionRejectionReason
                .ProducerPolicyMismatch,
            rejected.Rejection.Reason);
        Assert.Equal(0, harness.ProducerCalls);
    }

    [Fact]
    public void IntegrationCensusExecutor_RejectsMismatchedProducerReceipt()
    {
        Harness harness = BasicHarness();
        IntegrationSourceParticipantIdentity foreign = Participant("foreign");
        harness.Produce = address =>
                new IntegrationProducerPolicyAttempt.Completed(
                    new IntegrationProducerPolicyAttemptAddress(
                        foreign,
                        address.Policy),
                    []);

        IntegrationCensusExecutionResult.ExecutionRejected rejected =
            Assert.IsType<IntegrationCensusExecutionResult.ExecutionRejected>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            IntegrationCensusExecutionRejectionReason.InvalidProducerReceipt,
            rejected.Rejection.Reason);
        Assert.Equal(1, harness.ProducerCalls);
    }

    [Fact]
    public void IntegrationCensusExecutor_DoesNotInvokeProducersForUnavailableParticipants()
    {
        IntegrationSourceParticipantIdentity participant = Participant("source");
        Context context = new("one");
        var sourceAttempt =
            new IntegrationSourceParticipantAttempt.Rejected(
                participant,
                new ParticipantRejection());
        var harness = new Harness(
            [participant],
            [],
            [context],
            [
                new IntegrationSourceBindingContextIncidence(
                    participant,
                    [context]),
            ],
            [sourceAttempt]);

        IntegrationCensusSnapshot snapshot =
            Assert.IsType<IntegrationCensusExecutionResult.Ready>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken)).Snapshot;

        Assert.Equal(0, harness.ProducerCalls);
        Assert.Equal(3, snapshot.ProducerPolicyAttempts.Length);
        Assert.All(
            snapshot.ProducerPolicyAttempts,
            attempt => Assert.IsType<
                IntegrationProducerPolicyAttempt.Unavailable>(attempt));
        Assert.False(snapshot.IsComplete);
        Assert.Empty(snapshot.Candidates);
    }

    [Fact]
    public void IntegrationCensusExecutor_PreservesCandidateFailureWithoutManufacturingOut()
    {
        IntegrationSourceParticipantIdentity participant = Participant("source");
        Context context = new("one");
        MetadataTypeDefinitionName sourceType = TypeName("Source", "Client");
        IntegrationCandidateIdentity candidate = new(
            InspectionGraphIntegrationsCatalog.IntegrationObserved,
            IntegrationConceptCatalog.AI,
            new IntegrationCandidateSourceIdentity(
                participant,
                new IntegrationCandidateSourceElement.Type(sourceType)),
            AssemblyPeer("Peer", TypeName("Peer", "Client")));
        var harness = new Harness(
            [participant],
            [new IntegrationTypeIdentity(participant, sourceType)],
            [context],
            [
                new IntegrationSourceBindingContextIncidence(
                    participant,
                    [context]),
            ])
        {
            Produce = address =>
                ReferenceEquals(
                    address.Policy,
                    IntegrationAnalysisCatalog.EcosystemObserved)
                    ? new IntegrationProducerPolicyAttempt.Completed(
                        address,
                        [candidate])
                    : new IntegrationProducerPolicyAttempt.Completed(
                        address,
                        []),
            Bind = request =>
                new IntegrationPeerBindingAttempt.Failed(
                    request.Address,
                    new CandidateFailure()),
        };

        IntegrationCensusSnapshot snapshot =
            Assert.IsType<IntegrationCensusExecutionResult.Ready>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken)).Snapshot;

        Assert.Single(snapshot.FailedCandidateAttempts);
        Assert.Empty(snapshot.ClassifiedAttempts);
        Assert.False(snapshot.IsComplete);
        Assert.Equal(0, harness.ResolutionCalls);
    }

    [Fact]
    public void IntegrationCensusExecutor_CancellationAfterOwnerOperationReleasesAccess()
    {
        IntegrationSourceParticipantIdentity participant = Participant("source");
        Context context = new("one");
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                Xunit.TestContext.Current.CancellationToken);
        var harness = new Harness(
            [participant],
            [],
            [context],
            [
                new IntegrationSourceBindingContextIncidence(
                    participant,
                    [context]),
            ])
        {
            Produce = address =>
            {
                cancellation.Cancel();
                return new IntegrationProducerPolicyAttempt.Completed(
                    address,
                    []);
            },
        };

        Assert.IsType<IntegrationCensusExecutionResult.Cancelled>(
            harness.Run(cancellation.Token));
        Assert.Equal(1, harness.ProducerCalls);
        Assert.Equal(9, harness.Releases);
    }

    [Fact]
    public void IntegrationCensusExecutor_BindingBatchesExactlyCoverOneContext()
    {
        IntegrationSourceParticipantIdentity participant = Participant("source");
        Context context = new("one");
        MetadataTypeDefinitionName sourceType = TypeName("Source", "Client");
        IntegrationCandidateIdentity candidate = new(
            InspectionGraphIntegrationsCatalog.IntegrationObserved,
            IntegrationConceptCatalog.AI,
            new IntegrationCandidateSourceIdentity(
                participant,
                new IntegrationCandidateSourceElement.Type(sourceType)),
            AssemblyPeer("Peer", TypeName("Peer", "Client")));
        var harness = new Harness(
            [participant],
            [new IntegrationTypeIdentity(participant, sourceType)],
            [context],
            [
                new IntegrationSourceBindingContextIncidence(
                    participant,
                    [context]),
            ])
        {
            Produce = address =>
                ReferenceEquals(
                    address.Policy,
                    IntegrationAnalysisCatalog.EcosystemObserved)
                    ? new IntegrationProducerPolicyAttempt.Completed(
                        address,
                        [candidate])
                    : new IntegrationProducerPolicyAttempt.Completed(
                        address,
                        []),
            BindBatch = (bindingContext, requests) =>
                new IntegrationPeerBindingBatch(
                    bindingContext,
                    []),
        };

        IntegrationCensusExecutionResult.ExecutionRejected rejected =
            Assert.IsType<IntegrationCensusExecutionResult.ExecutionRejected>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            IntegrationCensusExecutionRejectionReason.InvalidPeerBindingBatch,
            rejected.Rejection.Reason);
        Assert.Equal(0, harness.ResolutionCalls);
    }

    [Fact]
    public void IntegrationCensusExecutor_ResolutionBatchMustConsumeEveryExactBinding()
    {
        (Harness harness, _) = CandidateHarness();
        harness.ResolveBatch = batch =>
            new IntegrationCandidateResolutionBatch(batch, []);

        IntegrationCensusExecutionResult.ExecutionRejected rejected =
            Assert.IsType<IntegrationCensusExecutionResult.ExecutionRejected>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            IntegrationCensusExecutionRejectionReason.InvalidResolutionBatch,
            rejected.Rejection.Reason);
    }

    [Fact]
    public void IntegrationCensusExecutor_ResolvedFulfillmentSourcesExactlyCoverDeclaredLookups()
    {
        (Harness harness, IntegrationCandidateIdentity candidate) =
            CandidateHarness(includeFulfillmentSource: true);
        harness.Resolve = binding =>
            new IntegrationCandidateResolutionAttempt.Resolved(
                binding,
                Resolved(
                    candidate.Peer,
                    candidate.Source.Participant,
                    candidate.Peer.Type));

        IntegrationCensusExecutionResult.ExecutionRejected rejected =
            Assert.IsType<IntegrationCensusExecutionResult.ExecutionRejected>(
                harness.Run(
                    Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(
            IntegrationCensusExecutionRejectionReason
                .InvalidResolutionEvidence,
            rejected.Rejection.Reason);
    }

    [Fact]
    public void IntegrationCensusExecutor_FulfillmentResolutionCannotRepeatLookup()
    {
        IntegrationSourceParticipantIdentity participant =
            Participant("source");
        MetadataTypeDefinitionName type = TypeName("Source", "Client");
        IntegrationCandidatePeerIdentity.NamedType lookup =
            NamedPeer(
                new MetadataTypeReferenceScope.CurrentAssembly(),
                type);
        var address = new IntegrationCandidateAttemptAddress(
            new IntegrationCandidateIdentity(
                InspectionGraphIntegrationsCatalog.IntegrationObserved,
                IntegrationConceptCatalog.AI,
                new IntegrationCandidateSourceIdentity(
                    participant,
                    new IntegrationCandidateSourceElement.Type(type)),
                AssemblyPeer("Peer", TypeName("Peer", "Client"))),
            new Context("one"));
        var binding = new IntegrationPeerBindingAttempt.Bound(
            address,
            new PeerBinding());
        IntegrationResolvedPeer resolution =
            Resolved(lookup, participant, type);

        Assert.Throws<ArgumentException>(() =>
            new IntegrationCandidateResolutionAttempt.Resolved(
                binding,
                Resolved(
                    address.Candidate.Peer,
                    participant,
                    address.Candidate.Peer.Type),
                [resolution, resolution]));
    }

    static IntegrationSourceParticipantIdentity Participant(string name) =>
        IntegrationSourceParticipantIdentity.Portable(
            new RealizedMemberCoordinate.Package(
                name,
                "1.0.0",
                "fixture",
                "net11.0",
                null),
            new AssemblyReferenceIdentity(name, Version, null, null));

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(@namespace, [name])).Name;

    static MemberAnchor Anchor(MetadataTypeDefinitionName declaringType) =>
        new(
            $"{declaringType.ToMetadataFullName()}.Create()",
            $"{declaringType.ToMetadataFullName()}.Create",
            "0000000000",
            declaringType.ToMetadataFullName(),
            "Create");

    static IntegrationCandidatePeerIdentity.NamedType NamedPeer(
        MetadataTypeReferenceScope scope,
        MetadataTypeDefinitionName type) =>
        new(new MetadataNamedTypeReference(scope, type));

    static IntegrationCandidatePeerIdentity.NamedType AssemblyPeer(
        string assembly,
        MetadataTypeDefinitionName type) =>
        NamedPeer(
            new MetadataTypeReferenceScope.AssemblyReference(
                new AssemblyReferenceIdentity(
                    assembly,
                    Version,
                    null,
                    null)),
            type);

    static IntegrationResolvedPeer Resolved(
        IntegrationCandidatePeerIdentity lookup,
        IntegrationSourceParticipantIdentity participant,
        MetadataTypeDefinitionName type) =>
        new(lookup, [new IntegrationTypeIdentity(participant, type)]);

    static Harness BasicHarness()
    {
        IntegrationSourceParticipantIdentity participant =
            Participant("source");
        Context context = new("one");
        return new Harness(
            [participant],
            [],
            [context],
            [
                new IntegrationSourceBindingContextIncidence(
                    participant,
                    [context]),
            ]);
    }

    static (Harness Harness, IntegrationCandidateIdentity Candidate)
        CandidateHarness(bool includeFulfillmentSource = false)
    {
        IntegrationSourceParticipantIdentity participant =
            Participant("source");
        Context context = new("one");
        MetadataTypeDefinitionName sourceType =
            TypeName("Source", "Client");
        IntegrationCandidateIdentity candidate = new(
            InspectionGraphIntegrationsCatalog.IntegrationObserved,
            IntegrationConceptCatalog.AI,
            new IntegrationCandidateSourceIdentity(
                participant,
                new IntegrationCandidateSourceElement.Type(sourceType)),
            AssemblyPeer("Peer", TypeName("Peer", "Client")));
        var harness = new Harness(
            [participant],
            [new IntegrationTypeIdentity(participant, sourceType)],
            [context],
            [
                new IntegrationSourceBindingContextIncidence(
                    participant,
                    [context]),
            ]);
        IntegrationCandidatePeerIdentity.NamedType? sourceLookup =
            includeFulfillmentSource
                ? NamedPeer(
                    new MetadataTypeReferenceScope.CurrentAssembly(),
                    sourceType)
                : null;
        harness.Produce = address =>
        {
            if (!ReferenceEquals(
                    address.Policy,
                    IntegrationAnalysisCatalog.EcosystemObserved))
            {
                return new IntegrationProducerPolicyAttempt.Completed(
                    address,
                    []);
            }
            return sourceLookup is null
                ? new IntegrationProducerPolicyAttempt.Completed(
                    address,
                    [candidate])
                : IntegrationProducerPolicyAttempt.Completed.WithEvidence(
                    address,
                    [
                        new IntegrationCandidateEvidence(
                            candidate,
                            [sourceLookup]),
                    ]);
        };
        return (harness, candidate);
    }

    sealed class Harness
    {
        readonly ImmutableArray<IntegrationSourceParticipantIdentity>
            _participants;
        readonly ImmutableArray<IntegrationTypeIdentity> _selectedTypes;
        readonly ImmutableArray<IIntegrationBindingContextIdentity> _contexts;
        readonly ImmutableArray<IntegrationSourceBindingContextIncidence>
            _incidence;
        readonly ImmutableArray<IntegrationSourceParticipantAttempt>
            _sourceAttempts;
        readonly IdentityDomain _identityDomain = new();

        public Harness(
            IEnumerable<IntegrationSourceParticipantIdentity> participants,
            IEnumerable<IntegrationTypeIdentity> selectedTypes,
            IEnumerable<IIntegrationBindingContextIdentity> contexts,
            IEnumerable<IntegrationSourceBindingContextIncidence> incidence,
            IEnumerable<IntegrationSourceParticipantAttempt>?
                sourceAttempts = null)
        {
            _participants = [.. participants];
            _selectedTypes = [.. selectedTypes];
            _contexts = [.. contexts];
            _incidence = [.. incidence];
            _sourceAttempts =
            [
                .. sourceAttempts
                    ?? _participants.Select(participant =>
                        new IntegrationSourceParticipantAttempt.Available(
                            participant)),
            ];
        }

        public Action<string>? Record { get; set; }
        public Func<
            IntegrationProducerPolicyAttemptAddress,
            IntegrationProducerPolicyAttempt>? Produce { get; set; }
        public Func<
            IntegrationCandidateEvaluationRequest,
            IntegrationPeerBindingAttempt>? Bind { get; set; }
        public Func<
            IIntegrationBindingContextIdentity,
            ImmutableArray<IntegrationCandidateEvaluationRequest>,
            IntegrationPeerBindingBatch>? BindBatch { get; set; }
        public Func<
            IntegrationPeerBindingAttempt.Bound,
            IntegrationCandidateResolutionAttempt>? Resolve { get; set; }
        public IIntegrationTypeIdentityDomain? ResolutionIdentityDomain
            { get; set; }
        public IEnumerable<IIntegrationBindingContextIdentity>?
            OperationBindingContexts { get; set; }
        public bool UseForeignCompleteness { get; set; }
        public bool MismatchProducerPolicy { get; set; }
        public bool WrongSelectedAccessType { get; set; }
        public Func<
            IntegrationPeerBindingBatch,
            IntegrationCandidateResolutionBatch>? ResolveBatch { get; set; }
        public int ProducerCalls { get; private set; }
        public int ResolutionCalls { get; private set; }
        public int Releases { get; private set; }

        public IntegrationCensusExecutionResult Run(
            CancellationToken cancellationToken = default)
        {
            using var workspace = new InspectionWorkspace();
            var completeness = new UniverseCompleteness();
            var participantAccess =
                new IntegrationSourceParticipantAccess(
                    _participants,
                    _sourceAttempts);
            var selectedAccess =
                new IntegrationSelectedTypeAccess(
                    _identityDomain,
                    _selectedTypes);
            var contextAccess =
                new IntegrationBindingContextAccess(
                    _contexts,
                    _incidence);
            ImmutableArray<IIntegrationBindingContextIdentity>
                operationContexts =
            [
                .. OperationBindingContexts ?? _contexts,
            ];
            var bindingAccess =
                new IntegrationPeerBindingAccess(
                    operationContexts,
                    (context, requests, token) =>
                    {
                        Record?.Invoke(
                            $"bind:{((Context)context).Name}:{requests.Length}");
                        token.ThrowIfCancellationRequested();
                        if (BindBatch is not null)
                            return BindBatch(context, requests);
                        return new IntegrationPeerBindingBatch(
                            context,
                            requests.Select(request =>
                                Bind?.Invoke(request)
                                    ?? new IntegrationPeerBindingAttempt.Bound(
                                        request.Address,
                                        new PeerBinding())));
                    });
            var resolutionAccess =
                new IntegrationExactPeerResolutionAccess(
                    ResolutionIdentityDomain ?? _identityDomain,
                    operationContexts,
                    (batch, token) =>
                    {
                        ImmutableArray<IntegrationPeerBindingAttempt.Bound>
                            bound =
                        [
                            .. batch.Attempts.OfType<
                                IntegrationPeerBindingAttempt.Bound>(),
                        ];
                        Record?.Invoke(
                            $"resolve:{((Context)batch.BindingContext).Name}:{bound.Length}");
                        ResolutionCalls++;
                        token.ThrowIfCancellationRequested();
                        if (ResolveBatch is not null)
                            return ResolveBatch(batch);
                        return new IntegrationCandidateResolutionBatch(
                            batch,
                            bound.Select(attempt =>
                                Resolve?.Invoke(attempt)
                                    ?? new IntegrationCandidateResolutionAttempt
                                        .Failed(
                                            attempt,
                                            new CandidateFailure())));
                    });
            var completenessAccess =
                new IntegrationCompletenessAccess(
                    UseForeignCompleteness
                        ? new UniverseCompleteness()
                        : completeness);

            AnalysisUniverseCapabilityDescriptor[] capabilities =
            [
                .. IntegrationAnalysisCatalog.UniverseRequirements
                    .Select(requirement => requirement.Capability)
                    .Distinct<AnalysisUniverseCapabilityDescriptor>(
                        ReferenceEqualityComparer.Instance),
            ];
            AnalysisUniverseOffer offer =
                workspace.CreateAnalysisUniverseOffer(
                    new UniverseIdentity(),
                    new UniverseBoundary(),
                    new UniverseBoundary(),
                    capabilities,
                    completeness,
                    capabilities.Select(capability =>
                        Registration(
                            capability,
                            participantAccess,
                            selectedAccess,
                            contextAccess,
                            bindingAccess,
                            resolutionAccess,
                            completenessAccess)));
            AnalysisRequestPlan plan = Plan(offer.Description);
            return IntegrationCensusExecutor.Execute(
                offer,
                plan,
                cancellationToken);
        }

        AnalysisUniverseCapabilityRegistration Registration(
            AnalysisUniverseCapabilityDescriptor capability,
            IntegrationSourceParticipantAccess participants,
            IntegrationSelectedTypeAccess selected,
            IntegrationBindingContextAccess contexts,
            IntegrationPeerBindingAccess binding,
            IntegrationExactPeerResolutionAccess resolution,
            IntegrationCompletenessAccess completeness)
        {
            if (ReferenceEquals(
                    capability,
                    IntegrationAnalysisCatalog.OrderedParticipants))
            {
                return Ready(capability, participants);
            }
            if (ReferenceEquals(
                    capability,
                    IntegrationAnalysisCatalog.SelectedTypes))
            {
                if (WrongSelectedAccessType)
                    return Ready(capability, new object());
                return Ready(capability, selected);
            }
            if (ReferenceEquals(
                    capability,
                    IntegrationAnalysisCatalog.BindingContexts))
            {
                return Ready(capability, contexts);
            }
            if (ReferenceEquals(
                    capability,
                    IntegrationAnalysisCatalog.PeerBinding))
            {
                return Ready(capability, binding);
            }
            if (ReferenceEquals(
                    capability,
                    IntegrationAnalysisCatalog.ExactPeerResolution))
            {
                return Ready(capability, resolution);
            }
            if (ReferenceEquals(
                    capability,
                    IntegrationAnalysisCatalog.Completeness))
            {
                return Ready(capability, completeness);
            }

            IntegrationProducerPolicyBinding policy =
                IntegrationAnalysisCatalog.ProducerPolicies.Single(item =>
                    ReferenceEquals(
                        item.EvidenceCapability,
                        capability));
            IntegrationProducerPolicyBinding accessPolicy =
                MismatchProducerPolicy
                    ? IntegrationAnalysisCatalog.ProducerPolicies.First(
                        candidate => !ReferenceEquals(candidate, policy))
                    : policy;
            var access = new IntegrationProducerPolicyAccess(
                accessPolicy,
                (address, token) =>
                {
                    Record?.Invoke(
                        $"produce:{Package(address.Participant)}:{policy.Policy.Id.Value}");
                    ProducerCalls++;
                    token.ThrowIfCancellationRequested();
                    return Produce?.Invoke(address)
                        ?? new IntegrationProducerPolicyAttempt.Completed(
                            address,
                            []);
                });
            return Ready(capability, access);
        }

        AnalysisUniverseCapabilityRegistration Ready<TAccess>(
            AnalysisUniverseCapabilityDescriptor capability,
            TAccess access)
            where TAccess : class =>
            new AnalysisUniverseCapabilityRegistration<TAccess>(
                capability,
                (_, _) =>
                    new AnalysisUniverseCapabilityAcquisition<TAccess>.Ready(
                        new AnalysisUniverseCapabilityLease<TAccess>(
                            access,
                            () => Releases++)));

        static string Package(
            IntegrationSourceParticipantIdentity participant) =>
            Assert.IsType<RealizedMemberCoordinate.Package>(
                participant.Coordinate).PackageId;
    }

    static AnalysisRequestPlan Plan(
        AnalysisUniverseDescription universe) =>
        Assert.IsType<AnalysisRequestPlanResult.Accepted>(
            IntegrationAnalysisCatalog.Capabilities.Plan(
                new AnalysisRequest(
                    IntegrationAnalysisCatalog.Analysis,
                    new AnalysisReportSurface(
                        AnalysisReportSurfaceKind.Workspace,
                        new SurfaceIdentity(),
                        [
                            new AnalysisTargetBinding(
                                IntegrationAnalysisCatalog.WorkspaceDomain,
                                new TargetIdentity()),
                        ]),
                    universe,
                    AnalysisQuestionMode.Census,
                    IntegrationAnalysisCatalog.Rows),
                new AnalysisPlanningEnvironment(
                    new InspectionQueryRegistry<object>()
                        .Add(
                            AssemblyContextIntegrationsQuery.Definition,
                            _ => new AssemblyContextIntegrationsResult([]))
                        .Add(
                            ExtensionMethodsQuery.Definition,
                            _ => new ExtensionMethodsResult.Available([]))
                        .Add(
                            AssemblyContextIntegrationOpportunitiesQuery
                                .Definition,
                            (_, _) =>
                                new AssemblyContextIntegrationOpportunitiesResult(
                                    []),
                            AssemblyContextIntegrationsQuery.Definition)
                        .Compile(),
                    IntegrationAnalysisCatalog.ProducerPolicies.Select(
                        policy => policy.ProducerPrerequisite))))
        .Plan;

    sealed class Context(string name) :
        IIntegrationBindingContextIdentity
    {
        public string Name { get; } = name;
    }

    sealed class IdentityDomain : IIntegrationTypeIdentityDomain;
    sealed class PeerBinding : IIntegrationPeerBinding;
    sealed class CandidateFailure : IIntegrationCandidateFailure;
    sealed class ParticipantRejection :
        IIntegrationSourceParticipantRejection;
    sealed class UniverseCompleteness : IAnalysisUniverseCompleteness;
    sealed class UniverseIdentity : IAnalysisUniverseIdentity;
    sealed class UniverseBoundary : IAnalysisUniverseBoundary;
    sealed class SurfaceIdentity : IAnalysisReportSurfaceIdentity;
    sealed class TargetIdentity : IAnalysisTargetIdentity;
}
