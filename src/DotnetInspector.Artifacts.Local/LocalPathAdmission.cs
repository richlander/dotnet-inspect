using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DotnetInspector.Artifacts.Local;

internal enum LocalPathKind
{
    RegularFile,
    Directory,
}

internal enum LocalPathOutcome
{
    Classified,
    Unavailable,
    Rejected,
    Failed,
}

internal enum LocalPathReason
{
    InvalidPath,
    KindMismatch,
    UnsupportedEntry,
    AdmissionFailed,
    ClassificationUnsupported,
}

internal readonly record struct LocalPathClassification(
    LocalPathOutcome Outcome,
    LocalPathReason? Reason,
    string RequestedPath,
    string? CanonicalPath,
    LocalPathKind? Kind)
{
    internal static LocalPathClassification Classified(
        string requestedPath,
        string canonicalPath,
        LocalPathKind kind) =>
        new(
            LocalPathOutcome.Classified,
            Reason: null,
            requestedPath,
            canonicalPath,
            kind);

    internal static LocalPathClassification Unavailable(
        string requestedPath,
        string? canonicalPath) =>
        new(
            LocalPathOutcome.Unavailable,
            Reason: null,
            requestedPath,
            canonicalPath,
            Kind: null);

    internal static LocalPathClassification Rejected(
        LocalPathReason reason,
        string requestedPath,
        string? canonicalPath) =>
        new(
            LocalPathOutcome.Rejected,
            reason,
            requestedPath,
            canonicalPath,
            Kind: null);

    internal static LocalPathClassification Failed(
        LocalPathReason reason,
        string requestedPath,
        string? canonicalPath) =>
        new(
            LocalPathOutcome.Failed,
            reason,
            requestedPath,
            canonicalPath,
            Kind: null);
}

internal sealed class LocalFileAdmission : IAsyncDisposable
{
    internal LocalFileAdmission(
        LocalPathClassification classification,
        FileStream? stream = null)
    {
        Classification = classification;
        Stream = stream;
    }

    internal LocalPathClassification Classification { get; }
    internal FileStream? Stream { get; }

    public ValueTask DisposeAsync() =>
        Stream is null ? ValueTask.CompletedTask : Stream.DisposeAsync();
}

internal enum WindowsReparseDisposition
{
    SupportedLink,
    DataBearing,
    Unsupported,
}

internal enum WindowsPathSyntaxDisposition
{
    Supported,
    Invalid,
    Unsupported,
}

internal enum WindowsKnownReparseTag : uint
{
    ReservedZero = 0x00000000,
    ReservedOne = 0x00000001,
    ReservedTwo = 0x00000002,
    MountPoint = 0xA0000003,
    Hsm = 0xC0000004,
    DriveExtender = 0x80000005,
    Hsm2 = 0x80000006,
    Sis = 0x80000007,
    Wim = 0x80000008,
    Csv = 0x80000009,
    Dfs = 0x8000000A,
    FilterManager = 0x8000000B,
    SymbolicLink = 0xA000000C,
    IisCache = 0xA0000010,
    Dfsr = 0x80000012,
    Dedup = 0x80000013,
    AppxStream = 0xC0000014,
    Nfs = 0x80000014,
    FilePlaceholder = 0x80000015,
    Dfm = 0x80000016,
    Wof = 0x80000017,
    Wci = 0x80000018,
    Wci1 = 0x90001018,
    GlobalReparse = 0xA0000019,
    Cloud = 0x9000001A,
    Cloud1 = 0x9000101A,
    Cloud2 = 0x9000201A,
    Cloud3 = 0x9000301A,
    Cloud4 = 0x9000401A,
    Cloud5 = 0x9000501A,
    Cloud6 = 0x9000601A,
    Cloud7 = 0x9000701A,
    Cloud8 = 0x9000801A,
    Cloud9 = 0x9000901A,
    CloudA = 0x9000A01A,
    CloudB = 0x9000B01A,
    CloudC = 0x9000C01A,
    CloudD = 0x9000D01A,
    CloudE = 0x9000E01A,
    CloudF = 0x9000F01A,
    AppExecutionLink = 0x8000001B,
    ProjFs = 0x9000001C,
    LxSymbolicLink = 0xA000001D,
    StorageSync = 0x8000001E,
    StorageSyncFolder = 0x90000027,
    WciTombstone = 0xA000001F,
    Unhandled = 0x80000020,
    OneDrive = 0x80000021,
    ProjFsTombstone = 0xA0000022,
    AfUnix = 0x80000023,
    LxFifo = 0x80000024,
    LxCharacter = 0x80000025,
    LxBlock = 0x80000026,
    WciLink = 0xA0000027,
    WciLink1 = 0xA0001027,
}

internal readonly record struct WindowsReparseTagEntry(
    WindowsKnownReparseTag Tag,
    WindowsReparseDisposition Disposition);

internal readonly record struct WindowsReparseInformation(
    WindowsReparseDisposition Disposition,
    string? TargetPath,
    bool IsRelative);

internal static partial class LocalPathAdmission
{
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixDirectory = 0x4000;
    private const int UnixRegularFile = 0x8000;
    private const int UnixErrorNoEntry = 2;
    private const int UnixErrorNotDirectory = 20;
    private const int BrowserErrorNoEntry = 44;
    private const int BrowserErrorNotDirectory = 54;
    private const int BrowserErrorSymbolicLinkLoop = 32;

    private const uint WindowsFileTypeUnknown = 0x0000;
    private const uint WindowsFileTypeDisk = 0x0001;
    private const uint WindowsOpenExisting = 3;
    private const uint WindowsFileFlagBackupSemantics = 0x02000000;
    private const uint WindowsFileFlagOpenReparsePoint = 0x00200000;
    private const uint WindowsFsctlGetReparsePoint = 0x000900A8;
    private const uint WindowsSymbolicLinkFlagRelative = 1;
    private const int WindowsMaximumReparseDataBufferSize = 16 * 1024;
    private const int MaximumWindowsLinkDepth = 40;
    private static readonly WindowsReparseTagEntry[] s_windowsReparseTags =
    [
        new(WindowsKnownReparseTag.ReservedZero, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.ReservedOne, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.ReservedTwo, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.MountPoint, WindowsReparseDisposition.SupportedLink),
        new(WindowsKnownReparseTag.Hsm, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.DriveExtender, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Hsm2, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Sis, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Wim, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Csv, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Dfs, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.FilterManager, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.SymbolicLink, WindowsReparseDisposition.SupportedLink),
        new(WindowsKnownReparseTag.IisCache, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.Dfsr, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.Dedup, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.AppxStream, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.Nfs, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.FilePlaceholder, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Dfm, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Wof, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Wci, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Wci1, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.GlobalReparse, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.Cloud, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Cloud1, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Cloud2, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Cloud3, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Cloud4, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Cloud5, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Cloud6, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Cloud7, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Cloud8, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.Cloud9, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.CloudA, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.CloudB, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.CloudC, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.CloudD, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.CloudE, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.CloudF, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.AppExecutionLink, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.ProjFs, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.LxSymbolicLink, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.StorageSync, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.StorageSyncFolder, WindowsReparseDisposition.DataBearing),
        new(WindowsKnownReparseTag.WciTombstone, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.Unhandled, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.OneDrive, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.ProjFsTombstone, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.AfUnix, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.LxFifo, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.LxCharacter, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.LxBlock, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.WciLink, WindowsReparseDisposition.Unsupported),
        new(WindowsKnownReparseTag.WciLink1, WindowsReparseDisposition.Unsupported),
    ];

    internal static IReadOnlyList<WindowsReparseTagEntry> WindowsReparseTags =>
        s_windowsReparseTags;

    internal static LocalPathClassification Classify(
        string requestedPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryCanonicalize(
            requestedPath,
            out string? canonicalPath))
        {
            return LocalPathClassification.Rejected(
                LocalPathReason.InvalidPath,
                requestedPath,
                canonicalPath: null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return OperatingSystem.IsWindows()
            ? ClassifyWindows(
                requestedPath,
                canonicalPath!,
                cancellationToken)
            : ClassifyUnix(requestedPath, canonicalPath!);
    }

    internal static LocalPathClassification AdmitDirectory(
        string requestedPath,
        CancellationToken cancellationToken = default) =>
        RequireKind(
            Classify(requestedPath, cancellationToken),
            LocalPathKind.Directory);

    internal static LocalFileAdmission AdmitRegularFile(
        string requestedPath,
        CancellationToken cancellationToken = default)
    {
        LocalPathClassification classification = RequireKind(
            Classify(requestedPath, cancellationToken),
            LocalPathKind.RegularFile);
        return AdmitRegularFile(classification, cancellationToken);
    }

    internal static LocalFileAdmission AdmitRegularFile(
        LocalPathClassification classification,
        CancellationToken cancellationToken = default)
    {
        if (classification.Outcome == LocalPathOutcome.Classified
            && classification.Kind != LocalPathKind.RegularFile)
        {
            throw new ArgumentException(
                "A classified admission must identify a regular file.",
                nameof(classification));
        }

        if (classification.Outcome != LocalPathOutcome.Classified)
            return new LocalFileAdmission(classification);

        FileStream? stream = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            stream = new FileStream(
                classification.CanonicalPath!,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read | FileShare.Delete,
                    BufferSize = 81920,
                    Options = FileOptions.Asynchronous
                        | FileOptions.SequentialScan,
                });

            LocalPathClassification verified = VerifyRegularFileHandle(
                classification,
                stream.SafeFileHandle);
            if (verified.Outcome != LocalPathOutcome.Classified)
            {
                stream.Dispose();
                stream = null;
                return new LocalFileAdmission(verified);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new LocalFileAdmission(verified, stream);
        }
        catch (OperationCanceledException)
        {
            stream?.Dispose();
            throw;
        }
        catch (Exception ex) when (IsMissing(ex))
        {
            stream?.Dispose();
            return new LocalFileAdmission(
                LocalPathClassification.Unavailable(
                    classification.RequestedPath,
                    classification.CanonicalPath));
        }
        catch (Exception ex) when (IsClassificationUnsupported(ex))
        {
            stream?.Dispose();
            return new LocalFileAdmission(
                LocalPathClassification.Failed(
                    LocalPathReason.ClassificationUnsupported,
                    classification.RequestedPath,
                    classification.CanonicalPath));
        }
        catch (Exception ex) when (IsAdmissionFailure(ex))
        {
            stream?.Dispose();
            return new LocalFileAdmission(
                LocalPathClassification.Failed(
                    LocalPathReason.AdmissionFailed,
                    classification.RequestedPath,
                    classification.CanonicalPath));
        }
    }

    internal static WindowsReparseDisposition ClassifyWindowsReparseTag(
        uint tag)
    {
        foreach (WindowsReparseTagEntry entry in s_windowsReparseTags)
        {
            if ((uint)entry.Tag == tag)
                return entry.Disposition;
        }

        return WindowsReparseDisposition.Unsupported;
    }

    internal static bool IsSupportedWindowsPathSyntax(string path)
        => ClassifyWindowsPathSyntax(path)
            == WindowsPathSyntaxDisposition.Supported;

    internal static WindowsPathSyntaxDisposition ClassifyWindowsPathSyntax(
        string path)
    {
        if (path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
        {
            return WindowsPathSyntaxDisposition.Unsupported;
        }

        if (IsWindowsNamespacePath(path)
            && !path.StartsWith(
                @"\\?\",
                StringComparison.OrdinalIgnoreCase))
        {
            return WindowsPathSyntaxDisposition.Invalid;
        }

        int firstComponent;
        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Contains('/'))
                return WindowsPathSyntaxDisposition.Invalid;

            if (path.StartsWith(
                @"\\?\UNC\",
                StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetUncContentStart(path, 8, out firstComponent))
                    return WindowsPathSyntaxDisposition.Invalid;
                if (IsUncPipeShare(path, 8))
                    return WindowsPathSyntaxDisposition.Unsupported;
                if (ContainsDotSegment(path, 8))
                    return WindowsPathSyntaxDisposition.Invalid;
            }
            else if (path.Length >= 7
                && char.IsAsciiLetter(path[4])
                && path[5] == ':'
                && IsWindowsDirectorySeparator(path[6]))
            {
                firstComponent = 7;
            }
            else
            {
                return path.StartsWith(
                    @"\\?\pipe\",
                    StringComparison.OrdinalIgnoreCase)
                    ? WindowsPathSyntaxDisposition.Unsupported
                    : WindowsPathSyntaxDisposition.Invalid;
            }

            if (!path.StartsWith(
                    @"\\?\UNC\",
                    StringComparison.OrdinalIgnoreCase)
                && ContainsDotSegment(path, firstComponent))
            {
                return WindowsPathSyntaxDisposition.Invalid;
            }
        }
        else if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            if (!TryGetUncContentStart(path, 2, out firstComponent))
                return WindowsPathSyntaxDisposition.Invalid;
            if (IsUncPipeShare(path, 2))
                return WindowsPathSyntaxDisposition.Unsupported;
        }
        else if (path.Length >= 3
            && char.IsAsciiLetter(path[0])
            && path[1] == ':'
            && IsWindowsDirectorySeparator(path[2]))
        {
            firstComponent = 3;
        }
        else
        {
            return WindowsPathSyntaxDisposition.Invalid;
        }

        return ContainsReservedDosName(path, firstComponent)
            ? WindowsPathSyntaxDisposition.Unsupported
            : WindowsPathSyntaxDisposition.Supported;
    }

    internal static WindowsPathSyntaxDisposition
        ClassifyResolvedWindowsPathSyntax(string path)
    {
        if (!path.StartsWith(
            @"\\?\Volume{",
            StringComparison.OrdinalIgnoreCase))
        {
            return ClassifyWindowsPathSyntax(path);
        }

        int volumeEnd = path.IndexOf(@"}\", StringComparison.Ordinal);
        if (volumeEnd <= 11
            || !Guid.TryParseExact(
                path.AsSpan(11, volumeEnd - 11),
                "D",
                out _)
            || path.Contains('/'))
        {
            return WindowsPathSyntaxDisposition.Invalid;
        }

        return ContainsDotSegment(path, volumeEnd + 2)
            ? WindowsPathSyntaxDisposition.Invalid
            : WindowsPathSyntaxDisposition.Supported;
    }

    private static bool TryCanonicalize(
        string requestedPath,
        out string? canonicalPath)
    {
        try
        {
            if (OperatingSystem.IsWindows()
                && IsWindowsNamespacePath(requestedPath))
            {
                canonicalPath = requestedPath;
                return ClassifyWindowsPathSyntax(canonicalPath)
                    != WindowsPathSyntaxDisposition.Invalid;
            }

            canonicalPath = Path.GetFullPath(requestedPath);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            canonicalPath = null;
            return false;
        }
    }

    private static bool IsWindowsNamespacePath(string path) =>
        path.Length >= 4
        && IsWindowsDirectorySeparator(path[0])
        && IsWindowsDirectorySeparator(path[3])
        && (IsWindowsDirectorySeparator(path[1])
            && path[2] is '.' or '?'
            || path[1] == '?'
            && path[2] == '?');

    private static LocalPathClassification RequireKind(
        LocalPathClassification classification,
        LocalPathKind requiredKind)
    {
        if (classification.Outcome != LocalPathOutcome.Classified
            || classification.Kind == requiredKind)
        {
            return classification;
        }

        return LocalPathClassification.Rejected(
            LocalPathReason.KindMismatch,
            classification.RequestedPath,
            classification.CanonicalPath);
    }

    private static LocalPathClassification ClassifyUnix(
        string requestedPath,
        string canonicalPath)
    {
        try
        {
            if (UnixStat(canonicalPath, out UnixFileStatus information) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (IsUnixMissing(error))
                {
                    return LocalPathClassification.Unavailable(
                        requestedPath,
                        canonicalPath);
                }

                if (IsUnixSymbolicLinkLoop(error))
                {
                    return LocalPathClassification.Rejected(
                        LocalPathReason.UnsupportedEntry,
                        requestedPath,
                        canonicalPath);
                }

                return LocalPathClassification.Failed(
                    LocalPathReason.AdmissionFailed,
                    requestedPath,
                    canonicalPath);
            }

            return FromUnixMode(
                information.Mode,
                requestedPath,
                canonicalPath);
        }
        catch (Exception ex) when (IsClassificationUnsupported(ex))
        {
            return LocalPathClassification.Failed(
                LocalPathReason.ClassificationUnsupported,
                requestedPath,
                canonicalPath);
        }
    }

    private static LocalPathClassification FromUnixMode(
        int mode,
        string requestedPath,
        string canonicalPath) =>
        (mode & UnixFileTypeMask) switch
        {
            UnixRegularFile => LocalPathClassification.Classified(
                requestedPath,
                canonicalPath,
                LocalPathKind.RegularFile),
            UnixDirectory => LocalPathClassification.Classified(
                requestedPath,
                canonicalPath,
                LocalPathKind.Directory),
            _ => LocalPathClassification.Rejected(
                LocalPathReason.UnsupportedEntry,
                requestedPath,
                canonicalPath),
        };

    private static LocalPathClassification ClassifyWindows(
        string requestedPath,
        string canonicalPath,
        CancellationToken cancellationToken)
    {
        try
        {
            return ClassifyWindowsTarget(
                requestedPath,
                canonicalPath,
                canonicalPath,
                isCoordinate: true,
                remainingLinkDepth: MaximumWindowsLinkDepth,
                cancellationToken);
        }
        catch (Exception ex) when (IsMissing(ex))
        {
            return LocalPathClassification.Unavailable(
                requestedPath,
                canonicalPath);
        }
        catch (Exception ex) when (IsClassificationUnsupported(ex))
        {
            return LocalPathClassification.Failed(
                LocalPathReason.ClassificationUnsupported,
                requestedPath,
                canonicalPath);
        }
        catch (Exception ex) when (IsAdmissionFailure(ex))
        {
            return LocalPathClassification.Failed(
                LocalPathReason.AdmissionFailed,
                requestedPath,
                canonicalPath);
        }
    }

    private static LocalPathClassification ClassifyWindowsTarget(
        string requestedPath,
        string canonicalPath,
        string inspectedPath,
        bool isCoordinate,
        int remainingLinkDepth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WindowsPathSyntaxDisposition syntax =
            isCoordinate
                ? ClassifyWindowsPathSyntax(inspectedPath)
                : ClassifyResolvedWindowsPathSyntax(inspectedPath);
        if (syntax != WindowsPathSyntaxDisposition.Supported)
        {
            bool invalidCoordinate =
                isCoordinate
                && syntax == WindowsPathSyntaxDisposition.Invalid;
            return LocalPathClassification.Rejected(
                invalidCoordinate
                    ? LocalPathReason.InvalidPath
                    : LocalPathReason.UnsupportedEntry,
                requestedPath,
                invalidCoordinate
                    ? null
                    : canonicalPath);
        }

        if (!TryGetWindowsContentStart(inspectedPath, out int contentStart))
        {
            return LocalPathClassification.Rejected(
                LocalPathReason.UnsupportedEntry,
                requestedPath,
                canonicalPath);
        }

        FileAttributes attributes;
        int componentStart = contentStart;
        while (componentStart < inspectedPath.Length)
        {
            while (componentStart < inspectedPath.Length
                && IsWindowsDirectorySeparator(inspectedPath[componentStart]))
            {
                componentStart++;
            }

            if (componentStart >= inspectedPath.Length)
                break;

            int componentEnd = componentStart;
            while (componentEnd < inspectedPath.Length
                && !IsWindowsDirectorySeparator(inspectedPath[componentEnd]))
            {
                componentEnd++;
            }

            string componentPath = inspectedPath[..componentEnd];
            attributes = File.GetAttributes(componentPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                WindowsReparseInformation reparse =
                    GetWindowsReparseInformation(componentPath);
                if (reparse.Disposition
                    == WindowsReparseDisposition.Unsupported)
                {
                    return LocalPathClassification.Rejected(
                        LocalPathReason.UnsupportedEntry,
                        requestedPath,
                        canonicalPath);
                }

                if (reparse.Disposition
                    == WindowsReparseDisposition.SupportedLink)
                {
                    if (remainingLinkDepth == 0
                        || reparse.TargetPath is null)
                    {
                        return LocalPathClassification.Rejected(
                            LocalPathReason.UnsupportedEntry,
                            requestedPath,
                            canonicalPath);
                    }

                    string targetPath = reparse.TargetPath;
                    if (reparse.IsRelative)
                    {
                        string? parent =
                            Path.GetDirectoryName(componentPath);
                        if (parent is null)
                        {
                            return LocalPathClassification.Rejected(
                                LocalPathReason.UnsupportedEntry,
                                requestedPath,
                                canonicalPath);
                        }

                        targetPath =
                            NormalizeRelativeResolvedWindowsLinkTarget(
                                Path.Join(parent, targetPath));
                    }

                    int remainderStart = componentEnd;
                    while (remainderStart < inspectedPath.Length
                        && IsWindowsDirectorySeparator(
                            inspectedPath[remainderStart]))
                    {
                        remainderStart++;
                    }

                    ReadOnlySpan<char> remainder =
                        inspectedPath.AsSpan(remainderStart);
                    if (!remainder.IsEmpty)
                        targetPath = Path.Join(targetPath, remainder);

                    if (string.Equals(
                        targetPath,
                        inspectedPath,
                        StringComparison.Ordinal))
                    {
                        return LocalPathClassification.Rejected(
                            LocalPathReason.UnsupportedEntry,
                            requestedPath,
                            canonicalPath);
                    }

                    return ClassifyWindowsTarget(
                        requestedPath,
                        canonicalPath,
                        targetPath,
                        isCoordinate: false,
                        remainingLinkDepth - 1,
                        cancellationToken);
                }
            }

            componentStart = componentEnd + 1;
        }

        attributes = File.GetAttributes(inspectedPath);

        return LocalPathClassification.Classified(
            requestedPath,
            canonicalPath,
            (attributes & FileAttributes.Directory) != 0
                ? LocalPathKind.Directory
                : LocalPathKind.RegularFile);
    }

    private static unsafe WindowsReparseInformation
        GetWindowsReparseInformation(
        string path)
    {
        string nativePath = ToExtendedWindowsPath(path);
        using SafeFileHandle handle = CreateFileWindows(
            nativePath,
            desiredAccess: 0,
            FileShare.ReadWrite | FileShare.Delete,
            securityAttributes: IntPtr.Zero,
            WindowsOpenExisting,
            WindowsFileFlagBackupSemantics
                | WindowsFileFlagOpenReparsePoint,
            templateFile: IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error is 2 or 3)
                throw new FileNotFoundException();

            throw new IOException($"CreateFileW failed with error {error}");
        }

        if (!GetFileInformationByHandleEx(
            handle,
            FileInfoByHandleClass.FileAttributeTagInfo,
            out WindowsFileAttributeTagInformation information,
            (uint)Marshal.SizeOf<WindowsFileAttributeTagInformation>()))
        {
            throw new IOException(
                $"GetFileInformationByHandleEx failed with error " +
                $"{Marshal.GetLastPInvokeError()}");
        }

        WindowsReparseDisposition disposition =
            ClassifyWindowsReparseTag(information.ReparseTag);
        if (disposition != WindowsReparseDisposition.SupportedLink)
            return new(disposition, TargetPath: null, IsRelative: false);

        Span<byte> buffer =
            stackalloc byte[WindowsMaximumReparseDataBufferSize];
        uint bytesReturned;
        fixed (byte* bufferPointer = buffer)
        {
            if (!DeviceIoControl(
                handle,
                WindowsFsctlGetReparsePoint,
                inputBuffer: null,
                inputBufferSize: 0,
                outputBuffer: bufferPointer,
                outputBufferSize: (uint)buffer.Length,
                out bytesReturned,
                overlapped: IntPtr.Zero))
            {
                throw new IOException(
                    $"DeviceIoControl failed with error " +
                    $"{Marshal.GetLastPInvokeError()}");
            }
        }

        if (bytesReturned > buffer.Length)
        {
            return new(
                WindowsReparseDisposition.Unsupported,
                TargetPath: null,
                IsRelative: false);
        }

        ReadOnlySpan<byte> returned = buffer[..(int)bytesReturned];
        if (information.ReparseTag
            == (uint)WindowsKnownReparseTag.SymbolicLink)
        {
            if (!MemoryMarshal.TryRead(
                    returned,
                    out WindowsSymbolicLinkReparseBuffer symbolicLink)
                || symbolicLink.ReparseTag != information.ReparseTag
                || symbolicLink.Reserved != 0
                || !TryGetWindowsSymbolicLinkRelativeFlag(
                    symbolicLink.Flags,
                    out bool isRelative)
                || !TryReadWindowsReparseTarget(
                    returned,
                    Marshal.SizeOf<WindowsSymbolicLinkReparseBuffer>(),
                    symbolicLink.ReparseDataLength,
                    symbolicLink.SubstituteNameOffset,
                    symbolicLink.SubstituteNameLength,
                    symbolicLink.PrintNameOffset,
                    symbolicLink.PrintNameLength,
                    out string targetPath))
            {
                return new(
                    WindowsReparseDisposition.Unsupported,
                    TargetPath: null,
                    IsRelative: false);
            }

            return new(
                disposition,
                isRelative
                    ? targetPath
                    : ProjectWindowsAbsoluteReparseTarget(targetPath),
                isRelative);
        }

        if (!MemoryMarshal.TryRead(
                returned,
                out WindowsMountPointReparseBuffer mountPoint)
            || mountPoint.ReparseTag != information.ReparseTag
                || mountPoint.Reserved != 0
                || !TryReadWindowsReparseTarget(
                    returned,
                    Marshal.SizeOf<WindowsMountPointReparseBuffer>(),
                    mountPoint.ReparseDataLength,
                    mountPoint.SubstituteNameOffset,
                    mountPoint.SubstituteNameLength,
                    mountPoint.PrintNameOffset,
                    mountPoint.PrintNameLength,
                    out string mountTarget))
        {
            return new(
                WindowsReparseDisposition.Unsupported,
                TargetPath: null,
                IsRelative: false);
        }

        return new(
            disposition,
            ProjectWindowsAbsoluteReparseTarget(mountTarget),
            IsRelative: false);
    }

    internal static bool TryGetWindowsSymbolicLinkRelativeFlag(
        uint flags,
        out bool isRelative)
    {
        isRelative = flags == WindowsSymbolicLinkFlagRelative;
        return flags is 0 or WindowsSymbolicLinkFlagRelative;
    }

    internal static bool TryReadWindowsReparseTarget(
        ReadOnlySpan<byte> buffer,
        int pathBufferOffset,
        ushort reparseDataLength,
        ushort targetOffset,
        ushort targetLength,
        ushort printNameOffset,
        ushort printNameLength,
        out string target)
    {
        int payloadEnd = sizeof(uint) + sizeof(ushort) + sizeof(ushort)
            + reparseDataLength;
        if ((reparseDataLength & 1) != 0
            || payloadEnd != buffer.Length
            || pathBufferOffset > payloadEnd
            || !TryGetWindowsReparseNameRange(
                pathBufferOffset,
                payloadEnd,
                targetOffset,
                targetLength,
                requireContent: true,
                out int targetStart)
            || !TryGetWindowsReparseNameRange(
                pathBufferOffset,
                payloadEnd,
                printNameOffset,
                printNameLength,
                requireContent: false,
                out _))
        {
            target = string.Empty;
            return false;
        }

        target = MemoryMarshal.Cast<byte, char>(
            buffer.Slice(targetStart, targetLength)).ToString();
        return true;
    }

    private static bool TryGetWindowsReparseNameRange(
        int pathBufferOffset,
        int payloadEnd,
        ushort nameOffset,
        ushort nameLength,
        bool requireContent,
        out int start)
    {
        start = pathBufferOffset + nameOffset;
        return (!requireContent || nameLength != 0)
            && (nameOffset & 1) == 0
            && (nameLength & 1) == 0
            && start <= payloadEnd
            && nameLength <= payloadEnd - start;
    }

    private static string ProjectWindowsAbsoluteReparseTarget(
        string target)
    {
        const string ntPrefix = @"\??\";
        if (!target.StartsWith(
            ntPrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            return target;
        }

        return @"\\?\" + target[ntPrefix.Length..];
    }

    private static LocalPathClassification VerifyRegularFileHandle(
        LocalPathClassification classification,
        SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows())
            return VerifyWindowsRegularFileHandle(classification, handle);

        try
        {
            if (UnixFStat(handle, out UnixFileStatus information) != 0)
            {
                return LocalPathClassification.Failed(
                    LocalPathReason.AdmissionFailed,
                    classification.RequestedPath,
                    classification.CanonicalPath);
            }

            return (information.Mode & UnixFileTypeMask) == UnixRegularFile
                ? classification
                : LocalPathClassification.Rejected(
                    LocalPathReason.KindMismatch,
                    classification.RequestedPath,
                    classification.CanonicalPath);
        }
        catch (Exception ex) when (IsClassificationUnsupported(ex))
        {
            return LocalPathClassification.Failed(
                LocalPathReason.ClassificationUnsupported,
                classification.RequestedPath,
                classification.CanonicalPath);
        }
    }

    internal static LocalPathClassification VerifyWindowsRegularFileHandle(
        LocalPathClassification classification,
        SafeFileHandle handle)
    {
        uint fileType = GetFileType(handle);
        if (fileType == WindowsFileTypeUnknown
            && Marshal.GetLastPInvokeError() != 0)
        {
            return LocalPathClassification.Failed(
                LocalPathReason.AdmissionFailed,
                classification.RequestedPath,
                classification.CanonicalPath);
        }

        if (fileType != WindowsFileTypeDisk
            || (File.GetAttributes(handle) & FileAttributes.Directory) != 0)
        {
            return LocalPathClassification.Rejected(
                LocalPathReason.KindMismatch,
                classification.RequestedPath,
                classification.CanonicalPath);
        }

        return classification;
    }

    private static bool IsMissing(Exception exception) =>
        exception is FileNotFoundException or DirectoryNotFoundException;

    internal static bool IsUnixMissing(int error) =>
        OperatingSystem.IsBrowser()
            ? error is BrowserErrorNoEntry or BrowserErrorNotDirectory
            : error is UnixErrorNoEntry or UnixErrorNotDirectory;

    internal static bool IsUnixSymbolicLinkLoop(int error)
    {
        if (OperatingSystem.IsBrowser())
            return error == BrowserErrorSymbolicLinkLoop;

        return OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD()
            ? error == 62
            : error == 40;
    }

    private static bool IsClassificationUnsupported(Exception exception) =>
        exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException;

    private static bool IsAdmissionFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException;

    private static bool TryGetUncContentStart(
        string path,
        int serverStart,
        out int contentStart)
    {
        int serverEnd = path.IndexOfAny(['\\', '/'], serverStart);
        if (serverEnd <= serverStart)
        {
            contentStart = 0;
            return false;
        }

        int shareStart = serverEnd + 1;
        int shareEnd = path.IndexOfAny(['\\', '/'], shareStart);
        if (shareEnd < 0)
        {
            contentStart = path.Length;
            return shareStart < path.Length;
        }

        contentStart = shareEnd + 1;
        return shareEnd > shareStart;
    }

    private static bool IsUncPipeShare(string path, int serverStart)
    {
        int serverEnd = path.IndexOfAny(['\\', '/'], serverStart);
        if (serverEnd < 0)
            return false;

        int shareStart = serverEnd + 1;
        int shareEnd = path.IndexOfAny(['\\', '/'], shareStart);
        ReadOnlySpan<char> share = shareEnd < 0
            ? path.AsSpan(shareStart)
            : path.AsSpan(shareStart, shareEnd - shareStart);
        return share.Equals("pipe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsDotSegment(string path, int start)
    {
        foreach (string componentText in EnumerateWindowsComponents(
            path,
            start))
        {
            ReadOnlySpan<char> component = componentText;
            if (component.SequenceEqual(".") || component.SequenceEqual(".."))
                return true;
        }

        return false;
    }

    private static bool TryGetWindowsContentStart(
        string path,
        out int contentStart)
    {
        if (path.StartsWith(
            @"\\?\Volume{",
            StringComparison.OrdinalIgnoreCase))
        {
            int volumeEnd = path.IndexOf(@"}\", StringComparison.Ordinal);
            if (volumeEnd <= 11)
            {
                contentStart = 0;
                return false;
            }

            contentStart = volumeEnd + 2;
            return true;
        }

        if (path.StartsWith(
            @"\\?\UNC\",
            StringComparison.OrdinalIgnoreCase))
        {
            return TryGetUncContentStart(path, 8, out contentStart);
        }

        if (path.Length >= 7
            && path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
            && char.IsAsciiLetter(path[4])
            && path[5] == ':'
            && IsWindowsDirectorySeparator(path[6]))
        {
            contentStart = 7;
            return true;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return TryGetUncContentStart(path, 2, out contentStart);

        if (path.Length >= 3
            && char.IsAsciiLetter(path[0])
            && path[1] == ':'
            && IsWindowsDirectorySeparator(path[2]))
        {
            contentStart = 3;
            return true;
        }

        contentStart = 0;
        return false;
    }

    internal static string NormalizeRelativeResolvedWindowsLinkTarget(
        string path)
    {
        if (!TryGetWindowsContentStart(path, out int contentStart))
            return path;

        if (!ContainsDotSegment(path, contentStart))
            return path;

        var components = new List<string>();
        foreach (string component in EnumerateWindowsComponents(
            path,
            contentStart))
        {
            if (component == ".")
                continue;

            if (component == "..")
            {
                if (components.Count != 0)
                    components.RemoveAt(components.Count - 1);
                continue;
            }

            components.Add(component);
        }

        string normalized =
            path[..contentStart] + string.Join('\\', components);
        if (components.Count != 0
            && IsWindowsDirectorySeparator(path[^1]))
        {
            normalized += '\\';
        }

        return normalized;
    }

    private static bool ContainsReservedDosName(string path, int start)
    {
        foreach (string componentText in EnumerateWindowsComponents(
            path,
            start))
        {
            ReadOnlySpan<char> component = componentText;
            int normalizedLength = component.Length;
            while (normalizedLength > 0
                && component[normalizedLength - 1] is ' ' or '.')
            {
                normalizedLength--;
            }

            ReadOnlySpan<char> normalized = component[..normalizedLength];
            int suffix = normalized.IndexOfAny('.', ':');
            if (suffix >= 0)
                normalized = normalized[..suffix];

            if (normalized.Equals("CON", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("AUX", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(
                    "CLOCK$",
                    StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(
                    "CONIN$",
                    StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(
                    "CONOUT$",
                    StringComparison.OrdinalIgnoreCase)
                || IsNumberedDosDevice(normalized, "COM")
                || IsNumberedDosDevice(normalized, "LPT"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNumberedDosDevice(
        ReadOnlySpan<char> name,
        ReadOnlySpan<char> prefix)
    {
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || name.Length != prefix.Length + 1)
        {
            return false;
        }

        return name[^1] is >= '1' and <= '9' or '¹' or '²' or '³';
    }

    private static IEnumerable<string>
        EnumerateWindowsComponents(string path, int start)
    {
        while (start < path.Length)
        {
            while (start < path.Length
                && IsWindowsDirectorySeparator(path[start]))
            {
                start++;
            }

            if (start >= path.Length)
                yield break;

            int end = start;
            while (end < path.Length
                && !IsWindowsDirectorySeparator(path[end]))
            {
                end++;
            }

            yield return path[start..end];
            start = end + 1;
        }
    }

    private static bool IsWindowsDirectorySeparator(char value) =>
        value is '\\' or '/';

    private static string ToExtendedWindowsPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            return path;

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + path[2..];

        return @"\\?\" + path;
    }

    [LibraryImport(
        "libSystem.Native",
        EntryPoint = "SystemNative_Stat",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnixStat(
        string path,
        out UnixFileStatus information);

    [LibraryImport(
        "libSystem.Native",
        EntryPoint = "SystemNative_FStat",
        SetLastError = true)]
    private static partial int UnixFStat(
        SafeFileHandle file,
        out UnixFileStatus information);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileWindows(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        FileInfoByHandleClass informationClass,
        out WindowsFileAttributeTagInformation information,
        uint bufferSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "DeviceIoControl",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(
        SafeFileHandle handle,
        uint controlCode,
        void* inputBuffer,
        uint inputBufferSize,
        void* outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetFileType(SafeFileHandle file);

    private enum FileInfoByHandleClass
    {
        FileAttributeTagInfo = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileAttributeTagInformation
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsSymbolicLinkReparseBuffer
    {
        internal uint ReparseTag;
        internal ushort ReparseDataLength;
        internal ushort Reserved;
        internal ushort SubstituteNameOffset;
        internal ushort SubstituteNameLength;
        internal ushort PrintNameOffset;
        internal ushort PrintNameLength;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsMountPointReparseBuffer
    {
        internal uint ReparseTag;
        internal ushort ReparseDataLength;
        internal ushort Reserved;
        internal ushort SubstituteNameOffset;
        internal ushort SubstituteNameLength;
        internal ushort PrintNameOffset;
        internal ushort PrintNameLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnixFileStatus
    {
        internal int Flags;
        internal int Mode;
        internal uint UserId;
        internal uint GroupId;
        internal long Size;
        internal long AccessTime;
        internal long AccessTimeNanoseconds;
        internal long ModificationTime;
        internal long ModificationTimeNanoseconds;
        internal long StatusChangeTime;
        internal long StatusChangeTimeNanoseconds;
        internal long BirthTime;
        internal long BirthTimeNanoseconds;
        internal long Device;
        internal long RawDevice;
        internal long Inode;
        internal uint UserFlags;
        internal int HardLinkCount;
    }
}
