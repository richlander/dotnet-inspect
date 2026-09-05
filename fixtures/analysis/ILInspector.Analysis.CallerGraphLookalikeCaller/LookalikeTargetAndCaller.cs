// Caller-graph cross-assembly fixture (#1579): the lookalike caller. Declares its own
// Target.Api.Ping (same FQN as the real target, different assembly) and calls it. Its calls
// resolve to the in-assembly lookalike, so a graph rooted at the real target must not report
// Consumer.Entry.Run as a caller; name-only callee matching would misattribute it.
namespace Target
{
    public static class Api
    {
        public static void Ping()
        {
        }
    }
}

namespace Consumer
{
    public static class Entry
    {
        public static void Run() => Target.Api.Ping();
    }
}
