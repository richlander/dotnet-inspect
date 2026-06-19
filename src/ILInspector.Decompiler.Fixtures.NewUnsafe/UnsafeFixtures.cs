namespace ILInspector.Decompiler.Fixtures.NewUnsafe;

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

    // Only the function-pointer invocation needs a context; the parameter type
    // is safe.
    public static int InvokeFunctionPointer(delegate*<int, int> callback, int x)
    {
        unsafe
        {
            return callback(x);
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
