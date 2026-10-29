namespace UnsafeMemberDiffFixtures
{
    // Unsafe member Diff (#10770): one member Removed, one Added, one
    // Changed, one Present through a renumbered closure, and one Present
    // unchanged. Only V2 adds the leading lambda that renumbers the closure, and
    // the public surface is identical in both versions.
    public static class UnsafeMemberDiffSample
    {
        public static unsafe int RemovedUnsafe(int value)
        {
            int* slot = stackalloc int[1];
            *slot = value;
            return *slot;
        }

        public static int AddedUnsafe(int value) => value + 1;

        public static unsafe int ChangedUnsafe(int value)
        {
            int* values = stackalloc int[1];
            values[0] = value;
            return value;
        }

        public static System.Func<int, int> RenumberedClosure()
            => static current =>
            {
                unsafe
                {
                    int* slot = stackalloc int[1];
                    *slot = current;
                    return *slot;
                }
            };

        public static unsafe int StableUnsafe(int value)
        {
            int* slot = stackalloc int[1];
            *slot = value;
            return *slot;
        }
    }

    // A Type present only in V1: every member is Removed at Type surface.
    public static class RemovedUnsafeType
    {
        public static unsafe int Read(int value)
        {
            int* slot = stackalloc int[1];
            *slot = value;
            return *slot;
        }
    }
}
