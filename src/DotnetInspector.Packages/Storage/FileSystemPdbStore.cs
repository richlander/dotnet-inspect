namespace DotnetInspector.Packages;

/// <summary>
/// Filesystem-backed <see cref="IPdbStore"/> that maps store-relative keys to
/// files under the symbol cache directory (<c>{app-cache}/packages/symbols</c>
/// by default). Reproduces the exact on-disk layout the desktop symbol
/// downloader has always used, so behavior is unchanged.
/// </summary>
public sealed class FileSystemPdbStore : IPdbStore
{
    private readonly string _root;

    /// <param name="root">Root directory under which keys resolve.</param>
    public FileSystemPdbStore(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        _root = root;
    }

    /// <summary>
    /// Creates a store rooted at the default symbol cache directory
    /// (<c>{NuGetCache.GetAppCachePath()}/symbols</c>).
    /// </summary>
    public static FileSystemPdbStore CreateDefault()
        => new(Path.Combine(NuGetCache.GetAppCachePath(), "symbols"));

    private string ResolvePath(string key)
        => StorePath.ResolveUnderRoot(_root, key);

    /// <inheritdoc />
    public ValueTask<Stream?> TryOpenAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path))
            return ValueTask.FromResult<Stream?>(null);

        try
        {
            return ValueTask.FromResult<Stream?>(File.OpenRead(path));
        }
        catch (IOException)
        {
            return ValueTask.FromResult<Stream?>(null);
        }
        catch (UnauthorizedAccessException)
        {
            return ValueTask.FromResult<Stream?>(null);
        }
    }

    /// <inheritdoc />
    public async ValueTask PutAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var destination = File.Create(path);
        await content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string? TryGetLocalPath(string key)
    {
        var path = ResolvePath(key);
        return File.Exists(path) ? path : null;
    }
}
