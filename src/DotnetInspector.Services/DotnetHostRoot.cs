using System.Runtime.InteropServices;

namespace DotnetInspector.Services;

/// <summary>
/// Locates the dotnet installation that owns the <c>dotnet</c> host on
/// <c>PATH</c>. A NativeAOT build has no CoreCLR runtime directory to derive
/// an installation from, and SDK installers such as dotnetup place the root
/// outside the well-known locations, so the host the user runs is the
/// portable signal.
/// </summary>
public static class DotnetHostRoot
{
    private const UnixFileMode ExecuteBits =
        UnixFileMode.UserExecute
        | UnixFileMode.GroupExecute
        | UnixFileMode.OtherExecute;

    /// <summary>
    /// Returns the directory of the first <c>dotnet</c> host found on
    /// <c>PATH</c>, following a symbolic link to the host's real location,
    /// or <see langword="null"/> when none is found.
    /// </summary>
    public static string? FindOnPath() =>
        FindOnPath(
            Environment.GetEnvironmentVariable("PATH"),
            Environment.CurrentDirectory);

    internal static string? FindOnPath(string? path) =>
        FindOnPath(path, Environment.CurrentDirectory);

    internal static string? FindOnPath(
        string? path,
        string currentDirectory)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        string hostName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "dotnet.exe"
            : "dotnet";
        foreach (string entry in path.Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.GetFullPath(
                    Path.Combine(entry, hostName),
                    currentDirectory);
                if (!File.Exists(candidate)
                    || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        && (File.GetUnixFileMode(candidate) & ExecuteBits) == 0)
                    continue;

                string host =
                    File.ResolveLinkTarget(candidate, returnFinalTarget: true)
                        ?.FullName
                    ?? Path.GetFullPath(candidate);
                return Path.GetDirectoryName(host);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException)
            {
                // An unreadable PATH entry is skipped like a missing one.
            }
        }

        return null;
    }
}
