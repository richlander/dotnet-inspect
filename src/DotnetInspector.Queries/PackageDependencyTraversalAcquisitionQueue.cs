namespace DotnetInspector.Queries;

internal sealed class PackageDependencyTraversalAcquisitionQueue(
    int capacity,
    CancellationToken token)
{
    private readonly Queue<IWork> _queued = [];
    private readonly List<Task> _active = [];

    internal interface IWork
    {
        Task Start();
    }

    internal sealed class Work<TResult>(Func<Task<TResult>> start) : IWork
    {
        internal Task<TResult>? Result { get; private set; }

        public Task Start()
        {
            try
            {
                return Result = start();
            }
            catch (Exception exception)
            {
                return Result = Task.FromException<TResult>(exception);
            }
        }
    }

    internal Work<TResult> Enqueue<TResult>(Func<Task<TResult>> start)
    {
        var work = new Work<TResult>(start);
        _queued.Enqueue(work);
        return work;
    }

    internal async Task<TResult> AwaitAsync<TResult>(Work<TResult> work)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                Task completed = _active[index];
                if (!completed.IsCompleted)
                    continue;
                _active.RemoveAt(index);
                await completed.ConfigureAwait(false);
            }

            // Graph publication uses Source's synchronous deadline check, which
            // briefly borrows one step from this same capacity.
            if (work.Result is { IsCompleted: true } result
                && _active.Count < capacity)
            {
                return await result.ConfigureAwait(false);
            }

            while (_active.Count < capacity && _queued.TryDequeue(out IWork? next))
            {
                token.ThrowIfCancellationRequested();
                _active.Add(next.Start());
            }

            await Task.WhenAny(_active).ConfigureAwait(false);
        }
    }

    internal async Task DrainAsync()
    {
        _queued.Clear();
        await Task.WhenAll(_active).ConfigureAwait(false);
    }
}
