using System.Collections.Immutable;

namespace ILInspector.Analysis.Tests;

public sealed class SynchronousCompletionAnalysisTests
{
    [Theory]
    [MemberData(nameof(TaskWaitSignatures))]
    public void ClassifiesFrameworkTaskWaitMembers(
        TypeRef returnType,
        TypeRef[] parameterTypes)
    {
        DirectCall call = Call(
            TypeRef.CoreLib(
                "System.Threading.Tasks",
                "Task"),
            "Wait",
            returnType,
            parameterTypes);

        SynchronousCompletionObservation observation =
            Assert.Single(
                SynchronousCompletionAnalysis.Inspect([call]));

        Assert.Same(call, observation.Call);
        Assert.Equal(
            SynchronousCompletionKind.TaskWait,
            observation.Kind);
    }

    [Fact]
    public void ClassifiesFrameworkTaskResultMember()
    {
        TypeRef resultType =
            TypeRef.CoreLib("System", "String");

        SynchronousCompletionObservation observation =
            Assert.Single(
                SynchronousCompletionAnalysis.Inspect(
                    [
                        Call(
                            Generic(
                                "System.Threading.Tasks",
                                "Task`1",
                                resultType),
                            "get_Result",
                            resultType),
                    ]));

        Assert.Equal(
            SynchronousCompletionKind.TaskResult,
            observation.Kind);
    }

    [Theory]
    [InlineData(
        "TaskAwaiter",
        false)]
    [InlineData(
        "TaskAwaiter`1",
        true)]
    [InlineData(
        "ConfiguredTaskAwaitable+ConfiguredTaskAwaiter",
        false)]
    [InlineData(
        "ConfiguredTaskAwaitable`1+ConfiguredTaskAwaiter",
        true)]
    public void ClassifiesFrameworkTaskAwaiterMembers(
        string typeName,
        bool generic)
    {
        TypeRef resultType =
            TypeRef.CoreLib("System", "String");
        TypeRef declaringType = generic
            ? Generic(
                "System.Runtime.CompilerServices",
                typeName,
                resultType)
            : TypeRef.CoreLib(
                "System.Runtime.CompilerServices",
                typeName);

        SynchronousCompletionObservation observation =
            Assert.Single(
                SynchronousCompletionAnalysis.Inspect(
                    [
                        Call(
                            declaringType,
                            "GetResult",
                            generic
                                ? resultType
                                : TypeRef.CoreLib(
                                    "System",
                                    "Void")),
                    ]));

        Assert.Equal(
            SynchronousCompletionKind.TaskAwaiterGetResult,
            observation.Kind);
    }

    [Fact]
    public void RejectsCustomAndUntrustedLookalikes()
    {
        DirectCall custom = Call(
            TypeRef.Definition(
                "Example",
                "System.Runtime.CompilerServices",
                "TaskAwaiter"),
            "GetResult",
            TypeRef.CoreLib("System", "Void"));
        DirectCall untrusted = Call(
            TypeRef.Definition(
                TypeRef.CoreLibrary,
                "System.Threading.Tasks",
                "Task`1",
                trustedFrameworkAssembly: false),
            "get_Result",
            TypeRef.CoreLib("System", "String"));

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
                    memberName,
                    TypeRef.CoreLib("System", "Void"))]));
    }

    [Fact]
    public void RejectsMalformedFrameworkMemberSignatures()
    {
        TypeRef resultType =
            TypeRef.CoreLib("System", "String");
        TypeRef taskResult = Generic(
            "System.Threading.Tasks",
            "Task`1",
            resultType);
        TypeRef awaiterResult = Generic(
            "System.Runtime.CompilerServices",
            "TaskAwaiter`1",
            resultType);

        Assert.Empty(
            SynchronousCompletionAnalysis.Inspect(
                [
                    Call(
                        TypeRef.CoreLib(
                            "System.Threading.Tasks",
                            "Task"),
                        "Wait",
                        TypeRef.CoreLib("System", "Int32"),
                        TypeRef.CoreLib("System", "String")),
                    Call(
                        taskResult,
                        "get_Result",
                        TypeRef.CoreLib("System", "Void")),
                    Call(
                        TypeRef.CoreLib(
                            "System.Runtime.CompilerServices",
                            "TaskAwaiter"),
                        "GetResult",
                        resultType),
                    Call(
                        awaiterResult,
                        "GetResult",
                        TypeRef.CoreLib("System", "Int32")),
                ]));
    }

    [Fact]
    public void RejectsNonOrdinaryMethodSignatures()
    {
        DirectCall valid = Call(
            TypeRef.CoreLib(
                "System.Threading.Tasks",
                "Task"),
            "Wait",
            TypeRef.CoreLib("System", "Void"));

        Assert.Empty(
            SynchronousCompletionAnalysis.Inspect(
                [
                    valid with
                    {
                        Callee = valid.Callee with
                        {
                            GenericArity = 1,
                        },
                    },
                    valid with
                    {
                        Callee = valid.Callee with
                        {
                            SignatureHeader = 0x25,
                        },
                    },
                    valid with
                    {
                        Callee = valid.Callee with
                        {
                            RequiredParameterCount = 1,
                        },
                    },
                ]));
    }

    static DirectCall Call(
        TypeRef declaringType,
        string name,
        TypeRef returnType,
        params TypeRef[] parameterTypes) =>
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
                [.. parameterTypes],
                returnType,
                MemberKind.Method)
            {
                HasThis = true,
                SignatureHeader = 0x20,
                RequiredParameterCount =
                    parameterTypes.Length,
            },
            ILOffset: 0,
            OperandToken: 0x0A000001,
            CalleeDefinitionToken: 0x0A000001,
            CallKind.CallVirtual);

    static TypeRef Generic(
        string ns,
        string name,
        TypeRef argument) =>
        TypeRef.GenericInstance(
            TypeRef.CoreLib(ns, name),
            [argument]);

    public static TheoryData<TypeRef, TypeRef[]>
        TaskWaitSignatures =>
        new()
        {
            {
                TypeRef.CoreLib("System", "Void"),
                []
            },
            {
                TypeRef.CoreLib("System", "Void"),
                [
                    TypeRef.CoreLib(
                        "System.Threading",
                        "CancellationToken"),
                ]
            },
            {
                TypeRef.CoreLib("System", "Boolean"),
                [TypeRef.CoreLib("System", "Int32")]
            },
            {
                TypeRef.CoreLib("System", "Boolean"),
                [TypeRef.CoreLib("System", "TimeSpan")]
            },
            {
                TypeRef.CoreLib("System", "Boolean"),
                [
                    TypeRef.CoreLib("System", "Int32"),
                    TypeRef.CoreLib(
                        "System.Threading",
                        "CancellationToken"),
                ]
            },
            {
                TypeRef.CoreLib("System", "Boolean"),
                [
                    TypeRef.CoreLib("System", "TimeSpan"),
                    TypeRef.CoreLib(
                        "System.Threading",
                        "CancellationToken"),
                ]
            },
        };
}
