namespace UnsafeMemberDiffFixtures
{
    // Unsafe member Diff (#10770): one member Removed, one Added, one
    // Changed, one Present through a renumbered closure, and one Present
    // unchanged. Only V2 adds the leading lambda that renumbers the closure, and
    // the public surface is identical in both versions.
    public static class UnsafeMemberDiffSample
    {
        public static int RemovedUnsafe(int value) => value;

        public static unsafe int AddedUnsafe(int value)
        {
            int* slot = stackalloc int[1];
            *slot = value;
            return *slot + 1;
        }

        public static unsafe int ChangedUnsafe(int value)
        {
            int* values = stackalloc int[1];
            values[0] = value;
            return values[0];
        }

        public static System.Func<int, int> RenumberedClosure()
        {
            System.Func<int, int> leading = static current => current + 2;
            return leading(0) == 2
                ? static current =>
                {
                    unsafe
                    {
                        int* slot = stackalloc int[1];
                        *slot = current;
                        return *slot;
                    }
                }
                : leading;
        }

        public static unsafe int StableUnsafe(int value)
        {
            int* slot = stackalloc int[1];
            *slot = value;
            return *slot;
        }
    }

    // A Type present only in V2: every member is Added at Type surface.
    public static class AddedUnsafeType
    {
        public static unsafe int Read(int value)
        {
            int* slot = stackalloc int[1];
            *slot = value;
            return *slot;
        }
    }
}
