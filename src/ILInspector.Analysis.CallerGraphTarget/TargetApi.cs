// Caller-graph cross-assembly fixture (#1579): the real target. The negative tests root
// the caller graph at Target.Api.Ping in this assembly.
namespace Target
{
    public static class Api
    {
        public static void Ping()
        {
        }
    }
}
