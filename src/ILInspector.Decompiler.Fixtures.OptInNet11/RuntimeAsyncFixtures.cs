using System.Runtime.CompilerServices;

namespace ILInspector.Decompiler.Fixtures.OptInNet11;

public static class RuntimeAsyncTaskFixtures
{
    public static async Task<int> AwaitTask(Task<int> task, int addend)
        => await task + addend;

    public static async ValueTask<int> AwaitValueTask(ValueTask<int> task)
        => await task;

    public static async Task<int> AwaitConfiguredTask(Task<int> task)
        => await task.ConfigureAwait(false);

    public static async Task<int> AwaitTwo(Task<int> first, Task<int> second)
    {
        int left = await first;
        int right = await second;
        return left + right;
    }

    public static async Task<int> AwaitInBranch(Task<int> task, bool condition)
    {
        if (condition)
            return await task;

        return 0;
    }
}

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

    public static async Task<int> YieldParameter(YieldAwaitable yield, int value)
    {
        await yield;
        return value;
    }

    public static async Task<int> ClassAwaitableParameter(
        RuntimeAsyncClassAwaitable awaitable,
        int value)
    {
        await awaitable;
        return value;
    }

    public static async Task<int> ExtensionAwaitableParameter(
        RuntimeAsyncExtensionAwaitable awaitable,
        int value)
    {
        await awaitable;
        return value;
    }
}

public sealed class RuntimeAsyncClassAwaitable
{
    public RuntimeAsyncClassAwaiter GetAwaiter() => new();
}

public sealed class RuntimeAsyncClassAwaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted => false;
    public void GetResult() { }
    public void OnCompleted(Action continuation) => continuation();
    public void UnsafeOnCompleted(Action continuation) => continuation();
}

public readonly struct RuntimeAsyncExtensionAwaitable;

public static class RuntimeAsyncExtensionAwaitableExtensions
{
    public static RuntimeAsyncExtensionAwaiter GetAwaiter(
        this RuntimeAsyncExtensionAwaitable awaitable)
        => new();
}

public readonly struct RuntimeAsyncExtensionAwaiter : INotifyCompletion
{
    public bool IsCompleted => false;
    public void GetResult() { }
    public void OnCompleted(Action continuation) => continuation();
}
