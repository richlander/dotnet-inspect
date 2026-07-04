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

        // V1/V2 differ only in the loaded constant value.
        public static int ConstantValue() => 1;

        // V2 inserts an operation before the label target. The branch target's
        // raw IL offset shifts, but it still targets the same logical return.
        public static int BranchTargetOffsetShift(bool skip)
        {
            if (skip)
                goto Target;

            Sink(1);

        Target:
            return 3;
        }

        // V2 retargets the branch to a different return.
        public static int BranchRetarget(bool skip)
        {
            if (skip)
                goto First;

            goto Second;

        First:
            return 1;

        Second:
            return 2;
        }

        // V1: safe body. V2 adds a visible unsafe operation.
        public static int AddsUnsafe(int value) => value;

        static void Sink(int value)
        {
            if (value == int.MinValue)
                throw new System.InvalidOperationException();
        }
    }
}