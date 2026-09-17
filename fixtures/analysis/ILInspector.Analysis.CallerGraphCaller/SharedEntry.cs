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

    }

    // Preserves the root-to-private-use-site shape from dotnet-inspect commit
    // ac9e9b4d2be08d4cdeeb851aafef3811bdcb35e1:
    // MemberSourceDiffPresentationAdapter.Create -> CreateMappedTextDiff -> AddChange.
    public static class RootPathEntry
    {
        public static void Create() => CreateMappedTextDiff();

        public static void CreateAlternative() => CreateMappedTextDiff();

        public static void CreateOuter() => Create();

        internal static void CreateMappedTextDiff()
        {
            AddChange();
            AddChange();
        }

        static void AddChange()
        {
            Target.RootPathApi.RootPathUse();
        }

        public static int Value
        {
            get
            {
                AccessorUse();
                return 0;
            }
            private set => AccessorUse();
        }

        public static void AssignValue() => Value = 1;

        static void AccessorUse()
        {
            Target.RootPathApi.AccessorPathUse();
        }

        static void UnreachableUse() =>
            Target.RootPathApi.UnreachablePathUse();

        public static void CycleRoot() => CycleA();

        static void CycleA() => CycleB();

        static void CycleB()
        {
            CycleA();
            CycleUse();
        }

        static void CycleUse()
        {
        }

        public static async Task AsyncRoot()
        {
            await Task.Yield();
            AsyncUse();
        }

        static void AsyncUse()
        {
        }
    }

    public static class PublicRootDirectUse
    {
        public static void Use() =>
            Target.RootPathApi.PublicPathUse();
    }
}
