// Caller-graph transitive-scope fixture (#3331). This assembly references ONLY the caller
// assembly, never the target, yet it belongs in a caller graph rooted at Target.Api.Ping:
// Indirect.Entry.Run -> Shared.Entry.Run -> Target.Api.Ping is two hops.
//
// It exists to pin the transitive obligation of the AssemblyRef prefilter. A prefilter that
// asks only "does this assembly reference the target?" drops this one and silently shortens
// the graph, so the reference set here must stay free of the target assembly.
namespace Indirect
{
    public static class Entry
    {
        public static void Run() => Shared.Entry.Run();
    }
}
