namespace ILInspector.Decompiler.Fixtures.LegacyUnsafe;

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

    // Function-pointer invocation: `callback(x)` (calli) requires an unsafe
    // context under both old and new rules.
    public static unsafe int InvokeFunctionPointer(delegate*<int, int> callback, int x)
    {
        return callback(x);
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
