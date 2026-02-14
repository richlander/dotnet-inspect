namespace DotnetInspector.Core;

/// <summary>
/// Runs async downloads concurrently and yields results in completion order.
///
/// Usage:
///   var dl = new Downloader&lt;PackResult&gt;();
///   dl.Add(() => DownloadFirst());
///   dl.Add(() => DownloadSecond());
///   await foreach (var result in dl)  // starts all immediately
///
/// The caller can break early; remaining downloads continue to completion
/// (e.g., for cache warming).
/// </summary>
public class Downloader<T> : IAsyncEnumerable<T> where T : class
{
    private readonly List<Func<Task<T?>>> _work = [];

    /// <summary>
    /// Queues work. Not started until iteration begins.
    /// </summary>
    public void Add(Func<Task<T?>> work) => _work.Add(work);

    public async IAsyncEnumerator<T> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        // Start all downloads immediately — same server, maximize overlap
        List<Task<T?>> tasks = [];
        foreach (var work in _work)
        {
            tasks.Add(work());
        }

        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(completed);

            var result = await completed.ConfigureAwait(false);
            if (result != null)
            {
                yield return result;
            }
        }
    }
}
