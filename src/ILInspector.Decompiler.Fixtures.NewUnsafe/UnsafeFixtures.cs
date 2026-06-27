namespace ILInspector.Decompiler.Fixtures.NewUnsafe;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
