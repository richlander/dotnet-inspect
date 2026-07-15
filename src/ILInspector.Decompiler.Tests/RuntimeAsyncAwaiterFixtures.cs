namespace ILInspector.Decompiler.Tests;

public static class RuntimeAsyncAwaiterFixtures
{
    public static async Task<int> YieldOnce(int value)
    {
        await Task.Yield();
        return value;
    }

    public static async Task<int> YieldTwice(int value)
    {
        await Task.Yield();
        await Task.Yield();
        return value;
    }

    public static async Task<int> YieldInBranch(bool condition)
    {
        if (condition)
            await Task.Yield();

        return 1;
    }
}
