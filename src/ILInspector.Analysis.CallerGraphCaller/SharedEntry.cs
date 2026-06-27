// Caller-graph cross-assembly fixture (#1579): a real caller. Shared.Entry.Run calls the
// real Target.Api.Ping. Its caller signature is intentionally identical to the twin caller
// assembly to exercise the caller-collapse case.
namespace Shared
{
    public static class Entry
    {
        public static void Run() => Target.Api.Ping();

        // Distinct callers of the int and string Ping overloads. A caller graph rooted at one
        // overload must report only its own caller; a CallerGraphKey that drops parameter
        // types would collapse these onto Ping and cross-link them (#1623 rung 1).
        public static void RunInt() => Target.Api.Ping(1);

        public static void RunString() => Target.Api.Ping("x");

        // Constructed-generic callers (#1339). A caller graph rooted at the open target
        // definition must report these once generic identity is normalized: UseBox invokes
        // Box<int>.Store (a member on a constructed generic type) and UseEcho invokes Echo<int>
        // (a constructed generic method via a MethodSpec).
        public static void UseBox() => new Target.Box<int>().Store(1);

        public static void UseEcho() => Target.GenericApi.Echo(1);
    }
}
