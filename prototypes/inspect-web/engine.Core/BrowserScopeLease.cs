using System.Runtime.Versioning;

namespace InspectWeb.Engine;

[SupportedOSPlatform("browser")]
internal sealed class BrowserScopeLease<TScope> : IDisposable
    where TScope : class, IDisposable
{
    Action? _release;

    internal BrowserScopeLease(
        TScope scope,
        Action release)
    {
        Scope = scope;
        _release = release;
    }

    public TScope Scope { get; }

    public void Dispose() =>
        Interlocked.Exchange(ref _release, null)?.Invoke();
}
