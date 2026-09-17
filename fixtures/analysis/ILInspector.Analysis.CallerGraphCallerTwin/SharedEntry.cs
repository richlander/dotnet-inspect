// Caller-graph cross-assembly fixture (#1579): the twin real caller. Same Shared.Entry.Run
// signature as ILInspector.Analysis.CallerGraphCaller, also calling the real Target.Api.Ping.
namespace Shared
{
    public static class Entry
    {
        public static void Run() => Target.Api.Ping();

        public static IEnumerable<int> IteratorRoot()
        {
            IteratorUse();
            yield return 1;
        }

        static void IteratorUse()
        {
        }
    }
}
