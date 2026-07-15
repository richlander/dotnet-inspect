using System.Runtime.CompilerServices;

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

    public static async Task<int> YieldParameter(YieldAwaitable yield, int value)
    {
        await yield;
        return value;
    }

    public static async Task<int> ClassAwaitableCall(int value)
    {
        await new RuntimeAsyncClassAwaitable();
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
