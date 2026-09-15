using System.Buffers;

namespace Ownership;

public static class Entry
{
    public static int RentAndReturnThroughHelper()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ReturnRentedArray(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndForwardToReturn()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ForwardRentedArray(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndStoreThroughHelper()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            StoreRentedArray(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnFromHelper()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            _ = ReturnRentedArrayToCaller(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnAtTwoSites(bool first)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            if (first)
                ReturnRentedArray(buffer);
            else
                ReturnRentedArray(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndTakeAddress()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ReplaceRentedArray(ref buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndForwardExternally()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            GC.KeepAlive(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnThroughInstance()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            new OwnershipSink().Return(7, buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnThroughConstructor()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            _ = new OwnershipSink(7, buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentWithMethodGroup(
        int first,
        byte[] other,
        int second)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            OwnershipSinkWithCallback(
                buffer,
                first,
                other,
                second,
                new OwnershipWorker().Work);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static unsafe void RentWithFunctionPointer(
        delegate*<byte[], void> callback)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        OwnershipBarrier();
        callback(buffer);
    }

    public static int RentWithReturnedValue(byte[] other)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            OwnershipSinkWithReturnedValue(
                buffer,
                OwnershipMarker(),
                other);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndUseFrameworkWrappers()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        Span<byte> constructed = new(buffer);
        Span<byte> converted = buffer;
        Span<byte> extension = buffer.AsSpan(1);
        Memory<byte> extensionMemory = buffer.AsMemory(1);
        ReadOnlySpan<byte> readOnlySpan = converted;
        Memory<byte> constructedMemory = new(buffer);
        Memory<byte> memory = buffer;
        ReadOnlyMemory<byte> readOnlyMemory = memory;
        int length =
            constructed.Slice(1).Length
            + converted.Slice(1, 2).Length
            + extension.Length
            + extensionMemory.Span.Length
            + readOnlySpan.Slice(1).Length
            + constructedMemory.Slice(1).Span.Length
            + memory.Slice(1).Span.Length
            + readOnlyMemory.Slice(1, 2).Span.Length;
        ArrayPool<byte>.Shared.Return(buffer);
        return length;
    }

    public static BindingOutcome BindOccurrenceReferences()
    {
        var owner = new BindingOwner<byte>();
        return owner.Apply(42, static value => value);
    }

    public static byte BindCollapsedResourceKinds() =>
        new BindingGenericOwner<byte>().Apply<byte>(1, 2);

    public static void BindEnumOutcomeAliases()
    {
        _ = GetBindingStatus();
    }

    static BindingStatus GetBindingStatus() => BindingStatus.Rejected;

    static void ForwardRentedArray(byte[] buffer) =>
        ReturnRentedArray(buffer);

    static void ReturnRentedArray(byte[] buffer) =>
        ArrayPool<byte>.Shared.Return(buffer);

    static byte[] ReturnRentedArrayToCaller(byte[] buffer) =>
        buffer;

    static void ReplaceRentedArray(ref byte[] buffer) =>
        buffer = [];

    static byte[]? s_rentedArray;
    static int s_ownershipProbe;

    static void StoreRentedArray(byte[] buffer) =>
        s_rentedArray = buffer;

    static void OwnershipSinkWithCallback(
        byte[] leaked,
        int first,
        byte[] returned,
        int second,
        Action callback)
    {
        s_rentedArray = leaked;
        ArrayPool<byte>.Shared.Return(returned);
    }

    static void OwnershipSinkWithReturnedValue(
        byte[] leaked,
        int marker,
        byte[] returned)
    {
        s_rentedArray = leaked;
        ArrayPool<byte>.Shared.Return(returned);
    }

    static int OwnershipMarker() => 7;

    static void OwnershipBarrier()
    {
    }

    sealed class OwnershipWorker
    {
        internal void Work()
        {
        }
    }

    sealed class OwnershipSink
    {
        internal OwnershipSink()
        {
        }

        internal OwnershipSink(int marker, byte[] buffer) =>
            ArrayPool<byte>.Shared.Return(buffer);

        internal void Return(int marker, byte[] buffer) =>
            ArrayPool<byte>.Shared.Return(buffer);
    }
}

public delegate T BindingCallback<T>(T value);

public enum BindingStatus
{
    Rejected = 0,
    Retry = 0,
    Accepted = 1,
}

public sealed class BindingOwner<T>
{
    public T? Child;
    public int ChildCount;
    public int[] ChildCounts = [];
    public BindingBox<T>? ChildBox;

    public BindingOutcome Apply(T child, BindingCallback<T> callback)
    {
        Child = callback(child);
        ChildCount++;
        return new BindingRejectedOutcome();
    }
}

public sealed class BindingBox<T>
{
    public T? Value;
}

public abstract class BindingOutcome;

public sealed class BindingAcceptedOutcome : BindingOutcome;

public sealed class BindingRejectedOutcome : BindingOutcome
{
    public object? ReturnedChild;
}

public sealed class BindingGenericOwner<T>
{
    public TMethod Apply<TMethod>(T value, TMethod result) => result;
}
