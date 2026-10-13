using System.Text;
using System.Net;
using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Networking;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public partial class DependsAssetCommandTests
{
    [Fact]
    public async Task LatestVersions_DerivesEveryFieldFromOneDiscoveryPerPolicy()
    {
        string source = CreateTemporaryDirectory();
        foreach (string version in new[]
                 {
                     "1.0.0",
                     "1.2.0",
                     "1.2.0.1",
                     "2.0.0",
                     "3.1.0",
                     "4.0.0-preview.1",
                 })
        {
            WriteLocalSourcePackage(source, "Contoso.Major", version, "");
        }
        WriteLocalSourcePackage(source, "Contoso.Minor", "1.0.0", "");
        WriteLocalSourcePackage(source, "Contoso.Minor", "1.1.0", "");
        WriteLocalSourcePackage(source, "Contoso.Patch", "1.0.0", "");
        WriteLocalSourcePackage(source, "Contoso.Patch", "1.0.1", "");
        WriteLocalSourcePackage(source, "Contoso.Revision", "1.0.0", "");
        WriteLocalSourcePackage(source, "Contoso.Revision", "1.0.0.1", "");
        WriteLocalSourcePackage(source, "Contoso.Current", "1.0.0", "");
        WriteLocalSourcePackage(source, "Contoso.Current", "2.0.0", "");
        // Prereleases inside Current's major and a prerelease-only major sit
        // outside the stable population that produces Latest.
        foreach (string version in new[]
                 {
                     "1.0.0",
                     "1.1.0",
                     "1.2.0-beta.1",
                     "2.0.0-beta.1",
                     "3.0.0",
                 })
        {
            WriteLocalSourcePackage(source, "Contoso.Beta", version, "");
        }
        foreach (string version in new[] { "1.0.0", "1.1.0", "1.2.0-beta.1" })
            WriteLocalSourcePackage(source, "Contoso.NextBeta", version, "");
        string stable = WriteTemporaryFile(
            "stable.nuspec",
            Manifest(
                "Contoso.Stable",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="Contoso.Major" version="[1.2.0, )" />
                  <dependency id="Contoso.Minor" version="[1.0.0, )" />
                  <dependency id="Contoso.Patch" version="[1.0.0, )" />
                  <dependency id="Contoso.Revision" version="[1.0.0, )" />
                  <dependency id="Contoso.Current" version="[2.0.0, )" />
                  <dependency id="Contoso.Beta" version="[1.1.0, )" />
                  <dependency id="Contoso.NextBeta" version="[1.1.0, )" />
                </group>
                """));
        string preview = WriteTemporaryFile(
            "preview.nuspec",
            Manifest(
                "Contoso.Preview",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="Contoso.Major" version="[4.0.0-preview.0, )" />
                </group>
                """));
        string duplicate = WriteTemporaryFile(
            "duplicate.nuspec",
            Manifest(
                "Contoso.Duplicate",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="Contoso.Major" version="[1.2.0, )" />
                </group>
                """));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            stable,
            "--nuspec",
            preview,
            "--nuspec",
            duplicate,
            "--source",
            source,
            "-S",
            "Latest Versions",
            "--json",
            "--compact",
        ]);

        Assert.True(
            exitCode == 0,
            $"Expected success.{Environment.NewLine}{error}{Environment.NewLine}{output}");
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement summary = document.RootElement.GetProperty("summary")
            .GetProperty("latest_versions");
        Assert.Equal("Complete", summary.GetProperty("completion").GetString());
        Assert.Equal(9, summary.GetProperty("declarations").GetInt32());
        // Contoso.Major is discovered once per prerelease policy; the
        // duplicate declaration shares the stable discovery.
        Assert.Equal(8, summary.GetProperty("packages").GetInt32());
        Assert.Equal(9, summary.GetProperty("compared").GetInt32());
        Assert.Equal(7, summary.GetProperty("behind").GetInt32());

        JsonElement[] rows =
        [
            .. document.RootElement.GetProperty("latest_versions")
                .EnumerateArray(),
        ];
        Assert.Equal(9, rows.Length);
        AssertLatest(
            Row(rows, 1, "contoso.beta"),
            "1.1.0",
            "1.1.0",
            "3.0.0",
            "Major",
            1);
        AssertLatest(
            Row(rows, 1, "contoso.nextbeta"),
            "1.1.0",
            "1.1.0",
            "1.1.0",
            "None",
            0);
        AssertLatest(
            Row(rows, 1, "contoso.major"),
            current: "1.2.0",
            latestInMajor: "1.2.0.1",
            latest: "3.1.0",
            behind: "Major",
            majorsBehind: 2);
        AssertLatest(
            Row(rows, 1, "contoso.minor"),
            "1.0.0",
            "1.1.0",
            "1.1.0",
            "Minor",
            0);
        AssertLatest(
            Row(rows, 1, "contoso.patch"),
            "1.0.0",
            "1.0.1",
            "1.0.1",
            "Patch",
            0);
        AssertLatest(
            Row(rows, 1, "contoso.revision"),
            "1.0.0",
            "1.0.0.1",
            "1.0.0.1",
            "Revision",
            0);
        AssertLatest(
            Row(rows, 1, "contoso.current"),
            "2.0.0",
            "2.0.0",
            "2.0.0",
            "None",
            0);
        AssertLatest(
            Row(rows, 2, "contoso.major"),
            "4.0.0-preview.0",
            "4.0.0-preview.1",
            "4.0.0-preview.1",
            "Prerelease",
            0);
        AssertLatest(
            Row(rows, 3, "contoso.major"),
            "1.2.0",
            "1.2.0.1",
            "3.1.0",
            "Major",
            2);
        Assert.All(
            rows,
            static row => Assert.Equal(
                "LowerBound",
                row.GetProperty("current_basis").GetString()));
    }

    [Fact]
    public async Task LatestVersions_ReportsResolvedOnlyFromRestoredAssociations()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(source, "Contoso.Direct", "1.0.0", "");
        WriteLocalSourcePackage(source, "Contoso.Direct", "1.2.0", "");
        WriteLocalSourcePackage(source, "Contoso.Direct", "1.5.0", "");
        string assets = WriteTemporaryFile(
            "project.assets.json",
            RestoredAboveLowerBoundDocument());
        // The same constraint as the restored project, plus an unlisted lower
        // bound and an open lower bound.
        string manifest = WriteTemporaryFile(
            "lower-bound.nuspec",
            Manifest(
                "Contoso.Manifest",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="Contoso.Direct" version="[1.0.0, )" />
                  <dependency id="Contoso.Unlisted" version="[0.5.0, )" />
                  <dependency id="Contoso.Open" version="(, 2.0.0]" />
                </group>
                """));
        WriteLocalSourcePackage(source, "Contoso.Unlisted", "1.0.0", "");

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            assets,
            "--nuspec",
            manifest,
            "--source",
            source,
            "-S",
            "Latest Versions",
            "--json",
            "--compact",
        ]);

        Assert.True(
            exitCode == 0,
            $"Expected success.{Environment.NewLine}{error}{Environment.NewLine}{output}");
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
        [
            .. document.RootElement.GetProperty("latest_versions")
                .EnumerateArray(),
        ];
        // Restore selected 1.2.0 above the declared minimum.
        JsonElement restored = Row(rows, 1, "contoso.direct");
        Assert.Equal("[1.0.0, )", restored.GetProperty("requested").GetString());
        Assert.Equal("1.2.0", restored.GetProperty("current").GetString());
        Assert.Equal(
            "Resolved",
            restored.GetProperty("current_basis").GetString());
        Assert.Equal("1.5.0", restored.GetProperty("latest").GetString());
        Assert.Equal("Minor", restored.GetProperty("behind").GetString());

        // The same constraint without a restored graph reports its minimum.
        JsonElement declared = Row(rows, 2, "contoso.direct");
        Assert.Equal("[1.0.0, )", declared.GetProperty("requested").GetString());
        Assert.Equal("1.0.0", declared.GetProperty("current").GetString());
        Assert.Equal(
            "LowerBound",
            declared.GetProperty("current_basis").GetString());

        // An unlisted lower bound is the declared minimum, not a restore claim.
        JsonElement unlisted = Row(rows, 2, "contoso.unlisted");
        Assert.Equal("0.5.0", unlisted.GetProperty("current").GetString());
        Assert.Equal(
            "LowerBound",
            unlisted.GetProperty("current_basis").GetString());
        Assert.Equal("1.0.0", unlisted.GetProperty("latest").GetString());
        Assert.Equal("Major", unlisted.GetProperty("behind").GetString());

        JsonElement open = Row(rows, 2, "contoso.open");
        Assert.Equal(
            "NoCurrentVersion",
            open.GetProperty("state").GetString());
        Assert.False(open.TryGetProperty("current", out _));
        Assert.False(open.TryGetProperty("latest", out _));
        Assert.Equal(
            2,
            document.RootElement.GetProperty("summary")
                .GetProperty("latest_versions")
                .GetProperty("packages")
                .GetInt32());
    }

    [Fact]
    public async Task LatestVersions_RowWindowRetainsFailures()
    {
        string source = CreateTemporaryDirectory();
        string manifest = WriteTemporaryFile(
            "window.nuspec",
            Manifest(
                "Contoso.Window",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="Contoso.Missing" version="[1.0.0, )" />
                </group>
                """));
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Nuspec,
                    manifest),
            ],
            Select =
            [
                DependsAssetSections.LatestVersions,
                DependsAssetSections.Failures,
            ],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            Rows = RowWindow.Head(0),
            LineWindowExplicitlySet = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ => throw new InvalidOperationException(
                        "Inventory must not be read."),
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Latest version discovery completed as Partial",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Empty(
            document.RootElement.GetProperty("latest_versions")
                .EnumerateArray());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray());
        Assert.Equal("LatestVersion", failure.GetProperty("phase").GetString());
    }

    [Fact]
    public async Task LatestVersions_UnauthorizedAndIncompleteDiscoveryFail()
    {
        string manifest = WriteTemporaryFile(
            "feeds.nuspec",
            Manifest(
                "Contoso.Feeds",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="Contoso.Present" version="[1.0.0, )" />
                  <dependency id="Contoso.Locked" version="[1.0.0, )" />
                  <dependency id="Contoso.Partial" version="[1.0.0, )" />
                </group>
                """));
        string[] select =
        [
            "depends",
            "--nuspec",
            manifest,
            "--source",
            "https://primary.feed.test/v3/index.json",
            "--source",
            "https://secondary.feed.test/v3/index.json",
        ];

        (int exitCode, string output, string error) =
            await RunCapturedWithScriptedFeedsAsync(
            [
                .. select,
                "-S",
                "Latest Versions,Failures",
                "--json",
                "--compact",
            ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Latest version discovery completed as Partial",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
        [
            .. document.RootElement.GetProperty("latest_versions")
                .EnumerateArray(),
        ];
        JsonElement present = Row(rows, 1, "contoso.present");
        Assert.Equal("Compared", present.GetProperty("state").GetString());
        Assert.Equal("2.0.0", present.GetProperty("latest").GetString());
        // The feed serves no archive, so the House cannot answer
        // compatibility; the row's discovery State is unchanged.
        Assert.Equal(
            "Unavailable",
            present.GetProperty("latest_compatible").GetString());
        JsonElement locked = Row(rows, 1, "contoso.locked");
        Assert.Equal("DiscoveryFailed", locked.GetProperty("state").GetString());
        Assert.NotEqual(
            "Incomplete",
            locked.GetProperty("failure_kind").GetString());
        JsonElement partial = Row(rows, 1, "contoso.partial");
        Assert.Equal("DiscoveryFailed", partial.GetProperty("state").GetString());
        Assert.Equal(
            "Incomplete",
            partial.GetProperty("failure_kind").GetString());
        Assert.False(partial.TryGetProperty("latest", out _));
        Assert.Equal(
            ["contoso.locked@", "contoso.partial@", "contoso.present@2.0.0"],
            document.RootElement.GetProperty("failures")
                .EnumerateArray()
                .Select(static failure =>
                {
                    JsonElement evidence = failure.GetProperty("evidence");
                    string? version = evidence.TryGetProperty(
                        "package_version",
                        out JsonElement value)
                        ? value.GetString()
                        : null;
                    return $"{evidence.GetProperty("package_id").GetString()}@{version}";
                })
                .Order());

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedWithScriptedFeedsAsync(
                [.. select, "-S", "Latest Versions", "--count"]);
        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Latest Versions' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LatestVersions_DiscoveryFailureStaysVisiblePerPackage()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(source, "Contoso.Present", "1.0.0", "");
        WriteLocalSourcePackage(source, "Contoso.Present", "2.0.0", "");
        WriteLocalSourcePackage(source, "Contoso.Preview", "2.0.0-beta.1", "");
        string manifest = WriteTemporaryFile(
            "failure.nuspec",
            Manifest(
                "Contoso.Failure",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="Contoso.Present" version="[1.0.0, )" />
                  <dependency id="Contoso.Missing" version="[1.0.0, )" />
                  <dependency id="Contoso.Preview" version="[1.0.0, )" />
                </group>
                """));
        string[] args =
        [
            "depends",
            "--nuspec",
            manifest,
            "--source",
            source,
            "-S",
            "Latest Versions,Failures",
            "--json",
            "--compact",
        ];

        (int exitCode, string output, string error) =
            await RunCapturedAsync(args);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Latest version discovery completed as Partial",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
        [
            .. document.RootElement.GetProperty("latest_versions")
                .EnumerateArray(),
        ];
        Assert.Equal(
            "Compared",
            Row(rows, 1, "contoso.present").GetProperty("state").GetString());
        JsonElement missing = Row(rows, 1, "contoso.missing");
        Assert.Equal(
            "DiscoveryFailed",
            missing.GetProperty("state").GetString());
        Assert.Equal(
            "NotFound",
            missing.GetProperty("failure_kind").GetString());
        Assert.Equal(
            "NoListedVersion",
            Row(rows, 1, "contoso.preview").GetProperty("state").GetString());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray());
        Assert.Equal(
            "LatestVersion",
            failure.GetProperty("phase").GetString());
        Assert.Equal(
            "contoso.missing",
            failure.GetProperty("evidence")
                .GetProperty("package_id")
                .GetString());
        JsonElement summary = document.RootElement.GetProperty("summary")
            .GetProperty("latest_versions");
        Assert.Equal("Partial", summary.GetProperty("completion").GetString());
        Assert.Equal(1, summary.GetProperty("discovery_failed").GetInt32());
        Assert.Equal(1, summary.GetProperty("no_listed_version").GetInt32());

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                manifest,
                "--source",
                source,
                "-S",
                "Latest Versions",
                "--count",
            ]);
        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Latest Versions' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LatestVersions_RejectsOfflineAndPackagePrefixBeforeDiscovery()
    {
        string manifest = WriteTemporaryFile(
            "offline.nuspec",
            Manifest(
                "Contoso.Offline",
                "1.0.0",
                Dependency("Contoso.Child", "[1.0.0, )")));

        (int offlineExitCode, string offlineOutput, string offlineError) =
            await RunCapturedOfflineAsync(
            [
                "depends",
                "--nuspec",
                manifest,
                "-S",
                "Latest Versions",
            ]);
        Assert.Equal(1, offlineExitCode);
        Assert.Empty(offlineOutput);
        Assert.Contains("--offline", offlineError, StringComparison.Ordinal);

        (int prefixExitCode, string prefixOutput, string prefixError) =
            await RunCapturedAsync(
            [
                "depends",
                "--package-prefix",
                "Contoso.",
                "-S",
                "Latest Versions",
            ]);
        Assert.Equal(1, prefixExitCode);
        Assert.Empty(prefixOutput);
        Assert.Contains(
            "--package-prefix",
            prefixError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LatestVersions_LocalPackageConsumesSourceOptions()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(source, "Contoso.Child", "1.0.0", "");
        WriteLocalSourcePackage(source, "Contoso.Child", "1.1.0", "");
        string packages = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            packages,
            "Contoso.Local",
            "1.0.0",
            Dependency("Contoso.Child", "[1.0.0, )"));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--package",
            Path.Combine(packages, "Contoso.Local.1.0.0.nupkg"),
            "--source",
            source,
            "-S",
            "Latest Versions",
            "--json",
            "--compact",
        ]);

        Assert.True(
            exitCode == 0,
            $"Expected success.{Environment.NewLine}{error}{Environment.NewLine}{output}");
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row = Assert.Single(
            document.RootElement.GetProperty("latest_versions")
                .EnumerateArray());
        Assert.Equal("1.1.0", row.GetProperty("latest").GetString());
        Assert.Equal("Minor", row.GetProperty("behind").GetString());
    }

    [Fact]
    public void LatestVersions_ComparisonUsesTheOneCandidatePopulation()
    {
        Assert.True(PackageVersionRange.TryParse(
            "contoso@1.0.0..3.0.0",
            out PackageVersionRange? range,
            out _));
        PackageVersionComparison comparison =
            PackageVersionComparison.Create(
                NuGetVersion.Parse("1.5.0"),
                NuGetVersion.Parse("3.0.0"),
                PackageVersionVector.Create(
                    range!,
                    ["1.0.0", "2.0.0", "2.1.0", "3.0.0"]));

        // Current 1.5.0 is not listed; Latest In Major is the highest 1.x.
        Assert.Equal("1.0.0", comparison.LatestInMajor);
        Assert.Equal(PackageVersionLag.Major, comparison.Behind);
        Assert.Equal(2, comparison.MajorsBehind);
        Assert.Null(
            PackageVersionComparison.InclusiveMinimum("(1.0.0, )"));
        Assert.Equal(
            NuGetVersion.Parse("1.0.0"),
            PackageVersionComparison.InclusiveMinimum("[1.0.0, )"));
    }

    private static JsonElement Row(
        JsonElement[] rows,
        int root,
        string package) =>
        Assert.Single(
            rows,
            row => row.GetProperty("root").GetInt32() == root
                && row.GetProperty("package").GetString() == package);

    private static void AssertLatest(
        JsonElement row,
        string current,
        string latestInMajor,
        string latest,
        string behind,
        int majorsBehind)
    {
        Assert.Equal("Compared", row.GetProperty("state").GetString());
        Assert.Equal(current, row.GetProperty("current").GetString());
        Assert.Equal(
            latestInMajor,
            row.GetProperty("latest_in_major").GetString());
        Assert.Equal(latest, row.GetProperty("latest").GetString());
        Assert.Equal(behind, row.GetProperty("behind").GetString());
        Assert.Equal(
            majorsBehind,
            row.GetProperty("majors_behind").GetInt32());
    }

    private static byte[] RestoredAboveLowerBoundDocument() =>
        Encoding.UTF8.GetBytes(
            new System.Text.Json.Nodes.JsonObject
            {
                ["version"] = 4,
                ["targets"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["net11.0"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["Contoso.Direct/1.2.0"] =
                            new System.Text.Json.Nodes.JsonObject
                            {
                                ["type"] = "package",
                            },
                    },
                },
                ["projectFileDependencyGroups"] =
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["net11.0"] = new System.Text.Json.Nodes.JsonArray(
                            "Contoso.Direct >= 1.0.0"),
                    },
                ["project"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["frameworks"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["net11.0"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["dependencies"] =
                                new System.Text.Json.Nodes.JsonObject
                                {
                                    ["Contoso.Direct"] =
                                        new System.Text.Json.Nodes.JsonObject
                                        {
                                            ["target"] = "Package",
                                            ["version"] = "[1.0.0, )",
                                        },
                                },
                        },
                    },
                },
            }.ToJsonString());

    private static async Task<(int ExitCode, string Output, string Error)>
        RunCapturedWithScriptedFeedsAsync(string[] args)
    {
        DotnetInspector.Networking.HttpClientFactory.Initialize(
            new HttpClientFactoryOptions());
        DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
        DotnetInspector.Networking.HttpClientFactory
            .SetPackageSourceHandlerForTesting(
                _ => new ScriptedFeedHandler());
        try
        {
            return await ConsoleCapture.RunAsync(() => RunAsync(args));
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory
                .SetPackageSourceHandlerForTesting(null);
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions());
            DotnetInspector.Networking.HttpClientFactory
                .ResetSharedForTesting();
        }
    }

    /// <summary>
    /// Two feeds: both list contoso.present; both refuse contoso.locked;
    /// only the primary answers for contoso.partial while the secondary fails.
    /// </summary>
    private sealed class ScriptedFeedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            string host = uri.Host;
            string path = uri.AbsolutePath;
            (HttpStatusCode status, string body) = path switch
            {
                "/v3/index.json" => (
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "version": "3.0.0",
                      "resources": [
                        {
                          "@id": "https://{{host}}/flat/",
                          "@type": "PackageBaseAddress/3.0.0"
                        }
                      ]
                    }
                    """),
                "/flat/contoso.present/index.json" => (
                    HttpStatusCode.OK,
                    """{"versions":["1.0.0","2.0.0"]}"""),
                "/flat/contoso.locked/index.json" => (
                    HttpStatusCode.Unauthorized,
                    ""),
                "/flat/contoso.partial/index.json"
                    when host.StartsWith(
                        "primary.",
                        StringComparison.Ordinal) => (
                    HttpStatusCode.OK,
                    """{"versions":["1.0.0","3.0.0"]}"""),
                "/flat/contoso.partial/index.json" => (
                    HttpStatusCode.InternalServerError,
                    ""),
                _ => (HttpStatusCode.NotFound, ""),
            };
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
                RequestMessage = request,
            });
        }
    }
}
