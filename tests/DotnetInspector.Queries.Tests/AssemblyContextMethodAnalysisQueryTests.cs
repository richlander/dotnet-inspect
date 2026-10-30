using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextMethodAnalysisQueryTests
{
    [Fact]
    public async Task MethodAnalysis_ReturnsExactPhysicalBodyEvidence()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        await using var workspace = new InspectionWorkspace();
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
                token,
                TestContext.Current.CancellationToken);

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
    public async Task
        MethodAnalysis_MatchesLegacyAllocationAndOptimizationPublication()
    {
        string path = typeof(MethodAnalysisProbe).Assembly.Location;
        byte[] image = File.ReadAllBytes(path);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);

        foreach (string methodName in new[]
        {
            nameof(MethodAnalysisProbe.Target),
            nameof(MethodAnalysisProbe.StringConcatenation),
            nameof(MethodAnalysisProbe.ObjectTypedStringBuilder),
            nameof(MethodAnalysisProbe.CompilerGeneratedBox),
            nameof(MethodAnalysisProbe.ExceptionAllocations),
            nameof(MethodAnalysisProbe.NamedNonExceptionAllocations),
        })
        {
            int token = typeof(MethodAnalysisProbe)
                .GetMethod(methodName)!
                .MetadataToken;
            AssemblyMethodAnalysis actual = Assert.IsType<
                AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                    AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                        group,
                        Assert.Single(group.Participants),
                        token,
                        TestContext.Current.CancellationToken)).Value;
            LibraryBodyAnalysisExecution legacy =
                LibraryBodyAnalysisService.ExecutePath(
                    path,
                    LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures
                            .OptimizationOpportunities,
                        new HashSet<int> { token }));
            try
            {
                Assert.Equal(
                    legacy.CallGraph.DirectCalls.Where(
                        call =>
                            call.EvidenceMethod.MetadataToken
                                == token),
                    actual.DirectCalls);
                MethodSignals expectedSignals =
                    legacy.CallGraph.MethodSignals
                        .GetValueOrDefault(token)
                    ?? MethodSignals.None;
                Assert.Equal(
                    expectedSignals with
                    {
                        EvidenceOffsets = [],
                        ExceptionTypeNames = [],
                    },
                    actual.Signals with
                    {
                        EvidenceOffsets = [],
                        ExceptionTypeNames = [],
                    });
                Assert.Equal(
                    expectedSignals.Evidence,
                    actual.Signals.Evidence);
                Assert.Equal(
                    expectedSignals.ExceptionTypes,
                    actual.Signals.ExceptionTypes);
                ImmutableArray<AllocationOccurrence> expectedAllocations =
                    legacy.Allocations.Occurrences
                        .GetValueOrDefault(token);
                Assert.Equal(
                    expectedAllocations.IsDefault
                        ? []
                        : expectedAllocations,
                    actual.Allocations);
                Assert.Equal(
                    legacy.Optimization.Opportunities.Where(
                        opportunity =>
                            (opportunity.EvidenceMethodToken
                                ?? opportunity.Method.MetadataToken)
                            == token),
                    actual.OptimizationOpportunities);
            }
            finally
            {
                legacy.CallGraph.ReleaseCaches();
            }
        }
    }

    [Fact]
    public async Task
        MethodAnalysis_MatchesLegacyLiftedOwnershipOpportunity()
    {
        string path = typeof(MethodAnalysisProbe).Assembly.Location;
        byte[] image = File.ReadAllBytes(path);
        MethodInfo lifted = typeof(MethodAnalysisProbe)
            .GetMethods(
                BindingFlags.Static
                    | BindingFlags.NonPublic)
            .Single(method => method.Name.Contains(
                "<GenericObjectEqualsLocalFunction>g__EqualsCore|",
                StringComparison.Ordinal));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);

        AssemblyMethodAnalysis actual = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    lifted.MetadataToken,
                    TestContext.Current.CancellationToken)).Value;
        LibraryBodyAnalysisExecution legacy =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    new HashSet<int> { lifted.MetadataToken }));
        try
        {
            OptimizationOpportunity expected = Assert.Single(
                legacy.Optimization.Opportunities,
                opportunity =>
                    opportunity.Shape
                        == "generic-parameter-object-box"
                    && opportunity.Method.MetadataToken
                        == lifted.MetadataToken);
            Assert.Equal(
                expected,
                Assert.Single(
                    actual.OptimizationOpportunities,
                    opportunity =>
                        opportunity.Shape
                            == "generic-parameter-object-box"));
        }
        finally
        {
            legacy.CallGraph.ReleaseCaches();
        }
    }

    [Fact]
    public async Task
        MethodAnalysis_SuppressesGeneratedLiftedOwnerLikeLegacy()
    {
        string path = typeof(MethodAnalysisProbe).Assembly.Location;
        byte[] image = File.ReadAllBytes(path);
        MethodInfo lifted = typeof(MethodAnalysisProbe)
            .GetMethods(
                BindingFlags.Static
                    | BindingFlags.NonPublic)
            .Single(method => method.Name.Contains(
                "<GeneratedGenericObjectEqualsLocalFunction>"
                    + "g__EqualsCore|",
                StringComparison.Ordinal));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);

        AssemblyMethodAnalysis actual = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    lifted.MetadataToken,
                    TestContext.Current.CancellationToken)).Value;
        LibraryBodyAnalysisExecution legacy =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    new HashSet<int> { lifted.MetadataToken }));
        try
        {
            Assert.DoesNotContain(
                legacy.Optimization.Opportunities,
                opportunity =>
                    opportunity.Shape
                        == "generic-parameter-object-box"
                    && opportunity.Method.MetadataToken
                        == lifted.MetadataToken);
            Assert.DoesNotContain(
                actual.OptimizationOpportunities,
                opportunity =>
                    opportunity.Shape
                        == "generic-parameter-object-box");
        }
        finally
        {
            legacy.CallGraph.ReleaseCaches();
        }
    }

    [Fact]
    public async Task MethodAnalysis_DoesNotReturnNeighborBodyEvidence()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        await using var workspace = new InspectionWorkspace();
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
                    token,
                    TestContext.Current.CancellationToken)).Value;

        Assert.Empty(result.Allocations);
        Assert.Empty(result.DirectCalls);
        Assert.Empty(result.ExceptionRegions);
        Assert.Empty(result.OptimizationOpportunities);
    }

    [Fact]
    public async Task MethodAnalysis_KeepsAsyncKickoffAndMoveNextEvidenceSeparate()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ClassicAsyncSiblingFixture).Assembly.Location);
        await using var workspace = new InspectionWorkspace();
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
                    kickoff.MetadataToken,
                    TestContext.Current.CancellationToken)).Value;
        AssemblyMethodAnalysis moveNextAnalysis = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    participant,
                    moveNext.MetadataToken,
                    TestContext.Current.CancellationToken)).Value;

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
            call =>
            {
                Assert.Equal(
                    kickoff.MetadataToken,
                    call.Caller.MetadataToken);
                Assert.Equal(
                    moveNext.MetadataToken,
                    call.EvidenceMethod.MetadataToken);
            });
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
    public async Task MethodAnalysis_UsesPhysicalCallerForGeneratedSignals()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);
        MethodInfo kickoff = typeof(MethodAnalysisProbe)
            .GetMethod(nameof(MethodAnalysisProbe.CopyAfterYield))!;
        MethodInfo moveNext = kickoff
            .GetCustomAttribute<AsyncStateMachineAttribute>()!
            .StateMachineType
            .GetMethod(
                nameof(IAsyncStateMachine.MoveNext),
                BindingFlags.Instance
                    | BindingFlags.NonPublic
                    | BindingFlags.Public)!;

        AssemblyMethodAnalysis analysis = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    moveNext.MetadataToken,
                    TestContext.Current.CancellationToken)).Value;

        Assert.Equal(1, analysis.Signals.Copies);
        Assert.Contains(
            analysis.DirectCalls,
            call =>
                call.Caller.MetadataToken
                    == kickoff.MetadataToken
                && call.EvidenceMethod.MetadataToken
                    == moveNext.MetadataToken
                && call.Callee.Name == nameof(Array.CopyTo));
    }

    [Fact]
    public async Task MethodAnalysis_PreservesDirectCallProvenance()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        int loopToken = typeof(MethodAnalysisProbe)
            .GetMethod(nameof(MethodAnalysisProbe.CallInLoop))!
            .MetadataToken;
        AssemblyMethodAnalysis loop = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    participant,
                    loopToken,
                    TestContext.Current.CancellationToken)).Value;
        Assert.Contains(
            loop.DirectCalls,
            call =>
                call.Callee.Name == nameof(List<object>.Add)
                && call.Multiplicity == AllocationMultiplicity.Loop);

        int genericToken = typeof(MethodAnalysisProbe)
            .GetMethod(nameof(MethodAnalysisProbe.ExternalGenericCall))!
            .MetadataToken;
        AssemblyMethodAnalysis generic = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    participant,
                    genericToken,
                    TestContext.Current.CancellationToken)).Value;
        DirectCall empty = Assert.Single(
            generic.DirectCalls,
            call => call.Callee.Name == nameof(Array.Empty));
        Assert.Equal(
            HandleKind.MethodSpecification,
            MetadataTokens.EntityHandle(empty.OperandToken).Kind);
        Assert.Equal(
            HandleKind.MemberReference,
            MetadataTokens.EntityHandle(
                empty.CalleeDefinitionToken).Kind);
    }

    [Fact]
    public async Task MethodAnalysis_PreservesIndependentEvidenceWhenCallsFail()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        int token = typeof(MethodAnalysisProbe)
            .GetMethod(nameof(MethodAnalysisProbe.RecoverableCallFailure))!
            .MetadataToken;
        CorruptCallOperandToField(
            image,
            token,
            callIndex: 2);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);

        AssemblyMethodAnalysis result = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    token,
                    TestContext.Current.CancellationToken)).Value;

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "call operand is a FieldDefinition",
                StringComparison.Ordinal));
        UnsafetyOccurrence occurrence = Assert.Single(
            result.UnsafetyOccurrences,
            occurrence =>
                occurrence.Kind == UnsafetyKind.StackAlloc);
        Assert.Equal("byte*", occurrence.Detail);
        Assert.Single(result.ExceptionRegions);
        Assert.Contains(
            result.UnsafeEvidence,
            evidence => evidence.Detail == "localloc");
        Assert.Contains(
            result.DirectCalls,
            call => call.Callee.Name
                == nameof(MethodAnalysisProbe.Neighbor));
        Assert.Contains(
            result.OptimizationOpportunities,
            opportunity =>
                opportunity.Shape == "box-value-type");
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public async Task MethodAnalysis_ReturnsExactUnsafetyOccurrences()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);
        int token = typeof(MethodAnalysisProbe)
            .GetMethod(nameof(MethodAnalysisProbe.StackAllocate))!
            .MetadataToken;

        AssemblyMethodAnalysis result = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    token,
                    TestContext.Current.CancellationToken)).Value;

        Assert.True(result.Signals.Unsafe);
        Assert.Contains(
            result.UnsafetyOccurrences,
            occurrence =>
                occurrence.Method.MetadataToken == token
                && occurrence.Kind == UnsafetyKind.StackAlloc);
    }

    [Fact]
    public async Task MethodAnalysis_InvalidTokenIsAVisibleParticipantFailure()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);

        var failed = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Failed>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    0x02000001,
                    TestContext.Current.CancellationToken));

        Assert.Contains("not a MethodDef", failed.Error.Message);
    }

    [Fact]
    public async Task MethodAnalysis_MissingMethodDefIsAVisibleParticipantFailure()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);

        var failed = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Failed>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    0x06FF_FFFF,
                    TestContext.Current.CancellationToken));

        Assert.Contains("not a MethodDef in", failed.Error.Message);
    }

    [Fact]
    public async Task MethodAnalysis_BodylessMethodIsAVisibleParticipantFailure()
    {
        byte[] image = File.ReadAllBytes(
            typeof(IMethodAnalysisProbe).Assembly.Location);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);
        int token = typeof(IMethodAnalysisProbe)
            .GetMethod(nameof(IMethodAnalysisProbe.Bodyless))!
            .MetadataToken;

        var failed = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAnalysis>.Failed>(
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    token,
                    TestContext.Current.CancellationToken));

        Assert.Contains("does not have an IL body", failed.Error.Message);
    }

    [Fact]
    public async Task MethodAnalysis_CancellationPropagatesCallerToken()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodAnalysisProbe).Assembly.Location);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, image);
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        int token = typeof(MethodAnalysisProbe)
            .GetMethod(nameof(MethodAnalysisProbe.Target))!
            .MetadataToken;
        cancellation.Cancel();

#pragma warning disable xUnit1051 // The linked caller token is the contract under test.
        OperationCanceledException error =
            Assert.Throws<OperationCanceledException>(() =>
                AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                    group,
                    Assert.Single(group.Participants),
                    token,
                    cancellation.Token));
#pragma warning restore xUnit1051
        Assert.Equal(
            cancellation.Token,
            error.CancellationToken);
    }

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        byte[] content,
        IAssemblyBindingPolicy? bindingPolicy = null)
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
            bindingPolicy ?? new MissingBindingPolicy());
        return workspace.CreateAssemblyContextGroup([participant]);
    }

    static void CorruptCallOperandToField(
        byte[] image,
        int methodToken,
        int callIndex)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle method =
            MetadataTokens.MethodDefinitionHandle(
                methodToken & 0x00FF_FFFF);
        MethodDefinition definition =
            reader.GetMethodDefinition(method);
        MethodBodyBlock body =
            pe.GetMethodBody(definition.RelativeVirtualAddress);
        DecodedInstruction call =
            InstructionDecoder
                .Decode(body.GetILBytes()!)
                .Where(static instruction =>
                    instruction.OpCode == ILOpCode.Call)
                .ElementAt(callIndex);
        int bodyOffset = RvaToFileOffset(
            pe.PEHeaders,
            definition.RelativeVirtualAddress);
        int headerSize = (image[bodyOffset] & 3) == 2
            ? 1
            : ((BinaryPrimitives.ReadUInt16LittleEndian(
                    image.AsSpan(bodyOffset, sizeof(ushort)))
                >> 12)
                & 0xF) * sizeof(uint);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(
                bodyOffset + headerSize + call.OperandOffset,
                sizeof(int)),
            MetadataTokens.GetToken(
                reader.FieldDefinitions.First()));
    }

    static int RvaToFileOffset(PEHeaders headers, int rva)
    {
        SectionHeader section =
            headers.SectionHeaders.Single(header =>
                rva >= header.VirtualAddress
                && rva < header.VirtualAddress
                    + Math.Max(
                        header.VirtualSize,
                        header.SizeOfRawData));
        return checked(
            rva - section.VirtualAddress + section.PointerToRawData);
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

    public static async Task CopyAfterYield(
        object[] source,
        object[] destination)
    {
        await Task.Yield();
        source.CopyTo(destination, 0);
    }

    public static int StackAllocate(int value)
    {
        Span<int> values = stackalloc int[1];
        values[0] = value;
        return values[0];
    }

    public static void CallInLoop(int count, List<object> sink)
    {
        for (int index = 0; index < count; index++)
            sink.Add(new object());
    }

    public static int[] ExternalGenericCall() => Array.Empty<int>();

    public static string StringConcatenation(
        string first,
        string second) =>
        string.Concat(first, second);

    public static string ObjectTypedStringBuilder(
        StringBuilder builder)
    {
        object value = builder;
        return value.ToString()!;
    }

    [CompilerGenerated]
    public static object CompilerGeneratedBox(int value) => value;

    public static bool GenericObjectEqualsLocalFunction<T>(
        T left,
        T right)
    {
        return EqualsCore(left, right);

        static bool EqualsCore(T x, T y) => x!.Equals(y);
    }

    [System.CodeDom.Compiler.GeneratedCode(
        "dotnet-inspect-tests",
        "1.0")]
    public static bool
        GeneratedGenericObjectEqualsLocalFunction<T>(
            T left,
            T right)
    {
        return EqualsCore(left, right);

        static bool EqualsCore(T x, T y) => x!.Equals(y);
    }

    public static void ExceptionAllocations(
        int count,
        List<Exception> sink)
    {
        for (int index = 0; index < count; index++)
        {
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
            sink.Add(new MethodAnalysisException());
        }
    }

    public static void NamedNonExceptionAllocations(
        int count,
        List<object> sink)
    {
        for (int index = 0; index < count; index++)
        {
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
            sink.Add(new MethodAnalysisPayloadException());
        }
    }

    public static int RecoverableCallFailure(int value)
    {
        try
        {
            Span<int> values = stackalloc int[1];
            _value = value;
            value = Neighbor(value);
            values[0] = Neighbor(value);
            return values[0];
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

}

public sealed class MethodAnalysisException : Exception
{
}

public sealed class MethodAnalysisPayloadException
{
}

public interface IMethodAnalysisProbe
{
    void Bodyless();
}
