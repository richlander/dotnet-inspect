using System.Runtime.Versioning;
using DotnetInspect.Web.Interop.Analysis;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserPerformanceBodyTargetTests
{
    [Fact]
    public async Task RankedPropertyBodiesResolveAsExactSourceTargets()
    {
        var reference = ResolvedAssemblyReference.CreateFromPath(
            typeof(PerformanceAccessorFixture).Assembly.Location,
            AssemblyResolutionProvenance.Local("performance-accessor-test"));
        await using var workspace = new InspectionWorkspace();
        var participant = new AssemblyContextParticipant(reference, new MissingBindingPolicy());
        using var group = workspace.CreateAssemblyContextGroup([participant]);
        var ranking = AssemblyContextOptimizationOpportunitiesQuery.ExecuteParticipant(group, participant);
        var surface = Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
            AssemblyContextApiSurfaceQuery.ExecuteParticipant(group, participant)).Value.Surface;
        var rows = ranking.RankedMembers.Where(row =>
            row.Member.PublicMember?.Type == typeof(PerformanceAccessorFixture).FullName).ToArray();
        Assert.Equal(3, rows.Length);
        foreach (var row in rows)
        {
            var member = row.Member.PublicMember!;
            var targets = AnalysisExports.PerformanceBodyTargets(surface,
                member.Type, member.StableSelector, member.BodyTokens, row.Member.Ranking.Opportunities);
            Assert.Equal(member.Member == "AccessorBoxedValue" ? 2 : 1, targets.Length);
            foreach (var target in targets)
            {
                Assert.Contains(target.MethodToken, member.BodyTokens);
                Assert.NotNull(target.IssueOffsets);
                Assert.NotEmpty(target.IssueOffsets);
                var projected = Assert.IsType<AssemblyContextEntry<AssemblyMemberProjection>.Available>(
                    AssemblyContextMemberProjectionQuery.ExecuteParticipant(group, participant,
                        new(target.TypeId, target.MemberName, MethodToken: target.MethodToken, SourceDocument: true)))
                    .Value.Projection.SourceDocument;
                Assert.NotNull(projected);
                Assert.Contains(projected.Nodes, node => node.Medium == ILInspector.Decompiler.SourceLineKind.CSharp
                    && node.Provenance?.IlOffsets.Any(offset => target.IssueOffsets.Contains(offset)) == true);
                Assert.NotNull(CallGraphMemberResolver.ResolveDefinitionIdentity(surface,
                    target.TypeId, target.MemberName, target.SelectorKey, target.MethodToken));
            }
            var opportunity = row.Member.Ranking.Opportunities[0];
            foreach (var unavailable in new[] {
                opportunity with { ILOffset = null },
                opportunity with { Provenance = PerformanceTriageProvenance.Aggregate },
            })
            {
                var incompleteTargets = AnalysisExports.PerformanceBodyTargets(surface,
                    member.Type, member.StableSelector, member.BodyTokens, [unavailable]);
                Assert.All(incompleteTargets, target => Assert.Null(target.IssueOffsets));
            }
            if (member.Member != "Method")
                Assert.All(targets, target => Assert.NotEqual(member.Member, target.MemberName));
        }
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();
        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request) =>
            new(Version, AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}

public static class PerformanceAccessorFixture
{
    static object? s_value;
    public static object Method() => 42;
    public static object BoxedValue => 42;
    public static object AccessorBoxedValue
    {
        get => s_value ?? 42;
        set => s_value = value ?? 43;
    }
}
