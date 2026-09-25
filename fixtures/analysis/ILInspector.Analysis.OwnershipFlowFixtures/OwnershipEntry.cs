using System.Buffers;
using System.Threading.Tasks;

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

    public static int RentAndReturnThroughGenericHelper()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ReturnGeneric(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnThroughNestedGenericHelpers()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ForwardGeneric(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnDirectlyOpenGeneric<T>()
    {
        T[] buffer = ArrayPool<T>.Shared.Rent(16);
        try
        {
            ArrayPool<T>.Shared.Return(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnCompoundThroughGenericHelper()
    {
        byte[][] buffer = ArrayPool<byte[]>.Shared.Rent(16);
        try
        {
            ReturnGenericArrays<byte>(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnCompoundThroughNestedGenericHelpers()
    {
        byte[][] buffer = ArrayPool<byte[]>.Shared.Rent(16);
        try
        {
            ForwardGenericArrays<byte>(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnNamedCompoundThroughGenericHelper()
    {
        OwnershipToken[][] buffer =
            ArrayPool<OwnershipToken[]>.Shared.Rent(16);
        try
        {
            ReturnGenericArrays<OwnershipToken>(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnNamedCompoundThroughNestedGenericHelpers()
    {
        OwnershipToken[][] buffer =
            ArrayPool<OwnershipToken[]>.Shared.Rent(16);
        try
        {
            ForwardGenericArrays<OwnershipToken>(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnConstructedCompoundThroughGenericHelper()
    {
        byte[][] buffer = ArrayPool<byte[]>.Shared.Rent(16);
        try
        {
            ForwardConstructedGenericArray<byte>(buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAndReturnNamedConstructedCompoundThroughGenericHelper()
    {
        OwnershipToken[][] buffer =
            ArrayPool<OwnershipToken[]>.Shared.Rent(16);
        try
        {
            ForwardConstructedGenericArray<OwnershipToken>(buffer);
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

    public static int RentOrAllocateStoreThenReturn(
        bool usePool)
    {
        byte[] buffer = usePool
            ? ArrayPool<byte>.Shared.Rent(16)
            : new byte[16];
        _ = new OwnershipBufferHolder(buffer);
        try
        {
            return buffer.Length;
        }
        finally
        {
            if (usePool)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static int RentAndReturnThroughConditionallyReplacedParameter(
        bool replace)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ReturnConditionallyReplaced(buffer, replace);
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

    public static int RentReturnAndForwardExternally()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ReturnRentedArray(buffer);
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

    public static int RentWithLeadingMethodGroup()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            OwnershipSinkWithLeadingCallback(
                new OwnershipWorker().Work,
                buffer);
        }
        finally
        {
            s_ownershipProbe++;
        }
        return buffer.Length;
    }

    public static int RentAcrossUnprotectedBoundaryWithUnrelatedMethodGroup()
    {
        Action callback = OwnershipBarrier;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ObserveResource(buffer);
            GC.KeepAlive(callback);
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

    public static BindingOutcome BindOpenGenericReferences<T>(T value) =>
        new BindingOpenOwner<T>().Apply(
            value,
            static item => new BindingBox<T> { Value = item });

    public static BindingOutcome BindClosedGenericReferences() =>
        new BindingOpenOwner<int>().Apply(
            42,
            static item => new BindingBox<int> { Value = item });

    public static BindingOutcome BindGuidGenericReferences() =>
        new BindingOpenOwner<Guid>().Apply(
            Guid.Empty,
            static item => new BindingBox<Guid> { Value = item });

    public static BindingOutcome InvokeMalformedCallback() =>
        new BindingOwnerWithExtra<byte, int>()
            .BindMalformedCallback(42);

    public static BindingOutcome InvokeMalformedField() =>
        new BindingOwnerWithExtra<byte, int>()
            .Use(new BindingBox<byte>());

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

    static void ReturnConditionallyReplaced(
        byte[] buffer,
        bool replace)
    {
        if (replace)
            buffer = new byte[16];
        ArrayPool<byte>.Shared.Return(buffer);
    }

    static byte[] ReturnRentedArrayToCaller(byte[] buffer) =>
        buffer;

    static void ReplaceRentedArray(ref byte[] buffer) =>
        buffer = [];

    static byte[]? s_rentedArray;
    static object? s_resource;
    static OwnershipToken? s_ownershipToken;
    static int s_ownershipProbe;
    static readonly Exception s_lifecycleException =
        new InvalidOperationException();

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

    static void OwnershipSinkWithLeadingCallback(
        Action callback,
        byte[] leaked) =>
        s_rentedArray = leaked;

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

    static Task ReleaseRentedArrayAsync<T>(T[] buffer) =>
        Task.CompletedTask;

    public static void RentAndReturnDirectly()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        ArrayPool<byte>.Shared.Return(buffer);
    }

    public static void RentTwoAndReturnDirectly()
    {
        byte[] first = ArrayPool<byte>.Shared.Rent(16);
        byte[] second = ArrayPool<byte>.Shared.Rent(32);
        ArrayPool<byte>.Shared.Return(first);
        ArrayPool<byte>.Shared.Return(second);
    }

    public static void RentWithoutReturn()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        ObserveResource(buffer);
        s_ownershipProbe += buffer.Length;
    }

    public static void RentUseAfterReturn()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer[0] = 1;
    }

    public static void RentDoubleReturn()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        ArrayPool<byte>.Shared.Return(buffer);
        ArrayPool<byte>.Shared.Return(buffer);
    }

    public static void RentAndReturnOnEitherBranch(bool first)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        if (first)
            ArrayPool<byte>.Shared.Return(buffer);
        else
            ArrayPool<byte>.Shared.Return(buffer);
    }

    public static void RentAndReturnOnSomeBranches(
        bool first,
        bool second)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        if (first)
            ArrayPool<byte>.Shared.Return(buffer);
        else if (second)
            ArrayPool<byte>.Shared.Return(buffer);
    }

    public static void AcquireAndReleaseThroughConcreteInterface()
    {
        var pool = new OwnershipResourcePool();
        byte[] buffer = pool.Acquire(16);
        pool.Release(buffer);
    }

    public static void RentTwoWithSecondAddress()
    {
        byte[] first = ArrayPool<byte>.Shared.Rent(16);
        byte[] second = ArrayPool<byte>.Shared.Rent(32);
        ArrayPool<byte>.Shared.Return(first);
        ReplaceRentedArray(ref second);
    }

    public static void RentAcrossThrowingBoundary()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        ObserveResource(buffer);
        ArrayPool<byte>.Shared.Return(buffer);
    }

    public static void RentAcrossProtectedThrowingBoundary()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ObserveResource(buffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static void RentAndReleaseAsyncUnobserved()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        ObserveResource(buffer);
        _ = ReleaseRentedArrayAsync(buffer);
        s_ownershipProbe += buffer.Length;
    }

    public static void RentAcrossNormalAndExceptionalExit(bool fail)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        for (int index = 0; index < buffer.Length; index++)
        {
            if (fail)
                throw s_lifecycleException;
            s_ownershipProbe += buffer[index];
        }
    }

    public static void RentAcrossConditionalFinally(
        bool release)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ObserveResource(buffer);
        }
        finally
        {
            if (release)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static void RentAcrossThrowingCleanupSetup()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ObserveResource(buffer);
        }
        finally
        {
            OwnershipBarrier();
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static void RentAcrossConstructorCleanupSetup()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ObserveResource(buffer);
        }
        finally
        {
            _ = new OwnershipWorker();
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static void RentAcrossArrayClearCleanupSetup(int start)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            ObserveResource(buffer);
        }
        finally
        {
            Array.Clear(buffer, start, 1);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public interface IOwnershipResourcePool
    {
        byte[] Acquire(int length);
        void Release(byte[] buffer);
    }

    public sealed class OwnershipResourcePool : IOwnershipResourcePool
    {
        public byte[] Acquire(int length) => new byte[length];

        public void Release(byte[] buffer)
        {
        }
    }

    public static void RentAcrossNestedThrowingBoundary()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        OwnershipSink.ObserveResource(buffer);
        ArrayPool<byte>.Shared.Return(buffer);
    }

    public static int RentReadBeforeReturn(Stream stream)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        int read = stream.Read(buffer, 0, 16);
        ArrayPool<byte>.Shared.Return(buffer);
        return read;
    }

    public static byte[] RentAndReturnToCaller() =>
        ArrayPool<byte>.Shared.Rent(16);

    public static void RentAndStoreDirectly() =>
        s_rentedArray = ArrayPool<byte>.Shared.Rent(16);

    public static void ExerciseTwoResourceDomains()
    {
        byte[] first = AcquireFirstResource();
        byte[] second = AcquireSecondResource();
        ObserveTwoResources(first, second);
    }

    public static void ExerciseTwoResourceDomainsThroughHelper()
    {
        byte[] first = AcquireFirstResource();
        ForwardResource(first);
        s_ownershipProbe += first.Length;
        byte[] second = AcquireSecondResource();
        ForwardResource(second);
        s_ownershipProbe += second.Length;
    }

    public static void ExerciseGenericResourceArguments()
    {
        object resource = AcquirePair<int, string>();
        KeepLocal(ref resource);
        StoreResource(resource);
    }

    public static void ExerciseOwnershipIsolation()
    {
        OwnershipToken resource = AcquireOwnershipToken();
        try
        {
            ForwardOwnershipToken(resource);
        }
        finally
        {
            ++s_ownershipProbe;
        }

        System.GC.KeepAlive(resource);
    }

    public static void ExerciseTrackedResourceMutation()
    {
        TrackedResource resource = AcquireTrackedResource<int>();
        KeepLocal(ref resource);
        MutateTrackedResource(resource);
    }

    public static void RentAddressThenObserve()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16);
        ReplaceRentedArray(ref buffer);
        ObserveResource(buffer);
    }

    static byte[] AcquireFirstResource() => [];
    static byte[] AcquireSecondResource() => [];
    static void ObserveResource(byte[] resource)
    {
    }
    static void ObserveTwoResources(byte[] first, byte[] second)
    {
    }

    static void ForwardResource(byte[] resource) =>
        ObserveResource(resource);

    static void ReturnGeneric<T>(T[] resource) =>
        ArrayPool<T>.Shared.Return(resource);

    static void ForwardGeneric<T>(T[] resource) =>
        ReturnGeneric(resource);

    static void ReturnGenericArrays<T>(T[][] resource) =>
        ArrayPool<T[]>.Shared.Return(resource);

    static void ForwardGenericArrays<T>(T[][] resource) =>
        ReturnGenericArrays<T>(resource);

    static void ForwardConstructedGenericArray<T>(T[][] resource) =>
        ReturnGeneric<T[]>(resource);

    static object AcquirePair<TFirst, TSecond>() =>
        new();

    static OwnershipToken AcquireOwnershipToken() =>
        new();

    static void ForwardOwnershipToken(OwnershipToken resource) =>
        s_ownershipToken = resource;

    static void KeepLocal<T>(ref T resource)
    {
    }

    static void StoreResource(object resource) =>
        s_resource = resource;

    static TrackedResource AcquireTrackedResource<T>() =>
        new();

    static void MutateTrackedResource(TrackedResource resource) =>
        resource.Value = 42;

    sealed class OwnershipWorker
    {
        internal void Work()
        {
        }
    }

    sealed class OwnershipSink
    {
        internal static void ObserveResource(byte[] buffer)
        {
        }

        internal OwnershipSink()
        {
        }

        internal OwnershipSink(int marker, byte[] buffer) =>
            ArrayPool<byte>.Shared.Return(buffer);

        internal void Return(int marker, byte[] buffer) =>
            ArrayPool<byte>.Shared.Return(buffer);
    }

    sealed class OwnershipBufferHolder(byte[] buffer)
    {
        readonly byte[] _buffer = buffer;
    }
}

public sealed class TrackedResource
{
    public int Value;
}

public sealed class OwnershipToken;

public delegate T BindingCallback<T>(T value);

public delegate BindingBox<T> BindingBoxCallback<T>(T value);

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

public sealed class BindingOwnerWithExtra<T, TExtra>
{
    public BindingOutcome BindMalformedCallback(T child) =>
        Apply(child, static value => value);

    public BindingOutcome Apply(T child, BindingCallback<T> callback) =>
        new BindingRejectedOutcome
        {
            ReturnedChild = callback(child),
        };

    public BindingOutcome Use(BindingBox<T> box) =>
        new BindingRejectedOutcome
        {
            ReturnedChild = box.Value,
        };
}

public sealed class BindingOpenOwner<T>
{
    public BindingBox<T>? Child;

    public BindingOutcome Apply(T value, BindingBoxCallback<T> callback)
    {
        Child = callback(value);
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
