namespace DotnetInspector.Services;

/// <summary>
/// Host-owned initial-request authorization and transport constraints for
/// source requests.
/// </summary>
public interface ISourceFetchPolicy
{
    bool IsRequestAllowed(Uri requestUri);
    void ConfigureRequest(HttpRequestMessage request);
}
