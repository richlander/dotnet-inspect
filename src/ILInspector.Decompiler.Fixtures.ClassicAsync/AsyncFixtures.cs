using System;
using System.Threading.Tasks;

namespace ILInspector.Decompiler.Fixtures.ClassicAsync;

/// <summary>
/// Representative async source compiled with <c>runtime-async=off</c>, so each method
/// lowers to a classic <c>AsyncTaskMethodBuilder</c> state machine (a <c>&lt;M&gt;d__N</c>
/// struct + <c>MoveNext</c>) rather than the runtime-async <c>AsyncHelpers.Await</c> form
/// the rest of the repo uses. This is the async axis of the multi-mode fixture matrix:
/// the same source the runtime-async build covers, recompiled in the other mode so the
/// decompiler is measured (via <c>--library-report</c>) on the classic lowering too.
///
/// On-demand only — not swept by any CI gate. Build it and point the harness at the DLL.
/// </summary>
public static class AsyncFixtures
{
    public static async Task<int> AwaitValue(Task<int> a, int b) => await a + b;

    public static async Task AwaitVoid(Task a) => await a;

    public static async Task TwoSequentialAwaits(Task<int> a, Task<int> b)
    {
        int x = await a;
        int y = await b;
        GC.KeepAlive((x, y));
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

    public static async Task<int> AwaitConditional(Task<int> a, bool flag)
        => flag ? await a : 0;
}
