using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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

    [Fact]
    public async Task ExecuteParticipant_RejectsIncompleteExternalCallerEvidence()
    {
        await using var healthyWorkspace = new InspectionWorkspace();
        using AssemblyContextGroup healthyGroup = Group(
            healthyWorkspace,
            ExternalCallerImage(corruptCaller: false),
            "complete-external-caller");
        AssemblyContextParticipant healthyParticipant =
            Assert.Single(healthyGroup.Participants);
        ApiType healthyType = Type(
            healthyGroup,
            healthyParticipant,
            "Target",
            ApiSurfaceScope.IncludeAll);
        AssemblyTypeMethodLeverageInspection healthy = Available(
            healthyGroup,
            healthyParticipant,
            AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(healthyType));
        Assert.Equal(1, healthy.WinnerCount);
        Assert.Equal(
            1,
            Assert.IsType<TypeMethodLeverageRank>(
                healthy.WinningRank).DirectCallerCount);

        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            ExternalCallerImage(corruptCaller: true),
            "incomplete-external-caller");
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ApiType type = Type(
            group,
            participant,
            "Target",
            ApiSurfaceScope.IncludeAll);
        string typeDefinitionId =
            AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type);

        var failed = Assert.IsType<
            AssemblyContextEntry<
                AssemblyTypeMethodLeverageInspection>.Failed>(
                    AssemblyContextTypeMethodLeverageQuery.ExecuteParticipant(
                        group,
                        participant,
                        typeDefinitionId));

        Assert.Contains(
            "Type method-leverage analysis is incomplete",
            failed.Error.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "CallWinner",
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
        => Group(
            workspace,
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(assemblyPath)),
            "type-method-leverage-fixture");

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        byte[] image,
        string provenance) =>
        Group(
            workspace,
            ImmutableCollectionsMarshal.AsImmutableArray(image),
            provenance);

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        ImmutableArray<byte> image,
        string provenance)
    {
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
                    provenance)),
            new MissingBindingPolicy());
        return workspace.CreateAssemblyContextGroup([participant]);
    }

    static byte[] ExternalCallerImage(bool corruptCaller)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("IncompleteExternalCaller.dll"),
            metadata.GetOrAddGuid(
                new Guid("D8C55865-4BA1-43F4-A8DD-768A7E01D899")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("IncompleteExternalCaller"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixture"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixture"),
            metadata.GetOrAddString("Caller"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(3));

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int winnerBody = RetBody(bodyEncoder);
        int otherBody = RetBody(bodyEncoder);
        var callerCode = new BlobBuilder();
        var caller = new InstructionEncoder(callerCode);
        caller.Call(MetadataTokens.MethodDefinitionHandle(1));
        caller.OpCode(ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(caller, maxStack: 0);
        BlobHandle signature = StaticVoidSignature(metadata);
        AddMethod(metadata, "Winner", signature, winnerBody);
        AddMethod(metadata, "Other", signature, otherBody);
        AddMethod(metadata, "CallWinner", signature, callerBody);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        byte[] bytes = image.ToArray();
        if (!corruptCaller)
        {
            return bytes;
        }
        using var reader =
            new PEReader(new MemoryStream(bytes, writable: false));
        int bodyRva = reader.GetMetadataReader()
            .GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(3))
            .RelativeVirtualAddress;
        SectionHeader section = Assert.Single(
            reader.PEHeaders.SectionHeaders,
            candidate =>
                bodyRva >= candidate.VirtualAddress
                && bodyRva < candidate.VirtualAddress
                    + candidate.SizeOfRawData);
        bytes[section.PointerToRawData
            + bodyRva
            - section.VirtualAddress] = 0;
        return bytes;
    }

    static int RetBody(MethodBodyStreamEncoder bodies)
    {
        var code = new BlobBuilder();
        new InstructionEncoder(code).OpCode(ILOpCode.Ret);
        return bodies.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 0);
    }

    static BlobHandle StaticVoidSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        return metadata.GetOrAddBlob(signature);
    }

    static void AddMethod(
        MetadataBuilder metadata,
        string name,
        BlobHandle signature,
        int body) =>
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            signature,
            body,
            MetadataTokens.ParameterHandle(1));

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
