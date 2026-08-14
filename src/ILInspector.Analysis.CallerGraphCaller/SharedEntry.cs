using System.Buffers;

// Caller-graph cross-assembly fixture (#1579): a real caller. Shared.Entry.Run calls the
// real Target.Api.Ping. Its caller signature is intentionally identical to the twin caller
// assembly to exercise the caller-collapse case.
namespace Shared
{
    public static class Entry
    {
        public static void Run() => Target.Api.Ping();

        // Cross-assembly callee-chain fixture (#3266). A callee graph rooted here and scoped
        // with the target assembly must expand RunOuter -> Run (same assembly) -> Target.Api.Ping
        // (a package boundary), proving the forward map deepens a callee chain across assemblies.
        public static void RunOuter() => Run();

        // CLI cross-library callee fixture (#3632). The target method has its own outbound
        // call, so a scoped graph must continue after crossing the assembly boundary.
        public static void RunAcrossBoundary() => Target.Api.Forward();

        // #3266 fan-out fixture: two call sites to the same callee. The cross-assembly callee tree
        // dedups to one Echo child but must still report a fan-out of 2 (true call-site count).
        // Echo is used so this does not perturb the exact-count caller-graph tests rooted at Ping.
        public static void RunTwice()
        {
            Target.GenericApi.Echo(1);
            Target.GenericApi.Echo(1);
        }

        // Distinct callers of the int and string Ping overloads. A caller graph rooted at one
        // overload must report only its own caller; correspondence that drops parameter
        // types would collapse these onto Ping and cross-link them (#1623 rung 1).
        public static void RunInt() => Target.Api.Ping(1);

        public static void RunString() => Target.Api.Ping("x");

        // Constructed-generic callers (#1339). A caller graph rooted at the open target
        // definition must report these once generic identity is normalized: UseBox invokes
        // Box<int>.Store (a member on a constructed generic type) and UseEcho invokes Echo<int>
        // (a constructed generic method via a MethodSpec).
        public static void UseBox() => new Target.Box<int>().Store(1);

        // #1731: calls the same-arity List<T> overload of Store on the same Box<int>. A
        // caller graph rooted at Store(List<T>) must report this and not UseBox.
        public static void UseBoxList() => new Target.Box<int>().Store(new System.Collections.Generic.List<int>());

        // #1741 (review): calls Store on the different-arity Box<int, string> (Box`2). A
        // caller graph rooted at Box`1.Store must not report this, and vice versa.
        public static void UseBox2() => new Target.Box<int, string>().Store(1);

        public static void UseEcho() => Target.GenericApi.Echo(1);

        // #3340: one caller per method-generic arity.
        public static void UseNonGenericStore() =>
            Target.ArityApi.Store(1);

        public static void UseGenericStore() =>
            Target.ArityApi.Store<string>(1);

        public static unsafe void UseCdeclStore(
            delegate* unmanaged[Cdecl]<int, int> value) =>
            Target.FunctionPointerApi.Store(value);

        public static unsafe void UseStdcallStore(
            delegate* unmanaged[Stdcall]<int, int> value) =>
            Target.FunctionPointerApi.Store(value);

        public static void CallBodiless(Target.IBodilessApi target) =>
            target.Invoke();

        public static void UseVararg() =>
            Target.VarargApi.Sink(
                new Target.VarargArg(),
                __arglist(
                    new Target.VarargArg(),
                    new Target.VarargArg()));

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
}
