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

    public static async Task<int> AwaitInLoop(IReadOnlyList<Task<int>> tasks)
    {
        int sum = 0;
        for (int i = 0; i < tasks.Count; i++)
            sum += await tasks[i];

        return sum;
    }

    public static async Task<int> AwaitWithCatch(Task<int> task)
    {
        try
        {
            return await task;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    public static async Task<int> AwaitWithFinally(Task<int> task, Action cleanup)
    {
        try
        {
            return await task;
        }
        finally
        {
            cleanup();
        }
    }

    public static async Task<int> AwaitUsingResource(int value)
    {
        await using var resource = new RuntimeAsyncDisposableResource(value);
        return resource.Value;
    }

    public static async Task<int> NestedAwaitUsingResources(int outerValue, int innerValue)
    {
        await using var outer = new RuntimeAsyncDisposableResource(outerValue);
        await using var inner = new RuntimeAsyncDisposableResource(innerValue);
        return outer.Value + inner.Value;
    }

    public static async Task<int> AwaitForeach(IAsyncEnumerable<int> source)
    {
        int sum = 0;
        await foreach (int value in source)
            sum += value;

        return sum;
    }
}

public sealed class RuntimeAsyncDisposableResource(int value) : IAsyncDisposable
{
    public int Value { get; } = value;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
