namespace ILInspector.Metadata;

internal static class OwnedResourceCleanup
{
    internal static void DisposeAfterFailure(
        IDisposable? resource,
        Exception primaryFailure)
    {
        ArgumentNullException.ThrowIfNull(primaryFailure);
        try
        {
            resource?.Dispose();
        }
        catch
        {
        }
    }
}
