namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Sample methods with known IL patterns for CFG testing.
/// </summary>
public class CfgSampleClass
{
    internal static bool s_finalized;

    // A C# destructor lowers to a Finalize override whose body is
    // try { s_finalized = true; } finally { base.Finalize(); }. DestructorRecoveryPass
    // strips the scaffold back to the body and marks the function a destructor; the
    // fidelity gate proves recompiling ~CfgSampleClass() restores the original IL.
    ~CfgSampleClass()
    {
        s_finalized = true;
    }

    // A non-public overload declared BEFORE the public one of the same name.
    // With publicOnly resolution, the visibility filter must skip this so the
    // overload index lands on the public overload below — masking the access
    // bits correctly (MethodAttributes.Public is the value 6, not a single bit,
    // so a naive `& Public` test lets internal/protected overloads through).
    internal static int VisibilityOverload() => 1;

    public static int VisibilityOverload(int ignored) => 2;

    public static byte ToByte(int x) => (byte)x;

    // Shift counts whose type is not `int`: C# requires `int`, so the source `(int)`
    // cast is mandatory — but uint->int and enum->int are no-op reinterprets that
    // emit no conv, so the IL shift count carries the original (uint/enum) type. The
    // printer must re-insert `(int)`; `1 << n` over a uint/enum is CS0019.
    public static int ShiftByUInt(uint n) => 1 << (int)n;
    public static int ShiftByEnum(CfgPriority d) => 1 << (int)d;

    // Negative: a plain int count needs no cast — `1 << n` stays bare.
    public static int ShiftByInt(int n) => 1 << n;

    // Event subscription/unsubscription. Subscribing to an event declared on
    // ANOTHER type lowers to a call to its compiler-generated add_/remove_
    // accessor, which is SpecialName and void. Calling such an accessor directly
    // is CS0571; the printer must raise it back to `e += h` / `e -= h`.
    // Static event (null receiver): Console.CancelKeyPress is a static event.
    public static void HookConsoleCancel(System.ConsoleCancelEventHandler h)
        => System.Console.CancelKeyPress += h;

    // Instance event on a passed-in receiver, unsubscribe form.
    public static void UnhookProcessExit(System.AppDomain domain, System.EventHandler h)
        => domain.ProcessExit -= h;

    // A parameter named with a C# keyword (`delegate`): the metadata name is the
    // bare keyword, so every reference must be @-escaped (`@delegate`) or it is
    // CS1001 "Identifier expected". Exercises the LoadArgument render path.
    public static int KeywordParam(int @delegate) => @delegate + 1;

    // A reference inequality against null: csc lowers `o != null` to
    // `ldnull; cgt.un` (an unsigned ordering), so the IR is GreaterThan-unsigned
    // with a null operand. The printer must spell it `o is not null`, not the
    // CS0019 `o > null`.
    public static bool IsNotNullReference(object o) => o != null;
    // A string literal containing the C# line terminators that are not
    // `char.IsControl`: U+2028 LINE SEPARATOR and U+2029 PARAGRAPH SEPARATOR.
    // The printer must escape them (\u2028/\u2029) or the emitted literal splits
    // across source lines (CS1010 "Newline in constant").
    public static string LineSeparatorLiteral() => "a\u2028b\u2029c";

    // A negative integer cast to a contextual-keyword type (`nint`): csc lowers
    // `nint x = -1` to `ldc.i4.m1; conv.i`, which the printer spells `(nint)-1`.
    // C# parses `(nint)-1` as binary subtraction (CS0075) because `nint` is a
    // contextual keyword, not a cast-disambiguating predefined type — so the
    // operand must be parenthesized: `(nint)(-1)`.
    public static nint NegativeNativeInt() => -1;

    public static ulong OrLongIntoULong(ulong mask, long flag) => mask | (ulong)flag;

    // The arithmetic sibling of OrLongIntoULong: `a * (ulong)b` over a ulong and a
    // long. The `(ulong)` is a same-width no-op (no conv in IL), so the importer
    // sees `mul(ulong, long)` — a mixed-sign 64-bit pair with no C# common type
    // (CS0019). add/sub/mul are sign-neutral at the same width, so the printer
    // reinterprets the signed operand as unsigned (`a * (ulong)b`), keeping the
    // same `mul` opcode.
    public static ulong MulLongIntoULong(ulong a, long b) => a * (ulong)b;

    // The 32-bit case: `a * (uint)b` over a uint and an int. The `(uint)` is a
    // same-width no-op, so the importer sees `mul(uint, int)` — which C# widens to
    // a 64-bit `long` mul (a fidelity break) rather than the original 32-bit one.
    // The printer reinterprets the signed operand to unsigned (`a * (uint)b`),
    // keeping the same 32-bit `mul`.
    public static uint MulIntIntoUInt(uint a, int b) => a * (uint)b;

    // Nested mixed-sign arithmetic: `(a * (uint)b) + count`. The inner mul renders
    // unsigned, and EffectiveType must report that so the outer add sees a uint
    // left operand (rather than the ECMA-signed ResultType) and stays unsigned —
    // the rendered-type propagation. The whole expression is one 32-bit `mul`/`add`.
    public static uint NestedMixedSignArithmetic(uint a, int b, uint count) => (a * (uint)b) + count;

    // A `ldind.i4` through a `ref` enum parameter is typed by the opcode width
    // (int), not the pointee enum, so `styles & 16` reads as `int & int` in the
    // IR. The importer must register the ref/pointer pointee's enum shape (and
    // member names) so the constant retypes to the enum — `styles & Gamma`, not
    // the CS0019 `styles & 16`.
    public static CfgStyles RefEnumMask(ref CfgStyles styles) => styles & CfgStyles.Gamma;

    // A constant array with enough elements that csc bulk-initializes it from a
    // <PrivateImplementationDetails> RVA blob via RuntimeHelpers.InitializeArray
    // (rather than element-wise stelem). The printer must fold the blob back into
    // `new int[] { ... }`; left raw the `ldtoken` of the angle-bracketed field name
    // renders as a comment, leaving `InitializeArray(arr, )` — CS1525.
    public static int[] RvaIntArray() => new int[] { 11, 22, 33, 44, 55, 66, 77, 88 };

    // A bitwise OR that mixes a bool flag with an integer, widened to a larger
    // integer return. csc lowers `cond ? 1 : 0` to a branchless `cgt.un` (a bool
    // stack value) and `or`s it with the uint argument. The importer types the
    // `or` by its left operand, so a bool-vs-int OR read as bool — and at the
    // `uint` return the printer wrapped it in the spurious `(...) ? 1 : 0`
    // (CS0029). The bitwise result must take the integer operand's type.
    public static uint BoolBitwiseOrWidened(bool a, uint c) => (a ? 1u : 0u) | c;

    // Adversarial near-miss for the not-null idiom: `x > 0` on a uint also emits
    // `cgt.un` against a zero constant, but the zero is an integer literal, not a
    // reference null. It must stay an unsigned `x > 0` comparison, never `is not null`.
    public static bool UnsignedGreaterThanZero(uint x) => x > 0;

    // A genuine unsigned ordering between two values: `cgt.un` with no null/zero
    // operand at all, so the not-null recovery must leave it as `a > b`.
    public static bool UnsignedGreaterThan(uint a, uint b) => a > b;

    // `a | b` of two bytes is `int` in C# (binary numeric promotion), so the
    // trailing conv.u1 is a real narrowing the language requires. The IR types the
    // `or` as byte (ECMA "wider operand wins"), so IdentityConvertPass must not drop
    // the conv as an identity — `return a | b;` would be CS0266.
    public static byte OrTwoBytes(byte a, byte b) => (byte)(a | b);

    public static int LengthOf(string s) => s.Length;

    // The canonical identity convert: `ldlen` yields a native int the importer
    // models as int-typed ArrayLength, and the trailing conv.i4 is int -> int.
    // IdentityConvertPass must still drop it — `a.Length`, no `(int)` cast.
    public static int ArrayLen(int[] a) => a.Length;

    public static char LastChar(string s) => s[^1];

    // Negative fixture: hand-written string indexing re-loads the receiver
    // directly, unlike the compiler's `^1` lowering which spills the receiver.
    public static char LastCharHandWritten(string s) => s[s.Length - 1];

    public static int Twice(int x) { var t = x + x; return t; }

    // Branch over a `ref bool` deref: csc emits `ldarg; ldind.u1; brtrue`, so the
    // condition's IR ResultType is `byte`, but it renders as the C# bool place.
    // The branch must spell `!flag`, not the CS0019 `flag == 0`.
    public static int RefBoolGuard(ref bool flag)
    {
        if (!flag)
        {
            return 1;
        }

        return 2;
    }

    // The `ref bool` deref compared to a constant, materialized as a value:
    // `ldarg; ldind.u1; ldc.i4.0; ceq`. The comparison's IR ResultType is `byte`,
    // so `flag == 0` is CS0019 unless the load recovers its bool pointee type — then
    // the constant retypes to `false` and folds to `!flag`.
    public static bool RefBoolIsClear(ref bool flag) => !flag;

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

    // A non-capturing lambda: the body reads only its own parameter, so the
    // compiler emits it as a delegate over a static method on the singleton
    // <>c closure holder, cached in a <>9__ field. LambdaCachePass strips the
    // lazy cache and LambdaRaisingPass recovers `x => x + 1`.
    public static System.Func<int, int> NonCapturingLambda() => x => x + 1;

    // Expression-tree lambdas do not emit a generated closure method to import;
    // ExpressionLambdaRewriter lowers the syntax directly to factory calls.
    public static System.Linq.Expressions.Expression<System.Func<int, int>> SimpleExpressionTreeLambda()
        => x => x + 1;

    // Near-miss for expression-tree recovery: factory calls can be source-authored
    // directly, with the same public Expression APIs that the compiler uses.
    public static System.Linq.Expressions.Expression<System.Func<int, int>> ManualSimpleExpressionTreeFactory()
    {
        System.Linq.Expressions.ParameterExpression x;
        return System.Linq.Expressions.Expression.Lambda<System.Func<int, int>>(
            System.Linq.Expressions.Expression.Add(
                x = System.Linq.Expressions.Expression.Parameter(typeof(int), "x"),
                System.Linq.Expressions.Expression.Constant(1)),
            x);
    }

    // Capturing: `n` is hoisted into a <>c__DisplayClass, so the delegate targets
    // an instance method on that class. LambdaRaisingPass substitutes the body's
    // `this.n` read with the captured value and recovers `x => x + n`.
    public static System.Func<int, int> CapturingLambda(int n) => x => x + n;

    // Two captured parameters, both substituted into the recovered body.
    public static System.Func<int, int> TwoCaptureLambda(int a, int b) => x => x + a - b;

    // Multi-statement display-class environment: the closure is a local that is
    // allocated, field-set, and bound to a local across separate statements (not
    // the folded `new DC { ... }`). LambdaRaisingPass elides the allocation and
    // capture store and recovers `f = x => x + n`.
    public static int InvokeLocalCapture(int n)
    {
        System.Func<int, int> f = x => x + n;
        return f(5) + f(6);
    }

    // Two lambdas share one display-class environment (`n` captured once); both
    // are raised and the shared allocation is elided.
    public static (System.Func<int, int>, System.Func<int, int>) SharedCaptureLambdas(int n)
        => (x => x + n, y => y - n);

    // Boundary fixtures for the lambda surface: each exercises a distinct
    // LambdaRaisingPass guard. When a later slice lifts a guard, flip its
    // scorecard entry to recovered in that PR.

    // Statement body: no captures or locals, so LambdaRaisingPass can render the
    // recovered block-bodied lambda in the outer scope.
    public static System.Func<int, int> StatementBodyLambda()
        => x => { System.Console.WriteLine(x); return x + 1; };

    // Local-bearing body: `y` is read twice, so inlining cannot fold it away and
    // the recovered lambda needs a nested local scope of its own.
    public static System.Func<int, int> LocalBodyLambda()
        => x => { int y = x + 1; return y * y; };

    // Capturing local-bearing body whose capture is an outer parameter. The
    // parameter name is stable in the nested lambda print scope, so this is now
    // recoverable.
    public static System.Func<int, int> CapturingLocalBodyLambda(int n)
        => x => { int y = x + n; return y * y; };

    // Negative: the capture is an outer local, whose slot would be ambiguous when
    // printing the lambda's own local scope.
    public static System.Func<int, int> CapturingOuterLocalBodyLambda(int n)
    {
        int z = n + 1;
        return x => { int y = x + z; return y * y; };
    }

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

    public static int PartitionStyleNestedSelfLoops(int[] values, int pivot, int lo, int hi)
    {
        int i = lo;
        int j = hi - 1;
        do
        {
            do
            {
                i++;
            }
            while (values[i] < pivot);

            do
            {
                j--;
            }
            while (pivot < values[j]);

            if (i >= j)
                break;

            int temp = values[i];
            values[i] = values[j];
            values[j] = temp;
        }
        while (i < j);

        return i;
    }

    // An infinite (while (true)) loop exited only by early returns: csc lowers
    // the back edge to an unconditional goto to the loop head. StructuringPass
    // raises the head-latch pair into while (true) with the returns as guards.
    public static int WhileTrueWithReturns(int seed)
    {
        int x = seed;
        while (true)
        {
            x = x * 3 + 1;
            if (x > 1000)
                return x;
            if (x < 0)
                return -1;
            x -= 2;
        }
    }

    // A while (true) whose body breaks to the block after the latch: the forward
    // exit becomes break, the unconditional back edge the loop edge.
    public static int WhileTrueWithBreak(int n)
    {
        int i = 0;
        int total = 0;
        while (true)
        {
            if (i >= n)
                break;
            total += i;
            i++;
        }
        return total;
    }

    // `for (;;)` is the same unconditional-back-branch lowering as `while (true)`
    // (no IL anchor distinguishes them), so it recovers as `while (true)` too.
    public static int ForEverLoopWithReturn(int seed)
    {
        int x = seed;
        for (;;)
        {
            x = x * 3 + 1;
            if (x > 1000)
                return x;
            x -= 2;
        }
    }

    // Adversarial: a mid-body `continue` adds a second back-edge to the loop head,
    // so the single-latch infinite-loop discriminator declines. It must still be a
    // real structured loop with no stray unstructured goto, not a partial raise.
    public static int InfiniteLoopWithContinue(int n)
    {
        int i = 0;
        int total = 0;
        while (true)
        {
            i++;
            if (i % 2 == 0)
                continue;
            if (i > n)
                return total;
            total += i;
        }
    }

    public static void Noop() { }

    public static int ParseOrZero(string s) => int.TryParse(s, out var v) ? v : 0;

    public static int FirstElement(int[] a) => a[0];

    public static int LastElement(int[] a) => a[^1];

    // Variable / computed from-end index: `a[^n]` lowers to `a[a.Length - n]`
    // with the same receiver spill as the constant `^1` form, so IndexFromEndPass
    // raises it back to `^n` for any integer offset expression.
    public static int NthFromEnd(int[] a, int n) => a[^n];

    public static int NthFromEndComputed(int[] a, int n) => a[^(n + 1)];

    public static char NthCharFromEnd(string s, int n) => s[^n];

    public static int ReadOnlySpanLast(System.ReadOnlySpan<int> span) => span[^1];

    public static int SpanLast(System.Span<int> span) => span[^1];

    public static int ReadOnlySpanNthFromEnd(System.ReadOnlySpan<int> span, int n) => span[^n];

    // Negative fixture: hand-written `a[a.Length - n]` re-loads the array
    // directly (no receiver spill), so it must NOT be raised even though the
    // offset is a variable.
    public static int NthFromEndHandWritten(int[] a, int n) => a[a.Length - n];

    // Negative fixture: hand-written `a[a.Length - 1]` re-loads the array
    // directly (two ldarg, no receiver spill), unlike the `^1` lowering which
    // spills into one stack slot. IndexFromEndPass must NOT raise this — doing
    // so would recompile to a different opcode stream (ldarg dup … vs ldarg
    // ldarg …). Kept opcode-exact by the fidelity gate.
    public static int LastElementHandWritten(int[] a) => a[a.Length - 1];

    public static int ReadOnlySpanLastHandWritten(System.ReadOnlySpan<int> span) => span[span.Length - 1];

    public static int ReadOnlySpanEnumFirst(System.ReadOnlySpan<CfgPriority> span) => (int)span[0];

    public static int SpanLastHandWritten(System.Span<int> span) => span[span.Length - 1];

    public static void SetFirstElement(int[] a, int v) => a[0] = v;

    // stelem.i1 stores into byte[], sbyte[], and bool[] alike, and stelem.i2 into
    // short[], ushort[], and char[]. The store must be typed from the array's
    // element (byte/char), not the opcode's signed default (sbyte/short): typing
    // it from the opcode makes the element store render a `(sbyte)`/`(short)`
    // cast, which is CS0266 against a byte[]/char[] element.
    public static void SetByteElement(byte[] a, int v) => a[0] = (byte)v;

    public static void SetCharElement(char[] a, int v) => a[0] = (char)v;

    public static int BoolArrayVisited(int index)
    {
        bool[] visited = new bool[index + 1];
        visited[index] = true;
        return visited[index] ? 1 : 0;
    }

    // A static abstract interface member invoked through a type parameter:
    // `constrained. T; call INumberBase<T>::IsNegative`. C#'s spelling is the type
    // parameter itself — `T.IsNegative(value)` — not the declaring interface
    // (`INumberBase<T>.IsNegative(value)` cannot invoke a static abstract member:
    // CS0119/CS8926). The constrained type is the receiver.
    public static bool IsNegativeNumber<T>(T value) where T : System.Numerics.INumberBase<T>
        => T.IsNegative(value);

    // A comparison result returned from an int-returning method (`cgt.un; ret`,
    // as in byte.Sign / Convert.ToInt32(bool)): the bool flows into an integer
    // target. C# has no implicit bool→int, so it must spell `value > 0 ? 1 : 0`,
    // which Roslyn folds back to the bare `cgt.un` — opcode-exact.
    public static int IsPositiveAsInt(uint value) => value > 0 ? 1 : 0;

    // Array range slices: the compiler lowers these to
    // RuntimeHelpers.GetSubArray(a, <Range>); the decompiler raises them back to
    // the a[i..j] indexer (RangeFromGetSubArrayPass). Both from-start endpoints
    // (rendered bare) and from-end endpoints (^n) are recovered, across the open
    // and closed range forms, for round-trip coverage.
    public static int[] ArrayRangeBoth(int[] a, int i, int j) => a[i..j];

    // Negative fixture: spelling the endpoint as `new Index(i, false)` is
    // source-equivalent to `i`, but it is not the compiler's ordinary
    // `a[i..j]` lowering (which calls Index.op_Implicit). Raising it would lose
    // the explicit constructor call and break opcode-exact round-trip.
    public static int[] ArrayRangeExplicitFromStartIndex(int[] a, int i, int j)
        => a[new System.Index(i, fromEnd: false)..j];

    public static int[] ArrayRangeFrom(int[] a, int i) => a[i..];

    public static int[] ArrayRangeFromEnd(int[] a, int i) => a[^i..];

    public static int[] ArrayRangeTo(int[] a, int j) => a[..j];

    public static int[] ArrayRangeAll(int[] a) => a[..];

    public static int[] ArrayRangeFromEndHi(int[] a, int i) => a[i..^1];

    public static int[] ArrayRangeFromEndBoth(int[] a) => a[^3..^1];

    public static int[] ArrayRangeToFromEnd(int[] a) => a[..^1];

    public static string StringRangeBoth(string s, int i, int j) => s[i..j];

    public static System.ReadOnlySpan<int> SpanRangeBoth(System.ReadOnlySpan<int> s, int i, int j) => s[i..j];

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

    // Byref provenance through a dup'd call-produced ref: `s[i] += v` calls
    // Span<int>.get_Item once (returning `ref int`), dups that managed pointer,
    // reads through it, adds, and stores back through the SAME pointer. The
    // compound fold must prove the store and load addresses are one ref
    // evaluation before collapsing to `s[i] += v`; a fold that re-derived the
    // ref would evaluate get_Item twice.
    public static void SpanElementCompoundAdd(System.Span<int> s, int i, int v) => s[i] += v;

    // Checked-context compound assignment: `checked(x += v)` lowers to
    // `x = checked(x + v)` (add.ovf). The compound sugar can only be recovered
    // inside a `checked { ... }` block, since `checked(x += v);` is not a valid
    // statement expression.
    public static int CheckedCompoundAdd(int x, int v)
    {
        checked { x += v; }
        return x;
    }

    public static int CheckedCompoundMul(int x, int v)
    {
        checked { x *= v; }
        return x;
    }

    public static int CheckedCompoundInc(int x)
    {
        checked { x++; }
        return x;
    }

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

    public static int TryFinallyRetryLoop(int x)
    {
        while (true)
        {
            try
            {
                if (x >= 3)
                    return x;
                x++;
                continue;
            }
            finally
            {
                LastValue = x;
            }
        }
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

    // A wider sparse switch — enough scattered cases that csc builds a multi-level
    // binary-search tree (pivots branching to other pivots), exercising the
    // sparse-int raise on a deeper dispatch than ClassifyMode's single pivot.
    public static string ClassifyWide(int code) => code switch
    {
        3 => "three",
        17 => "seventeen",
        42 => "forty-two",
        99 => "ninety-nine",
        128 => "one-twenty-eight",
        256 => "two-fifty-six",
        500 => "five-hundred",
        1000 => "thousand",
        _ => "other",
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

    // --- Cross-assembly enum vs integer (CS0019) ---
    // System.DayOfWeek / System.AttributeTargets live in CoreLib, not this test
    // assembly, so on decompile ResolveShape returns Unknown — the enum loses its
    // shape and a sibling int constant or value never retypes. The printer must
    // recognize the enum structurally (a named definition with no stack family,
    // opposite an integer) and cast the integer operand to the enum type, or the
    // comparison/bitwise op is CS0019 (`enum == int` / `enum & int`).

    // `(int)day` is a no-op on the stack (an enum is already its underlying int),
    // so the IL is a bare `ceq` of the enum arg against the int arg. Decompiled
    // cross-assembly that is `day == code` (CS0019); the fix casts: `day == (DayOfWeek)code`.
    public static bool CrossAssemblyEnumEqualsInt(System.DayOfWeek day, int code) => (int)day == code;

    // `t & AttributeTargets.Class` compiles to `ldarg; ldc.i4.4; and`; the 4 stays
    // a bare int once the enum shape is unknown, so the mask is `t & 4` (CS0019).
    // The fix casts the int operand to the enum: `t & (AttributeTargets)4`.
    public static System.AttributeTargets CrossAssemblyEnumBitwise(System.AttributeTargets t) => t & System.AttributeTargets.Class;

    // A cross-assembly enum (StringComparison, CoreLib) passed as a CALL ARGUMENT
    // lowers to `ldc.i4.5` for OrdinalIgnoreCase. With the enum shape unknown, the
    // bare `5` makes overload resolution miss the instance string.Equals(string,
    // StringComparison) and fall back to the static object.Equals(object, object),
    // which then can't be called on an instance (CS0176). The fix casts the
    // argument to the enum: `s.Equals("x", (StringComparison)5)`.
    public static bool CrossAssemblyEnumCallArgument(string s) => s.Equals("x", System.StringComparison.OrdinalIgnoreCase);

    // A `switch` over a cross-assembly enum (DayOfWeek, CoreLib): the IL jump
    // table switches on the enum's underlying int, so the raised case labels are
    // bare integers. With the enum shape unknown, `case 1:` is CS0266 — C#
    // converts int->enum implicitly only for the literal 0 — so the printer must
    // spell each label as the enum: `case (DayOfWeek)1:`.
    public static int CrossAssemblyEnumSwitch(System.DayOfWeek day)
    {
        switch (day)
        {
            case System.DayOfWeek.Sunday: return 10;
            case System.DayOfWeek.Monday: return 11;
            case System.DayOfWeek.Tuesday: return 12;
            default: return -1;
        }
    }

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

    // An OR-chain DIAMOND: two short-circuit conditions select between a true and
    // a non-empty false arm. csc lowers this to two conditionals both branching to
    // the shared true arm, with the false arm jumping past it to the merge — a
    // forward branch the structuring pass's one-conditional-per-diamond model
    // cannot name, so the container stays flat (issue #911). OrChainDiamondPass
    // folds the guards into one `||` conditional, leaving the single-conditional
    // diamond the structuring pass raises into if/else; recompiling the `a < 0 ||
    // b < 0` guard restores the same two-branch IL. The else arm is what
    // distinguishes this from the guard-only OrChainGuardPass shape (IfOr above).
    public static int OrChainDiamond(int a, int b, int x)
    {
        int r;
        if (a < 0 || b < 0)
            r = x * 2;
        else
            r = x + 7;
        return r - a;
    }

    // A clustered-case switch whose arms compute a bool: csc lowers the dispatch
    // to a binary-search/equality tree and each bool arm to a slot diamond that
    // stores the result and falls into a shared `return S`. The diamond arm is not
    // a straight-line terminator, so the structurer cannot inline it as a switch
    // leaf and the whole method stays flat (issue #912). SlotDiamondPass folds each
    // returned diamond to `return c ? a : b`, making the arm a terminator so the
    // dispatch raises into nested if/else.
    public static bool SlotDiamondDispatch(int x, int y) => x switch
    {
        0 => true,
        10 => true,
        100 => y is >= 64 and <= 127,
        127 => true,
        169 => y != 254,
        172 => y is >= 16 and <= 31,
        _ => false,
    };

    // The HttpClientFactory::IsNonPublic comparison-tree shape: clustered
    // sparse-switch dispatch over a byte, with guarded range arms that all branch
    // to one shared false return tail. ComparisonTreeBoolArmPass folds those range
    // arms to straight-line returns before structuring, so the tree can nest.
    public static bool ByteRangeSearchTree(byte[] b)
    {
        byte first = b[0];
        return first switch
        {
            0 => true,
            10 => true,
            127 => true,
            169 when b[1] == 254 => true,
            172 when b[1] >= 16 && b[1] <= 31 => true,
            192 when b[1] == 168 => true,
            100 when b[1] >= 64 && b[1] <= 127 => true,
            >= 224 => true,
            _ => false,
        };
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

    public static int StringSwitchWithJoin(string s)
    {
        int n;
        switch (s)
        {
            case "one": n = 1; break;
            case "two": n = 2; break;
            case "three": n = 3; break;
            default: n = -1; break;
        }
        return n * 10;
    }

    public static int SingleStringEquality(string s)
    {
        if (s == "x")
            return 1;
        return 0;
    }

    public static string StringSwitchNoDefault(string s)
    {
        string r = "none";
        switch (s)
        {
            case "a": r = "first"; break;
            case "b": r = "second"; break;
        }
        return r;
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

    // A null-conditional invocation over an unconstrained generic receiver:
    // `value?.ToString() ?? "none"`. csc cannot know whether T is a reference or
    // value type, so it emits a default(T)-box two-stage null test with a reload
    // between, both arms sharing the one ToString() call — a diamond the
    // structuring pass cannot nest (issue #911). NullConditionalCoalescePass folds
    // it back to the source idiom, which recompiles to the same two-stage test.
    public static string GenericNullConditionalToString<T>(T value)
        => value?.ToString() ?? "none";

    public static bool IsValueTypeOf<T>() => typeof(T).IsValueType;

    // The generic-math `(U)(object)x` idiom: a type parameter cast to a concrete
    // type through object lowers to `box T; unbox.any byte`. The box is an explicit
    // (object) cast the printer must keep — `(byte)left` over a generic T is CS0030.
    public static bool GreaterAsByte<T>(T left, T right) where T : struct
        => (byte)(object)left > (byte)(object)right;

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

    // `is T t` binding read by a single relational comparison in the right
    // conjunct; the printer folds it to a relational property pattern
    // `o is string { Length: > 0 }`.
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

    // Negative: the pattern local is read in the body, so folding the condition
    // to a non-binding property pattern would make the local unavailable.
    public static int IsPatternPropertyWithBindingUse(object o)
    {
        if (o is string s && s.Length == 5)
            return s.Length;
        return 0;
    }

    // Relational property sub-pattern: `t.Length > 5` folds to `{ Length: > 5 }`.
    public static int IsPatternPropertyGreater(object o)
    {
        if (o is string { Length: > 5 })
            return 1;
        return 0;
    }

    // Relational property sub-pattern with the `<=` operator.
    public static int IsPatternPropertyAtMost(object o)
    {
        if (o is string { Length: <= 3 })
            return 1;
        return 0;
    }

    public sealed class PatternPoint
    {
        public int X { get; init; }
        public int Y { get; init; }
    }

    public sealed class RecursivePatternSource
    {
        public object? PublicProperty { get; init; }
    }

    public sealed class FloatHolder
    {
        public double Magnitude { get; init; }
    }

    public sealed class PositionalPatternNode
    {
        public PositionalPatternNode(string? name, int count)
        {
            Name = name;
            Count = count;
        }

        public string? Name { get; }
        public int Count { get; }

        public void Deconstruct(out string? name, out int count)
        {
            name = Name;
            count = Count;
        }
    }

    public sealed class FloatPositionalPatternNode
    {
        public FloatPositionalPatternNode(string? name, double magnitude)
        {
            Name = name;
            Magnitude = magnitude;
        }

        public string? Name { get; }
        public double Magnitude { get; }

        public void Deconstruct(out string? name, out double magnitude)
        {
            name = Name;
            magnitude = Magnitude;
        }
    }

    // Negative: a relational sub-pattern on a floating-point property must NOT
    // fold to `{ Magnitude: > 1.5 }`. A float `>` lowers to an ordered compare
    // (`cgt`), but the same source relation can also surface as the unordered
    // (`cgt.un`) form, and the two disagree on NaN; folding either into a
    // relational pattern would silently fix one NaN answer. The type pattern is
    // still raised, but the comparison stays an explicit `&&`.
    public static bool IsPatternFloatPropertyRelational(object o) => o is FloatHolder { Magnitude: > 1.5 };

    public static bool IsPatternMultiProperty(object o) => o is PatternPoint { X: 1, Y: 2 };

    public static bool IsPatternMultiPropertyMixed(object o) => o is PatternPoint { X: > 0, Y: 2 };

    public static bool PositionalPattern(PositionalPatternNode? node) => node is ("ok", > 0);

    public static bool ManualPositionalPatternLookalike(PositionalPatternNode? node)
    {
        if (node is not null)
        {
            node.Deconstruct(out string? name, out int count);
            if (name == "ok")
                return count > 0;
        }
        return false;
    }

    public static bool FloatPositionalPattern(FloatPositionalPatternNode? node) => node is ("ok", > 1.5);

    // Runtime-mined frontier: a single-element string-array list pattern lowers
    // to a null/length guard, an element temp, an OR-chain of string equality
    // tests, then a bool result slot.
    public static int SingleElementStringArrayListPattern(string[] args)
    {
        if (args is ["--help" or "-h" or "/?" or "-?"])
            return 1;
        return 0;
    }

    // Near-miss: source manual guard with a named element temp. It is semantically
    // equivalent, but not a compiler list-pattern lowering because the temp is
    // source-authored and must remain available to source-level debugging/name
    // recovery.
    public static int ManualSingleElementStringArrayGuard(string[] args)
    {
        if (args is not null && args.Length == 1)
        {
            var arg = args[0];
            if (arg == "--help" || arg == "-h" || arg == "/?" || arg == "-?")
                return 1;
        }
        return 0;
    }

    // Frontier row #1142: a general list pattern with a trailing discard slice.
    // The source-like target is `values is [1, 2, ..]`.
    public static bool GeneralListPattern(int[] values) => values is [1, 2, ..];

    // Near-miss for #1142: the hand-written guard spells the current lowered
    // decompiler output. It must stay lowered unless a discriminator proves the
    // list-pattern source form.
    public static bool ManualGeneralListPatternGuard(int[] values)
        => values is not null && values.Length >= 2 && values[0] == 1 && values[1] == 2;

    // Runtime-mined frontier shape: a recursive property pattern with a
    // declaration subpattern whose variable is used in the body.
    public static int RecursivePropertyPatternBinding(RecursivePatternSource value)
    {
        if (value is { PublicProperty: string str })
            return str.Length;
        return 0;
    }

    public static int IsPatternManualAsAndPropertiesWithUse(object o)
    {
        var point = o as PatternPoint;
        if (point is not null && point.X == 1 && point.Y == 2)
            return point.X + point.Y;
        return 0;
    }

    public static bool IsPatternDuplicateProperty(object o) => o is PatternPoint { X: > 0, X: < 10 };

    // Genuine conjunction: the relational comparison is against a parameter, not
    // a constant, so it has no property sub-pattern form and stays a flat `&&`
    // with the pattern binding visible.
    public static bool IsPatternConjunctionVariableBound(object o, int n) => o is string s && s.Length > n;

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

    // Compiler foreach over List<T> — a disposable struct pattern enumerator
    // (List<T>.Enumerator, no IEnumerable interface), with a hidden enumerator
    // local. The PDB-hidden enumerator is the discriminator vs. the hand-written
    // StructUsing above (whose `e` carries a source name and stays using/while).
    public static int ForeachGenericList(System.Collections.Generic.List<int> items)
    {
        int sum = 0;
        foreach (int value in items)
            sum += value;
        return sum;
    }

    public ref struct RefStructResource
    {
        public RefStructResource(int value) => Value = value;

        public int Value { get; }

        public void Dispose() { }
    }

    public static int RefStructPatternUsing(int value)
    {
        using var resource = new RefStructResource(value);
        return resource.Value;
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

    // Adversarial breadth: a static local function called more than once. The call
    // sites must collapse to a single recovered declaration, not one per call.
    public static int StaticLocalFunctionCalledTwice(int x)
    {
        return Twice(x) + Twice(x + 1);

        static int Twice(int v) => v * 2;
    }

    // Local-bodied static local function: Release uses a stack slot for `y`, so
    // recovery needs a nested local-function scope, not the host method scope.
    public static int StaticLocalFunctionWithLocal(int x)
    {
        return SquarePlusOne(x);

        static int SquarePlusOne(int v)
        {
            int y = v + 1;
            return y * y;
        }
    }

    // Capturing local function: `n` is hoisted into a struct <>c__DisplayClass
    // passed to the synthesized method by ref. LocalFunctionRaisingPass substitutes
    // the captured field back and recovers the non-static `int Add(int v) => v + n;`.
    public static int CapturingLocalFunction(int n)
    {
        return Add(5);

        int Add(int v) => v + n;
    }

    // Adversarial breadth: one capturing local function called twice. Both calls
    // pass `ref env` for the same environment local; the pass must drop the ref-env
    // argument from each and recover a single declaration.
    public static int CapturingCalledTwice(int n)
    {
        return Add(5) + Add(7);

        int Add(int v) => v + n;
    }

    // Capturing local-bodied local functions still stay lowered: capture
    // substitution prints in the host scope, so local-bodied capture recovery
    // needs an additional nested-scope representation.
    public static int CapturingLocalFunctionWithLocal(int n)
    {
        return AddSquare(5);

        int AddSquare(int v)
        {
            int y = v + n;
            return y * y;
        }
    }

    // Adversarial soundness probe: the captured parameter is mutated before the
    // call, so the environment field is filled with the post-mutation value. The
    // recovered `v + n` reads the (reassigned) parameter — the fidelity gate proves
    // whether the substitution stays opcode-exact.
    public static int CapturingAfterMutation(int n)
    {
        n += 1;
        return Add(5);

        int Add(int v) => v + n;
    }

    // Capturing the host's SECOND parameter (index 1). The substituted capture
    // value is host arg 1, which shares the numeric index of the synthesized env
    // parameter (also the last/only ref parameter) — a raise that detects leftover
    // environment reads by raw argument index would mistake the substituted value
    // for an unresolved environment read and decline.
    public static int CaptureSecondParam(int x, int n)
    {
        return Add(5);

        int Add(int v) => v + n;
    }

    // Two distinct captured variables read by the local function — exercises
    // multi-field environment substitution; `b` again lands on the env parameter
    // index.
    public static int CaptureTwoVariables(int a, int b)
    {
        return Add(5);

        int Add(int v) => v + a - b;
    }

    // Adversarial negative: two local functions sharing one environment (both
    // capture `n`). The shared display-class local is referenced by both call sites,
    // so neither resolves to a clean single-use environment — must stay lowered.
    public static int SharedEnvironmentLocalFunctions(int n)
    {
        return Add(5) + Mul(3);

        int Add(int v) => v + n;
        int Mul(int v) => v * n;
    }

    // Adversarial negative: a recursive local function (its body calls itself), which
    // keeps the import non-recursive — must stay lowered.
    public static int RecursiveLocalFunction(int n)
    {
        return Fact(n);

        int Fact(int v) => v <= 1 ? 1 : v * Fact(v - 1);
    }

    // Adversarial negative: the captured variable `n` is reassigned AFTER the only
    // call. The compiler hoists `n` into the display class, so env.n gets an
    // initial-capture store and a post-call store. Substituting the post-call value
    // at the body read would change the program (at the call site `n` was still its
    // initial value), so the raise must decline.
    public static int CaptureReassignedAfterCall(int n, int m)
    {
        int r = Add(5);
        n = m;
        return r;

        int Add(int v) => v + n;
    }

    // Adversarial negative: the captured variable is reassigned before the call.
    // Two stores to env.n mean a single substituted value is not obviously the live
    // one; the raise stays conservative and declines.
    public static int CaptureReassignedBeforeCall(int n, int m)
    {
        n = m;
        return Add(5);

        int Add(int v) => v + n;
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

    public static int CompoundLatchLoop(int count)
    {
        int i = 0;
        int result = 0;
        while (i < count && result == 0)
        {
            result = i - 3;
            i++;
        }
        return result;
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

    public static int GuardReturnAfterLocalPrelude(int value, int whenPositive, int otherwise)
    {
        int positive = whenPositive;
        int negative = otherwise;
        LastValue = positive + negative;
        if (value > 0)
            return positive;
        return negative;
    }

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

    // An eight-element tuple literal: csc builds it as the nested-TRest form
    // `new ValueTuple<…T7, ValueTuple<int>>(…, new ValueTuple<int>(h))`, which
    // TupleCreationPass flattens back to one `(…, h)` literal. Recompiling the
    // literal rebuilds the same nested construction opcode-exact.
    public static (int, int, int, int, int, int, int, int) TupleRest(int a)
        => (a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6, a + 7);

    public static bool TupleValueEquals((int Sum, int Product) left, (int Sum, int Product) right) => left == right;

    public static bool TupleValueNotEquals((int Sum, int Product) left, (int Sum, int Product) right) => left != right;

    public static bool TupleValueEquals3((int A, int B, int C) left, (int A, int B, int C) right) => left == right;

    // Element-literal tuple equality: `(a, b) == (c, d)`. csc eagerly spills the
    // operands (unnamed temps) then compares element-wise — TupleLiteralBinaryPass
    // raises that back to the tuple operator.
    public static bool TupleLiteralEquals(int a, int b, int c, int d) => (a, b) == (c, d);

    public static bool TupleLiteralNotEquals(int a, int b, int c, int d) => (a, b) != (c, d);

    // Arity-3 element-literal tuple equality, to exercise the general N-ary chain.
    public static bool TupleLiteralEquals3(int a, int b, int c, int d, int e, int f) => (a, b, c) == (d, e, f);

    // Mixed: one literal operand, one tuple-variable operand. csc spills the
    // literal's elements AND the tuple variable, comparing the literal elements
    // against the variable's Item1/Item2 loads.
    public static bool TupleMixedLiteralLeft(int a, int b, (int, int) pair) => (a, b) == pair;

    public static bool TupleMixedLiteralRight((int, int) pair, int a, int b) => pair == (a, b);

    public static bool TupleMixedNotEquals(int a, int b, (int, int) pair) => (a, b) != pair;

    static int s_seq;
    static int Tick(int v) { s_seq++; return v; }

    // Side-effecting elements in a literal tuple comparison. Every element is a call
    // with an observable order, so csc's eager spill prologue holds the calls. The
    // raise inlines those spills back into the tuple literals; this is the fidelity
    // probe — recompiling `(Tick(a), Tick(b)) == (Tick(c), Tick(d))` must reproduce
    // the original spill order, or the side effects reorder.
    public static bool TupleLiteralSideEffectOrder(int a, int b, int c, int d)
        => (Tick(a), Tick(b)) == (Tick(c), Tick(d));

    // A constant element in an otherwise-variable literal tuple comparison. Both
    // operands are still tuple literals (`(a, 5)` and `(c, d)`), so the eager-spill
    // form applies and the raise to `(a, 5) == (c, d)` is faithful.
    public static bool TupleLiteralConstElement(int a, int c, int d) => (a, 5) == (c, d);

    // Adversarial near-miss: a non-short-circuit bitwise `&` over side-effecting
    // comparisons. It evaluates a, c, b, d in that order, whereas `(…) == (…)`
    // evaluates a, b, c, d — so raising it would reorder the side effects. The pass
    // must leave it as the bitwise `&`; the fidelity gate also pins that it stays
    // opcode-exact.
    public static bool BitwiseAndSideEffectComparison(int a, int b, int c, int d)
        => (Tick(a) == Tick(c)) & (Tick(b) == Tick(d));


    public static int DeconstructTuplePair((int Sum, int Product) pair)
    {
        (int sum, int product) = pair;
        return sum + product;
    }

    public static int DeconstructIntoExistingLocals((int Sum, int Product) pair, bool flag)
    {
        int sum = 10;
        int product = 20;
        if (flag)
        {
            (sum, product) = pair;
        }
        return sum + product;
    }

    public readonly struct Pairing
    {
        public Pairing(int first, int second)
        {
            First = first;
            Second = second;
        }

        public int First { get; }
        public int Second { get; }

        public void Deconstruct(out int first, out int second)
        {
            first = First;
            second = Second;
        }
    }

    public static int DeconstructViaMethod(Pairing pairing)
    {
        var (first, second) = pairing;
        return first + second;
    }

    // Mixed deconstruction over locals: `sum` is declared by the deconstruction,
    // `product` is a pre-existing local assigned into. csc stores Item1 to the fresh
    // slot and Item2 to the existing slot, both StoreLocal — recovered as the mixed
    // `(int sum, product) = pair`.
    public static int DeconstructMixedLocal((int Sum, int Product) pair, bool flag)
    {
        int product = flag ? 10 : 20;
        (int sum, product) = pair;
        return sum + product;
    }

    // Deconstruction into non-local targets — the issue #1142 "field"/"argument"
    // frontier. csc spills the tuple to a temp, then stores each element into the
    // target place: a static field, a `this`-instance field, or a by-value
    // parameter. Recovered as `(place0, place1) = pair`. The field cases live on
    // the top-level FieldDeconstructionTargets class (nested-type import is not
    // wired in the test importer helper).

    // Deconstruction into two by-value parameters.
    public static int DeconstructIntoParameters((int, int) pair, int a, int b)
    {
        (a, b) = pair;
        return a + b;
    }

    public static object AnonShorthand(int a, string b) => new { a, b };

    public static object AnonNamed(int x, string y) => new { Id = x, Name = y };

    public static object AnonSingle(int a) => new { a };
    public static object AnonMemberShorthand(InitTarget t) => new { t.X, t.Y };
    public static object AnonNested(int a, int b) => new { a, Inner = new { b } };
    public static object AnonDeepNested(int a, int b, int c) => new { Outer = new { a, Mid = new { b, c } } };

    public sealed class InitTarget
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z;
    }

    public sealed class InitContainer
    {
        public int Tag { get; set; }
        public InitTarget Inner { get; set; } = new();
        public System.Collections.Generic.List<int> Items { get; } = new();
    }

    public sealed class NonEnumerableAddTarget
    {
        public int Total { get; private set; }

        public void Add(int value) => Total += value;
    }

    public static InitContainer MakeNestedObject(int a, int b)
        => new InitContainer { Inner = { X = a, Y = b } };

    public static InitContainer MakeNestedCollection(int a, int b)
        => new InitContainer { Items = { a, b } };

    // Adversarial: `Inner = new InitTarget { ... }` REASSIGNS Inner (a flat member
    // store whose value is its own sub-initializer), so it must keep the `new` and
    // never collapse to the nested-mutation form `Inner = { ... }`.
    public static InitContainer MakeNestedReassign(int a, int b)
        => new InitContainer { Inner = new InitTarget { X = a, Y = b } };

    // Adversarial breadth: a flat scalar member interleaved with a nested block.
    public static InitContainer MakeFlatAndNested(int a, int b, int c)
        => new InitContainer { Tag = c, Inner = { X = a, Y = b } };

    // Adversarial breadth: two distinct nested members (object + collection) — the
    // pass must flush one block and open another on the member change.
    public static InitContainer MakeTwoNestedMembers(int a, int b)
        => new InitContainer { Inner = { X = a }, Items = { b } };

    // Adversarial negative: a named-local nested mutation uses a real local, not the
    // expression-position dup temp, so it must stay lowered (no initializer).
    public static InitContainer MakeNamedLocalNestedMutation(int a, int b)
    {
        var c = new InitContainer();
        c.Inner.X = a;
        c.Inner.Y = b;
        GC.KeepAlive(c);
        return c;
    }

    public static InitTarget MakePoint(int a, int b) => new InitTarget { X = a, Y = b };

    public static InitTarget MakePointWithField(int a, int b) => new InitTarget { X = a, Z = b };

    public static System.Collections.Generic.List<int> MakeList(int a, int b)
        => new System.Collections.Generic.List<int> { a, b, 42 };

    public static System.Collections.Generic.Dictionary<string, int> MakeDictionaryByIndexer(int a, int b)
        => new System.Collections.Generic.Dictionary<string, int> { ["x"] = a, ["y"] = b };

    public static System.Collections.Generic.Dictionary<string, int> MakeDictionaryByAdd(int a, int b)
        => new System.Collections.Generic.Dictionary<string, int> { { "x", a }, { "y", b } };

    public static NonEnumerableAddTarget MakeNonEnumerableAddLookalike(int a)
    {
        var target = new NonEnumerableAddTarget();
        target.Add(a);
        return target;
    }

    public static InitTarget MakeEmpty() => new InitTarget();

    public static int InitTargetX(InitTarget target) => target.X;

    public static int MakeAndRead(int a) => InitTargetX(new InitTarget { X = a });

    public static InitTarget NamedPointInitializer(int a, int b)
    {
        var target = new InitTarget { X = a, Y = b };
        if (a < 0)
            return null!;
        return target;
    }

    public static System.Collections.Generic.List<int> NamedListInitializer(int a, int b)
    {
        var values = new System.Collections.Generic.List<int> { a, b, 42 };
        if (a < 0)
            return null!;
        return values;
    }

    public static InitTarget NamedPointInitializerKeptAlive(int a)
    {
        var target = new InitTarget { X = a };
        GC.KeepAlive(target);
        return target;
    }

    public static string StringInterpolation(string name, int age)
        => $"Hello, {name}! You are {age} years old.";

    public static string ManualInterpolatedStringHandler(string name)
    {
        var handler = new System.Runtime.CompilerServices.DefaultInterpolatedStringHandler(7, 1);
        handler.AppendLiteral("Hello, ");
        handler.AppendFormatted(name);
        return handler.ToStringAndClear();
    }

    public static string ManualInterpolatedStringHandlerWithProvider(int value)
    {
        var handler = new System.Runtime.CompilerServices.DefaultInterpolatedStringHandler(
            6, 1, System.Globalization.CultureInfo.InvariantCulture);
        handler.AppendLiteral("value=");
        handler.AppendFormatted(value);
        return handler.ToStringAndClear();
    }

    public static int InterpolationToLocal(string name, int age)
    {
        string greeting = $"Hello {name}, you are {age}";
        return greeting.Length;
    }

    public static int InterpolationAsArgument(string name, int age)
        => ConsumeInterpolation($"Hello {name}, you are {age}");

    // Format specifier: AppendFormatted<T>(T, string).
    public static string InterpolationWithFormat(decimal amount)
        => $"Total: {amount:N2}";

    // Alignment specifier: AppendFormatted<T>(T, int) — positive and negative pad.
    public static string InterpolationWithAlignment(int code)
        => $"[{code,5}]";

    public static string InterpolationWithNegativeAlignment(string label)
        => $"[{label,-10}]";

    // Alignment and format together: AppendFormatted<T>(T, int, string).
    public static string InterpolationWithAlignmentAndFormat(double ratio)
        => $"{ratio,8:P1}";

    // Mixed: a plain hole next to a formatted hole in one string.
    public static string InterpolationMixedHoles(string name, int score)
        => $"{name} scored {score:D4}!";

    // A backslash-escaped custom format (the common TimeSpan/DateTime spelling):
    // the IR carries the format as the single-backslash `h\:mm\:ss`, but the hole
    // sits inside a non-verbatim `$"…"`, so it must render escaped (`h\\:mm\\:ss`)
    // or the bare `\:` is CS1009. Recompiling the escaped form restores the same
    // format constant opcode-exact.
    public static string InterpolationWithBackslashFormat(System.TimeSpan ts)
        => $@"{ts:h\:mm\:ss}";

    static int ConsumeInterpolation(string text) => text.Length;

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

    // foreach over an array. The compiler lowers this to a hidden array-copy temp
    // plus an indexed for loop over hidden index/length locals — no enumerator. The
    // raise pass recovers the foreach from that indexed shape.
    public static int ForeachArray(int[] numbers)
    {
        int sum = 0;
        foreach (int n in numbers)
            sum += n;
        return sum;
    }

    // Adversarial: a hand-written indexed for loop over the same array. The index and
    // the element local carry source names and the array is read directly with no
    // copy temp, so this must stay a for loop and never raise to foreach.
    public static int IndexedForOverArray(int[] numbers)
    {
        int sum = 0;
        for (int i = 0; i < numbers.Length; i++)
        {
            int n = numbers[i];
            sum += n;
        }
        return sum;
    }

    // Adversarial near-miss: the same indexed shape that copies the array first,
    // matching the foreach lowering structurally. The copy and index carry source
    // names (`copy`, `i`), so the hidden-scaffold discriminator must keep it a for
    // loop even though the structure is identical to lowered foreach.
    public static int CopyThenIndexedFor(int[] numbers)
    {
        int[] copy = numbers;
        int sum = 0;
        for (int i = 0; i < copy.Length; i++)
        {
            int n = copy[i];
            sum += n;
        }
        return sum;
    }

    // foreach over a string lowers like an array foreach, but with string.Length
    // and string.Chars over a hidden string-copy temp and hidden index local.
    public static int ForeachString(string text)
    {
        int sum = 0;
        foreach (char ch in text)
            sum += ch;
        return sum;
    }

    public static int ForeachStringWithBreak(string text)
    {
        int sum = 0;
        foreach (char ch in text)
        {
            if (ch == ',')
                break;
            sum += ch;
        }
        return sum;
    }

    // Adversarial: direct hand-written string indexed loop. No hidden string copy,
    // so this must stay a for loop.
    public static int IndexedForOverString(string text)
    {
        int sum = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            sum += ch;
        }
        return sum;
    }

    // Adversarial near-miss: structurally matches the string foreach lowering, but
    // source names on the copy and index locals keep it a for loop.
    public static int CopyThenIndexedForString(string text)
    {
        string copy = text;
        int sum = 0;
        for (int i = 0; i < copy.Length; i++)
        {
            char ch = copy[i];
            sum += ch;
        }
        return sum;
    }

    // Two array foreach loops in one method: the pass must raise BOTH, not just
    // the first — passes run once, so a single-match-then-return leaves the second
    // a for loop.
    public static int TwoForeachArrays(int[] a, int[] b)
    {
        int sum = 0;
        foreach (int n in a)
            sum += n;
        foreach (int m in b)
            sum -= m;
        return sum;
    }

    // An enumerator foreach followed by an array foreach in one method. The
    // enumerator phase must raise all its matches AND fall through to the array
    // phase so both forms recover in a single pass.
    public static int EnumeratorThenArrayForeach(IEnumerable<int> items, int[] more)
    {
        int sum = 0;
        foreach (int item in items)
            sum += item;
        foreach (int n in more)
            sum += n;
        return sum;
    }

    // A string foreach and an array foreach in one method. The shared indexed
    // phase must raise both forms; the compiler may even reuse the hidden index
    // slot across the two loops, so the per-slot pooling must tolerate it.
    public static int StringAndArrayForeach(string text, int[] more)
    {
        int sum = 0;
        foreach (char ch in text)
            sum += ch;
        foreach (int n in more)
            sum += n;
        return sum;
    }

    // Two string foreach loops in one method: the indexed phase must raise BOTH.
    public static int TwoForeachStrings(string a, string b)
    {
        int sum = 0;
        foreach (char x in a)
            sum += x;
        foreach (char y in b)
            sum -= y;
        return sum;
    }

    static string s_text = "abc";
    static string GetText() => "xyz";

    // String foreach over receivers other than a plain parameter/local: a static
    // field, a method result, and a string literal. csc still copies the receiver
    // into the hidden string-copy local, so each must raise to foreach with the
    // original receiver expression restored.
    public static int ForeachStringField()
    {
        int sum = 0;
        foreach (char ch in s_text)
            sum += ch;
        return sum;
    }

    public static int ForeachStringMethodResult()
    {
        int sum = 0;
        foreach (char ch in GetText())
            sum += ch;
        return sum;
    }

    public static int ForeachStringLiteral()
    {
        int sum = 0;
        foreach (char ch in "literal")
            sum += ch;
        return sum;
    }

    // Nested string foreach: an inner string foreach inside an outer one. The two
    // loops use distinct hidden copy/index slots (the outer's are live across the
    // inner), so both must raise without the inner's detach disturbing the outer.
    public static int NestedForeachString(string outer, string inner)
    {
        int sum = 0;
        foreach (char a in outer)
            foreach (char b in inner)
                sum += a + b;
        return sum;
    }

    public readonly struct PatternEnumerable
    {
        public PatternEnumerator GetEnumerator() => new();
    }

    public struct PatternEnumerator
    {
        int _current;

        public int Current => _current;

        public bool MoveNext()
        {
            if (_current == 3)
                return false;
            _current++;
            return true;
        }
    }

    public static int ForeachPatternEnumerable(PatternEnumerable source)
    {
        int sum = 0;
        foreach (int value in source)
            sum += value;
        return sum;
    }

    public static int ManualPatternEnumeratorLoop(PatternEnumerable source)
    {
        int sum = 0;
        PatternEnumerator e = source.GetEnumerator();
        while (e.MoveNext())
        {
            int value = e.Current;
            sum += value;
        }
        return sum;
    }

    public static int ForeachRectangularArray(int[,] matrix)
    {
        int sum = 0;
        foreach (int value in matrix)
            sum += value;
        return sum;
    }

    public static int ManualRectangularGetLengthLoops(int[,] matrix)
    {
        int sum = 0;
        for (int i = 0; i < matrix.GetLength(0); i++)
        {
            for (int j = 0; j < matrix.GetLength(1); j++)
            {
                int value = matrix[i, j];
                sum += value;
            }
        }
        return sum;
    }

    public static int CopyThenManualRectangularBoundsLoops(int[,] matrix)
    {
        int[,] copy = matrix;
        int upper0 = copy.GetUpperBound(0);
        int upper1 = copy.GetUpperBound(1);
        int sum = 0;
        for (int i = copy.GetLowerBound(0); i <= upper0; i++)
        {
            for (int j = copy.GetLowerBound(1); j <= upper1; j++)
            {
                int value = copy[i, j];
                sum += value;
            }
        }
        return sum;
    }

    public static int ForeachRectangularArray3D(int[,,] cube)
    {
        int sum = 0;
        foreach (int value in cube)
            sum += value;
        return sum;
    }

    public static int CopyThenManualRectangular3DBoundsLoops(int[,,] cube)
    {
        int[,,] copy = cube;
        int upper0 = copy.GetUpperBound(0);
        int upper1 = copy.GetUpperBound(1);
        int upper2 = copy.GetUpperBound(2);
        int sum = 0;
        for (int i = copy.GetLowerBound(0); i <= upper0; i++)
        {
            for (int j = copy.GetLowerBound(1); j <= upper1; j++)
            {
                for (int k = copy.GetLowerBound(2); k <= upper2; k++)
                {
                    int value = copy[i, j, k];
                    sum += value;
                }
            }
        }
        return sum;
    }

    public static Func<int, int> ClosureCapture(int offset)
    {
        return x => x + offset;
    }

    public static List<int> ClosureWithLinq(int[] items, int threshold)
    {
        return items.Where(x => x > threshold).ToList();
    }

    // A non-capturing lambda passed straight to a call. The compiler caches the
    // delegate in a <>9__ field, but interleaves the receiver's evaluation
    // between the cache load and the null guard, so the load is not adjacent to
    // the guard. LambdaCachePass scans back to it and still collapses the dance.
    public static IEnumerable<int> CachedDelegateArgument(IEnumerable<int> items)
        => items.Where(x => x > 0);

    // Two cached non-capturing delegates in a chain: the second call's receiver
    // (the first call's result) interleaves ahead of the second guard, and reads
    // the first delegate's result slot — both tolerated by the back-scan.
    public static IEnumerable<int> CachedDelegateChain(IEnumerable<int> items)
        => items.Where(x => x > 0).Select(x => x * 2);

    static string CacheMethodGroupIdentity(string value) => value;

    // Static method groups use Roslyn's <>O holder and <N>__Method cache fields,
    // not the <>c/<>9__ lambda cache shape. LambdaCachePass should strip that
    // cache too so LINQ chains with method groups do not stay as shared-forward
    // merge goto soup.
    public static System.Collections.Generic.IEnumerable<string> CachedStaticMethodGroup(System.Collections.Generic.IEnumerable<string> items)
        => System.Linq.Enumerable.Select(items, CacheMethodGroupIdentity);

    // A same-assembly user extension method, called in instance form. csc lowers it
    // to the static call `ExtensionMethodSamples.Doubled(n)`; the [Extension] mark is
    // read from the same-assembly MethodDef, so it should render back as `n.Doubled()`.
    public static int CallsUserExtension(int n) => n.Doubled();

    // Adversarial: a plain static method (NOT [Extension]) with a first parameter,
    // called explicitly. It is byte-identical in shape to an extension call but
    // carries no [Extension] mark, so it must keep the static `Combine(a, b)` form
    // and never sugar to `a.Combine(b)`.
    public static int CallsNonExtensionStatic(int a, int b) => ExtensionMethodSamples.Combine(a, b);

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

    // A checked conversion over a checked add (`add.ovf; conv.ovf.u1`). Both the
    // convert and the binary are checked, but one `checked(...)` already covers
    // the whole expression — the printer must collapse the redundant nesting to
    // `checked((byte)(left + right))`, not `checked((byte)(checked(left + right)))`.
    public static byte CheckedAddToByte(int left, int right)
    {
        return checked((byte)(left + right));
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

    public static string NullCoalescingAssignLocalWithExtraThenStatement(string? input, string fallback)
    {
        string? value = input;
        if (value == null)
        {
            value = fallback;
            LastValue = value.Length;
        }
        return value;
    }

    public static string? CachedName;

    public sealed class CacheHolder
    {
        public string? Cache;
        public string? CacheProp { get; set; }
        public CacheHolder? Next;
    }

    public static string? StaticNameProp { get; set; }

    public static string NullCoalescingAssignStaticField(string fallback)
    {
        CachedName ??= fallback;
        return CachedName;
    }

    public static string NullCoalescingAssignInstanceField(CacheHolder holder, string fallback)
    {
        holder.Cache ??= fallback;
        return holder.Cache;
    }

    public static string NullCoalescingAssignFieldWithExtraThenStatement(CacheHolder holder, string fallback)
    {
        if (holder.Cache == null)
        {
            holder.Cache = fallback;
            LastValue = fallback.Length;
        }
        return holder.Cache;
    }

    public static string NullCoalescingAssignInstanceProperty(CacheHolder holder, string fallback)
    {
        holder.CacheProp ??= fallback;
        return holder.CacheProp;
    }

    public static string NullCoalescingAssignStaticProperty(string fallback)
    {
        StaticNameProp ??= fallback;
        return StaticNameProp;
    }

    public static string NullCoalescingAssignChainedProperty(CacheHolder holder, string fallback)
    {
        holder.Next!.CacheProp ??= fallback;
        return holder.Next!.CacheProp!;
    }

    public static string NullCoalescingAssignPropertyWithExtraThenStatement(CacheHolder holder, string fallback)
    {
        if (holder.CacheProp == null)
        {
            holder.CacheProp = fallback;
            LastValue = fallback.Length;
        }
        return holder.CacheProp;
    }

    // Indexer ??= — the property form plus index arguments. csc spills the receiver
    // and index to locals read identically by the get and set, so the two accesses
    // fold into one re-evaluable d[k] place.
    public static string NullCoalescingAssignIndexer(StringIndexer cache, int key, string fallback)
    {
        cache[key] ??= fallback;
        return cache[key]!;
    }

    // Constant index — exercises the SameOperand equal-constant path (no spill).
    public static string NullCoalescingAssignIndexerConstKey(StringIndexer cache, string fallback)
    {
        cache[0] ??= fallback;
        return cache[0]!;
    }

    public static string NullCoalescingAssignDictionaryIndexer(System.Collections.Generic.Dictionary<string, string> map, string key, string fallback)
    {
        map[key] ??= fallback;
        return map[key];
    }

    // Indexer compound += — set(recv, idx, get(recv, idx) + delta) with a
    // re-evaluable member place folds to recv[idx] += delta.
    public static int CompoundAssignIndexer(CounterIndexer counter, int key, int delta)
    {
        counter[key] += delta;
        return counter[key];
    }

    public static int CompoundAssignDictionaryIndexer(System.Collections.Generic.Dictionary<string, int> map, string key, int delta)
    {
        map[key] += delta;
        return map[key];
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

    public static IEnumerable<bool[]> YieldCollectionExpressionSpread(bool[] arch)
    {
        yield return [.. arch, true];
    }

    public static int[] ArraySpreadWithTail(int[] prefix, int tail) => [.. prefix, tail];

    public static int[] ManualArraySpreadLowering(int[] prefix, int tail)
    {
        int index = 0;
        int[] result = new int[1 + prefix.Length];
        ReadOnlySpan<int> source = new(prefix);
        Span<int> destination = new(result);
        source.CopyTo(destination.Slice(index, source.Length));
        index += source.Length;
        result[index] = tail;
        return result;
    }

    public static int[] ManualArraySpreadLoweringWithSourceTemp(int[] prefix, int tail)
    {
        int[] sourceArray = prefix;
        int index = 0;
        int[] result = new int[1 + sourceArray.Length];
        ReadOnlySpan<int> source = new(sourceArray);
        Span<int> destination = new(result);
        source.CopyTo(destination.Slice(index, source.Length));
        index += source.Length;
        result[index] = tail;
        return result;
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

    // C# promotes every sub-int (byte/sbyte/short/ushort/char) binary result to
    // int — `hi - 0xD800` over a char is typed `int`, never `char`. Storing that
    // into a `uint` local needs an explicit `(uint)` cast (CS0266 without it),
    // which the source spells as a same-width reinterpret that emits no conv —
    // so the IL is just `sub; stloc`. The decompiler must re-insert the `(uint)`
    // cast (the IR keeps the narrow char result type) to render compilable,
    // opcode-exact C#.
    public static uint SubIntPromotionToUInt(char hi, char lo)
    {
        uint a = (uint)(hi - 0xD800);
        uint b = (uint)(lo - 0xDC00);
        return (a | b) + (a & b);
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

    // The array-pin form `fixed (T* p = array)`: the whole array is pinned, so the
    // language inserts the null/empty guard diamond (FixedArrayStatementPass).
    // Distinct from SumPinnedArray's `&values[0]` managed-reference pin.
    public static unsafe void FixedWholeArray(byte[] data)
    {
        fixed (byte* p = data)
        {
            ConsumePointer(p);
        }
    }

    static unsafe void ConsumePointer(byte* p)
    {
    }

    public static unsafe nuint AddressAsNativeUInt()
    {
        int value = 42;
        return (nuint)(&value);
    }

    // A pointer compared to null: csc lowers `p == null` to `ldc.i4.0; conv.u;
    // ceq`, so the zero arrives as a native-int constant. The branch must spell
    // `p == null`, not the CS0019 `p == (nuint)0`.
    public static unsafe int PointerNullBranch(byte* p)
    {
        if (p == null)
        {
            return 1;
        }

        return 2;
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

    // The generic reinterpret-then-read idiom (Enum.IsDefinedPrimitive et al.):
    // take the address of a generic value, reinterpret it as a primitive pointer,
    // and deref — `ldarga; conv.u; ldind.<T>`. The conv to a native int is what
    // makes the naive deref `*((nuint)(&value))` (CS0193); the read must spell its
    // own pointer type: `*(byte*)(&value)`.
    public static unsafe byte ReinterpretFirstByte<T>(T value) where T : unmanaged
    {
        return *(byte*)&value;
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

    // Runtime GenericFunctionPointer-style specimen: a void* is cast to a
    // generic unmanaged function-pointer signature, then invoked through calli.
    // The generic return and argument types must stay on the CallIndirect
    // signature instead of degrading to an unsupported function-pointer shape.
    public static unsafe T InvokesGenericUnmanagedFunctionPointer<T, U>(void* callback, U value)
    {
        return ((delegate* unmanaged<U, T>)callback)(value);
    }

    // Same calli family, but with a byref argument. This pins the ref-kind
    // preservation discriminator from the runtime specimen without pulling in
    // UnmanagedCallersOnly or interop runtime behavior.
    public static unsafe void InvokesUnmanagedFunctionPointerWithRef(void* callback, ref int value, float arg)
    {
        ((delegate* unmanaged<ref int, float, void>)callback)(ref value, arg);
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    public static int UnmanagedStdcallTarget(int value) => value;

    // Runtime UnmanagedCallersOnly-style specimen: ldftn for a target with an
    // unmanaged calling convention is stored through a delegate* local and then
    // invoked through calli. The local's function-pointer type is the convention
    // witness the decompiler must preserve.
    public static unsafe int InvokesUnmanagedCallersOnlyStdcallTarget(int value)
    {
        delegate* unmanaged[Stdcall]<int, int> callback = &UnmanagedStdcallTarget;
        return callback(value);
    }

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

    // A constant byte-array initializer in a ReadOnlySpan<byte> context: csc's
    // 1-byte-element optimization builds the span directly as
    // `new ReadOnlySpan<byte>(ref <PrivateImplementationDetails>.HASH, length)`
    // (a ref to the mapped RVA blob plus its length) rather than through
    // CreateSpan. RvaSpanPass decodes the blob and raises it back to the array
    // literal, which csc re-lowers to the same field — opcode-exact.
    public static System.ReadOnlySpan<byte> ConstantByteSpan() => new byte[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

    static int SumSpan(System.ReadOnlySpan<int> s) => s.Length;

    // A collection expression with NON-constant elements in a ReadOnlySpan<T>
    // context: csc cannot use an RVA blob (the elements are runtime values), so
    // it lowers `[a, b]` to a compiler-synthesized inline-array buffer
    // (`<>y__InlineArray2<int>` / `InlineArray2<int>` on .NET 11+) default-init'd,
    // each slot stored through <PrivateImplementationDetails>.InlineArrayElementRef,
    // and exposed via InlineArrayAsReadOnlySpan. InlineArrayCollectionPass raises
    // it back to `[a, b]`, which csc re-lowers to the same buffer — opcode-exact.
    public static int InlineArraySpan(int a, int b) => SumSpan([a, b]);

    [System.Runtime.CompilerServices.InlineArray(4)]
    public struct Inline4 { private int _element0; }

    Inline4 _inlineField;

    // A real [InlineArray(4)] FIELD viewed as a Span<int> and indexed. Unlike the
    // collection-expression case above (a synthesized buffer built from element
    // stores), this is a direct span conversion of a pre-existing inline-array
    // place: csc lowers `Span<int> s = _inlineField` to
    // <PrivateImplementationDetails>.InlineArrayAsSpan<Inline4, int>(ref
    // _inlineField, 4). The angle-bracketed method name never parses, so the bare
    // call is malformed C#; InlineArrayCollectionPass raises it to the cast
    // `(Span<int>)_inlineField`, which csc re-lowers to the same AsSpan call.
    public int InlineArrayFieldAsSpan(int i)
    {
        System.Span<int> s = _inlineField;
        return s[i];
    }

    // The ReadOnlySpan dual: a span conversion of the same field through the
    // read-only path, lowered to InlineArrayAsReadOnlySpan and raised to
    // `(ReadOnlySpan<int>)_inlineField`.
    public int InlineArrayFieldAsReadOnlySpan(int i)
    {
        System.ReadOnlySpan<int> s = _inlineField;
        return s[i];
    }

    [System.Runtime.CompilerServices.InlineArray(4)]
    public struct RuntimeStyleInlineArray
    {
        private int _element0;

        public int Length => 4;

        public System.Collections.Generic.IEnumerator<int> GetEnumerator()
        {
            for (int i = 0; i < Length; i++)
                yield return this[i];
        }
    }

    public static int RuntimeInlineArrayIndexer(RuntimeStyleInlineArray values, int index)
        => values[index];

    public static int RuntimeInlineArrayForeach(RuntimeStyleInlineArray values)
    {
        int sum = 0;
        foreach (int value in values)
            sum += value;
        return sum;
    }

    int _spanBacking;

    // Adversarial negative (#1045, runtime specimen MyArray<T>.AsSpan in
    // Loader/classloader/InlineArray/InlineArrayValid.cs): a HAND-WRITTEN span view
    // via MemoryMarshal.CreateSpan, then indexed. This is NOT csc's
    // <PrivateImplementationDetails>.InlineArrayAsSpan conversion — a name user code
    // cannot declare — so InlineArrayCollectionPass must NOT raise the CreateSpan to
    // a (Span<int>) cast: it renders faithfully as the CreateSpan call it is.
    public int HandWrittenCreateSpan(int i)
    {
        System.Span<int> s = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _spanBacking, 4);
        return s[i];
    }

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

    // A null-conditional property whose result type is a cross-assembly reference
    // type outside the primitive/string stack-family table. The null arm must
    // adopt the property type at the join, not conflict as object vs Type.
    public static System.Type NullConditionalCrossAssemblyReference(JoinTypeProvider provider)
        => provider?.ResolvedType ?? typeof(object);

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

    // A nullable bool test whose lowering reuses one stack slot first as the
    // nullable receiver string, then as the synthesized bool result. The bool
    // live range must not declare with the earlier receiver type (CS0029).
    public static string ReusedSlotNullableBool(JoinBase node)
        => node?.Label.Contains("x") == true ? "hit" : "miss";

    // The object-initializer-like lowering reuses a stack slot first for the
    // string Status value, then for the nullable list receiver, then for Count.
    // Those disjoint live ranges need distinct synthetic carriers.
    public static SlotReuseSection ReusedSlotStringListCount(System.Collections.Generic.List<string>? missing, bool complete)
    {
        var section = new SlotReuseSection();
        section.Status = complete ? "Complete" : "Partial";
        section.Missing = missing?.Count ?? 0;
        return section;
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

    // ---- runtime-async (async v2) fixtures: awaits lower to AsyncHelpers.Await calls ----
    public static async System.Threading.Tasks.Task<int> AwaitOnce(System.Threading.Tasks.Task<int> t)
    {
        int x = await t;
        return x + 1;
    }

    public static async System.Threading.Tasks.Task AwaitVoid(System.Threading.Tasks.Task t)
    {
        await t;
    }

    public static async System.Threading.Tasks.Task<int> AwaitTwo(System.Threading.Tasks.Task<int> a, System.Threading.Tasks.Task<int> b)
    {
        int x = await a;
        int y = await b;
        return x + y;
    }

    // Ordering stress: three awaits combined. Each earlier await sits on the
    // runtime-async eval stack across the later ones, so all but the last must
    // spill to keep source order (await a, then b, then c).
    public static async System.Threading.Tasks.Task<int> AwaitThree(System.Threading.Tasks.Task<int> a, System.Threading.Tasks.Task<int> b, System.Threading.Tasks.Task<int> c)
    {
        int x = await a;
        int y = await b;
        int z = await c;
        return x + y + z;
    }

    // Ordering stress: two awaits consumed as call arguments. They sit on the
    // stack together and are consumed in order by the call — no statement
    // boundary strands the first, so order is preserved without a spill.
    public static async System.Threading.Tasks.Task<int> AwaitInArguments(System.Threading.Tasks.Task<int> a, System.Threading.Tasks.Task<int> b)
    {
        return AwaitOrderingHelpers.Combine(await a, await b);
    }

    // Ordering stress for the void-call statement path: a void call sequences
    // between the first await and its use, so the first await must spill ahead
    // of the call instead of materializing after it.
    public static async System.Threading.Tasks.Task<int> AwaitAcrossVoidCall(System.Threading.Tasks.Task<int> a, int seed)
    {
        int x = await a;
        AwaitOrderingHelpers.Sink(seed);
        return x + seed;
    }

    public sealed class AsyncDisposableResource : System.IAsyncDisposable
    {
        public AsyncDisposableResource(int value) => Value = value;

        public int Value { get; }

        public System.Threading.Tasks.ValueTask DisposeAsync() => default;
    }

    public static async System.Threading.Tasks.Task<int> AwaitUsingResource(int value)
    {
        await using var resource = new AsyncDisposableResource(value);
        return resource.Value;
    }

    public static async System.Threading.Tasks.Task<int> NestedAwaitUsingResources(int outerValue, int innerValue)
    {
        await using var outer = new AsyncDisposableResource(outerValue);
        await using var inner = new AsyncDisposableResource(innerValue);
        return outer.Value + inner.Value;
    }

    // ---- iterator fixtures: kickoff hands off to a <Method>d__N state machine ----
    // The simplest linear case: two constant yields, no params or captures.
    public static System.Collections.Generic.IEnumerable<int> YieldTwo()
    {
        yield return 1;
        yield return 2;
    }

    // Parameterized + loop: the kickoff still only constructs the state machine
    // (the param is hoisted to a <>3__ field), so it acknowledges the same way.
    public static System.Collections.Generic.IEnumerable<int> YieldRange(int n)
    {
        for (int i = 0; i < n; i++)
            yield return i;
    }

    // Negative: returns IEnumerable<int> but is NOT an iterator (no state machine);
    // array covariance, a plain `return source;`. The pass must not fire.
    public static System.Collections.Generic.IEnumerable<int> NotAnIterator(int[] source)
    {
        return source;
    }

    // Three linear constant yields — exercises a longer state chain (0 -> 1 -> 2 ->
    // terminal) and distinct constant values.
    public static System.Collections.Generic.IEnumerable<int> YieldThree()
    {
        yield return 10;
        yield return 20;
        yield return 30;
    }

    // String-element iterator: the yielded constants are reference-type literals,
    // and the <>2__current field type is string (not int) — confirms the
    // reconstruction is element-type agnostic.
    public static System.Collections.Generic.IEnumerable<string> YieldStrings()
    {
        yield return "a";
        yield return "b";
    }

    // IEnumerator<T>-returning iterator (not IEnumerable<T>): the kickoff creates
    // the state machine with initial state 0 directly, but the linear MoveNext
    // dispatch is identical, so reconstruction still applies.
    public static System.Collections.Generic.IEnumerator<int> YieldEnumerator()
    {
        yield return 7;
        yield return 8;
    }

    // Empty iterator: a bare `yield break;` makes this an iterator that yields
    // nothing. Its MoveNext never stores <>2__current (and csc emits an `if`, not a
    // `switch`), so reconstruction rebuilds the body as `yield break;`.
    public static System.Collections.Generic.IEnumerable<int> JustBreak()
    {
        yield break;
    }

    // Adversarial near-miss of JustBreak: an iterator that yields nothing but runs a
    // self-contained side effect first. Its MoveNext still never stores <>2__current,
    // yet the body is NOT equivalent to a bare `yield break;` — the Console.WriteLine
    // executes lazily on the first MoveNext. Reconstruction must preserve the call.
    public static System.Collections.Generic.IEnumerable<int> BreakWithSideEffect()
    {
        System.Console.WriteLine("side effect");
        yield break;
    }

    // Boundary partner of BreakWithSideEffect: the yield-nothing side effect reads a
    // parameter, which the state machine hoists to a field. That field has no spelling
    // in the kickoff's scope, so reconstruction must decline to honest acknowledgment
    // rather than emit a bare `yield break;` that silently drops the call.
    public static System.Collections.Generic.IEnumerable<int> BreakWithParameterSideEffect(string message)
    {
        System.Console.WriteLine(message);
        yield break;
    }

    // Counting loop with a constant bound and an arithmetic yielded value: the
    // single-yield loop shape reconstruction must map the hoisted loop field to a
    // local and rebuild `i * i` over it (no parameter involved).
    public static System.Collections.Generic.IEnumerable<int> YieldSquares()
    {
        for (int i = 0; i < 4; i++)
            yield return i * i;
    }

    // Nested counting loops: the irreducible state dispatch (the resume edge jumps
    // into the inner loop) is made reducible by the transform-then-restructure path,
    // then the structurer raises both loops (ReducibleIteratorReconstruction).
    public static System.Collections.Generic.IEnumerable<int> YieldGrid()
    {
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                yield return i + j;
    }

    // foreach-delegation: the most common iterator shape. The state machine hoists
    // the enumerator into a field and its MoveNext drives GetEnumerator/MoveNext/
    // Current with a fault-handler Dispose. Irreducible single-yield dispatch plus the
    // split disposal idiom — reconstructed by ForeachIteratorReconstruction (strip both
    // the state scaffolding and the disposal, then recover the foreach).
    public static System.Collections.Generic.IEnumerable<int> YieldEach(System.Collections.Generic.IEnumerable<int> source)
    {
        foreach (var x in source)
            yield return x;
    }

    // Conditional yield: a guarded yield with a trailing unconditional one. Two
    // yields but no loop; the guard references a parameter. Reconstructed by
    // MultiYieldReconstruction (jump-table dispatch the structurer raises to an `if`).
    public static System.Collections.Generic.IEnumerable<int> YieldIf(bool flag)
    {
        if (flag)
            yield return 1;
        yield return 2;
    }

    // Multiple yields inside one loop: more states than the single-yield counting
    // loop, same loop structure. Reconstructed by MultiYieldReconstruction.
    public static System.Collections.Generic.IEnumerable<int> YieldPairs(int n)
    {
        for (int i = 0; i < n; i++)
        {
            yield return i;
            yield return -i;
        }
    }

    // An early `break` out of the INNER foreach over a disposable enumerator:
    // the inner loop body lives in a try (the enumerator's dispose finally), and
    // the break lowers to a non-tail `leave` to the continuation after the inner
    // try/finally — the `if (!matched)` check, NOT a trivial return, so the EH
    // pass cannot inline it away and the leave survives. This is exactly the
    // surviving-leave shape of HashSetEqualityComparer::Equals. The structuring
    // pass treats that container-exiting leave as a clean path terminator (the
    // printer renders the same `goto IL_xxxx; // leave`), so the inner try body
    // raises into `while (...) { if (...) { ...; goto ...; } }` instead of
    // staying flat goto-soup. The post-try continuation block holds the leave's
    // target label and keeps the outer container flat (leave-target-in-container),
    // so the goto's label always survives.
    public static bool AllOuterMatchInner(
        System.Collections.Generic.IEnumerable<int> outer,
        System.Collections.Generic.IEnumerable<int> inner)
    {
        foreach (int o in outer)
        {
            bool matched = false;
            foreach (int i in inner)
            {
                if (i == o)
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
                return false;
        }
        return true;
    }
}

internal static class AwaitOrderingHelpers
{
    public static int Combine(int x, int y) => x - y;
    public static void Sink(int value) { }
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

public sealed class SlotReuseSection
{
    public string Status { get; set; } = "";
    public int Missing { get; set; }
}

public sealed class JoinTypeProvider
{
    public System.Type ResolvedType => typeof(string);
}

public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }

// uint-underlying with a high-bit member: the value 0x80000000 emits as the
// signed int -2147483648, the case the member-map key must agree on.
[System.Flags]
public enum CfgFlags : uint { None = 0, Top = 0x80000000u }

// A flags enum with a named member at a non-low bit (Gamma = 16): a `ref CfgStyles`
// bitwise test reads the enum via `ldind.i4`, so the importer must register the
// pointee's shape and member names for the int constant to name `Gamma`.
[System.Flags]
public enum CfgStyles { None = 0, Alpha = 1, Beta = 2, Gamma = 16 }

// A value-type instance method whose `this` value is read directly: returning
// `this` by value compiles to `ldarg.0; ldobj` (a load-indirect of the `this`
// managed pointer), which must render as `this`, not the CS0193 `*this`.
public struct CfgSelf
{
    public int Value;
    public CfgSelf Identity() => this;
}

// A value-type Equals(object) that reads a field off the unboxed argument:
// `((CfgBoxed)other).Value` compiles to `unbox` (a managed pointer into the
// box) + `ldfld`, so the field receiver is an Unbox node. The printer must
// spell the access `((CfgBoxed)other).Value`, NOT `(ref (CfgBoxed)other).Value`
// — the `ref` form is CS1525 "Invalid expression term 'ref'".
public struct CfgBoxed
{
    public int Value;
    public bool FieldEquals(object other) => Value == ((CfgBoxed)other).Value;
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

/// <summary>Reference-typed indexer for indexer <c>??=</c> fixtures.</summary>
public sealed class StringIndexer
{
    private readonly string?[] _slots = new string?[8];

    public string? this[int index]
    {
        get => _slots[index];
        set => _slots[index] = value;
    }
}

/// <summary>Numeric indexer for indexer compound-assignment fixtures.</summary>
public sealed class CounterIndexer
{
    private readonly int[] _counts = new int[8];

    public int this[int index]
    {
        get => _counts[index];
        set => _counts[index] = value;
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

// Same-assembly samples for extension-method rendering: one genuine [Extension]
// method and one plain static of the same call shape, to exercise the IsExtension
// gate that decides instance-vs-static spelling.
public static class ExtensionMethodSamples
{
    public static int Doubled(this int value) => value * 2;

    public static int Combine(int left, int right) => left + right;
}

// Issue #1142: deconstruction into field targets. Top-level (the test importer
// helper resolves by simple FullName, not nested `+` names).
public sealed class FieldDeconstructionTargets
{
    public int InstanceX;
    public int InstanceY;
    public static int StaticX;
    public static int StaticY;

    // Two instance fields. An instance-field receiver keeps the importer from
    // promoting the tuple temp to a stack slot, so the seed is a StoreLocal.
    public void IntoTwoInstanceFields((int, int) pair) => (InstanceX, InstanceY) = pair;

    // Two static fields — no receiver, so the temp promotes to a stack slot.
    public static void IntoTwoStaticFields((int, int) pair) => (StaticX, StaticY) = pair;
}
