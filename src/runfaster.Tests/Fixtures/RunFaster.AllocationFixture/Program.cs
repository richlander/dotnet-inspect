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

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object AllocateOne() => new object();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object AllocateTwo() => new string('a', 16);
}
