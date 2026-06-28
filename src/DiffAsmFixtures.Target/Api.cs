extern alias LibA;
extern alias LibB;

// #1741: two overloads whose parameter types share the FQN Shared.Token but come from
// different assemblies (LibA vs LibB). The cross-assembly caller graph must keep them
// distinct, which requires parameter-type assembly identity in the key.
namespace DiffAsmTarget
{
    public static class Api
    {
        public static void Ping(LibA::Shared.Token value)
        {
        }

        public static void Ping(LibB::Shared.Token value)
        {
        }
    }
}
