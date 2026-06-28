extern alias LibA;
extern alias LibB;

namespace DiffAsmCaller
{
    public static class Use
    {
        public static void UseA() => DiffAsmTarget.Api.Ping(new LibA::Shared.Token());

        public static void UseB() => DiffAsmTarget.Api.Ping(new LibB::Shared.Token());
    }
}
