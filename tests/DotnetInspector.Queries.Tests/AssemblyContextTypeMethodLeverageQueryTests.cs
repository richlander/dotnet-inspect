using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextTypeMethodLeverageQueryTests
{
    [Fact]
    public void Winners_PreservesSemanticTiesAndIgnoresMaxDepthAndToken()
    {
        MethodLeverage first = Leverage(
            "First",
            0x06000001,
            directCallers: 3,
            rootReach: 5,
            fanout: 2,
            loopCalls: 1,
            maxDepth: 2);
        MethodLeverage tied = Leverage(
            "Tied",
            0x06000002,
            directCallers: 3,
            rootReach: 5,
            fanout: 2,
            loopCalls: 1,
            maxDepth: 20);
        MethodLeverage runnerUp = Leverage(
            "RunnerUp",
            0x06000003,
            directCallers: 2,
            rootReach: 99,
            fanout: 99,
            loopCalls: 99,
            maxDepth: 99);

        ImmutableArray<MethodLeverage> winners =
            AssemblyContextTypeMethodLeverageQuery.Winners(
                [first, tied, runnerUp]);

        Assert.Equal([first, tied], winners);
    }

    [Fact]
    public void Winners_RequiresNonzeroInboundCallers()
    {
        Assert.Empty(
            AssemblyContextTypeMethodLeverageQuery.Winners(
                [
                    Leverage(
                        "Disconnected",
                        0x06000001,
                        directCallers: 0,
                        rootReach: 1,
                        fanout: 8,
                        loopCalls: 3,
                        maxDepth: 12),
                ]));
    }

    [Fact]
    public async Task ExecuteParticipant_SystemTextJsonKeepsInternalWinner()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            RealAsset("DocumentationQuery", "System.Text.Json.dll"));
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ApiType type = Type(
            group,
            participant,
            "JsonSerializerOptions",
            ApiSurfaceScope.IncludeAll);
        string typeDefinitionId =
            AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type);
        ApiMember verifyMutable = Assert.Single(
            type.Members,
            member => member.Name == "VerifyMutable");
        ApiMember publicRunnerUp = Assert.Single(
            type.Members,
            member => member.Name == "AllowDuplicateProperties");

        AssemblyTypeMethodLeverageInspection result =
            Available(group, participant, typeDefinitionId);

        Assert.Equal(
            InspectionCost.Unbounded,
            AssemblyContextTypeMethodLeverageQuery.Definition.Cost);
        Assert.NotEqual(0, result.MethodCount);
        Assert.Equal(1, result.WinnerCount);
        Assert.Equal(31, Assert.IsType<TypeMethodLeverageRank>(
            result.WinningRank).DirectCallerCount);
        TypeMethodLeverageWinner winner =
            Assert.Single(result.AnchoredWinners);
        Assert.Equal(typeDefinitionId, winner.TypeDefinitionId);
        Assert.Equal(
            ApiMemberIdentity.GetMemberAnchor(type, verifyMutable)
                .StableSelector,
            winner.StableSelector);
        Assert.True(
            verifyMutable.MetadataToken is { } verifyMutableToken
            && winner.MethodTokens.Contains(verifyMutableToken));
        Assert.NotEqual(
            ApiMemberIdentity.GetMemberAnchor(type, publicRunnerUp)
                .StableSelector,
            winner.StableSelector);
    }

    [Fact]
    public async Task ExecuteParticipant_FailsVisiblyForUnknownType()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            RealAsset("DocumentationQuery", "System.Text.Json.dll"));
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var failed = Assert.IsType<
            AssemblyContextEntry<
                AssemblyTypeMethodLeverageInspection>.Failed>(
                    AssemblyContextTypeMethodLeverageQuery.ExecuteParticipant(
                        group,
                        participant,
                        "Missing.Type"));

        Assert.Contains(
            "was not found",
            failed.Error.Message,
            StringComparison.Ordinal);
    }

    static AssemblyTypeMethodLeverageInspection Available(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string typeDefinitionId) =>
        Assert.IsType<
            AssemblyContextEntry<
                AssemblyTypeMethodLeverageInspection>.Available>(
                    AssemblyContextTypeMethodLeverageQuery.ExecuteParticipant(
                        group,
                        participant,
                        typeDefinitionId))
            .Value;

    static ApiType Type(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string name,
        ApiSurfaceScope scope)
    {
        AssemblyApiSurface surface =
            Assert.IsType<
                AssemblyContextEntry<AssemblyApiSurface>.Available>(
                    AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                        group,
                        participant,
                        scope))
                .Value;
        return Assert.Single(
            surface.Surface.Types,
            type => type.Name == name);
    }

    static MethodLeverage Leverage(
        string name,
        int token,
        int directCallers,
        int rootReach,
        int fanout,
        int loopCalls,
        int maxDepth) =>
        new(
            new MethodIdentity(
                "Assembly",
                Guid.Empty,
                TypeRef.Definition("Assembly", "Namespace", "Type"),
                name,
                [],
                TypeRef.CoreLib("System", "Void"),
                token,
                IsStatic: true),
            directCallers,
            fanout,
            maxDepth,
            loopCalls,
            rootReach);

    static string RealAsset(string scenario, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "RealAssets", scenario, fileName);

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        string assemblyPath)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(assemblyPath));
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
                    "type-method-leverage-fixture")),
            new MissingBindingPolicy());
        return workspace.CreateAssemblyContextGroup([participant]);
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
