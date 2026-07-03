using System;
using System.Runtime.CompilerServices;

namespace RunFaster.AllocationFixture;

public class Program
{
    public static void Main()
    {
        for (int i = 0; i < 50000; i++)
        {
            AllocateOne();
        }
    }

    // The checked-in fixture.nettrace was captured with AllocateOne at metadata
    // token 0x06000002. Keep this method first among user-declared methods, or
    // update the trace and E2EFixtureTests token guard together.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object AllocateOne() => new object();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object AllocateTwo() => new string('a', 16);
}
