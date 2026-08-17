using System.IO.Compression;
using System.Net;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class DependencyGraphServiceTests
{
    public DependencyGraphServiceTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_FileInput_ReturnsAssemblyReferenceGraph()
    {
        var assemblyPath = typeof(DependencyGraphServiceTests).Assembly.Location;
        using var httpClient = new HttpClient();
        var logger = new VerboseLogger(enabled: false);

        var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
            httpClient, assemblyPath, sourceOptions: null, logger);

        var graph = Assert.IsType<LibraryDependencyGraphResult.Graph>(result);
        Assert.Equal("dotnet-inspect.Tests", graph.AssemblyName);
        Assert.NotEmpty(graph.References);
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_PackageInputUsesLibDescendingRootOrder()
    {
        var packageDir = Directory.CreateTempSubdirectory("depends-package-root-test").FullName;
        var packagePath = Path.Combine(packageDir, "DependsRoot.1.0.0.nupkg");
        var sourceAssembly = typeof(DependencyGraphServiceTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "lib/net6.0/A.dll");
                archive.CreateEntryFromFile(sourceAssembly, "lib/netstandard2.0/Z.dll");
            }

            using var httpClient = new HttpClient();
            var logger = new VerboseLogger(enabled: false);

            var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                httpClient,
                packagePath,
                sourceOptions: null,
                logger);

            var graph = Assert.IsType<LibraryDependencyGraphResult.Graph>(result);
            Assert.Equal("Z", graph.AssemblyName);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_PackageInputWithNoLibAssembliesReportsSpecificError()
    {
        var packageDir = Directory.CreateTempSubdirectory("depends-package-no-lib-test").FullName;
        var packagePath = Path.Combine(packageDir, "NoLib.1.0.0.nupkg");
        var sourceAssembly = typeof(DependencyGraphServiceTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "tools/NoLib.dll");
            }

            using var httpClient = new HttpClient();
            var logger = new VerboseLogger(enabled: false);

            var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                httpClient,
                packagePath,
                sourceOptions: null,
                logger);

            var error = Assert.IsType<LibraryDependencyGraphResult.Error>(result);
            Assert.Contains("No libraries found in package", error.Message);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildPackageDependencyTreeAsync_LocalPackageStillSupported()
    {
        string packageDir =
            Directory.CreateTempSubdirectory("depends-local-package-test")
                .FullName;
        string packagePath =
            Path.Combine(packageDir, "Depends.Local.1.0.0.nupkg");

        try
        {
            using (ZipArchive archive =
                ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry nuspec =
                    archive.CreateEntry("Depends.Local.nuspec");
                await using Stream stream = nuspec.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(
                    """
                    <?xml version="1.0"?>
                    <package>
                      <metadata>
                        <id>Depends.Local</id>
                        <version>1.0.0</version>
                      </metadata>
                    </package>
                    """);
            }

            using var httpClient = new HttpClient();
            var logger = new VerboseLogger(enabled: false);

            PackageDependencyGraphResult result =
                await DependencyGraphService.BuildPackageDependencyTreeAsync(
                    httpClient,
                    packagePath,
                    requestedTfm: null,
                    sourceOptions: null,
                    logger);

            Assert.IsType<PackageDependencyGraphResult.Empty>(result);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildPackageDependencyTreeAsync_AcquiresOnlyRootNuspec()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Depends.Root.{suffix}";
        string reportingServiceIndex =
            $"https://reporting.example.test/{suffix}/v3/index.json";
        string reportingFlatContainer =
            $"https://reporting-content.example.test/{suffix}/flat/";
        string otherServiceIndex =
            $"https://other.example.test/{suffix}/v3/index.json";
        string otherFlatContainer =
            $"https://other-content.example.test/{suffix}/flat/";
        var handler = new ManifestOnlyHandler(
            reportingServiceIndex,
            reportingFlatContainer,
            otherServiceIndex,
            otherFlatContainer,
            packageId);
        using var httpClient = new HttpClient(handler);
        var logger = new VerboseLogger(enabled: false);

        PackageDependencyGraphResult result =
            await DependencyGraphService.BuildPackageDependencyTreeAsync(
                httpClient,
                packageId,
                requestedTfm: null,
                new NuGetSourceOptions
                {
                    Sources =
                    [
                        otherServiceIndex,
                        reportingServiceIndex,
                    ],
                },
                logger);

        Assert.IsType<PackageDependencyGraphResult.Empty>(result);
        Assert.Contains(
            handler.Requests,
            uri => uri.AbsolutePath.EndsWith(
                $"/{packageId.ToLowerInvariant()}.nuspec",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            handler.Requests,
            uri => uri.AbsoluteUri.StartsWith(
                    otherFlatContainer,
                    StringComparison.Ordinal)
                && uri.AbsolutePath.EndsWith(
                    $"/1.0.0/{packageId.ToLowerInvariant()}.nuspec",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            handler.Requests,
            uri => uri.AbsolutePath.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ManifestOnlyHandler(
        string reportingServiceIndex,
        string reportingFlatContainer,
        string otherServiceIndex,
        string otherFlatContainer,
        string packageId) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri);

            if (uri.AbsoluteUri.Equals(
                reportingServiceIndex,
                StringComparison.Ordinal))
            {
                return Task.FromResult(Response(
                    ServiceIndex(reportingFlatContainer)));
            }
            if (uri.AbsoluteUri.Equals(
                otherServiceIndex,
                StringComparison.Ordinal))
            {
                return Task.FromResult(Response(
                    ServiceIndex(otherFlatContainer)));
            }

            string normalizedPackageId = packageId.ToLowerInvariant();
            if (uri.AbsoluteUri.Equals(
                $"{reportingFlatContainer}{normalizedPackageId}/index.json",
                StringComparison.Ordinal))
            {
                return Task.FromResult(Response(
                    """{"versions":["1.0.0"]}"""));
            }
            if (uri.AbsoluteUri.Equals(
                $"{otherFlatContainer}{normalizedPackageId}/index.json",
                StringComparison.Ordinal))
            {
                return Task.FromResult(Response(
                    """{"versions":["0.9.0"]}"""));
            }

            if (uri.AbsolutePath.EndsWith(
                $"/1.0.0/{normalizedPackageId}.nuspec",
                StringComparison.Ordinal))
            {
                return Task.FromResult(Response(
                    $$"""
                    <?xml version="1.0"?>
                    <package>
                      <metadata>
                        <id>{{packageId}}</id>
                        <version>1.0.0</version>
                      </metadata>
                    </package>
                    """));
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                });
        }

        private static string ServiceIndex(string flatContainer) =>
            $$"""
            {
              "resources": [
                {
                  "@type": "PackageBaseAddress/3.0.0",
                  "@id": "{{flatContainer}}"
                }
              ]
            }
            """;

        private static HttpResponseMessage Response(string content) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(content),
            };
    }
}
