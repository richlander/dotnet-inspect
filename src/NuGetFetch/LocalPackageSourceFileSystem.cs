using System.Security;

namespace NuGetFetch;

[Flags]
internal enum LocalPackageSourceHostCapabilities
{
    None = 0,
    List = 1 << 0,
    Read = 1 << 1,
    Transfer = 1 << 2,
}

internal sealed record LocalPackageSourceDirectory(
    string Name,
    object Handle);

internal sealed record LocalPackageSourceFile(
    string Name,
    object Handle,
    long? ObservedLength = null,
    object? StabilityEvidence = null);

internal sealed record LocalPackageSourceDirectoryListing(
    IReadOnlyList<LocalPackageSourceDirectory> Directories,
    IReadOnlyList<LocalPackageSourceFile> Files,
    bool HasMoreEntries);

internal sealed record LocalPackageSourceOpenFile(
    Stream Content,
    long Length,
    object? StabilityEvidence = null);

internal interface ILocalPackageSourceFileSystem
{
    LocalPackageSourceHostCapabilities Capabilities { get; }

    bool TryGetDirectory(
        LocalPackageSourceIdentity source,
        out LocalPackageSourceDirectory? directory);

    LocalPackageSourceDirectoryListing List(
        LocalPackageSourceDirectory directory,
        int maximumEntries,
        NuGetOperationDeadline operation);

    LocalPackageSourceOpenFile OpenRead(
        LocalPackageSourceFile file,
        NuGetOperationDeadline operation);
}

internal sealed class UnavailableLocalPackageSourceFileSystem
    : ILocalPackageSourceFileSystem
{
    public static UnavailableLocalPackageSourceFileSystem Instance { get; } =
        new();

    public LocalPackageSourceHostCapabilities Capabilities =>
        LocalPackageSourceHostCapabilities.None;

    public bool TryGetDirectory(
        LocalPackageSourceIdentity source,
        out LocalPackageSourceDirectory? directory)
    {
        ArgumentNullException.ThrowIfNull(source);
        directory = null;
        return false;
    }

    public LocalPackageSourceDirectoryListing List(
        LocalPackageSourceDirectory directory,
        int maximumEntries,
        NuGetOperationDeadline operation) =>
        throw new NuGetSourceCapabilityUnavailableException();

    public LocalPackageSourceOpenFile OpenRead(
        LocalPackageSourceFile file,
        NuGetOperationDeadline operation) =>
        throw new NuGetSourceCapabilityUnavailableException();
}

internal sealed class PhysicalLocalPackageSourceFileSystem
    : ILocalPackageSourceFileSystem
{
    public static PhysicalLocalPackageSourceFileSystem Instance { get; } =
        new();

    public LocalPackageSourceHostCapabilities Capabilities =>
        LocalPackageSourceHostCapabilities.List
        | LocalPackageSourceHostCapabilities.Read
        | LocalPackageSourceHostCapabilities.Transfer;

    public bool TryGetDirectory(
        LocalPackageSourceIdentity source,
        out LocalPackageSourceDirectory? directory)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            if (!Directory.Exists(source.CanonicalPath))
            {
                directory = null;
                return false;
            }

            directory = new LocalPackageSourceDirectory(
                string.Empty,
                source.CanonicalPath);
            return true;
        }
        catch (Exception exception) when (IsHostAccessFailure(exception))
        {
            throw TransportFailure(exception);
        }
    }

    public LocalPackageSourceDirectoryListing List(
        LocalPackageSourceDirectory directory,
        int maximumEntries,
        NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEntries);
        ArgumentNullException.ThrowIfNull(operation);
        string path = GetPath(directory.Handle);
        var directories = new List<LocalPackageSourceDirectory>();
        var files = new List<LocalPackageSourceFile>();
        int observed = 0;

        try
        {
            foreach (string entryPath in Directory.EnumerateFileSystemEntries(
                         path))
            {
                operation.ThrowIfExpired();
                if (observed == maximumEntries)
                {
                    return new LocalPackageSourceDirectoryListing(
                        directories,
                        files,
                        HasMoreEntries: true);
                }

                string name = Path.GetFileName(entryPath);
                FileAttributes attributes = File.GetAttributes(entryPath);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(
                        new LocalPackageSourceDirectory(name, entryPath));
                }
                else
                {
                    files.Add(new LocalPackageSourceFile(name, entryPath));
                }

                observed++;
            }

            operation.ThrowIfExpired();
            return new LocalPackageSourceDirectoryListing(
                directories,
                files,
                HasMoreEntries: false);
        }
        catch (Exception exception) when (IsHostAccessFailure(exception))
        {
            throw TransportFailure(exception);
        }
    }

    public LocalPackageSourceOpenFile OpenRead(
        LocalPackageSourceFile file,
        NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(operation);
        operation.ThrowIfExpired();
        try
        {
            var stream = new FileStream(
                GetPath(file.Handle),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            try
            {
                long length = stream.Length;
                operation.ThrowIfExpired();
                return new LocalPackageSourceOpenFile(stream, length);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (IsHostAccessFailure(exception))
        {
            throw TransportFailure(exception);
        }
    }

    private static string GetPath(object handle) =>
        handle as string
        ?? throw new InvalidOperationException(
            "The physical local-source adapter received an invalid handle.");

    private static bool IsHostAccessFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException;

    private static IOException TransportFailure(Exception exception) =>
        new("The local package source could not complete a filesystem operation.",
            exception);
}
