namespace ILInspector.Decompiler.Fixtures.LegacyUnsafe;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public unsafe struct FixedBufferResiduals
{
    public fixed int Data[4];
    public fixed int Values[4];

    public int Sum()
    {
        int sum = 0;
        for (int i = 0; i < 4; i++)
            sum += Data[i];
        return sum;
    }

    public int ReadAt(int index) => Data[index];

    public int ReadFirst() => Data[0];

    public void WriteAt(int index, int value) => Data[index] = value;

    public void WriteFirst(int value) => Data[0] = value;

    public void WriteAtNestedIndex() => Data[Data[1]] = 1;

    public void WriteAtNestedZeroIndex() => Data[Data[0]] = 1;

    public int ReadAtThroughFixedAddress(int index)
    {
        fixed (int* p = &Data[index])
            return *p;
    }

    public ref int RefAt(int index) => ref Data[index];

    public ref int RefFirst() => ref Data[0];

    public void PassByRef(int index) => Increment(ref Data[index]);

    public void PassFirstByRef() => Increment(ref Data[0]);

    public int RefLocalIncrement(int index)
    {
        ref int value = ref Data[index];
        value++;
        return value;
    }

    public int RefLocalFirstIncrement()
    {
        ref int value = ref Data[0];
        value++;
        return value;
    }

    public static int PointerLocalValue(int index)
    {
        FixedBufferResiduals value = default;
        int* p = null;
        p = &value.Data[index];
        *p = 42;
        return *p;
    }

    public static int PointerLocalFirstValue()
    {
        FixedBufferResiduals value = default;
        int* p = null;
        p = &value.Data[0];
        *p = 42;
        return *p;
    }

    public static int* PointerReturn(int index)
    {
        FixedBufferResiduals value = default;
        return &value.Data[index];
    }

    public static int* PointerReturnFirst()
    {
        FixedBufferResiduals value = default;
        return &value.Data[0];
    }

    public static void PointerArgument(int index)
    {
        FixedBufferResiduals value = default;
        ConsumePointer(&value.Data[index]);
    }

    public static void PointerArgumentFirst()
    {
        FixedBufferResiduals value = default;
        ConsumePointer(&value.Data[0]);
    }

    public string FormatValue(int index) => Values[index].ToString();

    public int FirstValueHashCode() => Values[0].GetHashCode();

    static void Increment(ref int value) => value++;

    static void ConsumePointer(int* value) => _ = value;
}

public unsafe struct FixedBufferPrimitiveResiduals
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

    public bool ReadBool(int index) => Bools[index];
    public void WriteBool(int index, bool value) => Bools[index] = value;
    public byte ReadByte(int index) => Bytes[index];
    public void WriteByte(int index, byte value) => Bytes[index] = value;
    public sbyte ReadSByte(int index) => SBytes[index];
    public void WriteSByte(int index, sbyte value) => SBytes[index] = value;
    public char ReadChar(int index) => Chars[index];
    public void WriteChar(int index, char value) => Chars[index] = value;
    public short ReadShort(int index) => Shorts[index];
    public void WriteShort(int index, short value) => Shorts[index] = value;
    public ushort ReadUShort(int index) => UShorts[index];
    public void WriteUShort(int index, ushort value) => UShorts[index] = value;
    public int ReadInt(int index) => Ints[index];
    public void WriteInt(int index, int value) => Ints[index] = value;
    public uint ReadUInt(int index) => UInts[index];
    public void WriteUInt(int index, uint value) => UInts[index] = value;
    public long ReadLong(int index) => Longs[index];
    public void WriteLong(int index, long value) => Longs[index] = value;
    public ulong ReadULong(int index) => ULongs[index];
    public void WriteULong(int index, ulong value) => ULongs[index] = value;
    public float ReadFloat(int index) => Floats[index];
    public void WriteFloat(int index, float value) => Floats[index] = value;
    public double ReadDouble(int index) => Doubles[index];
    public void WriteDouble(int index, double value) => Doubles[index] = value;

    public int ReadIntAtLong(long index) => Ints[index];
    public int ReadIntAtUInt(uint index) => Ints[index];
    public int ReadIntAtULong(ulong index) => Ints[index];
    public void WriteIntAtLong(long index, int value) => Ints[index] = value;
    public void WriteIntAtUInt(uint index, int value) => Ints[index] = value;
    public void WriteIntAtULong(ulong index, int value) => Ints[index] = value;
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
    public static unsafe int CoalescedSpanLocal()
    {
        int* a = stackalloc int[] { 1, 2, 3 };
        int* b = stackalloc int[] { 4, 5, 6 };
        return a[0] + b[0];
    }

    public static unsafe void SourceAuthoredCopyBlock(byte* dest, byte* src)
    {
        System.Runtime.CompilerServices.Unsafe.CopyBlock(dest, src, 10);
    }

    // Boolean/floating-point RVA elements are not covered by RvaSpanPass's shared
    // primitive decoder in a bit-preserving way (Boolean canonicalizes to true/false,
    // NaN payloads collapse), so StackAllocInitializerPass declines these element
    // types until that decoder round-trips exactly.
    public static unsafe bool StackallocBooleanInitializer()
    {
        bool* values = stackalloc bool[] { true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false };
        return values[0] || values[2];
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
