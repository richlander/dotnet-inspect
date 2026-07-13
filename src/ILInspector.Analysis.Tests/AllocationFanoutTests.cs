using System.Collections.Immutable;

namespace ILInspector.Analysis.Tests;

public class AllocationFanoutTests
{
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef s_type = TypeRef.Definition("Fixture", "Fixtures", "Graph");

    [Fact]
    public void Analyze_MultipliesRepeatedOnceCallSites()
    {
        var root = Method(1, "Root");
        var leaf = Method(2, "Leaf");

        var rootSummary = Summary(
            [root, leaf],
            [
                Call(root, leaf, 4, AllocationMultiplicity.Once),
                Call(root, leaf, 8, AllocationMultiplicity.Once),
                Call(root, leaf, 12, AllocationMultiplicity.Once),
            ],
            new Dictionary<int, ImmutableArray<AllocationOccurrence>>
            {
                [leaf.MetadataToken] = [Allocation(leaf, AllocationMultiplicity.Once)],
            },
            root);

        Assert.Equal(0, rootSummary.DirectSites);
        Assert.Equal(3, rootSummary.OncePaths);
        Assert.Equal(0, rootSummary.ConditionalPaths);
        Assert.Equal(0, rootSummary.OpaquePaths);
    }

    [Fact]
    public void Analyze_ComposesMultiplicityAcrossEveryEdge()
    {
        var root = Method(1, "Root");
        var conditional = Method(2, "Conditional");
        var repeated = Method(3, "Repeated");
        var unknown = Method(4, "Unknown");
        var leaf = Method(5, "Leaf");

        var rootSummary = Summary(
            [root, conditional, repeated, unknown, leaf],
            [
                Call(root, conditional, 4, AllocationMultiplicity.Conditional),
                Call(root, repeated, 8, AllocationMultiplicity.Loop),
                Call(root, unknown, 12, AllocationMultiplicity.Unknown),
                Call(conditional, leaf, 4, AllocationMultiplicity.Once),
                Call(repeated, leaf, 4, AllocationMultiplicity.Once),
                Call(unknown, leaf, 4, AllocationMultiplicity.Once),
            ],
            new Dictionary<int, ImmutableArray<AllocationOccurrence>>
            {
                [leaf.MetadataToken] = [Allocation(leaf, AllocationMultiplicity.Once)],
            },
            root);

        Assert.Equal(0, rootSummary.OncePaths);
        Assert.Equal(1, rootSummary.ConditionalPaths);
        Assert.Equal(1, rootSummary.RepeatedPaths);
        Assert.Equal(1, rootSummary.UnknownPaths);
    }

    [Fact]
    public void Analyze_SeparatesLocalMultiplicityAndCachedAllocations()
    {
        var method = Method(1, "Root");

        var summary = Summary(
            [method],
            [],
            new Dictionary<int, ImmutableArray<AllocationOccurrence>>
            {
                [method.MetadataToken] =
                [
                    Allocation(method, AllocationMultiplicity.Once),
                    Allocation(method, AllocationMultiplicity.Conditional),
                    Allocation(method, AllocationMultiplicity.Loop),
                    Allocation(method, AllocationMultiplicity.Unknown),
                    Allocation(method, AllocationMultiplicity.Once, cached: true),
                ],
            },
            method);

        Assert.Equal(5, summary.DirectSites);
        Assert.Equal(1, summary.OncePaths);
        Assert.Equal(1, summary.ConditionalPaths);
        Assert.Equal(1, summary.RepeatedPaths);
        Assert.Equal(1, summary.UnknownPaths);
        Assert.Equal(1, summary.CachedSites);
    }

    [Fact]
    public void Analyze_DeduplicatesCachedSitesAcrossRepeatedCallPaths()
    {
        var root = Method(1, "Root");
        var leaf = Method(2, "Leaf");

        var rootSummary = Summary(
            [root, leaf],
            [
                Call(root, leaf, 4, AllocationMultiplicity.Once),
                Call(root, leaf, 8, AllocationMultiplicity.Once),
                Call(root, leaf, 12, AllocationMultiplicity.Once),
            ],
            new Dictionary<int, ImmutableArray<AllocationOccurrence>>
            {
                [leaf.MetadataToken] = [Allocation(leaf, AllocationMultiplicity.Once, cached: true)],
            },
            root);

        Assert.Equal(1, rootSummary.CachedSites);
        Assert.Equal(0, rootSummary.OncePaths);
    }

    [Fact]
    public void Analyze_LeavesUnprovenTargetsOpaque()
    {
        var root = Method(1, "Root");
        var leaf = Method(2, "Leaf");
        var opaque = Call(root, leaf, 4, AllocationMultiplicity.Once) with { ExactTarget = false };

        var rootSummary = Summary(
            [root, leaf],
            [opaque],
            new Dictionary<int, ImmutableArray<AllocationOccurrence>>
            {
                [root.MetadataToken] = [Allocation(root, AllocationMultiplicity.Once)],
                [leaf.MetadataToken] = [Allocation(leaf, AllocationMultiplicity.Once)],
            },
            root);

        Assert.Equal(1, rootSummary.OncePaths);
        Assert.Equal(1, rootSummary.OpaquePaths);
    }

    [Fact]
    public void Analyze_TerminatesRecursiveComponentsWithoutInventingCounts()
    {
        var first = Method(1, "First");
        var second = Method(2, "Second");

        var summaries = AllocationFanout.Analyze(
            [first, second],
            [
                Call(first, second, 4, AllocationMultiplicity.Once),
                Call(second, first, 4, AllocationMultiplicity.Once),
            ],
            new Dictionary<int, ImmutableArray<AllocationOccurrence>>
            {
                [first.MetadataToken] = [Allocation(first, AllocationMultiplicity.Once)],
                [second.MetadataToken] = [Allocation(second, AllocationMultiplicity.Once)],
            });

        Assert.All(summaries, summary =>
        {
            Assert.Equal(1, summary.OncePaths);
            Assert.Equal(1, summary.OpaquePaths);
        });
    }

    [Fact]
    public void Analyze_HandlesDeepCallGraphsWithoutRecursingOnTheNativeStack()
    {
        const int count = 20_000;
        var methods = Enumerable.Range(1, count)
            .Select(token => Method(token, $"Method{token}"))
            .ToImmutableArray();
        var calls = Enumerable.Range(0, count - 1)
            .Select(index => Call(methods[index], methods[index + 1], 4, AllocationMultiplicity.Once))
            .ToImmutableArray();

        var summaries = AllocationFanout.Analyze(
            methods,
            calls,
            new Dictionary<int, ImmutableArray<AllocationOccurrence>>
            {
                [methods[^1].MetadataToken] = [Allocation(methods[^1], AllocationMultiplicity.Once)],
            });

        var root = Assert.Single(summaries, summary => summary.Method.MetadataToken == methods[0].MetadataToken);
        Assert.Equal(1, root.OncePaths);
    }

    static AllocationFanoutSummary Summary(
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<DirectCall> calls,
        IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> occurrences,
        MethodIdentity method)
        => Assert.Single(
            AllocationFanout.Analyze(methods, calls, occurrences),
            summary => summary.Method.MetadataToken == method.MetadataToken);

    static MethodIdentity Method(int token, string name)
        => new(
            "Fixture",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            s_type,
            name,
            [],
            s_void,
            token,
            IsStatic: true);

    static DirectCall Call(
        MethodIdentity caller,
        MethodIdentity callee,
        int offset,
        AllocationMultiplicity multiplicity)
        => new(
            caller,
            new MemberRef(callee.DeclaringType, callee.Name, callee.ParameterTypes, callee.ReturnType, MemberKind.Method),
            offset,
            callee.MetadataToken,
            callee.MetadataToken,
            CallKind.Call)
        {
            ExactTarget = true,
            Multiplicity = multiplicity,
        };

    static AllocationOccurrence Allocation(
        MethodIdentity method,
        AllocationMultiplicity multiplicity,
        bool cached = false)
        => new(
            method,
            ILOffset: 4,
            OperandToken: null,
            AllocationKind.Object,
            s_object,
            "System.Object",
            CountsAsHeapAllocation: true,
            cached ? AllocationFrequency.CachedOnce : AllocationFrequency.Always,
            InLoop: multiplicity == AllocationMultiplicity.Loop,
            AllocationEscape.Unknown,
            AllocationFactSource.Newobj)
        {
            Multiplicity = multiplicity,
        };
}
