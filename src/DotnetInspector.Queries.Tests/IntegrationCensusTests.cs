using System.Collections.Immutable;
using System.Reflection;

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

    [Fact]
    public void IntegrationCensus_CanonicalizesShuffledReceiptProducts()
    {
        IntegrationSourceParticipantIdentity first = Portable("first");
        IntegrationSourceParticipantIdentity second = Portable("second");
        var participants = new[] { first, second };
        IntegrationCandidateIdentity discoveredFirst = ObservedCandidate(
            first,
            IntegrationConceptCatalog.AI,
            AssemblyPeer(Assembly("Peer"), TypeName("Peer", "Zed")));
        IntegrationCandidateIdentity discoveredSecond = ObservedCandidate(
            first,
            IntegrationConceptCatalog.AI,
            AssemblyPeer(Assembly("Peer"), TypeName("Peer", "Alpha")));
        IntegrationCandidateIdentity finalCandidate = OpportunityCandidate(
            second,
            IntegrationConceptCatalog.AI,
            PolicyTargetPeer("Peer", TypeName("Peer", "Client")));
        IContext firstContext = new Context();
        IContext secondContext = new Context();
        List<IntegrationProducerPolicyAttempt> producers = Producers(
            participants,
            Completed(
                first,
                Ecosystem,
                discoveredFirst,
                discoveredSecond),
            Completed(second, Opportunity, finalCandidate));
        IntegrationCandidateAttempt[] candidateAttempts =
        [
            ClassifiedOut(finalCandidate, secondContext),
            ClassifiedOut(discoveredSecond, secondContext),
            ClassifiedOut(discoveredFirst, secondContext),
            ClassifiedOut(finalCandidate, firstContext),
            ClassifiedOut(discoveredSecond, firstContext),
            ClassifiedOut(discoveredFirst, firstContext),
        ];

        IntegrationCensusSnapshot snapshot = Snapshot(
            participants,
            sourceAttempts: participants.Reverse().Select(Available),
            producerAttempts: producers.AsEnumerable().Reverse(),
            contexts: [firstContext, secondContext],
            candidateAttempts: candidateAttempts);

        Assert.Equal(
            participants,
            snapshot.SourceAttempts.Select(attempt => attempt.Participant));
        Assert.Equal(
            participants.SelectMany(participant =>
                new[] { Ecosystem, OpenTelemetry, Opportunity }.Select(
                    policy =>
                        new IntegrationProducerPolicyAttemptAddress(
                            participant,
                            policy))),
            snapshot.ProducerPolicyAttempts.Select(
                attempt => attempt.Address));
        Assert.Equal(
            [discoveredFirst, discoveredSecond, finalCandidate],
            snapshot.Candidates.Select(candidate => candidate.Identity));
        Assert.Equal(
            [
                new IntegrationCandidateAttemptAddress(
                    discoveredFirst,
                    firstContext),
                new IntegrationCandidateAttemptAddress(
                    discoveredFirst,
                    secondContext),
                new IntegrationCandidateAttemptAddress(
                    discoveredSecond,
                    firstContext),
                new IntegrationCandidateAttemptAddress(
                    discoveredSecond,
                    secondContext),
                new IntegrationCandidateAttemptAddress(
                    finalCandidate,
                    firstContext),
                new IntegrationCandidateAttemptAddress(
                    finalCandidate,
                    secondContext),
            ],
            snapshot.CandidateAttempts.Select(attempt => attempt.Address));
    }

    // ---- Candidate attempt accounting -----------------------------------

    [Fact]
    public void IntegrationCensus_ContextIncidenceExactlyCoversSourceParticipants()
    {
        IntegrationSourceParticipantIdentity first = Portable("first");
        IntegrationSourceParticipantIdentity second = Portable("second");
        IntegrationSourceParticipantIdentity foreign = Portable("foreign");
        var participants = new[] { first, second };
        IContext context = new Context();

        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                contexts: [context],
                contextIncidence: [Incidence(first, context)]));
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                contexts: [context],
                contextIncidence:
                [
                    Incidence(first, context),
                    Incidence(second, context),
                    Incidence(foreign, context),
                ]));
    }

    [Fact]
    public void IntegrationCensus_ContextIncidenceRejectsDuplicateOrForeignContexts()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        IContext first = new Context();
        IContext second = new Context();
        IContext foreign = new Context();

        Assert.Throws<ArgumentException>(() =>
            new IntegrationBindingContextAccess(
                [first, first],
                [Incidence(participant, first)]));
        Assert.Throws<ArgumentException>(() =>
            Incidence(participant, first, first));
        Assert.Throws<ArgumentException>(() =>
            new IntegrationBindingContextAccess(
                [first, second],
                [Incidence(participant, foreign)]));
        Assert.Throws<ArgumentException>(() =>
            new IntegrationBindingContextAccess(
                [first, second],
                [
                    Incidence(participant, first),
                    Incidence(participant, second),
                ]));

        IntegrationBindingContextAccess access = new(
            [first, second],
            [Incidence(participant, second, first)]);
        IntegrationSourceBindingContextIncidence incidence =
            Assert.Single(access.SourceIncidence);
        Assert.Equal([first, second], incidence.BindingContexts);
        Assert.Same(first, incidence.BindingContexts[0]);
        Assert.Same(second, incidence.BindingContexts[1]);
    }

    [Fact]
    public void IntegrationCensus_CandidateAttemptsFollowOwnerIssuedContextIncidence()
    {
        IntegrationSourceParticipantIdentity firstParticipant =
            Portable("first");
        IntegrationSourceParticipantIdentity secondParticipant =
            Portable("second");
        var participants = new[] { firstParticipant, secondParticipant };
        IntegrationCandidateIdentity firstCandidate = ObservedCandidate(
            firstParticipant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IntegrationCandidateIdentity secondCandidate = ObservedCandidate(
            secondParticipant,
            IntegrationConceptCatalog.AI,
            AssemblyPeer(
                Assembly("Second.Peer"),
                TypeName("Peer", "SecondClient")));
        IContext first = new Context();
        IContext second = new Context();

        List<IntegrationProducerPolicyAttempt> producers = Producers(
            participants,
            Completed(firstParticipant, Ecosystem, firstCandidate),
            Completed(secondParticipant, Ecosystem, secondCandidate));
        IntegrationSourceBindingContextIncidence[] incidence =
        [
            Incidence(firstParticipant, second, first),
            Incidence(secondParticipant, first),
        ];

        IntegrationCensusSnapshot snapshot = Snapshot(
            participants,
            producerAttempts: producers,
            contexts: [first, second],
            contextIncidence: incidence,
            candidateAttempts:
            [
                ClassifiedOut(secondCandidate, first),
                ClassifiedOut(firstCandidate, second),
                ClassifiedOut(firstCandidate, first),
            ]);
        Assert.Equal(
            [
                new IntegrationCandidateAttemptAddress(firstCandidate, first),
                new IntegrationCandidateAttemptAddress(firstCandidate, second),
                new IntegrationCandidateAttemptAddress(secondCandidate, first),
            ],
            snapshot.CandidateAttempts.Select(attempt => attempt.Address));
        Assert.Equal(
            participants,
            snapshot.SourceContextIncidence.Select(
                entry => entry.Participant));
        Assert.Same(
            firstParticipant,
            snapshot.SourceContextIncidence[0].Participant);
        Assert.Equal(
            [first, second],
            snapshot.SourceContextIncidence[0].BindingContexts);

        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: producers,
                contexts: [first, second],
                contextIncidence: incidence,
                candidateAttempts:
                [
                    ClassifiedOut(firstCandidate, first),
                    ClassifiedOut(secondCandidate, first),
                ]));
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: producers,
                contexts: [first, second],
                contextIncidence: incidence,
                candidateAttempts:
                [
                    ClassifiedOut(firstCandidate, first),
                    ClassifiedOut(firstCandidate, second),
                    ClassifiedOut(secondCandidate, first),
                    ClassifiedOut(secondCandidate, first),
                ]));
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: producers,
                contexts: [first, second],
                contextIncidence: incidence,
                candidateAttempts:
                [
                    ClassifiedOut(firstCandidate, first),
                    ClassifiedOut(firstCandidate, second),
                    ClassifiedOut(secondCandidate, first),
                    ClassifiedOut(secondCandidate, second),
                ]));
        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: producers,
                contexts: [first, second],
                contextIncidence: incidence,
                candidateAttempts:
                [
                    ClassifiedOut(firstCandidate, first),
                    ClassifiedOut(firstCandidate, second),
                    ClassifiedOut(secondCandidate, first),
                    ClassifiedOut(secondCandidate, new Context()),
                ]));
    }

    [Fact]
    public void IntegrationCensus_SemanticContextIncidenceUsesHashBackedAddressing()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity observed = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            AssemblyPeer(Assembly("Peer"), TypeName("Peer", "Client")),
            TypeElement("Adapters", "ClientAdapter"));
        IntegrationCandidateIdentity opportunity = OpportunityCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            PolicyTargetPeer("Peer", TypeName("Peer", "Client")));
        var fulfillmentSourceLookup =
            Assert.IsType<IntegrationCandidatePeerIdentity.NamedType>(
                NamedPeer(
                    new MetadataTypeReferenceScope.CurrentAssembly(),
                    opportunity.Source.SourceType));
        IntegrationCandidateIdentity observedEquivalent =
            IndependentEquivalentCandidate(observed);
        Assert.Equal(observed, observedEquivalent);
        Assert.NotSame(observed.Source, observedEquivalent.Source);
        Assert.NotSame(
            observed.Source.Participant,
            observedEquivalent.Source.Participant);
        Assert.NotSame(
            observed.Source.Element,
            observedEquivalent.Source.Element);
        Assert.NotSame(observed.Peer, observedEquivalent.Peer);
        CountingContext[] contexts =
        [
            .. Enumerable.Range(0, 1_000).Select(
                index => new CountingContext(index)),
        ];
        var attempts = new List<IntegrationCandidateAttempt>();
        for (int index = 0; index < contexts.Length; index++)
        {
            IntegrationCandidateIdentity classifiedCandidate =
                IndependentEquivalentCandidate(observed);
            IntegrationCandidateIdentity fulfillerCandidate =
                IndependentEquivalentCandidate(observed);
            IntegrationCandidateIdentity suppressedCandidate =
                IndependentEquivalentCandidate(opportunity);
            IIntegrationBindingContextIdentity classifiedContext =
                new CountingContext(index);
            IIntegrationBindingContextIdentity fulfillerContext =
                new CountingContext(index);
            IIntegrationBindingContextIdentity suppressedContext =
                new CountingContext(index);
            attempts.Add(
                new IntegrationCandidateAttempt.Suppressed(
                    new IntegrationCandidateAttemptAddress(
                        suppressedCandidate,
                        suppressedContext),
                    new IntegrationCandidateAttemptAddress(
                        fulfillerCandidate,
                        fulfillerContext),
                    new IntegrationOpportunityFulfillment(
                        SourceType(suppressedCandidate),
                        Resolved(
                            suppressedCandidate,
                            PeerTerminal(fulfillerCandidate)))));
            attempts.Add(
                new IntegrationCandidateAttempt.Classified(
                    new IntegrationCandidateAttemptAddress(
                        classifiedCandidate,
                        classifiedContext),
                    new IntegrationCandidateDisposition.Out(
                        Resolved(
                            classifiedCandidate,
                            PeerTerminal(classifiedCandidate))),
                    [
                        Resolved(
                            fulfillmentSourceLookup,
                            SourceType(suppressedCandidate)),
                    ]));
        }
        attempts.Reverse();

        CountingContext.Reset();
        IntegrationCensusSnapshot snapshot = Snapshot(
            participants,
            producerAttempts: Producers(
                participants,
                CompletedWithEvidence(
                    participant,
                    Ecosystem,
                    new IntegrationCandidateEvidence(
                        observed,
                        [fulfillmentSourceLookup]),
                    new IntegrationCandidateEvidence(
                        IndependentEquivalentCandidate(observed),
                        [fulfillmentSourceLookup])),
                Completed(
                    participant,
                    Opportunity,
                    opportunity,
                    IndependentEquivalentCandidate(opportunity))),
            contexts: contexts,
            candidateAttempts: attempts);

        Assert.Equal(2, snapshot.Candidates.Length);
        Assert.Equal(contexts.Length * 2, snapshot.CandidateAttempts.Length);
        Assert.Equal(contexts.Length, snapshot.SuppressedAttempts.Length);
        Assert.True(
            CountingContext.EqualsCalls < 20_000,
            $"Expected hash-backed addressing, but observed {CountingContext.EqualsCalls} context equality calls.");
    }

    [Fact]
    public void IntegrationCensus_SourceParticipantsRequireIncidentContext()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);

        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: Producers(
                    participants,
                    Completed(participant, Ecosystem, candidate)),
                contexts: [],
                contextIncidence: [Incidence(participant)]));
    }

    [Fact]
    public void IntegrationCensus_RequiresExactContextIncidenceRequirement()
    {
        AnalysisRequestPlan plan = Plan();
        var withoutIncidence = new AnalysisRequestPlan(
            plan.Request,
            plan.Analysis,
            plan.ReportSurface,
            plan.Universe,
            plan.Projection,
            [
                .. plan.UniverseRequirements.Where(requirement =>
                    !ReferenceEquals(
                        requirement,
                        IntegrationAnalysisCatalog
                            .BindingContextsRequirement)),
            ],
            plan.Cost);

        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                [Portable()],
                plan: withoutIncidence));
    }

    [Fact]
    public void IntegrationCensus_ContextAccessExposesOnlyImmutableOwnerIdentities()
    {
        Type access = typeof(IntegrationBindingContextAccess);
        Assert.False(typeof(IDisposable).IsAssignableFrom(access));
        Assert.DoesNotContain(
            access.GetMethods(
                BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly),
            method => !method.IsSpecialName);
        Assert.Equal(
            ["BindingContexts", "SourceIncidence"],
            access.GetProperties().Select(property => property.Name));

        Type[] forbidden =
        [
            typeof(InspectionWorkspace),
            typeof(AssemblyContextGroup),
            typeof(AnalysisUniverseExecutionAccess),
            typeof(AnalysisUniverseCapabilityLease<>),
        ];
        IEnumerable<Type> publicShape =
            access.GetProperties().Select(property => property.PropertyType)
                .Concat(
                    access.GetConstructors().SelectMany(constructor =>
                        constructor.GetParameters().Select(
                            parameter => parameter.ParameterType)));
        Assert.All(
            publicShape,
            shape => Assert.DoesNotContain(
                forbidden,
                type => TypeShapeContains(shape, type)));
    }

    [Fact]
    public void IntegrationCapability_ExecutableHandoffProvidesTypedContextIncidence()
    {
        using var workspace = new InspectionWorkspace();
        IntegrationSourceParticipantIdentity participant = Portable();
        IContext context = new Context();
        var contextAccess = new IntegrationBindingContextAccess(
            [context],
            [Incidence(participant, context)]);
        AnalysisUniverseCapabilityDescriptor[] capabilities =
        [
            .. IntegrationAnalysisCatalog.UniverseRequirements
                .Select(requirement => requirement.Capability),
        ];
        AnalysisUniverseOffer offer = workspace.CreateAnalysisUniverseOffer(
            new UniverseIdentity(),
            new UniverseBoundary(),
            new UniverseBoundary(),
            capabilities,
            new UniverseCompleteness(),
            capabilities.Select(capability =>
                Registration(capability, contextAccess)));
        AnalysisRequestPlan plan = Plan(universe: offer.Description);

        using AnalysisUniverseExecutionAccess execution =
            Assert.IsType<AnalysisUniverseIssuanceResult.Ready>(
                offer.IssueExecutionAccess(
                    plan,
                    Xunit.TestContext.Current.CancellationToken))
            .Access;

        Assert.Same(
            contextAccess,
            IntegrationAnalysisCatalog.GetBindingContextAccess(execution));
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
                    new IntegrationCandidateDisposition.In(
                        Resolved(candidate, terminal))),
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
                        new IntegrationCandidateDisposition.In(
                            Resolved(candidate, terminal))),
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
                            Resolved(candidate, mismatched))),
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
        var resolved =
            new IntegrationResolvedPeer(candidate.Peer, [facade, terminal]);

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
        Assert.Equal(candidate.Peer, classified.Disposition.Peer.Lookup);
        Assert.Equal(terminal, classified.Disposition.Peer.Terminal);
    }

    [Fact]
    public void IntegrationCensus_ResolutionRejectsMismatchedLookupForwardingHopAndCycle()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            DefaultPeer);
        IContext context = new Context();
        IntegrationTypeIdentity terminal = PeerTerminal(candidate);

        // A resolution issued for a different lookup cannot classify this
        // candidate, regardless of the selected participant.
        IntegrationTypeIdentity wrongAssemblyStart = new(
            ParticipantForAssembly(Assembly("Wrong")),
            candidate.Peer.Type);
        IntegrationCandidatePeerIdentity wrongLookup =
            AssemblyPeer(Assembly("Wrong"), candidate.Peer.Type);
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
                            new IntegrationResolvedPeer(
                                wrongLookup,
                                [wrongAssemblyStart]))),
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
                            new IntegrationResolvedPeer(
                                candidate.Peer,
                                [renamingHop, terminal]))),
                ]));

        // A repeated identity in the resolution path is a forwarding cycle.
        Assert.Throws<ArgumentException>(() =>
            new IntegrationResolvedPeer(
                candidate.Peer,
                [terminal, terminal]));

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
    public void IntegrationCensus_ResolutionRetainsLookupAcrossBindingPolicyVersionSelection()
    {
        IntegrationSourceParticipantIdentity participant = Portable();
        var participants = new[] { participant };
        IntegrationCandidateIdentity candidate = ObservedCandidate(
            participant,
            IntegrationConceptCatalog.AI,
            AssemblyPeer(
                new AssemblyReferenceIdentity(
                    "Peer",
                    new Version(1, 0),
                    null,
                    null),
                TypeName("Peer", "Client")));
        IContext context = new Context();
        IntegrationTypeIdentity selected = new(
            ParticipantForAssembly(
                new AssemblyReferenceIdentity(
                    "Peer",
                    new Version(2, 0),
                    null,
                    null)),
            candidate.Peer.Type);

        IntegrationCensusSnapshot snapshot = Snapshot(
            participants,
            producerAttempts: Producers(
                participants,
                Completed(participant, Ecosystem, candidate)),
            selectedTypes: [selected],
            contexts: [context],
            candidateAttempts:
            [
                new IntegrationCandidateAttempt.Classified(
                    new IntegrationCandidateAttemptAddress(candidate, context),
                    new IntegrationCandidateDisposition.In(
                        Resolved(candidate, selected))),
            ]);

        IntegrationResolvedPeer resolved =
            Assert.Single(snapshot.ClassifiedAttempts).Disposition.Peer;
        Assert.Equal(candidate.Peer, resolved.Lookup);
        Assert.Equal(selected, resolved.Terminal);
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
                    new IntegrationCandidateDisposition.In(
                        Resolved(candidate, terminal))),
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
        Assert.NotEqual(
            SourceType(fixture.OpportunityCandidateId),
            SourceType(fixture.ObservedCandidate));

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
    public void IntegrationCensus_SuppressionRejectsSelfAndMissingFulfiller()
    {
        SuppressionFixture fixture = new(this);

        // Self / cyclic.
        Assert.Throws<ArgumentException>(() =>
            fixture.Snapshot(
                new IntegrationCandidateAttempt.Suppressed(
                    fixture.OpportunityAttemptAddress,
                    fixture.OpportunityAttemptAddress,
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
    public void IntegrationCensus_SuppressionRejectsCrossContextFulfiller()
    {
        SuppressionFixture fixture = new(this);
        var participants = new[] { fixture.Participant };
        IntegrationCandidateAttemptAddress observedSecond =
            new(fixture.ObservedCandidate, fixture.OtherContext);

        Assert.Throws<ArgumentException>(() =>
            Snapshot(
                participants,
                producerAttempts: Producers(
                    participants,
                    Completed(
                        fixture.Participant,
                        Opportunity,
                        fixture.OpportunityCandidateId),
                    Completed(
                        fixture.Participant,
                        Ecosystem,
                        fixture.ObservedCandidate)),
                contexts: [fixture.Context, fixture.OtherContext],
                candidateAttempts:
                [
                    ClassifiedOut(
                        fixture.ObservedCandidate,
                        fixture.Context),
                    ClassifiedOut(
                        fixture.ObservedCandidate,
                        fixture.OtherContext),
                    new IntegrationCandidateAttempt.Suppressed(
                        new IntegrationCandidateAttemptAddress(
                            fixture.OpportunityCandidateId,
                            fixture.Context),
                        observedSecond,
                        fixture.Fulfillment),
                    new IntegrationCandidateAttempt.Suppressed(
                        new IntegrationCandidateAttemptAddress(
                            fixture.OpportunityCandidateId,
                            fixture.OtherContext),
                        observedSecond,
                        fixture.Fulfillment),
                ]));
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
        IntegrationCandidateIdentity second = new(
            OpportunityRel,
            IntegrationConceptCatalog.AI,
            new IntegrationCandidateSourceIdentity(
                participant,
                TypeElement("Src", "Other")),
            PolicyTargetPeer("Peer.Lib", TypeName("Peer", "Client")));
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
                            Resolved(first, PeerTerminal(second)))),
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
            AssemblyPeer(Assembly("Peer.Lib"), TypeName("Peer", "Client")));
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
                            Resolved(observed, observedTerminal))),
                    new IntegrationCandidateAttempt.Suppressed(
                        new IntegrationCandidateAttemptAddress(
                            opportunity,
                            context),
                        observedAddress,
                        new IntegrationOpportunityFulfillment(
                            SourceType(opportunity),
                            Resolved(opportunity, observedTerminal))),
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

        // Every declared fulfillment-source lookup requires one exact
        // resolution before the observation can suppress an opportunity.
        Assert.Throws<ArgumentException>(() =>
            fixture.Snapshot(
                fixture.Suppressed(fixture.ObservedAttemptAddress),
                observedAttempt:
                    new IntegrationCandidateAttempt.Classified(
                        fixture.ObservedAttemptAddress,
                        new IntegrationCandidateDisposition.In(
                            Resolved(
                                fixture.ObservedCandidate,
                                fixture.ObservedTerminal)))));

        // Proof source must equal the opportunity source, not some other Type.
        IntegrationOpportunityFulfillment wrongSource =
            new IntegrationOpportunityFulfillment(
                new IntegrationTypeIdentity(participant, TypeName("Src", "Other")),
                Resolved(
                    fixture.OpportunityCandidateId,
                    fixture.ObservedTerminal));
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
                    fixture.OpportunityCandidateId,
                    new IntegrationTypeIdentity(
                        ParticipantForAssembly(Assembly("OtherPeer")),
                        TypeName("Peer", "Client"))));
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
        IntegrationCandidatePeerIdentity peer,
        IntegrationCandidateSourceElement? sourceElement = null) =>
        new(
            Observed,
            concept,
            new IntegrationCandidateSourceIdentity(
                participant,
                sourceElement ?? TypeElement()),
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

    static IntegrationCandidateIdentity IndependentEquivalentCandidate(
        IntegrationCandidateIdentity candidate)
    {
        var coordinate = Assert.IsType<RealizedMemberCoordinate.Package>(
            candidate.Source.Participant.Coordinate);
        IntegrationSourceParticipantIdentity participant =
            IntegrationSourceParticipantIdentity.Portable(
                new RealizedMemberCoordinate.Package(
                    coordinate.PackageId,
                    coordinate.Version,
                    coordinate.Producer,
                    coordinate.Framework,
                    coordinate.RuntimeIdentifier),
                CopyAssembly(candidate.Source.Participant.Assembly));
        var source = Assert.IsType<IntegrationCandidateSourceElement.Type>(
            candidate.Source.Element);
        IntegrationCandidatePeerIdentity peer = candidate.Peer switch
        {
            IntegrationCandidatePeerIdentity.NamedType
            {
                Reference.Scope:
                    MetadataTypeReferenceScope.AssemblyReference assembly,
            } named =>
                AssemblyPeer(
                    CopyAssembly(assembly.Assembly),
                    CopyTypeName(named.Reference.Type)),
            IntegrationCandidatePeerIdentity.PolicyTarget target =>
                PolicyTargetPeer(
                    target.Target.AssemblyName,
                    CopyTypeName(target.Target.Type)),
            _ => throw new InvalidOperationException(
                "The semantic-key fixture supports assembly and policy targets."),
        };
        return new IntegrationCandidateIdentity(
            candidate.Relationship,
            candidate.Concept,
            new IntegrationCandidateSourceIdentity(
                participant,
                new IntegrationCandidateSourceElement.Type(
                    CopyTypeName(source.Name))),
            peer);
    }

    static AssemblyReferenceIdentity CopyAssembly(
        AssemblyReferenceIdentity assembly) =>
        new(
            assembly.Name,
            assembly.Version,
            assembly.Culture,
            assembly.PublicKeyToken);

    static MetadataTypeDefinitionName CopyTypeName(
        MetadataTypeDefinitionName type) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                type.Namespace,
                type.Segments)).Name;

    static IntegrationResolvedPeer Resolved(
        IntegrationCandidateIdentity candidate,
        IntegrationTypeIdentity terminal) =>
        new(candidate.Peer, [terminal]);

    static IntegrationResolvedPeer Resolved(
        IntegrationCandidatePeerIdentity lookup,
        IntegrationTypeIdentity terminal) =>
        new(lookup, [terminal]);

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

    static IntegrationProducerPolicyAttempt CompletedWithEvidence(
        IntegrationSourceParticipantIdentity participant,
        IntegrationProducerPolicyBinding policy,
        params IntegrationCandidateEvidence[] evidence) =>
        IntegrationProducerPolicyAttempt.Completed.WithEvidence(
            new IntegrationProducerPolicyAttemptAddress(participant, policy),
            evidence);

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
                Resolved(candidate, PeerTerminal(candidate))));

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
        IEnumerable<IntegrationSourceBindingContextIncidence>?
            contextIncidence = null,
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

        IIntegrationBindingContextIdentity[] contextRoster =
            [.. contexts ?? [new Context()]];
        IntegrationBindingContextAccess contextAccess = new(
            contextRoster,
            contextIncidence
                ?? participants.Select(participant =>
                    new IntegrationSourceBindingContextIncidence(
                        participant,
                        contextRoster)));

        return new(
            plan ?? Plan(),
            participants,
            selected,
            contextAccess,
            sourceAttempts ?? participants.Select(Available),
            producers,
            candidateAttempts ?? []);
    }

    static IntegrationSourceBindingContextIncidence Incidence(
        IntegrationSourceParticipantIdentity participant,
        params IIntegrationBindingContextIdentity[] contexts) =>
        new(participant, contexts);

    static AnalysisUniverseCapabilityRegistration Registration(
        AnalysisUniverseCapabilityDescriptor capability,
        IntegrationBindingContextAccess contextAccess)
    {
        if (ReferenceEquals(
                capability,
                IntegrationAnalysisCatalog.BindingContexts))
        {
            return new AnalysisUniverseCapabilityRegistration<
                IntegrationBindingContextAccess>(
                    capability,
                    (_, _) =>
                        new AnalysisUniverseCapabilityAcquisition<
                            IntegrationBindingContextAccess>.Ready(
                                new AnalysisUniverseCapabilityLease<
                                    IntegrationBindingContextAccess>(
                                        contextAccess,
                                        static () => { })));
        }

        return new AnalysisUniverseCapabilityRegistration<object>(
            capability,
            (_, _) =>
                new AnalysisUniverseCapabilityAcquisition<object>.Ready(
                    new AnalysisUniverseCapabilityLease<object>(
                        new object(),
                        static () => { })));
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
                    ExtensionMethodsQuery.Definition,
                    _ => new ExtensionMethodsResult.Available([]))
                .Add(
                    AssemblyContextIntegrationOpportunitiesQuery.Definition,
                    (_, _) => new AssemblyContextIntegrationOpportunitiesResult([]),
                    AssemblyContextIntegrationsQuery.Definition)
                .Compile(),
            IntegrationAnalysisCatalog.ProducerPolicies.Select(
                policy => policy.ProducerPrerequisite));

    interface IContext : IIntegrationBindingContextIdentity;
    sealed class Context : IContext;
    sealed class CountingContext(int id) : IIntegrationBindingContextIdentity
    {
        public static int EqualsCalls { get; private set; }

        public static void Reset() => EqualsCalls = 0;

        public override bool Equals(object? obj)
        {
            EqualsCalls++;
            return obj is CountingContext other
                && id == other.Id;
        }

        public override int GetHashCode() => id;

        int Id => id;
    }
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

    static bool TypeShapeContains(Type shape, Type expected)
    {
        if (shape == expected
            || shape.IsGenericType
                && shape.GetGenericTypeDefinition() == expected)
        {
            return true;
        }
        return shape.GetGenericArguments().Any(argument =>
            TypeShapeContains(argument, expected));
    }

    // A same-context observed adapter member and opportunity sharing one
    // concept and exact target. The proof source is the SDK Type the adapter
    // extends, not the adapter member that supplied observed evidence.
    sealed class SuppressionFixture
    {
        readonly IntegrationSourceParticipantIdentity _participant;
        readonly IntegrationTypeIdentity _observedTerminal;
        readonly IntegrationCandidatePeerIdentity.NamedType
            _fulfillmentSourceLookup;
        readonly IntegrationResolvedPeer _fulfillmentSourceResolution;

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
                AssemblyPeer(
                    Assembly("Peer.Lib"),
                    TypeName("Peer", "Client")),
                new IntegrationCandidateSourceElement.Member(
                    TypeName("Adapters", "ChatClientAdapter"),
                    Anchor("Adapters.ChatClientAdapter")));
            _observedTerminal = PeerTerminal(ObservedCandidate);
            _fulfillmentSourceLookup =
                Assert.IsType<IntegrationCandidatePeerIdentity.NamedType>(
                    NamedPeer(
                        new MetadataTypeReferenceScope.CurrentAssembly(),
                        OpportunityCandidateId.Source.SourceType));
            _fulfillmentSourceResolution = Resolved(
                _fulfillmentSourceLookup,
                SourceType(OpportunityCandidateId));
            Fulfillment = new IntegrationOpportunityFulfillment(
                SourceType(OpportunityCandidateId),
                Resolved(OpportunityCandidateId, _observedTerminal));
        }

        public IContext Context { get; }
        public IContext OtherContext { get; }
        public IntegrationSourceParticipantIdentity Participant =>
            _participant;
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
                    CompletedWithEvidence(
                        _participant,
                        Ecosystem,
                        new IntegrationCandidateEvidence(
                            ObservedCandidate,
                            [_fulfillmentSourceLookup]))),
                selectedTypes: [_observedTerminal],
                contexts: [Context],
                candidateAttempts:
                [
                    observedAttempt
                        ?? new IntegrationCandidateAttempt.Classified(
                            ObservedAttemptAddress,
                            new IntegrationCandidateDisposition.In(
                                Resolved(
                                    ObservedCandidate,
                                    _observedTerminal)),
                            [_fulfillmentSourceResolution]),
                    opportunityAttempt,
                ]);
        }
    }
}
