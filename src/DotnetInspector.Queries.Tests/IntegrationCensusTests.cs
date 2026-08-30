using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class IntegrationCensusTests
{
    // ---- Candidate identity component set -------------------------------

    [Fact]
    public void IntegrationCandidate_IdentityDoesNotContainDispositionOrGraphLocalIds()
    {
        string[] properties =
        [
            .. typeof(IntegrationCandidateIdentity)
                .GetProperties()
                .Select(property => property.Name),
        ];

        Assert.Equal(
            ["Relationship", "Concept", "Source", "Peer", "IsPortable"],
            properties);
        Assert.DoesNotContain("Disposition", properties);
        Assert.DoesNotContain("Occurrence", properties);
        Assert.DoesNotContain("GraphId", properties);

        // Disposition is a closed successful outcome, never part of identity,
        // and a failure outcome is not a disposition at all.
        Type[] dispositions =
        [
            .. typeof(IntegrationCandidateDisposition)
                .GetNestedTypes()
                .Where(type => type.IsNestedPublic)
                .OrderBy(type => type.Name),
        ];
        Assert.Equal(
            [
                typeof(IntegrationCandidateDisposition.In),
                typeof(IntegrationCandidateDisposition.Out),
            ],
            dispositions);
        Assert.False(
            typeof(IntegrationCandidateDisposition).IsAssignableFrom(
                typeof(IntegrationCandidateAttempt.Failed)));
    }

    [Fact]
    public void IntegrationCandidate_EquivalentAssemblyReferenceScopesShareIdentity()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        MetadataTypeDefinitionName type = TypeName("Peer", "Client");

        // Null and "neutral" cultures are semantically the same ECMA identity,
        // yet the underlying record is not value-equal across the two spellings.
        var left = new AssemblyReferenceIdentity("Lib", Ver, null, null);
        var right = new AssemblyReferenceIdentity("Lib", Ver, "neutral", null);
        Assert.NotEqual(left, right);
        Assert.True(left.IsEquivalentTo(right));

        IntegrationCandidateIdentity first = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            AssemblyPeer(left, type));
        IntegrationCandidateIdentity second = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            AssemblyPeer(right, type));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void IntegrationCandidate_DifferentRelationshipConceptSourceTypeOrScopeSplitIdentity()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        IntegrationCandidatePeerIdentity peer =
            AssemblyPeer(Assembly("Peer"), TypeName("Peer", "Client"));
        IntegrationCandidateIdentity baseline = new(
            Observed,
            IntegrationConceptCatalog.AI,
            new IntegrationCandidateSourceIdentity(participant, TypeElement()),
            peer);

        // Relationship differs (observed vs opportunity for a shared concept).
        // An opportunity requires a policy-issued target rather than a named
        // peer, so a valid opportunity candidate is used here.
        Assert.NotEqual(
            baseline,
            OpportunityCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                PolicyTargetPeer("Peer.Lib", TypeName("Peer", "Client"))));
        // Concept differs.
        Assert.NotEqual(
            baseline,
            new IntegrationCandidateIdentity(
                Observed,
                IntegrationConceptCatalog.Aspire,
                new IntegrationCandidateSourceIdentity(participant, TypeElement()),
                peer));
        // Source element differs.
        Assert.NotEqual(
            baseline,
            new IntegrationCandidateIdentity(
                Observed,
                IntegrationConceptCatalog.AI,
                new IntegrationCandidateSourceIdentity(
                    participant,
                    TypeElement("Src", "Other")),
                peer));
        // Peer Type differs.
        Assert.NotEqual(
            baseline,
            ObservedCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                AssemblyPeer(Assembly("Peer"), TypeName("Peer", "Other"))));
        // Peer scope differs (distinct assembly reference).
        Assert.NotEqual(
            baseline,
            ObservedCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                AssemblyPeer(Assembly("Other"), TypeName("Peer", "Client"))));
    }

    [Fact]
    public void IntegrationCandidate_DistinctScopeKindsSplitIdentity()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        MetadataTypeDefinitionName type = TypeName("Peer", "Client");

        IntegrationCandidatePeerIdentity current = NamedPeer(
            new MetadataTypeReferenceScope.CurrentAssembly(),
            type);
        IntegrationCandidatePeerIdentity intrinsic = NamedPeer(
            new MetadataTypeReferenceScope.IntrinsicCoreLibrary(),
            type);
        IntegrationCandidatePeerIdentity assembly = AssemblyPeer(
            Assembly("Peer"),
            type);
        IntegrationCandidatePeerIdentity module = NamedPeer(
            new MetadataTypeReferenceScope.ModuleReference("peer.netmodule"),
            type);

        IntegrationCandidatePeerIdentity[] peers =
            [current, intrinsic, assembly, module];
        for (int left = 0; left < peers.Length; left++)
        {
            for (int right = left + 1; right < peers.Length; right++)
            {
                Assert.NotEqual(
                    ObservedCandidate(
                        participant,
                        IntegrationConceptCatalog.AI,
                        peers[left]),
                    ObservedCandidate(
                        participant,
                        IntegrationConceptCatalog.AI,
                        peers[right]));
            }
        }

        // Same current-assembly scope keeps identity.
        Assert.Equal(
            ObservedCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                NamedPeer(new MetadataTypeReferenceScope.CurrentAssembly(), type)),
            ObservedCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                NamedPeer(new MetadataTypeReferenceScope.CurrentAssembly(), type)));
    }

    [Fact]
    public void IntegrationCandidate_ModuleScopeNamesCompareOrdinally()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        MetadataTypeDefinitionName type = TypeName("Peer", "Client");

        Assert.Equal(
            ObservedCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                NamedPeer(
                    new MetadataTypeReferenceScope.ModuleReference("peer.netmodule"),
                    type)),
            ObservedCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                NamedPeer(
                    new MetadataTypeReferenceScope.ModuleReference("peer.netmodule"),
                    type)));
        Assert.NotEqual(
            ObservedCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                NamedPeer(
                    new MetadataTypeReferenceScope.ModuleReference("peer.netmodule"),
                    type)),
            ObservedCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                NamedPeer(
                    new MetadataTypeReferenceScope.ModuleReference("Peer.NetModule"),
                    type)));
    }

    [Fact]
    public void IntegrationCandidate_PolicyTargetAssemblyNameComparesOrdinalIgnoreCase()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        MetadataTypeDefinitionName type = TypeName("Peer", "Client");

        Assert.Equal(
            OpportunityCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                PolicyTargetPeer("Peer.Lib", type)),
            OpportunityCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                PolicyTargetPeer("peer.lib", type)));
        Assert.NotEqual(
            OpportunityCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                PolicyTargetPeer("Peer.Lib", type)),
            OpportunityCandidate(
                participant,
                IntegrationConceptCatalog.AI,
                PolicyTargetPeer("Other.Lib", type)));
    }

    // ---- Source participant identity ------------------------------------

    [Fact]
    public void IntegrationCandidate_PortableSourceIdentityMatchesStructurallyEquivalentCoordinates()
    {
        AssemblyReferenceIdentity assembly = Assembly("Src");
        AssemblyReferenceIdentity equivalent =
            new("Src", Ver, "neutral", null);

        RealizedMemberCoordinate package = new RealizedMemberCoordinate.Package(
            "contoso.client", "1.0.0", "fixture", "net11.0", null);
        RealizedMemberCoordinate packageCopy = new RealizedMemberCoordinate.Package(
            "contoso.client", "1.0.0", "fixture", "net11.0", null);
        RealizedMemberCoordinate platform = new RealizedMemberCoordinate.Platform(
            "runtime", "11.0.0", "fixture", "net11.0", assembly: null);
        RealizedMemberCoordinate platformCopy = new RealizedMemberCoordinate.Platform(
            "runtime", "11.0.0", "fixture", "net11.0", assembly: null);
        RealizedMemberCoordinate embedded = new RealizedMemberCoordinate.Embedded(
            "lib/peer.dll", Digest, "Peer");
        RealizedMemberCoordinate embeddedCopy = new RealizedMemberCoordinate.Embedded(
            "lib/peer.dll", Digest, "Peer");

        foreach ((RealizedMemberCoordinate a, RealizedMemberCoordinate b)
            in new[]
            {
                (package, packageCopy),
                (platform, platformCopy),
                (embedded, embeddedCopy),
            })
        {
            IntegrationSourceParticipantIdentity left =
                IntegrationSourceParticipantIdentity.Portable(a, assembly);
            IntegrationSourceParticipantIdentity right =
                IntegrationSourceParticipantIdentity.Portable(b, equivalent);
            Assert.True(left.Equals(right));
            Assert.Equal(left.GetHashCode(), right.GetHashCode());
        }

        // Distinct coordinate kinds never merge.
        Assert.False(
            IntegrationSourceParticipantIdentity.Portable(package, assembly)
                .Equals(IntegrationSourceParticipantIdentity.Portable(
                    platform,
                    assembly)));
    }

    [Fact]
    public void IntegrationCandidate_WorkspaceIdentityIsolatedByAcquisitionRegistration()
    {
        AssemblyReferenceIdentity assembly = Assembly("Src");
        AssemblyAcquisitionRegistration first = Registration();
        AssemblyAcquisitionRegistration second = Registration();

        Assert.True(
            IntegrationSourceParticipantIdentity.Workspace(first, assembly)
                .Equals(IntegrationSourceParticipantIdentity.Workspace(
                    first,
                    assembly)));
        Assert.False(
            IntegrationSourceParticipantIdentity.Workspace(first, assembly)
                .Equals(IntegrationSourceParticipantIdentity.Workspace(
                    second,
                    assembly)));

        // A portable and a workspace source are never the same identity.
        RealizedMemberCoordinate coordinate =
            new RealizedMemberCoordinate.Package(
                "contoso.client", "1.0.0", "fixture", "net11.0", null);
        Assert.False(
            IntegrationSourceParticipantIdentity.Portable(coordinate, assembly)
                .Equals(IntegrationSourceParticipantIdentity.Workspace(
                    first,
                    assembly)));
    }

    // ---- Source element -------------------------------------------------

    [Fact]
    public void IntegrationCandidate_MemberSourceRejectsDeclaringTypeAnchorDisagreement()
    {
        MetadataTypeDefinitionName declaringType = TypeName("Src", "Widget");
        MemberAnchor agreeing = Anchor(declaringType.ToMetadataFullName());
        MemberAnchor disagreeing = Anchor("Src.Other");

        // Agreement is accepted.
        _ = new IntegrationCandidateSourceElement.Member(declaringType, agreeing);
        Assert.Throws<ArgumentException>(() =>
            new IntegrationCandidateSourceElement.Member(
                declaringType,
                disagreeing));
    }

    [Fact]
    public void IntegrationCandidate_RawExtensionRelationshipIsNotACandidate()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        IntegrationCandidateSourceIdentity source =
            new(participant, TypeElement());
        IntegrationCandidatePeerIdentity peer =
            AssemblyPeer(Assembly("Peer"), TypeName("Peer", "Client"));

        Assert.Throws<ArgumentException>(() =>
            new IntegrationCandidateIdentity(
                InspectionGraphIntegrationsCatalog.Extension,
                IntegrationConceptCatalog.AI,
                source,
                peer));
        Assert.Throws<ArgumentException>(() =>
            new IntegrationCandidateIdentity(
                InspectionGraphIntegrationsCatalog.MetadataReference,
                IntegrationConceptCatalog.AI,
                source,
                peer));
        // A concept the relationship's policies never inform is also rejected.
        Assert.Throws<ArgumentException>(() =>
            new IntegrationCandidateIdentity(
                OpportunityRel,
                IntegrationConceptCatalog.OpenTelemetry,
                source,
                peer));
    }

    [Fact]
    public void IntegrationCandidate_CrossedRelationshipArmsAreRejected()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        MetadataTypeDefinitionName type = TypeName("Peer", "Client");
        IntegrationCandidateSourceIdentity typeSource =
            new(participant, TypeElement());
        IntegrationCandidateSourceIdentity memberSource =
            new(
                participant,
                new IntegrationCandidateSourceElement.Member(
                    TypeName("Src", "Widget"),
                    Anchor(TypeName("Src", "Widget").ToMetadataFullName())));
        IntegrationCandidatePeerIdentity namedPeer = AssemblyPeer(
            Assembly("Peer"),
            type);
        IntegrationCandidatePeerIdentity policyPeer =
            PolicyTargetPeer("Peer.Lib", type);

        // Observed evidence requires a structured named peer, never a policy
        // target.
        Assert.Throws<ArgumentException>(() =>
            new IntegrationCandidateIdentity(
                Observed,
                IntegrationConceptCatalog.AI,
                typeSource,
                policyPeer));
        // An opportunity requires a policy-issued target, never a named peer.
        Assert.Throws<ArgumentException>(() =>
            new IntegrationCandidateIdentity(
                OpportunityRel,
                IntegrationConceptCatalog.AI,
                typeSource,
                namedPeer));
        // An opportunity requires a Type source, never a member source.
        Assert.Throws<ArgumentException>(() =>
            new IntegrationCandidateIdentity(
                OpportunityRel,
                IntegrationConceptCatalog.AI,
                memberSource,
                policyPeer));
    }

    // ---- Participant receipts -------------------------------------------

    [Fact]
    public void IntegrationCensus_ParticipantReceiptsExactlyCoverDeclaredParticipants()
    {
        IntegrationSourceParticipantIdentity a = Portable("a");
        IntegrationSourceParticipantIdentity b = Portable("b");
        var participants = new[] { a, b };

        // Exact cover succeeds.
        _ = Snapshot(participants);

        // Missing.
        Assert.Throws<ArgumentException>(() =>
            Snapshot(participants, sourceAttempts: [Available(a)]));
        // Duplicate.
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                sourceAttempts: [Available(a), Available(b), Available(a)]));
        // Extraneous.
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                sourceAttempts:
                [
                    Available(a),
                    Available(b),
                    Available(Portable("c")),
                ]));
    }

    [Fact]
    public void IntegrationCensus_RejectedOrFailedParticipantMakesCensusIncomplete()
    {
        IntegrationSourceParticipantIdentity a = Portable("a");
        IntegrationSourceParticipantIdentity b = Portable("b");
        var participants = new[] { a, b };

        IntegrationCensusSnapshot rejected = Snapshot(
            participants,
            sourceAttempts:
            [
                Available(a),
                new IntegrationSourceParticipantAttempt.Rejected(
                    b,
                    new ParticipantRejection()),
            ],
            producerAttempts: Producers(
                participants,
                Unavailable(b, Ecosystem),
                Unavailable(b, OpenTelemetry),
                Unavailable(b, Opportunity)));

        Assert.False(rejected.IsComplete);
        Assert.Empty(rejected.Candidates);

        IntegrationCensusSnapshot failed = Snapshot(
            participants,
            sourceAttempts:
            [
                Available(a),
                new IntegrationSourceParticipantAttempt.Failed(
                    b,
                    new ParticipantFailure()),
            ],
            producerAttempts: Producers(
                participants,
                Failed(b, Ecosystem),
                Failed(b, OpenTelemetry),
                Failed(b, Opportunity)));

        Assert.False(failed.IsComplete);
    }

    // ---- Producer receipts ----------------------------------------------

    [Fact]
    public void IntegrationCensus_ProducerReceiptsCoverParticipantByRetainedPolicyProduct()
    {
        IntegrationSourceParticipantIdentity a = Portable("a");
        IntegrationSourceParticipantIdentity b = Portable("b");
        var participants = new[] { a, b };

        IntegrationCensusSnapshot snapshot = Snapshot(participants);
        Assert.Equal(
            [Ecosystem, OpenTelemetry, Opportunity],
            snapshot.RequiredProducerPolicies);
        Assert.Equal(
            participants.Length * 3,
            snapshot.ProducerPolicyAttempts.Length);

        // Missing one address.
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: Producers(participants)
                    .Where(attempt => !ReferenceEquals(
                        attempt.Address.Participant,
                        b)
                        || !ReferenceEquals(attempt.Address.Policy, Opportunity))
                    .ToArray()));
        // Duplicate address.
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts:
                [
                    .. Producers(participants),
                    Completed(a, Ecosystem),
                ]));
        // Extraneous address (a participant not in the declared set).
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts:
                [
                    .. Producers(participants),
                    Completed(Portable("c"), Ecosystem),
                ]));
    }

    [Fact]
    public void IntegrationCensus_ProducerCompletedEvidenceRejectsMismatches()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        IntegrationSourceParticipantIdentity other = Portable("other");
        IntegrationProducerPolicyAttemptAddress address =
            new(participant, Ecosystem);

        // Wrong participant on the candidate.
        Assert.Throws<ArgumentException>(() =>
            new IntegrationProducerPolicyAttempt.Completed(
                address,
                [ObservedCandidate(other, IntegrationConceptCatalog.AI, DefaultPeer)]));
        // Wrong relationship (opportunity candidate under an observed policy).
        Assert.Throws<ArgumentException>(() =>
            new IntegrationProducerPolicyAttempt.Completed(
                address,
                [OpportunityCandidate(
                    participant,
                    IntegrationConceptCatalog.AI,
                    PolicyTargetPeer("Peer.Lib", TypeName("Peer", "Client")))]));
        // Wrong concept (OpenTelemetry is not an ecosystem-observed concept).
        Assert.Throws<ArgumentException>(() =>
            new IntegrationProducerPolicyAttempt.Completed(
                address,
                [ObservedCandidate(
                    participant,
                    IntegrationConceptCatalog.OpenTelemetry,
                    DefaultPeer)]));
    }

    [Fact]
    public void IntegrationCensus_UnavailableOrFailedProducerYieldsNoCandidatesAndIncompleteness()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };

        IntegrationCensusSnapshot unavailable = Snapshot(
            participants,
            producerAttempts: Producers(
                participants,
                Unavailable(participant, Ecosystem)));
        Assert.Empty(unavailable.Candidates);
        Assert.False(unavailable.IsComplete);

        IntegrationCensusSnapshot failed = Snapshot(
            participants,
            producerAttempts: Producers(
                participants,
                Failed(participant, Opportunity)));
        Assert.Empty(failed.Candidates);
        Assert.False(failed.IsComplete);
    }

    // ---- Candidate coalescing -------------------------------------------

    [Fact]
    public void IntegrationCensus_DuplicateEvidenceCoalescesRetainingProducerCorrespondence()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext context = new Context();

        IntegrationCensusSnapshot snapshot = Snapshot(
            participants,
            producerAttempts: Producers(
                participants,
                Completed(participant, Ecosystem, candidate, candidate)),
            contexts: [context],
            candidateAttempts: [ClassifiedOut(candidate, context)]);

        IntegrationCensusCandidate coalesced = Assert.Single(snapshot.Candidates);
        Assert.Equal(candidate, coalesced.Identity);
        IntegrationProducerPolicyAttemptAddress correspondence =
            Assert.Single(coalesced.ProducerAttempts);
        Assert.Same(participant, correspondence.Participant);
        Assert.Same(Ecosystem, correspondence.Policy);
    }

    // ---- Candidate attempt accounting -----------------------------------

    [Fact]
    public void IntegrationCensus_CandidateAttemptsCoverCoalescedCandidatesByContext()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext first = new Context();
        IContext second = new Context();

        // Two candidates? No: one candidate over two contexts => two attempts.
        List<IntegrationProducerPolicyAttempt> producers = Producers(
            participants,
            Completed(participant, Ecosystem, candidate));

        _ = Snapshot(
            participants,
            producerAttempts: producers,
            contexts: [first, second],
            candidateAttempts:
            [
                ClassifiedOut(candidate, first),
                ClassifiedOut(candidate, second),
            ]);

        // Missing one context attempt.
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: producers,
                contexts: [first, second],
                candidateAttempts: [ClassifiedOut(candidate, first)]));
        // Duplicate.
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: producers,
                contexts: [first, second],
                candidateAttempts:
                [
                    ClassifiedOut(candidate, first),
                    ClassifiedOut(candidate, second),
                    ClassifiedOut(candidate, first),
                ]));
        // Extraneous (context not declared).
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: producers,
                contexts: [first, second],
                candidateAttempts:
                [
                    ClassifiedOut(candidate, first),
                    ClassifiedOut(candidate, second),
                    ClassifiedOut(candidate, new Context()),
                ]));
    }

    [Fact]
    public void IntegrationCensus_EmptyHealthyUniverseIsCompleteAndSuccessful()
    {
        IntegrationCensusSnapshot snapshot = Snapshot([Portable()]);

        Assert.Empty(snapshot.Candidates);
        Assert.Empty(snapshot.CandidateAttempts);
        Assert.True(snapshot.IsComplete);
    }

    // ---- Disposition ----------------------------------------------------

    [Fact]
    public void IntegrationCensus_ClassifiedInRequiresSelectedTerminalPeer()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext context = new Context();
        IntegrationTypeIdentity terminal = PeerTerminal(candidate);
        List<IntegrationProducerPolicyAttempt> producers = Producers(
            participants,
            Completed(participant, Ecosystem, candidate));

        // In with the terminal peer selected succeeds.
        _ = Snapshot(
            participants,
            producerAttempts: producers,
            selectedTypes: [terminal],
            contexts: [context],
            candidateAttempts:
            [
                new IntegrationCandidateAttempt.Classified(
                    new IntegrationCandidateAttemptAddress(candidate, context),
                    new IntegrationCandidateDisposition.In(Resolved(terminal))),
            ]);

        // In without a selected terminal is rejected.
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: producers,
                selectedTypes: [],
                contexts: [context],
                candidateAttempts:
                [
                    new IntegrationCandidateAttempt.Classified(
                        new IntegrationCandidateAttemptAddress(candidate, context),
                        new IntegrationCandidateDisposition.In(Resolved(terminal))),
                ]));
    }

    [Fact]
    public void IntegrationCensus_ClassifiedOutRequiresUnselectedTerminalPeer()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext context = new Context();
        IntegrationTypeIdentity terminal = PeerTerminal(candidate);
        List<IntegrationProducerPolicyAttempt> producers = Producers(
            participants,
            Completed(participant, Ecosystem, candidate));

        IntegrationCensusSnapshot snapshot = Snapshot(
            participants,
            producerAttempts: producers,
            selectedTypes: [],
            contexts: [context],
            candidateAttempts: [ClassifiedOut(candidate, context)]);
        IntegrationCandidateAttempt.Classified classified =
            Assert.Single(snapshot.ClassifiedAttempts);
        var disposition = Assert.IsType<IntegrationCandidateDisposition.Out>(
            classified.Disposition);
        Assert.Equal(
            IntegrationCandidateOutReason.PeerOutsideUniverse,
            disposition.Reason);

        // Out with the terminal selected is rejected.
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: producers,
                selectedTypes: [terminal],
                contexts: [context],
                candidateAttempts: [ClassifiedOut(candidate, context)]));
    }

    [Fact]
    public void IntegrationCensus_ClassificationRequiresTerminalPeerMatchingCandidate()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext context = new Context();
        IntegrationTypeIdentity mismatched =
            new(participant, TypeName("Peer", "Other"));

        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: Producers(
                    participants,
                    Completed(participant, Ecosystem, candidate)),
                selectedTypes: [],
                contexts: [context],
                candidateAttempts:
                [
                    new IntegrationCandidateAttempt.Classified(
                        new IntegrationCandidateAttemptAddress(candidate, context),
                        new IntegrationCandidateDisposition.Out(
                            Resolved(mismatched))),
                ]));
    }

    [Fact]
    public void IntegrationCensus_ForwardedClassificationRetainsResolutionPath()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext context = new Context();
        IntegrationTypeIdentity facade = PeerTerminal(candidate);
        IntegrationTypeIdentity terminal =
            new(ParticipantForAssembly(Assembly("Implementation")), candidate.Peer.Type);
        var resolved = new IntegrationResolvedPeer([facade, terminal]);

        IntegrationCensusSnapshot snapshot = Snapshot(
            participants,
            producerAttempts: Producers(
                participants,
                Completed(participant, Ecosystem, candidate)),
            selectedTypes: [terminal],
            contexts: [context],
            candidateAttempts:
            [
                new IntegrationCandidateAttempt.Classified(
                    new IntegrationCandidateAttemptAddress(candidate, context),
                    new IntegrationCandidateDisposition.In(resolved)),
            ]);

        IntegrationCandidateAttempt.Classified classified =
            Assert.Single(snapshot.ClassifiedAttempts);
        Assert.Equal(
            [facade, terminal],
            classified.Disposition.Peer.ResolutionPath);
        Assert.Equal(terminal, classified.Disposition.Peer.Terminal);
    }

    [Fact]
    public void IntegrationCensus_ResolutionRejectsWrongAssemblyForwardingHopAndCycle()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext context = new Context();
        IntegrationTypeIdentity terminal = PeerTerminal(candidate);

        // An assembly-reference lookup cannot begin in the wrong assembly.
        IntegrationTypeIdentity wrongAssemblyStart = new(
            ParticipantForAssembly(Assembly("Wrong")),
            candidate.Peer.Type);
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: Producers(
                    participants,
                    Completed(participant, Ecosystem, candidate)),
                selectedTypes: [wrongAssemblyStart],
                contexts: [context],
                candidateAttempts:
                [
                    new IntegrationCandidateAttempt.Classified(
                        new IntegrationCandidateAttemptAddress(candidate, context),
                        new IntegrationCandidateDisposition.In(
                            Resolved(wrongAssemblyStart))),
                ]));

        // A forwarding hop must retain the exact candidate Type name.
        IntegrationTypeIdentity renamingHop = new(
            ParticipantForAssembly(Assembly("Forward")),
            TypeName("Peer", "Alias"));
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: Producers(
                    participants,
                    Completed(participant, Ecosystem, candidate)),
                selectedTypes: [terminal],
                contexts: [context],
                candidateAttempts:
                [
                    new IntegrationCandidateAttempt.Classified(
                        new IntegrationCandidateAttemptAddress(candidate, context),
                        new IntegrationCandidateDisposition.In(
                            new IntegrationResolvedPeer([renamingHop, terminal]))),
                ]));

        // A repeated identity in the resolution path is a forwarding cycle.
        Assert.Throws<ArgumentException>(() =>
            new IntegrationResolvedPeer([terminal, terminal]));

        IntegrationCandidateIdentity moduleCandidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            NamedPeer(
                new MetadataTypeReferenceScope.ModuleReference("peer.netmodule"),
                candidate.Peer.Type));
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: Producers(
                    participants,
                    Completed(participant, Ecosystem, moduleCandidate)),
                contexts: [context],
                candidateAttempts:
                [ClassifiedOut(moduleCandidate, context)]));
    }

    [Fact]
    public void IntegrationCensus_FailedCandidateHasNoDispositionAndIsIncomplete()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext context = new Context();

        IntegrationCensusSnapshot snapshot = Snapshot(
            participants,
            producerAttempts: Producers(
                participants,
                Completed(participant, Ecosystem, candidate)),
            contexts: [context],
            candidateAttempts:
            [
                new IntegrationCandidateAttempt.Failed(
                    new IntegrationCandidateAttemptAddress(candidate, context),
                    new CandidateFailure()),
            ]);

        Assert.Single(snapshot.Candidates);
        Assert.Empty(snapshot.ClassifiedAttempts);
        Assert.Single(snapshot.FailedCandidateAttempts);
        Assert.False(snapshot.IsComplete);
    }

    // ---- Universe variance ----------------------------------------------

    [Fact]
    public void IntegrationCensus_SameCandidateAcrossContextsProducesDistinctAttempts()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext first = new Context();
        IContext second = new Context();

        IntegrationCensusSnapshot snapshot = Snapshot(
            participants,
            producerAttempts: Producers(
                participants,
                Completed(participant, Ecosystem, candidate)),
            selectedTypes: [],
            contexts: [first, second],
            candidateAttempts:
            [
                ClassifiedOut(candidate, first),
                ClassifiedOut(candidate, second),
            ]);

        Assert.Single(snapshot.Candidates);
        Assert.Equal(2, snapshot.CandidateAttempts.Length);
        Assert.All(
            snapshot.CandidateAttempts,
            attempt => Assert.Equal(candidate, attempt.Address.Candidate));
        Assert.NotEqual(
            snapshot.CandidateAttempts[0].Address,
            snapshot.CandidateAttempts[1].Address);
    }

    [Fact]
    public void IntegrationCensus_AddingOrRemovingSelectedPeerPreservesIdentityWhileFlippingDisposition()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext context = new Context();
        IntegrationTypeIdentity terminal = PeerTerminal(candidate);
        List<IntegrationProducerPolicyAttempt> producers = Producers(
            participants,
            Completed(participant, Ecosystem, candidate));

        IntegrationCensusSnapshot withPeer = Snapshot(
            participants,
            producerAttempts: producers,
            selectedTypes: [terminal],
            contexts: [context],
            candidateAttempts:
            [
                new IntegrationCandidateAttempt.Classified(
                    new IntegrationCandidateAttemptAddress(candidate, context),
                    new IntegrationCandidateDisposition.In(Resolved(terminal))),
            ]);
        IntegrationCensusSnapshot withoutPeer = Snapshot(
            participants,
            producerAttempts: producers,
            selectedTypes: [],
            contexts: [context],
            candidateAttempts: [ClassifiedOut(candidate, context)]);

        Assert.Equal(
            withPeer.Candidates.Single().Identity,
            withoutPeer.Candidates.Single().Identity);
        Assert.IsType<IntegrationCandidateDisposition.In>(
            withPeer.ClassifiedAttempts.Single().Disposition);
        Assert.IsType<IntegrationCandidateDisposition.Out>(
            withoutPeer.ClassifiedAttempts.Single().Disposition);
    }

    [Fact]
    public void IntegrationCensus_RemovingSelectedSourceMembershipRejectsStaleCandidate()
    {
        // Stale evidence cannot retain a candidate: a completed producer
        // receipt whose source Type is not in the selected universe is
        // rejected outright. Everything except the selected source membership
        // is held constant.
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext context = new Context();
        List<IntegrationProducerPolicyAttempt> producers = Producers(
            participants,
            Completed(participant, Ecosystem, candidate));
        IntegrationCandidateAttempt[] attempts =
            [ClassifiedOut(candidate, context)];

        // The selected source Type is present (auto-admitted): construction
        // succeeds and the candidate survives.
        IntegrationCensusSnapshot admitted = Snapshot(
            participants,
            producerAttempts: producers,
            contexts: [context],
            candidateAttempts: attempts);
        Assert.Equal(candidate, Assert.Single(admitted.Candidates).Identity);

        // Removing only the selected source membership rejects the snapshot.
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: producers,
                selectedTypes: [],
                admitSources: false,
                contexts: [context],
                candidateAttempts: attempts));
    }

    // ---- Suppression ----------------------------------------------------

    [Fact]
    public void IntegrationCensus_SuppressionRequiresSameContextClassifiedObservedOfSameConcept()
    {
        SuppressionFixture fixture = new(this);

        IntegrationCensusSnapshot snapshot = fixture.Snapshot(
            fixture.Suppressed(fixture.ObservedAttemptAddress));

        IntegrationCandidateAttempt.Suppressed suppressed =
            Assert.Single(snapshot.SuppressedAttempts);
        Assert.Equal(
            IntegrationCandidateSuppressionReason.FulfilledByObservation,
            suppressed.Reason);
        Assert.Equal(fixture.ObservedAttemptAddress, suppressed.FulfilledBy);
        Assert.True(snapshot.IsComplete);
    }

    [Fact]
    public void IntegrationCensus_SuppressionRejectsSelfCrossContextAndMissingFulfiller()
    {
        SuppressionFixture fixture = new(this);

        // Self / cyclic.
        Assert.Throws<ArgumentException>(() =>
            fixture.Snapshot(
                new IntegrationCandidateAttempt.Suppressed(
                    fixture.OpportunityAttemptAddress,
                    fixture.OpportunityAttemptAddress,
                    fixture.Fulfillment)));
        // Cross-context fulfiller (a context the suppressed attempt is not in).
        Assert.Throws<ArgumentException>(() =>
            fixture.Snapshot(
                new IntegrationCandidateAttempt.Suppressed(
                    fixture.OpportunityAttemptAddress,
                    new IntegrationCandidateAttemptAddress(
                        fixture.ObservedCandidate,
                        fixture.OtherContext),
                    fixture.Fulfillment)));
        // Fulfiller that is not among the candidate attempts.
        Assert.Throws<ArgumentException>(() =>
            fixture.Snapshot(
                new IntegrationCandidateAttempt.Suppressed(
                    fixture.OpportunityAttemptAddress,
                    new IntegrationCandidateAttemptAddress(
                        ObservedCandidate(
                            Portable(),
                            IntegrationConceptCatalog.AI,
                            AssemblyPeer(Assembly("Ghost"), TypeName("Ghost", "T"))),
                        fixture.Context),
                    fixture.Fulfillment)));
    }

    [Fact]
    public void IntegrationCensus_SuppressionRejectsOpportunityFulfillingOpportunity()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IContext context = new Context();

        IntegrationCandidateIdentity first = OpportunityCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            PolicyTargetPeer("Peer.Lib", TypeName("Peer", "Client")));
        IntegrationCandidateIdentity second = OpportunityCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            PolicyTargetPeer("Peer.Lib", TypeName("Peer", "Server")));
        IntegrationCandidateAttemptAddress secondAddress = new(second, context);

        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: Producers(
                    participants,
                    Completed(participant, Opportunity, first, second)),
                selectedTypes: [],
                contexts: [context],
                candidateAttempts:
                [
                    ClassifiedOut(second, context),
                    new IntegrationCandidateAttempt.Suppressed(
                        new IntegrationCandidateAttemptAddress(first, context),
                        secondAddress,
                        new IntegrationOpportunityFulfillment(
                            SourceType(first),
                            Resolved(PeerTerminal(second)))),
                ]));
    }

    [Fact]
    public void IntegrationCensus_SuppressionRejectsWrongConceptFulfiller()
    {
        // An opportunity for one concept cannot be fulfilled by an observation
        // of a different concept.
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IContext context = new Context();

        IntegrationCandidateIdentity opportunity = OpportunityCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            PolicyTargetPeer("Peer.Lib", TypeName("Peer", "Client")));
        IntegrationCandidateIdentity observed = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.Aspire,
            AssemblyPeer(Assembly("Peer"), TypeName("Peer", "Adapter")));
        IntegrationTypeIdentity observedTerminal = PeerTerminal(observed);

        IntegrationCandidateAttemptAddress observedAddress =
            new(observed, context);

        // The observation classifies validly; the sole defect is the concept
        // mismatch, so the rejection is meaningful.
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: Producers(
                    participants,
                    Completed(participant, Opportunity, opportunity),
                    Completed(participant, Ecosystem, observed)),
                selectedTypes: [observedTerminal],
                contexts: [context],
                candidateAttempts:
                [
                    new IntegrationCandidateAttempt.Classified(
                        observedAddress,
                        new IntegrationCandidateDisposition.In(
                            Resolved(observedTerminal))),
                    new IntegrationCandidateAttempt.Suppressed(
                        new IntegrationCandidateAttemptAddress(
                            opportunity,
                            context),
                        observedAddress,
                        new IntegrationOpportunityFulfillment(
                            SourceType(opportunity),
                            Resolved(observedTerminal))),
                ]));
    }

    [Fact]
    public void IntegrationCensus_SuppressionRejectsUnclassifiedFulfiller()
    {
        SuppressionFixture fixture = new(this);

        Assert.Throws<ArgumentException>(() =>
            fixture.Snapshot(
                fixture.Suppressed(fixture.ObservedAttemptAddress),
                observedAttempt: new IntegrationCandidateAttempt.Failed(
                    fixture.ObservedAttemptAddress,
                    new CandidateFailure())));
    }

    [Fact]
    public void IntegrationCensus_SuppressionRejectsWrongProofSourceOrTarget()
    {
        SuppressionFixture fixture = new(this);
        IntegrationSourceParticipantIdentity participant = Portable();

        // Proof source must equal the opportunity source, not some other Type.
        IntegrationOpportunityFulfillment wrongSource =
            new IntegrationOpportunityFulfillment(
                new IntegrationTypeIdentity(participant, TypeName("Src", "Other")),
                Resolved(fixture.ObservedTerminal));
        Assert.Throws<ArgumentException>(() =>
            fixture.Snapshot(
                fixture.Suppressed(
                    fixture.ObservedAttemptAddress,
                    wrongSource)));

        // Proof target must equal the observation's resolved terminal.
        IntegrationOpportunityFulfillment wrongTarget =
            new IntegrationOpportunityFulfillment(
                SourceType(fixture.OpportunityCandidateId),
                Resolved(
                    new IntegrationTypeIdentity(
                        ParticipantForAssembly(Assembly("Peer.Lib")),
                        TypeName("Peer", "Other"))));
        Assert.Throws<ArgumentException>(() =>
            fixture.Snapshot(
                fixture.Suppressed(
                    fixture.ObservedAttemptAddress,
                    wrongTarget)));
    }

    // ---- Snapshot compatibility & revision ------------------------------

    [Fact]
    public void IntegrationCensus_SnapshotCompatibilityIgnoresProjectionButRequiresSharedInputs()
    {
        AnalysisReportSurface surface = Surface();
        AnalysisUniverseDescription universe = FullUniverse();
        AnalysisRequestPlan rows = Plan(surface, universe, IntegrationAnalysisCatalog.Rows);

        IntegrationCensusSnapshot snapshot = Snapshot([Portable()], plan: rows);

        Assert.True(snapshot.IsCompatibleWith(rows));
        Assert.True(
            snapshot.IsCompatibleWith(
                Plan(surface, universe, IntegrationAnalysisCatalog.Matrix)));
        Assert.True(
            snapshot.IsCompatibleWith(
                Plan(surface, universe, IntegrationAnalysisCatalog.Graph)));

        // A different universe object breaks compatibility.
        Assert.False(
            snapshot.IsCompatibleWith(
                Plan(surface, FullUniverse(), IntegrationAnalysisCatalog.Rows)));
        // A different report surface breaks compatibility.
        Assert.False(
            snapshot.IsCompatibleWith(
                Plan(Surface(), universe, IntegrationAnalysisCatalog.Rows)));

        Assert.Same(
            IntegrationConceptCatalog.Revision,
            snapshot.CatalogRevision);
    }

    // =====================================================================
    // Fixture / helper layer
    // =====================================================================

    static readonly Version Ver = new(1, 0, 0, 0);
    const string Digest =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    static InspectionGraphRelationshipDescriptor Observed =>
        InspectionGraphIntegrationsCatalog.IntegrationObserved;
    static InspectionGraphRelationshipDescriptor OpportunityRel =>
        InspectionGraphIntegrationsCatalog.IntegrationOpportunity;

    static IntegrationProducerPolicyBinding Ecosystem =>
        IntegrationAnalysisCatalog.EcosystemObserved;
    static IntegrationProducerPolicyBinding OpenTelemetry =>
        IntegrationAnalysisCatalog.OpenTelemetryObserved;
    static IntegrationProducerPolicyBinding Opportunity =>
        IntegrationAnalysisCatalog.Opportunity;

    static IntegrationCandidatePeerIdentity DefaultPeer =>
        AssemblyPeer(Assembly("Peer"), TypeName("Peer", "Client"));

    static AssemblyReferenceIdentity Assembly(string name) =>
        new(name, Ver, null, null);

    static MetadataTypeDefinitionName TypeName(string @namespace, string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(@namespace, [name])).Name;

    static MemberAnchor Anchor(string typeFullName) =>
        new(
            $"{typeFullName}.Member()",
            $"{typeFullName}.Member",
            "0000000000",
            typeFullName,
            "Member");

    static IntegrationCandidateSourceElement TypeElement(
        string @namespace = "Src",
        string name = "Type") =>
        new IntegrationCandidateSourceElement.Type(TypeName(@namespace, name));

    static IntegrationCandidatePeerIdentity NamedPeer(
        MetadataTypeReferenceScope scope,
        MetadataTypeDefinitionName type) =>
        new IntegrationCandidatePeerIdentity.NamedType(
            new MetadataNamedTypeReference(scope, type));

    static IntegrationCandidatePeerIdentity AssemblyPeer(
        AssemblyReferenceIdentity assembly,
        MetadataTypeDefinitionName type) =>
        NamedPeer(
            new MetadataTypeReferenceScope.AssemblyReference(assembly),
            type);

    static IntegrationCandidatePeerIdentity PolicyTargetPeer(
        string assemblyName,
        MetadataTypeDefinitionName type) =>
        new IntegrationCandidatePeerIdentity.PolicyTarget(
            new IntegrationOpportunityTarget(assemblyName, type));

    static IntegrationSourceParticipantIdentity Portable(string package = "src") =>
        IntegrationSourceParticipantIdentity.Portable(
            new RealizedMemberCoordinate.Package(
                $"contoso.{package}", "1.0.0", "fixture", "net11.0", null),
            Assembly("Src"));

    static AssemblyAcquisitionRegistration Registration() =>
        ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity("Src", Ver, null, null),
            path: null,
            () => new MemoryStream(),
            AssemblyResolutionProvenance.Local("test")).Registration;

    static IntegrationCandidateIdentity ObservedCandidate(
        IntegrationSourceParticipantIdentity participant,
        IntegrationConceptDescriptor concept,
        IntegrationCandidatePeerIdentity peer) =>
        new(
            Observed,
            concept,
            new IntegrationCandidateSourceIdentity(participant, TypeElement()),
            peer);

    static IntegrationCandidateIdentity OpportunityCandidate(
        IntegrationSourceParticipantIdentity participant,
        IntegrationConceptDescriptor concept,
        IntegrationCandidatePeerIdentity peer) =>
        new(
            OpportunityRel,
            concept,
            new IntegrationCandidateSourceIdentity(participant, TypeElement()),
            peer);

    static IntegrationResolvedPeer Resolved(IntegrationTypeIdentity terminal) =>
        new([terminal]);

    static IntegrationSourceParticipantAttempt Available(
        IntegrationSourceParticipantIdentity participant) =>
        new IntegrationSourceParticipantAttempt.Available(participant);

    static IntegrationProducerPolicyAttempt Completed(
        IntegrationSourceParticipantIdentity participant,
        IntegrationProducerPolicyBinding policy,
        params IntegrationCandidateIdentity[] candidates) =>
        new IntegrationProducerPolicyAttempt.Completed(
            new IntegrationProducerPolicyAttemptAddress(participant, policy),
            candidates);

    static IntegrationProducerPolicyAttempt Unavailable(
        IntegrationSourceParticipantIdentity participant,
        IntegrationProducerPolicyBinding policy) =>
        new IntegrationProducerPolicyAttempt.Unavailable(
            new IntegrationProducerPolicyAttemptAddress(participant, policy),
            new PolicyUnavailable());

    static IntegrationProducerPolicyAttempt Failed(
        IntegrationSourceParticipantIdentity participant,
        IntegrationProducerPolicyBinding policy) =>
        new IntegrationProducerPolicyAttempt.Failed(
            new IntegrationProducerPolicyAttemptAddress(participant, policy),
            new PolicyFailure());

    static IntegrationCandidateAttempt ClassifiedOut(
        IntegrationCandidateIdentity candidate,
        IIntegrationBindingContextIdentity context) =>
        new IntegrationCandidateAttempt.Classified(
            new IntegrationCandidateAttemptAddress(candidate, context),
            new IntegrationCandidateDisposition.Out(
                Resolved(PeerTerminal(candidate))));

    // The exact selected-universe Type identity backing one candidate's source
    // element, admitted so completed producer evidence stays live.
    static IntegrationTypeIdentity SourceType(
        IntegrationCandidateIdentity candidate) =>
        new(candidate.Source.Participant, candidate.Source.SourceType);

    // A plausible peer participant consistent with the candidate peer scope: an
    // assembly-reference or policy target binds in the referenced assembly,
    // while current/module/intrinsic scopes bind in the source participant.
    static IntegrationSourceParticipantIdentity PeerParticipant(
        IntegrationCandidateIdentity candidate) =>
        candidate.Peer switch
        {
            IntegrationCandidatePeerIdentity.NamedType named =>
                named.Reference.Scope switch
                {
                    MetadataTypeReferenceScope.AssemblyReference assembly =>
                        ParticipantForAssembly(assembly.Assembly),
                    _ => candidate.Source.Participant,
                },
            IntegrationCandidatePeerIdentity.PolicyTarget target =>
                ParticipantForAssembly(Assembly(target.Target.AssemblyName)),
            _ => throw new InvalidOperationException(
                "Unknown Integration candidate peer identity."),
        };

    static IntegrationTypeIdentity PeerTerminal(
        IntegrationCandidateIdentity candidate) =>
        new(PeerParticipant(candidate), candidate.Peer.Type);

    static IntegrationSourceParticipantIdentity ParticipantForAssembly(
        AssemblyReferenceIdentity assembly) =>
        IntegrationSourceParticipantIdentity.Portable(
            new RealizedMemberCoordinate.Package(
                $"contoso.peer.{assembly.Name.ToLowerInvariant()}",
                "1.0.0",
                "fixture",
                "net11.0",
                null),
            assembly);

    static List<IntegrationProducerPolicyAttempt> Producers(
        IReadOnlyList<IntegrationSourceParticipantIdentity> participants,
        params IntegrationProducerPolicyAttempt[] overrides)
    {
        var list = new List<IntegrationProducerPolicyAttempt>();
        foreach (IntegrationSourceParticipantIdentity participant in participants)
        {
            foreach (IntegrationProducerPolicyBinding policy in
                new[] { Ecosystem, OpenTelemetry, Opportunity })
            {
                var address =
                    new IntegrationProducerPolicyAttemptAddress(participant, policy);
                IntegrationProducerPolicyAttempt? overridden =
                    overrides.FirstOrDefault(
                        attempt => attempt.Address.Equals(address));
                list.Add(
                    overridden
                        ?? new IntegrationProducerPolicyAttempt.Completed(
                            address,
                            []));
            }
        }

        return list;
    }

    static IntegrationCensusSnapshot Snapshot(
        IReadOnlyList<IntegrationSourceParticipantIdentity> participants,
        IEnumerable<IntegrationSourceParticipantAttempt>? sourceAttempts = null,
        IEnumerable<IntegrationProducerPolicyAttempt>? producerAttempts = null,
        IEnumerable<IntegrationTypeIdentity>? selectedTypes = null,
        IEnumerable<IIntegrationBindingContextIdentity>? contexts = null,
        IEnumerable<IntegrationCandidateAttempt>? candidateAttempts = null,
        AnalysisRequestPlan? plan = null,
        bool admitSources = true)
    {
        List<IntegrationProducerPolicyAttempt> producers =
            [.. producerAttempts ?? Producers(participants)];
        var selected = new List<IntegrationTypeIdentity>(selectedTypes ?? []);
        if (admitSources)
        {
            foreach (IntegrationProducerPolicyAttempt.Completed completed in
                producers.OfType<IntegrationProducerPolicyAttempt.Completed>())
            {
                foreach (IntegrationCandidateIdentity candidate in
                    completed.Candidates)
                {
                    IntegrationTypeIdentity source = SourceType(candidate);
                    if (!selected.Any(type => type.Equals(source)))
                        selected.Add(source);
                }
            }
        }

        return new(
            plan ?? Plan(),
            participants,
            selected,
            contexts ?? [new Context()],
            sourceAttempts ?? participants.Select(Available),
            producers,
            candidateAttempts ?? []);
    }

    // ---- Request plan helpers (mirroring IntegrationAnalysisCatalogTests) ----

    static AnalysisRequestPlan Plan(
        AnalysisReportSurface? surface = null,
        AnalysisUniverseDescription? universe = null,
        AnalysisProjectionDescriptor? projection = null)
    {
        var accepted = Assert.IsType<AnalysisRequestPlanResult.Accepted>(
            IntegrationAnalysisCatalog.Capabilities.Plan(
                new AnalysisRequest(
                    IntegrationAnalysisCatalog.Analysis,
                    surface ?? Surface(),
                    universe ?? FullUniverse(),
                    AnalysisQuestionMode.Census,
                    projection ?? IntegrationAnalysisCatalog.Rows),
                Environment()));
        return accepted.Plan;
    }

    static AnalysisReportSurface Surface() =>
        new(
            AnalysisReportSurfaceKind.Workspace,
            new SurfaceIdentity(),
            [
                new AnalysisTargetBinding(
                    IntegrationAnalysisCatalog.WorkspaceDomain,
                    new TargetIdentity()),
            ]);

    static AnalysisUniverseDescription FullUniverse() =>
        new(
            new UniverseIdentity(),
            new UniverseBoundary(),
            new UniverseBoundary(),
            isFinite: true,
            IntegrationAnalysisCatalog.UniverseRequirements.Select(
                requirement => requirement.Capability),
            new UniverseCompleteness());

    static AnalysisPlanningEnvironment Environment() =>
        new(
            new InspectionQueryRegistry<object>()
                .Add(
                    AssemblyContextIntegrationsQuery.Definition,
                    _ => new AssemblyContextIntegrationsResult([]))
                .Add(
                    AssemblyContextIntegrationOpportunitiesQuery.Definition,
                    (_, _) => new AssemblyContextIntegrationOpportunitiesResult([]),
                    AssemblyContextIntegrationsQuery.Definition)
                .Compile(),
            IntegrationAnalysisCatalog.ProducerPolicies.Select(
                policy => policy.ProducerPrerequisite));

    interface IContext : IIntegrationBindingContextIdentity;
    sealed class Context : IContext;
    sealed class SurfaceIdentity : IAnalysisReportSurfaceIdentity;
    sealed class TargetIdentity : IAnalysisTargetIdentity;
    sealed class UniverseIdentity : IAnalysisUniverseIdentity;
    sealed class UniverseBoundary : IAnalysisUniverseBoundary;
    sealed class UniverseCompleteness : IAnalysisUniverseCompleteness;
    sealed class ParticipantRejection : IIntegrationSourceParticipantRejection;
    sealed class ParticipantFailure : IIntegrationSourceParticipantFailure;
    sealed class PolicyUnavailable : IIntegrationProducerPolicyUnavailable;
    sealed class PolicyFailure : IIntegrationProducerPolicyFailure;
    sealed class CandidateFailure : IIntegrationCandidateFailure;

    // A same-context observed/opportunity pair sharing one concept and one
    // exact target Type/assembly, so the opportunity can legitimately be
    // fulfilled by the observation. The observation names the opportunity's
    // policy target Type in that target's assembly, and its resolved terminal
    // is the fulfillment's proof target.
    sealed class SuppressionFixture
    {
        readonly IntegrationSourceParticipantIdentity _participant;
        readonly IntegrationTypeIdentity _observedTerminal;

        public SuppressionFixture(IntegrationCensusTests _)
        {
            _participant = Portable();
            Context = new Context();
            OtherContext = new Context();
            OpportunityCandidateId = OpportunityCandidate(
                _participant,
                IntegrationConceptCatalog.AI,
                PolicyTargetPeer("Peer.Lib", TypeName("Peer", "Client")));
            ObservedCandidate = IntegrationCensusTests.ObservedCandidate(
                _participant,
                IntegrationConceptCatalog.AI,
                AssemblyPeer(Assembly("Peer.Lib"), TypeName("Peer", "Client")));
            _observedTerminal = PeerTerminal(ObservedCandidate);
            Fulfillment = new IntegrationOpportunityFulfillment(
                SourceType(OpportunityCandidateId),
                Resolved(_observedTerminal));
        }

        public IContext Context { get; }
        public IContext OtherContext { get; }
        public IntegrationCandidateIdentity OpportunityCandidateId { get; }
        public IntegrationCandidateIdentity ObservedCandidate { get; }
        public IntegrationTypeIdentity ObservedTerminal => _observedTerminal;
        public IntegrationOpportunityFulfillment Fulfillment { get; }

        public IntegrationCandidateAttemptAddress OpportunityAttemptAddress =>
            new(OpportunityCandidateId, Context);
        public IntegrationCandidateAttemptAddress ObservedAttemptAddress =>
            new(ObservedCandidate, Context);

        public IntegrationCandidateAttempt Suppressed(
            IntegrationCandidateAttemptAddress fulfilledBy,
            IntegrationOpportunityFulfillment? fulfillment = null) =>
            new IntegrationCandidateAttempt.Suppressed(
                OpportunityAttemptAddress,
                fulfilledBy,
                fulfillment ?? Fulfillment);

        public IntegrationCensusSnapshot Snapshot(
            IntegrationCandidateAttempt opportunityAttempt,
            IntegrationCandidateAttempt? observedAttempt = null)
        {
            var participants = new[] { _participant };
            return IntegrationCensusTests.Snapshot(
                participants,
                producerAttempts: Producers(
                    participants,
                    Completed(_participant, Opportunity, OpportunityCandidateId),
                    Completed(_participant, Ecosystem, ObservedCandidate)),
                selectedTypes: [_observedTerminal],
                contexts: [Context],
                candidateAttempts:
                [
                    observedAttempt
                        ?? new IntegrationCandidateAttempt.Classified(
                            ObservedAttemptAddress,
                            new IntegrationCandidateDisposition.In(
                                Resolved(_observedTerminal))),
                    opportunityAttempt,
                ]);
        }
    }
}
