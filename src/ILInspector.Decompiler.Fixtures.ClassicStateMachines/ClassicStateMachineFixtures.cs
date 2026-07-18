using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ILInspector.Decompiler.Fixtures.ClassicStateMachines;

/// <summary>
/// Classic-lowering state-machine fixtures for issue #2818 (child of the #2814
/// decompiler-quality direction, successor to #622's broader-coverage
/// recommendations). Compiled with <c>runtime-async=off</c> (see the .csproj),
/// so every await here lowers to a classic <c>AsyncTaskMethodBuilder</c> state
/// machine rather than the runtime-async <c>AsyncHelpers.Await</c> form the rest
/// of the repo's corpora use. Iterators and async iterators always lower to a
/// compiler-generated <c>MoveNext</c> state machine regardless of the
/// runtime-async switch, so they are measured here too — together with classic
/// async, this is the full "classic era" state-machine set the real-world and
/// opt-in-net11 corpora do not exercise.
///
/// Method-name prefixes are the feature-coverage contract read by
/// <c>CorpusSensor.RecordClassicStateMachineFeatureCoverage</c>: <c>Async_</c>
/// (classic async kickoff + its compiler-generated <c>MoveNext</c>),
/// <c>Iterator_</c> (classic <c>yield</c> iterator), <c>AsyncIterator_</c>
/// (classic async iterator — <c>IAsyncEnumerable&lt;T&gt;</c> combining both
/// state-machine shapes), and <c>Switch_</c> (a plain, non-pattern switch
/// statement used as a control). This is a representative current-compiler
/// matrix, not an exhaustive cross-compiler or downlevel-TFM corpus.
/// </summary>
public class ClassicStateMachineFixtures
{
    public static async Task<int> Async_AwaitValue(Task<int> a, int b) => await a + b;

    public static async Task Async_TwoSequentialAwaits(Task<int> a, Task<int> b)
    {
        int x = await a;
        int y = await b;
        GC.KeepAlive((x, y));
    }

    public static async Task<int> Async_AwaitInTryFinally(Task<int> a)
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

    public static async Task<int> Async_AwaitInLoop(Task<int>[] tasks)
    {
        int sum = 0;
        foreach (var task in tasks)
        {
            sum += await task;
        }
        return sum;
    }

    public static async void Async_VoidBuilder(Action completed)
    {
        await Task.Yield();
        completed();
    }

    public static async ValueTask<int> Async_ValueTaskBuilder(ValueTask<int> value)
        => await value;

    public async Task<T> Async_InstanceGeneric<T>(Task<T> value)
    {
        await Task.Yield();
        return await value;
    }

    public static async Task<int> Async_AwaitInCatchAndFinally(Task<int> value)
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
            return await value;
        }
        finally
        {
            await Task.Yield();
        }
    }

    public static IEnumerable<int> Iterator_YieldSequence(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return i;
        }
    }

    public static IEnumerable<int> Iterator_YieldBreakEarly(int limit)
    {
        int i = 0;
        while (true)
        {
            if (i >= limit)
            {
                yield break;
            }
            yield return i;
            i++;
        }
    }

    public static IEnumerable<int> Iterator_YieldInTryFinally(IEnumerable<int> source)
    {
        try
        {
            foreach (var item in source)
            {
                yield return item;
            }
        }
        finally
        {
            GC.KeepAlive(source);
        }
    }

    public static IEnumerable Iterator_NonGeneric()
    {
        yield return "classic";
        yield return 42;
    }

    public static IEnumerable<T> Iterator_Generic<T>(T first, T second)
    {
        yield return first;
        yield return second;
    }

    public IEnumerable<int> Iterator_Instance(int value)
    {
        yield return value;
        yield return value + 1;
    }

    public static async IAsyncEnumerable<int> AsyncIterator_AwaitThenYield(Task<int>[] tasks)
    {
        foreach (var task in tasks)
        {
            int value = await task;
            yield return value;
        }
    }

    public static async IAsyncEnumerable<int> AsyncIterator_YieldBreakEarly(Task<int>[] tasks, int limit)
    {
        int i = 0;
        foreach (var task in tasks)
        {
            if (i >= limit)
            {
                yield break;
            }
            yield return await task;
            i++;
        }
    }

    public static async IAsyncEnumerable<int> AsyncIterator_WithCancellation(
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }

    public static async IAsyncEnumerable<int> AsyncIterator_TryFinallyAndDisposal(
        IAsyncEnumerable<int> source)
    {
        await using var resource = new AsyncResource();
        try
        {
            await foreach (int item in source)
                yield return item;
        }
        finally
        {
            await resource.TouchAsync();
        }
    }

    // Plain (non-pattern) integer switch: the old jump-table lowering, exercised
    // alongside the classic async/iterator state-machine's own internal state
    // dispatch (also a switch, over the compiler-generated `<>1__state` field).
    // This lowering is TFM/LangVersion-agnostic, so it belongs in this lane
    // rather than a separate downlevel-TFM axis.
    public static string Switch_ClassifyState(int state)
    {
        switch (state)
        {
            case -1:
                return "not-started";
            case 0:
                return "running";
            case 1:
                return "suspended";
            case 2:
                return "finished";
            default:
                return "unknown";
        }
    }

    public static async Task<string> Switch_InsideAsync(Task<int> a)
    {
        int value = await a;
        switch (value)
        {
            case 0:
                return "zero";
            case 1:
                return "one";
            default:
                return "many";
        }
    }

    sealed class AsyncResource : IAsyncDisposable
    {
        public ValueTask TouchAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
