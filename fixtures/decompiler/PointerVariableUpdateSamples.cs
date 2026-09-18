namespace ILInspector.Decompiler.Fixtures;

internal static class PointerVariableUpdateSamples
{
    static unsafe ulong* s_position;

    public static unsafe byte* Bytes(byte* cursor, int count)
    {
        unsafe { cursor += count; cursor--; return cursor; }
    }

    public static unsafe ulong* Words(ulong* cursor, int count)
    {
        unsafe { cursor += count; cursor -= count; return cursor; }
    }

    public static unsafe ulong* Steps(ulong* cursor)
    {
        unsafe { cursor++; cursor--; cursor += 3; return cursor; }
    }

    public static unsafe ulong* UnitAssignment(ulong* cursor)
    {
        unsafe { cursor += 1; cursor -= 1; return cursor; }
    }

    public static unsafe ulong* Local(ulong* start, int count)
    {
        unsafe
        {
            ulong* cursor = start;
            while (count-- > 0)
                cursor++;
            return cursor;
        }
    }

    public static unsafe void ByReference(ref ulong* cursor, int count)
    {
        unsafe { cursor += count; cursor--; }
    }

    public static unsafe void Indirect(ulong** cursor, int count)
    {
        unsafe { *cursor += count; (*cursor)++; }
    }

    public static unsafe void Field(Cursor holder, int count)
    {
        unsafe { holder.Position += count; holder.Position--; }
    }

    public static unsafe void StaticField(int count)
    {
        unsafe { s_position += count; s_position--; }
    }

    public static unsafe Action CapturingLambda(ulong* cursor, int count)
    {
        return () => { unsafe { cursor += count; } };
    }

    public static Func<ulong> CaptureLocal(nint address, int count)
    {
        return () =>
        {
            unsafe
            {
                ulong* cursor = (ulong*)address;
                cursor += count;
                return *cursor;
            }
        };
    }

    public static unsafe void Property(Cursor holder, int count)
    {
        unsafe { holder.Current += count; holder.Current++; }
    }

    public static unsafe void Indexer(Cursor holder, int index, int count)
    {
        unsafe { holder[index] += count; }
    }

    public static unsafe ulong* Checked(ulong* cursor, int count)
    {
        unsafe { checked { cursor += count; cursor--; } return cursor; }
    }

    public static unsafe byte* CheckedBytes(byte* cursor, int count)
    {
        unsafe { checked { cursor += count; } return cursor; }
    }

    public static unsafe ulong* CheckedIndex(ulong* cursor, int count)
    {
        unsafe { checked { cursor += unchecked(count + 1); } return cursor; }
    }

    public static unsafe ulong* UnsignedCount(ulong* cursor, uint count)
    {
        unsafe { cursor += count; return cursor; }
    }

    public static unsafe ulong* LongCount(ulong* cursor, long count)
    {
        unsafe { cursor -= count; return cursor; }
    }

    public static unsafe ulong* UnsignedLongCount(ulong* cursor, ulong count)
    {
        unsafe { cursor += count; return cursor; }
    }

    public static unsafe void Loop(ulong* cursor, ulong* end)
    {
        unsafe
        {
            for (; cursor < end; cursor++)
                *cursor = 0;
        }
    }

    public static unsafe ulong* MutatingPointer(ulong* cursor, ulong* replacement)
    {
        unsafe { cursor += Replace(ref cursor, replacement); return cursor; }
    }

    public static unsafe void MutatingField(Cursor holder, ulong* replacement)
    {
        unsafe { holder.Position += Replace(ref holder.Position, replacement); }
    }

    public static unsafe ulong* ByteOffset(ulong* cursor, int bytes)
    {
        unsafe { cursor = (ulong*)((byte*)cursor + bytes); return cursor; }
    }

    public static unsafe ulong* ResultUsed(ulong* cursor)
    {
        unsafe { return cursor++; }
    }

    static unsafe int Replace(ref ulong* cursor, ulong* replacement)
    {
        cursor = replacement;
        return 2;
    }

    internal sealed class Cursor
    {
        public unsafe ulong* Position;
        public int Trace;

        public unsafe ulong* Current
        {
            get { unsafe { Trace = Trace * 10 + 1; return Position; } }
            set { unsafe { Trace = Trace * 10 + 2; Position = value; } }
        }

        public unsafe ulong* this[int index]
        {
            get { unsafe { Trace = Trace * 10 + index; return Position; } }
            set { unsafe { Trace = Trace * 10 + index; Position = value; } }
        }
    }
}
