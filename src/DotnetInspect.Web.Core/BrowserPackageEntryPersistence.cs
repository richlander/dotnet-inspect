namespace DotnetInspect.Web;

internal interface IBrowserPackageEntryPersistence
{
    bool IsPersistent { get; }

    ValueTask<byte[]?> ReadAsync(string key);

    ValueTask PublishAsync(string key, ReadOnlyMemory<byte> content);
}

internal interface IBrowserPackageArchivePersistence : IBrowserPackageEntryPersistence
{
    ValueTask<long?> GetArchiveLengthAsync(string key);

    ValueTask<byte[]?> ReadArchiveAsync(string key, int expectedLength);
}

internal sealed class BrowserPackageEntryPersistenceException(
    string message,
    Exception innerException)
    : Exception(message, innerException);
