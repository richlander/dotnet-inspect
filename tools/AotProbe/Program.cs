using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using InertText;

// Two workloads, chosen because the two Native AOT codegen knobs move them independently:
//
//   scan - InertString.IsPermitted walks the string scalar by scalar. This is the shape of
//          most of the CLI's text handling, and it is what OptimizationPreference moves.
//   pre  - a printable-ASCII prefilter (IndexOfAnyExceptInRange) that falls back to the scan
//          on a miss. A BCL vectorized helper, and it is what the instruction-set baseline
//          moves. Included because it is the candidate replacement for the scan, so the
//          question "does raising the baseline pay?" is only meaningful alongside it.
//
// Reported figures are the best of 9 timed repetitions. Minimum rather than mean: the
// quantity of interest is the cost of the work itself, and every source of noise on a shared
// machine only ever adds. Runs 1-2 of an earlier sweep on this hardware were inflated ~2x by
// unrelated load, including in the instruction-set-independent column, which is what
// motivated taking minima here.

string typename = "System.Collections.Generic.Dictionary`2+KeyCollection+Enumerator";
string signature = "public static async System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<T>> "
    + "LoadAsync(string path, CancellationToken token)";
string long4k = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 93))[..4096];

// Permitted by Field but not ASCII, so the prefilter misses and pays its overhead on top of
// the scan. This is the case that bounds the prefilter's downside.
string nonAscii = "Ярославль.Коллекции.Словарь`2+Перечислитель 中文名前 emoji \U0001F600 tail";

string label = args.Length > 0 ? args[0] : "?";
Console.WriteLine(
    $"# {label,-14} {(RuntimeFeature.IsDynamicCodeCompiled ? "JIT " : "NAOT")} "
    + $"v128={Flag(Vector128.IsHardwareAccelerated)} "
    + $"v256={Flag(Vector256.IsHardwareAccelerated)} "
    + $"v512={Flag(Vector512.IsHardwareAccelerated)}");

Report("typename", typename);
Report("signature", signature);
Report("long4k", long4k);
Report("nonAscii", nonAscii);

void Report(string name, string value)
{
    double scan = Best(() => InertString.IsPermitted(TextPolicy.Field, value));
    double prefiltered = Best(() => Prefilter(value));
    Console.WriteLine(
        $"{label,-14} {name,-10} {value.Length,5} scan={scan,9:F1} pre={prefiltered,8:F2} "
        + $"x={scan / prefiltered,6:F0}");
}

static string Flag(bool value) => value ? "1" : "0";

// Space (U+0020) through tilde (U+007E) are all graphic, so a string containing only them is
// permitted by every policy and needs no scan. No surrogate is in that range, so a string
// that passes has no multi-unit scalars either.
static bool Prefilter(string value)
    => value.AsSpan().IndexOfAnyExceptInRange(' ', '~') < 0
    || InertString.IsPermitted(TextPolicy.Field, value);

// Calibrates the iteration count to roughly 40 ms per repetition so that a 2 ns operation and
// a 13 us one are both measured over a useful interval without either taking minutes.
static double Best(Func<bool> operation)
{
    for (int i = 0; i < 50_000; i++)
    {
        _ = operation();
    }

    var probe = Stopwatch.StartNew();
    for (int i = 0; i < 20_000; i++)
    {
        _ = operation();
    }

    probe.Stop();

    double perOperation = Math.Max(probe.Elapsed.TotalNanoseconds / 20_000, 0.05);
    int count = (int)Math.Clamp(40_000_000 / perOperation, 10_000, 5_000_000);

    double best = double.MaxValue;
    bool sink = false;
    for (int repetition = 0; repetition < 9; repetition++)
    {
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            sink ^= operation();
        }

        timer.Stop();
        best = Math.Min(best, timer.Elapsed.TotalNanoseconds / count);
    }

    GC.KeepAlive(sink);
    return best;
}
