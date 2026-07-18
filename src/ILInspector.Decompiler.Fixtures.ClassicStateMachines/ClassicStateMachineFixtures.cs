using System;
using System.Collections.Generic;
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
/// statement — the old jump-table lowering, TFM/LangVersion-agnostic like the
/// rest of this fixture, so it is measured here rather than requiring a
/// downlevel TFM/LangVersion axis).
/// </summary>
public static class ClassicStateMachineFixtures
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
}
