using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal sealed partial class BrowserPackageEntryCachePersistence :
    IBrowserPackageArchivePersistence
{
    const string ModuleName = "browser-package-entry-cache";
    const string ModulePath = "/browser-package-entry-cache.js";

    BrowserPackageEntryCachePersistence(
        bool isPersistent,
        BrowserPackageEntryPersistenceException? durabilityFailure)
    {
        IsPersistent = isPersistent;
        DurabilityFailure = durabilityFailure;
    }

    public bool IsPersistent { get; }

    internal BrowserPackageEntryPersistenceException? DurabilityFailure
        { get; }

    internal static async Task<BrowserPackageEntryCachePersistence>
        CreateAsync()
    {
        try
        {
            await JSHost.ImportAsync(ModuleName, ModulePath);
            await ConfigureAsync(BrowserPackageWorkspace.PackageEntryCacheName);
        }
        catch (JSException exception)
        {
            throw new BrowserPackageEntryPersistenceException(
                "Cache Storage initialization failed.",
                exception);
        }

        try
        {
            return new(await IsPersistentAsync(), durabilityFailure: null);
        }
        catch (JSException exception)
        {
            return new(
                isPersistent: false,
                new BrowserPackageEntryPersistenceException(
                    "The browser did not report the origin's storage durability.",
                    exception));
        }
    }

    public async ValueTask<byte[]?> ReadAsync(string key)
    {
        try
        {
            string? encoded = await ReadEncodedAsync(key);
            return encoded is null
                ? null
                : Convert.FromBase64String(encoded);
        }
        catch (JSException exception)
        {
            throw new BrowserPackageEntryPersistenceException(
                "Cache Storage rejected the read.",
                exception);
        }
        catch (FormatException exception)
        {
            throw new BrowserPackageEntryPersistenceException(
                "Cache Storage returned an invalid binary payload.",
                exception);
        }
    }

    public async ValueTask PublishAsync(
        string key,
        ReadOnlyMemory<byte> content)
    {
        try
        {
            await PublishEncodedAsync(
                key,
                Convert.ToBase64String(content.Span));
        }
        catch (JSException exception)
        {
            throw new BrowserPackageEntryPersistenceException(
                "Cache Storage rejected the publication.",
                exception);
        }
    }

    public async ValueTask<long?> GetArchiveLengthAsync(string key)
    {
        try
        {
            int length = await ArchiveLengthAsync(key);
            return length < 0 ? null : length;
        }
        catch (JSException exception)
        {
            throw new BrowserPackageEntryPersistenceException(
                "Cache Storage rejected the archive length read.", exception);
        }
    }

    public async ValueTask<byte[]?> ReadArchiveAsync(string key, int expectedLength)
    {
        try
        {
            string? encoded = await ReadArchiveEncodedAsync(key, expectedLength);
            return encoded is null ? null : Convert.FromBase64String(encoded);
        }
        catch (Exception exception) when (exception is JSException or FormatException)
        {
            throw new BrowserPackageEntryPersistenceException(
                "Cache Storage rejected the bounded archive read.", exception);
        }
    }

    [JSImport("archiveLength", ModuleName)]
    private static partial Task<int> ArchiveLengthAsync(string key);

    [JSImport("readArchive", ModuleName)]
    private static partial Task<string?> ReadArchiveEncodedAsync(string key, int expectedLength);

    [JSImport("configure", ModuleName)]
    private static partial Task ConfigureAsync(string cacheName);

    [JSImport("isPersistent", ModuleName)]
    private static partial Task<bool> IsPersistentAsync();

    [JSImport("read", ModuleName)]
    private static partial Task<string?> ReadEncodedAsync(string key);

    [JSImport("publish", ModuleName)]
    private static partial Task PublishEncodedAsync(
        string key,
        string content);
}
