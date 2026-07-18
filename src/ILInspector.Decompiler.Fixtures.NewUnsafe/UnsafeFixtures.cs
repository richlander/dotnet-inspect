namespace ILInspector.Decompiler.Fixtures.NewUnsafe;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public struct FixedBufferResiduals
{
    public fixed int Data[4];

    public int Sum()
    {
        int sum = 0;
        for (int i = 0; i < 4; i++)
        {
            unsafe
            {
                sum += Data[i];
            }
        }
        return sum;
    }
}

public static class StringPinningResiduals
{
    public static int FixedStringFirstChar(string value)
    {
        fixed (char* p = value)
        {
            unsafe
            {
                return p[0];
            }
        }
    }
}

public static class StackallocInitializerResiduals
{
    public static int StackallocPointerInitializer()
    {
        int* values = stackalloc int[] { 1, 2, 3 };
        unsafe
        {
            return values[0] + values[2];
        }
    }

    public static int StackallocSpanInitializer()
    {
        Span<int> values = stackalloc[] { 1, 2, 3 };
        return values[0] + values[2];
    }
}

public static class StackallocInitializerNegatives
{
    public static void PartialCopy()
    {
        unsafe {
            byte* dest = stackalloc byte[12];
            ReadOnlySpan<byte> src = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
            System.Runtime.CompilerServices.Unsafe.CopyBlock(ref *dest, ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src), 10);
        }
    }

    public static void EscapedDestination(byte* escaped)
    {
        unsafe {
            ReadOnlySpan<byte> src = new byte[] { 1, 2, 3 };
            System.Runtime.CompilerServices.Unsafe.CopyBlock(ref *escaped, ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src), 3);
        }
    }

    public static void NonConstantSize()
    {
        unsafe {
            int size = 12;
            byte* dest = stackalloc byte[size];
            ReadOnlySpan<byte> src = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
            System.Runtime.CompilerServices.Unsafe.CopyBlock(ref *dest, ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src), (uint)size);
        }
    }
}

public static class PointerArithmeticFixtures
{
    public static int PointerIncrement(int* p)
    {
        int sum;
        unsafe
        {
            sum = *p;
        }
        p++;
        unsafe
        {
            sum += *p;
        }
        --p;
        unsafe
        {
            return sum + *p;
        }
    }

    public static long PointerArithmeticAndComparison(int* p, int* q)
    {
        int* next = p + 1;
        int* prev = next - 1;
        long distance = q - p;
        return (prev == p && next > p) ? distance : -1;
    }
}

/// <summary>
/// New-rules unsafe fixtures. This assembly is compiled with
/// <c>/features:updated-memory-safety-rules</c>, so the compiler enforces the
/// new unsafe rules and stamps the module with <c>MemorySafetyRulesAttribute</c>.
/// Under those rules the member <c>unsafe</c> modifier no longer introduces a
/// body context, so each unsafe operation is wrapped in an explicit, minimally
/// scoped <c>unsafe { }</c> block. Taking an address, declaring a pointer local,
/// using a function-pointer type, and the <c>fixed</c> statement are all safe
/// under the new rules and stay outside the blocks.
///
/// The IL is identical to the LegacyUnsafe fixtures; the only difference the
/// decompiler can observe is this module's <c>MemorySafetyRulesAttribute</c>,
/// which it must use to render explicit blocks rather than a member modifier.
/// </summary>
public static class UnsafeFixtures
{
    // Only the pointer indirection `*p` needs a context; `&value` and the
    // pointer local are safe.
    public static int DerefPointer(int value)
    {
        int* p = &value;
        unsafe
        {
            return *p;
        }
    }

    public static int ConsumePointer(int* p)
    {
        unsafe
        {
            return *p;
        }
    }

    public static int PassAddress(int value)
    {
        return ConsumePointer(&value);
    }

    // Only the function-pointer invocation needs a context; the parameter type
    // is safe.
    public static int InvokeFunctionPointer(delegate*<int, int> callback, int x)
    {
        unsafe
        {
            return callback(x);
        }
    }

    // A method declared `unsafe` with NO pointers in its signature is still
    // *requires-unsafe* under the new rules: the compiler stamps it with
    // `RequiresUnsafeAttribute`. There is no unsafe operation in its own body,
    // so it needs no block here.
    public static unsafe int Risky() => 42;

    // Calling a requires-unsafe member needs an unsafe context even though no
    // pointer crosses the call boundary. The call — not any intrinsic op — is
    // what forces the block.
    public static int CallRisky()
    {
        unsafe
        {
            return Risky();
        }
    }

    // Compat mode: NativeMemory.Free has a pointer in its signature, so it is
    // requires-unsafe even though its attributes can't be read cross-assembly.
    // The call needs an unsafe context; declaring the pointer parameter does not.
    public static void FreePointer(void* p)
    {
        unsafe
        {
            NativeMemory.Free(p);
        }
    }

    // stackalloc -> Span is unsafe ONLY when the member has [SkipLocalsInit]
    // (the stack space is uninitialized and a Span is a safe wrapper). The
    // stackalloc expression needs the context; using the span is safe.
    [SkipLocalsInit]
    public static int StackAllocSkipInit(int n)
    {
        unsafe
        {
            Span<int> s = stackalloc int[n];
            return s.Length;
        }
    }

    // Without [SkipLocalsInit] the same stackalloc -> Span is SAFE under the new
    // rules and needs no unsafe context.
    public static int StackAllocDefault(int n)
    {
        Span<int> s = stackalloc int[n];
        return s.Length;
    }

    // Runtime-style event data often stages small payloads in a raw stack buffer
    // and reinterprets that storage through a pointer. The stackalloc itself and
    // the pointer element accesses require explicit unsafe contexts under the
    // new rules; the pointer locals do not.
    public static int StackAllocEventData(int eventId)
    {
        unsafe
        {
            byte* payload = stackalloc byte[sizeof(int) * 2];
            int* values = (int*)payload;
            values[0] = eventId;
            values[1] = eventId + 1;
            return values[0] + values[1];
        }
    }

    // `fixed` is safe; only the `p[i]` element access needs a context. The block
    // is scoped to that access inside the loop, not the whole `fixed` statement.
    public static int SumPinned(int[] data)
    {
        int sum = 0;
        fixed (int* p = data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                unsafe
                {
                    sum += p[i];
                }
            }
        }

        return sum;
    }
}
