using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DotnetInspector.Inspectors;

internal readonly record struct PhysicalFileIdentity(
    ulong Device,
    ulong FileLow,
    ulong FileHigh);

internal static partial class PhysicalFileIdentityProvider
{
    internal static bool TryGet(
        string path,
        out PhysicalFileIdentity identity,
        out string? failure)
    {
        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return TryGet(handle, out identity, out failure);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or DllNotFoundException
            or EntryPointNotFoundException)
        {
            identity = default;
            failure = $"{ex.GetType().Name} (0x{ex.HResult:X8})";
            return false;
        }
    }

    private static bool TryGet(
        SafeFileHandle handle,
        out PhysicalFileIdentity identity,
        out string? failure)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetFileInformationByHandleEx(
                handle,
                FileInfoByHandleClass.FileIdInfo,
                out WindowsFileIdInformation info,
                (uint)Marshal.SizeOf<WindowsFileIdInformation>()))
            {
                identity = default;
                failure = $"GetFileInformationByHandleEx failed with error " +
                    $"{Marshal.GetLastPInvokeError()}";
                return false;
            }

            if (info.FileIdLow == 0 && info.FileIdHigh == 0)
            {
                identity = default;
                failure = "GetFileInformationByHandleEx returned no stable file ID";
                return false;
            }

            identity = new PhysicalFileIdentity(
                info.VolumeSerialNumber,
                info.FileIdLow,
                info.FileIdHigh);
            failure = null;
            return true;
        }

        if (!Environment.Is64BitProcess)
        {
            identity = default;
            failure = "physical file identity is unavailable in a 32-bit process";
            return false;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            if (UnixFStat(handle, out UnixFileStatus info) != 0)
            {
                identity = default;
                failure = $"SystemNative_FStat failed with error " +
                    $"{Marshal.GetLastPInvokeError()}";
                return false;
            }

            return FromUnix(
                unchecked((ulong)info.Device),
                unchecked((ulong)info.Inode),
                out identity,
                out failure);
        }

        identity = default;
        failure = $"physical file identity is unavailable on " +
            $"{RuntimeInformation.OSDescription}";
        return false;
    }

    private static bool FromUnix(
        ulong device,
        ulong inode,
        out PhysicalFileIdentity identity,
        out string? failure)
    {
        if (inode == 0)
        {
            identity = default;
            failure = "fstat returned no stable inode";
            return false;
        }

        identity = new PhysicalFileIdentity(device, inode, 0);
        failure = null;
        return true;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        FileInfoByHandleClass informationClass,
        out WindowsFileIdInformation information,
        uint bufferSize);

    [LibraryImport(
        "libSystem.Native",
        EntryPoint = "SystemNative_FStat",
        SetLastError = true)]
    private static partial int UnixFStat(
        SafeFileHandle file,
        out UnixFileStatus information);

    private enum FileInfoByHandleClass
    {
        FileIdInfo = 18,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileIdInformation
    {
        internal ulong VolumeSerialNumber;
        internal ulong FileIdLow;
        internal ulong FileIdHigh;
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
