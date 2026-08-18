namespace DotnetInspector.Services;

/// <summary>
/// Host-owned authorization and transport constraints for SourceLink requests.
/// </summary>
public interface ISourceFetchPolicy
{
    bool IsRequestAllowed(Uri requestUri);
    bool FinalResponseUriIsReliable { get; }
    void ConfigureRequest(HttpRequestMessage request);
}
