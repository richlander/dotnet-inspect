using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

public sealed class DirectCallTargetTests
{
    static LibraryCallGraphAnalysisResult CallGraph() =>
        LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog
                    .Get(FixtureIds.AnalysisCalleeResolution)
                    .AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence))
            .CallGraph;

    static DirectCall Call(
        LibraryCallGraphAnalysisResult graph,
        string callerType,
        string caller,
        string callee) =>
        Assert.Single(
            graph.DirectCalls,
            call => call.Caller.DeclaringType.Name == callerType
                && call.Caller.Name == caller
                && call.Callee.Name == callee);

    static bool IsMemberReferenceToken(int token) =>
        (uint)token >> 24 == 0x0A;

    [Theory]
    [InlineData("Consumer", "UseClosedGeneric", "Get", "Box`1")]
    [InlineData("Consumer", "UseClosedGeneric", ".ctor", "Box`1")]
    [InlineData("Consumer", "UseOpenGeneric", "GetTwice", "Box`1")]
    [InlineData("Consumer", "CreatePair", "Create", "Pair`2")]
    public void GenericInstantiationCalls_ResolveToCurrentModuleDefinitions(
        string callerType,
        string caller,
        string callee,
        string targetType)
    {
        LibraryCallGraphAnalysisResult graph = CallGraph();
        DirectCall call = Call(graph, callerType, caller, callee);

        // The raw definition token is a MemberRef, so raw-token matching
        // against DeclaredMethods misses this call.
        Assert.True(IsMemberReferenceToken(call.CalleeDefinitionToken));
        Assert.DoesNotContain(
            graph.DeclaredMethods,
            method => method.MetadataToken == call.CalleeDefinitionToken);

        DirectCallTarget.CurrentModule target =
            Assert.IsType<DirectCallTarget.CurrentModule>(
                graph.ResolveTarget(call));
        Assert.Equal(targetType, target.Method.DeclaringType.Name);
        Assert.Equal(callee, target.Method.Name);
        Assert.Contains(target.Method, graph.DeclaredMethods);
    }

    [Fact]
    public void GenericSelfCall_ResolvesThroughOpenInstantiation()
    {
        LibraryCallGraphAnalysisResult graph = CallGraph();
        DirectCall[] calls =
        [
            .. graph.DirectCalls.Where(call =>
                call.Caller.Name == "GetTwice"
                && call.Callee.Name == "Get"),
        ];

        Assert.Equal(2, calls.Length);
        foreach (DirectCall call in calls)
        {
            Assert.True(IsMemberReferenceToken(call.CalleeDefinitionToken));
            DirectCallTarget.CurrentModule target =
                Assert.IsType<DirectCallTarget.CurrentModule>(
                    graph.ResolveTarget(call));
            Assert.Equal("Box`1", target.Method.DeclaringType.Name);
        }
    }

    [Fact]
    public void MethodDefinitionCall_ResolvesByToken()
    {
        LibraryCallGraphAnalysisResult graph = CallGraph();
        DirectCall call = Call(graph, "Consumer", "UseDirect", "Value");

        DirectCallTarget.CurrentModule target =
            Assert.IsType<DirectCallTarget.CurrentModule>(
                graph.ResolveTarget(call));
        Assert.Equal(call.CalleeDefinitionToken, target.Method.MetadataToken);
    }

    [Fact]
    public void ExternalGenericInstantiation_KeepsExactReferencedAssembly()
    {
        LibraryCallGraphAnalysisResult graph = CallGraph();
        DirectCall call = Call(graph, "Consumer", "UseExternal", "Add");

        DirectCallTarget.External target =
            Assert.IsType<DirectCallTarget.External>(
                graph.ResolveTarget(call));
        TypeReferenceOrigin.AssemblyReference reference =
            Assert.IsType<TypeReferenceOrigin.AssemblyReference>(
                target.Origin);
        Assert.StartsWith("System.", reference.Assembly.Name);
    }

    [Fact]
    public void ArrayAccessor_IsRuntimeProvided()
    {
        LibraryCallGraphAnalysisResult graph = CallGraph();
        DirectCall call = Call(graph, "Consumer", "UseArray", "Get");

        Assert.IsType<DirectCallTarget.RuntimeProvided>(
            graph.ResolveTarget(call));
    }

    [Fact]
    public void FunctionPointerCall_IsUnresolvedIndirect()
    {
        LibraryCallGraphAnalysisResult graph = CallGraph();
        DirectCall call = Assert.Single(
            graph.DirectCalls,
            call => call.Caller.Name == "UseIndirect"
                && call.Kind == CallKind.CallIndirect);

        DirectCallTarget.Unresolved target =
            Assert.IsType<DirectCallTarget.Unresolved>(
                graph.ResolveTarget(call));
        Assert.Equal(DirectCallTargetUnresolvedReason.Indirect, target.Reason);
    }

    [Fact]
    public void LiftedLambdaTarget_MapsToDeclaredSourceOnFocusedResult()
    {
        LibraryCallGraphAnalysisResult graph = CallGraph();
        DirectCall load = Assert.Single(
            graph.DirectCalls,
            call => call.Caller.Name == "UseLambda"
                && call.Kind == CallKind.LoadFunction);

        DirectCallTarget.CurrentModule target =
            Assert.IsType<DirectCallTarget.CurrentModule>(
                graph.ResolveTarget(load));
        Assert.NotEqual("Consumer", target.Method.DeclaringType.Name);

        MethodIdentity source = Assert.IsType<MethodIdentity>(
            graph.ResolveDeclaredMethod(target.Method));
        Assert.Equal("UseLambda", source.Name);
        Assert.Equal("Consumer", source.DeclaringType.Name);
        Assert.Null(graph.ResolveDeclaredMethod(source));

        DirectCall bodyCall = Assert.Single(
            graph.DirectCalls,
            call => call.Callee.Name == "Value"
                && call.EvidenceMethod.MetadataToken
                    == target.Method.MetadataToken);
        Assert.Equal(source, bodyCall.Caller);
    }

    [Fact]
    public void EveryCall_ReceivesExactlyOneTypedTarget()
    {
        LibraryCallGraphAnalysisResult graph = CallGraph();

        foreach (DirectCall call in graph.DirectCalls)
        {
            DirectCallTarget target = graph.ResolveTarget(call);
            if (target is DirectCallTarget.CurrentModule current)
                Assert.Contains(current.Method, graph.DeclaredMethods);
        }
        Assert.DoesNotContain(
            graph.DirectCalls,
            call => graph.ResolveTarget(call) is DirectCallTarget.Unresolved
            {
                Reason: not DirectCallTargetUnresolvedReason.Indirect,
            });
    }
}
