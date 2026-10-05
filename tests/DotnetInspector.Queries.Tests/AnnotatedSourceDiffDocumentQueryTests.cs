using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

public sealed class AnnotatedSourceDiffDocumentQueryTests
{
    static readonly MetadataTypeDefinitionName s_type =
        WorkspaceResearchTargetFixture.TypeName;
    static readonly MemberTargetSelector s_member =
        WorkspaceResearchTargetFixture.Selector;

    [Fact]
    public async Task PairedBodies_ProjectExactCSharpAndIlDocuments()
    {
        byte[] before = WorkspaceResearchTargetFixture.BuildAssembly(
            "Direct",
            mvid: new("00000000-0000-0000-0000-000000000101"),
            methodResult: 1);
        byte[] after = WorkspaceResearchTargetFixture.BuildAssembly(
            "Direct",
            mvid: new("00000000-0000-0000-0000-000000000102"),
            methodResult: 2);
        await using var fixture =
            new WorkspaceResearchTargetFixture(before, after);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([1]);

        var published =
            Assert.IsType<AnnotatedSourceDiffDocumentQueryResult.Published>(
                Execute(
                    fixture,
                    beforeGroup,
                    afterGroup,
                    [0],
                    [1],
                    includeIl: true));

        Assert.IsType<ResearchTargetCorrespondenceOutcome.Paired>(
            published.Correspondence);
        Assert.Equal(
            AnnotatedSourceDiffSideOutcomeKind.Present,
            published.Document.Before.Outcome);
        Assert.Equal(
            AnnotatedSourceDiffSideOutcomeKind.Present,
            published.Document.After.Outcome);
        Assert.Equal(
            new Guid("00000000-0000-0000-0000-000000000101"),
            published.Document.Before.Endpoint!.ModuleVersionId);
        Assert.Equal(
            new Guid("00000000-0000-0000-0000-000000000102"),
            published.Document.After.Endpoint!.ModuleVersionId);
        Assert.Equal(
            [
                AnnotatedSourceDiffMediumKind.CSharp,
                AnnotatedSourceDiffMediumKind.Il,
            ],
            published.Document.Media.Select(static medium => medium.Medium));
        Assert.All(
            published.Document.Media,
            static medium => Assert.NotNull(medium.Comparison));
        Assert.Contains(
            published.Document.Before.Document!.Nodes,
            static node => node.Medium == SourceLineKind.Il);
    }

    [Fact]
    public async Task ForwardedBodies_RetainCanonicalHopProvenance()
    {
        byte[] beforeTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000202"),
            methodResult: 1);
        byte[] beforeFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(beforeTerminal),
            mvid: new("00000000-0000-0000-0000-000000000201"));
        byte[] afterTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "Terminal",
            mvid: new("00000000-0000-0000-0000-000000000204"),
            methodResult: 2);
        byte[] afterFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(afterTerminal),
            mvid: new("00000000-0000-0000-0000-000000000203"));
        await using var fixture = new WorkspaceResearchTargetFixture(
            beforeFacade,
            beforeTerminal,
            afterFacade,
            afterTerminal);
        using AssemblyContextGroup beforeGroup =
            fixture.CreateGroup([0, 1]);
        using AssemblyContextGroup afterGroup =
            fixture.CreateGroup([2, 3]);

        var published =
            Assert.IsType<AnnotatedSourceDiffDocumentQueryResult.Published>(
                Execute(
                    fixture,
                    beforeGroup,
                    afterGroup,
                    [0, 1],
                    [2, 3]));

        Assert.Collection(
            published.Document.Forwarders,
            forwarder =>
            {
                Assert.Equal(
                    AnnotatedSourceDiffSideKind.Before,
                    forwarder.Side);
                Assert.Equal(0, forwarder.HopIndex);
                Assert.Equal("N.Type", forwarder.TypeName);
                Assert.Equal("Terminal", forwarder.TargetAssembly);
            },
            forwarder =>
            {
                Assert.Equal(
                    AnnotatedSourceDiffSideKind.After,
                    forwarder.Side);
                Assert.Equal(0, forwarder.HopIndex);
            });
    }

    [Fact]
    public async Task AddedBody_ProjectsOccupiedSideAndTypedAbsence()
    {
        byte[] before = WorkspaceResearchTargetFixture.BuildAssembly(
            "Direct",
            methodName: "Other",
            mvid: new("00000000-0000-0000-0000-000000000301"));
        byte[] after = WorkspaceResearchTargetFixture.BuildAssembly(
            "Direct",
            methodName: "Value",
            mvid: new("00000000-0000-0000-0000-000000000302"));
        await using var fixture =
            new WorkspaceResearchTargetFixture(before, after);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([1]);

        var published =
            Assert.IsType<AnnotatedSourceDiffDocumentQueryResult.Published>(
                Execute(
                    fixture,
                    beforeGroup,
                    afterGroup,
                    [0],
                    [1]));

        Assert.IsType<ResearchTargetCorrespondenceOutcome.AfterOnly>(
            published.Correspondence);
        Assert.Equal(
            AnnotatedSourceDiffSideOutcomeKind.Absent,
            published.Document.Before.Outcome);
        Assert.Equal(
            AnnotatedSourceDiffSideReason.CorrespondenceKeyAbsent,
            published.Document.Before.Reason);
        Assert.Equal(
            AnnotatedSourceDiffSideOutcomeKind.Present,
            published.Document.After.Outcome);
        Assert.Null(Assert.Single(published.Document.Media).Comparison);
    }

    [Fact]
    public async Task AddedType_ProjectsTheOnlyComposedSide()
    {
        byte[] before = WorkspaceResearchTargetFixture.BuildAssembly(
            "Direct",
            definesType: false,
            mvid: new("00000000-0000-0000-0000-000000000311"));
        byte[] after = WorkspaceResearchTargetFixture.BuildAssembly(
            "Direct",
            mvid: new("00000000-0000-0000-0000-000000000312"));
        await using var fixture =
            new WorkspaceResearchTargetFixture(before, after);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([1]);

        var published =
            Assert.IsType<AnnotatedSourceDiffDocumentQueryResult.Published>(
                Execute(
                    fixture,
                    beforeGroup,
                    afterGroup,
                    [0],
                    [1]));

        Assert.IsType<ResearchTargetCorrespondenceOutcome.AfterOnly>(
            published.Correspondence);
        Assert.Equal(
            AnnotatedSourceDiffSideOutcomeKind.Absent,
            published.Document.Before.Outcome);
        Assert.Equal(
            AnnotatedSourceDiffSideOutcomeKind.Present,
            published.Document.After.Outcome);
    }

    [Fact]
    public async Task DivergentTerminalDomains_AreTypedUnavailable()
    {
        byte[] beforeTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "BeforeTerminal",
            mvid: new("00000000-0000-0000-0000-000000000402"));
        byte[] beforeFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(beforeTerminal),
            mvid: new("00000000-0000-0000-0000-000000000401"));
        byte[] afterTerminal = WorkspaceResearchTargetFixture.BuildAssembly(
            "AfterTerminal",
            mvid: new("00000000-0000-0000-0000-000000000404"));
        byte[] afterFacade = WorkspaceResearchTargetFixture.BuildAssembly(
            "Facade",
            definesType: false,
            forwardsTo: WorkspaceResearchTargetFixture.Identity(afterTerminal),
            mvid: new("00000000-0000-0000-0000-000000000403"));
        await using var fixture = new WorkspaceResearchTargetFixture(
            beforeFacade,
            beforeTerminal,
            afterFacade,
            afterTerminal);
        using AssemblyContextGroup beforeGroup =
            fixture.CreateGroup([0, 1]);
        using AssemblyContextGroup afterGroup =
            fixture.CreateGroup([2, 3]);

        var unavailable =
            Assert.IsType<AnnotatedSourceDiffDocumentQueryResult.Unavailable>(
                Execute(
                    fixture,
                    beforeGroup,
                    afterGroup,
                    [0, 1],
                    [2, 3]));

        Assert.Equal(
            AnnotatedSourceDiffDocumentQueryUnavailability
                .DivergentTerminalDomains,
            unavailable.Reason);
        Assert.Equal(2, unavailable.Forwarders.Length);
    }

    [Fact]
    public async Task Cancellation_IsTypedBeforeAcquisition()
    {
        byte[] before =
            WorkspaceResearchTargetFixture.BuildAssembly("Direct");
        byte[] after =
            WorkspaceResearchTargetFixture.BuildAssembly("Direct");
        await using var fixture =
            new WorkspaceResearchTargetFixture(before, after);
        using AssemblyContextGroup beforeGroup = fixture.CreateGroup([0]);
        using AssemblyContextGroup afterGroup = fixture.CreateGroup([1]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.IsType<AnnotatedSourceDiffDocumentQueryResult.Cancelled>(
            AnnotatedSourceDiffDocumentQuery.Execute(
                Request(
                    fixture,
                    beforeGroup,
                    afterGroup,
                    [0],
                    [1]),
                cancellation.Token));
        Assert.All(fixture.Nodes, static node => Assert.Equal(0, node.Opens));
    }

    static AnnotatedSourceDiffDocumentQueryResult Execute(
        WorkspaceResearchTargetFixture fixture,
        AssemblyContextGroup beforeGroup,
        AssemblyContextGroup afterGroup,
        int[] beforeBindings,
        int[] afterBindings,
        bool includeIl = false)
        => AnnotatedSourceDiffDocumentQuery.Execute(
            Request(
                fixture,
                beforeGroup,
                afterGroup,
                beforeBindings,
                afterBindings,
                includeIl));

    static AnnotatedSourceDiffDocumentRequest Request(
        WorkspaceResearchTargetFixture fixture,
        AssemblyContextGroup beforeGroup,
        AssemblyContextGroup afterGroup,
        int[] beforeBindings,
        int[] afterBindings,
        bool includeIl = false)
        => new(
            new(
                beforeGroup,
                beforeGroup.Participants[0],
                beforeBindings.Select(fixture.Binding)),
            new(
                afterGroup,
                afterGroup.Participants[0],
                afterBindings.Select(fixture.Binding)),
            s_type,
            s_member,
            includeIl);
}
