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
        // CallerGraphKey, which is only exercised across assemblies (#1623 rung 1).
        public static void Ping(int value)
        {
        }

        public static void Ping(string value)
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
}
