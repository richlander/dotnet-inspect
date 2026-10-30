using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ILInspector.Decompiler.Fixtures.ClassicAsync;

/// <summary>
/// Compiler-produced witnesses for the general classic async inverse
/// (<c>docs/design/classic-async-reconstruction.md</c>, "General inverse").
/// Each method pins one protocol role or one decline boundary of the role
/// table rather than a user-code shape: dispatch forms, suspension sites,
/// completion, finally guards, hoisted storage, housekeeping clears, and the
/// stores the inverse must carry. Compiled with <c>runtime-async=off</c> like
/// the rest of this fixture assembly.
/// </summary>
public static class GeneralInverseFixtures
{
    public sealed class Reader
    {
        public int Depth;
        public bool IsStart;

        public Task<bool> ReadAsync(CancellationToken cancellationToken)
            => Task.FromResult(Depth-- > 0);

        /// <summary>Hoisted <c>this</c> and parameter; an await in a loop condition; two sites sharing one awaiter local.</summary>
        public async Task SkipAsync(CancellationToken cancellationToken)
        {
            if (IsStart)
            {
                await ReadAsync(cancellationToken).ConfigureAwait(false);
            }

            if (Depth > 0)
            {
                int depth = Depth;
                while (await ReadAsync(cancellationToken).ConfigureAwait(false) && depth < Depth)
                {
                }
            }
        }
    }

    /// <summary>Three suspension states: Roslyn dispatches through a <c>switch</c>.</summary>
    public static async Task ThreeSequentialAwaits(Task a, Task b, Task c, bool flag)
    {
        await a;
        if (flag)
        {
            await b;
        }

        await c;
    }

    /// <summary>A <c>using</c> with code after it: the wrap clear after disposal and the guarded finally.</summary>
    public static async Task<string> UsingThenCode(Func<Task<string>> read)
    {
        string text;
        using (var writer = new StringWriter())
        {
            text = await read();
            writer.Write(text);
        }

        return text + "!";
    }

    /// <summary>A hoisted reference-type local whose block ends before the method: Roslyn's scope-end clear.</summary>
    public static async Task<int> ScopedHoistedLocal(int n)
    {
        int result = 0;
        {
            var scoped = new object();
            await Task.Yield();
            result = scoped.GetHashCode();
        }

        await Task.Delay(1);
        return result + n;
    }

    /// <summary>Negative: the user's own <c>big = null</c> before an await has the compiler clear's physical form and must stay.</summary>
    public static async Task<int> UserClearBeforeAwait(int n)
    {
        var big = new byte[n];
        await Task.Yield();
        int length = big.Length;
        big = null;
        await Task.Delay(1);
        return length + (big is null ? 1 : 0);
    }

    /// <summary>Negative: the user's second <c>x = null</c> is a branch target after a conditional clear; it is a join, not an adjacent clear, and must stay.</summary>
    public static async Task<string> ConditionalThenUnconditionalClear(bool condition, Func<string> get)
    {
        string text = get();
        if (condition)
        {
            text = null;
        }

        text = null;
        await Task.Yield();
        return text ?? "null";
    }

    /// <summary>Negative: awaits in a <c>catch</c> and a <c>finally</c> inside a loop; the lowering's selector resets are carried state.</summary>
    public static async Task<int> AwaitInCatchAndFinallyLoop(Func<int, Task> body, int count)
    {
        int hits = 0;
        for (int i = 0; i < count; i++)
        {
            try
            {
                await body(i);
            }
            catch (InvalidOperationException)
            {
                await Task.Yield();
                hits++;
            }
            finally
            {
                await Task.Yield();
                hits += 10;
            }
        }

        return hits;
    }

    /// <summary>Several <c>leave</c>s to the completion tail, each a return of the result local.</summary>
    public static async Task<int> EarlyReturns(Task<int> a, Task<int> b)
    {
        int first = await a;
        if (first < 0)
        {
            return -1;
        }

        if (first == 0)
        {
            return 0;
        }

        return first + await b;
    }

    /// <summary><c>AsyncVoidMethodBuilder</c>: the same roles with a void completion.</summary>
    public static async void VoidBuilder(Action completed)
    {
        await Task.Yield();
        completed();
    }

    /// <summary><c>AsyncValueTaskMethodBuilder&lt;T&gt;</c> with a struct awaitable spill.</summary>
    public static async ValueTask<int> ValueTaskBuilder(ValueTask<int> value, int add)
        => await value + add;

    public sealed class Holder<T>
    {
        public T Value;

        /// <summary>A generic instance kickoff with a <c>YieldAwaitable</c> and a hoisted <c>this</c> store.</summary>
        public async Task<T> GetAsync(Task<T> source)
        {
            await Task.Yield();
            Value = await source;
            return Value;
        }
    }

    /// <summary>A parameter written in the body: the hoisted parameter field is stored, not only read.</summary>
    public static async Task<int> ParameterWrite(int n, Task<int> a)
    {
        n += await a;
        n *= 2;
        return n;
    }

    /// <summary>An async local function: its state machine is <c>&lt;&lt;Outer&gt;g__Inner|N_M&gt;d</c>, not <c>&lt;M&gt;d__N</c>.</summary>
    public static async Task<int> WithLocalFunction(Task<int> a)
    {
        return await Inner(a) + 1;

        static async Task<int> Inner(Task<int> task)
        {
            await Task.Yield();
            return await task;
        }
    }

    /// <summary>An async lambda: the kickoff is an instance method of a display class.</summary>
    public static Func<Task<int>> Lambda(Task<int> a)
        => async () =>
        {
            await Task.Yield();
            return await a;
        };

    /// <summary>A user catch with a filter around an await inside the protocol try.</summary>
    public static async Task<int> UserCatchFilter(Task<int> a, Func<Exception, bool> accept)
    {
        try
        {
            return await a;
        }
        catch (Exception e) when (accept(e))
        {
            return -1;
        }
    }

    /// <summary>A lambda-captured local read after an await: the display-class environment field is cleared at its block's end and must be removed so the closure raise folds.</summary>
    public static async Task<int> CapturedLocalAfterAwait(int n)
    {
        int result;
        {
            int captured = n;
            Func<int> read = () => captured + 1;
            await Task.Delay(n);
            result = read() + captured;
        }

        await Task.Yield();
        return result;
    }

    /// <summary>A hoisted local written and read across awaits in a loop, with a conditional exit.</summary>
    public static async Task<int> AccumulateUntil(Func<Task<int>> next, int limit)
    {
        int total = 0;
        while (true)
        {
            int value = await next().ConfigureAwait(false);
            if (value < 0)
            {
                break;
            }

            total += value;
            if (total > limit)
            {
                return limit;
            }
        }

        return total;
    }
}

public static class GeneralInverseLoopFixtures
{
    /// <summary>An async <c>foreach</c> over an array with a reference-type iteration variable used after the await: Roslyn hoists it and clears it at the end of each body (see #10577).</summary>
    public static async Task<int> AwaitForeachArray(string[] items, Func<string, Task<int>> measure)
    {
        int total = 0;
        foreach (string item in items)
        {
            int length = await measure(item);
            total += length + item.Length;
        }

        return total;
    }

    /// <summary>The same shape over an <c>IEnumerable&lt;T&gt;</c>: the enumerator raise path (see #10577).</summary>
    public static async Task<int> AwaitForeachEnumerable(System.Collections.Generic.IEnumerable<string> items, Func<string, Task<int>> measure)
    {
        int total = 0;
        foreach (string item in items)
        {
            int length = await measure(item);
            total += length + item.Length;
        }

        return total;
    }

    /// <summary>Decline witness: an await only inside a <c>finally</c> lowers to a used <c>catch (object)</c> handler.</summary>
    public static async Task<int> AwaitInFinallyOnly(Func<Task> body, Func<Task> cleanup)
    {
        try
        {
            await body();
            return 1;
        }
        finally
        {
            await cleanup();
        }
    }

    /// <summary>Awaits only inside a <c>catch</c> in a loop: the lowering's pending-catch selector is reset every iteration and must be carried.</summary>
    public static async Task<int> AwaitInCatchLoop(Func<int, Task> body, int count)
    {
        int hits = 0;
        for (int i = 0; i < count; i++)
        {
            try
            {
                await body(i);
            }
            catch (InvalidOperationException)
            {
                await Task.Yield();
                hits++;
            }
        }

        return hits;
    }

    /// <summary>A <c>using var</c> declaration whose variable is hoisted across the await.</summary>
    public static async Task<int> UsingVar(Func<Task<int>> read)
    {
        using var writer = new StringWriter();
        int value = await read();
        writer.Write(value);
        return writer.ToString().Length;
    }

    /// <summary>A struct awaitable local hoisted across an earlier await: Roslyn awaits it through the hoisted field's address.</summary>
    public static async Task<int> HoistedStructAwaitable(Func<ValueTask<int>> start)
    {
        ValueTask<int> pending = start();
        await Task.Yield();
        return await pending;
    }

    /// <summary>A struct awaitable parameter awaited after an earlier await: the hoisted parameter field's address is the operand.</summary>
    public static async Task<int> StructParameterAfterYield(ValueTask<int> pending)
    {
        await Task.Yield();
        return await pending;
    }

    /// <summary>A stored <c>YieldAwaitable</c> awaited after an earlier await.</summary>
    public static async Task HoistedYieldAwaitable(Func<Task> first)
    {
        var yielded = Task.Yield();
        await first();
        await yielded;
    }

    /// <summary>Decline witness: an await inside a <c>catch</c> that rethrows keeps the pending exception as <c>object</c> and throws it.</summary>
    public static async Task AwaitInCatchRethrow(Func<Task> body, Func<Task> report)
    {
        try
        {
            await body();
        }
        catch (InvalidOperationException)
        {
            await report();
            throw;
        }
    }
}
