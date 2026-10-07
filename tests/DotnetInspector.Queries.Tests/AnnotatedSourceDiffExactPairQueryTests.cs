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
