using System.Collections.Generic;

namespace DiffFixtureSample
{
    public static class DiffSample
    {
        // V1: a single allocation, not in a loop (1 allocation).
        public static void RegressesAllocInLoop(int n, List<object> sink)
        {
            sink.Add(new object());
        }

        // V1: three allocations.
        public static void ImprovesAlloc(List<object> sink)
        {
            sink.Add(new object());
            sink.Add(new object());
            sink.Add(new object());
        }

        // V1: one allocation, not in a loop (count 1, allocInLoop=false).
        public static void SameAllocationCountBecomesHot(int n, List<object> sink)
        {
            sink.Add(new object());
        }

        // Identical in both versions -> no diff row.
        public static int Stable() => 42;
    }
}