using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{
}

public class GenericConstraintBase;

public static class OptimizationOpportunityAsyncSiblingFixtures
{
    public static IEnumerable<int> ReadValues(int value)
    {
        yield return value;
    }

    public static async IAsyncEnumerable<int> ReadValuesAsync(
        int value,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return value;
    }

    public static async Task<int> CallsSyncSiblingFromAsync(int value)
    {
        await Task.Yield();
        return ReadValues(value).Count();
    }

    public static async Task<int>
        CallsSameSyncSiblingFromAsync(int value)
    {
        await Task.Yield();
        return ReadValues(value).Count();
    }

    public static int ReadOtherValue(int value) => value;

    public static Task<int> ReadOtherValueAsync(int value)
        => Task.FromResult(value);

    public static async Task<int>
        CallsOtherSyncSiblingFromAsync(int value)
    {
        await Task.Yield();
        return ReadOtherValue(value);
    }
}

public class OptimizationOpportunityFixtures
{
    private readonly int _field = 3;
    private readonly UserDisplayClassTarget _displayClassTarget = new();
    private int[]? _arrayField;
    private PlainObject? _objectField;
    private static PlainObject? _staticObjectField;
    private IFormattable? _formattableField;

    // A capturing delegate created INSIDE a loop: a repeated allocation -> high confidence.
    public static int CapturingDelegateInLoop(int[] values, int seed)
    {
        var total = 0;
        foreach (var v in values)
        {
            System.Func<int> f = () => v + seed;
            total += f();
        }
        return total;
    }

    public static async IAsyncEnumerable<int> AsyncStream(int count)
    {
        await System.Threading.Tasks.Task.Yield();
        for (int i = 0; i < count; i++)
            yield return i;
    }

    public static async Task<int> PlainAsyncTask(int value)
    {
        await Task.Yield();
        return value + 1;
    }

    public static int CallsSyncSiblingFromNonAsync(int value)
        => OptimizationOpportunityAsyncSiblingFixtures
            .ReadValues(value)
            .Count();

    public static int ReadWithoutCompatibleAsyncSibling(int value)
        => value;

    public static Task<int> ReadWithoutCompatibleAsyncSiblingAsync(
        string value)
        => Task.FromResult(value.Length);

    public static async Task<int>
        CallsSyncWithoutCompatibleAsyncSibling(int value)
    {
        await Task.Yield();
        return ReadWithoutCompatibleAsyncSibling(value);
    }

    public static int ReadWithMismatchedAsyncReturn(int value)
        => value;

    public static Task<string> ReadWithMismatchedAsyncReturnAsync(
        int value)
        => Task.FromResult(value.ToString());

    public static async Task<int>
        CallsSyncWithMismatchedAsyncReturn(int value)
    {
        await Task.Yield();
        return ReadWithMismatchedAsyncReturn(value);
    }

    public static int ExecuteOwnCore(int value)
        => value;

    public static async Task<int> ExecuteOwnCoreAsync(
        int value)
    {
        await Task.Yield();
        return ExecuteOwnCore(value);
    }

    public static void OnLifecycle()
    {
    }

    public static Task OnLifecycleAsync()
        => Task.CompletedTask;

    public static async Task CallsBothLifecycleSiblingsAsync()
    {
        OnLifecycle();
        await OnLifecycleAsync();
    }

    public static async Task<int> CallsFileReadLinesFromAsync(
        string path)
    {
        await Task.Yield();
        return File.ReadLines(path).Count();
    }

    public static async Task
        CallsBothFileReadLinesSiblingsAsync(string path)
    {
        _ = File.ReadLines(path);
        _ = File.ReadLinesAsync(
            path,
            CancellationToken.None);
        await Task.Yield();
    }

    public static int MaterializesInvariantSourceInLoop(IEnumerable<int> source, int count)
    {
        var total = 0;
        for (int i = 0; i < count; i++)
            total += source.ToArray().Length;
        return total;
    }

    public static int MaterializesShortFormSourceArgumentInLoop(int a, int b, int c, int d, IEnumerable<int> source, int count)
    {
        var total = a + b + c + d;
        for (int i = 0; i < count; i++)
            total += source.ToList().Count;
        return total;
    }

    public static int MaterializesPerIterationSourceInLoop(IEnumerable<int> source, int count)
    {
        var total = 0;
        for (int i = 0; i < count; i++)
        {
            var filtered = source.Where(value => value > i);
            total += filtered.ToArray().Length;
        }
        return total;
    }

    public static int MaterializesSourceMutatedByRefInLoop(IEnumerable<int> source, IEnumerable<int> replacement, int count)
    {
        var current = source;
        var total = 0;
        for (int i = 0; i < count; i++)
        {
            ReplaceSource(ref current, replacement);
            total += current.ToArray().Length;
        }
        return total;
    }

    static void ReplaceSource(ref IEnumerable<int> source, IEnumerable<int> replacement)
    {
        source = replacement;
    }

    // A method taking an UNTRUSTED RenderTreeBuilder lookalike (defined in this test assembly,
    // so it carries no framework public-key-token). It allocates a capturing delegate in a
    // loop; because the parameter type is not the trusted framework RenderTreeBuilder, this is
    // NOT treated as Blazor render plumbing and the allocation IS reported. The trusted
    // counterpart (RealRenderFixtures.RenderWithDelegateLoop) is suppressed.
    public static int RenderLikeMethod(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder, int[] values, int seed)
    {
        var total = 0;
        foreach (var v in values)
        {
            System.Func<int> handler = () => v + seed;
            total += handler();
        }

        return total;
    }

    public int[] MakesArrayAfterFieldAccess()
    {
        var value = _field;
        return new int[3];
    }

    public static int[] MakesArrayAfterCallAndArgument(int length)
    {
        Console.WriteLine(42);
        return new int[length];
    }

    // --- Escape analysis (small-array vs stackalloc-candidate) ---

    // Array created, written, and read entirely locally -> provably non-escaping.
    public static int LocalArrayStaysLocal()
    {
        var a = new int[4];
        a[0] = 1;
        a[3] = 2;
        return a[0] + a[3];
    }

    public static int LocalArrayInTryCatch(int value)
    {
        try
        {
            var a = new int[4];
            a[0] = value;
            return a[0];
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    // Non-escaping but a managed (reference) element type -> not stackalloc-eligible.
    public static int LocalStringArrayStaysLocal()
    {
        var a = new string[4];
        a[0] = "x";
        a[3] = "yz";
        return a[0].Length + a[3].Length;
    }

    // Returned -> escapes.
    public static int[] ReturnsSmallArray() => new int[4];

    public static int[] ReturnsIntArray10() => new int[10];

    public static int[] ReturnsIntArray5() => new int[5];

    public static int[] ReturnsIntArray5AfterUnrelatedBranch(bool condition)
    {
        if (condition)
            ConsumeBoolean(condition);

        return new int[5];
    }

    public static int[] ReturnsConditionalSmallOrHugeArray(bool condition)
        => new int[condition ? 5 : 1_000_000];

    public static int[] ReturnsConditionalHugeOrSmallArray(bool condition)
        => new int[condition ? 1_000_000 : 5];

    public static byte[] ReturnsByteArray100() => new byte[100];

    public static string[] ReturnsStringArray8() => new string[8];

    public static long[] ReturnsLongArray4() => new long[4];

    public static IntPtr[] ReturnsIntPtrArray3() => new IntPtr[3];

    public static UIntPtr[] ReturnsUIntPtrArray3() => new UIntPtr[3];

    public static Guid[] ReturnsGuidArray4() => new Guid[4];

    // Constructed and popped without leaving the method -> local-only lifetime.
    public static int DropsPlainObject()
    {
        _ = new PlainObject(1);
        return 1;
    }

    public static object ReturnsPlainObject() => new PlainObject(1);

    // Growable collections whose churned backing store the analysis reports.
    public static object AllocatesListOfInt() => new System.Collections.Generic.List<int>();
    public static object AllocatesQueueOfByte() => new System.Collections.Generic.Queue<byte>();
    public static object AllocatesStackOfLong() => new System.Collections.Generic.Stack<long>();
    public static object AllocatesStringBuilder() => new System.Text.StringBuilder();
    // Multi-array backing (buckets + entries) -> fail-honest, no single churned type.
    public static object AllocatesDictionary() => new System.Collections.Generic.Dictionary<int, string>();

    public void StoresPlainObjectToField() => _objectField = new PlainObject(1);

    public static void ExceptionUnknownOrThrow(bool passToUnknown)
    {
        var exception = new InvalidOperationException("bad");
        if (passToUnknown)
        {
            ConsumeObject(exception);
            return;
        }
        throw exception;
    }

    public static void FinallyAllocates()
    {
        try
        {
            ConsumeObject(null);
        }
        finally
        {
            ConsumeObject(new PlainObject(1));
        }
    }

    public static int CatchAllocatesInLoop(int count)
    {
        var total = 0;
        for (int i = 0; i < count; i++)
        {
            try
            {
                MaybeThrow(i);
            }
            catch (InvalidOperationException)
            {
                ConsumeObject(new PlainObject(i));
                total--;
            }
        }
        return total;
    }

    public static object CatchAllocatesBeforeOnlyReturn()
    {
        object? result = null;
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
            result = new PlainObject(1);
        }
        return result;
    }

    static void MaybeThrow(int value)
    {
        if (value < 0)
            throw new InvalidOperationException();
    }

    public static object ReturnsNestedGenericObject()
        => new AllocationMetadataOuter<int>.Inner<string>();

    // --- String accumulation (string-build-in-loop) ---

    // `s += x` inside a foreach: String.Concat(s, x) stored back to the same local each
    // iteration -> O(n^2) growing-accumulator copy. The canonical StringBuilder case (HIGH).
    public static string AppendsStringInLoop(string[] items)
    {
        var s = "";
        foreach (var x in items)
            s += x;
        return s;
    }

    // `s += item.Property` inside a loop: an instance-call (get_Property) sits between the
    // accumulator load and the Concat, so it exercises the not-clearing-on-calls path.
    public static string AppendsStringPropertyInLoop(System.Collections.Generic.List<string> items)
    {
        var s = "";
        for (int i = 0; i < items.Count; i++)
            s = s + items[i] + ",";
        return s;
    }

    // Accumulation into a parameter slot rather than a local (`seed += x`).
    public static string AppendsToParameterInLoop(string seed, string[] items)
    {
        foreach (var x in items)
            seed += x;
        return seed;
    }

    // NOT accumulation: each iteration builds a fresh string added to a list. The concat
    // result is never stored back into one of its inputs -> no StringBuilder rewrite, so it
    // must NOT be flagged (this was the noisy tier measured to be ~all false positives).
    public static System.Collections.Generic.List<string> ConcatsIntoListInLoop(System.Collections.Generic.Dictionary<string, string> pairs)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var kv in pairs)
            parts.Add(kv.Key + "=" + kv.Value);
        return parts;
    }

    // NOT accumulation: a one-time concat on a return path inside a loop -> not flagged.
    public static string ReturnsConcatInLoop(string[] items)
    {
        foreach (var x in items)
        {
            if (x.Length > 3)
                return x + "-found";
        }
        return "";
    }

    // NOT accumulation: `s += x` but OUTSIDE any loop -> not flagged (no repeated copy).
    public static string AppendsStringOnce(string a, string b)
    {
        var s = a;
        s += b;
        return s;
    }

    // Accumulator is the LAST concat argument (prepend): `s = x + sep + s` is still O(n^2).
    public static string PrependsStringInLoop(string[] items)
    {
        var s = "";
        foreach (var x in items)
            s = x + " " + s;
        return s;
    }

    // NOT accumulation by the stack-aware check: the destination slot is loaded only to pass
    // to an unrelated call, then reassigned from operands that do not include it
    // (`Foo(s); s = a + b;`). The bare load is popped by Foo before the concat, so `s` is not a
    // concat argument -> must NOT be flagged. (Adversarial-review regression guard.)
    public static string ReassignsUnrelatedSlotInLoop(string seed, string a, string b, string[] items)
    {
        foreach (var x in items)
        {
            System.Console.WriteLine(seed);
            seed = a + b;
        }
        return seed;
    }

    // Conservatively NOT flagged: the accumulator flows through a call (`s.Trim()`) before the
    // concat, so the bare-load bit does not survive. Documents the precision/recall tradeoff.
    public static string DerivedAccumulatorInLoop(string[] items)
    {
        var s = "";
        foreach (var x in items)
            s = x + " " + s.Trim();
        return s;
    }

    // --- Enumerator allocation (enumerator-allocation) ---

    // foreach over an interface-typed sequence INSIDE a loop: GetEnumerator returns the
    // reference-type IEnumerator<T>, allocated on each outer iteration.
    public static int ForeachInterfaceInLoop(System.Collections.Generic.IEnumerable<int> items, int times)
    {
        var total = 0;
        for (int i = 0; i < times; i++)
        {
            foreach (var x in items)
                total += x;
        }

        return total;
    }

    // foreach over an interface-typed sequence with no enclosing loop: a single enumerator
    // allocation -> not flagged.
    public static int ForeachInterfaceOnce(System.Collections.Generic.IEnumerable<int> items)
    {
        var total = 0;
        foreach (var x in items)
            total += x;
        return total;
    }

    // foreach over a concrete List<T> inside a loop: List<T>.GetEnumerator returns the struct
    // List<T>.Enumerator by value -> no heap allocation, must not be flagged.
    public static int ForeachConcreteListInLoop(System.Collections.Generic.List<int> items, int times)
    {
        var total = 0;
        for (int i = 0; i < times; i++)
        {
            foreach (var x in items)
                total += x;
        }

        return total;
    }

    // foreach over a collection whose GetEnumerator returns an UNTRUSTED IEnumerator lookalike
    // (defined in this test assembly). Even inside a loop it must not be flagged — the framework
    // enumerator identity is trust-gated, so a same-namespace/name user type is not mistaken for
    // the real IEnumerator.
    public static int ForeachLookalikeEnumeratorInLoop(LookalikeEnumeratorCollection c, int times)
    {
        var total = 0;
        for (int i = 0; i < times; i++)
        {
            foreach (var x in c)
                total += x;
        }

        return total;
    }

    // --- Copy allocation (span-to-array) ---

    // ReadOnlySpan<T>.ToArray() materializes a copy but the array is returned, so the copy is
    // required by this API shape. It remains a copy signal but is not an optimization row.
    public static int[] SpanToArrayCopy(System.ReadOnlySpan<int> span) => span.ToArray();

    // Span<T>.ToArray() also copies, but returning the array retains it.
    public static int[] MutableSpanToArrayCopy(System.Span<int> span) => span.ToArray();

    public static int ReadsSpanToArrayLocally(System.ReadOnlySpan<int> span)
    {
        var copy = span.ToArray();
        return copy[0];
    }

    public static int ReadsMutableSpanToArrayLocally(System.Span<int> span)
    {
        var copy = span.ToArray();
        return copy[0];
    }

    public static int ReadsSpanToArrayLengthLocally(System.ReadOnlySpan<int> span)
        => span.ToArray().Length;

    public static void SpanToArrayPassedToArrayApi(System.ReadOnlySpan<int> span)
        => ConsumeArray(span.ToArray());

    // List<T>.ToArray() is a common, usually-legitimate snapshot -> deliberately NOT flagged
    // as span-to-array-copy (kept out to avoid flooding the section).
    public static int[] ListToArrayNotFlagged(System.Collections.Generic.List<int> list) => list.ToArray();

    public static int[] EnumerableToArrayCopy(System.Collections.Generic.IEnumerable<int> values)
        => values.ToArray();

    // --- Repeated LINQ scan inside a loop (linq-scan-in-loop) ---

    // A membership LINQ scan (Enumerable.Any) runs once per loop iteration over a
    // sequence whose size scales with the input -> O(n*m). The canonical fix is to
    // build a HashSet once outside the loop.
    public static int LinqScanInLoop(System.Collections.Generic.IEnumerable<int> source, int[] keys)
    {
        var count = 0;
        foreach (var key in keys)
        {
            if (System.Linq.Enumerable.Any(source, x => x == key))
            {
                count++;
            }
        }
        return count;
    }

    // The same membership scan, but not inside any loop -> not a repeated scan.
    public static bool LinqScanOutsideLoop(System.Collections.Generic.IEnumerable<int> source, int key)
        => System.Linq.Enumerable.Any(source, x => x == key);

    // A lazy operator (Enumerable.Where) called in a loop does not itself enumerate,
    // so it is not a terminal scan and must not be flagged.
    public static int LazyWhereInLoopNotFlagged(System.Collections.Generic.IEnumerable<int> source, int[] keys)
    {
        var seen = 0;
        foreach (var key in keys)
        {
            var filtered = System.Linq.Enumerable.Where(source, x => x == key);
            seen += filtered is null ? 0 : 1;
        }
        return seen;
    }

    // --- Cross-method repeated scan (scan-method-in-loop-call) ---

    // A scanning helper: contains a membership scan but no loop in its own body.
    public static bool ContainsKey(System.Collections.Generic.IEnumerable<int> source, int key)
        => System.Linq.Enumerable.Any(source, x => x == key);

    // Invokes the scanning helper once per loop iteration -> the scan runs O(m) times.
    public static int CallsScanHelperInLoop(System.Collections.Generic.IEnumerable<int> source, int[] keys)
    {
        var n = 0;
        foreach (var key in keys)
        {
            if (ContainsKey(source, key))
            {
                n++;
            }
        }
        return n;
    }

    // A scanning helper that is only ever invoked outside any loop -> not a repeated scan.
    public static bool ContainsKeyNeverLooped(System.Collections.Generic.IEnumerable<int> source, int key)
        => System.Linq.Enumerable.Any(source, x => x == key);

    public static bool CallsScanHelperOnceNoLoop(System.Collections.Generic.IEnumerable<int> source, int key)
        => ContainsKeyNeverLooped(source, key);

    // Parameterless positional/aggregate terminals are O(1) (a positional read or the
    // ICollection.Count fast path), so they must NOT be flagged even inside a loop.
    public static int ParameterlessTerminalsInLoopNotFlagged(System.Collections.Generic.List<int> list, int[] keys)
    {
        var n = 0;
        foreach (var key in keys)
        {
            if (System.Linq.Enumerable.Any(list))
            {
                n += System.Linq.Enumerable.Count(list);
            }
            if (System.Linq.Enumerable.First(list) == key)
            {
                n++;
            }
        }
        return n;
    }

    // A lazy-returning helper: hands back a deferred Where query without enumerating it.
    public static System.Collections.Generic.IEnumerable<int> FilterLazy(System.Collections.Generic.IEnumerable<int> source, int key)
        => System.Linq.Enumerable.Where(source, x => x == key);

    // Enumerates the lazy helper's result once per loop iteration -> the deferred linear
    // scan runs O(m) times across the call boundary.
    public static int CallsLazyHelperInLoop(System.Collections.Generic.IEnumerable<int> source, int[] keys)
    {
        var n = 0;
        foreach (var key in keys)
        {
            foreach (var _ in FilterLazy(source, key))
            {
                n++;
            }
        }
        return n;
    }

    // A lazy producer immediately consumed by a parameterless terminal still scans the
    // source. This is the compiled shape of Where(...).FirstOrDefault().
    public static int FilterThenFirstOrDefault(System.Collections.Generic.IEnumerable<int> source, int key)
        => System.Linq.Enumerable.FirstOrDefault(
            System.Linq.Enumerable.Where(source, x => x == key));

    public static int CallsComposedScanHelperInLoop(System.Collections.Generic.IEnumerable<int> source, int[] keys)
    {
        var n = 0;
        foreach (var key in keys)
        {
            n += FilterThenFirstOrDefault(source, key);
        }
        return n;
    }

    public static bool ContainsEither(
        System.Collections.Generic.IEnumerable<int> source,
        int first,
        int second)
        => System.Linq.Enumerable.Any(
                source,
                x => x == first)
            || System.Linq.Enumerable.Any(
                source,
                x => x == second);

    public static int CallsAmbiguousScanHelperInLoop(
        System.Collections.Generic.IEnumerable<int> source,
        int[] keys)
    {
        var n = 0;
        foreach (var key in keys)
        {
            n += ContainsEither(source, key, key + 1)
                ? 1
                : 0;
        }
        return n;
    }

    // The lazy result is stored, and First consumes another source. Merely having both calls
    // in one method is not enough evidence that the lazy query is scanned.
    public static int UnrelatedLazyAndTerminal(System.Collections.Generic.IEnumerable<int> source, int[] other)
    {
        var filtered = System.Linq.Enumerable.Where(source, x => x > 0);
        return filtered is null ? 0 : System.Linq.Enumerable.First(other);
    }

    public static int CallsUnrelatedLazyAndTerminalInLoop(
        System.Collections.Generic.IEnumerable<int> source,
        int[] other)
    {
        var n = 0;
        foreach (var _ in other)
        {
            n += UnrelatedLazyAndTerminal(source, other);
        }
        return n;
    }

    public static int ProjectThenFirst(System.Collections.Generic.IEnumerable<int> source)
        => System.Linq.Enumerable.First(
            System.Linq.Enumerable.Select(source, x => x + 1));

    public static int CallsProjectedFirstInLoop(
        System.Collections.Generic.IEnumerable<int> source,
        int[] values)
    {
        var n = 0;
        foreach (var _ in values)
        {
            n += ProjectThenFirst(source);
        }
        return n;
    }

    public static string StringConcatCopy(string left, string right)
        => string.Concat(left, right);

    public static string StringJoinCopy(string[] values)
        => string.Join(",", values);

    public static string StringSubstringCopy(string value)
        => value.Substring(1);

    // Copy-signal variant recall (#1623 rung 3). IsCopyApi recognizes ToList, CopyTo
    // (span/array), and RuntimeHelpers.GetSubArray (range slicing) in addition to the
    // ToArray/Substring/Concat/Join forms above, but those variants had no recall
    // guard — a regression dropping any of them would have been silent.
    public static System.Collections.Generic.List<int> EnumerableToListCopy(System.Collections.Generic.IEnumerable<int> values)
        => System.Linq.Enumerable.ToList(values);

    public static void ArrayCopyToCopy(int[] source, int[] destination)
        => source.CopyTo(destination, 0);

    public static void SpanCopyToCopy(System.ReadOnlySpan<int> source, System.Span<int> destination)
        => source.CopyTo(destination);

    public static void MutableSpanCopyToCopy(System.Span<int> source, System.Span<int> destination)
        => source.CopyTo(destination);

    public static int[] RangeSubArrayCopy(int[] values)
        => values[1..];

    public static string UserJoinLookalike(int a, int b)
        => UserCopyLookalikes.Join(a, b);

    public static string UserConcatLookalike(int a, int b)
        => UserCopyLookalikes.Concat(a, b);

    public static string UserSubstringLookalike(string value)
        => UserCopyLookalikes.Substring(value);

    // Stored to a field -> escapes.
    public void StoresArrayToField() => _arrayField = new int[4];

    // Stored to a static field -> escapes-static.
    public static void StoresObjectToStaticField() => _staticObjectField = new PlainObject(1);

    // Stored to a field of a user type that merely echoes `c__DisplayClass` in its
    // name -> plain Field escape (not a false Capture). Container is a parameter so
    // the only tracked allocation is the stored object.
    public static void StoresObjectToLookalikeDisplayClassField(Fake_c__DisplayClass target)
        => target.Field = new object();

    // The yielded allocation is stored into the iterator's <>2__current field.
    public static IEnumerable<PlainObject> YieldsPlainObject()
    {
        yield return new PlainObject(1);
    }

    // Async iterators use the same <>2__current field for the yielded value.
    public static async IAsyncEnumerable<PlainObject> YieldsPlainObjectAsync()
    {
        await Task.Yield();
        yield return new PlainObject(1);
    }

    // Escapes to two distinct sinks (static field + return) -> fail-honest: no single kind.
    // The branch forces the value into a local slot (two distinct ldloc uses) rather than a dup.
    public static object StoresToStaticThenReturns(bool condition)
    {
        var o = new PlainObject(1);
        if (condition)
            _staticObjectField = o;
        return o;
    }

    // Stored into an array element -> escapes-collection.
    public static void StoresObjectIntoArrayElement(object[] target) => target[0] = new PlainObject(1);

    // Captured by a closure -> the array is hoisted onto a display class (escapes-capture).
    public static Func<int> CapturesArrayInClosure()
    {
        var a = new int[4];
        return () => a.Length;
    }

    // Stored to a local but then passed to a call -> escapes.
    public static void LocalArrayPassedToCall()
    {
        var a = new int[4];
        a[0] = 1;
        ConsumeArray(a);
    }

    private static void ConsumeArray(int[] data) => Console.WriteLine(data.Length);

    private static void ConsumeObject(object? value) => Console.WriteLine(value);

    private static void ConsumeBoolean(bool value) => Console.WriteLine(value);

    // --- Delegate allocation (capture detection + de-dup) ---

    // Captures a local -> capturing delegate (one row).
    public static Func<int> CapturingLambda(int seed)
    {
        return () => seed + 1;
    }

    public static Func<int> CapturingLambdaWithDependentLocal(int seed, int addend)
    {
        int sum = seed + addend;
        return () => seed + sum;
    }

    // A capturing lambda consumed by a lazy LINQ operator (Where). Rewriting the lambda as
    // an iterator/local function only MOVES the allocation to the state machine, so the
    // capturing-delegate row carries a "moved allocation" caveat.
    public static System.Collections.Generic.IEnumerable<int> CapturingLambdaConsumedByWhere(
        System.Collections.Generic.IEnumerable<int> source, int threshold)
        => System.Linq.Enumerable.Where(source, x => x > threshold);

    // A capturing lambda NOT consumed by a LINQ operator: ordinary use, no moved-allocation
    // caveat (the local-function rewrite genuinely removes the allocation here).
    public static int CapturingLambdaConsumedByNonLinq(int threshold)
    {
        Func<int, bool> predicate = x => x > threshold;
        return predicate(5) ? 1 : 0;
    }

    // Non-capturing lambda -> compiler-cached delegate (one row, not capturing).
    public static Func<int> NonCapturingLambda()
    {
        return () => 42;
    }

    private static readonly OptimizationOpportunityFixtures CachedReceiver = new();
    private static Func<string, int>? s_cachedInstanceMethodGroup;

    public static Func<string, int> CachedInstanceMethodGroup()
    {
        return s_cachedInstanceMethodGroup ??= CachedReceiver.InstanceParseLength;
    }

    private readonly ColdStackGuard _stackGuard = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _concurrentCache = new();
    private readonly FreshFactory _stableFactory = new();
    private readonly UserGetOrAddCache _userCache = new();

    public int ConcurrentDictionaryInstanceFactory(string key)
        => _concurrentCache.GetOrAdd(key, InstanceParseLength);

    public int ConcurrentDictionaryFreshReceiverFactory(string key)
        => _concurrentCache.GetOrAdd(
            key,
            new FreshFactory().Create);

    public int ConcurrentDictionaryStableGetterFactory(string key)
        => _concurrentCache.GetOrAdd(
            key,
            StableFactory.Create);

    public int ConcurrentDictionaryStableGetterFactoryTwice(
        string first,
        string second)
        => _concurrentCache.GetOrAdd(
                first,
                StableFactory.Create)
            + _concurrentCache.GetOrAdd(
                second,
                StableFactory.Create);

    private FreshFactory StableFactory => _stableFactory;

    public int ConcurrentDictionaryVirtualGetterFactory(string key)
        => _concurrentCache.GetOrAdd(
            key,
            VirtualFactory.Create);

    protected virtual FreshFactory VirtualFactory => _stableFactory;

    public int UserGetOrAddInstanceFactory(string key)
        => _userCache.GetOrAdd(key, InstanceParseLength);

    public int StackGuardFallback(int value)
    {
        if (!_stackGuard.TryEnterOnCurrentStack())
        {
            return _stackGuard.RunOnEmptyStack(InstanceTransform, value);
        }

        return value;
    }

    public int StackGuardFallbackStoredInvertedCondition(int value)
    {
        bool fallback = _stackGuard.TryEnterOnCurrentStack() == false;
        if (fallback)
        {
            return _stackGuard.RunOnEmptyStack(InstanceTransform, value);
        }

        return value;
    }

    private int InstanceTransform(int value) => value + _field;

    // Static method group -> non-capturing delegate (one row).
    public static Func<string, int> StaticMethodGroup()
    {
        return ParseLength;
    }

    private static int ParseLength(string value) => value.Length;

    // Instance method group -> per-call delegate binding the receiver (never cached).
    public Func<string, int> InstanceMethodGroup()
    {
        return InstanceParseLength;
    }

    private int InstanceParseLength(string value) => value.Length + _field;

    // Virtual instance method group -> ldvirtftn, also a per-call delegate.
    public Func<int> VirtualInstanceMethodGroup()
    {
        return VirtualHelper;
    }

    public virtual int VirtualHelper() => _field;

    public Func<int> UserTypeNameContainsDisplayClass()
    {
        return _displayClassTarget.GetValue;
    }

    // --- Boxing (box-value-type) ---

    // Boxes an int into an object-typed API argument -> escapes via call.
    public static string BoxesIntoStringFormat(int value)
    {
        return string.Format("v={0}", value);
    }

    // Boxes a value into an exception message that is thrown immediately. The box is an
    // error-path allocation (executes at most once before unwinding), so it must NOT be
    // flagged as a box-value-type opportunity.
    public static void ThrowsWithBoxedValue(int value)
    {
        if (value < 0)
        {
            throw new System.ArgumentException(string.Format("bad {0}", value));
        }
    }

    // Boxes a value type per iteration into a non-generic collection -> in a loop (high).
    public static void BoxesInLoop(int[] values, System.Collections.ArrayList sink)
    {
        foreach (int v in values)
        {
            sink.Add(v);
        }
    }

    // External well-known value-type box recall (#1623 rung 3). Boxing a referenced
    // (TypeReference) framework struct is recognized only via IsWellKnownValueType's
    // curated (namespace, name) set, since the SRM-direct product cannot resolve the
    // external type definition. Canary representative members of that set so dropping
    // one is not silent; the boxed value escapes via the return.
    public static object BoxesGuidValue(System.Guid value) => value;

    public static object BoxesDateTimeValue(System.DateTime value) => value;

    public static class UserCopyLookalikes
    {
        public static string Join(int a, int b) => $"{a}:{b}";
        public static string Concat(int a, int b) => $"{a}{b}";
        public static string Substring(string value) => value;
    }

    public class GenericOptimizationOpportunityFixtures<T>
    {
        public Func<T, T> NonCapturingLambda()
        {
            return value => value;
        }
    }

    public sealed class UserDisplayClassTarget
    {
        public int GetValue() => 42;
    }

    private sealed class UserGetOrAddCache
    {
        public int GetOrAdd(string key, Func<string, int> valueFactory)
            => valueFactory(key);
    }

    protected sealed class FreshFactory
    {
        public int Create(string value) => value.Length;
    }

    // A user-defined type whose name echoes the closure suffix `c__DisplayClass`
    // but lacks the compiler-generated `<>` marker. A field store here must be a
    // plain Field escape, never a false Capture.
    public sealed class Fake_c__DisplayClass
    {
        public object? Field;
    }

    private sealed class ColdStackGuard
    {
        public bool TryEnterOnCurrentStack() => true;

        public TResult RunOnEmptyStack<TArg, TResult>(Func<TArg, TResult> action, TArg argument)
            => action(argument);
    }

    public sealed class AmortizedConstructorFixture
    {
        public int Total;

        public AmortizedConstructorFixture(int[] values, int seed)
        {
            foreach (var value in values)
            {
                Func<int> projector = () => value + seed;
                Total += projector();
            }
        }
    }

    public sealed class HotConstructorFixture
    {
        public int Total;

        public HotConstructorFixture(int[] values, int seed)
        {
            foreach (var value in values)
            {
                Func<int> projector = () => value + seed;
                Total += projector();
            }
        }
    }

    public static int CreateHotConstructorsInLoop(int[][] inputs, int seed)
    {
        var total = 0;
        foreach (var values in inputs)
        {
            total += new HotConstructorFixture(values, seed).Total;
        }

        return total;
    }

    // Boxes a value into an exception message on a throw path inside a loop -> the box only
    // allocates when throwing, so it is NOT hot pay-dirt: reported, but medium (not loop-promoted).
    public static void BoxesIntoThrowMessage(int code, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (code == i)
            {
                throw new System.InvalidOperationException(string.Format("dup {0}", code));
            }
        }
    }

    // Boxes a generic parameter (compiler-mandated) -> NOT reported.
    public static object BoxesGenericParameter<T>(T value) where T : struct
    {
        return value;
    }

    public static bool GenericObjectEquals<T>(T left, T right)
        => left!.Equals(right);

    public static bool GenericObjectEqualsLocalFunction<T>(T left, T right)
    {
        return EqualsCore(left, right);

        static bool EqualsCore(T x, T y) => x!.Equals(y);
    }

    public static Func<T, T, bool> GenericObjectEqualsLambda<T>() =>
        (left, right) => left!.Equals(right);

    public static int MultipleLiftedFunctions(int value)
    {
        return AddOne(value) + AddTwo(value);

        static int AddOne(int item) => item + 1;
        static int AddTwo(int item) => item + 2;
    }

    public static int IndirectLiftedFunction(int value)
    {
        return Later(value);

        static int Earlier(int item) => item + 1;
        static int Later(int item) => Earlier(item);
    }

    public static bool DistinctMethodSpecsWithSharedMemberRef<T>(
        T left,
        T right)
    {
        int length = Array.Empty<int>().Length
            + Array.Empty<string>().Length;
        return length == 0 && Core(left, right);

        static bool Core(T x, T y) => x!.Equals(y);
    }

    [System.CodeDom.Compiler.GeneratedCode("test", "1.0")]
    public static bool SourceGeneratedLocalFunction<T>(T left, T right)
    {
        return EqualsCore(left, right);

        static bool EqualsCore(T x, T y) => x!.Equals(y);
    }

    [System.CodeDom.Compiler.GeneratedCode("test", "1.0")]
    public static Func<T, bool> SourceGeneratedLambda<T>(T right)
        => left => left!.Equals(right);

    public static bool ReferenceGenericObjectEquals<T>(T left, T right)
        where T : class
        => left.Equals(right);

    public static bool NamedClassGenericObjectEquals<T>(T left, T right)
        where T : GenericConstraintBase
        => left.Equals(right);

    [System.Runtime.CompilerServices.CompilerGenerated]
    public static bool CompilerGeneratedOrdinaryGenericObjectEquals<T>(
        T left,
        T right)
        => left!.Equals(right);

    [System.Runtime.CompilerServices.CompilerGenerated]
    public static bool CompilerGeneratedOwner<T>(T left, T right)
    {
        return EqualsCore(left, right);

        static bool EqualsCore(T x, T y) => x!.Equals(y);
    }

    public static bool GenericTypedEquals<T>(T left, T right)
        where T : System.IEquatable<T>
        => left.Equals(right);

    public static bool StaticObjectEquals<T>(T left, T right)
        => object.Equals(left, right);

    public static void PassesGenericToObjectConsumer<T>(
        T value,
        System.Collections.ArrayList sink)
        => sink.Add(value);

    // Boxing a Nullable<T> allocates only when non-null -> conservatively NOT reported.
    public static object? BoxesNullable(int? value)
    {
        return value;
    }

    public static int BoxesThenUnboxesLocal(int value)
    {
        object boxed = value;
        return (int)boxed;
    }

    public static IFormattable? BoxThenIsinstReturn(int value)
        => (object)value as IFormattable;

    public void BoxThenIsinstStoreField(int value)
        => _formattableField = (object)value as IFormattable;

    // Boxing an in-assembly struct that escapes into an object-typed API -> reported
    // (value-typeness resolved authoritatively via the struct's System.ValueType base).
    public static void BoxesInAssemblyStruct(BoxFixtureStruct value, System.Collections.ArrayList sink)
    {
        sink.Add(value);
    }

    // BitConverter.GetBytes allocates a transient byte[] -> temporary-byte-array-copy.
    public static byte FirstValueByte(int value)
    {
        return System.BitConverter.GetBytes(value)[0];
    }

    // Boxing a constructed in-assembly generic STRUCT (a TypeSpec) -> reported: value-type-ness
    // comes from the TypeSpec signature blob (ELEMENT_TYPE_VALUETYPE), not the well-known list.
    public static object BoxesGenericStruct(BoxFixtureStruct<int> value)
    {
        return value;
    }

    // >= threshold heap allocations, none in a loop -> a medium allocation-hotspot row.
    public static object AllocatesManyObjects()
    {
        var items = new System.Collections.Generic.List<object>();
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        return items;
    }

    // >= threshold heap allocations inside a loop, matching no specific shape -> a medium
    // allocation-hotspot (repeated, dense, not otherwise pinpointed).
    public static object AllocatesManyObjectsInLoop(int n)
    {
        var items = new System.Collections.Generic.List<object>();
        for (int i = 0; i < n; i++)
        {
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
        }
        return items;
    }

    // Dense allocations inside a loop, but the method ALSO allocates a capturing delegate (a
    // specific shape). The hotspot row is deduped away so it doesn't double-count the
    // already-pinpointed method.
    public static object AllocatesDenselyInLoopWithDelegate(int n)
    {
        var items = new System.Collections.Generic.List<object>();
        System.Func<int> captured = () => n + 1;
        items.Add(captured());
        for (int i = 0; i < n; i++)
        {
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
        }
        return items;
    }

    // Many exception constructors on mutually-exclusive throw paths -> NOT a hotspot: these
    // allocate only when throwing, not in steady state, so they are excluded from the count.
    public static void ManyThrowArms(int code)
    {
        switch (code)
        {
            case 0: throw new System.InvalidOperationException("0");
            case 1: throw new System.ArgumentException("1");
            case 2: throw new System.ArgumentNullException("2");
            case 3: throw new System.NotSupportedException("3");
            case 4: throw new System.FormatException("4");
            case 5: throw new System.OverflowException("5");
            case 6: throw new System.InvalidCastException("6");
            case 7: throw new System.TimeoutException("7");
            case 8: throw new System.NotImplementedException("8");
        }
    }

    public static int AllocatesManyPseudoExceptions()
    {
        var a0 = new PseudoException(0);
        var a1 = new PseudoException(1);
        var a2 = new PseudoException(2);
        var a3 = new PseudoException(3);
        var a4 = new PseudoException(4);
        var a5 = new PseudoException(5);
        var a6 = new PseudoException(6);
        var a7 = new PseudoException(7);
        return a0.Value + a1.Value + a2.Value + a3.Value + a4.Value + a5.Value + a6.Value + a7.Value;
    }

    // Pseudo-exceptions (non-Exception types named like exceptions) are real steady-state
    // allocations, so a loop densely constructing them is a hotspot (confirms they are not
    // wrongly excluded as exception construction).
    public static int AllocatesManyPseudoExceptionsInLoop(int n)
    {
        var total = 0;
        for (int i = 0; i < n; i++)
        {
            total += new PseudoException(0).Value + new PseudoException(1).Value + new PseudoException(2).Value
                + new PseudoException(3).Value + new PseudoException(4).Value + new PseudoException(5).Value
                + new PseudoException(6).Value + new PseudoException(7).Value + new PseudoException(8).Value
                + new PseudoException(9).Value + new PseudoException(10).Value + new PseudoException(11).Value
                + new PseudoException(12).Value + new PseudoException(13).Value + new PseudoException(14).Value
                + new PseudoException(15).Value + new PseudoException(16).Value + new PseudoException(17).Value;
        }
        return total;
    }

    // Densely constructs VALUE types in a loop (Nullable + an in-assembly struct). None of
    // these `newobj` allocate on the heap, so the method must NOT be an allocation-hotspot
    // even though it clears the >= 16 newobj count.
    public static int ConstructsManyValueTypesInLoop(int n)
    {
        var total = 0;
        for (int i = 0; i < n; i++)
        {
            System.Nullable<int> n0 = new System.Nullable<int>(0); System.Nullable<int> n1 = new System.Nullable<int>(1);
            System.Nullable<int> n2 = new System.Nullable<int>(2); System.Nullable<int> n3 = new System.Nullable<int>(3);
            System.Nullable<int> n4 = new System.Nullable<int>(4); System.Nullable<int> n5 = new System.Nullable<int>(5);
            System.Nullable<int> n6 = new System.Nullable<int>(6); System.Nullable<int> n7 = new System.Nullable<int>(7);
            System.Nullable<int> n8 = new System.Nullable<int>(8); System.Nullable<int> n9 = new System.Nullable<int>(9);
            var p0 = new PlainValue(0); var p1 = new PlainValue(1); var p2 = new PlainValue(2);
            var p3 = new PlainValue(3); var p4 = new PlainValue(4); var p5 = new PlainValue(5);
            var p6 = new PlainValue(6); var p7 = new PlainValue(7);
            total += (n0 ?? 0) + (n1 ?? 0) + (n2 ?? 0) + (n3 ?? 0) + (n4 ?? 0) + (n5 ?? 0) + (n6 ?? 0)
                + (n7 ?? 0) + (n8 ?? 0) + (n9 ?? 0) + p0.V + p1.V + p2.V + p3.V + p4.V + p5.V + p6.V + p7.V;
        }
        return total;
    }

    public static int AllocatesManyPlainObjects()
    {
        var a0 = new PlainObject(0);
        var a1 = new PlainObject(1);
        var a2 = new PlainObject(2);
        var a3 = new PlainObject(3);
        var a4 = new PlainObject(4);
        var a5 = new PlainObject(5);
        var a6 = new PlainObject(6);
        var a7 = new PlainObject(7);
        return a0.Value + a1.Value + a2.Value + a3.Value + a4.Value + a5.Value + a6.Value + a7.Value;
    }

    public static void ManyCustomThrowArms(int code)
    {
        switch (code)
        {
            case 0: throw new FixtureException("0");
            case 1: throw new FixtureException("1");
            case 2: throw new FixtureException("2");
            case 3: throw new FixtureException("3");
            case 4: throw new FixtureException("4");
            case 5: throw new FixtureException("5");
            case 6: throw new FixtureException("6");
            case 7: throw new FixtureException("7");
        }
    }

    public static void ManyDerivedCustomThrowArms(int code)
    {
        switch (code)
        {
            case 0: throw new DerivedFixtureException("0");
            case 1: throw new DerivedFixtureException("1");
            case 2: throw new DerivedFixtureException("2");
            case 3: throw new DerivedFixtureException("3");
            case 4: throw new DerivedFixtureException("4");
            case 5: throw new DerivedFixtureException("5");
            case 6: throw new DerivedFixtureException("6");
            case 7: throw new DerivedFixtureException("7");
        }
    }

    public static void ManyCrossAssemblyCustomThrowArms(int code)
    {
        switch (code)
        {
            case 0: throw new CrossAssemblyDerivedFixtureException("0");
            case 1: throw new CrossAssemblyDerivedFixtureException("1");
            case 2: throw new CrossAssemblyDerivedFixtureException("2");
            case 3: throw new CrossAssemblyDerivedFixtureException("3");
            case 4: throw new CrossAssemblyDerivedFixtureException("4");
            case 5: throw new CrossAssemblyDerivedFixtureException("5");
            case 6: throw new CrossAssemblyDerivedFixtureException("6");
            case 7: throw new CrossAssemblyDerivedFixtureException("7");
        }
    }

    // A single steady-state allocation inside a loop, below the hotspot threshold and
    // matching no specific shape, so it surfaces NO opportunity row. It still allocates
    // in a loop, so MethodSignals.AllocInLoop must be true (the loop bit is independent
    // of the opportunity/threshold machinery).
    public static object AllocatesOnceInLoop(int n)
    {
        object last = new object();
        for (int i = 0; i < n; i++)
        {
            last = new object();
        }
        return last;
    }

    public static object AllocatesPseudoExceptionOnceInLoop(int n)
    {
        object last = new PseudoException(0);
        for (int i = 0; i < n; i++)
        {
            last = new PseudoException(i);
        }
        return last;
    }

    // Exception construction inside a loop only allocates on the throw path, so it must
    // not set AllocInLoop (error-path allocation is not steady-state hot pay-dirt).
    public static void ThrowsInLoop(int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (i < 0)
                throw new System.InvalidOperationException("never");
        }
    }

    // Early-return allocation inside a loop: the return exits the frame, so the
    // allocation runs at most once even though its IL offset sits in the loop.
    public static object? ReturnsObjectFromLoop(int n)
    {
        for (int i = 0; i < n; i++)
        {
            ConsumeBoolean(i > 0);
            if (i == 5)
                return new PlainObject(i);
        }
        return null;
    }

    public static void ThrowsInitializedExceptionInLoop(int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (i < 0)
                throw new System.InvalidOperationException("never") { HelpLink = "https://example.invalid" };
        }
    }

    public static void ThrowsConditionalExceptionInLoop(int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (i < 0)
                throw i == -1
                    ? new System.InvalidOperationException("never")
                    : new System.ArgumentException("never");
        }
    }

    // A real exception object retained (stored) per iteration is a steady-state hot
    // allocation, not a throw-path one, so MethodSignals.AllocInLoop must be true (#1610).
    public static System.Exception[] RetainsExceptionsInLoop(int count)
    {
        var errors = new System.Exception[count];
        for (int i = 0; i < count; i++)
        {
            errors[i] = new System.InvalidOperationException(i.ToString());
        }
        return errors;
    }
}

public sealed class GenericSelfSiblingFixture<T>
{
    public int Read(T value) => 0;

    public async Task<int> ReadAsync(T value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public sealed class GenericPrivateSiblingFixture<T>
{
    private static int Read(T value) => 0;

    private static Task<int> ReadAsync(T value)
        => Task.FromResult(Read(value));

    public static async Task<int>
        CallsPrivateSiblingFromAsync(T value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public static class RequiredTokenSiblingFixture
{
    public static int Read(int value) => value;

    public static Task<int> ReadAsync(
        int value,
        CancellationToken cancellationToken)
        => Task.FromResult(value);

    public static async Task<int>
        CallsRequiredTokenSiblingAsync(int value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public class UnrelatedVirtualSiblingService
{
    public int Load() => 42;

    public virtual Task<int> LoadAsync()
        => Task.FromResult(42);
}

public sealed class UnrelatedVirtualSiblingConsumer
{
    readonly UnrelatedVirtualSiblingService _service =
        new();

    public async Task<int> LoadAsync()
    {
        await Task.Yield();
        return _service.Load();
    }
}

public class NewSlotVirtualSiblingBase
{
    public int Load() => 42;

    public virtual Task<int> LoadAsync()
        => Task.FromResult(42);
}

public class NewSlotVirtualSiblingMiddle
    : NewSlotVirtualSiblingBase
{
    public new virtual Task<int> LoadAsync()
        => Task.FromResult(43);
}

public sealed class NewSlotVirtualSiblingLeaf
    : NewSlotVirtualSiblingMiddle
{
    public override async Task<int> LoadAsync()
    {
        await Task.Yield();
        return ((NewSlotVirtualSiblingBase)this)
            .Load();
    }
}

public class GenericNewSlotSiblingBase<TFirst, TSecond>
{
    public TFirst Read(TFirst value) => value;

    public virtual Task<TFirst> ReadAsync(TFirst value)
        => Task.FromResult(value);
}

public class InheritedAsyncSiblingBase<T>
{
    public Task<T> ReadAsync(T value)
        => Task.FromResult(value);
}

public sealed class InheritedAsyncSiblingDerived<T>
    : InheritedAsyncSiblingBase<T>
{
    public T Read(T value) => value;

    public async Task<T> AnalyzeAsync(T value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public class HiddenInheritedAsyncSiblingBase
{
    public Task<int> ReadAsync(int value)
        => Task.FromResult(value);
}

public sealed class HiddenInheritedAsyncSiblingDerived
    : HiddenInheritedAsyncSiblingBase
{
    public int Read(int value) => value;

    public new Task<string> ReadAsync(int value)
        => Task.FromResult(value.ToString());

    public async Task<int> AnalyzeAsync(int value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public class NearestInheritedAsyncSiblingBase
{
    public Task<int> ReadAsync(int value)
        => Task.FromResult(value);
}

public class NearestInheritedAsyncSiblingMiddle
    : NearestInheritedAsyncSiblingBase
{
    public new Task<int> ReadAsync(int value)
        => Task.FromResult(value);
}

public sealed class NearestInheritedAsyncSiblingLeaf
    : NearestInheritedAsyncSiblingMiddle
{
    public int Read(int value) => value;

    public async Task<int> AnalyzeAsync(int value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public class ProtectedInheritedAsyncSiblingBase
{
    protected Task<int> ReadAsync(int value)
        => Task.FromResult(value);
}

public sealed class ProtectedInheritedAsyncSiblingDerived
    : ProtectedInheritedAsyncSiblingBase
{
    public int Read(int value) => value;

    public async Task<int> AnalyzeAsync(int value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public static class ProtectedInheritedAsyncSiblingConsumer
{
    public static async Task<int> AnalyzeAsync(
        ProtectedInheritedAsyncSiblingDerived value)
    {
        await Task.Yield();
        return value.Read(1);
    }
}

public class InternalInheritedAsyncSiblingBase
{
    internal Task<int> ReadAsync(int value)
        => Task.FromResult(value);
}

public sealed class InternalInheritedAsyncSiblingDerived
    : InternalInheritedAsyncSiblingBase
{
    public int Read(int value) => value;

    public async Task<int> AnalyzeAsync(int value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public class InheritedSynchronousSiblingBase
{
    public int Read(int value) => value;

    public Task<int> ReadAsync(int value)
        => Task.FromResult(value);
}

public sealed class HiddenInheritedSynchronousSiblingDerived
    : InheritedSynchronousSiblingBase
{
    public new Task<string> ReadAsync(int value)
        => Task.FromResult(value.ToString());

    public async Task<int> AnalyzeAsync(int value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public static class InheritedSynchronousSiblingConsumer
{
    public static async Task<int> AnalyzeAsync(
        InheritedSynchronousSiblingBase value)
    {
        await Task.Yield();
        return value.Read(1);
    }
}

public static class InheritedSynchronousDerivedReceiverConsumer
{
    public static async Task<int> AnalyzeAsync(
        HiddenInheritedSynchronousSiblingDerived value)
    {
        await Task.Yield();
        return value.Read(1);
    }
}

public static class InheritedSynchronousNameBypassConsumer
{
    public static async Task<int> ReadAsync(
        HiddenInheritedSynchronousSiblingDerived value)
    {
        await Task.Yield();
        return value.Read(1);
    }
}

public interface IInterfaceInheritedSynchronousBase
{
    int Read(int value);

    Task<int> ReadAsync(int value);
}

public interface IInterfaceInheritedSynchronousDerived
    : IInterfaceInheritedSynchronousBase
{
    new Task<string> ReadAsync(int value);
}

public static class InterfaceInheritedSynchronousConsumer
{
    public static async Task<int> AnalyzeAsync(
        IInterfaceInheritedSynchronousDerived value)
    {
        await Task.Yield();
        return value.Read(1);
    }
}

public class SameTypeInheritedSynchronousBase
{
    public int Read(int value) => value;

    public Task<int> ReadAsync(int value)
        => Task.FromResult(value);

    public async Task<int> AnalyzeAsync(
        SameTypeInheritedSynchronousDerived value)
    {
        await Task.Yield();
        return value.Read(1);
    }
}

public sealed class SameTypeInheritedSynchronousDerived
    : SameTypeInheritedSynchronousBase
{
    public new Task<string> ReadAsync(int value)
        => Task.FromResult(value.ToString());
}

public class StaticInheritedSynchronousBase
{
    public static int Read(int value) => value;

    public static Task<int> ReadAsync(int value)
        => Task.FromResult(value);
}

public sealed class StaticInheritedSynchronousDerived
    : StaticInheritedSynchronousBase
{
    public new static Task<string> ReadAsync(int value)
        => Task.FromResult(value.ToString());
}

public static class StaticInheritedSynchronousConsumer
{
    public static async Task<int> AnalyzeAsync(int value)
    {
        await Task.Yield();
        return StaticInheritedSynchronousDerived.Read(value);
    }
}

public sealed class SealedSynchronousSiblingService
{
    public int Read(int value) => value;

    public Task<int> ReadAsync(int value)
        => Task.FromResult(value);
}

public static class SealedSynchronousSiblingConsumer
{
    public static async Task<int> AnalyzeAsync(
        SealedSynchronousSiblingService value)
    {
        await Task.Yield();
        return value.Read(1);
    }
}

public class GenericNewSlotSiblingMiddle<TFirst, TSecond>
    : GenericNewSlotSiblingBase<TSecond, TFirst>
{
    public virtual Task<TFirst> ReadAsync(
        TFirst value)
        => Task.FromResult(value);
}

public sealed class GenericNewSlotSiblingLeaf<TFirst, TSecond>
    : GenericNewSlotSiblingMiddle<TFirst, TSecond>
{
    public override async Task<TSecond> ReadAsync(
        TSecond value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public class GenericMatchingNewSlotSiblingBase<T>
{
    public T Read(T value) => value;

    public virtual Task<T> ReadAsync(T value)
        => Task.FromResult(value);
}

public class GenericMatchingNewSlotSiblingMiddle<T>
    : GenericMatchingNewSlotSiblingBase<T>
{
    public new virtual Task<T> ReadAsync(T value)
        => Task.FromResult(value);
}

public sealed class GenericMatchingNewSlotSiblingLeaf<T>
    : GenericMatchingNewSlotSiblingMiddle<T>
{
    public override async Task<T> ReadAsync(T value)
    {
        await Task.Yield();
        return ((GenericMatchingNewSlotSiblingBase<T>)this)
            .Read(value);
    }
}

public class UnrelatedOverrideSiblingBase
{
    public virtual Task<int> LoadAsync()
        => Task.FromResult(41);
}

public class UnrelatedOverrideSiblingService
{
    public int Load() => 42;

    public virtual Task<int> LoadAsync()
        => Task.FromResult(42);
}

public sealed class UnrelatedOverrideSiblingConsumer
    : UnrelatedOverrideSiblingBase
{
    readonly UnrelatedOverrideSiblingService _service =
        new();

    public override async Task<int> LoadAsync()
    {
        await Task.Yield();
        return _service.Load();
    }
}

public static class GenericInvocationSiblingFixture
{
    public static int Read<T>() =>
        typeof(T) == typeof(int) ? 1 : 2;

    public static Task<int> ReadAsync<T>() =>
        Task.FromResult(
            typeof(T) == typeof(int) ? 1 : 2);

    public static async Task<int>
        CallsDifferentInstantiationAsync()
    {
        int value = Read<int>();
        _ = await ReadAsync<string>();
        return value;
    }

    public static async Task<int>
        CallsSameInstantiationAsync()
    {
        int value = Read<int>();
        _ = await ReadAsync<int>();
        return value;
    }
}

public interface IInterfaceSelfSiblingFixture
{
    int Read();
    Task<int> ReadAsync();
}

public sealed class InterfaceSelfSiblingFixture
    : IInterfaceSelfSiblingFixture
{
    public int Read() => 0;

    public async Task<int> ReadAsync()
    {
        await Task.Yield();
        return ((IInterfaceSelfSiblingFixture)this).Read();
    }
}

public static class ConstrainedSiblingFixture
{
    public static T Read<T>(T value) => value;

    public static Task<T> ReadAsync<T>(T value)
        where T : class
        => Task.FromResult(value);

    public static async Task<int>
        CallsConstrainedSiblingFromAsync()
    {
        await Task.Yield();
        return Read(1);
    }

    public static T ReadAllowsRefStruct<T>(T value)
        where T : allows ref struct
        => value;

    public static Task<T> ReadAllowsRefStructAsync<T>(
        T value)
        => Task.FromResult(value);

    public static async Task<int>
        CallsAllowsRefStructFromAsync()
    {
        await Task.Yield();
        Span<int> values = stackalloc int[1];
        return ReadAllowsRefStruct(values).Length;
    }
}

public class VirtualSelfSiblingFixture
{
    public virtual int Read() => 0;

    public virtual Task<int> ReadAsync()
        => Task.FromResult(Read());
}

public sealed class DerivedVirtualSelfSiblingFixture
    : VirtualSelfSiblingFixture
{
    public override async Task<int> ReadAsync()
    {
        await Task.Yield();
        return ((VirtualSelfSiblingFixture)this).Read();
    }
}

public static class UnsupportedSignatureSiblingFixture
{
    public static unsafe void Read(
        delegate*<int, void> callback)
        => callback(1);

    public static unsafe Task ReadAsync(
        delegate*<string, void> callback)
    {
        callback("");
        return Task.CompletedTask;
    }

    public static async Task AnalyzeAsync()
    {
        await Task.Yield();
        unsafe
        {
            Read(&Consume);
        }
    }

    static void Consume(int value)
    {
    }
}

public sealed class PseudoException
{
    public PseudoException(int value) => Value = value;

    public int Value { get; }
}

public sealed class PlainObject
{
    public PlainObject(int value) => Value = value;

    public int Value { get; }
}

public sealed class AllocationMetadataOuter<T>
{
    public sealed class Inner<U>
    {
    }
}

// An in-assembly value type: `new PlainValue(...)` does not allocate on the heap, so it must
// not count toward allocation-hotspot density.
public struct PlainValue
{
    public PlainValue(int v) => V = v;

    public int V { get; }
}

// #1804 / rung 7. Consumes the cross-assembly shape fixtures (defined in an
// external fixture assembly, which the SRM-direct product does not load) plus
// an in-assembly struct. Each method constructs a value as a method ARGUMENT in a
// loop, which forces a `newobj` (a value assigned to a local emits `call .ctor` instead), so
// the allocation signal's shape classification is exercised.
public static class CrossAsmShapeConsumer
{
    static int SinkInAssembly(PlainValue value) => value.V;
    static int SinkStruct(CrossAsmShapeFixtures.CrossValueStruct value) => value.Value;
    static int SinkGeneric(CrossAsmShapeFixtures.CrossGenericStruct<int> value) => value.Value;
    static int SinkRef(CrossAsmShapeFixtures.CrossRefType value) => value.Value;

    public static int ConstructsInAssemblyStructInLoop(int n)
    {
        int total = 0;
        for (int i = 0; i < n; i++)
            total += SinkInAssembly(new PlainValue(i));
        return total;
    }

    public static int ConstructsCrossNonGenericStructInLoop(int n)
    {
        int total = 0;
        for (int i = 0; i < n; i++)
            total += SinkStruct(new CrossAsmShapeFixtures.CrossValueStruct(i));
        return total;
    }

    public static int ConstructsCrossGenericStructInLoop(int n)
    {
        int total = 0;
        for (int i = 0; i < n; i++)
            total += SinkGeneric(new CrossAsmShapeFixtures.CrossGenericStruct<int>(i));
        return total;
    }

    public static int ConstructsCrossRefTypeInLoop(int n)
    {
        int total = 0;
        for (int i = 0; i < n; i++)
            total += SinkRef(new CrossAsmShapeFixtures.CrossRefType(i));
        return total;
    }

    public static int UsesCrossEnum(int n)
    {
        var value = (CrossAsmShapeFixtures.CrossEnum)(n % 2);
        return (int)value;
    }
}

public sealed class FixtureException : Exception
{
    public FixtureException(string message) : base(message)
    {
    }
}

public sealed class DerivedFixtureException : InvalidOperationException
{
    public DerivedFixtureException(string message) : base(message)
    {
    }
}

public sealed class CrossAssemblyDerivedFixtureException : ExceptionBaseFixtures.ExternalFixtureException
{
    public CrossAssemblyDerivedFixtureException(string message) : base(message)
    {
    }
}

public struct BoxFixtureStruct
{
    public int X;
}

public struct BoxFixtureStruct<T>
{
    public T Value;
}

// A source-generated type (mirrors the [GeneratedCode] System.Text.Json source generator
// emits): its MakesSmallArray would be a small-array opportunity, but #1273 suppresses
// optimization opportunities for [GeneratedCode] types.
[System.CodeDom.Compiler.GeneratedCode("TestSourceGenerator", "1.0")]
public class SourceGeneratedOptimizationFixtures
{
    public static int[] MakesSmallArray() => new int[4];

    public static Func<T, bool> SourceGeneratedTypeLambda<T>(T right)
        => left => left!.Equals(right);
}

[System.CodeDom.Compiler.GeneratedCode("TestSourceGenerator", "1.0")]
public class SourceGeneratedClassicAsyncOuter
{
    public class Nested
    {
        static int Read() => 42;

        static Task<int> ReadAsync() => Task.FromResult(42);

        public static async Task<int> AnalyzeAsync()
        {
            await Task.Yield();
            return Read();
        }
    }
}

public class GeneratedMethodNestingFixtures
{
    public class Nested
    {
        [System.CodeDom.Compiler.GeneratedCode("test", "1.0")]
        public static bool SourceGeneratedNestedLocal<T>(T left, T right)
        {
            return NestedCore(left, right);

            static bool NestedCore(T x, T y) => x!.Equals(y);
        }

        [System.CodeDom.Compiler.GeneratedCode("test", "1.0")]
        public static Func<T, bool> SourceGeneratedNestedLambda<T>(T right)
            => left => left!.Equals(right);
    }
}

public class MixedGeneratedOverloadFixtures
{
    [System.CodeDom.Compiler.GeneratedCode("test", "1.0")]
    public static bool Handle<T>(T left, T right, int marker)
    {
        return GeneratedCore(left, right);

        static bool GeneratedCore(T x, T y) => x!.Equals(y);
    }

    public static bool Handle<T>(T left, T right, string marker)
    {
        return AuthoredCore(left, right);

        static bool AuthoredCore(T x, T y) => x!.Equals(y);
    }
}

public static class MemberRefLiftedOverloadFixture<TOuter>
{
    public static bool Owner<T>(T left, T right, int marker)
    {
        return Core(left, right) & Core(left, right);

        static bool Core(T x, T y) => x!.Equals(y);
    }

    public static bool Owner<T>(T left, T right, string marker)
    {
        return Core(left, right, marker.Length);

        static bool Core(T x, T y, int ignored) => x!.Equals(y);
    }
}

[System.Runtime.CompilerServices.CompilerGenerated]
public class CompilerGeneratedOwnerContainer
{
    public static bool CompilerGeneratedTypeOwner<T>(T left, T right)
    {
        return EqualsCore(left, right);

        static bool EqualsCore(T x, T y) => x!.Equals(y);
    }
}

public static class ResolvedNestedLiftedOwnerFixture
{
    public static bool Ultimate<T>(T left, T right)
        => AAAAAAAAAAAAAAAAAAAA(left, right);

    public static bool AAAAAAAAAAAAAAAAAAAA<T>(
        T left,
        T right)
        => BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB(
            left,
            right);

    public static bool BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB<T>(
        T left,
        T right)
        => left!.Equals(right);
}

public static class GeneratedUltimateNestedFixture
{
    [System.CodeDom.Compiler.GeneratedCode("test", "1.0")]
    public static bool GeneratedUltimate<T>(
        T left,
        T right)
        => CCCCCCCCCCCCCCCCCCCCCCCCCCCCC(
            left,
            right);

    public static bool CCCCCCCCCCCCCCCCCCCCCCCCCCCCC<T>(
        T left,
        T right)
        => DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD(
            left,
            right);

    public static bool DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD<T>(
        T left,
        T right)
        => left!.Equals(right);
}

public static unsafe class MemberRefFunctionPointerFixture<TOuter>
{
    public static bool Owner<T>(
        T left,
        T right,
        delegate*<int, void> marker)
    {
        return Core(left, right, marker);

        static bool Core(
            T x,
            T y,
            delegate*<int, void> ignored) => x!.Equals(y);
    }

    public static bool Owner<T>(
        T left,
        T right,
        delegate*<string, void> marker)
    {
        return Core(left, right, marker);

        static bool Core(
            T x,
            T y,
            delegate*<string, void> ignored) => x!.Equals(y);
    }
}

public static class OpportunityLeverageFixtures
{
    // Root1/Root2 are roots (no in-assembly caller); both reach Allocator, whose small
    // array opportunity should carry the joined Root Reach of 2.
    public static void Root1() => Consume(Allocator());

    public static void Root2() => Consume(Allocator());

    public static int[] Allocator() => new int[3];

    private static void Consume(int[] data) => Console.WriteLine(data.Length);
}

// A positional record: every synthesized member (EqualityContract/PrintMembers/Equals/
// GetHashCode/ToString/Deconstruct) is [CompilerGenerated], so none should yield
// optimization-opportunity rows.
public record OpportunityRecordFixture(int Value, string Name);

public static class CallerTreeFixtures
{
    public static void RootCall() => Mid();

    public static void Mid() => Inner();

    public static void Inner() => Console.WriteLine("leaf");
}

public static class LeverageFixtures
{
    public static void A() => Hot();

    public static void B() => Hot();

    public static void C()
    {
        Hot();
        Cold();
    }

    public static void Hot() => Console.WriteLine("hot");

    public static void Cold() => Console.WriteLine("cold");

    public static void Fanned()
    {
        A();
        B();
        C();
        for (int i = 0; i < 3; i++)
            Hot();
    }
}

public static class LeverageDepthFixtures
{
    public static void ChainTop() => ChainMid();

    public static void ChainMid() => ChainLeaf();

    public static void ChainLeaf() => Console.WriteLine("leaf");

    // Mutually recursive pair; Pong also reaches the chain. Used to confirm the
    // longest-chain walk terminates on cycles and reports a path-length that does
    // not depend on which method's ranking computed a shared node first.
    public static void Ping() => Pong();

    public static void Pong()
    {
        Ping();
        ChainTop();
    }
}

public static class LeverageRootReachFixtures
{
    // Two distinct roots (no in-assembly caller) both funnel into Single through Funnel.
    // Demonstrates that Root Reach (distinct reaching roots) can exceed direct fanin:
    // Single has one direct caller yet sits under two roots.
    public static void Root1() => Funnel();

    public static void Root2() => Funnel();

    public static void Funnel() => Single();

    public static void Single() => Console.WriteLine("single");
}

public static class LeverageSelfRootFixtures
{
    // Self-recursive entry point with no other in-assembly caller: still a root.
    public static void SelfRoot()
    {
        if (Environment.TickCount > 0)
            SelfRoot();
        Helper();
    }

    public static void Helper() => Console.WriteLine("helper");
}

public class OpaqueUnsafeTests
{
    static MethodIdentity Method(string name, CallerUnsafeMode mode, TypeRef returnType, params TypeRef[] parameterTypes)
        => new(
            AssemblyName: "Fixture",
            ModuleVersionId: Guid.Empty,
            DeclaringType: TypeRef.Definition("Fixture", "Ns", "Holder"),
            Name: name,
            ParameterTypes: [.. parameterTypes],
            ReturnType: returnType,
            MetadataToken: name.GetHashCode(),
            IsStatic: true,
            CallerUnsafeMode: mode);

    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void IsOpaque_RequiresUnsafeWithNoPointerSignature_IsOpaque()
    {
        // unsafe modifier / RequiresUnsafeAttribute on a pointerless signature:
        // the obligation is invisible to a caller reading parameter/return types.
        var contract = Method("ContractUnsafe", CallerUnsafeMode.Explicit, Int, TypeRef.SzArray(Int));
        Assert.True(OpaqueUnsafe.IsOpaque(contract));
    }

    [Fact]
    public void IsOpaque_PointerSignature_IsNotOpaque()
    {
        // The pointer is visible in the signature, so the unsafety is not hidden.
        var pointerParam = Method("TakesPointer", CallerUnsafeMode.Implicit, Int, TypeRef.Pointer(Int));
        var pointerReturn = Method("ReturnsPointer", CallerUnsafeMode.Explicit, TypeRef.Pointer(Int), Int);
        Assert.False(OpaqueUnsafe.IsOpaque(pointerParam));
        Assert.False(OpaqueUnsafe.IsOpaque(pointerReturn));
    }

    [Fact]
    public void IsOpaque_NotRequiresUnsafe_IsNotOpaque()
    {
        var safe = Method("Safe", CallerUnsafeMode.None, Int, Int);
        Assert.False(OpaqueUnsafe.IsOpaque(safe));
    }

    [Fact]
    public void Collect_SelectsOnlyOpaqueMethods_OrderedByToken()
    {
        var methods = ImmutableArray.Create(
            Method("Safe", CallerUnsafeMode.None, Int, Int),
            Method("TakesPointer", CallerUnsafeMode.Implicit, Int, TypeRef.Pointer(Int)),
            Method("Contract", CallerUnsafeMode.Explicit, Int, TypeRef.SzArray(Int)),
            Method("Hollow", CallerUnsafeMode.Explicit, Int));

        var opaque = OpaqueUnsafe.Collect(methods);

        Assert.Equal(["Contract", "Hollow"], opaque.Select(o => o.Method.Name).Order());
        Assert.All(opaque, o => Assert.NotEqual(CallerUnsafeMode.None, o.Mode));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void OpaqueUnsafeMethods_PointerSignatureFixtureIsNotOpaque()
    {
        // The in-test fixture assembly is not opted into the updated memory-safety
        // rules, so its only requires-unsafe methods carry a pointer signature —
        // none should be reported as opaque.
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        var opaque = index.OpaqueUnsafeMethods();

        Assert.DoesNotContain(opaque, o => o.Method.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead));
        Assert.All(opaque, o => Assert.False(
            o.Method.ParameterTypes.Any(t => t.ContainsPointer()) || o.Method.ReturnType.ContainsPointer()));
    }
}

public class HollowUnsafeTests
{
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");

    static MethodIdentity Method(string name, CallerUnsafeMode mode, TypeRef? returnType = null, params TypeRef[] parameterTypes)
        => new(
            AssemblyName: "Fixture",
            ModuleVersionId: Guid.Empty,
            DeclaringType: TypeRef.Definition("Fixture", "Ns", "Holder"),
            Name: name,
            ParameterTypes: [.. parameterTypes],
            ReturnType: returnType ?? Int,
            MetadataToken: name.GetHashCode(),
            IsStatic: true,
            CallerUnsafeMode: mode);

    static UnsafeEvidence Structural(MethodIdentity method, string kind)
        => new(method, "structural", kind, kind, ILOffset: null, OperandToken: null);

    static UnsafeEvidence Realized(MethodIdentity method, string kind)
        => new(method, "Unsafe operation", kind, kind, ILOffset: 0x10, OperandToken: null);

    [Fact]
    public void IsHollow_RequiresUnsafeWithNoEvidence_IsHollow()
    {
        var hollow = Method("HollowUnsafe", CallerUnsafeMode.Explicit);
        Assert.True(HollowUnsafe.IsHollow(hollow, []));
    }

    [Fact]
    public void IsHollow_OnlyStructuralEvidence_IsHollow()
    {
        // A pointer signature / pointer local is structural (no IL offset); the
        // body still shows no realized unsafe operation.
        var signatureOnly = Method("SignatureOnlyUnsafe", CallerUnsafeMode.Explicit, returnType: TypeRef.CoreLib("System", "Boolean"), TypeRef.Pointer(Int));
        ImmutableArray<UnsafeEvidence> evidence = [Structural(signatureOnly, "signature")];
        Assert.True(HollowUnsafe.IsHollow(signatureOnly, evidence));
    }

    [Fact]
    public void IsHollow_RealizedBodyOp_IsNotHollow()
    {
        var real = Method("RealUnsafePointer", CallerUnsafeMode.Explicit, returnType: Int, TypeRef.Pointer(Int));
        ImmutableArray<UnsafeEvidence> evidence = [Structural(real, "signature"), Realized(real, "opcode")];
        Assert.False(HollowUnsafe.IsHollow(real, evidence));
    }

    [Fact]
    public void IsHollow_NotRequiresUnsafe_IsNotHollow()
    {
        var safe = Method("Safe", CallerUnsafeMode.None);
        Assert.False(HollowUnsafe.IsHollow(safe, []));
    }

    [Fact]
    public void Collect_SelectsRequiresUnsafeMethodsWithoutRealizedOps_OrderedByToken()
    {
        var safe = Method("Safe", CallerUnsafeMode.None);
        var hollow = Method("Hollow", CallerUnsafeMode.Explicit);
        var signatureOnly = Method("SignatureOnly", CallerUnsafeMode.Explicit, returnType: Int, TypeRef.Pointer(Int));
        var real = Method("Real", CallerUnsafeMode.Explicit, returnType: Int, TypeRef.Pointer(Int));
        var delegated = Method("Delegated", CallerUnsafeMode.Implicit, returnType: Int, TypeRef.Pointer(Int));

        var methods = ImmutableArray.Create(safe, hollow, signatureOnly, real, delegated);
        ImmutableArray<UnsafeEvidence> evidence =
        [
            Structural(signatureOnly, "signature"),
            Structural(real, "signature"),
            Realized(real, "opcode"),
            Structural(delegated, "signature"),
            Realized(delegated, "call"),   // an unsafe call is a realized body op
        ];

        var result = HollowUnsafe.Collect(methods, evidence);

        // Hollow (no realized op) and SignatureOnly (only structural) qualify;
        // Real (deref) and Delegated (unsafe call) do not; Safe is not unsafe.
        Assert.Equal(["Hollow", "SignatureOnly"], result.Select(h => h.Method.Name).Order());
        Assert.All(result, h => Assert.NotEqual(CallerUnsafeMode.None, h.Mode));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void HollowUnsafeMethods_PointerDereferenceFixtureIsNotHollow()
    {
        // UnsafePointerRead dereferences its pointer parameter, so it carries a
        // realized (IL-offset-anchored) body op and must not be reported hollow.
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        var hollow = index.HollowUnsafeMethods();

        Assert.DoesNotContain(hollow, h => h.Method.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead));
        Assert.All(hollow, h => Assert.False(
            HollowUnsafe.HasRealizedUnsafeOp(h.Method, index.UnsafeEvidence)));
    }
}

public class CallTreeTests
{
    static readonly LibraryBodyIndex Index = LibraryBodyIndex.Open(typeof(CallTreeFixtures).Assembly.Location);

    static int Token(string methodName)
        => Index.Methods
            .First(method => method.DeclaringType.Name == nameof(CallTreeFixtures) && method.Name == methodName)
            .MetadataToken;

    static CallTreeNode? Find(CallTreeNode node, string memberName)
    {
        if (node.Member.Name == memberName)
            return node;
        foreach (var child in node.Children)
            if (Find(child, memberName) is { } match)
                return match;
        return null;
    }

    [Fact]
    public void BuildCallTree_ExpandsInAssemblyChain()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Root)), maxDepth: 5, maxNodes: 100);

        Assert.Equal(nameof(CallTreeFixtures.Root), tree.Member.Name);
        var one = Assert.Single(tree.Children, child => child.Member.Name == nameof(CallTreeFixtures.LevelOne));
        var two = Assert.Single(one.Children);
        Assert.Equal(nameof(CallTreeFixtures.LevelTwo), two.Member.Name);
        var three = Assert.Single(two.Children);
        Assert.Equal(nameof(CallTreeFixtures.LevelThree), three.Member.Name);
    }

    [Fact]
    public void BuildCallTree_PopulatesAllocationAndCopySignals()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.AllocatesAndCopies)), maxDepth: 1, maxNodes: 100);

        // new List<int>(...) and the object[] literal -> two newobj allocations.
        Assert.True(tree.Perf?.SignalsOrNone.Allocations >= 2, $"expected >= 2 allocations, got {tree.Perf?.SignalsOrNone.Allocations}");
        // data.ToArray() -> one copy.
        Assert.True(tree.Perf?.SignalsOrNone.Copies >= 1, $"expected >= 1 copy, got {tree.Perf?.SignalsOrNone.Copies}");
    }

    [Fact]
    public void BuildCallTree_CountsArrayAllocationsInAllocSignal()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.AllocatesArray)), maxDepth: 1, maxNodes: 100);

        // newarr is folded into Allocations, and its IL offset is retained as evidence.
        Assert.True(tree.Perf?.SignalsOrNone.Allocations >= 1, $"expected >= 1 alloc, got {tree.Perf?.SignalsOrNone.Allocations}");
        Assert.NotEmpty(tree.Perf!.SignalsOrNone.Evidence);
    }

    [Fact]
    public void BuildCallTree_PopulatesThrowSignal()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Throws)), maxDepth: 1, maxNodes: 100);

        Assert.True(tree.Perf?.SignalsOrNone.Throws >= 1, $"expected >= 1 throw, got {tree.Perf?.SignalsOrNone.Throws}");
    }

    [Fact]
    public void BuildCallTree_PopulatesConstructedExceptionTypesSeparatelyFromThrowSites()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Throws)), maxDepth: 1, maxNodes: 100);
        var signals = tree.Perf?.SignalsOrNone;

        // Throw-site count and constructed exception type are distinct signals (#1277).
        Assert.True(signals?.Throws >= 1);
        Assert.Contains("InvalidOperationException", signals!.ExceptionTypes);
    }

    [Fact]
    public void ConstructedExceptionTypes_ExcludesInAssemblyNonExceptionLookalike()
    {
        // #1572: an in-assembly type named *Exception that does not derive from
        // System.Exception is a heap allocation but not a constructed exception.
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.ConstructsLookalikeException)), maxDepth: 1, maxNodes: 100);
        var signals = tree.Perf?.SignalsOrNone;

        Assert.True(signals?.Allocations >= 1, "the lookalike newobj is still an allocation");
        Assert.DoesNotContain(nameof(PseudoException), signals!.ExceptionTypes);
    }

    [Fact]
    public void ConstructedExceptionTypes_IncludesInAssemblyCustomException()
    {
        // #1572: an in-assembly type that derives from System.Exception still reports.
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.ConstructsCustomException)), maxDepth: 1, maxNodes: 100);
        var signals = tree.Perf?.SignalsOrNone;

        Assert.Contains(nameof(CustomDomainException), signals!.ExceptionTypes);
    }

    [Fact]
    public void ConstructedExceptionTypes_ExternalSuffixLookalike_PinsSuffixFallbackBoundary()
    {
        // #1623 rung 2 boundary pin. The product is SRM-direct and does not load the
        // referenced assembly, so it cannot prove that ExternalPseudoException does NOT
        // derive from System.Exception. The documented fallback is the "*Exception"
        // name suffix, so this cross-assembly non-exception lookalike IS counted. This
        // is a deliberately-owned false positive at the no-referenced-assembly-loading
        // boundary (Invariant 1) — pinned so the heuristic is explicit, not silent. If
        // referenced-assembly base resolution is ever added, this assertion flips to
        // DoesNotContain.
        var signals = SignalsForMethod(nameof(CallTreeFixtures.ConstructsExternalSuffixLookalike));

        Assert.True(signals.Allocations >= 1, "the cross-assembly newobj is still an allocation");
        Assert.Contains(nameof(ExceptionBaseFixtures.ExternalPseudoException), signals.ExceptionTypes);
    }

    [Fact]
    public void ConstructedExceptionTypes_ExternalUnsuffixedException_PinsSuffixFallbackBoundary()
    {
        // #1623 rung 2 boundary pin (the dual of the test above). ExternalErrorState
        // really derives from System.Exception, but its name does not end with
        // "Exception" and its base chain is in a referenced assembly the SRM-direct
        // product does not load, so the suffix fallback misses it. This is a
        // deliberately-owned false negative at the same boundary — the allocation is
        // still counted, only the exception-type classification degrades.
        var signals = SignalsForMethod(nameof(CallTreeFixtures.ConstructsExternalUnsuffixedException));

        Assert.True(signals.Allocations >= 1, "the cross-assembly newobj is still an allocation");
        Assert.DoesNotContain(nameof(ExceptionBaseFixtures.ExternalErrorState), signals.ExceptionTypes);
    }

    static MethodSignals SignalsForMethod(string methodName)
    {
        int token = Index.Methods.First(method => method.Name == methodName).MetadataToken;
        return Index.BuildCallTree(token, maxDepth: 1, maxNodes: 100).Perf!.SignalsOrNone;
    }

    [Fact]
    public void ConstructedExceptionTypes_ExcludesNestedInAssemblyLookalike()
    {
        // #1572 review: a nested in-assembly type decodes as "ExceptionHost+Nested...",
        // so the in-assembly map must key by the decoded name or the lookalike leaks
        // back through the suffix fallback.
        var signals = SignalsForMethod(nameof(ExceptionHost.MakesNestedLookalike));

        Assert.True(signals.Allocations >= 1);
        Assert.DoesNotContain(signals.ExceptionTypes, name => name.Contains("Pseudo", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void ConstructedExceptionTypes_IncludesNestedInAssemblyException()
    {
        var signals = SignalsForMethod(nameof(ExceptionHost.MakesNestedReal));

        Assert.Contains(signals.ExceptionTypes, name => name.Contains(nameof(ExceptionHost.NestedRealException), StringComparison.Ordinal));
    }

    [Fact]
    public void ConstructedExceptionTypes_IncludesExceptionWithClosedGenericBase()
    {
        // The base is a closed generic (TypeSpecification) we do not resolve, so the
        // type is left to the conservative name-suffix fallback — and its name ends
        // with "Exception", so it still reports.
        var signals = SignalsForMethod(nameof(GenericExceptionFixtures.MakesGenericBaseDerived));

        Assert.Contains(nameof(ClosedGenericDerivedException), signals.ExceptionTypes);
    }

    [Fact]
    public void ConstructedExceptionTypes_RecordsGenericExceptionDefinitionName()
    {
        // A constructed generic exception is a GenericInstance with an empty Name; the
        // recorded entry must be the underlying definition name, not a blank string.
        var signals = SignalsForMethod(nameof(GenericExceptionFixtures.MakesDirectGeneric));

        Assert.DoesNotContain("", signals.ExceptionTypes);
        Assert.Contains(signals.ExceptionTypes, name => name.StartsWith("DirectGenericException", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildCallTree_PopulatesCatchAndFinallySignals()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.TryCatchFinally)), maxDepth: 1, maxNodes: 100);

        Assert.True(tree.Perf?.SignalsOrNone.Catches >= 1, $"expected >= 1 catch, got {tree.Perf?.SignalsOrNone.Catches}");
        Assert.True(tree.Perf?.SignalsOrNone.Finallys >= 1, $"expected >= 1 finally, got {tree.Perf?.SignalsOrNone.Finallys}");
    }

    [Fact]
    public void BuildCallTree_PopulatesReflectionSignal()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Reflects)), maxDepth: 1, maxNodes: 100);

        // Type.GetMethods + Activator.CreateInstance == 2; the two typeof lowerings
        // (Type.GetTypeFromHandle) must not be counted.
        Assert.Equal(2, tree.Perf?.SignalsOrNone.Reflection);
    }

    [Fact]
    public void BuildCallTree_StopsAtDepthLimit()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Root)), maxDepth: 2, maxNodes: 100);

        var two = Find(tree, nameof(CallTreeFixtures.LevelTwo));
        Assert.NotNull(two);
        Assert.Equal(CallTreeStatus.DepthLimited, two!.Status);
        // Depth-limited, but LevelTwo still calls LevelThree: true fan-out is reported.
        Assert.Equal(1, two.Perf?.Fanout);
        Assert.Empty(two.Children);
        Assert.Null(Find(tree, nameof(CallTreeFixtures.LevelThree)));
    }

    [Fact]
    public void BuildCallTree_RecordsCrossAssemblyCalleesAsExternalLeaves()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.LevelThree)), maxDepth: 5, maxNodes: 100);

        var external = Assert.Single(tree.Children);
        Assert.Equal("WriteLine", external.Member.Name);
        Assert.Equal(CallTreeStatus.External, external.Status);
        Assert.Empty(external.Children);
    }

    [Fact]
    public void BuildCallTree_MarksCyclesAsAlreadyShown()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Ping)), maxDepth: 5, maxNodes: 100);

        var pong = Assert.Single(tree.Children);
        Assert.Equal(nameof(CallTreeFixtures.Pong), pong.Member.Name);
        var pingAgain = Assert.Single(pong.Children);
        Assert.Equal(nameof(CallTreeFixtures.Ping), pingAgain.Member.Name);
        Assert.Equal(CallTreeStatus.AlreadyShown, pingAgain.Status);
        // Shown above, but Ping still calls Pong: true fan-out is reported.
        Assert.Equal(1, pingAgain.Perf?.Fanout);
        Assert.Empty(pingAgain.Children);
    }

    [Fact]
    public void BuildCallTree_TruncatesAtNodeCap()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Root)), maxDepth: 5, maxNodes: 2);

        Assert.Equal(CallTreeStatus.Truncated, tree.Status);
        Assert.Single(tree.Children);
        // Only one child fit the node budget, but Root's true fan-out (LevelOne + External) is 2.
        Assert.Equal(2, tree.Perf?.Fanout);
    }

    [Fact]
    public void BuildCallTree_LeafMethodHasNoChildren()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Leaf)), maxDepth: 5, maxNodes: 100);

        Assert.Equal(CallTreeStatus.Leaf, tree.Status);
        Assert.Empty(tree.Children);
    }
}

public static class CallSiteFixtures
{
    public static void CallsConsoleWriteLine() => Console.WriteLine("hello");

    public static void CallsConsoleWriteLineInLoop(int iterations)
    {
        for (int i = 0; i < iterations; i++)
            CallsConsoleWriteLine();
    }

    // Exercises a forward branch (the early-return guard) appearing before the loop.
    // A loop-region scan that double-advances on the forward branch desyncs and misses
    // the loop's backward branch, so the in-loop call would be misreported as not-in-loop.
    public static void GuardThenCallsInLoop(int iterations, bool skip)
    {
        if (skip)
            return;
        for (int i = 0; i < iterations; i++)
            CallsConsoleWriteLine();
    }

    public static string? CallsVirtualToString(object value) => value.ToString();

    public static void CallsListAdd(List<int> values) => values.Add(42);

    // Intra-assembly generic method: the call site below references it by a
    // MethodSpec token (the int instantiation), not the method's MethodDef token.
    public static T GenericEcho<T>(T value) => value;

    public static int CallsGenericEcho() => GenericEcho(42);

    public static object ExactAllocationLeaf() => new();

    public static object CallsExactAllocationLeaf() => ExactAllocationLeaf();

    public static object? ConditionallyCallsExactAllocationLeaf(bool create)
        => create ? ExactAllocationLeaf() : null;
}

public static class GenericDeclaringCallers
{
    public static int CallGenericTarget() => GenericDeclaringTarget<int>.Target();

    public static int CallGenericTargetWithParameter() => GenericDeclaringTarget<int>.TargetWithParameter(41);

    public static unsafe int CallUnsafeGenericTarget(int* value) => GenericUnsafeTarget<int>.UnsafeTarget(value);
}

public static class GenericDeclaringTarget<T>
{
    public static int Target() => 42;

    public static T TargetWithParameter(T value) => value;
}

public static class GenericUnsafeTarget<T>
{
    public static unsafe int UnsafeTarget(int* value) => *value;
}

public interface ICallerGraphTarget
{
    void Target();
}

public static class BodilessRootFixtures
{
    // The callvirt references ICallerGraphTarget.Target by its (bodiless) interface
    // method token, so a Caller Graph rooted at that token must still find this caller.
    public static void InvokesThroughInterface(ICallerGraphTarget target) => target.Target();
}

public class VirtualDispatchBase
{
    public virtual int Work() => 1;
}

public sealed class VirtualDispatchDerived : VirtualDispatchBase
{
    public override int Work() => 2;

    public int CallsBaseThenVirtual(
        VirtualDispatchBase value) =>
        base.Work() + value.Work();

    public int CallsVirtualThenBaseInLoop(
        VirtualDispatchBase value,
        int count)
    {
        int result = value.Work();
        for (int i = 0; i < count; i++)
            result += base.Work();
        return result;
    }
}

public class FinalVirtualDispatchDerived : VirtualDispatchBase
{
    public sealed override int Work() => 3;
}

public static class VirtualDispatchCallers
{
    // C# emits `callvirt VirtualDispatchBase::Work` because `value` is statically typed Base.
    public static int ViaBase(VirtualDispatchBase value) => value.Work();

    // A VirtualDispatchDerived is constructed, but the local is typed Base, so the callvirt
    // operand is still VirtualDispatchBase::Work; the override is only reached at runtime.
    public static int ViaDerived()
    {
        VirtualDispatchBase value = new VirtualDispatchDerived();
        return value.Work();
    }

    public static int ViaNonVirtual(
        NonVirtualDispatchTarget value) =>
        value.Work();
}

public sealed class NonVirtualDispatchTarget
{
    public int Work() => 3;
}

public static class CallTreeFixtures
{
    public static void Root()
    {
        LevelOne();
        External();
    }

    public static void LevelOne() => LevelTwo();

    public static void LevelTwo() => LevelThree();

    public static void LevelThree() => Console.WriteLine("leaf");

    public static void External() => Console.WriteLine("external");

    public static void Ping() => Pong();

    public static void Pong() => Ping();

    public static void Leaf() { }

    // Two newobj (List<int> ×2) -> allocations; ToArray -> copy.
    public static int AllocatesAndCopies(int[] data)
    {
        var list = new List<int>(data);
        var more = new List<int>();
        var copy = data.ToArray();
        return list.Count + more.Count + copy.Length;
    }

    // newarr -> folded into Allocations (alloc), with an evidence offset.
    public static int[] AllocatesArray() => new int[4];

    // throw site (and a newobj for the exception object).
    public static void Throws(int x)
    {
        if (x < 0)
            throw new InvalidOperationException("negative");
    }

    // try/catch + finally -> one catch clause, one finally clause.
    public static int TryCatchFinally(int x)
    {
        try { return 100 / x; }
        catch (DivideByZeroException) { return -1; }
        finally { GC.KeepAlive(x); }
    }

    // System.Type reflection (GetMethods) + System.Activator.CreateInstance -> reflection.
    // The typeof(...) lowering (Type.GetTypeFromHandle) must NOT count as reflection.
    public static object? Reflects()
    {
        _ = typeof(CallTreeFixtures).GetMethods();
        return System.Activator.CreateInstance(typeof(CallTreeFixtures));
    }

    // Constructs an in-assembly type whose simple name ends with "Exception" but which
    // does NOT derive from System.Exception (#1572). It is a heap allocation but must
    // not be reported as a constructed exception.
    public static object ConstructsLookalikeException() => new PseudoException(0);

    // Constructs an in-assembly type that actually derives from System.Exception; it
    // must still be reported as a constructed exception.
    public static object ConstructsCustomException() => new CustomDomainException();

    // Cross-assembly boundary pins (#1623 rung 2). These construct types defined in a
    // referenced assembly whose base chain the SRM-direct product cannot walk, so
    // exception classification falls back to the documented "*Exception" suffix.
    public static object ConstructsExternalSuffixLookalike()
        => new ExceptionBaseFixtures.ExternalPseudoException();

    public static object ConstructsExternalUnsuffixedException()
        => new ExceptionBaseFixtures.ExternalErrorState("external");
}

// Reflection-signal recall fixtures (#1623 rung 3 / #1715). One canary per
// IsReflectionApi branch so a regression removing any branch fails recall:
//   - System.Reflection.* namespace prefix (MethodInfo.Invoke, Assembly.GetTypes)
//   - System.Linq.Expressions namespace
//   - System.Type curated member set, beyond the GetMethods covered by Reflects
// System.Activator is already covered by CallTreeFixtures.Reflects.
public static class ReflectionRecallFixtures
{
    public static object? InvokesViaReflection(System.Reflection.MethodInfo method, object target)
        => method.Invoke(target, null);

    public static System.Type[] EnumeratesAssemblyTypes(System.Reflection.Assembly assembly)
        => assembly.GetTypes();

    public static System.Linq.Expressions.ConstantExpression BuildsExpression()
        => System.Linq.Expressions.Expression.Constant(42);

    // Four distinct System.Type curated-set members -> Reflection count 4.
    public static int CallsTypeMemberSet(System.Type type)
    {
        int found = 0;
        if (type.GetProperty("Length") is not null) found++;
        if (type.GetField("value") is not null) found++;
        if (type.GetConstructor(System.Type.EmptyTypes) is not null) found++;
        if (type.MakeArrayType() is not null) found++;
        return found;
    }
}

// An in-assembly custom exception that derives from System.Exception.
public sealed class CustomDomainException : Exception;

// Nested in-assembly types: the call index keys these as "ExceptionHost+Nested*",
// so the in-assembly map must use the same decoded name to match (#1572 review).
public static class ExceptionHost
{
    public sealed class NestedPseudoException;
    public sealed class NestedRealException : Exception;

    public static object MakesNestedLookalike() => new NestedPseudoException();
    public static object MakesNestedReal() => new NestedRealException();
}

// Generic exception shapes: a type whose base is a closed generic (TypeSpecification)
// exception, and a directly-constructed generic exception instance.
public class GenericExceptionBase<T> : Exception;
public sealed class ClosedGenericDerivedException : GenericExceptionBase<int>;
public sealed class DirectGenericException<T> : Exception;

public static class GenericExceptionFixtures
{
    public static object MakesGenericBaseDerived() => new ClosedGenericDerivedException();
    public static object MakesDirectGeneric() => new DirectGenericException<int>();
}

public static partial class UnsafeEvidenceFixtures
{
    public static unsafe int UnsafePointerRead(int* value) => *value;

    public static uint CallsUnsafeAs(ref int value) => Unsafe.As<int, uint>(ref value);

    [DllImport("kernel32.dll")]
    public static extern int PInvokeOnly();

    [DllImport("kernel32.dll")]
    public static unsafe extern int PointerExtern(int* value);

    public static unsafe int PointerExternCallerA(int* value) => PointerExtern(value);

    public static unsafe int PointerExternCallerB(int* value) => PointerExtern(value);
}

// Two independent containing types that each nest a type with the same simple name
// `Dup`. Their nested declaring-type identity must stay distinct (NestedLeft.Dup vs
// NestedRight.Dup); collapsing to the innermost `Dup` merges the two Target methods
// in graph keys / FindCalls (#1554).
public static class NestedLeft
{
    public static class Dup
    {
        public static void Target() { }
    }

    public static void Call() => Dup.Target();
}

public static class NestedRight
{
    public static class Dup
    {
        public static void Target() { }
    }

    public static void Call() => Dup.Target();
}

// A nested type under a generic outer type. The open-definition declaring identity is
// `GenericOuter`1+Inner`; its display must preserve the path and strip per-segment
// arity -> `GenericOuter.Inner`.
public static class GenericOuter<T>
{
    public static class Inner
    {
        public static void Leaf() { }
    }

    public static void Use() => Inner.Leaf();
}

// Three overloads sharing the simple name `M` but differing by signature. Analysis
// identity keys must keep them distinct (FindCalls, Call/Caller Graph, leverage),
// or calls to one overload are mis-credited to another (#1623 rung 1).
public static class OverloadTargets
{
    public static void M() { }

    public static void M(int value) { }

    public static void M(string value) { }
}

public static class OverloadCallers
{
    public static void CallsEachOverloadOnce()
    {
        OverloadTargets.M();
        OverloadTargets.M(1);
        OverloadTargets.M("x");
    }
}

// A collection whose GetEnumerator returns an untrusted IEnumerator lookalike (see
// EnumeratorLookalikeStub.cs). foreach binds to this GetEnumerator via the enumerator pattern;
// because the return type is not the trusted framework IEnumerator, it must not be flagged.
public sealed class LookalikeEnumeratorCollection
{
    public System.Collections.Generic.IEnumeratorLookalike GetEnumerator() => new();
}
