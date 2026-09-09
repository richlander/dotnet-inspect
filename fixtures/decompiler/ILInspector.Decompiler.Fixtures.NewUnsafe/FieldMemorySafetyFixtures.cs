namespace ILInspector.Decompiler.Fixtures.NewUnsafe;

using System.Threading.Tasks;

public static class FieldMemorySafetyFixtures
{
    public sealed class InstanceHolder
    {
        public unsafe int UnsafeField;
        public int SafeField;
    }

    public static class GenericUnsafeFieldHolder<T>
    {
        public static unsafe T Value = default!;
    }

    public static unsafe int UnsafeField;
    public static int SafeField;
    public static int* SafePointerField;
    public static unsafe string? UnsafeTextField;
    public static unsafe int AwaitField;

    public static int ReadUnsafeField()
    {
        unsafe
        {
            return UnsafeField;
        }
    }

    public static void WriteUnsafeField(int value)
    {
        unsafe
        {
            UnsafeField = value;
        }
    }

    public static ref int AddressUnsafeField()
    {
        unsafe
        {
            return ref UnsafeField;
        }
    }

    public static int ReadSafeField() => SafeField;

    public static int* ReadSafePointerField() => SafePointerField;

    public static int ReadInstanceUnsafeField(InstanceHolder holder)
    {
        unsafe
        {
            return holder.UnsafeField;
        }
    }

    public static void WriteInstanceUnsafeField(
        InstanceHolder holder,
        int value)
    {
        unsafe
        {
            holder.UnsafeField = value;
        }
    }

    public static ref int AddressInstanceUnsafeField(
        InstanceHolder holder)
    {
        unsafe
        {
            return ref holder.UnsafeField;
        }
    }

    public static int ReadInstanceSafeField(InstanceHolder holder)
        => holder.SafeField;

    public static T ReadGenericUnsafeField<T>()
    {
        unsafe
        {
            return GenericUnsafeFieldHolder<T>.Value;
        }
    }

    public static string ReadOrInitializeUnsafeField()
    {
        unsafe
        {
            return UnsafeTextField ??= "initialized";
        }
    }

    public static async Task<int> AwaitUnsafeField(Task<int> task)
    {
        bool condition;
        unsafe
        {
            condition = AwaitField != 0;
        }
        if (condition)
            return await task;
        return 0;
    }
}
