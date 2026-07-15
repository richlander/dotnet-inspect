using DotnetInspector.Fixtures;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class RecursiveTraversalCensusTests
{
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_type = TypeRef.Definition("Fixture", "Fixtures", "Graph");

    [Fact]
    public void Measure_ClassifiesCompiledTraversalAndNearMisses()
    {
        string assemblyPath = FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        var report = RecursiveTraversalCensus.Measure([assemblyPath]);
        var index = LibraryBodyIndex.Open(assemblyPath);

        Assert.Contains(index.DirectCalls, static call =>
            call.Caller.Name == "TraverseVirtually"
            && call.Callee.Name == "TraverseVirtually"
            && call.Kind == CallKind.CallVirtual
            && call.InLoop);

        Assert.Contains(report.Roots, static root =>
            root.Method.Contains("::TraverseRecursively(", StringComparison.Ordinal));
        Assert.Contains(report.Roots, static root =>
            root.Method.Contains("::TraverseConditionally(", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Roots, static root =>
            root.Method.Contains("::RecursiveBox(", StringComparison.Ordinal)
            || root.Method.Contains("::TraverseMutual", StringComparison.Ordinal)
            || root.Method.Contains("::TraverseVirtually(", StringComparison.Ordinal)
            || root.Method.Contains("::LoadSelfFunctionInLoop(", StringComparison.Ordinal));

        var direct = Assert.Single(report.Rows, static row =>
            row.Member.Contains("::BuildTraversalNode(", StringComparison.Ordinal));
        Assert.Equal(RecursiveTraversalClassification.Direct, direct.Classification);
        Assert.Equal(1, direct.DownstreamDepth);
        Assert.Equal("once", direct.LocalMultiplicity);
        Assert.False(direct.LocalInLoop);
        Assert.Contains("::TraverseRecursively(", direct.Root?.Method, StringComparison.Ordinal);
        Assert.Equal(2, direct.Witness.Count);
        Assert.True(direct.Witness[0].InLoop);
        Assert.False(direct.Witness[1].InLoop);

        var conditional = Assert.Single(report.Rows, static row =>
            row.Member.Contains("::BuildConditionalTraversalNode(", StringComparison.Ordinal));
        Assert.Equal(RecursiveTraversalClassification.Direct, conditional.Classification);
        Assert.Equal("conditional", conditional.LocalMultiplicity);
        Assert.False(conditional.LocalInLoop);

        Assert.DoesNotContain(report.Rows, static row =>
            (row.Member.Contains("::BuildMutualTraversalNode(", StringComparison.Ordinal)
            || row.Member.Contains("::BuildVirtualTraversalNode(", StringComparison.Ordinal))
            && row.Classification != RecursiveTraversalClassification.None);
    }

    [Fact]
    public void Analyze_ReportsBoundedDepthAndPreservesOpportunitySemantics()
    {
        var root = Method(1, "Root");
        var wrapper = Method(2, "Wrapper");
        var target = Method(3, "Target");
        var opportunity = Opportunity(target, "pt~exact") with
        {
            Multiplicity = "conditional",
            Provenance = PerformanceTriageProvenance.Exact,
            PathContext = "error path",
        };
        var result = RecursiveTraversalCensus.Analyze(
            "Fixture.dll",
            [root, wrapper, target],
            [
                Call(root, wrapper, 4),
                Call(root, root, 8, inLoop: true),
                Call(wrapper, target, 12),
            ],
            [opportunity],
            maxDepth: 1);

        var traversal = Assert.Single(result.Roots);
        Assert.Equal(8, traversal.RecursionOffset);

        var row = Assert.Single(result.Rows);
        Assert.Equal(RecursiveTraversalClassification.BeyondBound, row.Classification);
        Assert.Equal(2, row.DownstreamDepth);
        Assert.Equal([8, 4, 12], row.Witness.Select(static step => step.ILOffset));
        Assert.Equal("pt~exact", row.Candidate);
        Assert.Equal("conditional", row.LocalMultiplicity);
        Assert.False(row.LocalInLoop);
        Assert.Equal(PerformanceTriageProvenance.Exact, row.Provenance);
    }

    [Fact]
    public void Analyze_RejectsNonBranchingRecursionAndFunctionLoads()
    {
        var recursive = Method(1, "Recursive");
        var mutualA = Method(2, "MutualA");
        var mutualB = Method(3, "MutualB");
        var functionTarget = Method(4, "FunctionTarget");
        var virtualRecursive = Method(5, "VirtualRecursive");
        var result = RecursiveTraversalCensus.Analyze(
            "Fixture.dll",
            [recursive, mutualA, mutualB, functionTarget, virtualRecursive],
            [
                Call(recursive, recursive, 4),
                Call(mutualA, mutualB, 8, inLoop: true),
                Call(mutualB, mutualA, 12),
                Call(functionTarget, functionTarget, 16, inLoop: true, CallKind.LoadFunction),
                Call(virtualRecursive, virtualRecursive, 20, inLoop: true, CallKind.CallVirtual),
            ],
            [
                Opportunity(recursive, "pt~recursive"),
                Opportunity(mutualA, "pt~mutual"),
                Opportunity(functionTarget, "pt~function"),
                Opportunity(virtualRecursive, "pt~virtual"),
            ]);

        Assert.Empty(result.Roots);
        Assert.All(result.Rows, static row =>
            Assert.Equal(RecursiveTraversalClassification.None, row.Classification));
    }

    [Fact]
    public void Analyze_UsesStableRecursionSite()
    {
        var root = Method(1, "Root");
        var row = Assert.Single(RecursiveTraversalCensus.Analyze(
            "Fixture.dll",
            [root],
            [
                Call(root, root, 12, inLoop: true),
                Call(root, root, 4, inLoop: true),
            ],
            [Opportunity(root, "pt~root")]).Rows);

        Assert.Equal(RecursiveTraversalClassification.TraversalRoot, row.Classification);
        Assert.Equal(0, row.DownstreamDepth);
        Assert.Equal(4, row.Root?.RecursionOffset);
        Assert.Equal(4, Assert.Single(row.Witness).ILOffset);
    }

    [Fact]
    public void Measure_ReportsInputFailures()
    {
        var report = RecursiveTraversalCensus.Measure(["/not/a/real/assembly.dll"]);

        Assert.Equal(0, report.Opened);
        Assert.Equal(1, report.Failed);
        Assert.Equal(report.Assemblies, report.Opened + report.Failed);
        Assert.Single(report.Failures);
        Assert.Contains("assembly.dll", report.Failures[0].AssemblyPath, StringComparison.Ordinal);
    }

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
        bool inLoop = false,
        CallKind kind = CallKind.Call)
        => new(
            caller,
            new MemberRef(
                callee.DeclaringType,
                callee.Name,
                callee.ParameterTypes,
                callee.ReturnType,
                MemberKind.Method),
            offset,
            callee.MetadataToken,
            callee.MetadataToken,
            kind,
            inLoop);

    static OptimizationOpportunity Opportunity(MethodIdentity method, string candidate)
        => new(
            method,
            "box-value-type",
            "evidence",
            "fix",
            "medium",
            InLoop: false,
            ILOffset: 4,
            Caveat: null)
        {
            CandidateId = candidate,
            Multiplicity = "once",
            Provenance = PerformanceTriageProvenance.Exact,
        };
}
