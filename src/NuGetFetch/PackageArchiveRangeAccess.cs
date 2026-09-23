using ZipFetch;

namespace NuGetFetch;

/// <summary>
/// Ranged access to a package archive on a source that can serve byte
/// ranges: the archive's directory and selected entries without acquiring
/// the whole archive. A client implementation that can range exposes this
/// interface beside <see cref="IPackageSourceClient"/>, which is unchanged.
/// Owned by <c>docs/design/package-archive-range-access.md</c>.
/// </summary>
public interface IPackageArchiveRangeSource
{
    /// <summary>
    /// Opens one exact coordinate's archive by reading its directory. The
    /// returned reader makes every later transfer under the same operation
    /// context, credential, and source identity, and must be disposed before
    /// the operation ends.
    /// </summary>
    Task<PackageArchiveReadResult<PackageArchiveReader>> OpenArchiveAsync(
        string packageId,
        string version,
        ZipReadLimits limits,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);
}

/// <summary>The range-specific ways a read can be refused; each is owned by the capability.</summary>
public enum PackageArchiveReadRefusal
{
    /// <summary>The source answered a ranged request with the whole archive; take the full fetch.</summary>
    RangeIgnored,

    /// <summary>The archive changed between requests (validator or total differs); take the full fetch.</summary>
    ArchiveChanged,

    /// <summary>The archive cannot be read by range at all (Zip64, or a compression method other than stored and deflate).</summary>
    ArchiveUnsupported,
}

/// <summary>
/// The outcome of one ranged read: exactly one of a value, an ordinary
/// <see cref="PackageSourceFailure"/> for the transport class of failures,
/// or a range-specific <see cref="PackageArchiveReadRefusal"/>.
/// </summary>
public sealed class PackageArchiveReadResult<T>
    where T : class
{
    internal PackageArchiveReadResult(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    internal PackageArchiveReadResult(PackageSourceFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    internal PackageArchiveReadResult(PackageArchiveReadRefusal refusal)
    {
        if (!Enum.IsDefined(refusal))
            throw new ArgumentOutOfRangeException(nameof(refusal));
        Refusal = refusal;
    }

    public T? Value { get; }

    public PackageSourceFailure? Failure { get; }

    public PackageArchiveReadRefusal? Refusal { get; }
}

/// <summary>One entry's expanded content, owned by the caller.</summary>
public sealed class PackageArchiveEntryContent
{
    internal PackageArchiveEntryContent(ZipEntry entry, byte[] content)
    {
        Entry = entry;
        Content = content;
    }

    public ZipEntry Entry { get; }

    public ReadOnlyMemory<byte> Content { get; }
}
