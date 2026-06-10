using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Decompiler;

namespace DotnetInspector.Decompiler.Tests;

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
    public static int Add(int a, int b) => a + b;

    public static bool IsPositive(int x) => x > 0;

    // --- Unsigned/unordered comparison fixtures (cgt.un/clt.un/b*.un) ---

    public static bool UnsignedBoundsCheck(int index, int[] array) => (uint)index < (uint)array.Length;

    public static string UnsignedBoundsBranch(int index, int[] array)
    {
        if ((uint)index >= (uint)array.Length)
            return "out";
        return "in";
    }

    public static bool FloatUnordered(double a, double b) => !(a <= b);

    public static bool NotNullIdiom(object o) => o != null;

    public static int UnsignedShift(int x, int n) => x >>> n;

    public static uint UnsignedDivide(uint a, uint b) => a / b;

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
}

public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }

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
