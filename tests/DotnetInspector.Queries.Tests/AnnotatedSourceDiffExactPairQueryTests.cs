using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

public sealed class AnnotatedSourceDiffExactPairQueryTests
{
    [Theory]
    [InlineData(42)]
    [InlineData(43)]
    public async Task DesignatedSignaturePair_ProjectsBothExactDeclarations(int result)
    {
        byte[] before = WorkspaceResearchTargetFixture.BuildAssembly("Direct", methodResult: 42);
        byte[] after = WorkspaceResearchTargetFixture.BuildAssembly("Direct", methodResult: result, methodParameter: true);
        await using var fixture = new WorkspaceResearchTargetFixture(before, after);
        using var oldGroup = fixture.CreateGroup([0]);
        using var newGroup = fixture.CreateGroup([1]);
        var oldRoot = fixture.Nodes[0].Participant;
        var newRoot = fixture.Nodes[1].Participant;
        var oldSelection = Selection(oldGroup, oldRoot);
        var newSelection = Selection(newGroup, newRoot);
        Assert.NotEqual(oldSelection.Selector, newSelection.Selector);
        var document = AssemblyContextImplementationDiffQuery.Execute(oldGroup, oldRoot, newGroup, newRoot,
            new HashSet<int> { oldSelection.MethodToken }, new HashSet<int> { newSelection.MethodToken },
            (oldBinding, newBinding) => AnnotatedSourceDiffExactPairQuery.Execute(oldGroup, oldRoot, newGroup, newRoot,
                oldBinding, newBinding, oldSelection, newSelection, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.Equal(AnnotatedSourceDiffSideOutcomeKind.Present, document.Before.Outcome);
        Assert.Equal(AnnotatedSourceDiffSideOutcomeKind.Present, document.After.Outcome);
        Assert.All(document.Media, medium => Assert.NotNull(medium.Comparison));
        Assert.NotEqual(document.Before.Document!.Text, document.After.Document!.Text);
        Assert.Equal(document.Subject.Selector, newSelection.Selector.NormalizedSelector);
        Assert.Throws<ArgumentException>(() => AssemblyContextImplementationDiffQuery.Execute(oldGroup, oldRoot, newGroup, newRoot,
            new HashSet<int>(), new HashSet<int> { newSelection.MethodToken },
            (oldBinding, newBinding) => AnnotatedSourceDiffExactPairQuery.Execute(oldGroup, oldRoot, newGroup, newRoot,
                oldBinding, newBinding, null, newSelection with { Anchor = oldSelection.Anchor }, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
    }

    static AnnotatedSourceDiffBodyEndpoint Selection(AssemblyContextGroup group, AssemblyContextParticipant root)
    {
        var available = Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
            AssemblyContextApiSurfaceQuery.ExecuteParticipant(group, root, ApiSurfaceScope.Public));
        var type = Assert.Single(available.Value.Surface.Types);
        var member = Assert.Single(type.Members);
        var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
        return new(type.DefinitionName!, anchor, MemberTargetSelector.Parse(anchor.StableSelector),
            member.MetadataToken!.Value, ResearchTargetRelationshipRole.Method);
    }
}

public sealed class MemberBodyDiffInspectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovedBody_RetainsItsExactOccupiedSideAndTypeTopology(bool removeType)
    {
        await using var fixture = new WorkspaceResearchTargetFixture(
            WorkspaceResearchTargetFixture.BuildAssembly("Direct"),
            WorkspaceResearchTargetFixture.BuildAssembly("Direct", definesType: !removeType, definesMethod: false));
        using var oldGroup = fixture.CreateGroup([0]);
        using var newGroup = fixture.CreateGroup([1]);
        var inspection = DotnetInspector.ResearchSections.MemberBodyDiffInspection.Execute(
            oldGroup, fixture.Nodes[0].Participant, newGroup, fixture.Nodes[1].Participant,
            new ApiSurfaceProjectionLimits(2, 100, 100, 100, 100, 10_000), TestContext.Current.CancellationToken);
        var removed = Assert.Single(inspection.Content.Members);
        Assert.Equal("Removed", removed.Outcome);
        Assert.NotNull(removed.Before);
        Assert.Null(removed.After);
        Assert.NotNull(removed.ApiRelation);
        Assert.Equal(removeType, Assert.Single(inspection.Content.Api.Comparison.Subjects).Comparison.After is null);
        Assert.DoesNotContain(inspection.Content.Destinations, member => member.After is not null);
    }

    [Theory]
    [InlineData(42, false)]
    [InlineData(43, false)]
    [InlineData(42, true)]
    [InlineData(43, true)]
    public async Task ApiSignaturePair_RetainsTheRelationAndImplementationEvidence(int result, bool property)
    {
        await using var fixture = new WorkspaceResearchTargetFixture(
            WorkspaceResearchTargetFixture.BuildAssembly("Direct", methodResult: 42, definesProperty: property),
            WorkspaceResearchTargetFixture.BuildAssembly("Direct", methodReturnsLong: true, methodResult: result, definesProperty: property));
        using var oldGroup = fixture.CreateGroup([0]);
        using var newGroup = fixture.CreateGroup([1]);
        var oldRoot = fixture.Nodes[0].Participant;
        var newRoot = fixture.Nodes[1].Participant;
        var inventory = DotnetInspector.ResearchSections.MemberBodyDiffInspection.Execute(
            oldGroup, oldRoot, newGroup, newRoot, new ApiSurfaceProjectionLimits(2, 100, 100, 100, 100, 10_000),
            TestContext.Current.CancellationToken);
        var member = Assert.Single(inventory.Content.Members);
        Assert.NotNull(member.ApiRelation);
        Assert.Equal("Changed", member.Outcome);
        Assert.NotEmpty(member.Implementation);
        var inspection = DotnetInspector.ResearchSections.MemberBodyDiffInspection.ExecuteMember(
            oldGroup, oldRoot, newGroup, newRoot, member, TestContext.Current.CancellationToken);
        Assert.Same(member.ApiRelation, inspection.Content.ApiRelation);
        Assert.NotEqual(inspection.Content.Document.Before.Document!.Text, inspection.Content.Document.After.Document!.Text);
        Assert.Throws<ArgumentException>(() => DotnetInspector.ResearchSections.MemberBodyDiffInspection.ExecuteMember(
            oldGroup, oldRoot, newGroup, newRoot, member with { Before = null }, TestContext.Current.CancellationToken));
    }
}
