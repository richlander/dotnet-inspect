using System.IO.Compression;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Builds in-memory <c>.nupkg</c> archives for tests that exercise
/// host-neutral package paths. Nothing is written to disk.
/// </summary>
internal static class TestPackageArchive
{
    internal static byte[] Create(params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string entryPath, byte[] content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryPath);
                using Stream stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return buffer.ToArray();
    }

    internal static byte[] Create(params string[] entryPaths)
        => Create([.. entryPaths.Select(path => (path, new byte[] { 1, 2, 3 }))]);
}
