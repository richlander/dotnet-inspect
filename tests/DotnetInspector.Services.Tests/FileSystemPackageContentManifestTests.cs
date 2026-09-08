using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Filesystem package content exposes declared entry lengths so a bounded
/// caller can reject an over-budget entry before opening its body.
/// </summary>
public sealed class FileSystemPackageContentManifestTests
{
    [Fact]
    public void FileSystemLengthUsesManifestPreflight()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-manifest-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "lib", "net11.0"));
            byte[] payload = new byte[1234];
            string relativePath = "lib/net11.0/Sample.dll";
            File.WriteAllBytes(
                Path.Combine(root, "lib", "net11.0", "Sample.dll"),
                payload);

            var content = new FileSystemPackageContent(
                root,
                nupkgPath: null,
                fromCache: false,
                producerKey: "tests",
                requiresArchiveTreeMatch: false);

            var manifest = Assert.IsAssignableFrom<IPackageContentEntryManifest>(
                content);
            Assert.True(
                manifest.TryGetEntryLength(relativePath, out long length));
            Assert.Equal(payload.LongLength, length);
            Assert.False(
                manifest.TryGetEntryLength(
                    "lib/net11.0/Missing.dll",
                    out long missing));
            Assert.Equal(0, missing);
            Assert.Equal(
                payload.LongLength,
                Assert.Single(
                    manifest.EnumerateEntriesWithLengths(),
                    entry => entry.Path == relativePath).Length);

            // Over budget, bounded open throws an InvalidDataException that a
            // caller cannot distinguish from an unrelated read failure. The
            // manifest is what lets that caller answer before opening at all.
            Assert.Throws<InvalidDataException>(
                () => content.TryOpenEntry(
                    relativePath,
                    payload.LongLength - 1,
                    out _));
            Assert.True(
                content.TryOpenEntry(
                    relativePath,
                    payload.LongLength,
                    out Stream? admitted));
            using (admitted)
            {
                Assert.NotNull(admitted);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
