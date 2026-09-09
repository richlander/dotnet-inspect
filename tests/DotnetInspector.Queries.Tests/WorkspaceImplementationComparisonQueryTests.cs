using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using Inspector.Findings;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceImplementationComparisonQueryTests
{
    static readonly MetadataTypeDefinitionName s_type =
        WorkspaceResearchTargetFixture.TypeName;
    static readonly MetadataTypeDefinitionName s_genericType =
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Type`1"])).Name;
    static readonly MetadataTypeDefinitionName s_nestedType =
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Type", "Inner"])).Name;
    static readonly MemberTargetSelector s_member =
        WorkspaceResearchTargetFixture.Selector;

    [Fact]
    public void ForwardedDefinitions_CompareExactTerminalBodiesAndRetainNativeFindings()
    {
        byte[] beforeTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000102"),
            methodResult: 1);
        byte[] beforeFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(beforeTerminal),
            mvid: new("00000000-0000-0000-0000-000000000101"));
        byte[] afterTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000202"),
            methodResult: 2);
        byte[] afterFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(afterTerminal),
            mvid: new("00000000-0000-0000-0000-000000000201"));

        using var fixture = new WorkspaceResearchTargetFixture(
            beforeFacade,
            beforeTerminal,
            afterFacade,
            afterTerminal);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0, 1]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([2, 3]);
        fixture.ResetProbes();
        int[] opens = fixture.Nodes.Select(node => node.Opens).ToArray();

        WorkspaceImplementationComparisonPublication publication = Published(
            Execute(
                fixture,
                beforeGroup,
                afterGroup,
                beforeBindings: [0, 1],
                afterBindings: [2, 3]));

        Assert.Equal(
            ResearchTargetCorrespondenceKind.Paired,
            publication.WorkItem.Correspondence.Kind);
        Assert.Equal(
            ResearchTargetDiagnosticKind.DeclaringTypeForwarded,
            Assert.IsType<WorkspaceResearchTargetAttemptEvidence.Unavailable>(
                publication.Before.RootAttempt).Diagnostic);
        Assert.Equal(
            ResearchTargetDiagnosticKind.DeclaringTypeForwarded,
            Assert.IsType<WorkspaceResearchTargetAttemptEvidence.Unavailable>(
                publication.After.RootAttempt).Diagnostic);
        Assert.Equal(
            new Guid("00000000-0000-0000-0000-000000000102"),
            publication.Before.EffectiveAttempt.Address!.Value.ModuleVersionId);
        Assert.Equal(
            new Guid("00000000-0000-0000-0000-000000000202"),
            publication.After.EffectiveAttempt.Address!.Value.ModuleVersionId);
        Assert.Equal(2, publication.Forwarders.Length);
        Assert.Collection(
            publication.Forwarders,
            before =>
            {
                Assert.Equal(QueryComparisonSide.Before, before.Side);
                Assert.Equal(0, before.HopIndex);
                Assert.Same(MetadataFindings.TypeForwarderDescriptor, before.Finding.Descriptor);
                Assert.Equal("N.Type", before.Finding.Payload.TypeName);
                Assert.Equal("Terminal", before.Finding.Payload.TargetAssembly);
            },
            after =>
            {
                Assert.Equal(QueryComparisonSide.After, after.Side);
                Assert.Equal(0, after.HopIndex);
                Assert.Same(MetadataFindings.TypeForwarderDescriptor, after.Finding.Descriptor);
                Assert.Equal("N.Type", after.Finding.Payload.TypeName);
                Assert.Equal("Terminal", after.Finding.Payload.TargetAssembly);
            });

        ResearchProducerCompletion completion =
            Assert.IsType<ResearchProducerSessionOutcome.Completed>(
                publication.Outcome).Completion;
        ImmutableArray<ResearchProducerWorkResult> exactResults =
        [
            .. completion.Results.Where(result =>
                result.Item.Basis is ResearchProducerWorkBasis.Correspondence basis
                && ReferenceEquals(basis.Outcome, publication.WorkItem.Correspondence)),
        ];
        Assert.Equal(2, exactResults.Length);
        var csharp = Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
            exactResults.Single(result =>
                result.Item.Producer == ResearchProducerKind.CSharp).Outcome).Result;
        var il = Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(
            exactResults.Single(result =>
                result.Item.Producer == ResearchProducerKind.IlBody).Outcome).Result;
        Assert.False(csharp.BodyDiff!.IsExact);
        Assert.False(il.MemberDiff!.Diff.IsExact);
        Assert.Equal(
            opens.Select(count => count + 1),
            fixture.Nodes.Select(node => node.Opens));
    }

    [Fact]
    public void DirectDefinitions_HaveNoForwarderUses()
    {
        byte[] before = WorkspaceResearchTargetFixture.BuildAssembly(
            "Direct",
            mvid: new("00000000-0000-0000-0000-000000000301"),
            methodResult: 1);
        byte[] after = WorkspaceResearchTargetFixture.BuildAssembly(
            "Direct",
            mvid: new("00000000-0000-0000-0000-000000000302"),
            methodResult: 1);

        using var fixture = new WorkspaceResearchTargetFixture(before, after);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([1]);
        fixture.ResetProbes();
        int[] opens = fixture.Nodes.Select(node => node.Opens).ToArray();

        WorkspaceImplementationComparisonPublication publication = Published(
            Execute(
                fixture,
                beforeGroup,
                afterGroup,
                beforeBindings: [0],
                afterBindings: [1]));

        Assert.Empty(publication.Forwarders);
        Assert.IsType<WorkspaceResearchTargetAttemptEvidence.Resolved>(
            publication.Before.RootAttempt);
        Assert.IsType<WorkspaceResearchTargetAttemptEvidence.Resolved>(
            publication.After.RootAttempt);
        ResearchProducerCompletion completion =
            Assert.IsType<ResearchProducerSessionOutcome.Completed>(
                publication.Outcome).Completion;
        Assert.All(
            completion.Results.Where(result =>
                result.Item.Basis is ResearchProducerWorkBasis.Correspondence basis
                && ReferenceEquals(basis.Outcome, publication.WorkItem.Correspondence)),
            result => Assert.True(
                result.Outcome is ResearchProducerWorkOutcome.ProducedCSharp
                    or ResearchProducerWorkOutcome.ProducedIlBody));
        Assert.Equal(
            opens.Select(count => count + 1),
            fixture.Nodes.Select(node => node.Opens));
    }

    [Fact]
    public void GenericForwarders_PreserveNativeFindingPayloadAndKey()
    {
        byte[] beforeTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000352"),
            methodResult: 1,
            typeGenericArity: 1);
        byte[] beforeFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(beforeTerminal),
            mvid: new("00000000-0000-0000-0000-000000000351"),
            typeGenericArity: 1);
        byte[] afterTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000354"),
            methodResult: 2,
            typeGenericArity: 1);
        byte[] afterFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(afterTerminal),
            mvid: new("00000000-0000-0000-0000-000000000353"),
            typeGenericArity: 1);

        using var fixture = new WorkspaceResearchTargetFixture(
            beforeFacade,
            beforeTerminal,
            afterFacade,
            afterTerminal);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0, 1]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([2, 3]);

        WorkspaceImplementationComparisonPublication publication = Published(
            Execute(
                fixture,
                beforeGroup,
                afterGroup,
                beforeBindings: [0, 1],
                afterBindings: [2, 3],
                declaringType: s_genericType));

        AssertNativeForwarderFinding(
            publication.Forwarders[0],
            beforeFacade,
            "N.Type<T>");
    }

    [Fact]
    public void NestedForwarders_PreserveOuterNativeFindingPayloadAndKey()
    {
        byte[] beforeTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000362"),
            methodResult: 1,
            nestedType: true);
        byte[] beforeFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(beforeTerminal),
            mvid: new("00000000-0000-0000-0000-000000000361"),
            nestedType: true);
        byte[] afterTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000364"),
            methodResult: 2,
            nestedType: true);
        byte[] afterFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(afterTerminal),
            mvid: new("00000000-0000-0000-0000-000000000363"),
            nestedType: true);

        using var fixture = new WorkspaceResearchTargetFixture(
            beforeFacade,
            beforeTerminal,
            afterFacade,
            afterTerminal);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0, 1]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([2, 3]);

        WorkspaceImplementationComparisonPublication publication = Published(
            Execute(
                fixture,
                beforeGroup,
                afterGroup,
                beforeBindings: [0, 1],
                afterBindings: [2, 3],
                declaringType: s_nestedType));

        AssertNativeForwarderFinding(
            publication.Forwarders[0],
            beforeFacade,
            "N.Type");
    }

    [Fact]
    public void DuplicateForwardersToSameTarget_PreserveTypedOutcomeAndEveryNativeFinding()
    {
        byte[] beforeTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000372"),
            methodResult: 1);
        byte[] beforeFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(beforeTerminal),
            mvid: new("00000000-0000-0000-0000-000000000371"),
            forwarderCount: 2);
        byte[] afterTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000374"),
            methodResult: 2);
        byte[] afterFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(afterTerminal),
            mvid: new("00000000-0000-0000-0000-000000000373"),
            forwarderCount: 2);

        using var fixture = new WorkspaceResearchTargetFixture(
            beforeFacade,
            beforeTerminal,
            afterFacade,
            afterTerminal);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0, 1]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([2, 3]);

        var rejected = Assert.IsType<
            WorkspaceImplementationComparisonResult.CompositionRejected>(
                Execute(
                    fixture,
                    beforeGroup,
                    afterGroup,
                    beforeBindings: [0, 1],
                    afterBindings: [2, 3]));

        WorkspaceTypeForwarderUse[] beforeUses =
        [
            .. rejected.Forwarders.Where(use =>
                use.Side == QueryComparisonSide.Before),
        ];
        ImmutableArray<Finding<TypeForwarderInfo>> native =
            NativeForwarderFindings(beforeFacade);
        Assert.Equal(
            WorkspaceResearchTargetCompositionRejection.RootAttemptMismatch,
            rejected.Result.Reason);
        Assert.Equal(QueryComparisonSide.Before, rejected.Side);
        Assert.Null(rejected.CompletedSide);
        Assert.Equal(2, native.Length);
        Assert.All(beforeUses, use => Assert.Equal(0, use.HopIndex));
        Assert.Equal(
            native,
            beforeUses.Select(use => use.Finding));
        Assert.Equal(2, rejected.Forwarders.Length);
    }

    [Fact]
    public void MissingTerminalParticipant_RetainsFollowedHopAndCompletedSide()
    {
        byte[] beforeTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000402"));
        byte[] beforeFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(beforeTerminal),
            mvid: new("00000000-0000-0000-0000-000000000401"));
        byte[] afterFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(beforeTerminal),
            mvid: new("00000000-0000-0000-0000-000000000403"));

        using var fixture = new WorkspaceResearchTargetFixture(
            beforeFacade,
            beforeTerminal,
            afterFacade);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0, 1]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([2]);
        fixture.ResetProbes();
        int[] opens = fixture.Nodes.Select(node => node.Opens).ToArray();

        var unavailable =
            Assert.IsType<WorkspaceImplementationComparisonResult.CompositionUnavailable>(
                Execute(
                    fixture,
                    beforeGroup,
                    afterGroup,
                    beforeBindings: [0, 1],
                    afterBindings: [2]));

        Assert.Equal(QueryComparisonSide.After, unavailable.Side);
        Assert.NotNull(unavailable.CompletedSide);
        Assert.Equal(
            WorkspaceResearchTargetCompositionUnavailability.MetadataResolutionUnavailable,
            unavailable.Result.Reason);
        Assert.Equal(2, unavailable.Forwarders.Length);
        Assert.All(unavailable.Forwarders, use =>
            Assert.Same(MetadataFindings.TypeForwarderDescriptor, use.Finding.Descriptor));
        Assert.Equal(
            opens.Select(count => count + 1),
            fixture.Nodes.Select(node => node.Opens));
    }

    [Fact]
    public void DivergentTerminalDomains_RetainBothForwarderUsesWithoutRunningProducers()
    {
        byte[] beforeTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "BeforeTerminal",
            mvid: new("00000000-0000-0000-0000-000000000502"));
        byte[] beforeFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(beforeTerminal),
            mvid: new("00000000-0000-0000-0000-000000000501"));
        byte[] afterTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "AfterTerminal",
            mvid: new("00000000-0000-0000-0000-000000000504"));
        byte[] afterFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(afterTerminal),
            mvid: new("00000000-0000-0000-0000-000000000503"));

        using var fixture = new WorkspaceResearchTargetFixture(
            beforeFacade,
            beforeTerminal,
            afterFacade,
            afterTerminal);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0, 1]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([2, 3]);
        fixture.ResetProbes();
        int[] opens = fixture.Nodes.Select(node => node.Opens).ToArray();

        var unavailable =
            Assert.IsType<WorkspaceImplementationComparisonResult.HandoffFailed>(
                Execute(
                    fixture,
                    beforeGroup,
                    afterGroup,
                    beforeBindings: [0, 1],
                    afterBindings: [2, 3]));

        Assert.Equal(
            WorkspaceImplementationComparisonHandoffFailureKind.Unavailable,
            unavailable.Kind);
        Assert.Equal(2, unavailable.Forwarders.Length);
        Assert.All(unavailable.Outcomes, outcome =>
            Assert.IsType<WorkspaceResearchTargetHandoffResult.Unavailable>(outcome));
        Assert.Equal(
            opens.Select(count => count + 1),
            fixture.Nodes.Select(node => node.Opens));
    }

    [Fact]
    public void MultiHopForwarding_RetainsOneNativeFindingPerOrderedHop()
    {
        byte[] beforeTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000603"));
        byte[] beforeBridge = WorkspaceResearchTargetFixture.BuildAssembly(
            "Bridge",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(beforeTerminal),
            mvid: new("00000000-0000-0000-0000-000000000602"));
        byte[] beforeFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(beforeBridge),
            mvid: new("00000000-0000-0000-0000-000000000601"));
        byte[] afterTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000703"));
        byte[] afterBridge = WorkspaceResearchTargetFixture.BuildAssembly(
            "Bridge",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(afterTerminal),
            mvid: new("00000000-0000-0000-0000-000000000702"));
        byte[] afterFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(afterBridge),
            mvid: new("00000000-0000-0000-0000-000000000701"));

        using var fixture = new WorkspaceResearchTargetFixture(
            beforeFacade,
            beforeBridge,
            beforeTerminal,
            afterFacade,
            afterBridge,
            afterTerminal);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0, 1, 2]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([3, 4, 5]);

        WorkspaceImplementationComparisonPublication publication = Published(
            Execute(
                fixture,
                beforeGroup,
                afterGroup,
                beforeBindings: [0, 1, 2],
                afterBindings: [3, 4, 5]));

        Assert.Collection(
            publication.Forwarders,
            use => AssertForwarder(use, QueryComparisonSide.Before, 0, "Bridge"),
            use => AssertForwarder(use, QueryComparisonSide.Before, 1, "Terminal"),
            use => AssertForwarder(use, QueryComparisonSide.After, 0, "Bridge"),
            use => AssertForwarder(use, QueryComparisonSide.After, 1, "Terminal"));
    }

    [Fact]
    public void ParticipantImageFailure_IsTypedBeforePlanning()
    {
        byte[] before = WorkspaceResearchTargetFixture.BuildAssembly("Direct");
        byte[] after = WorkspaceResearchTargetFixture.BuildAssembly("Direct");
        using var fixture = new WorkspaceResearchTargetFixture(before, after);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([1]);
        fixture.Nodes[0].Image = WorkspaceResearchTargetFixture.BuildAssembly("Changed");

        var unavailable =
            Assert.IsType<WorkspaceImplementationComparisonResult.ParticipantImageUnavailable>(
                Execute(
                    fixture,
                    beforeGroup,
                    afterGroup,
                    beforeBindings: [0],
                    afterBindings: [1]));

        Assert.Equal(QueryComparisonSide.Before, unavailable.Side);
        Assert.Equal(0, unavailable.ParticipantIndex);
        Assert.Equal("Direct", unavailable.Assembly.Name);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            unavailable.Failure.Kind);
        Assert.Equal(0, fixture.Nodes[1].Opens);
    }

    [Fact]
    public void Cancellation_IsTypedBeforeImageAcquisition()
    {
        byte[] before = WorkspaceResearchTargetFixture.BuildAssembly("Direct");
        byte[] after = WorkspaceResearchTargetFixture.BuildAssembly("Direct");
        using var fixture = new WorkspaceResearchTargetFixture(before, after);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([1]);
        WorkspaceImplementationComparisonRequest request = Request(
            fixture,
            beforeGroup,
            afterGroup,
            beforeBindings: [0],
            afterBindings: [1]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.IsType<WorkspaceImplementationComparisonResult.Cancelled>(
            WorkspaceImplementationComparisonQuery.Execute(
                request,
                cancellation.Token));
        Assert.All(fixture.Nodes, node => Assert.Equal(0, node.Opens));
    }

    static WorkspaceImplementationComparisonResult Execute(
        WorkspaceResearchTargetFixture fixture,
        AssemblyContextGroup beforeGroup,
        AssemblyContextGroup afterGroup,
        int[] beforeBindings,
        int[] afterBindings,
        MetadataTypeDefinitionName? declaringType = null)
        => WorkspaceImplementationComparisonQuery.Execute(Request(
            fixture,
            beforeGroup,
            afterGroup,
            beforeBindings,
            afterBindings,
            declaringType));

    static WorkspaceImplementationComparisonRequest Request(
        WorkspaceResearchTargetFixture fixture,
        AssemblyContextGroup beforeGroup,
        AssemblyContextGroup afterGroup,
        int[] beforeBindings,
        int[] afterBindings,
        MetadataTypeDefinitionName? declaringType = null)
        => new(
                new(
                    beforeGroup,
                    beforeGroup.Participants[0],
                    beforeBindings.Select(fixture.Binding)),
                new(
                    afterGroup,
                    afterGroup.Participants[0],
                    afterBindings.Select(fixture.Binding)),
                declaringType ?? s_type,
                s_member,
                ResearchProducerCatalog.Kinds);

    static WorkspaceImplementationComparisonPublication Published(
        WorkspaceImplementationComparisonResult result)
        => Assert.IsType<WorkspaceImplementationComparisonResult.Published>(result)
            .Publication;

    static void AssertForwarder(
        WorkspaceTypeForwarderUse use,
        QueryComparisonSide side,
        int hopIndex,
        string target)
    {
        Assert.Equal(side, use.Side);
        Assert.Equal(hopIndex, use.HopIndex);
        Assert.Same(MetadataFindings.TypeForwarderDescriptor, use.Finding.Descriptor);
        Assert.Equal(target, use.Finding.Payload.TargetAssembly);
    }

    static void AssertNativeForwarderFinding(
        WorkspaceTypeForwarderUse use,
        byte[] facade,
        string expectedTypeName)
    {
        Finding<TypeForwarderInfo> native =
            Assert.Single(NativeForwarderFindings(facade));

        Assert.Equal(expectedTypeName, native.Payload.TypeName);
        Assert.Equal(native.Subject, use.Finding.Subject);
        Assert.Same(native.Descriptor, use.Finding.Descriptor);
        Assert.Equal(native.Key, use.Finding.Key);
        Assert.Equal(native.Payload, use.Finding.Payload);
    }

    static ImmutableArray<Finding<TypeForwarderInfo>> NativeForwarderFindings(
        byte[] facade)
    {
        AssemblyReferenceIdentity identity =
            WorkspaceResearchTargetFixture.Identity(facade);
        using var peReader = new PEReader(
            new MemoryStream(facade, writable: false));
        FindingInspection<TypeForwarderInfo> nativeInspection =
            MetadataFindings.InspectTypeForwarders(
                AssemblyDetailScanner.ScanTypeForwarders(peReader),
                new FindingSubject(
                    (identity with { Version = null }).ToString(),
                    identity.Name));
        return Assert.IsType<FindingInspection<TypeForwarderInfo>.Complete>(
                nativeInspection.Value)
            .Findings;
    }

}
