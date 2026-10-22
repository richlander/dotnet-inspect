using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Theory]
    [InlineData(null, "Measured")]
    [InlineData("all", "Measured")]
    [InlineData("net9.0", "NoApplicableSlice")]
    public async Task PackageCommand_OnlineFactsQueryContentOnlyRoot(
        string? framework,
        string expectedStatus)
    {
        string id = $"Workspace.PackageFacts.{Guid.NewGuid():N}";
        byte[] archive = CreatePackage(
            id,
            "package facts do not require a loadable Library",
            library: new byte[17]);
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                requests));
        List<string> args =
        [
            "package", $"{id}@{Version}", "--source", FirstFeed,
            "-S", "Package Info", "--json", "--verbose",
        ];
        if (framework is not null)
            args.AddRange(["--tfm", framework]);

        var result = await RunCommandAsync([.. args]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "Using committed content-only package Root for inspection:",
            result.Error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement measurements = document.RootElement.GetProperty(
            "package_info_measurements");
        Assert.Equal(
            expectedStatus,
            measurements.GetProperty("status").GetString());
        Assert.Equal(
            archive.LongLength,
            measurements.GetProperty("compressed_package_bytes").GetInt64());
        if (expectedStatus == "Measured")
        {
            Assert.Equal(
                17,
                measurements.GetProperty(
                    "selected_library_payload_bytes").GetInt64());
        }
        else
        {
            Assert.False(measurements.TryGetProperty(
                "selected_library_payload_bytes", out _));
        }
        Assert.Single(requests, request =>
            request.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PackageCommand_OnlineChildrenRetainExplicitTarget()
    {
        string id = $"Workspace.PackageChildren.{Guid.NewGuid():N}";
        byte[] library = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] archive = CreatePackage(
            id,
            "the requested framework chooses the Library population",
            library: library,
            libraryName: "Net11Only.dll",
            extraEntries:
            [
                ("lib/net8.0/Net8Only.dll", library),
            ]);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
        [
            "package", $"{id}@{Version}", "--source", FirstFeed,
            "--tfm", "net8.0", "--json", "--verbose",
        ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "Using committed content-only package Root for inspection:",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains("Net8Only", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Net11Only", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageCommand_OnlineFactsRetainSelectedReportingSource()
    {
        string id = $"Workspace.SelectedFacts.{Guid.NewGuid():N}";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new SelectionFeedHandler(
                source,
                id,
                source == FirstFeed ? ["1.0.0", "2.0.0"] : ["1.5.0"],
                version => CreatePackage(id, source, version: version),
                requests));

        var result = await RunCommandAsync(
        [
            "package", $"{id}@1.*", "--source", FirstFeed,
            "--source", SecondFeed, "-S", "Package Info",
            "--json", "--verbose",
        ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "Using committed content-only package Root for inspection:",
            result.Error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "1.5.0",
            document.RootElement.GetProperty("version").GetString());
        string payloadRequest = Assert.Single(requests, request =>
            request.EndsWith(".nupkg", StringComparison.Ordinal));
        Assert.StartsWith(
            "https://second-payload.invalid/",
            payloadRequest,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("all", false)]
    [InlineData("net9.0", false)]
    [InlineData(null, true)]
    public async Task PackageCommand_OnlineSurveyQueriesIndependentContentRoots(
        string? framework,
        bool repeatedInput)
    {
        string firstId = $"Workspace.First.{Guid.NewGuid():N}";
        string secondId = repeatedInput
            ? firstId
            : $"Workspace.Second.{Guid.NewGuid():N}";
        byte[] first = CreatePackage(
            firstId, "first subject", library: new byte[17]);
        byte[] second = repeatedInput
            ? first
            : CreatePackage(
                secondId, "second subject", library: new byte[29]);
        string source = WritePackageSurveyFeed(
            (firstId, first),
            (secondId, second));
        List<string> args =
        [
            "package", $"{firstId}@{Version}", $"{secondId}@{Version}",
            "--source", source, "-S", "Package Info", "--json", "--verbose",
        ];
        if (framework is not null)
            args.AddRange(["--tfm", framework]);

        var result = await RunCommandAsync([.. args]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Equal(
            2,
            result.Error.Split(
                "Using committed content-only package Root for inspection:",
                StringSplitOptions.None).Length - 1);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement[] packages = [.. document.RootElement.EnumerateArray()];
        Assert.Equal(2, packages.Length);
        Assert.Equal(
            firstId,
            packages[0].GetProperty("package_name").GetString(),
            ignoreCase: true);
        Assert.Equal(
            secondId,
            packages[1].GetProperty("package_name").GetString(),
            ignoreCase: true);
        long[] sizes = [first.LongLength, second.LongLength];
        long[] librarySizes = [17, repeatedInput ? 17 : 29];
        for (int index = 0; index < packages.Length; index++)
        {
            JsonElement measurements =
                packages[index].GetProperty("package_info_measurements");
            Assert.Equal(
                sizes[index],
                measurements.GetProperty("compressed_package_bytes").GetInt64());
            Assert.Equal(
                framework == "net9.0" ? "NoApplicableSlice" : "Measured",
                measurements.GetProperty("status").GetString());
            if (framework == "net9.0")
            {
                Assert.False(measurements.TryGetProperty(
                    "selected_library_payload_bytes", out _));
            }
            else
            {
                Assert.Equal(
                    librarySizes[index],
                    measurements.GetProperty(
                        "selected_library_payload_bytes").GetInt64());
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageCommand_OnlineSurveyRetainsGlobalReadmeRows(
        bool count)
    {
        string firstId = $"Workspace.Readme.First.{Guid.NewGuid():N}";
        string secondId = $"Workspace.Readme.Second.{Guid.NewGuid():N}";
        string source = WritePackageSurveyFeed(
            (firstId, CreatePackage(firstId, "first README")),
            (secondId, CreatePackage(secondId, "second README")));
        List<string> args =
        [
            "package", $"{firstId}@{Version}", $"{secondId}@{Version}",
            "--source", source, "--path", "@readme",
            "--rows", "2..", "--verbose",
        ];
        args.AddRange(count ? ["--count"] : ["--tsv", "--no-header"]);

        var result = await RunCommandAsync([.. args]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.DoesNotContain(
            "package-info-measurements.",
            result.Error,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            result.Error.Split(
                "Using committed content-only package Root for inspection:",
                StringSplitOptions.None).Length - 1);
        if (count)
        {
            Assert.Equal("1", result.Output.Trim());
        }
        else
        {
            Assert.Contains(
                secondId, result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                firstId, result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("README.md", result.Output, StringComparison.Ordinal);
            Assert.Single(result.Output.Split(
                '\n', StringSplitOptions.RemoveEmptyEntries));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("DotnetTool")]
    [InlineData("DotnetToolRidPackage")]
    public async Task PackageCommand_OnlineSurveyFilesJsonRetainsHouseMeasurements(
        string? toolPackageType)
    {
        string firstId = $"Workspace.FilesJson.First.{Guid.NewGuid():N}";
        string secondId = $"Workspace.FilesJson.Second.{Guid.NewGuid():N}";
        byte[] first = toolPackageType is null
            ? CreatePackage(firstId, "first README", library: new byte[17])
            : CreatePackage(
                firstId,
                "first README",
                library: new byte[17],
                libraryDirectory: "tools/net11.0/any",
                nuspecContent: $"""
                    <package><metadata>
                      <id>{firstId}</id><version>{Version}</version>
                      <authors>Payload tests</authors><description>Tool measurement fixture</description>
                      <readme>README.md</readme>
                      <packageTypes><packageType name="{toolPackageType}" /></packageTypes>
                    </metadata></package>
                    """,
                extraEntries:
                [
                    ("tools/net11.0/any/DotnetToolSettings.xml",
                        Encoding.UTF8.GetBytes("""
                            <DotNetCliTool Version="1">
                              <Commands><Command Name="measurement-tool" EntryPoint="Npgsql.dll" Runner="dotnet" /></Commands>
                            </DotNetCliTool>
                            """)),
                ]);
        string source = WritePackageSurveyFeed(
            (firstId, first),
            (secondId, CreatePackage(secondId, "second README", library: new byte[29])));

        var result = await RunCommandAsync(
        [
            "package", $"{firstId}@{Version}", $"{secondId}@{Version}",
            "--source", source, "--path", "@readme", "--json", "--verbose",
        ]);

        Assert.True(result.Exit == 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement[] packages = [.. document.RootElement.EnumerateArray()];
        Assert.Equal(2, packages.Length);
        long[] librarySizes = [17, 29];
        for (int index = 0; index < packages.Length; index++)
        {
            Assert.Equal(
                "README.md",
                Assert.Single(packages[index].GetProperty("files").EnumerateArray())
                    .GetProperty("path").GetString());
            JsonElement measurements =
                packages[index].GetProperty("package_info_measurements");
            Assert.Equal("Measured", measurements.GetProperty("status").GetString());
            Assert.Equal(
                librarySizes[index],
                measurements.GetProperty("selected_library_payload_bytes").GetInt64());
        }
    }

    [Fact]
    public async Task PackageCommand_OnlineSurveyRetainsPerInputSelectedVersions()
    {
        string id = $"Workspace.SelectedSurvey.{Guid.NewGuid():N}";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new SelectionFeedHandler(
                source,
                id,
                source == FirstFeed
                    ? ["1.0.0", "2.0.0"]
                    : ["1.5.0", "2.5.0"],
                version => CreatePackage(id, source, version: version),
                requests));

        var result = await RunCommandAsync(
        [
            "package", $"{id}@1.*", $"{id}@2.*",
            "--source", FirstFeed, "--source", SecondFeed,
            "-S", "Package Info", "--json", "--verbose",
        ]);

        Assert.True(result.Exit == 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            ["1.5.0", "2.5.0"],
            document.RootElement.EnumerateArray()
                .Select(package => package.GetProperty("version").GetString()));
        string[] payloads =
        [
            .. requests.Where(request =>
                request.EndsWith(".nupkg", StringComparison.Ordinal)),
        ];
        Assert.Equal(2, payloads.Length);
        Assert.All(payloads, request =>
            Assert.StartsWith(
                "https://second-payload.invalid/",
                request,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task PackageCommand_OnlineSurveyAcquisitionFailureDoesNotRetryOrPublishPartialOutput()
    {
        string id = $"Workspace.FailedSurvey.{Guid.NewGuid():N}";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new SelectionFeedHandler(
                source,
                id,
                [Version, "2.0.0"],
                version => version == Version
                    ? CreatePackage(id, "accepted subject")
                    : new byte[17],
                requests));

        var result = await RunCommandAsync(
        [
            "package", $"{id}@{Version}", $"{id}@2.0.0",
            "--source", FirstFeed, "-S", "Package Info", "--json", "--verbose",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Error:", result.Error, StringComparison.Ordinal);
        Assert.Equal(
            1,
            result.Error.Split(
                "Using committed content-only package Root for inspection:",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            2,
            requests.Count(request =>
                request.EndsWith(".nupkg", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PackageCommand_OnlineSurveyPreservesLocalFileCompatibility()
    {
        string localId = $"Workspace.LocalSubject.{Guid.NewGuid():N}";
        string onlineId = $"Workspace.OnlineSubject.{Guid.NewGuid():N}";
        string localPath = Path.Combine(_root, $"{localId}.{Version}.nupkg");
        File.WriteAllBytes(
            localPath, CreatePackage(localId, "explicit local subject"));
        string source = WritePackageSurveyFeed(
            (onlineId, CreatePackage(onlineId, "configured source subject")));

        var result = await RunCommandAsync(
        [
            "package", localPath, $"{onlineId}@{Version}", "--source", source,
            "-S", "Package Info", "--json", "--verbose",
        ]);

        Assert.True(result.Exit == 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement[] packages = [.. document.RootElement.EnumerateArray()];
        Assert.Equal(2, packages.Length);
        Assert.Equal("File", packages[0].GetProperty("source").GetString());
        Assert.Equal("NuGet", packages[1].GetProperty("source").GetString());
        Assert.Equal(
            1,
            result.Error.Split(
                "Using committed content-only package Root for inspection:",
                StringSplitOptions.None).Length - 1);
    }

    private string WritePackageSurveyFeed(
        params (string Id, byte[] Archive)[] packages)
    {
        string source = Path.Combine(_root, $"survey-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        foreach (var (id, archive) in packages)
        {
            File.WriteAllBytes(
                Path.Combine(source, $"{id}.{Version}.nupkg"),
                archive);
        }
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException(
                "A local configured survey opened an HTTP transport."));
        return source;
    }
}
