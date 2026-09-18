using System.Collections.Immutable;

namespace ILInspector.Analysis.Tests;

public sealed class SynchronousCompletionAnalysisTests
{
    [Theory]
    [InlineData(
        "System.Threading.Tasks",
        "Task",
        "Wait",
        SynchronousCompletionKind.TaskWait)]
    [InlineData(
        "System.Threading.Tasks",
        "Task`1",
        "get_Result",
        SynchronousCompletionKind.TaskResult)]
    [InlineData(
        "System.Runtime.CompilerServices",
        "TaskAwaiter",
        "GetResult",
        SynchronousCompletionKind.TaskAwaiterGetResult)]
    [InlineData(
        "System.Runtime.CompilerServices",
        "TaskAwaiter`1",
        "GetResult",
        SynchronousCompletionKind.TaskAwaiterGetResult)]
    [InlineData(
        "System.Runtime.CompilerServices",
        "ConfiguredTaskAwaitable+ConfiguredTaskAwaiter",
        "GetResult",
        SynchronousCompletionKind.TaskAwaiterGetResult)]
    [InlineData(
        "System.Runtime.CompilerServices",
        "ConfiguredTaskAwaitable`1+ConfiguredTaskAwaiter",
        "GetResult",
        SynchronousCompletionKind.TaskAwaiterGetResult)]
    public void ClassifiesFrameworkTaskCompletionMembers(
        string typeNamespace,
        string typeName,
        string memberName,
        SynchronousCompletionKind expected)
    {
        DirectCall call = Call(
            TypeRef.CoreLib(typeNamespace, typeName),
            memberName);

        SynchronousCompletionObservation observation =
            Assert.Single(
                SynchronousCompletionAnalysis.Inspect([call]));

        Assert.Same(call, observation.Call);
        Assert.Equal(expected, observation.Kind);
    }

    [Fact]
    public void RejectsCustomAndUntrustedLookalikes()
    {
        DirectCall custom = Call(
            TypeRef.Definition(
                "Example",
                "System.Runtime.CompilerServices",
                "TaskAwaiter"),
            "GetResult");
        DirectCall untrusted = Call(
            TypeRef.Definition(
                TypeRef.CoreLibrary,
                "System.Threading.Tasks",
                "Task`1",
                trustedFrameworkAssembly: false),
            "get_Result");

        Assert.Empty(
            SynchronousCompletionAnalysis.Inspect(
                [custom, untrusted]));
    }

    [Theory]
    [InlineData("ValueTask`1", "get_Result")]
    [InlineData("ValueTaskAwaiter`1", "GetResult")]
    [InlineData("Task", "WaitAsync")]
    public void KeepsDeferredAndNonBlockingShapesOutOfTheObservation(
        string typeName,
        string memberName)
    {
        string typeNamespace = typeName.Contains(
            "Awaiter",
            StringComparison.Ordinal)
                ? "System.Runtime.CompilerServices"
                : "System.Threading.Tasks";

        Assert.Empty(
            SynchronousCompletionAnalysis.Inspect(
                [Call(
                    TypeRef.CoreLib(typeNamespace, typeName),
                    memberName)]));
    }

    static DirectCall Call(
        TypeRef declaringType,
        string name) =>
        new(
            new MethodIdentity(
                "Fixture",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TypeRef.Definition("Fixture", "Fixture", "Caller"),
                "Run",
                [],
                TypeRef.CoreLib("System", "Void"),
                0x06000001,
                IsStatic: true),
            new MemberRef(
                declaringType,
                name,
                [],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method)
            {
                HasThis = true,
            },
            ILOffset: 0,
            OperandToken: 0x0A000001,
            CalleeDefinitionToken: 0x0A000001,
            CallKind.CallVirtual);
}
