using System.Buffers;
using System.Runtime.CompilerServices;

namespace ResourceLifecycle;

public static class Cases
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadBeforeReturn(Stream stream)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        int read = stream.Read(buffer, 0, 16);
        ArrayPool<byte>.Shared.Return(buffer);
        return read;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadWithFinallyReturn(Stream stream)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            return stream.Read(buffer, 0, 16);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadWithCatchAllReturn(Stream stream)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            int read = stream.Read(buffer, 0, 16);
            ArrayPool<byte>.Shared.Return(buffer);
            return read;
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadWithTypedCatchReturn(Stream stream)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            int read = stream.Read(buffer, 0, 16);
            ArrayPool<byte>.Shared.Return(buffer);
            return read;
        }
        catch (IOException)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int TwoRootsReleaseOnlyFirst(Stream stream)
    {
        byte[] first = ArrayPool<byte>.Shared.Rent(16);
        byte[] second = ArrayPool<byte>.Shared.Rent(32);
        ArrayPool<byte>.Shared.Return(first);
        int read = stream.Read(second, 0, 32);
        ArrayPool<byte>.Shared.Return(second);
        return read;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowAfterRent(bool release)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        if (release)
            ArrayPool<byte>.Shared.Return(buffer);
        throw new InvalidOperationException();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowAfterTwoConditionalReturns(
        bool releaseFirst,
        bool releaseSecond)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        if (releaseFirst)
            ArrayPool<byte>.Shared.Return(buffer);
        if (releaseSecond)
            ArrayPool<byte>.Shared.Return(buffer);
        throw new InvalidOperationException();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReleaseOnBothBranchesBeforeRead(
        Stream stream,
        bool first)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        if (first)
            ArrayPool<byte>.Shared.Return(buffer);
        else
            ArrayPool<byte>.Shared.Return(buffer);
        return stream.Read(buffer, 0, 16);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadWithConditionalFinallyReturn(
        Stream stream,
        bool release)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            return stream.Read(buffer, 0, 16);
        }
        finally
        {
            if (release)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ProtectedReadOrDirectThrow(
        Stream stream,
        bool directThrow)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        if (directThrow)
            throw new InvalidOperationException();
        try
        {
            return stream.Read(buffer, 0, 16);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
