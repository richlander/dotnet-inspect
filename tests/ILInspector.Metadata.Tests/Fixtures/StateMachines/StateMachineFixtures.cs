using System.Runtime.CompilerServices;

namespace ILInspector.Metadata.StateMachineFixtures;

public static class StateMachineFixtures
{
    public static int Synchronous(int value) =>
        value;

    public static async Task<int> ClassicAsync(int value)
    {
        await Task.Yield();
        return value;
    }

    public static Func<Task<int>> AsyncLambda() =>
        async () =>
        {
            await Task.Yield();
            return 42;
        };

    public static Task<int> AsyncLocalFunction()
    {
        return LocalAsync();

        static async Task<int> LocalAsync()
        {
            await Task.Yield();
            return 42;
        }
    }

    public static async CustomTask<int> CustomBuilderAsync()
    {
        await Task.Yield();
        return 42;
    }

    public static async Task<T> GenericAsync<T>(T value)
    {
        await Task.Yield();
        return value;
    }

    public static async IAsyncEnumerable<int> AsyncIterator(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return 42;
    }

    public static IEnumerable<int> Iterator()
    {
        yield return 42;
    }

    [AsyncStateMachine(typeof(ExplicitStateMachine))]
    public static Task ExplicitAsync() =>
        Task.CompletedTask;

    private struct ExplicitStateMachine : IAsyncStateMachine
    {
        public void MoveNext(int value)
        {
        }

        void IAsyncStateMachine.MoveNext()
        {
        }

        void IAsyncStateMachine.SetStateMachine(
            IAsyncStateMachine stateMachine)
        {
        }
    }
}

public sealed class GenericStateMachineFixtures<T>
{
    public async Task<T> InstanceAsync(T value)
    {
        await Task.Yield();
        return value;
    }
}

public interface IExplicitGenericStateMachines<TFirst, TSecond>
{
    IEnumerable<int> Items { get; }
    Task<int> GetAsync();
}

public sealed class ExplicitGenericStateMachines :
    IExplicitGenericStateMachines<string, int>
{
    IEnumerable<int>
        IExplicitGenericStateMachines<string, int>.Items
    {
        get
        {
            yield return 42;
        }
    }

    async Task<int>
        IExplicitGenericStateMachines<string, int>.GetAsync()
    {
        await Task.Yield();
        return 42;
    }
}

[AsyncMethodBuilder(typeof(CustomTaskMethodBuilder<>))]
public readonly struct CustomTask<T>
{
    readonly Task<T> _task;

    internal CustomTask(Task<T> task) =>
        _task = task;

    public TaskAwaiter<T> GetAwaiter() =>
        _task.GetAwaiter();
}

public struct CustomTaskMethodBuilder<T>
{
    AsyncTaskMethodBuilder<T> _builder;

    public static CustomTaskMethodBuilder<T> Create() =>
        new()
        {
            _builder = AsyncTaskMethodBuilder<T>.Create(),
        };

    public CustomTask<T> Task =>
        new(_builder.Task);

    public void SetResult(T result) =>
        _builder.SetResult(result);

    public void SetException(Exception exception) =>
        _builder.SetException(exception);

    public void SetStateMachine(IAsyncStateMachine stateMachine) =>
        _builder.SetStateMachine(stateMachine);

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine =>
        _builder.Start(ref stateMachine);

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _builder.AwaitOnCompleted(
            ref awaiter,
            ref stateMachine);

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _builder.AwaitUnsafeOnCompleted(
            ref awaiter,
            ref stateMachine);
}
