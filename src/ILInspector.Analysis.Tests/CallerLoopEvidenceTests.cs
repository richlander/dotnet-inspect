namespace ILInspector.Analysis.Tests;

public class CallerLoopEvidenceTests
{
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_type = TypeRef.Definition("Fixture", "Fixtures", "Graph");

    [Fact]
    public void FindNearest_DepthOneReturnsOnlyDirectInvocations()
    {
        var loop = Method(1, "Loop");
        var wrapper = Method(2, "Wrapper");
        var target = Method(3, "Target");

        var evidence = CallerLoopEvidenceAnalysis.FindNearest(
            [loop, wrapper, target],
            [Call(loop, wrapper, 4, inLoop: true), Call(wrapper, target, 8)],
            maxDepth: 1);

        var direct = Assert.Contains(wrapper.MetadataToken, evidence);
        Assert.Equal(1, direct.Depth);
        Assert.Equal(4, Assert.Single(direct.Witness).ILOffset);
        Assert.DoesNotContain(target.MetadataToken, evidence);
    }

    [Fact]
    public void FindNearest_RejectsFunctionLoadsAndSelfRecursion()
    {
        var caller = Method(1, "Caller");
        var functionTarget = Method(2, "FunctionTarget");
        var recursive = Method(3, "Recursive");

        var evidence = CallerLoopEvidenceAnalysis.FindNearest(
            [caller, functionTarget, recursive],
            [
                Call(caller, functionTarget, 4, inLoop: true, CallKind.LoadFunction),
                Call(recursive, recursive, 8, inLoop: true),
            ],
            maxDepth: 1);

        Assert.Empty(evidence);
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
}
