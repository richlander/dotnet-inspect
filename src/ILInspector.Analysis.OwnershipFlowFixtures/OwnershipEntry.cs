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
