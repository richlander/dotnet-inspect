using ILInspector.Instructions;

namespace ILInspector.Analysis.Tests;

public sealed class OptimizationOpportunityAnalysisTests
{
    const int BitConverterToken = 0x0A000001;
    const int EnumeratorToken = 0x0A000002;
    const int SpanToArrayToken = 0x0A000003;
    const int MaterializerToken = 0x0A000004;

    static readonly TypeRef s_int =
        TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_void =
        TypeRef.CoreLib("System", "Void");

    [Fact]
    public void MemberResolutionDrivesOpportunitiesInInstructionOrder()
    {
        byte[] il =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x26,
            0x6F, 0x02, 0x00, 0x00, 0x0A,
            0x26,
            0x6F, 0x02, 0x00, 0x00, 0x0A,
            0x26,
            0x2A,
        ];
        var context = Context(il, [(6, 11)]);
        var resolver = new Resolver(il);

        var opportunities = OptimizationOpportunityAnalysis.Collect(
            context,
            [],
            new MethodAllocationAnalysis(context),
            resolver);

        Assert.Equal(
            [
                BitConverterToken,
                EnumeratorToken,
                EnumeratorToken,
            ],
            resolver.ResolvedTokens);
        Assert.Collection(
            opportunities,
            opportunity =>
            {
                Assert.Equal(
                    "temporary-byte-array-copy",
                    opportunity.Shape);
                Assert.Equal(0, opportunity.ILOffset);
            },
            opportunity =>
            {
                Assert.Equal(
                    "enumerator-allocation",
                    opportunity.Shape);
                Assert.Equal(6, opportunity.ILOffset);
            });
    }

    [Fact]
    public void ReachingDefinitionsAreLazyAndMemoized()
    {
        byte[] unrelatedIl =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x26,
            0x2A,
        ];
        var unrelatedContext = Context(unrelatedIl);
        var unrelatedResolver = new Resolver(unrelatedIl);

        var unrelated = OptimizationOpportunityAnalysis.Collect(
            unrelatedContext,
            [],
            new MethodAllocationAnalysis(unrelatedContext),
            unrelatedResolver);

        Assert.Single(unrelated);
        Assert.Equal(0, unrelatedResolver.ReachingDefinitionsCalls);

        byte[] outsideLoopMaterializerIl =
        [
            0x28, 0x04, 0x00, 0x00, 0x0A,
            0x26,
            0x2A,
        ];
        var outsideLoopMaterializerContext =
            Context(outsideLoopMaterializerIl);
        var outsideLoopMaterializerResolver =
            new Resolver(outsideLoopMaterializerIl);

        var outsideLoopMaterializer =
            OptimizationOpportunityAnalysis.Collect(
                outsideLoopMaterializerContext,
                [],
                new MethodAllocationAnalysis(
                    outsideLoopMaterializerContext),
                outsideLoopMaterializerResolver);

        Assert.Empty(outsideLoopMaterializer);
        Assert.Equal(
            0,
            outsideLoopMaterializerResolver.ReachingDefinitionsCalls);

        byte[] spanCopiesIl =
        [
            0x28, 0x03, 0x00, 0x00, 0x0A,
            0x26,
            0x28, 0x03, 0x00, 0x00, 0x0A,
            0x26,
            0x2A,
        ];
        var spanContext = Context(spanCopiesIl);
        var spanResolver = new Resolver(spanCopiesIl);

        var spanCopies = OptimizationOpportunityAnalysis.Collect(
            spanContext,
            [],
            new MethodAllocationAnalysis(spanContext),
            spanResolver);

        Assert.Empty(spanCopies);
        Assert.Equal(1, spanResolver.ReachingDefinitionsCalls);
    }

    [Fact]
    public void LaterResolverFailurePublishesNoPartialResult()
    {
        byte[] il =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x26,
            0x28, 0x02, 0x00, 0x00, 0x0A,
            0x26,
            0x2A,
        ];
        var context = Context(il);
        var resolver = new Resolver(
            il,
            throwOnToken: EnumeratorToken);

        var exception = Assert.Throws<BadImageFormatException>(
            () => OptimizationOpportunityAnalysis.Collect(
                context,
                [],
                new MethodAllocationAnalysis(context),
                resolver));

        Assert.Equal(
            "Malformed member token.",
            exception.Message);
        Assert.Equal(
            [BitConverterToken, EnumeratorToken],
            resolver.ResolvedTokens);
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

    sealed class Resolver(
        byte[] il,
        int? throwOnToken = null)
        : IOptimizationOpportunityResolver
    {
        public List<int> ResolvedTokens { get; } = [];

        public int ReachingDefinitionsCalls { get; private set; }

        public MemberRef ResolveMember(int token)
        {
            ResolvedTokens.Add(token);
            if (token == throwOnToken)
            {
                throw new BadImageFormatException(
                    "Malformed member token.");
            }
            return token switch
            {
                BitConverterToken => new MemberRef(
                    TypeRef.CoreLib(
                        "System",
                        "BitConverter"),
                    "GetBytes",
                    [s_int],
                    TypeRef.SzArray(
                        TypeRef.CoreLib(
                            "System",
                            "Byte")),
                    MemberKind.Method),
                EnumeratorToken => new MemberRef(
                    TypeRef.GenericInstance(
                        TypeRef.CoreLib(
                            "System.Collections.Generic",
                            "IEnumerable`1"),
                        [s_int]),
                    "GetEnumerator",
                    [],
                    TypeRef.GenericInstance(
                        TypeRef.CoreLib(
                            "System.Collections.Generic",
                            "IEnumerator`1"),
                        [s_int]),
                    MemberKind.Method)
                {
                    HasThis = true,
                },
                SpanToArrayToken => new MemberRef(
                    TypeRef.GenericInstance(
                        TypeRef.CoreLib(
                            "System",
                            "ReadOnlySpan`1"),
                        [s_int]),
                    "ToArray",
                    [],
                    TypeRef.SzArray(s_int),
                    MemberKind.Method)
                {
                    HasThis = true,
                },
                MaterializerToken => new MemberRef(
                    TypeRef.Definition(
                        "System.Linq",
                        "System.Linq",
                        "Enumerable"),
                    "ToArray",
                    [
                        TypeRef.GenericInstance(
                            TypeRef.CoreLib(
                                "System.Collections.Generic",
                                "IEnumerable`1"),
                            [s_int]),
                    ],
                    TypeRef.SzArray(s_int),
                    MemberKind.Method)
                {
                    GenericArity = 1,
                    TypeArguments = [s_int],
                },
                _ => MemberRef.Unsupported(
                    "member token"),
            };
        }

        public TypeRef ResolveType(int token)
            => TypeRef.Unsupported("type token");

        public bool IsAllocatingValueTypeBox(
            int operandToken,
            TypeRef boxed)
            => false;

        public bool GenericParameterCanBeValueType(
            TypeRef genericParameter)
            => false;

        public bool IsStableReceiverGetter(
            DecodedInstruction instruction)
            => false;

        public bool IsAsyncStateMachineType(TypeRef? type)
            => false;

        public ReachingDefinitionsResult AnalyzeReachingDefinitions()
        {
            ReachingDefinitionsCalls++;
            return ReachingDefinitions.Analyze(
                il,
                argumentSlotCount: 0);
        }
    }
}
