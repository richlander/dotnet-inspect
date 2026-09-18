using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// Framework-authenticated structures that synchronously observe
/// <see cref="System.Threading.Tasks.Task"/> completion.
/// </summary>
public enum SynchronousCompletionKind
{
    TaskWait,
    TaskResult,
    TaskAwaiterGetResult,
}

/// <summary>
/// One physical call that synchronously observes task completion.
/// </summary>
public sealed record SynchronousCompletionObservation(
    DirectCall Call,
    SynchronousCompletionKind Kind);

/// <summary>
/// Classifies positive synchronous-completion structure from existing direct
/// call evidence. It makes no runtime blocking or completion-state claim.
/// </summary>
public static class SynchronousCompletionAnalysis
{
    public static ImmutableArray<SynchronousCompletionObservation> Inspect(
        IEnumerable<DirectCall> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);

        var observations =
            ImmutableArray.CreateBuilder<SynchronousCompletionObservation>();
        foreach (DirectCall call in calls)
        {
            if (TryClassify(call, out SynchronousCompletionKind kind))
                observations.Add(new(call, kind));
        }
        return observations.ToImmutable();
    }

    public static bool TryClassify(
        DirectCall call,
        out SynchronousCompletionKind kind)
    {
        ArgumentNullException.ThrowIfNull(call);

        MemberRef callee = call.Callee;
        if (call.Kind is not (CallKind.Call or CallKind.CallVirtual)
            || !callee.HasThis)
        {
            kind = default;
            return false;
        }

        if (callee.Name == "Wait"
            && FrameworkIdentity.IsCoreLibraryType(
                callee.DeclaringType,
                "System.Threading.Tasks",
                "Task"))
        {
            kind = SynchronousCompletionKind.TaskWait;
            return true;
        }

        if (callee.Name == "get_Result"
            && callee.ParameterTypes.Length == 0
            && FrameworkIdentity.IsCoreLibraryType(
                callee.DeclaringType,
                "System.Threading.Tasks",
                "Task`1"))
        {
            kind = SynchronousCompletionKind.TaskResult;
            return true;
        }

        if (callee.Name == "GetResult"
            && callee.ParameterTypes.Length == 0
            && IsTaskAwaiter(callee.DeclaringType))
        {
            kind = SynchronousCompletionKind.TaskAwaiterGetResult;
            return true;
        }

        kind = default;
        return false;
    }

    static bool IsTaskAwaiter(TypeRef type) =>
        FrameworkIdentity.IsCoreLibraryType(
            type,
            "System.Runtime.CompilerServices",
            "TaskAwaiter")
        || FrameworkIdentity.IsCoreLibraryType(
            type,
            "System.Runtime.CompilerServices",
            "TaskAwaiter`1")
        || FrameworkIdentity.IsCoreLibraryType(
            type,
            "System.Runtime.CompilerServices",
            "ConfiguredTaskAwaitable+ConfiguredTaskAwaiter")
        || FrameworkIdentity.IsCoreLibraryType(
            type,
            "System.Runtime.CompilerServices",
            "ConfiguredTaskAwaitable`1+ConfiguredTaskAwaiter");
}
