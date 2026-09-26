using System.Runtime.Versioning;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserRetainedWorkspaceActivationRegistry
{
    static readonly object Gate = new();
    static BrowserRetainedWorkspaceActivationOwner _owner = CreateOwner();

    internal static BrowserRetainedWorkspaceActivationOwner Owner
    {
        get
        {
            lock (Gate)
                return _owner;
        }
    }

    internal static async Task ResetForTestsAsync()
    {
        BrowserRetainedWorkspaceActivationOwner prior;
        lock (Gate)
        {
            prior = _owner;
            _owner = CreateOwner();
        }
        await prior.DisposeAsync().ConfigureAwait(false);
    }

    static BrowserRetainedWorkspaceActivationOwner CreateOwner() =>
        new(BrowserCompleteRestorationOptions.Create);
}
