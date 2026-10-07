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

    public void WriteAtNestedIndex()
    {
        unsafe
        {
            Data[Data[1]] = 1;
        }
    }

    public void WriteAtNestedZeroIndex()
    {
        unsafe
        {
            Data[Data[0]] = 1;
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

public struct SpanInitializerHalfWord
{
    public short Value;
}

public struct SpanInitializerPoint
{
    public int X;
    public int Y;
}

public static class SpanStackallocInitializers
{
    public static int ByteElements()
    {
        Span<byte> values = stackalloc byte[] { 1, 2, 3, 4 };
        return values[0] + values[3];
    }

    public static int ArgumentElements(int first, int second)
    {
        Span<int> values = stackalloc int[] { first, second };
        return values[0] + values[1];
    }

    public static int WidenedConstantElement(long first)
    {
        Span<long> values = stackalloc long[] { first, 2 };
        return (int)values[1];
    }

    public static int FieldElements(SpanInitializerPoint point)
    {
        Span<int> values = stackalloc int[] { point.X, point.Y };
        return values[1];
    }

    public static int VirtualCallElement(string text)
    {
        Span<int> values = stackalloc int[] { text.Length, 1 };
        return values[1];
    }

    public static int DivisionElement(int value)
    {
        Span<int> values = stackalloc int[] { value / 2, 1 };
        return values[1];
    }

    public static int ArrayElement(int[] source)
    {
        Span<int> values = stackalloc int[] { source[0], 1 };
        return values[1];
    }

    public static int ConditionalElement(bool flag)
    {
        Span<int> values = stackalloc int[] { flag ? 1 : 2, 3 };
        return values[1];
    }

    public static int StructElements(
        SpanInitializerPoint first,
        SpanInitializerPoint second)
    {
        Span<SpanInitializerPoint> values =
            stackalloc SpanInitializerPoint[] { first, second };
        return values[1].X;
    }

    public static int NativeIntElements()
    {
        Span<nint> values = stackalloc nint[] { 1, 2 };
        return (int)values[1];
    }

    public static int PointerLocalWrapped(int length)
    {
        unsafe
        {
            int* values = stackalloc int[length];
            values[0] = 1;
            values[1] = 2;
            return new Span<int>(values, length)[0];
        }
    }

    public static int OutOfBoundsStoreWrapped()
    {
        unsafe
        {
            int* values = stackalloc int[3];
            values[5] = 1;
            return new Span<int>(values, 3)[0];
        }
    }

    // The constant index wraps in IL; tracing must treat it as unprovable rather
    // than fail the library analysis.
    public static int OverflowingConstantIndex()
    {
        Span<int> values = stackalloc int[2];
        unsafe
        {
            int* pointer = stackalloc int[2];
            pointer[0x4000000000000000L] = 1;
        }
        return values[0];
    }

    // Each int32 product wraps to int.MinValue, so the real displacement is -4 GiB
    // although the exact terms cancel; the store is not provably in bounds.
    public static int WrappingDisplacementWrapped()
    {
        unsafe
        {
            int* values = stackalloc int[2];
            *(int*)((byte*)values
                + 0x40000000 * sizeof(SpanInitializerHalfWord)
                + (-0x40000000) * sizeof(SpanInitializerHalfWord)) = 1;
            return new Span<int>(values, 2)[0];
        }
    }

    // An int32 product of 0x40000000 * sizeof(nint) wraps to int.MinValue, so
    // this store lands 2 GiB below an exactly sized 8 GiB allocation.
    public static int Int32WrapLargeAllocationWrapped()
    {
        unsafe
        {
            nint* values = stackalloc nint[0x40000001];
            *(nint*)((byte*)values + 0x40000000 * sizeof(nint)) = 1;
            return new Span<nint>(values, 0x40000001).Length;
        }
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

    public static class UnsafeAsyncFixtures
    {
        public readonly struct PointerTarget
        {
            public Task<int> GetTask() => Task.FromResult(42);
        }

        public sealed class UnsafeHolder
        {
            public static unsafe PointerTarget* Pointer => null;
            public unsafe int* Risky => null;
        }

        public static unsafe bool Risky(int value) => value == 0;

        public static async Task<int> AwaitPointerReceiver()
        {
            Task<int> task;
            unsafe
            {
                task = UnsafeHolder.Pointer->GetTask();
            }
            return await task;
        }

        public static async Task<int> IfUnsafeConditionAwaitBody(
            Task<int> task,
            int seed)
        {
            bool condition;
            unsafe
            {
                condition = Risky(seed);
            }
            if (condition)
                return await task;
            return 0;
        }

        public static async Task<int> WhileUnsafeConditionAwaitBody(
            Task<int> task,
            UnsafeHolder holder)
        {
            bool go;
            unsafe
            {
                go = *holder.Risky == 0;
            }
            int sum = 0;
            while (go)
            {
                sum += await task;
                go = false;
            }
            return sum;
        }

        public static async Task<int> DoWhileUnsafeConditionAwaitBody(
            Task<int> task,
            UnsafeHolder holder)
        {
            bool go;
            int sum = 0;
            do
            {
                sum += await task;
                unsafe
                {
                    go = *holder.Risky > 0;
                }
            }
            while (go);
            return sum;
        }

        public static async Task<int> SwitchUnsafeSelectorAwaitArms(
            Task<int> task,
            UnsafeHolder holder)
        {
            int selector;
            unsafe
            {
                selector = *holder.Risky;
            }
            switch (selector)
            {
                case 0:
                    return await task;
                case 1:
                    return 1 + await task;
                default:
                    return 3 + await task;
            }
        }

        public static async Task<int> UsingUnsafeResourceAwaitBody(
            Task<int> task,
            UnsafeHolder holder)
        {
            MemoryStream resource;
            unsafe
            {
                resource = new MemoryStream(*holder.Risky);
            }
            using (resource)
                return resource.Capacity + await task;
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

public sealed class AccessorContractFixtures
{
    public unsafe AccessorContractFixtures() { }

    public int Property
    {
        unsafe get => 42;
        set { }
    }

    public unsafe event Action Changed
    {
        add { }
        remove { }
    }

    public static int ReadProperty(AccessorContractFixtures fixture)
    {
        unsafe
        {
            return fixture.Property;
        }
    }

    public static void WriteProperty(
        AccessorContractFixtures fixture,
        int value)
        => fixture.Property = value;

    public static void SubscribeEvent(
        AccessorContractFixtures fixture,
        Action handler)
    {
        unsafe
        {
            fixture.Changed += handler;
        }
    }

    public static AccessorContractFixtures Create()
    {
        unsafe
        {
            return new AccessorContractFixtures();
        }
    }
}

// Unsafe member findings attribution: generated bodies fold into the declared
// member, and private and internal members are findings alongside public ones.
public static class UnsafeFindingAttributionSamples
{
    public static System.Collections.Generic.IEnumerable<int> IteratorDereference(int[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            int value;
            unsafe
            {
                int* pointer = stackalloc int[1];
                *pointer = values[index];
                value = *pointer;
            }
            yield return value;
        }
    }

    public static int LocalFunctionDereference(int[] values)
    {
        unsafe
        {
            return Read(values);
        }

        static unsafe int Read(int[] source)
        {
            unsafe
            {
                fixed (int* pointer = source)
                {
                    return *pointer;
                }
            }
        }
    }

    static unsafe int PrivateDereference(int* pointer)
    {
        unsafe
        {
            return *pointer;
        }
    }

    internal static unsafe int InternalDereference(int* pointer)
    {
        unsafe
        {
            return *pointer;
        }
    }

    public static unsafe int CallsNonPublic(int* pointer)
    {
        unsafe
        {
            return PrivateDereference(pointer) + InternalDereference(pointer);
        }
    }

    public static int Sink;

    static unsafe delegate*<int> s_reader = &ReadZero;

    static int ReadZero() => 0;

    // Roslyn lifts this finally into <IteratorFinallyCall>d__N.<>m__Finally1,
    // which only an authenticated owner may claim.
    public static System.Collections.Generic.IEnumerable<int> IteratorFinallyCall(int[] values)
    {
        try
        {
            foreach (int value in values)
                yield return value;
        }
        finally
        {
            unsafe
            {
                Sink = s_reader();
            }
        }
    }

    // An extern UnsafeAccessor is an IL declaration without a body: no
    // applicable input, not a gap.
    [System.Runtime.CompilerServices.UnsafeAccessor(
        System.Runtime.CompilerServices.UnsafeAccessorKind.StaticField,
        Name = "s_value")]
    safe static extern ref int TargetValue(UnsafeAccessorTarget? target);
}

public sealed class UnsafeAccessorTarget
{
    static int s_value = 1;

    public static int Value => s_value;
}
