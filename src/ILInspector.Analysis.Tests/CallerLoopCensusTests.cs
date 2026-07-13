using System.Collections.Immutable;

using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class CallerLoopCensusTests
{
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_type = TypeRef.Definition("Fixture", "Fixtures", "Graph");

    [Fact]
    public void Analyze_ClassifiesDirectAndTransitiveWitnesses()
    {
        var loop = Method(1, "Loop");
        var wrapper = Method(2, "Wrapper");
        var target = Method(3, "Target");
        var rows = CallerLoopCensus.Analyze(
            "Fixture.dll",
            [loop, wrapper, target],
            [Call(loop, wrapper, 4, inLoop: true), Call(wrapper, target, 8)],
            [Opportunity(wrapper, "c1"), Opportunity(target, "c2")],
            maxDepth: 4);

        Assert.Equal(CallerLoopClassification.Direct, rows[0].Classification);
        Assert.Equal(1, rows[0].NearestDepth);
        Assert.Equal(CallerLoopClassification.Transitive, rows[1].Classification);
        Assert.Equal(2, rows[1].NearestDepth);
        Assert.Equal([4, 8], rows[1].Witness.Select(static step => step.ILOffset));
    }

    [Fact]
    public void Analyze_RejectsNonInvocationAndNonLoopEdges()
    {
        var caller = Method(1, "Caller");
        var functionLoadTarget = Method(2, "FunctionLoadTarget");
        var straightTarget = Method(3, "StraightTarget");
        var rows = CallerLoopCensus.Analyze(
            "Fixture.dll",
            [caller, functionLoadTarget, straightTarget],
            [
                Call(caller, functionLoadTarget, 4, inLoop: true, CallKind.LoadFunction),
                Call(caller, straightTarget, 8),
            ],
            [Opportunity(functionLoadTarget, "c1"), Opportunity(straightTarget, "c2")]);

        Assert.All(rows, static row => Assert.Equal(CallerLoopClassification.None, row.Classification));
    }

    [Fact]
    public void Analyze_DoesNotTreatSelfRecursionAsCallerLoop()
    {
        var recursive = Method(1, "Recursive");
        var row = Assert.Single(CallerLoopCensus.Analyze(
            "Fixture.dll",
            [recursive],
            [Call(recursive, recursive, 4, inLoop: true)],
            [Opportunity(recursive, "c1")]));

        Assert.Equal(CallerLoopClassification.None, row.Classification);
    }

    [Fact]
    public void Analyze_ReportsWitnessBeyondConfiguredBound()
    {
        var loop = Method(1, "Loop");
        var first = Method(2, "First");
        var second = Method(3, "Second");
        var target = Method(4, "Target");
        var row = Assert.Single(CallerLoopCensus.Analyze(
            "Fixture.dll",
            [loop, first, second, target],
            [
                Call(loop, first, 4, inLoop: true),
                Call(first, second, 8),
                Call(second, target, 12),
            ],
            [Opportunity(target, "c1")],
            maxDepth: 2));

        Assert.Equal(CallerLoopClassification.BeyondBound, row.Classification);
        Assert.Equal(3, row.NearestDepth);
        Assert.Equal(3, row.Witness.Count);
    }

    [Fact]
    public void Analyze_UsesStableShortestWitnessTieBreak()
    {
        var zCaller = Method(1, "ZCaller");
        var aCaller = Method(2, "ACaller");
        var target = Method(3, "Target");
        var row = Assert.Single(CallerLoopCensus.Analyze(
            "Fixture.dll",
            [zCaller, aCaller, target],
            [
                Call(zCaller, target, 4, inLoop: true),
                Call(aCaller, target, 12, inLoop: true),
            ],
            [Opportunity(target, "c1")]));

        Assert.Contains("ACaller", Assert.Single(row.Witness).Caller, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_CycleTerminatesAtNearestDepth()
    {
        var loop = Method(1, "Loop");
        var first = Method(2, "First");
        var second = Method(3, "Second");
        var row = Assert.Single(CallerLoopCensus.Analyze(
            "Fixture.dll",
            [loop, first, second],
            [
                Call(loop, first, 4, inLoop: true),
                Call(first, second, 8),
                Call(second, first, 12),
            ],
            [Opportunity(second, "c1")]));

        Assert.Equal(CallerLoopClassification.Transitive, row.Classification);
        Assert.Equal(2, row.NearestDepth);
    }

    [Fact]
    public void Analyze_DoesNotPropagateRecursiveCycleBackToLoopOwner()
    {
        var loopOwner = Method(1, "LoopOwner");
        var wrapper = Method(2, "Wrapper");
        var row = Assert.Single(CallerLoopCensus.Analyze(
            "Fixture.dll",
            [loopOwner, wrapper],
            [
                Call(loopOwner, wrapper, 4, inLoop: true),
                Call(wrapper, loopOwner, 8),
            ],
            [Opportunity(loopOwner, "c1")]));

        Assert.Equal(CallerLoopClassification.None, row.Classification);
    }

    [Fact]
    public void Analyze_PreservesCandidateAndLocalSemantics()
    {
        var loop = Method(1, "Loop");
        var target = Method(2, "Target");
        var opportunity = Opportunity(target, "pt~exact") with
        {
            Multiplicity = "conditional",
            Provenance = PerformanceTriageProvenance.Exact,
            PathContext = "error path",
        };
        var row = Assert.Single(CallerLoopCensus.Analyze(
            "Fixture.dll",
            [loop, target],
            [Call(loop, target, 4, inLoop: true)],
            [opportunity]));

        Assert.Equal("pt~exact", row.Candidate);
        Assert.Equal("conditional", row.LocalMultiplicity);
        Assert.False(row.LocalInLoop);
        Assert.Equal("error path", row.Path);
        Assert.Equal(PerformanceTriageProvenance.Exact, row.Provenance);
    }

    [Fact]
    public void Measure_ReportsInputFailures()
    {
        var report = CallerLoopCensus.Measure(["/not/a/real/assembly.dll"]);

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
            new MemberRef(callee.DeclaringType, callee.Name, callee.ParameterTypes, callee.ReturnType, MemberKind.Method),
            offset,
            callee.MetadataToken,
            callee.MetadataToken,
            kind,
            inLoop);

    static OptimizationOpportunity Opportunity(MethodIdentity method, string candidate)
        => new(
            method,
            "instance-method-group-delegate",
            "evidence",
            "fix",
            "low",
            InLoop: false,
            ILOffset: 4,
            Caveat: null)
        {
            CandidateId = candidate,
            Multiplicity = "once",
            Provenance = PerformanceTriageProvenance.Exact,
        };
}
