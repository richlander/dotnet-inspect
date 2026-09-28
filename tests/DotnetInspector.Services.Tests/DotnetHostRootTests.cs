using System.Runtime.InteropServices;

namespace DotnetInspector.Services.Tests;

public sealed class DotnetHostRootTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"dotnet-host-root-{Guid.NewGuid():N}");

    public DotnetHostRootTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string HostName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "dotnet.exe"
            : "dotnet";

    [Fact]
    public void FindsTheFirstHostOnPathInOrder()
    {
        string empty = Directory.CreateDirectory(
            Path.Combine(_root, "empty")).FullName;
        string first = Directory.CreateDirectory(
            Path.Combine(_root, "first")).FullName;
        string second = Directory.CreateDirectory(
            Path.Combine(_root, "second")).FullName;
        File.WriteAllText(Path.Combine(first, HostName), "");
        File.WriteAllText(Path.Combine(second, HostName), "");

        string path = string.Join(
            Path.PathSeparator,
            [empty, Path.Combine(_root, "missing"), first, second]);

        Assert.Equal(
            Path.GetFullPath(first),
            DotnetHostRoot.FindOnPath(path));
    }

    [Fact]
    public void FollowsALinkedHostToItsInstallation()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return; // Creating symbolic links needs elevation on Windows.

        // Installers commonly put /usr/local/bin/dotnet ->
        // /usr/local/share/dotnet/dotnet on PATH.
        string install = Directory.CreateDirectory(
            Path.Combine(_root, "share", "dotnet")).FullName;
        string bin = Directory.CreateDirectory(
            Path.Combine(_root, "bin")).FullName;
        File.WriteAllText(Path.Combine(install, HostName), "");
        File.CreateSymbolicLink(
            Path.Combine(bin, HostName),
            Path.Combine(install, HostName));

        string? found = DotnetHostRoot.FindOnPath(bin);

        Assert.NotNull(found);
        Assert.Equal(
            Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(found).TrimEnd(Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ReturnsNullWithoutPath(string? path) =>
        Assert.Null(DotnetHostRoot.FindOnPath(path));

    [Fact]
    public void ReturnsNullWhenNoEntryHoldsAHost() =>
        Assert.Null(DotnetHostRoot.FindOnPath(
            string.Join(Path.PathSeparator, [_root, Path.Combine(_root, "none")])));
}
