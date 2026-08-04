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

        // #3266 fan-out fixture: two call sites to the same callee. The cross-assembly callee tree
        // dedups to one Echo child but must still report a fan-out of 2 (true call-site count).
        // Echo is used so this does not perturb the exact-count caller-graph tests rooted at Ping.
        public static void RunTwice()
        {
            Target.GenericApi.Echo(1);
            Target.GenericApi.Echo(1);
        }

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

        // #1731: calls the same-arity List<T> overload of Store on the same Box<int>. A
        // caller graph rooted at Store(List<T>) must report this and not UseBox.
        public static void UseBoxList() => new Target.Box<int>().Store(new System.Collections.Generic.List<int>());

        // #1741 (review): calls Store on the different-arity Box<int, string> (Box`2). A
        // caller graph rooted at Box`1.Store must not report this, and vice versa.
        public static void UseBox2() => new Target.Box<int, string>().Store(1);

        public static void UseEcho() => Target.GenericApi.Echo(1);

        public static void CallBodiless(Target.IBodilessApi target) =>
            target.Invoke();
    }
}
