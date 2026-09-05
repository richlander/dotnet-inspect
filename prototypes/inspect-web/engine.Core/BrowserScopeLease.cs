using System.Runtime.Versioning;

namespace InspectWeb.Engine;

/// <summary>
/// One asynchronous pin on a registry-owned scope. Releasing the last pin of a scope whose
/// removal was requested disposes that scope, so the release is awaited rather than dropped.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserScopeLease<TScope> : IAsyncDisposable
    where TScope : class, IAsyncDisposable
{
    Func<ValueTask>? _release;

    internal BrowserScopeLease(
        TScope scope,
        Func<ValueTask> release)
    {
        Scope = scope;
        _release = release;
    }

    public TScope Scope { get; }

    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _release, null) is { } release
            ? release()
            : ValueTask.CompletedTask;
}
