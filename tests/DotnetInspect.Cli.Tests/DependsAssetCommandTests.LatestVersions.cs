using System.Text;
using System.Text.Json;
using DotnetInspector.Sections;
using NuGet.Versioning;

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
        Assert.Equal(7, summary.GetProperty("declarations").GetInt32());
        // Contoso.Major is discovered once per prerelease policy; the
        // duplicate declaration shares the stable discovery.
        Assert.Equal(6, summary.GetProperty("packages").GetInt32());
        Assert.Equal(7, summary.GetProperty("compared").GetInt32());
        Assert.Equal(6, summary.GetProperty("behind").GetInt32());

        JsonElement[] rows =
        [
            .. document.RootElement.GetProperty("latest_versions")
                .EnumerateArray(),
        ];
        Assert.Equal(7, rows.Length);
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
        WriteLocalSourcePackage(source, "Contoso.Direct", "1.5.0", "");
        string assets = WriteTemporaryFile(
            "project.assets.json",
            ProjectAssetsDocument());
        string manifest = WriteTemporaryFile(
            "lower-bound.nuspec",
            Manifest(
                "Contoso.Manifest",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="Contoso.Direct" version="[0.5.0, )" />
                  <dependency id="Contoso.Open" version="(, 2.0.0]" />
                </group>
                """));

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
        JsonElement restored = Row(rows, 1, "contoso.direct");
        Assert.Equal("1.0.0", restored.GetProperty("current").GetString());
        Assert.Equal(
            "Resolved",
            restored.GetProperty("current_basis").GetString());
        Assert.Equal("1.5.0", restored.GetProperty("latest").GetString());

        // The unlisted lower bound is the declared minimum, not a restore claim.
        JsonElement lowerBound = Row(rows, 2, "contoso.direct");
        Assert.Equal("0.5.0", lowerBound.GetProperty("current").GetString());
        Assert.Equal(
            "LowerBound",
            lowerBound.GetProperty("current_basis").GetString());
        Assert.Equal("1.5.0", lowerBound.GetProperty("latest").GetString());
        Assert.Equal("Major", lowerBound.GetProperty("behind").GetString());

        JsonElement open = Row(rows, 2, "contoso.open");
        Assert.Equal(
            "NoCurrentVersion",
            open.GetProperty("state").GetString());
        Assert.False(open.TryGetProperty("current", out _));
        Assert.False(open.TryGetProperty("latest", out _));
        Assert.Equal(
            1,
            document.RootElement.GetProperty("summary")
                .GetProperty("latest_versions")
                .GetProperty("packages")
                .GetInt32());
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
        DependencyInspectionVersionComparison comparison =
            DependencyInspectionVersionComparison.Create(
                NuGetVersion.Parse("1.5.0"),
                NuGetVersion.Parse("3.0.0"),
                [
                    NuGetVersion.Parse("1.0.0"),
                    NuGetVersion.Parse("2.0.0"),
                    NuGetVersion.Parse("2.1.0"),
                    NuGetVersion.Parse("3.0.0"),
                ]);

        // Current 1.5.0 is not listed; Latest In Major is the highest 1.x.
        Assert.Equal("1.0.0", comparison.LatestInMajor);
        Assert.Equal(DependencyInspectionVersionLag.Major, comparison.Behind);
        Assert.Equal(2, comparison.MajorsBehind);
        Assert.Null(
            DependencyInspectionLatestVersion.InclusiveMinimum("(1.0.0, )"));
        Assert.Equal(
            NuGetVersion.Parse("1.0.0"),
            DependencyInspectionLatestVersion.InclusiveMinimum("[1.0.0, )"));
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
}
