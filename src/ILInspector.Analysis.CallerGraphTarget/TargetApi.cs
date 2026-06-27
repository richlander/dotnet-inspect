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
}
