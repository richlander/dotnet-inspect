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
    const byte InstanceDefaultSignature = 0x20;
    const byte IntrinsicSignatureType = 0x00;
    const byte ValueTypeSignatureType = 0x11;

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
            || !HasOrdinaryInstanceSignature(callee))
        {
            kind = default;
            return false;
        }

        if (IsTaskWait(callee))
        {
            kind = SynchronousCompletionKind.TaskWait;
            return true;
        }

        if (IsTaskResult(callee))
        {
            kind = SynchronousCompletionKind.TaskResult;
            return true;
        }

        if (IsTaskAwaiterGetResult(callee))
        {
            kind = SynchronousCompletionKind.TaskAwaiterGetResult;
            return true;
        }

        kind = default;
        return false;
    }

    static bool HasOrdinaryInstanceSignature(MemberRef callee) =>
        callee.HasThis
        && callee.SignatureHeader == InstanceDefaultSignature
        && callee.GenericArity == 0
        && callee.RequiredParameterCount
            == callee.ParameterTypes.Length;

    static bool IsTaskWait(MemberRef callee)
    {
        if (callee.Name != "Wait"
            || !FrameworkIdentity.IsCoreLibraryType(
                callee.DeclaringType,
                "System.Threading.Tasks",
                "Task"))
        {
            return false;
        }

        if (IsCoreLibrarySignatureType(
                callee.ReturnType,
                "System",
                "Void",
                IntrinsicSignatureType))
        {
            return callee.ParameterTypes.Length == 0
                || callee.ParameterTypes.Length == 1
                    && IsCoreLibrarySignatureType(
                        callee.ParameterTypes[0],
                        "System.Threading",
                        "CancellationToken",
                        ValueTypeSignatureType);
        }

        if (!IsCoreLibrarySignatureType(
                callee.ReturnType,
                "System",
                "Boolean",
                IntrinsicSignatureType))
        {
            return false;
        }

        return callee.ParameterTypes.Length == 1
                && IsTaskWaitTimeout(
                    callee.ParameterTypes[0])
            || callee.ParameterTypes.Length == 2
                && IsTaskWaitTimeout(
                    callee.ParameterTypes[0])
                && IsCoreLibrarySignatureType(
                    callee.ParameterTypes[1],
                    "System.Threading",
                    "CancellationToken",
                    ValueTypeSignatureType);
    }

    static bool IsTaskWaitTimeout(TypeRef type) =>
        IsCoreLibrarySignatureType(
            type,
            "System",
            "Int32",
            IntrinsicSignatureType)
        || IsCoreLibrarySignatureType(
            type,
            "System",
            "TimeSpan",
            ValueTypeSignatureType);

    static bool IsTaskResult(MemberRef callee) =>
        callee.Name == "get_Result"
        && callee.ParameterTypes.Length == 0
        && HasGenericResultSignature(callee)
        && FrameworkIdentity.IsCoreLibraryType(
            callee.DeclaringType,
            "System.Threading.Tasks",
            "Task`1");

    static bool IsTaskAwaiterGetResult(MemberRef callee)
    {
        if (callee.Name != "GetResult"
            || callee.ParameterTypes.Length != 0)
        {
            return false;
        }

        TypeRef declaringType = callee.DeclaringType;
        if (FrameworkIdentity.IsCoreLibraryType(
                declaringType,
                "System.Runtime.CompilerServices",
                "TaskAwaiter")
            || FrameworkIdentity.IsCoreLibraryType(
                declaringType,
                "System.Runtime.CompilerServices",
                "ConfiguredTaskAwaitable+ConfiguredTaskAwaiter"))
        {
            return IsCoreLibrarySignatureType(
                callee.ReturnType,
                "System",
                "Void",
                IntrinsicSignatureType);
        }

        return HasGenericResultSignature(callee)
            && (FrameworkIdentity.IsCoreLibraryType(
                    declaringType,
                    "System.Runtime.CompilerServices",
                    "TaskAwaiter`1")
                || FrameworkIdentity.IsCoreLibraryType(
                    declaringType,
                    "System.Runtime.CompilerServices",
                    "ConfiguredTaskAwaitable`1+ConfiguredTaskAwaiter"));
    }

    static bool HasGenericResultSignature(MemberRef callee)
    {
        TypeRef openReturnType =
            callee.OpenSignatureReturn;
        if (openReturnType.Kind
                != TypeRefKind.GenericParameter
            || openReturnType.GenericParameterIndex != 0)
        {
            return false;
        }

        TypeRef declaringType = callee.DeclaringType;
        return declaringType.Kind
                == TypeRefKind.GenericInstance
            && declaringType.TypeArguments is
                [var resultType]
            && callee.ReturnType.Equals(resultType)
            || declaringType.Kind
                == TypeRefKind.Definition
                && callee.ReturnType.Kind
                    == TypeRefKind.GenericParameter
                && callee.ReturnType.GenericParameterIndex
                    == 0;
    }

    static bool IsCoreLibrarySignatureType(
        TypeRef type,
        string ns,
        string name,
        byte rawTypeKind) =>
        type.RawTypeKind == rawTypeKind
        && FrameworkIdentity.IsCoreLibraryType(
            type,
            ns,
            name);
}
