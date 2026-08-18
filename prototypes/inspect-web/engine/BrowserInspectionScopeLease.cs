using System.Runtime.Versioning;

namespace InspectWeb.Engine;

[SupportedOSPlatform("browser")]
internal sealed class BrowserInspectionScopeLease : IDisposable
{
    Action? _release;

    internal BrowserInspectionScopeLease(
        BrowserInspectionScope scope,
        Action release)
    {
        Scope = scope;
        _release = release;
    }

    public BrowserInspectionScope Scope { get; }

    public void Dispose() =>
        Interlocked.Exchange(ref _release, null)?.Invoke();
}
