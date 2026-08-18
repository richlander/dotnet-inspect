using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DotnetInspector.Inspectors;

internal readonly record struct PhysicalFileIdentity(ulong Device, ulong File);

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
            if (!GetFileInformationByHandle(handle, out WindowsFileInformation info))
            {
                identity = default;
                failure = $"GetFileInformationByHandle failed with error " +
                    $"{Marshal.GetLastPInvokeError()}";
                return false;
            }

            ulong file = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
            if (file == 0)
            {
                identity = default;
                failure = "GetFileInformationByHandle returned no stable file index";
                return false;
            }

            identity = new PhysicalFileIdentity(info.VolumeSerialNumber, file);
            failure = null;
            return true;
        }

        if (!Environment.Is64BitProcess)
        {
            identity = default;
            failure = "physical file identity is unavailable in a 32-bit process";
            return false;
        }

        if (OperatingSystem.IsLinux())
        {
            int descriptor = handle.DangerousGetHandle().ToInt32();
            if (LinuxFStat(descriptor, out LinuxFileStatus info) != 0)
            {
                identity = default;
                failure = $"fstat failed with error {Marshal.GetLastPInvokeError()}";
                return false;
            }

            return FromUnix(info.Device, info.Inode, out identity, out failure);
        }

        if (OperatingSystem.IsMacOS())
        {
            int descriptor = handle.DangerousGetHandle().ToInt32();
            if (MacFStat(descriptor, out MacFileStatus info) != 0)
            {
                identity = default;
                failure = $"fstat$INODE64 failed with error " +
                    $"{Marshal.GetLastPInvokeError()}";
                return false;
            }

            return FromUnix(info.Device, info.Inode, out identity, out failure);
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

        identity = new PhysicalFileIdentity(device, inode);
        failure = null;
        return true;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(
        SafeFileHandle file,
        out WindowsFileInformation information);

    [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static partial int LinuxFStat(
        int descriptor,
        out LinuxFileStatus information);

    [LibraryImport(
        "libSystem.dylib",
        EntryPoint = "fstat$INODE64",
        SetLastError = true)]
    private static partial int MacFStat(
        int descriptor,
        out MacFileStatus information);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileInformation
    {
        internal uint FileAttributes;
        internal uint CreationTimeLow;
        internal uint CreationTimeHigh;
        internal uint LastAccessTimeLow;
        internal uint LastAccessTimeHigh;
        internal uint LastWriteTimeLow;
        internal uint LastWriteTimeHigh;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxFileStatus
    {
        // The supported 64-bit Linux ABIs begin stat with dev_t and ino_t.
        // Reserve more than the complete native struct so fstat cannot overrun it.
        [FieldOffset(0)]
        internal ulong Device;

        [FieldOffset(8)]
        internal ulong Inode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct MacFileStatus
    {
        // Darwin's 64-bit inode layout places dev_t at 0 and ino_t at 8.
        [FieldOffset(0)]
        internal uint Device;

        [FieldOffset(8)]
        internal ulong Inode;
    }
}
