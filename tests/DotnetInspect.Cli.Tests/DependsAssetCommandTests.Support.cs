using System.CommandLine;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Cache;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public partial class DependsAssetCommandTests
{
#if DEBUG
    private static async Task<JsonElement> RootAsync(string path)
    {
        (int exitCode, string output, _) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            path,
            "-S",
            "Roots",
            "--format=json",
            "--compact",
        ]);
        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("roots")[0].Clone();
    }

    private static string RestoredDigest(JsonElement root) =>
        root.GetProperty("evidence_identity")
            .GetProperty("restored_project")
            .GetProperty("facts_digest")
            .GetString()!;
#endif

    private static async Task<int> HierarchyCountAsync(string[] arguments)
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            .. arguments,
            "-S",
            "Dependency Hierarchy",
            "--count",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        return int.Parse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> DependencyCountAsync(string[] arguments)
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            .. arguments,
            "-S",
            "Dependencies",
            "--count",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        return int.Parse(
            output.Trim(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<JsonDocument> HierarchyJsonAsync(
        string[] arguments)
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            .. arguments,
            "-S",
            "Dependency Hierarchy",
            "--format=json",
            "--compact",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        return JsonDocument.Parse(output);
    }

    private static int NonEmptyLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    private static IEnumerable<(string Identity, int Depth)> ParseGraphLines(
        string output)
    {
        foreach (string line in output.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            yield return (
                $"{root.GetProperty("source_identity").GetString()}\0"
                    + $"{root.GetProperty("relationship").GetString()}\0"
                    + root.GetProperty("target_identity").GetString(),
                root.GetProperty("minimum_depth").GetInt32());
        }
    }

    private static Task<(int ExitCode, string Output, string Error)>
        RunCapturedAsync(string[] args) =>
        ConsoleCapture.RunAsync(() => RunAsync(args));

    private static Task<(int ExitCode, string Output, string Error)>
        RunCapturedOfflineAsync(string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Networking.HttpClientFactory
                .ResetSharedForTesting();
            try
            {
                return await RunAsync(args);
            }
            finally
            {
                DotnetInspector.Networking.HttpClientFactory.Initialize(
                    new HttpClientFactoryOptions());
                DotnetInspector.Networking.HttpClientFactory
                    .ResetSharedForTesting();
            }
        });

    private static async Task<(
        int ExitCode,
        string Output,
        string Error,
        ConcurrentQueue<Uri> Requests)> RunCapturedWithPackageFeedAsync(
            string[] args,
            string packageId,
            string version)
    {
        var requests = new ConcurrentQueue<Uri>();
        DotnetInspector.Networking.HttpClientFactory
            .SetAuthenticationDecorator(
                innerHandler => new PackageFeedHandler(
                    packageId,
                    version,
                    requests,
                    innerHandler));
        DotnetInspector.Networking.HttpClientFactory.Initialize(
            new HttpClientFactoryOptions());
        DotnetInspector.Networking.HttpClientFactory
            .ResetSharedForTesting();
        DotnetInspector.Networking.HttpClientFactory
            .SetPackageSourceHandlerForTesting(
                _ => new PackageFeedHandler(
                    packageId,
                    version,
                    requests,
                    new HttpClientHandler()));
        try
        {
            var result = await ConsoleCapture.RunAsync(async errorWriter =>
            {
                using IDisposable trafficLogging =
                    DotnetInspector.Networking.HttpClientFactory
                        .EnableNetworkTrafficLogging(
                            CSharpText.CSharpIdentifier
                                .ContainRenderedText,
                            errorWriter);
                return await RunAsync(args);
            });
            return (
                result.ExitCode,
                result.Output,
                result.Error,
                requests);
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory
                .SetAuthenticationDecorator(null);
            DotnetInspector.Networking.HttpClientFactory
                .SetPackageSourceHandlerForTesting(null);
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions());
            DotnetInspector.Networking.HttpClientFactory
                .ResetSharedForTesting();
        }
    }

    private static Task<int> RunAsync(string[] args)
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();
        string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
        return CommandLineBuilder.InvokeAsync(
            root.Parse(processed),
            processed);
    }

    private static string WriteTemporaryFile(string name, byte[] content)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-depends-tests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-depends-tests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class PackageFeedHandler(
        string packageId,
        string version,
        ConcurrentQueue<Uri> requests,
        HttpMessageHandler innerHandler)
        : DelegatingHandler(innerHandler)
    {
        private const string FlatContainer =
            "https://api.nuget.org/v3-flatcontainer/";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            requests.Enqueue(uri);
            string path = uri.GetLeftPart(UriPartial.Path);
            string? body = uri.Host.Equals(
                    "azuresearch-usnc.nuget.org",
                    StringComparison.OrdinalIgnoreCase)
                ? $$"""{"data":[{"id":"{{packageId}}","version":"{{version}}"}]}"""
                : path switch
                {
                    "https://api.nuget.org/v3/index.json" => $$"""
                        {
                          "version": "3.0.0",
                          "resources": [
                            {
                              "@id": "{{FlatContainer}}",
                              "@type": "PackageBaseAddress/3.0.0"
                            }
                          ]
                        }
                        """,
                    _ => null,
                };

            return Task.FromResult(new HttpResponseMessage(
                body is null
                    ? HttpStatusCode.NotFound
                    : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? ""),
                RequestMessage = request,
            });
        }
    }

    private static void WriteLocalSourcePackage(
        string folder,
        string packageId,
        string version,
        string dependenciesXml,
        string licenseXml = "")
    {
        string path = Path.Combine(
            folder,
            $"{packageId}.{version}.nupkg");
        using FileStream file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry($"{packageId}.nuspec");
        using Stream entryStream = entry.Open();
        entryStream.Write(
            Manifest(packageId, version, dependenciesXml, licenseXml));
    }

    private static string Dependency(string packageId, string constraint) =>
        $"""
         <group targetFramework="net8.0">
           <dependency id="{packageId}" version="{constraint}" />
         </group>
         """;

    private static byte[] DenseProjectMeshDocument(int projects)
    {
        var targets = new JsonObject();
        for (int index = 0; index < projects; index++)
        {
            var dependencies = new JsonObject();
            for (int other = 0; other < projects; other++)
            {
                if (other != index)
                    dependencies.Add($"Mesh.Project{other}", "1.0.0");
            }

            targets.Add(
                $"Mesh.Project{index}/1.0.0",
                new JsonObject
                {
                    ["type"] = "project",
                    ["dependencies"] = dependencies,
                });
        }

        var document = new JsonObject
        {
            ["version"] = 4,
            ["targets"] = new JsonObject
            {
                ["net11.0"] = targets,
            },
            ["projectFileDependencyGroups"] = new JsonObject
            {
                ["net11.0"] =
                    new JsonArray("Mesh.Project0 >= 1.0.0"),
            },
            ["project"] = new JsonObject
            {
                ["frameworks"] = new JsonObject
                {
                    ["net11.0"] = new JsonObject
                    {
                        ["dependencies"] = new JsonObject(),
                    },
                },
            },
        };
        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }

    private static byte[] Manifest(
        string packageId,
        string version,
        string dependencies,
        string license = "") =>
        Encoding.UTF8.GetBytes(
            $$"""
              <?xml version="1.0" encoding="utf-8"?>
              <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                <metadata>
                  <id>{{packageId}}</id>
                  <version>{{version}}</version>
                  <authors>Depends Tests</authors>
                  <description>Depends test package.</description>
                  {{license}}
                  <dependencies>
                    {{dependencies}}
                  </dependencies>
                </metadata>
              </package>
              """);

    private static InstalledPlatformPruneSource.Result PruneInventory(
        params string[] lines) =>
        PruneInventoryFor("net11.0", lines);

    private static InstalledPlatformPruneSource.Result PruneInventoryFor(
        string targetFramework,
        params string[] lines) =>
        PruneInventoryForFamily(
            "Microsoft.NETCore.App",
            targetFramework,
            lines);

    private static InstalledPlatformPruneSource.Result
        PruneInventoryForFamily(
            string family,
            string targetFramework,
            params string[] lines) =>
        new(
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    family,
                    targetFramework,
                    NuGetVersion.Parse("11.0.0")),
                lines),
            Error: null);
}
