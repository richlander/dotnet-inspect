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
                member.Type, member.StableSelector, member.BodyTokens);
            Assert.Equal(member.Member == "AccessorBoxedValue" ? 2 : 1, targets.Length);
            foreach (var target in targets)
            {
                Assert.Contains(target.MethodToken, member.BodyTokens);
                Assert.NotNull(CallGraphMemberResolver.ResolveDefinitionIdentity(surface,
                    target.TypeId, target.MemberName, target.SelectorKey, target.MethodToken));
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
