using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class DeferredCallbackCensusTests
{
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef s_intPtr = TypeRef.CoreLib("System", "IntPtr");
    static readonly TypeRef s_action = TypeRef.CoreLib("System", "Action");
    static readonly TypeRef s_type = TypeRef.Definition("Fixture", "Fixtures", "Graph");

    [Fact]
    public void Measure_ClassifiesCompiledRegistrationAndNearMisses()
    {
        var report = DeferredCallbackCensus.Measure(
            [FixtureCatalog.AnalysisRender.AssemblyPath()]);

        var registration = Assert.Single(report.Sites, static site =>
            site.Caller.Contains("::RenderWithDeferredFragmentLoop(", StringComparison.Ordinal));
        Assert.Equal(DeferredCallbackSiteClassification.FrameworkRegistration, registration.Classification);
        Assert.Equal("render-tree-registration", registration.ConsumerKind);
        Assert.False(registration.ConsumptionProven);

        var immediate = Assert.Single(report.Sites, static site =>
            site.Caller.Contains("::InvokeConstructedCallbackInLoop(", StringComparison.Ordinal));
        Assert.Equal(DeferredCallbackSiteClassification.ImmediateInvocation, immediate.Classification);
        Assert.Equal("delegate-invoke", immediate.ConsumerKind);
        Assert.True(immediate.ConsumptionProven);

        Assert.Contains(report.Sites, static site =>
            site.Caller.Contains("::RenderWithUnknownConsumerLoop(", StringComparison.Ordinal)
            && site.Classification == DeferredCallbackSiteClassification.UnknownConsumer
            && !site.ConsumptionProven);
        Assert.Contains(report.Sites, static site =>
            site.Caller.Contains("::BuildLatestFragment(", StringComparison.Ordinal)
            && site.Classification == DeferredCallbackSiteClassification.ConstructedWithoutImmediateConsumer);
        Assert.Contains(report.Sites, static site =>
            site.Caller.Contains("::InvokeLatestFragmentOnce(", StringComparison.Ordinal)
            && site.Classification == DeferredCallbackSiteClassification.ConstructedWithoutImmediateConsumer
            && !site.ConsumptionProven);
        Assert.Contains(report.Sites, static site =>
            site.Caller.Contains("::RenderWithOutsideCallback(", StringComparison.Ordinal)
            && site.Classification == DeferredCallbackSiteClassification.ConstructionOutsideLoop);
        Assert.Contains(report.Sites, static site =>
            site.Caller.Contains("::LoadFunctionPointerInLoop(", StringComparison.Ordinal)
            && site.Classification == DeferredCallbackSiteClassification.StandaloneFunctionLoad);
        Assert.DoesNotContain(report.Sites, static site =>
            site.Caller.Contains("::InvokeDirectlyInLoop(", StringComparison.Ordinal));
    }

    [Fact]
    public void Measure_SeparatesRegisteredOpportunityFromProvenInvocation()
    {
        var report = DeferredCallbackCensus.Measure(
            [FixtureCatalog.AnalysisRender.AssemblyPath()]);

        var registered = Assert.Single(report.Rows, static row =>
            row.Member.Contains("::BoxDeferredValue(", StringComparison.Ordinal));
        Assert.Equal(DeferredCallbackReachClassification.Downstream, registered.Classification);
        Assert.Equal(1, registered.DownstreamDepth);
        Assert.Equal(
            DeferredCallbackSiteClassification.FrameworkRegistration,
            registered.Construction?.Classification);
        Assert.False(registered.Construction?.ConsumptionProven);

        var invoked = Assert.Single(report.Rows, static row =>
            row.Member.Contains("::BoxImmediateValue(", StringComparison.Ordinal));
        Assert.Equal(DeferredCallbackReachClassification.Downstream, invoked.Classification);
        Assert.Equal(1, invoked.DownstreamDepth);
        Assert.Equal(
            DeferredCallbackSiteClassification.ImmediateInvocation,
            invoked.Construction?.Classification);
        Assert.True(invoked.Construction?.ConsumptionProven);
    }

    [Fact]
    public void Analyze_ReportsDownstreamDepthAndPreservesOpportunitySemantics()
    {
        var loop = Method(1, "Loop");
        var callback = Method(2, "Callback");
        var wrapper = Method(3, "Wrapper");
        var target = Method(4, "Target");
        var opportunity = Opportunity(target, "pt~exact") with
        {
            Multiplicity = "conditional",
            Provenance = PerformanceTriageProvenance.Exact,
            PathContext = "error path",
        };
        var result = DeferredCallbackCensus.Analyze(
            "Fixture.dll",
            [loop, callback, wrapper, target],
            [
                FunctionLoad(loop, callback, 4, returnAddress: 10),
                DelegateConstruction(loop, 10, returnAddress: 15),
                DelegateInvoke(loop, 15),
                Call(callback, wrapper, 20),
                Call(wrapper, target, 30),
            ],
            Allocations(DelegateAllocation(loop, 10)),
            [opportunity],
            maxDepth: 1);

        var site = Assert.Single(result.Sites);
        Assert.Equal(DeferredCallbackSiteClassification.ImmediateInvocation, site.Classification);
        Assert.True(site.ConsumptionProven);

        var row = Assert.Single(result.Rows);
        Assert.Equal(DeferredCallbackReachClassification.BeyondBound, row.Classification);
        Assert.Equal(2, row.DownstreamDepth);
        Assert.Equal([20, 30], row.InvocationWitness.Select(static step => step.ILOffset));
        Assert.Equal("pt~exact", row.Candidate);
        Assert.Equal("conditional", row.LocalMultiplicity);
        Assert.False(row.LocalInLoop);
        Assert.Equal(PerformanceTriageProvenance.Exact, row.Provenance);
    }

    [Fact]
    public void Analyze_ClassifiesCachedConstructionWithoutClaimingConsumption()
    {
        var loop = Method(1, "Loop");
        var callback = Method(2, "Callback");
        var result = DeferredCallbackCensus.Analyze(
            "Fixture.dll",
            [loop, callback],
            [
                FunctionLoad(loop, callback, 4, returnAddress: 10),
                DelegateConstruction(loop, 10, returnAddress: 15),
            ],
            Allocations(DelegateAllocation(loop, 10, AllocationFrequency.CachedOnce)),
            []);

        var site = Assert.Single(result.Sites);
        Assert.Equal(DeferredCallbackSiteClassification.CachedConstruction, site.Classification);
        Assert.False(site.ConsumptionProven);
    }

    [Fact]
    public void Analyze_ProvenInvocationTakesPriorityOverUnknownConsumer()
    {
        var unknownCaller = Method(1, "AUnknownCaller");
        var immediateCaller = Method(2, "ZImmediateCaller");
        var callback = Method(3, "Callback");
        var result = DeferredCallbackCensus.Analyze(
            "Fixture.dll",
            [unknownCaller, immediateCaller, callback],
            [
                FunctionLoad(unknownCaller, callback, 4, returnAddress: 10),
                DelegateConstruction(unknownCaller, 10, returnAddress: 15),
                UnknownConsumer(unknownCaller, 15),
                FunctionLoad(immediateCaller, callback, 20, returnAddress: 25),
                DelegateConstruction(immediateCaller, 25, returnAddress: 30),
                DelegateInvoke(immediateCaller, 30),
            ],
            Allocations(
                DelegateAllocation(unknownCaller, 10),
                DelegateAllocation(immediateCaller, 25)),
            [Opportunity(callback, "pt~callback")]);

        var row = Assert.Single(result.Rows);
        Assert.Equal(DeferredCallbackReachClassification.Target, row.Classification);
        Assert.Equal(
            DeferredCallbackSiteClassification.ImmediateInvocation,
            row.Construction?.Classification);
        Assert.True(row.Construction?.ConsumptionProven);
    }

    [Fact]
    public void Measure_ReportsInputFailures()
    {
        var report = DeferredCallbackCensus.Measure(["/not/a/real/assembly.dll"]);

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

    static DirectCall FunctionLoad(
        MethodIdentity caller,
        MethodIdentity callback,
        int offset,
        int returnAddress)
        => Call(caller, callback, offset, CallKind.LoadFunction) with
        {
            InLoop = true,
            ReturnAddress = returnAddress,
        };

    static DirectCall DelegateConstruction(
        MethodIdentity caller,
        int offset,
        int returnAddress)
        => new(
            caller,
            new MemberRef(
                s_action,
                ".ctor",
                [s_object, s_intPtr],
                s_void,
                MemberKind.Constructor)
            {
                HasThis = true,
            },
            offset,
            0,
            0,
            CallKind.NewObject,
            InLoop: true)
        {
            ReturnAddress = returnAddress,
        };

    static DirectCall DelegateInvoke(MethodIdentity caller, int offset)
        => new(
            caller,
            new MemberRef(s_action, "Invoke", [], s_void, MemberKind.Method)
            {
                HasThis = true,
            },
            offset,
            0,
            0,
            CallKind.CallVirtual,
            InLoop: true);

    static DirectCall UnknownConsumer(MethodIdentity caller, int offset)
        => new(
            caller,
            new MemberRef(
                TypeRef.Definition("Fixture", "Fixtures", "Consumer"),
                "Register",
                [s_action],
                s_void,
                MemberKind.Method),
            offset,
            0,
            0,
            CallKind.Call,
            InLoop: true);

    static DirectCall Call(
        MethodIdentity caller,
        MethodIdentity callee,
        int offset,
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
            kind);

    static IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> Allocations(
        params AllocationOccurrence[] occurrences)
        => occurrences
            .GroupBy(static occurrence => occurrence.Method.MetadataToken)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray());

    static AllocationOccurrence DelegateAllocation(
        MethodIdentity method,
        int offset,
        AllocationFrequency frequency = AllocationFrequency.Always)
        => new(
            method,
            offset,
            OperandToken: null,
            AllocationKind.Delegate,
            s_action,
            Detail: null,
            CountsAsHeapAllocation: true,
            frequency,
            InLoop: true,
            AllocationEscape.Unknown,
            AllocationFactSource.Newobj);

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
