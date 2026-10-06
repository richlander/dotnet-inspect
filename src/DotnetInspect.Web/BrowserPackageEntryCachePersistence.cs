using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal sealed partial class BrowserPackageEntryCachePersistence :
    IBrowserPackageEntryPersistence
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
