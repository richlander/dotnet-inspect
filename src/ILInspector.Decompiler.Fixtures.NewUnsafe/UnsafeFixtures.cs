namespace ILInspector.Decompiler.Fixtures.NewUnsafe;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public struct FixedBufferResiduals
{
    public fixed int Data[4];
    public fixed int Values[4];

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

    public int ReadAt(int index)
    {
        unsafe
        {
            return Data[index];
        }
    }

    public int ReadFirst()
    {
        unsafe
        {
            return Data[0];
        }
    }

    public void WriteAt(int index, int value)
    {
        unsafe
        {
            Data[index] = value;
        }
    }

    public void WriteFirst(int value)
    {
        unsafe
        {
            Data[0] = value;
        }
    }

    public int ReadAtThroughFixedAddress(int index)
    {
        unsafe
        {
            fixed (int* p = &Data[index])
            {
                return *p;
            }
        }
    }

    public ref int RefAt(int index)
    {
        unsafe
        {
            return ref Data[index];
        }
    }

    public ref int RefFirst()
    {
        unsafe
        {
            return ref Data[0];
        }
    }

    public void PassByRef(int index)
    {
        unsafe
        {
            Increment(ref Data[index]);
        }
    }

    public void PassFirstByRef()
    {
        unsafe
        {
            Increment(ref Data[0]);
        }
    }

    public int RefLocalIncrement(int index)
    {
        unsafe
        {
            ref int value = ref Data[index];
            value++;
            return value;
        }
    }

    public int RefLocalFirstIncrement()
    {
        unsafe
        {
            ref int value = ref Data[0];
            value++;
            return value;
        }
    }

    public static int PointerLocalValue(int index)
    {
        unsafe
        {
            FixedBufferResiduals value = default;
            int* p = null;
            p = &value.Data[index];
            *p = 42;
            return *p;
        }
    }

    public static int PointerLocalFirstValue()
    {
        unsafe
        {
            FixedBufferResiduals value = default;
            int* p = null;
            p = &value.Data[0];
            *p = 42;
            return *p;
        }
    }

    public static int* PointerReturn(int index)
    {
        unsafe
        {
            FixedBufferResiduals value = default;
            return &value.Data[index];
        }
    }

    public static int* PointerReturnFirst()
    {
        unsafe
        {
            FixedBufferResiduals value = default;
            return &value.Data[0];
        }
    }

    public static void PointerArgument(int index)
    {
        unsafe
        {
            FixedBufferResiduals value = default;
            ConsumePointer(&value.Data[index]);
        }
    }

    public static void PointerArgumentFirst()
    {
        unsafe
        {
            FixedBufferResiduals value = default;
            ConsumePointer(&value.Data[0]);
        }
    }

    public string FormatValue(int index)
    {
        unsafe
        {
            return Values[index].ToString();
        }
    }

    public int FirstValueHashCode()
    {
        unsafe
        {
            return Values[0].GetHashCode();
        }
    }

    static void Increment(ref int value) => value++;

    static unsafe void ConsumePointer(int* value) => _ = value;
}

public struct FixedBufferPrimitiveResiduals
{
    public fixed bool Bools[4];
    public fixed byte Bytes[4];
    public fixed sbyte SBytes[4];
    public fixed char Chars[4];
    public fixed short Shorts[4];
    public fixed ushort UShorts[4];
    public fixed int Ints[4];
    public fixed uint UInts[4];
    public fixed long Longs[4];
    public fixed ulong ULongs[4];
    public fixed float Floats[4];
    public fixed double Doubles[4];

    public bool ReadBool(int index)
    {
        unsafe { return Bools[index]; }
    }

    public void WriteBool(int index, bool value)
    {
        unsafe { Bools[index] = value; }
    }

    public byte ReadByte(int index)
    {
        unsafe { return Bytes[index]; }
    }

    public void WriteByte(int index, byte value)
    {
        unsafe { Bytes[index] = value; }
    }

    public sbyte ReadSByte(int index)
    {
        unsafe { return SBytes[index]; }
    }

    public void WriteSByte(int index, sbyte value)
    {
        unsafe { SBytes[index] = value; }
    }

    public char ReadChar(int index)
    {
        unsafe { return Chars[index]; }
    }

    public void WriteChar(int index, char value)
    {
        unsafe { Chars[index] = value; }
    }

    public short ReadShort(int index)
    {
        unsafe { return Shorts[index]; }
    }

    public void WriteShort(int index, short value)
    {
        unsafe { Shorts[index] = value; }
    }

    public ushort ReadUShort(int index)
    {
        unsafe { return UShorts[index]; }
    }

    public void WriteUShort(int index, ushort value)
    {
        unsafe { UShorts[index] = value; }
    }

    public int ReadInt(int index)
    {
        unsafe { return Ints[index]; }
    }

    public void WriteInt(int index, int value)
    {
        unsafe { Ints[index] = value; }
    }

    public uint ReadUInt(int index)
    {
        unsafe { return UInts[index]; }
    }

    public void WriteUInt(int index, uint value)
    {
        unsafe { UInts[index] = value; }
    }

    public long ReadLong(int index)
    {
        unsafe { return Longs[index]; }
    }

    public void WriteLong(int index, long value)
    {
        unsafe { Longs[index] = value; }
    }

    public ulong ReadULong(int index)
    {
        unsafe { return ULongs[index]; }
    }

    public void WriteULong(int index, ulong value)
    {
        unsafe { ULongs[index] = value; }
    }

    public float ReadFloat(int index)
    {
        unsafe { return Floats[index]; }
    }

    public void WriteFloat(int index, float value)
    {
        unsafe { Floats[index] = value; }
    }

    public double ReadDouble(int index)
    {
        unsafe { return Doubles[index]; }
    }

    public void WriteDouble(int index, double value)
    {
        unsafe { Doubles[index] = value; }
    }

    public int ReadIntAtLong(long index)
    {
        unsafe { return Ints[index]; }
    }

    public int ReadIntAtUInt(uint index)
    {
        unsafe { return Ints[index]; }
    }

    public int ReadIntAtULong(ulong index)
    {
        unsafe { return Ints[index]; }
    }

    public void WriteIntAtLong(long index, int value)
    {
        unsafe { Ints[index] = value; }
    }

    public void WriteIntAtUInt(uint index, int value)
    {
        unsafe { Ints[index] = value; }
    }

    public void WriteIntAtULong(ulong index, int value)
    {
        unsafe { Ints[index] = value; }
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
    public static int CoalescedSpanLocal()
    {
        unsafe {
            int* a = stackalloc int[] { 1, 2, 3 };
            int* b = stackalloc int[] { 4, 5, 6 };
            return a[0] + b[0];
        }
    }

    public static unsafe void SourceAuthoredCopyBlock(byte* dest, byte* src)
    {
        unsafe {
            System.Runtime.CompilerServices.Unsafe.CopyBlock(dest, src, 10);
        }
    }

    // Boolean/floating-point RVA elements are not covered by RvaSpanPass's shared
    // primitive decoder in a bit-preserving way (Boolean canonicalizes to true/false,
    // NaN payloads collapse), so StackAllocInitializerPass declines these element
    // types until that decoder round-trips exactly.
    public static unsafe bool StackallocBooleanInitializer()
    {
        unsafe
        {
            bool* values = stackalloc bool[] { true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false };
            return values[0] || values[2];
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

    public static unsafe int Risky() => 42;

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
