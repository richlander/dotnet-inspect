namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Sample methods with known IL patterns for CFG testing.
/// </summary>
public class CfgSampleClass
{
    // A non-public overload declared BEFORE the public one of the same name.
    // With publicOnly resolution, the visibility filter must skip this so the
    // overload index lands on the public overload below — masking the access
    // bits correctly (MethodAttributes.Public is the value 6, not a single bit,
    // so a naive `& Public` test lets internal/protected overloads through).
    internal static int VisibilityOverload() => 1;

    public static int VisibilityOverload(int ignored) => 2;

    public static byte ToByte(int x) => (byte)x;

    public static int LengthOf(string s) => s.Length;

    public static char LastChar(string s) => s[^1];

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

    public static int LastElement(int[] a) => a[^1];

    // Negative fixture: hand-written `a[a.Length - 1]` re-loads the array
    // directly (two ldarg, no receiver spill), unlike the `^1` lowering which
    // spills into one stack slot. IndexFromEndPass must NOT raise this — doing
    // so would recompile to a different opcode stream (ldarg dup … vs ldarg
    // ldarg …). Kept opcode-exact by the fidelity gate.
    public static int LastElementHandWritten(int[] a) => a[a.Length - 1];

    public static void SetFirstElement(int[] a, int v) => a[0] = v;

    // Compound assignment over an array element: `a[i] += v` captures &a[i] in a
    // dup slot and stores back through it. The expanded `a[i] = a[i] + v` form
    // (no slot) must NOT fold, so both spellings are kept for contrast.
    public static void ArrayElementAdd(int[] a, int i, int v) => a[i] += v;

    public static void ArrayElementShift(int[] a, int i, int n) => a[i] <<= n;

    public static void ArrayElementInc(int[] a, int i) => a[i]++;

    public static void ArrayElementExpandedAdd(int[] a, int i, int v) => a[i] = a[i] + v;

    public int CompoundField;

    // Compound assignment over an instance property and a ref target — both
    // compile identically to their `x = x + v` expansion (no dup), so folding to
    // `x += v` is unconditionally faithful.
    public int CompoundProperty { get; set; }

    public void PropertyAdd(int v) => CompoundProperty += v;

    public static void RefAdd(ref int p, int v) => p += v;

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

    // A switch over sparse masked values csc lowers to a comparison tree (not a
    // jump table), which the structuring pass leaves as a flat block graph.
    // `result` is still assigned on every case and the default before the read,
    // so CFG definite-assignment must prove it bare rather than `= default`.
    public static int ClassifyMode(int mode) => (mode & 0xF000) switch
    {
        0x1000 => 1,
        0x2000 => 2,
        0x4000 => 4,
        0x8000 => 8,
        0xA000 => 10,
        0xC000 => 12,
        _ => 0,
    };

    // Explicit gotos to a common exit — the forward-common-merge shape. The merge
    // is a short `return result;` tail reached by two unconditional gotos plus a
    // fallthrough, so the return-merge pass inlines the tail into each arm and the
    // guard tree above nests cleanly (the step-2 common-exit fold). `result` is
    // assigned on every path, so the CFG definite-assignment declares it bare.
    public static int GotoCommonExit(int x)
    {
        int result;
        if (x > 0)
        {
            if (x > 100)
            {
                result = 2;
                goto done;
            }
            result = 1;
            goto done;
        }
        result = 0;
    done:
        return result;
    }

    // A forward-common-merge whose merge is NOT a short return tail: it ends in a
    // guard, so the return-merge pass leaves it (its scale/shape guards reject a
    // non-return-tail merge) and the index-range structurer still cannot express
    // the past-region join — the body stays a goto graph. `result` is assigned on
    // every path before the read, so CFG definite-assignment still declares it bare.
    public static int GotoCommonExitGuardedMerge(int x)
    {
        int result;
        if (x > 0)
        {
            if (x > 100)
            {
                result = 2;
                goto done;
            }
            result = 1;
            goto done;
        }
        result = 0;
    done:
        if (result > 1)
            return result + 100;
        return result;
    }

    // A diamond whose false arm carries an internal guard that branches straight
    // to the shared merge — `if (y > 0) goto done;` from inside the false arm,
    // the merge lying past the false arm's lexical boundary. The merge ends in a
    // guard (not a short return tail), so the return-merge pass leaves it and the
    // join survives as a real block. The index-range model bailed here
    // (cond-target-past-region: the false-arm conditional's target is the region
    // join, which is > the arm's stop); the merge-exit recovery (step 3) raises
    // it, because the target is the region's tracked join. `r` is assigned on
    // every path before the read.
    public static int DiamondArmEarlyExitGuardedMerge(int x, int y)
    {
        int r = 0;
        if (x > 0)
            goto trueArm;
        if (y > 0)
            goto done;
        r = 1;
        goto done;
    trueArm:
        r = 2;
    done:
        if (r > 1)
            return r + 100;
        return r;
    }

    // A local assigned inside a lock body before the read after it. Modeling the
    // lock as its sequential body (rather than bailing) proves the bare decl.
    public static int LockedAssign(object gate, int x)
    {
        int result;
        lock (gate)
        {
            result = x + 1;
        }
        return result;
    }

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

    // `is T t` type pattern as a statement guard. csc lowers it to a
    // `t = o as T;` store gating an `if (t != null)`; IsPatternPass raises that
    // back to `if (o is string s)`, which recompiles to the same as/brtrue.
    public static int IsPatternGuard(object o)
    {
        if (o is string s)
            return s.Length;
        return -1;
    }

    // `is T t` used as a value in a short-circuit `&&` expression. The pattern
    // binds in the left conjunct and is read in the right.
    public static bool IsPatternConjunction(object o) => o is string s && s.Length > 0;

    // A property pattern lowers to the same as-store plus `t != null && t.P == k`;
    // recovered as `o is string s && s.Length == 5` (the deconstructed `{ ... }`
    // form is a later slice).
    public static int IsPatternProperty(object o)
    {
        if (o is string { Length: 5 })
            return 1;
        return 0;
    }

    // Negative: a plain `as` whose local is read on BOTH the matched and the
    // fall-through paths is not a pattern binding (the variable would not be
    // definitely assigned), so it must stay a flat `as` + null test.
    public static string AsWithoutPattern(object o)
    {
        var s = o as string;
        if (s != null)
            return s;
        return s ?? "none";
    }

    public static int NormalUsing(string s)
    {
        using var reader = new System.IO.StringReader(s);
        return reader.Read();
    }

    // A `using` over a value-type resource (List<T>.Enumerator is a struct
    // IDisposable). csc emits no null guard — the finally is a bare constrained
    // `e.Dispose();` through the local's address — exercising the value-type slice
    // of the using raise that UsingStatementPass covers beyond the reference-type
    // IDisposable null-guard shape.
    public static int StructUsing(System.Collections.Generic.List<int> items)
    {
        int sum = 0;
        using (var e = items.GetEnumerator())
        {
            while (e.MoveNext())
                sum += e.Current;
        }
        return sum;
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

    public static int EqualityGuardReturn(int value, int whenZero, int otherwise)
    {
        if (value == 0)
            return whenZero;
        return otherwise;
    }

    public static int ObjectReferenceEqualityGuardReturn(object left, object right, int whenSame, int whenDifferent)
    {
        if (left == right)
            return whenSame;
        return whenDifferent;
    }

    public static int StringEqualityGuardReturn(string left, string right, int whenSame, int whenDifferent)
    {
        if (left == right)
            return whenSame;
        return whenDifferent;
    }

    public static int FloatUnorderedGuardReturn(double value, double limit, int whenLessOrEqual, int whenGreaterOrUnordered)
    {
        if (value <= limit)
            return whenLessOrEqual;
        return whenGreaterOrUnordered;
    }

    public static string UnsignedBoundsGuard(int index, int length)
    {
        if ((uint)index >= (uint)length)
            return "out";
        return "in";
    }

    public static (int Sum, int Product) TuplePair(int a, int b) => (a + b, a * b);

    public sealed class InitTarget
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z;
    }

    public static InitTarget MakePoint(int a, int b) => new InitTarget { X = a, Y = b };

    public static InitTarget MakePointWithField(int a, int b) => new InitTarget { X = a, Z = b };

    public static System.Collections.Generic.List<int> MakeList(int a, int b)
        => new System.Collections.Generic.List<int> { a, b, 42 };

    public static InitTarget MakeEmpty() => new InitTarget();

    public static int InitTargetX(InitTarget target) => target.X;

    public static int MakeAndRead(int a) => InitTargetX(new InitTarget { X = a });

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

    public static string NullCoalescingAssignLocal(string? input, string fallback)
    {
        string? value = input;
        value ??= fallback;
        return value;
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

    // A loop deref keeps the pin alive through csc optimization (a single deref
    // is elided): the pinned `int&` local survives and the fixed statement is
    // raised back from the csc pin lowering. Indexing through the fixed pointer
    // (`p[i]`) keeps the body recompile-exact.
    public static unsafe int SumPinnedArray(int[] values)
    {
        int sum = 0;
        fixed (int* p = &values[0])
        {
            for (int i = 0; i < values.Length; i++)
            {
                sum += *p;
            }
        }
        return sum;
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

    // Two pinned locals in one method — the [LibraryImport] custom-marshaller
    // stub shape (one pin per marshalled argument). The compiler nests the pins
    // LIFO (pin a, pin b, ..., unpin b, unpin a). FixedStatementPass must raise
    // them into stacked `fixed` headers over a shared body, folding each pin's
    // derived pointer into the `fixed` variable; a single-pinned-local guard
    // would leave both flat as the unspellable `pinned ref int`. The loop deref
    // keeps both pins alive through csc optimization (a single deref is elided).
    // Deref directly (mirroring SumPinnedArray) rather than index — indexed `p[i]`
    // renders as explicit pointer arithmetic that recompiles with extra conv
    // chains, an unrelated rendering trait that would mask the pin fidelity.
    public static unsafe int SumTwoPinned(int[] a, int[] b)
    {
        int sum = 0;
        fixed (int* pa = &a[0])
        fixed (int* pb = &b[0])
        {
            for (int i = 0; i < a.Length; i++)
                sum += *pa + *pb;
        }
        return sum;
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

    // A constant array initializer in a ReadOnlySpan<T> context: csc lowers
    // `new uint[] { ... }` to a content-addressed <PrivateImplementationDetails>
    // field whose RVA maps the little-endian element bytes, loaded through
    // RuntimeHelpers.CreateSpan<uint>(ldtoken field). RvaSpanPass decodes the
    // blob and raises it back to the array literal, which csc re-lowers to the
    // same field — opcode-exact.
    public static System.ReadOnlySpan<uint> ConstantUIntSpan() => new uint[] { 1, 10, 100, 1000, 10000 };

    static int SumSpan(System.ReadOnlySpan<int> s) => s.Length;

    // A collection expression with NON-constant elements in a ReadOnlySpan<T>
    // context: csc cannot use an RVA blob (the elements are runtime values), so
    // it lowers `[a, b]` to a compiler-synthesized inline-array buffer
    // (`<>y__InlineArray2<int>` / `InlineArray2<int>` on .NET 11+) default-init'd,
    // each slot stored through <PrivateImplementationDetails>.InlineArrayElementRef,
    // and exposed via InlineArrayAsReadOnlySpan. InlineArrayCollectionPass raises
    // it back to `[a, b]`, which csc re-lowers to the same buffer — opcode-exact.
    public static int InlineArraySpan(int a, int b) => SumSpan([a, b]);

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

    // Calls on a constructed generic instance resolve as MemberReferences
    // (TypeSpec parent) that carry no parameter rows, so the out/in keyword must
    // be recovered from the underlying generic MethodDef, not the MemberRef.
    public static int GenericRefKindCallSites(int a)
    {
        var box = new RefKindBox<int>();
        box.TryGet(out int value);
        box.Put(in a);
        return value;
    }

    // A nested type whose leaf name (NestedSample) is shared with an unrelated
    // top-level type below. Its full metadata name is the declaring chain
    // (CfgSampleClass.NestedSample), not the leaf — the IR importer must
    // qualify nested types or this body is unreachable (and collides with the
    // top-level NestedSample on its bare leaf name).
    public sealed class NestedSample
    {
        public static int Triple(int x) => x * 3;
    }
}

// A top-level type sharing the nested type's leaf name, to prove the importer
// keys on the fully-qualified name, not the leaf.
public sealed class NestedSample
{
    public static int Negate(int x) => -x;
}

public sealed class RefKindBox<T>
{
    T _value = default!;

    public bool TryGet(out T value)
    {
        value = _value;
        return true;
    }

    public void Put(in T value) => _value = value;
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
