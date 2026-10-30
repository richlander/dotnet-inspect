using System.Runtime.CompilerServices;

namespace ILInspector.Analysis.IteratorFixtures;

// Kept beside the iterators for the same reason: no source member claims
// this body, so every census over this assembly is incomplete. The body sits
// in a nested compiler-generated type that no member references, so no edge
// names its owner.
public static class UnclaimedGeneratedBodySample
{
    public static int Read(int value) => value;

    [CompilerGenerated]
    static class Unclaimed
    {
        internal static int Hidden(int value) =>
            Unsafe.As<int, int>(ref value);
    }
}
