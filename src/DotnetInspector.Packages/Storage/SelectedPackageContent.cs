using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Packages;

/// <summary>
/// A semantic view over retained package content that exposes only selected
/// entries while preserving the underlying immutable generation identity.
/// </summary>
internal class SelectedPackageContent :
    IPackageContent,
    IPackageHousePayloadSource
{
    private readonly IPackageContent _content;
    private readonly IReadOnlyList<string> _entries;
    private readonly HashSet<string> _entrySet;

    protected SelectedPackageContent(
        IPackageContent content,
        IReadOnlyList<string> entries)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;
        _entrySet = new HashSet<string>(
            entries,
            StringComparer.OrdinalIgnoreCase);
    }

    public string? RootPath => null;

    public string? NupkgPath => null;

    public bool FromCache => _content.FromCache;

    public string ProducerKey => _content.ProducerKey;

    public PackageContentGenerationIdentity GenerationIdentity =>
        _content.GenerationIdentity;

    public bool RequiresArchiveTreeMatch => false;

    public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
    {
        stream = null;
        return false;
    }

    public bool TryOpenEntry(
        string relativePath,
        [NotNullWhen(true)] out Stream? stream)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (!_entrySet.Contains(relativePath))
        {
            stream = null;
            return false;
        }
        return _content.TryOpenEntry(relativePath, out stream);
    }

    public bool TryOpenEntry(
        string relativePath,
        long maxExpandedBytes,
        [NotNullWhen(true)] out Stream? stream)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxExpandedBytes);
        ArgumentNullException.ThrowIfNull(relativePath);
        if (!_entrySet.Contains(relativePath))
        {
            stream = null;
            return false;
        }
        return _content.TryOpenEntry(
            relativePath,
            maxExpandedBytes,
            out stream);
    }

    public IEnumerable<string> EnumerateEntries() => _entries;

    bool IPackageHousePayloadSource.TryOpenPayloadRead(
        string relativePath,
        long maxExpandedBytes,
        [NotNullWhen(true)] out Stream? stream)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(maxExpandedBytes);
        if (!_entrySet.Contains(relativePath))
        {
            stream = null;
            return false;
        }
        if (_content is not IPackageHousePayloadSource source)
        {
            throw new NotSupportedException(
                "This PackageHouse payload does not support pull-based reads.");
        }
        return source.TryOpenPayloadRead(
            relativePath,
            maxExpandedBytes,
            out stream);
    }

    internal static IPackageContent Create(
        IPackageContent content,
        IReadOnlyList<string> entries) =>
        content is IPackageContentEntryManifest manifest
            ? new WithManifest(content, manifest, entries)
            : new SelectedPackageContent(content, entries);

    private sealed class WithManifest :
        SelectedPackageContent,
        IPackageContentEntryManifest
    {
        private readonly IPackageContentEntryManifest _manifest;
        private readonly IReadOnlyList<PackageContentEntry> _manifestEntries;
        private readonly HashSet<string> _manifestEntrySet;

        internal WithManifest(
            IPackageContent content,
            IPackageContentEntryManifest manifest,
            IReadOnlyList<string> entries)
            : base(content, entries)
        {
            _manifest = manifest;
            _manifestEntrySet = new HashSet<string>(
                entries,
                StringComparer.OrdinalIgnoreCase);
            _manifestEntries =
            [
                .. entries.Select(path =>
                {
                    if (!_manifest.TryGetEntryLength(path, out long length))
                    {
                        throw new InvalidOperationException(
                            $"The selected package entry '{path}' has no manifest length.");
                    }
                    return new PackageContentEntry(path, length);
                }),
            ];
        }

        public bool TryGetEntryLength(
            string relativePath,
            out long length)
        {
            ArgumentNullException.ThrowIfNull(relativePath);
            if (!_manifestEntrySet.Contains(relativePath))
            {
                length = 0;
                return false;
            }
            return _manifest.TryGetEntryLength(relativePath, out length);
        }

        public PackageContentEntryScanner CreateEntryScanner() =>
            PackageContentEntryScanner.From(_manifestEntries);
    }
}
