namespace DotnetInspect.Web;

internal interface IBrowserPackageEntryPersistence
{
    bool IsPersistent { get; }

    ValueTask<byte[]?> ReadAsync(string key);

    ValueTask PublishAsync(string key, ReadOnlyMemory<byte> content);
}

internal sealed class BrowserPackageEntryPersistenceException(
    string message,
    Exception innerException)
    : Exception(message, innerException);
