namespace ILInspector.Analysis.UnsafeMemberFindingFixtures;

// A stackalloc inside a loop body and the same stackalloc on a straight-line
// path. The unsafety producer must mark the first as in a loop so the
// loop-refined guidance is reachable from a real finding, and must not mark
// the second.
//
// CA2014 is the very rule the loop-refined catalog entry points at; the fixture
// exists to trip it.
#pragma warning disable CA2014
public static class StackallocLoopFixtures
{
    public static unsafe int SumInLoop(int rounds)
    {
        int total = 0;
        for (int i = 0; i < rounds; i++)
        {
            byte* buffer = stackalloc byte[16];
            buffer[0] = (byte)i;
            total += buffer[0];
        }

        return total;
    }

    public static unsafe int SumOnce(int value)
    {
        byte* buffer = stackalloc byte[16];
        buffer[0] = (byte)value;
        return buffer[0];
    }
}
#pragma warning restore CA2014
