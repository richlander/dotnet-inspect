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

        // V1/V2 differ only in the loaded constant value.
        public static int ConstantValue() => 2;

        // V1/V2 have two separated value changes with stable work between them.
        public static int MultipleHunks(int value)
        {
            int first = value + 2;
            Sink(first);
            return value + 4;
        }

        // V2 inserts an operation before the label target. The branch target's
        // raw IL offset shifts, but it still targets the same logical return.
        public static int BranchTargetOffsetShift(bool skip)
        {
            if (skip)
                goto Target;

            Sink(1);
            Sink(2);

        Target:
            return 3;
        }

        // V2 retargets the branch to a different return.
        public static int BranchRetarget(bool skip)
        {
            if (skip)
                goto Second;

            goto First;

        First:
            return 1;

        Second:
            return 2;
        }

        // V2: visible unsafe operation added relative to V1.
        public static unsafe int AddsUnsafe(int value)
        {
            int* values = stackalloc int[1];
            values[0] = value;
            return values[0];
        }

        static void Sink(int value)
        {
            if (value == int.MinValue)
                throw new System.InvalidOperationException();
        }
    }
}
