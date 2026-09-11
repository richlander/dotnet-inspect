// Caller-graph cross-assembly fixture (#1579): the real target. The negative tests root
// the caller graph at Target.Api.Ping in this assembly.
namespace Target
{
    public static class Api
    {
        public static void Ping()
        {
        }

        // Overloads sharing the simple name Ping. A cross-assembly caller graph rooted at one
        // overload must not pull in callers of the other; that requires the param-bearing
        // catalog member correspondence, which is exercised across assemblies.
        public static void Ping(int value)
        {
        }

        public static void Ping(string value)
        {
        }

        public static void Forward() => Leaf();

        public static void Leaf()
        {
        }
    }

    // Generic target surfaces (#1339). A cross-assembly caller graph rooted at the open
    // definition must report constructed-instantiation callers in another assembly, which it
    // can only do once generic member identity is normalized to the open definition.

    // Member on a generic type: a caller invokes Store on a constructed Box<int>, keyed on the
    // instantiation, which must match the open Box<T>.Store(T) target.
    public sealed class Box<T>
    {
        public void Store(T value)
        {
        }

        // #1731: a same-arity overload on the same generic declaring type. The
        // cross-assembly caller graph must keep Store(T) and Store(List<T>) distinct;
        // the prior arity-only erasure collapsed them.
        public void Store(System.Collections.Generic.List<T> values)
        {
        }
    }

    // #1741 (review): a different-arity generic type with the SAME simple name (Box`2)
    // and a same-name/same-arity method. The declaring-type portion of the caller-graph
    // key must preserve generic arity so Box`1.Store and Box`2.Store stay distinct.
    public sealed class Box<T1, T2>
    {
        public void Store(T1 value)
        {
        }
    }

    // Generic method on a non-generic type: a caller invokes Echo<int>, a MethodSpec keyed on
    // the instantiation, which must match the open Echo<T>(T) target.
    public static class GenericApi
    {
        public static T Echo<T>(T value) => value;
    }

    // #3340: method overloads that differ only by generic arity.
    public static class ArityApi
    {
        public static void Store(int value)
        {
        }

        public static void Store<T>(int value)
        {
        }
    }

    public static unsafe class FunctionPointerApi
    {
        public static void Store(
            delegate* unmanaged[Cdecl]<int, int> value)
        {
        }

        public static void Store(
            delegate* unmanaged[Stdcall]<int, int> value)
        {
        }
    }

    public sealed class InstanceRecursionApi
    {
        public bool Recurse(int depth) =>
            depth > 0 && Recurse(depth - 1);

        public int RecurseTwice(int depth) =>
            depth <= 0
                ? 0
                : RecurseTwice(depth - 1)
                    + RecurseTwice(depth - 2);

        public bool IsEven(int value) =>
            value == 0 || IsOdd(value - 1);

        bool IsOdd(int value) =>
            value != 0 && IsEven(value - 1);
    }

    public interface IBodilessApi
    {
        void Invoke();
    }

    public sealed class VarargArg
    {
    }

    public static class VarargApi
    {
        public static VarargArg Sink(
            VarargArg required,
            __arglist) =>
            required;

        public static VarargArg Sink(
            VarargArg required,
            VarargArg second,
            VarargArg third,
            __arglist) =>
            required;
    }
}
