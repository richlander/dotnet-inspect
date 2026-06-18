using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Linq;
using ILInspector.Decompiler;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Tests for ControlFlowGraph construction ported from runtime's FlowGraph.
/// Validates basic block splitting, edge linking, and exception region handling.
/// </summary>
public class ControlFlowGraphTests
{
    [Fact]
    public void SimpleMethod_HasSingleBlock()
    {
        // Add(int, int) is ldarg.0 / ldarg.1 / add / ret — no branches
        var cfg = BuildCfg(nameof(CfgSampleClass.Add));
        Assert.Single(cfg.BasicBlocks);
        Assert.Empty(cfg.BasicBlocks[0].Targets);
        Assert.Empty(cfg.BasicBlocks[0].Sources);
    }

    [Fact]
    public void MethodWithBranch_HasMultipleBlocks()
    {
        var cfg = BuildCfg(nameof(CfgSampleClass.Classify));
        Assert.True(cfg.BasicBlocks.Count >= 3,
            $"Expected >= 3 blocks for if/else, got {cfg.BasicBlocks.Count}");
    }

    [Fact]
    public void FirstBlock_StartsAtZero()
    {
        var cfg = BuildCfg(nameof(CfgSampleClass.Classify));
        Assert.Equal(0, cfg.BasicBlocks[0].Start);
    }

    [Fact]
    public void Blocks_CoverEntireMethod()
    {
        var (cfg, context) = BuildCfgWithContext(nameof(CfgSampleClass.Classify));
        int totalSize = cfg.BasicBlocks.Sum(bb => bb.Size);
        Assert.Equal(context!.ILBytes.Length, totalSize);
    }

    [Fact]
    public void Blocks_AreContiguous()
    {
        var cfg = BuildCfg(nameof(CfgSampleClass.Classify));
        for (int i = 1; i < cfg.BasicBlocks.Count; i++)
        {
            var prev = cfg.BasicBlocks[i - 1];
            var curr = cfg.BasicBlocks[i];
            Assert.Equal(prev.Start + prev.Size, curr.Start);
        }
    }

    [Fact]
    public void ConditionalBranch_HasTwoTargets()
    {
        var cfg = BuildCfg(nameof(CfgSampleClass.Classify));

        // First block of Classify has a conditional branch (if x > 0) → 2 targets
        var first = cfg.BasicBlocks[0];
        Assert.Equal(2, first.Targets.Count);
    }

    [Fact]
    public void TargetsAndSources_AreConsistent()
    {
        var cfg = BuildCfg(nameof(CfgSampleClass.Classify));

        foreach (var bb in cfg.BasicBlocks)
        {
            foreach (var target in bb.Targets)
                Assert.Contains(bb, target.Sources);

            foreach (var source in bb.Sources)
                Assert.Contains(bb, source.Targets);
        }
    }

    [Fact]
    public void SwitchStatement_HasMultipleTargets()
    {
        var cfg = BuildCfg(nameof(CfgSampleClass.SwitchCase));
        // The block containing the switch should have 4+ targets
        var switchBlock = cfg.BasicBlocks.FirstOrDefault(bb => bb.Targets.Count >= 4);
        Assert.NotNull(switchBlock);
    }

    [Fact]
    public void TryCatch_SplitsAtExceptionBoundaries()
    {
        var (cfg, context) = BuildCfgWithContext(nameof(CfgSampleClass.TryCatch));
        Assert.True(context!.ExceptionRegions.Length > 0);

        // Exception region boundaries should create block splits
        foreach (var region in context.ExceptionRegions)
        {
            Assert.NotNull(cfg.Lookup(region.TryOffset));
            Assert.NotNull(cfg.Lookup(region.HandlerOffset));
        }
    }

    [Fact]
    public void Lookup_FindsCorrectBlock()
    {
        var cfg = BuildCfg(nameof(CfgSampleClass.Classify));

        foreach (var bb in cfg.BasicBlocks)
        {
            // Start offset should find this block
            Assert.Equal(bb, cfg.Lookup(bb.Start));

            // Middle of block should also find this block
            if (bb.Size > 1)
                Assert.Equal(bb, cfg.Lookup(bb.Start + 1));
        }
    }

    [Fact]
    public void Lookup_OutOfRange_ReturnsNull()
    {
        var cfg = BuildCfg(nameof(CfgSampleClass.Add));
        Assert.Null(cfg.Lookup(-1));
        Assert.Null(cfg.Lookup(1000));
    }

    [Fact]
    public void ReturnBlock_HasNoTargets()
    {
        var cfg = BuildCfg(nameof(CfgSampleClass.Add));
        var last = cfg.BasicBlocks[^1];
        Assert.Empty(last.Targets);
    }

    [Fact]
    public void MethodWithLoop_HasBackEdge()
    {
        var cfg = BuildCfg(nameof(CfgSampleClass.LoopSum));

        // A loop creates a back edge: some block targets an earlier block
        bool hasBackEdge = cfg.BasicBlocks.Any(bb =>
            bb.Targets.Any(t => t.Start <= bb.Start));
        Assert.True(hasBackEdge, "Expected a back edge for the loop");
    }

    [Theory]
    [InlineData(nameof(CfgSampleClass.Add))]
    [InlineData(nameof(CfgSampleClass.Classify))]
    [InlineData(nameof(CfgSampleClass.SwitchCase))]
    [InlineData(nameof(CfgSampleClass.TryCatch))]
    [InlineData(nameof(CfgSampleClass.TryFinally))]
    [InlineData(nameof(CfgSampleClass.LoopSum))]
    [InlineData(nameof(CfgSampleClass.NestedExceptionHandlers))]
    [InlineData(nameof(CfgSampleClass.ThrowAndRethrow))]
    public void AllMethods_ProduceValidCfg(string methodName)
    {
        var cfg = BuildCfg(methodName);
        Assert.NotEmpty(cfg.BasicBlocks);
        Assert.Equal(0, cfg.BasicBlocks[0].Start);

        // Blocks should be sorted by offset
        for (int i = 1; i < cfg.BasicBlocks.Count; i++)
            Assert.True(cfg.BasicBlocks[i].Start > cfg.BasicBlocks[i - 1].Start);
    }

    [Fact]
    public void PlatformAssembly_AllMethods_NoCrashes()
    {
        var assembly = typeof(object).Assembly;
        var path = assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        int totalMethods = 0;
        int totalBlocks = 0;
        List<string> failures = [];

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                totalMethods++;

                try
                {
                    var context = MethodBodyContext.Create(peReader, reader, method);
                    if (context is null)
                        continue;

                    var cfg = ControlFlowGraph.Create(context);
                    totalBlocks += cfg.BasicBlocks.Count;
                }
                catch (Exception ex)
                {
                    string typeName = reader.GetString(typeDef.Name);
                    string methodName = reader.GetString(method.Name);
                    failures.Add($"{typeName}::{methodName}: {ex.Message}");
                }
            }
        }

        Assert.True(totalMethods > 1000, $"Expected many methods, got {totalMethods}");
        Assert.True(totalBlocks > 5000, $"Expected many blocks, got {totalBlocks}");

        // Allow a small number of failures from methods with unusual IL (R2R stubs, etc.)
        double failureRate = (double)failures.Count / totalMethods;
        Assert.True(failureRate < 0.01,
            $"CFG failed for {failures.Count}/{totalMethods} methods ({failureRate:P1}):\n{string.Join("\n", failures.Take(20))}");
    }

    // --- Helpers ---

    static ControlFlowGraph BuildCfg(string methodName)
        => BuildCfgWithContext(methodName).Cfg;

    static (ControlFlowGraph Cfg, MethodBodyContext? Context) BuildCfgWithContext(string methodName)
    {
        var assemblyPath = typeof(CfgSampleClass).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(
            peReader,
            typeof(CfgSampleClass).FullName!,
            methodName);
        Assert.NotNull(context);
        return (ControlFlowGraph.Create(context), context);
    }
}

/// <summary>
/// Sample methods with known IL patterns for CFG testing.
/// </summary>
public class CfgSampleClass
{
    public static byte ToByte(int x) => (byte)x;

    public static int LengthOf(string s) => s.Length;

    public static int Twice(int x) { var t = x + x; return t; }

    public static int Reused(int x) { var n = x + 1; return n * n; }

    public static bool BothPositive(int a, int b)
    {
        if (a > 0)
        {
            if (b > 0)
            {
                return true;
            }
        }
        return false;
    }

    public static int Pick(bool c, int a, int b) => c ? a : b;

    public static int DoWhileSum(int n)
    {
        int s = 0;
        do { s += n; n--; } while (n > 0);
        return s;
    }

    // A do-while whose body breaks early: the break is a forward exit branch,
    // so the loop is out of the do-while slice and stays honestly flat.
    public static int DoWhileWithBreak(int n)
    {
        int s = 0;
        do
        {
            s += n;
            if (s > 100)
                break;
            n--;
        }
        while (n > 0);
        return s;
    }

    public static void Noop() { }

    public static int ParseOrZero(string s) => int.TryParse(s, out var v) ? v : 0;

    public static int FirstElement(int[] a) => a[0];

    public static void SetFirstElement(int[] a, int v) => a[0] = v;

    public static int TryFinallyAdd(int x)
    {
        try { return x + 1; }
        finally { LastValue = x; }
    }

    public static int TryFinallyTwoReturns(int x)
    {
        // Two returns inside a try compile to two leaves to distinct return
        // blocks; the EH pass inlines them back so the try/finally raises.
        try
        {
            if (x > 0)
                return x;
            return -1;
        }
        finally { LastValue = x; }
    }

    public static int LastValue;

    public static int FilteredLength(string s)
    {
        try { return s.Length; }
        catch (Exception e) when (e.Message.Length > 0) { return 0; }
    }

    public static int PowerOfTwo(int x) => x switch { 0 => 1, 1 => 2, 2 => 4, 3 => 8, _ => 0 };

    // --- EH structuring fixtures ---

    public static int CatchLogs(string s)
    {
        try { return int.Parse(s); }
        catch (FormatException e) { Console.WriteLine(e.Message); return -1; }
    }

    public static int CatchDiscards(string s)
    {
        try { return int.Parse(s); }
        catch (FormatException) { return 0; }
    }

    public static int CatchEverything(string s)
    {
        try { return int.Parse(s); }
        catch { return 0; }
    }

    public static int LogAndRethrow(string s)
    {
        try { return int.Parse(s); }
        catch (FormatException) { Console.WriteLine("bad"); throw; }
    }

    public static int TwoCatches(string s)
    {
        try { return int.Parse(s); }
        catch (FormatException) { return -1; }
        catch (OverflowException) { return -2; }
    }

    public static int ParseWithCleanup(string s, Action done)
    {
        try { return int.Parse(s); }
        catch (FormatException) { return 0; }
        finally { done(); }
    }

    [System.Runtime.InteropServices.DllImport("nonexistent")]
    private static extern void Overloaded(int x);

    public static void Overloaded(double x) { _ = x; }

    public static int Add(int a, int b) => a + b;

    public static bool IsPositive(int x) => x > 0;

    // Null-check on a generic instance: the brtrue operand is List<int>, a
    // reference type by IL well-formedness, so the guard renders `is null`.
    public static int CountOrZero(System.Collections.Generic.List<int> items)
    {
        if (items == null)
            return 0;
        return items.Count;
    }

    // Null-check on a non-generic reference type (CfgNullableTarget is defined
    // in this assembly) — same-assembly shape resolution renders `is null`.
    public static int GateOrZero(CfgNullableTarget gate)
    {
        if (gate == null)
            return 0;
        return gate.Value;
    }

    // Passes an enum constant into an enum parameter position: the ldc.i4.2
    // retypes to CfgPriority (defined in this assembly) and the printer names
    // it CfgPriority.High from the resolved member map, not the raw 2.
    public static void TakesPriority(CfgPriority priority) { _ = priority; }

    public static void CallWithHighPriority() => TakesPriority(CfgPriority.High);

    public static void TakesFlags(CfgFlags flags) { _ = flags; }

    // CfgFlags.Top = 0x80000000 emits as the signed int -2147483648; the
    // member-map key must reinterpret the uint the same way to name it.
    public static void CallWithTopFlag() => TakesFlags(CfgFlags.Top);

    // Returns an enum the method never loads as a value other than the literal:
    // the enum is reached only through the return-type signature, so it resolves
    // (and the ldc.i4.2 names CfgPriority.High) only when the signature is seeded.
    public static CfgPriority ReturnsHighPriority() => CfgPriority.High;

    // Bitwise mask of an enum: the ldc.i4.2 operand beside the enum value must
    // retype to CfgPriority, otherwise `p & 2` is CS0019 (enum & int).
    public static CfgPriority MaskHighPriority(CfgPriority p) => p & CfgPriority.High;

    // A non-constant int converted to an enum: IL carries an enum as its
    // underlying int with no conv, so the printer must re-insert the explicit
    // (CfgPriority)value cast — C# converts int->enum implicitly only for 0.
    public static CfgPriority ToPriority(int value) => (CfgPriority)value;

    // --- Unsigned/unordered comparison fixtures (cgt.un/clt.un/b*.un) ---

    public static bool UnsignedBoundsCheck(int index, int[] array) => (uint)index < (uint)array.Length;

    // --- Short-circuit condition chains ---

    public static string IfAnd(int a, int b)
    {
        if (a > 0 && b > 0)
            return "both";
        return "no";
    }

    public static string IfOr(int a, int b)
    {
        if (a > 0 || b > 0)
            return "either";
        return "neither";
    }

    public static string TripleAnd(int a, int b, int c)
    {
        if (a > 0 && b > 0 && c > 0)
            return "all";
        return "no";
    }

    public static string MixedAndOr(string? s, int n)
    {
        if (s != null && (s.Length > n || n < 0))
            return s;
        return "";
    }

    public static string UnsignedBoundsBranch(int index, int[] array)
    {
        if ((uint)index >= (uint)array.Length)
            return "out";
        return "in";
    }

    public static bool FloatUnordered(double a, double b) => !(a <= b);

    public static bool NotNullIdiom(object o) => o != null;

    public static int UnsignedShift(int x, int n) => x >>> n;

    public static int SignedShift(int x, int n) => x >> n;

    public static int LeftShift(int x, int n) => x << n;

    public static long LongLeftShift(long x, int n) => x << n;

    public static uint UnsignedDivide(uint a, uint b) => a / b;

    public static bool ULongGe(ulong a, ulong b) => a >= b;

    int _shadowed = 1;

    public int Shadowed(int _shadowed) => this._shadowed + _shadowed;

    public static string LowerBoundCheck(System.Array array)
    {
        if (array.GetLowerBound(0) != 0)
            return "nonzero";
        return "zero";
    }

    public static void ReverseCopy(int[] src, int[] dst, int dstIndex, int count)
    {
        int i = 0;
        int j = dstIndex + count;
        while (i < count)
            dst[--j] = src[i++];
    }

    public static void ChecksThenTry(int x)
    {
        if (x < 0)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (x > 100)
            throw new ArgumentException("too big");
        try
        {
            Console.WriteLine(x);
        }
        catch (InvalidOperationException)
        {
            throw new ArgumentException("bad");
        }
    }

    public static int DayNumber(string day)
    {
        switch (day)
        {
            case "mon": return 1;
            case "tue": return 2;
            case "wed": return 3;
            case "thu": return 4;
            case "fri": return 5;
            case "sat": return 6;
            case "sun": return 7;
            default: return 0;
        }
    }

    public static int SmallStringSwitch(string s)
    {
        switch (s)
        {
            case "a": return 1;
            case "b": return 2;
            default: return 0;
        }
    }

    public static int LenOrZero(object o)
    {
        var s = o as string;
        if (s != null)
            return s.Length;
        return 0;
    }

    public static int LenViaIsCast(object o)
    {
        if (o is string)
            return ((string)o).Length;
        return 0;
    }

    public static bool NeitherOr(bool a, bool b, bool c)
    {
        if (a && b)
            return false;
        return c;
    }

    public static T GetAt<T>(T[] array, int index) => array[index];

    public static bool IsValueTypeOf<T>() => typeof(T).IsValueType;

    public struct Pair { public int A; public int B; }

    public static int FirstA(Pair[] pairs) => pairs[0].A;

    public static ulong MaxULong(ulong a, ulong b)
    {
        if (a >= b)
            return a;
        return b;
    }

    public static int FilterCatch(string s)
    {
        try
        {
            return int.Parse(s);
        }
        catch (FormatException e) when (s.Length > 3)
        {
            return e.Message.Length;
        }
    }

    public static int NormalUsing(string s)
    {
        using var reader = new System.IO.StringReader(s);
        return reader.Read();
    }

    public static int FinallyWithExtraWork(string s)
    {
        var reader = new System.IO.StringReader(s);
        int count = 0;
        try
        {
            count = reader.Read();
        }
        finally
        {
            reader.Dispose();
            count = -1;
        }
        return count;
    }

    public class MutableHolder
    {
        public int Value;
    }

    public struct Money
    {
        public int Cents;
        public static Money operator +(Money a, Money b) => new() { Cents = a.Cents + b.Cents };
        public static Money operator -(Money m) => new() { Cents = -m.Cents };
        public static implicit operator int(Money m) => m.Cents;
    }

    public enum Color
    {
        Red,
        Green,
        Blue,
        Yellow,
    }

    public static Money NegateSum(Money a, Money b) => -(a + b);

    public static int MoneyToInt(Money m) => m;

    public static int DoubleViaLocalFunction(int x)
    {
        return Twice(x);

        static int Twice(int v) => v * 2;
    }

    static void ThrowOverflow() => throw new OverflowException();

    // CoreLib Math.Abs(short) shape: the throw is an out-of-line call reached
    // by fallthrough, with both guards jumping PAST it to the return.
    public static short AbsShortHelper(short value)
    {
        if (value < 0)
        {
            value = (short)-value;
            if (value < 0)
                ThrowOverflow();
        }
        return value;
    }

    public static short AbsShort(short value)
    {
        if (value < 0)
        {
            value = (short)-value;
            if (value < 0)
                throw new OverflowException();
        }
        return value;
    }

    public static int CountPositive(int[] values) => values.Count(v => v > 0);

    public static int CountAbove(int[] values, int min) => values.Count(v => v > min);

    public static int LoopWithContinue(int[] values)
    {
        int sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < 0)
                continue;
            sum += values[i];
        }
        return sum;
    }

    public static int LoopWithBreak(int[] values, int limit)
    {
        int sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (sum > limit)
                break;
            sum += values[i];
        }
        return sum;
    }

    public static string ColorName(Color c)
    {
        switch (c)
        {
            case Color.Red: return "red";
            case Color.Green: return "green";
            case Color.Blue: return "blue";
            case Color.Yellow: return "yellow";
            default: return "?";
        }
    }

    static int s_bumpCount;

    public static void Bump(MutableHolder h, int n)
    {
        h.Value += n;
        h.Value++;
        s_bumpCount++;
    }

    public static bool IsSpaceOrTab(char c) => c == ' ' || c == '\t';

    public static int StaleFieldRead(MutableHolder h)
    {
        int v = h.Value;
        h.Value = 99;
        return v + h.Value;
    }

    public static long ManualDisposeAsyncInFinally(System.IO.MemoryStream stream)
    {
        try
        {
            return stream.Length;
        }
        finally
        {
            _ = stream.DisposeAsync();
        }
    }

    public static string Classify(int x)
    {
        if (x > 0) return "positive";
        if (x < 0) return "negative";
        return "zero";
    }

    public static string SwitchCase(int x) => x switch
    {
        0 => "zero",
        1 => "one",
        2 => "two",
        3 => "three",
        _ => "other"
    };

    public static string SwitchStatement(int x)
    {
        switch (x)
        {
            case 0: return "zero";
            case 1: return "one";
            case 2: return "two";
            case 3: return "three";
            case 4: return "four";
            case 5: return "five";
            default: return "other";
        }
    }

    public static int TryCatch(string s)
    {
        try { return int.Parse(s); }
        catch (FormatException) { return -1; }
    }

    public static void TryFinally(Action action)
    {
        try { action(); }
        finally { Console.WriteLine("done"); }
    }

    public static int LoopSum(int n)
    {
        int sum = 0;
        for (int i = 0; i < n; i++)
            sum += i;
        return sum;
    }

    public static int NestedExceptionHandlers(string s)
    {
        try
        {
            try { return int.Parse(s); }
            catch (FormatException) { return -1; }
        }
        finally { Console.WriteLine("done"); }
    }

    public static int MultipleCatch(string s)
    {
        try
        {
            return int.Parse(s);
        }
        catch (FormatException)
        {
            return -1;
        }
        catch (OverflowException)
        {
            return -2;
        }
    }

    public static void ThrowAndRethrow()
    {
        try { throw new InvalidOperationException("test"); }
        catch { throw; }
    }

    public static int WhileLoop(int n)
    {
        int i = 0;
        while (i < n)
            i++;
        return i;
    }

    public static int DoWhileLoop(int n)
    {
        int i = 0;
        do { i++; } while (i < n);
        return i;
    }

    public static int LoopWithBreak(int[] arr)
    {
        int result = -1;
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] == 42)
            {
                result = i;
                break;
            }
        }
        return result;
    }

    public static int NestedLoops(int n, int m)
    {
        int sum = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                sum += i * j;
        return sum;
    }

    public static string Ternary(int x) => x > 0 ? "positive" : "non-positive";

    public static int TernaryInt(int a, int b) => a > b ? a : b;

    public static string StringInterpolation(string name, int age)
        => $"Hello, {name}! You are {age} years old.";

    public static int UsingStatement(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        return stream.ReadByte();
    }

    public static List<string> ForeachLoop(IEnumerable<int> items)
    {
        var result = new List<string>();
        foreach (var item in items)
            result.Add(item.ToString());
        return result;
    }

    public static Func<int, int> ClosureCapture(int offset)
    {
        return x => x + offset;
    }

    public static List<int> ClosureWithLinq(int[] items, int threshold)
    {
        return items.Where(x => x > threshold).ToList();
    }

    public static bool IsPositiveOrZero(int value)
    {
        return value >= 0;
    }

    public static bool AlwaysTrue()
    {
        return true;
    }

    public static bool AlwaysFalse()
    {
        return false;
    }

    public static void SetFlag(out bool flag)
    {
        flag = true;
    }

    public static void CallByRefTarget(int value)
    {
        ByRefTarget(ref value);
    }

    public static void ByRefTarget(ref int value)
    {
        value++;
    }

    public static void CallInTarget(int value)
    {
        InTarget(in value);
    }

    public static void InTarget(in int value)
    {
        Console.WriteLine(value);
    }

    public static void CallOutTarget()
    {
        OutTarget(out int value);
        Console.WriteLine(value);
    }

    public static void OutTarget(out int value)
    {
        value = 42;
    }

    public static bool BoolAnd(int x, int y)
    {
        return x > 0 && y > 0;
    }

    public static bool BoolOr(int x, int y)
    {
        return x > 0 || y > 0;
    }

    public static double DoubleConstant()
    {
        return 3.14d;
    }

    public static double DoubleWholeNumber()
    {
        return 1.0d;
    }

    public static double DoubleNaN()
    {
        return double.NaN;
    }

    public static double DoublePositiveInfinity()
    {
        return double.PositiveInfinity;
    }

    public static int? NullableReturn(bool flag)
    {
        if (flag) return 42;
        return null;
    }

    public static int CheckedAdd(int a, int b)
    {
        return checked(a + b);
    }

    public static short CheckedCast(int value)
    {
        return checked((short)value);
    }

    public static string NullCoalesce(string? a, string b)
    {
        return a ?? b;
    }

    public static string[] ArrayWithInit(string a)
    {
        return new string[] { a, "hello" };
    }

    public static int[] ArrayWithDynamicSize(int n)
    {
        var values = new int[n];
        values[0] = 1;
        return values;
    }

    public static void CallWithLocalEnum()
    {
        HandlePriority(CfgPriority.High);
    }

    public static void HandlePriority(CfgPriority p)
    {
        Console.WriteLine(p);
    }

    public static char ReturnChar(int value)
    {
        return (char)value;
    }

    public static ushort ReturnUInt16(int value)
    {
        return (ushort)value;
    }

    public static long LongConstArith(int x)
    {
        return x + 1L;
    }

    public static ulong ULongNegOne() => unchecked((ulong)-1);

    public static void AcceptsBool(bool flag) { }

    public static void PassesBoolFalse() => AcceptsBool(false);

    public static List<string> CollectionWithCapacity(List<string> values)
    {
        return [with(capacity: values.Count * 2), .. values];
    }

    public static HashSet<string> CollectionWithComparer()
    {
        return [with(System.StringComparer.OrdinalIgnoreCase), "Hello", "HELLO", "hello"];
    }

    public static int ReadOnlySpanCollectionExpression(int a)
    {
        ReadOnlySpan<int> values = [a, 42];
        return values[0] + values[1];
    }

    public static int ClassicLock(object gate)
    {
        lock (gate)
        {
            return gate.GetHashCode();
        }
    }

    public static int SystemThreadingLock(System.Threading.Lock gate)
    {
        lock (gate)
        {
            return gate.GetHashCode();
        }
    }

    public static void NullConditionalFieldAssignment(CfgNullableTarget? target, int value)
    {
        target?.Value = value;
    }

    public static void NullConditionalFieldCompoundAssignment(CfgNullableTarget? target, int value)
    {
        target?.Value += value;
    }

    public static void NullConditionalPropertyAssignment(CfgNullableTarget? target, string value)
    {
        target?.Text = value;
    }

    public static void NullConditionalPropertyCompoundAssignment(CfgNullableTarget? target, int value)
    {
        target?.Count += value;
    }

    public static void NullConditionalIndexerAssignment(CfgNullableTarget? target, int value)
    {
        target?[0] = value;
    }

    public static unsafe int UnsafeReadThroughAddress()
    {
        int value = 42;
        return *(&value);
    }

    public static unsafe nuint AddressAsNativeUInt()
    {
        int value = 42;
        return (nuint)(&value);
    }

    public static unsafe int UnsafeReadArrayElementAddress(int[] values)
    {
        fixed (int* p = &values[0])
        {
            return *p;
        }
    }

    public static unsafe nuint ArrayElementAddressAsNativeUInt(int[] values)
    {
        fixed (int* p = &values[0])
        {
            return (nuint)p;
        }
    }

    // A function-pointer parameter is a representable type: delegate*<int, int>
    // imports at Full fidelity and renders in C# function-pointer syntax (return
    // type last). It carries no node-level stop.
    public static unsafe void TakesFunctionPointer(delegate*<int, int> callback) { _ = callback; }

    // An unmanaged function-pointer parameter carries a calling convention that
    // must render as `delegate* unmanaged[Cdecl]<…>`.
    public static unsafe void TakesUnmanagedFunctionPointer(delegate* unmanaged[Cdecl]<int, void> callback) { _ = callback; }

    // Invoking a function pointer compiles to calli: the arguments and the
    // pointer value are pushed, then the call-site signature drives the call.
    // Raised to a CallIndirect and rendered as `callback(value)`.
    public static unsafe int InvokesFunctionPointer(delegate*<int, int> callback, int value) => callback(value);

    // A void-returning function-pointer invocation renders as a statement.
    public static unsafe void InvokesVoidFunctionPointer(delegate*<int, void> callback, int value) => callback(value);

    static unsafe delegate*<int, int> s_functionPointer;
    static int FunctionPointerTarget(int value) => value;

    // Storing a method address into a delegate*-typed field is a static ldftn
    // that no delegate constructor consumes; it raises to &Method.
    public static unsafe void StoresMethodAddress() => s_functionPointer = &FunctionPointerTarget;

    // An `in` parameter carries modreq(InAttribute) on the byref. The importer
    // sees through the modifier, so the body imports at Full fidelity and the
    // underlying ByRef(int) shape stays intact for the load-indirect unwrap.
    public static int InParameterSum(in int x, in int y) => x + y;

    // A volatile field's type carries modreq(IsVolatile); reading it must still
    // import at Full fidelity once the modifier is seen through.
    public static volatile int VolatileFlag;

    public static int ReadVolatileFlag() => VolatileFlag;

    static void Tick() { }
    void Instance() { }

    // A static method group: `ldftn Tick; newobj Action::.ctor(object, native
    // int)` with a null target. The DelegateConstructionPass raises this to
    // `new Action(Tick)` at Full fidelity.
    public static System.Action StaticMethodGroup() => new System.Action(Tick);

    // An instance method group: the target is `this`, so the method group drops
    // the qualifier to `new Action(Instance)`.
    public System.Action InstanceMethodGroup() => new System.Action(Instance);

    // localloc: `stackalloc byte[n]` lowers to a byte count, `localloc`, and a
    // pointer store. The importer raises localloc to a StackAllocate node so the
    // body imports at Full fidelity and prints `stackalloc byte[...]`.
    public static unsafe byte StackAllocFirst(int n)
    {
        byte* buffer = stackalloc byte[n];
        return buffer[0];
    }

    // A reference-type stack join: the ternary's two branches push a derived
    // and a base instance into one slot that merges to their common base
    // JoinBase. The importer types the slot JoinBase — an actual ancestor it
    // resolves by walking the same-assembly base chain, never a guess — so the
    // body imports at Full fidelity instead of stopping at a join-type unknown.
    public static string MergedReferenceSlot(bool flag)
        => (flag ? new JoinDerived() : new JoinBase()).Label;

    // An interface stack join: one arm is cast to the interface IJoinShape, the
    // other is a class that implements it. The slot merges to IJoinShape — an
    // interface one side resolves to and the other implements — exercising the
    // interface arm of the merge, distinct from the base-class walk above.
    public static string MergedInterfaceSlot(bool flag)
        => (flag ? (IJoinShape)new JoinDerived() : new JoinImpl()).Shape();

    // A ternary whose result is used as a receiver and then returned: the
    // compiler keeps it on the stack (a dup slot), so the folded ternary types
    // the declared slot. The arms are JoinDerived and JoinBase; the slot must
    // declare as the common base JoinBase, not the WhenTrue arm JoinDerived —
    // guarding Conditional.MergedType against narrowing to one branch.
    public static JoinBase MergedTernaryDeclaration(bool flag)
    {
        JoinBase node = flag ? new JoinDerived() : new JoinBase();
        node.Mark();
        return node;
    }

    // A null-conditional property access consumed inline by ?? — the inline use
    // keeps the value on the stack, so the compiler spills the receiver into a
    // stack slot, null-tests it, and reloads the spill to read the property on
    // the non-null path, reusing ONE slot for both the receiver (JoinBase) and
    // the result (string). NullConditionalPass raises this to node?.Label.
    public static string NullConditionalProperty(JoinBase node) => node?.Label ?? "none";

    // The call form: node?.Shape() consumed by ??. Same receiver-spill lowering,
    // raised to a null-conditional invocation node?.Shape().
    public static string NullConditionalCall(JoinBase node) => node?.Shape() ?? "none";

    // An interpolated string lowers to an in-place DefaultInterpolatedStringHandler
    // construction: `ldloca handler; call DefaultInterpolatedStringHandler::.ctor(
    // literalLength, formattedCount)`. The handler is a ref struct, so the compiler
    // always initializes it in place rather than via a copied temporary.
    // StructConstructorPass raises that call back to `handler = new
    // DefaultInterpolatedStringHandler(...)`; left alone it prints as the illegal
    // handler..ctor(...) (CS0201, "not a valid statement").
    public static string InterpolatedStruct(int value) => $"value={value} again={value}";

    // Candidates for the boolean-materialization pass: a select/diamond that
    // stores a literal 0/1 beside a genuine bool into a synthetic stack slot.
    // A select that puts a bool literal beside a genuine bool expression; the
    // compiler emits the literal as `0`, which the boolean-materialization pass
    // recovers as `false` so the slot declares bool, not int (CS0029).
    public static bool SelectBoolReturn(object gate, int x)
    {
        bool result = x > 0 ? false : x.GetHashCode() > 0;
        System.GC.KeepAlive(gate);
        return result;
    }

    // Same-assembly callees with explicit ref-kinds, so the importer can recover
    // each parameter's keyword from the MethodDef parameter rows.
    public static void RefHelper(ref int x) => x++;

    public static void OutHelper(out int x) => x = 0;

    public static void InHelper(in int x) { }

    // A caller forwarding a managed pointer to each ref-kind: the call sites must
    // print `ref`/`out` and leave the `in` argument bare (CS1620/CS1615).
    public static int RefKindCallSites(int a)
    {
        int r = a;
        RefHelper(ref r);
        OutHelper(out int o);
        InHelper(in r);
        return r + o;
    }
}

public interface IJoinShape
{
    string Shape();
}

public class JoinBase : IJoinShape
{
    public virtual string Label => "base";

    public string Shape() => "shape";

    public void Mark() { }
}

public sealed class JoinDerived : JoinBase
{
    public override string Label => "derived";
}

public sealed class JoinImpl : IJoinShape
{
    public string Shape() => "impl";
}

public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }

// uint-underlying with a high-bit member: the value 0x80000000 emits as the
// signed int -2147483648, the case the member-map key must agree on.
[System.Flags]
public enum CfgFlags : uint { None = 0, Top = 0x80000000u }

// A value-type instance method whose `this` value is read directly: returning
// `this` by value compiles to `ldarg.0; ldobj` (a load-indirect of the `this`
// managed pointer), which must render as `this`, not the CS0193 `*this`.
public struct CfgSelf
{
    public int Value;
    public CfgSelf Identity() => this;
}

public sealed class CfgNullableTarget
{
    public int Value;

    public string? Text { get; set; }

    public int Count { get; set; }

    public int this[int index]
    {
        get => Value + index;
        set => Value = value + index;
    }
}

/// <summary>Base type for constructor-chain fixtures (base(...) targets).</summary>
public class CtorChainBase
{
    public CtorChainBase() { }

    public CtorChainBase(string? message) => Message = message;

    public string? Message { get; }
}

/// <summary>
/// Constructor shapes the chain pass must render: a plain base call, a base
/// call whose argument carries control flow (the spilled-this <c>??</c>
/// shape), a <c>this(...)</c> delegation, and an implicit parameterless base.
/// </summary>
public sealed class CtorChainSamples : CtorChainBase
{
    public CtorChainSamples() { }                       // implicit base()

    public CtorChainSamples(string message) : base(message) { }

    public CtorChainSamples(int code) : base(code > 0 ? "positive" : null) { }

    public CtorChainSamples(string message, bool _) : base(message ?? "default") { }

    public CtorChainSamples(long value) : this(value.ToString()) { }
}

/// <summary>Lock shapes the lock-sugar pass must raise: a void lock, a lock with a value body, and a lock on a parameter.</summary>
public sealed class LockFixtureSamples
{
    readonly object _root = new();
    int _value;

    public void IncrementUnderLock()
    {
        lock (_root) { _value++; }
    }

    public int ReadUnderLock()
    {
        lock (_root) { return _value; }
    }

    public void LockOnParameter(object gate)
    {
        lock (gate) { _value = 1; }
    }
}
