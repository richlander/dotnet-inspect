using System.IO.Compression;
using System.Text;
using DotnetInspector.Packages;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageIconQueryTests
{
    const string PackageId = "Example.Package";
    const string PackageVersion = "1.0.0";

    static readonly byte[] Png =
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public void Execute_ProjectsDeclaredPngWithNuGetPathSeparators()
    {
        InMemoryPackageContent content = Content(
            ($"{PackageId}.nuspec", Manifest(@"images\package.png")),
            ("images/package.png", Png));

        PackageIcon icon = Available(
            PackageIconQuery.Execute(
                content,
                PackageId,
                PackageVersion));

        Assert.Equal("image/png", icon.MediaType);
        Assert.Equal(Png, icon.Bytes.ToArray());
    }

    [Fact]
    public void Execute_ProjectsDeclaredJpegByContent()
    {
        byte[] jpeg =
        [
            0xff, 0xd8,
            0xff, 0xc0, 0x00, 0x0b, 0x08,
            0x00, 0x80, 0x00, 0x80,
            0x01, 0x01, 0x11, 0x00,
            0xff, 0xd9,
        ];
        InMemoryPackageContent content = Content(
            ($"{PackageId}.nuspec", Manifest("package.bin")),
            ("package.bin", jpeg));

        PackageIcon icon = Available(
            PackageIconQuery.Execute(
                content,
                PackageId,
                PackageVersion));

        Assert.Equal("image/jpeg", icon.MediaType);
        Assert.Equal(jpeg, icon.Bytes.ToArray());
    }

    [Fact]
    public void Execute_IconUrlAloneDoesNotAuthorizeNetworkAcquisition()
    {
        InMemoryPackageContent content = Content(
            ($"{PackageId}.nuspec", Manifest(
                iconPath: null,
                iconUrl: "https://example.test/package.png")));

        Assert.IsType<PackageIconResult.Missing>(
            PackageIconQuery.Execute(
                content,
                PackageId,
                PackageVersion));
    }

    [Theory]
    [InlineData("../package.png")]
    [InlineData("/package.png")]
    [InlineData("images//package.png")]
    [InlineData("C:/package.png")]
    public void Execute_RejectsUnsafeDeclaredPaths(string iconPath)
    {
        InMemoryPackageContent content = Content(
            ($"{PackageId}.nuspec", Manifest(iconPath)));

        AssertUnavailable(
            PackageIconUnavailableReason.InvalidPath,
            PackageIconQuery.Execute(
                content,
                PackageId,
                PackageVersion));
    }

    [Fact]
    public void Execute_ReportsMissingDeclaredEntry()
    {
        InMemoryPackageContent content = Content(
            ($"{PackageId}.nuspec", Manifest("missing.png")));

        AssertUnavailable(
            PackageIconUnavailableReason.MissingEntry,
            PackageIconQuery.Execute(
                content,
                PackageId,
                PackageVersion));
    }

    [Fact]
    public void Execute_RejectsUnsupportedContentDespitePngExtension()
    {
        InMemoryPackageContent content = Content(
            ($"{PackageId}.nuspec", Manifest("package.png")),
            ("package.png", "not a png"u8.ToArray()));

        AssertUnavailable(
            PackageIconUnavailableReason.UnsupportedFormat,
            PackageIconQuery.Execute(
                content,
                PackageId,
                PackageVersion));
    }

    [Fact]
    public void Execute_RejectsPngWhoseDecodedDimensionsExceedBrowserBound()
    {
        byte[] oversizedDimensions = Png.ToArray();
        oversizedDimensions[16] = 0x00;
        oversizedDimensions[17] = 0x00;
        oversizedDimensions[18] = 0x10;
        oversizedDimensions[19] = 0x00;
        InMemoryPackageContent content = Content(
            ($"{PackageId}.nuspec", Manifest("package.png")),
            ("package.png", oversizedDimensions));

        AssertUnavailable(
            PackageIconUnavailableReason.InvalidImage,
            PackageIconQuery.Execute(
                content,
                PackageId,
                PackageVersion));
    }

    [Fact]
    public void Execute_EnforcesNuGetEncodedByteLimit()
    {
        byte[] oversized = new byte[PackageIconQuery.MaxIconBytes + 1];
        Png.CopyTo(oversized, 0);
        InMemoryPackageContent content = Content(
            ($"{PackageId}.nuspec", Manifest("package.png")),
            ("package.png", oversized));

        AssertUnavailable(
            PackageIconUnavailableReason.ConfiguredLimitExceeded,
            PackageIconQuery.Execute(
                content,
                PackageId,
                PackageVersion));
    }

    static PackageIcon Available(PackageIconResult result) =>
        Assert.IsType<PackageIconResult.Available>(result).Value;

    static void AssertUnavailable(
        PackageIconUnavailableReason expected,
        PackageIconResult result) =>
        Assert.Equal(
            expected,
            Assert.IsType<PackageIconResult.Unavailable>(result).Reason);

    static byte[] Manifest(
        string? iconPath,
        string? iconUrl = null) =>
        Encoding.UTF8.GetBytes(
            $"""
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{PackageId}</id>
                <version>{PackageVersion}</version>
                <authors>Example</authors>
                <description>Example</description>
                {(iconPath is null ? "" : $"<icon>{iconPath}</icon>")}
                {(iconUrl is null ? "" : $"<iconUrl>{iconUrl}</iconUrl>")}
              </metadata>
            </package>
            """);

    static InMemoryPackageContent Content(
        params (string Path, byte[] Content)[] entries)
    {
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(
            package,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] bytes) in entries)
            {
                using Stream entry = archive
                    .CreateEntry(path, CompressionLevel.NoCompression)
                    .Open();
                entry.Write(bytes);
            }
        }

        return new InMemoryPackageContent(
            package.ToArray(),
            fromCache: false,
            producerKey: "package-icon-query-tests");
    }
}
