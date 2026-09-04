using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ILInspector.Decompiler.Fixtures.ClassicAsync;

/// <summary>
/// Representative async source compiled with <c>runtime-async=off</c>, so each method
/// lowers to a classic <c>AsyncTaskMethodBuilder</c> state machine (a <c>&lt;M&gt;d__N</c>
/// struct + <c>MoveNext</c>) rather than the runtime-async <c>AsyncHelpers.Await</c> form
/// the rest of the repo uses. This is the async axis of the multi-mode fixture matrix:
/// <c>ILInspector.Decompiler.Fixtures.RuntimeAsync</c> compiles this exact source
/// with the opposite feature setting so lowering differences are attributable to
/// the compiler mode.
///
/// Library reports are on-demand; <c>AsyncLoweringFixtureMatrixTests</c> gates the
/// physical lowering distinction.
/// </summary>
public static class AsyncFixtures
{
    public struct Observation
    {
        public int Value;
    }

    public sealed class Box
    {
        public int Value;
    }

    public sealed record Snapshot(int Value);

    public struct Counter
    {
        public int Value;

        public static Counter operator ++(Counter value)
        {
            value.Value++;
            return value;
        }
    }

    public static int Observed;
    public static int Observed2;
    public static int? ObservedNullable;
    public static int ObservedProperty { get; set; }
    public static Observation ObservedStruct;
    public static event Action<int> ObservedEvent = delegate { };

    public static void RaiseObservedEvent(int value)
        => ObservedEvent(value);

    public static async Task<int> AwaitValue(Task<int> a, int b) => await a + b;

    public static async Task AwaitVoid(Task a) => await a;

    public static async Task TwoSequentialAwaits(Task<int> a, Task<int> b)
    {
        int x = await a;
        int y = await b;
        GC.KeepAlive((x, y));
    }

    public static async Task TwoSequentialNamedAwaits(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        int beta = await b;
        GC.KeepAlive((alpha, beta));
    }

    public static async Task SequentialWithFieldStore(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        Observed = alpha;
        int beta = await b;
        GC.KeepAlive((alpha, beta));
    }

    public static void SetResult(int value)
        => Observed = value;

    public static int RecordObserved(int value)
    {
        Observed = value;
        return value;
    }

    public static Task<int> SelectAndRecord(Task<int> task)
    {
        Observed++;
        return task;
    }

    public static async Task SequentialWithOrdinarySetResultCall(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        SetResult(alpha);
        int beta = await b;
        GC.KeepAlive((alpha, beta));
    }

    public static async Task SequentialWithSeparateBuilderReceiver(
        Task<int> a,
        Task<int> b,
        AsyncTaskMethodBuilder other)
    {
        int alpha = await a;
        other.SetResult();
        int beta = await b;
        GC.KeepAlive((alpha, beta, other.Task));
    }

    public static async Task SequentialWithChainedFieldStores(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        Observed2 = Observed = alpha;
        int beta = await b;
        GC.KeepAlive((alpha, beta));
    }

    public static async Task SequentialWithNullCoalescingFieldStore(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        ObservedNullable ??= alpha;
        int beta = await b;
        GC.KeepAlive((alpha, beta));
    }

    public static async Task SequentialWithPropertyStore(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        ObservedProperty = alpha;
        int beta = await b;
        GC.KeepAlive((alpha, beta));
    }

    public static async Task SequentialWithInitObjectStore(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        ObservedStruct = default;
        int beta = await b;
        GC.KeepAlive((alpha, beta));
    }

    public static async Task<int> AwaitOrdinarySetMethod(Task<int> task)
        => await set_GetTask(task);

    public static Task<int> set_GetTask(Task<int> task) => task;

    public static (int Left, int Right) Pair(int value)
        => (value, value + 1);

    public static async Task SequentialWithEventSubscription(
        Task<int> a,
        Task<int> b,
        Action<int> handler)
    {
        int alpha = await a;
        ObservedEvent += handler;
        int beta = await b;
        GC.KeepAlive((alpha, beta));
    }

    public static async Task SequentialWithParameterWrite(
        Task<int> a,
        Task<int> b,
        int captured)
    {
        int alpha = await a;
        captured = alpha;
        int beta = await b;
        GC.KeepAlive((alpha, beta, captured));
    }

    public static async Task SequentialWithHoistedLocalWrite(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        alpha = 42;
        int beta = await b;
        GC.KeepAlive((alpha, beta));
    }

    public static async Task SequentialWithHoistedLocalIncrement(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        alpha++;
        int beta = await b;
        GC.KeepAlive((alpha, beta));
    }

    public static async Task SequentialWithStructParameterReset(
        Task<int> a,
        Task<int> b,
        Observation captured)
    {
        int alpha = await a;
        captured = default;
        int beta = await b;
        GC.KeepAlive((alpha, beta, captured));
    }

    public static async Task SequentialWithDeconstructionWrite(
        Task<int> a,
        Task<int> b,
        int left,
        int right)
    {
        int alpha = await a;
        (left, right) = Pair(alpha);
        int beta = await b;
        GC.KeepAlive((alpha, beta, left, right));
    }

    public static async Task SequentialWithCapturedNullCoalescingWrite(
        Task<int> a,
        Task<int> b,
        int? captured)
    {
        int alpha = await a;
        captured ??= alpha;
        int beta = await b;
        GC.KeepAlive((alpha, beta, captured));
    }

    public static async Task SequentialWithEmbeddedIncrement(
        Task<int> a,
        Task<int> b,
        Counter captured)
    {
        int alpha = await a;
        int beta = await b + (captured++).Value;
        GC.KeepAlive((alpha, beta, captured));
    }

    public static async Task SequentialWithRealizedInitializer(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        int beta = await b;
        GC.KeepAlive(new Box { Value = alpha + beta });
    }

    public static async Task SequentialWithRealizedWithExpression(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        int beta = await b;
        GC.KeepAlive(new Snapshot(alpha) with { Value = beta });
    }

    public static async Task SequentialWithImplicitConversion(
        Task<int> a,
        Task<int> b)
    {
        int alpha = await a;
        long beta = await b;
        GC.KeepAlive((alpha, beta));
    }

    public static async ValueTask<int> AwaitValueTask(ValueTask<int> a) => await a;

    public static async Task<int> AwaitInLoop(Task<int>[] tasks)
    {
        int sum = 0;
        foreach (var task in tasks)
        {
            sum += await task;
        }
        return sum;
    }

#pragma warning disable CS8981
    public static async Task<int> AwaitInLoopWithRoleNameCollision<sum>(
        Task<int>[] tasks)
    {
        int total = 0;
        foreach (var work in tasks)
        {
            total += await work;
        }
        return total;
    }
#pragma warning restore CS8981

    public static async Task<int> AwaitInLoopWithWrappedOperand(
        Task<int>[] tasks)
    {
        int sum = 0;
        foreach (Task<int> task in tasks)
        {
            sum += await SelectAndRecord(task);
        }
        return sum;
    }

    public static async Task<int> TwoAwaitsOverTasksArray(
        Task<int>[] tasks)
    {
        int first = await tasks[0];
        int second = await tasks[1];
        return first + second;
    }

    public static async Task<int> LoopWithFieldStore(
        Task<int>[] tasks)
    {
        int sum = 0;
        foreach (var task in tasks)
        {
            sum += await task;
            Observed = sum;
        }
        return sum;
    }

    public static async Task<int> LoopWithAccumulatorWrite(
        Task<int>[] tasks)
    {
        int sum = 0;
        foreach (var task in tasks)
        {
            sum += await task;
            sum *= 2;
        }
        return sum;
    }

    public static async Task<int> LoopWithClamp(
        Task<int>[] tasks)
    {
        int sum = 0;
        foreach (var task in tasks)
        {
            sum += await task;
        }
        if (sum < 0)
        {
            sum = 0;
        }
        return sum;
    }

    public static async Task<int> AwaitInTryFinally(Task<int> a)
    {
        try
        {
            return await a;
        }
        finally
        {
            GC.KeepAlive(a);
        }
    }

    public static async Task<int> AwaitInTryFinallyWithGuardedCall(
        Task<int> a,
        bool flag)
    {
        try
        {
            return await a;
        }
        finally
        {
            if (flag)
            {
                RecordObserved(1);
            }
        }
    }

    public static async Task<int> AwaitConditional(Task<int> a, bool flag)
        => flag ? await a : 0;

    public static async Task<int> AwaitConditionalWithWrappedResult(
        Task<int> a,
        bool flag)
        => flag ? RecordObserved(await a) : 0;

    public static async Task<int> AwaitCompoundConditional(
        Task<int> a,
        bool flag,
        bool other)
        => flag && other ? await a : 0;

    public static async Task<bool> DynamicReferenceIdentity(
        Task<ReferenceIdentityPlain> value,
        dynamic right)
        => (object)(await value) == (object)right;

    public static async Task<bool> DynamicArrayReferenceIdentity(
        Task<ReferenceIdentityPlain> value,
        dynamic[] right)
        => (object)(await value) == (object)right[0];

    public static async Task<bool> ObjectArrayReferenceIdentity(
        Task<ReferenceIdentityPlain> value,
        object[] right)
        => (object)(await value) == right[0];

    public static async Task<int> InterfaceReceiver(Task<InterfaceValue> value)
        => ((IInterfaceValue)(await value)).GetValue();

#pragma warning disable CS1998 // Pins async metadata when no await survives into the body.
    public static async Task NoAwait()
    {
    }

    public sealed class ReferenceIdentityPlain
    {
    }

    public interface IInterfaceValue
    {
        int GetValue() => 7;
    }

    public readonly struct InterfaceValue : IInterfaceValue
    {
    }
#pragma warning restore CS1998
}
