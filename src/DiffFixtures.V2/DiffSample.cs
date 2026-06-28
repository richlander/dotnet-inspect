using System.Collections.Generic;

namespace DiffFixtureSample
{
    public static class DiffSample
    {
        // V2: the allocation moved into a loop and gained a second site (2 allocations,
        // in-loop) -> an allocation regression that is hot.
        public static void RegressesAllocInLoop(int n, List<object> sink)
        {
            sink.Add(new object());
            for (int i = 0; i < n; i++)
                sink.Add(new object());
        }

        // V2: a single allocation -> an improvement vs V1's three.
        public static void ImprovesAlloc(List<object> sink)
        {
            sink.Add(new object());
        }

        // V2: the same single allocation moved into a loop (count still 1, but
        // allocInLoop=true) -> a hotness-only allocation regression the count delta misses.
        public static void SameAllocationCountBecomesHot(int n, List<object> sink)
        {
            for (int i = 0; i < n; i++)
                sink.Add(new object());
        }

        // Identical in both versions -> no diff row.
        public static int Stable() => 42;
    }
}
