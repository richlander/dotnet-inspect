using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextMethodAnalysisQueryTests
{
    [Fact]
    public void MethodAnalysis_ReturnsExactPhysicalBodyEvidence()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        int token = typeof(MethodAnalysisProbe)
            .GetMethod(nameof(MethodAnalysisProbe.Target))!
            .MetadataToken;

        AssemblyContextEntry<AssemblyMethodAnalysis> entry =
            AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                group,
                participant,
                token);

        AssemblyMethodAnalysis result = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                entry).Value;
        Assert.Equal(InspectionCost.Unbounded,
            AssemblyContextMethodAnalysisQuery.Definition.Cost);
        Assert.Equal(token, result.RequestedMethodToken);
        Assert.Equal(token, result.Method.MetadataToken);
        Assert.True(result.Signals.Allocations > 0);
        Assert.Contains(
            result.Allocations,
            allocation =>
                allocation.Method.MetadataToken == token
                && allocation.Kind == AllocationKind.Box);
        Assert.All(
            result.DirectCalls,
            call => Assert.Equal(
                token,
                call.EvidenceMethod.MetadataToken));
        Assert.Contains(
            result.DirectCalls,
            call => call.Kind == CallKind.NewObject);
        Assert.NotEmpty(result.ExceptionRegions);
        Assert.All(
            result.OptimizationOpportunities,
            opportunity => Assert.Equal(
                token,
                opportunity.EvidenceMethodToken
                    ?? opportunity.Method.MetadataToken));
        Assert.Contains(
            result.OptimizationOpportunities,
            opportunity => opportunity.Shape == "box-value-type");
    }

    [Fact]
    public void MethodAnalysis_DoesNotReturnNeighborBodyEvidence()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        int token = typeof(MethodAnalysisProbe)
            .GetMethod(nameof(MethodAnalysisProbe.Neighbor))!
            .MetadataToken;

        AssemblyMethodAnalysis result = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    participant,
                    token)).Value;

        Assert.Empty(result.Allocations);
        Assert.Empty(result.DirectCalls);
        Assert.Empty(result.ExceptionRegions);
        Assert.Empty(result.OptimizationOpportunities);
    }

    [Fact]
    public void MethodAnalysis_KeepsAsyncKickoffAndMoveNextEvidenceSeparate()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        MethodInfo kickoff = typeof(ClassicAsyncSiblingFixture)
            .GetMethod(
                nameof(
                    ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync))!;
        Type stateMachine = kickoff
            .GetCustomAttribute<AsyncStateMachineAttribute>()!
            .StateMachineType;
        MethodInfo moveNext = stateMachine.GetMethod(
            nameof(IAsyncStateMachine.MoveNext),
            BindingFlags.Instance
                | BindingFlags.NonPublic
                | BindingFlags.Public)!;

        AssemblyMethodAnalysis kickoffAnalysis = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    participant,
                    kickoff.MetadataToken)).Value;
        AssemblyMethodAnalysis moveNextAnalysis = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    participant,
                    moveNext.MetadataToken)).Value;

        Assert.DoesNotContain(
            kickoffAnalysis.DirectCalls,
            call =>
                call.Callee.Name
                == nameof(ClassicAsyncSiblingFixture.ReadValue));
        Assert.Contains(
            moveNextAnalysis.DirectCalls,
            call =>
                call.Callee.Name
                == nameof(ClassicAsyncSiblingFixture.ReadValue));
        Assert.All(
            kickoffAnalysis.DirectCalls,
            call => Assert.Equal(
                kickoff.MetadataToken,
                call.EvidenceMethod.MetadataToken));
        Assert.All(
            moveNextAnalysis.DirectCalls,
            call => Assert.Equal(
                moveNext.MetadataToken,
                call.EvidenceMethod.MetadataToken));
        Assert.All(
            moveNextAnalysis.OptimizationOpportunities,
            opportunity => Assert.Equal(
                moveNext.MetadataToken,
                opportunity.EvidenceMethodToken
                    ?? opportunity.Method.MetadataToken));
        Assert.All(
            moveNextAnalysis.Diagnostics,
            diagnostic => Assert.Equal(
                moveNext.MetadataToken,
                diagnostic.MethodToken));
    }

    [Fact]
    public void MethodAnalysis_ReturnsExactUnsafetyOccurrences()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);
        int token = typeof(MethodAnalysisProbe)
            .GetMethod(nameof(MethodAnalysisProbe.StackAllocate))!
            .MetadataToken;

        AssemblyMethodAnalysis result = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    token)).Value;

        Assert.True(result.Signals.Unsafe);
        Assert.Contains(
            result.UnsafetyOccurrences,
            occurrence =>
                occurrence.Method.MetadataToken == token
                && occurrence.Kind == UnsafetyKind.StackAlloc);
    }

    [Fact]
    public void MethodAnalysis_InvalidTokenIsAVisibleParticipantFailure()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);

        var failed = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Failed>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    0x02000001));

        Assert.Contains("not a MethodDef", failed.Error.Message);
    }

    [Fact]
    public void MethodAnalysis_BodylessMethodIsAVisibleParticipantFailure()
    {
        byte[] image = File.ReadAllBytes(
            typeof(IMethodAnalysisProbe).Assembly.Location);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);
        int token = typeof(IMethodAnalysisProbe)
            .GetMethod(nameof(IMethodAnalysisProbe.Bodyless))!
            .MetadataToken;

        var failed = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Failed>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    token));

        Assert.Contains("does not have an IL body", failed.Error.Message);
    }

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        byte[] content)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(content);
        using var reader = new PEReader(image);
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        var participant = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "method-analysis-fixture")),
            new MissingBindingPolicy());
        return workspace.CreateAssemblyContextGroup([participant]);
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)

        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                AssemblyBindingFailureKind.CandidateUnavailable));
        }
    }
}

public static class MethodAnalysisProbe
{
    static object? _value;

    public static object Target(int value)
    {
        try
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            return value;
        }
        finally
        {
            _value = value;
        }
    }

    public static int Neighbor(int value) => value + 1;

    public static int StackAllocate(int value)
    {
        Span<int> values = stackalloc int[1];
        values[0] = value;
        return values[0];
    }
}

public interface IMethodAnalysisProbe
{
    void Bodyless();
}
