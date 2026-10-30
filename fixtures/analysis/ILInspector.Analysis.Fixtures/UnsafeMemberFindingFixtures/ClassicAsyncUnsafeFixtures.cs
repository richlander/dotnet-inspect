namespace ILInspector.Analysis.UnsafeMemberFindingFixtures;

// Classic async compiles to a state machine, so the unsafe body evidence lives
// in MoveNext and must fold into the declared async method.
public static class ClassicAsyncUnsafeFixtures
{
    public static async Task<int> AsyncStackAllocation(int value)
    {
        await Task.Yield();
        unsafe
        {
            int* pointer = stackalloc int[1];
            *pointer = value;
            return *pointer;
        }
    }
}
