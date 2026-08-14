using ILInspector.Instructions;

namespace ILInspector.Analysis.Tests;

public sealed class OptimizationRecognizerAnalysisTests
{
    const int StackGuardToken = 0x0A000001;

    static readonly TypeRef s_bool =
        TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_void =
        TypeRef.CoreLib("System", "Void");

    [Fact]
    public void StackGuardFallbackRecognizesDirectAndStoredInvertedConditions()
    {
        byte[] directIl =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x2D, 0x05,
            0x73, 0x02, 0x00, 0x00, 0x0A,
            0x2A,
        ];
        byte[] storedInvertedIl =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x16,
            0xFE, 0x01,
            0x0A,
            0x06,
            0x2C, 0x05,
            0x73, 0x02, 0x00, 0x00, 0x0A,
            0x2A,
        ];
        var resolver = new Resolver();

        Assert.True(StackGuardFallbackAnalysis.IsFallbackAllocation(
            Context(directIl),
            allocationOffset: 7,
            resolver));
        Assert.True(StackGuardFallbackAnalysis.IsFallbackAllocation(
            Context(storedInvertedIl),
            allocationOffset: 12,
            resolver));
        Assert.False(StackGuardFallbackAnalysis.IsFallbackAllocation(
            Context(directIl),
            allocationOffset: 7,
            new Resolver(resolveStackGuard: false)));
    }

    [Fact]
    public void ArrayFlowDistinguishesLocalUseFromEscape()
    {
        byte[] localIl =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x06,
            0x8E,
            0x26,
            0x2A,
        ];
        byte[] escapingIl =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x06,
            0x2A,
        ];

        Assert.True(
            ArrayEscapeAnalysis.ArrayProvablyStaysLocal(
                Context(localIl),
                ReachingDefinitions.Analyze(localIl, argumentSlotCount: 0),
                positionAfterNewarr: 6));
        Assert.False(
            ArrayEscapeAnalysis.ArrayProvablyStaysLocal(
                Context(escapingIl),
                ReachingDefinitions.Analyze(
                    escapingIl,
                    argumentSlotCount: 0),
                positionAfterNewarr: 6));
    }

    [Fact]
    public void SpanToArrayFlowDistinguishesLocalUseFromEscape()
    {
        byte[] localIl =
        [
            0x28, 0x02, 0x00, 0x00, 0x0A,
            0x0A,
            0x06,
            0x8E,
            0x26,
            0x2A,
        ];
        byte[] escapingIl =
        [
            0x28, 0x02, 0x00, 0x00, 0x0A,
            0x2A,
        ];

        Assert.False(
            ArrayEscapeAnalysis.SpanToArrayResultEscapes(
                Context(localIl),
                ReachingDefinitions.Analyze(localIl, argumentSlotCount: 0),
                positionAfterCall: 5));
        Assert.True(
            ArrayEscapeAnalysis.SpanToArrayResultEscapes(
                Context(escapingIl),
                ReachingDefinitions.Analyze(
                    escapingIl,
                    argumentSlotCount: 0),
                positionAfterCall: 5));
    }

    [Fact]
    public void MaterializerFlowRequiresSourceDefinedOutsideLoop()
    {
        byte[] il =
        [
            0x02,
            0x28, 0x02, 0x00, 0x00, 0x0A,
            0x26,
            0x2A,
        ];

        Assert.True(
            LoopInvariantMaterializerAnalysis.TryGetLoopInvariantSource(
                Context(il, [(1, 6)]),
                ReachingDefinitions.Analyze(
                    il,
                    argumentSlotCount: 1),
                callOffset: 1,
                out var evidence));
        Assert.Equal("arg0", evidence);
        Assert.False(
            LoopInvariantMaterializerAnalysis.TryGetLoopInvariantSource(
                Context(il),
                ReachingDefinitions.Analyze(
                    il,
                    argumentSlotCount: 1),
                callOffset: 1,
                out _));
    }

    [Fact]
    public void StringConcatAccumulationRequiresStoredSourceArgument()
    {
        byte[] accumulatingIl =
        [
            0x06,
            0x72, 0x01, 0x00, 0x00, 0x70,
            0x28, 0x02, 0x00, 0x00, 0x0A,
            0x0A,
            0x2A,
        ];
        byte[] unrelatedStoreIl =
        [
            0x06,
            0x72, 0x01, 0x00, 0x00, 0x70,
            0x28, 0x02, 0x00, 0x00, 0x0A,
            0x0B,
            0x2A,
        ];
        var resolver = new Resolver();

        Assert.True(StringConcatAccumulationAnalysis.AccumulatesIntoSource(
            Context(accumulatingIl),
            concatOffset: 6,
            storeOffset: 11,
            concatArgumentCount: 2,
            resolver));
        Assert.False(StringConcatAccumulationAnalysis.AccumulatesIntoSource(
            Context(unrelatedStoreIl),
            concatOffset: 6,
            storeOffset: 11,
            concatArgumentCount: 2,
            resolver));
    }

    static MethodBodyAnalysisContext Context(
        byte[] il,
        IReadOnlyList<(int Start, int End)>? loopRegions = null)
    {
        var instructions = MethodInstructions.Decode(
            il,
            il.Length,
            []);
        Assert.True(instructions.IsComplete);
        return new MethodBodyAnalysisContext(
            new MethodIdentity(
                "Fixture",
                Guid.Empty,
                TypeRef.Definition(
                    "Fixture",
                    "Fixtures",
                    "Caller"),
                "M",
                [],
                s_void,
                MetadataToken: 0x06000001,
                IsStatic: true),
            instructions,
            [],
            loopRegions ?? [],
            []);
    }

    sealed class Resolver(bool resolveStackGuard = true)
        : IOptimizationOpportunityResolver
    {
        public MemberRef ResolveMember(int token)
            => resolveStackGuard && token == StackGuardToken
                ? new MemberRef(
                    TypeRef.Definition(
                        "Fixture",
                        "Fixtures",
                        "StackGuard"),
                    "TryEnterOnCurrentStack",
                    [],
                    s_bool,
                    MemberKind.Method)
                : MemberRef.Unsupported("member token");

        public TypeRef ResolveType(int token)
            => throw new NotSupportedException();

        public bool IsAllocatingValueTypeBox(
            int operandToken,
            TypeRef boxed)
            => throw new NotSupportedException();

        public bool IsAsyncStateMachineType(TypeRef? type)
            => throw new NotSupportedException();

        public ReachingDefinitionsResult AnalyzeReachingDefinitions()
            => throw new NotSupportedException();
    }
}
