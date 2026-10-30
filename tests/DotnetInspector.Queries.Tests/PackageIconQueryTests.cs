using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DotnetInspector.Packages;
using NuGetFetch;
using ZipFetch;

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

    [Fact]
    public async Task RangeQuery_RealPackageReadsOnlyDirectoryManifestAndIconRanges()
    {
        const string packageId = "System.Text.Json";
        const string packageVersion = "9.0.4";
        byte[] archive = await File.ReadAllBytesAsync(
            Path.Combine(
                FindRepositoryRoot(),
                "fixtures",
                "services",
                "signatures",
                "system.text.json.9.0.4.nupkg"),
            TestContext.Current.CancellationToken);
        var handler = new PackageRangeHandler(archive);
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                handler);

        PackageIconRangeResult result =
            await PackageIconRangeQuery.ExecuteAsync(
                Assert.IsAssignableFrom<IPackageArchiveRangeSource>(client),
                PackageSourceCoordinate.Create(packageId, packageVersion),
                ZipReadLimits.Default,
                TestContext.Current.CancellationToken);

        PackageIcon icon = Available(
            Assert.IsType<PackageIconRangeResult.Completed>(result).Icon);
        Assert.NotEmpty(icon.Bytes);
        Assert.True(handler.Requests >= 2);
        Assert.Equal(0, handler.NonRangeRequests);
        Assert.True(handler.BytesServed < archive.Length);
    }

    [Fact]
    public async Task RangeQuery_RangeRefusalDoesNotRetryAsACompleteGet()
    {
        byte[] archive = Archive(
            ($"{PackageId}.nuspec", Manifest("package.png")),
            ("package.png", Png));
        var handler = new PackageRangeHandler(
            archive,
            ignoreRanges: true);
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                handler);

        PackageIconRangeResult result =
            await PackageIconRangeQuery.ExecuteAsync(
                Assert.IsAssignableFrom<IPackageArchiveRangeSource>(client),
                PackageSourceCoordinate.Create(PackageId, PackageVersion),
                ZipReadLimits.Default,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            PackageArchiveReadRefusal.RangeIgnored,
            Assert.IsType<PackageIconRangeResult.Refused>(result).Reason);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(0, handler.NonRangeRequests);
    }

    [Fact]
    public async Task RangeQuery_PreservesMissingAndInvalidIconOutcomes()
    {
        PackageIconResult missing = await ExecuteRangeAsync(
            Archive(($"{PackageId}.nuspec", Manifest(iconPath: null))));
        Assert.IsType<PackageIconResult.Missing>(missing);

        PackageIconResult invalid = await ExecuteRangeAsync(
            Archive(
                ($"{PackageId}.nuspec", Manifest("package.png")),
                ("package.png", "not an image"u8.ToArray())));
        AssertUnavailable(
            PackageIconUnavailableReason.UnsupportedFormat,
            invalid);
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
        => new(
            Archive(entries),
            fromCache: false,
            producerKey: "package-icon-query-tests");

    static byte[] Archive(
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

        return package.ToArray();
    }

    static async Task<PackageIconResult> ExecuteRangeAsync(byte[] archive)
    {
        var handler = new PackageRangeHandler(archive);
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                handler);
        PackageIconRangeResult result =
            await PackageIconRangeQuery.ExecuteAsync(
                Assert.IsAssignableFrom<IPackageArchiveRangeSource>(client),
                PackageSourceCoordinate.Create(PackageId, PackageVersion),
                ZipReadLimits.Default,
                TestContext.Current.CancellationToken);
        Assert.Equal(0, handler.NonRangeRequests);
        return Assert.IsType<PackageIconRangeResult.Completed>(result).Icon;
    }

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(
                Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }

    sealed class PackageRangeHandler(
        byte[] archive,
        bool ignoreRanges = false) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public int NonRangeRequests { get; private set; }

        public long BytesServed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            RangeItemHeaderValue? range =
                request.Headers.Range?.Ranges.SingleOrDefault();
            if (range is null)
                NonRangeRequests++;
            if (ignoreRanges || range is null)
            {
                BytesServed += archive.Length;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archive),
                    RequestMessage = request,
                });
            }

            long from;
            long to;
            if (range.From is null)
            {
                from = Math.Max(0, archive.Length - range.To!.Value);
                to = archive.Length - 1;
            }
            else
            {
                from = range.From.Value;
                to = Math.Min(
                    range.To ?? archive.Length - 1,
                    archive.Length - 1);
            }

            byte[] bytes = archive[(int)from..(int)(to + 1)];
            BytesServed += bytes.Length;
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentRange =
                new ContentRangeHeaderValue(from, to, archive.Length);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = content,
                    RequestMessage = request,
                });
        }
    }
}
