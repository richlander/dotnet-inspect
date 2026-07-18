namespace ILInspector.Decompiler.Fixtures.LegacyUnsafe;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public unsafe struct FixedBufferResiduals
{
    public fixed int Data[4];

    public int Sum()
    {
        int sum = 0;
        for (int i = 0; i < 4; i++)
            sum += Data[i];
        return sum;
    }
}

public static class StringPinningResiduals
{
    public static unsafe int FixedStringFirstChar(string value)
    {
        fixed (char* p = value)
            return p[0];
    }
}

public static class StackallocInitializerResiduals
{
    public static unsafe int StackallocPointerInitializer()
    {
        int* values = stackalloc int[] { 1, 2, 3 };
        return values[0] + values[2];
    }

    public static int StackallocSpanInitializer()
    {
        Span<int> values = stackalloc[] { 1, 2, 3 };
        return values[0] + values[2];
    }
}

public static class StackallocInitializerNegatives
{
    public static unsafe void PartialCopy()
    {
        byte* dest = stackalloc byte[12];
        ReadOnlySpan<byte> src = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        System.Runtime.CompilerServices.Unsafe.CopyBlock(ref *dest, ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src), 10);
    }

    public static unsafe void EscapedDestination(byte* escaped)
    {
        ReadOnlySpan<byte> src = new byte[] { 1, 2, 3 };
        System.Runtime.CompilerServices.Unsafe.CopyBlock(ref *escaped, ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src), 3);
    }

    public static unsafe void NonConstantSize()
    {
        int size = 12;
        byte* dest = stackalloc byte[size];
        ReadOnlySpan<byte> src = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        System.Runtime.CompilerServices.Unsafe.CopyBlock(ref *dest, ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src), (uint)size);
    }

    public static unsafe void SharedSpanLiteralMutation()
    {
        byte* dest = stackalloc byte[12];
        ReadOnlySpan<byte> src = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        System.Runtime.CompilerServices.Unsafe.CopyBlock(ref *dest, ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src), 12);
        System.Console.WriteLine(src[0]);
    }

    public static unsafe void InterveningSideEffect()
    {
        byte* dest = stackalloc byte[12];
        System.Console.WriteLine("Side effect");
        ReadOnlySpan<byte> src = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        System.Runtime.CompilerServices.Unsafe.CopyBlock(ref *dest, ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src), 12);
    }

    public static unsafe void InterveningWrite()
    {
        byte* dest = stackalloc byte[12];
        dest[0] = 42;
        ReadOnlySpan<byte> src = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        System.Runtime.CompilerServices.Unsafe.CopyBlock(ref *dest, ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src), 12);
    }

    public static unsafe void CrossBlockCopy(bool condition)
    {
        byte* dest = stackalloc byte[12];
        if (condition)
        {
            ReadOnlySpan<byte> src = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
            System.Runtime.CompilerServices.Unsafe.CopyBlock(ref *dest, ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src), 12);
        }
    }

    public static unsafe int CoalescedSpanLocal()
    {
        int* a = stackalloc int[] { 1, 2, 3 };
        int* b = stackalloc int[] { 4, 5, 6 };
        return a[0] + b[0];
    }
}

public static class PointerArithmeticFixtures
{
    public static unsafe int PointerIncrement(int* p)
    {
        int sum = *p;
        p++;
        sum += *p;
        --p;
        return sum + *p;
    }

    public static unsafe long PointerArithmeticAndComparison(int* p, int* q)
    {
        int* next = p + 1;
        int* prev = next - 1;
        long distance = q - p;
        return (prev == p && next > p) ? distance : -1;
    }
}

/// <summary>
/// Legacy-rules unsafe fixtures. Each method uses the member <c>unsafe</c>
/// modifier, which under pre-memory-safety semantics makes the whole body an
/// unsafe context. The decompiler should reproduce that member modifier when
/// the source module has no <c>MemorySafetyRulesAttribute</c>.
/// </summary>
public static class UnsafeFixtures
{
    // Pointer indirection: `*p` requires an unsafe context under both old and
    // new rules.
    public static unsafe int DerefPointer(int value)
    {
        int* p = &value;
        return *p;
    }

    public static unsafe int ConsumePointer(int* p) => *p;

    public static unsafe int PassAddress(int value) => ConsumePointer(&value);

    // Function-pointer invocation: `callback(x)` (calli) requires an unsafe
    // context under both old and new rules.
    public static unsafe int InvokeFunctionPointer(delegate*<int, int> callback, int x)
    {
        return callback(x);
    }

    // A requires-unsafe member: declared `unsafe` with no pointers. Under legacy
    // rules calling it needs an unsafe context too — supplied by the caller's
    // member `unsafe` modifier — so the body printer emits no block. The IL is
    // identical to the new-rules fixture.
    public static unsafe int Risky() => 42;

    public static unsafe int CallRisky() => Risky();

    // Compat mode: calling NativeMemory.Free (pointer in signature) needs an
    // unsafe context, supplied here by the member modifier. IL is identical to
    // the new-rules fixture.
    public static unsafe void FreePointer(void* p) => NativeMemory.Free(p);

    // stackalloc -> Span is legal in safe C# today, so no member modifier is
    // needed. The IL (including the cleared localsinit flag from
    // [SkipLocalsInit]) is identical to the new-rules fixture.
    [SkipLocalsInit]
    public static int StackAllocSkipInit(int n)
    {
        Span<int> s = stackalloc int[n];
        return s.Length;
    }

    public static int StackAllocDefault(int n)
    {
        Span<int> s = stackalloc int[n];
        return s.Length;
    }

    public static unsafe int StackAllocEventData(int eventId)
    {
        byte* payload = stackalloc byte[sizeof(int) * 2];
        int* values = (int*)payload;
        values[0] = eventId;
        values[1] = eventId + 1;
        return values[0] + values[1];
    }

    // `fixed` is SAFE under the new rules, but the pointer element access
    // `p[i]` inside the loop is not. A minimal-scope emitter must wrap only the
    // unsafe access, not the whole `fixed` statement.
    public static unsafe int SumPinned(int[] data)
    {
        int sum = 0;
        fixed (int* p = data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                sum += p[i];
            }
        }

        return sum;
    }
}
