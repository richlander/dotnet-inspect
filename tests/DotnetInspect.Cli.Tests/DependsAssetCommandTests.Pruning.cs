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
    [Fact]
    public async Task Pruning_ProjectsDelegationAndPackageRetentionWithoutTraversal()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Pruning.Root",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="System.Text.Json" version="[9.0.0]" />
              <dependency id="System.Runtime" version="[4.3.2]" />
            </group>
            """);
        WriteLocalSourcePackage(
            source,
            "System.Text.Json",
            "9.0.0",
            "");
        WriteLocalSourcePackage(
            source,
            "System.Runtime",
            "4.3.2",
            "");
        int inventoryReads = 0;
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Pruning.Root@1.0.0"),
            ],
            Tfm = "net11.0",
            PruningPlatformFamily = "RUNTIME",
            Select = [DependsAssetSections.Pruning],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    frameworkSpec =>
                    {
                        inventoryReads++;
                        Assert.Equal("runtime@11.0", frameworkSpec);
                        return PruneInventory(
                            "System.Text.Json|11.0.0",
                            "System.Runtime|4.3.1");
                    },
                    TestContext.Current.CancellationToken));

        Assert.True(
            exitCode == 0,
            $"Expected success.{Environment.NewLine}{error}{Environment.NewLine}{output}");
        Assert.Empty(error);
        Assert.Equal(1, inventoryReads);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement pruningSummary =
            document.RootElement.GetProperty("summary")
                .GetProperty("pruning");
        Assert.Equal(
            "Complete",
            pruningSummary.GetProperty("completion").GetString());
        Assert.Equal(2, pruningSummary.GetProperty("evaluated").GetInt32());
        Assert.Equal(1, pruningSummary.GetProperty("delegated").GetInt32());
        Assert.Equal(1, pruningSummary.GetProperty("retained").GetInt32());

        JsonElement[] rows =
        [
            .. document.RootElement.GetProperty("pruning")
                .EnumerateArray(),
        ];
        JsonElement delegated = Assert.Single(rows, row =>
            row.GetProperty("package_id").GetString()
                == "system.text.json");
        Assert.Equal(
            "PlatformDelegation",
            delegated.GetProperty("disposition").GetString());
        Assert.Equal(
            "9.0.0",
            delegated.GetProperty("candidate_version").GetString());
        Assert.Equal(
            "11.0.0",
            delegated.GetProperty("platform_provided_version")
                .GetString());
        Assert.True(
            delegated.GetProperty("delegates_to_platform").GetBoolean());
        Assert.Equal(
            "Resolved",
            delegated.GetProperty("candidate")
                .GetProperty("kind")
                .GetString());
        Assert.Equal(
            "9.0.0",
            delegated.GetProperty("resolved_candidate")
                .GetProperty("coordinate")
                .GetProperty("version")
                .GetString());

        JsonElement retained = Assert.Single(rows, row =>
            row.GetProperty("package_id").GetString()
                == "system.runtime");
        Assert.Equal(
            "PackageRetained",
            retained.GetProperty("disposition").GetString());
        Assert.Equal(
            "4.3.2",
            retained.GetProperty("candidate_version").GetString());
        Assert.Equal(
            "4.3.1",
            retained.GetProperty("platform_provided_version")
                .GetString());
        Assert.False(
            retained.GetProperty("delegates_to_platform").GetBoolean());
        Assert.False(
            document.RootElement.TryGetProperty(
                "dependency_hierarchy",
                out _));
    }

    [Fact]
    public async Task Pruning_ExplainsApplicationAuthorshipWithoutInventoryWork()
    {
        int inventoryReads = 0;
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Project,
                    AssetsFixture),
            ],
            Tfm = "net11.0",
            Select = [DependsAssetSections.Pruning],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ =>
                    {
                        inventoryReads++;
                        throw new InvalidOperationException(
                            "Inventory must not be read.");
                    },
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(0, inventoryReads);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
        [
            .. document.RootElement.GetProperty("pruning")
                .EnumerateArray(),
        ];
        Assert.NotEmpty(rows);
        Assert.All(
            rows,
            row =>
            {
                Assert.Equal(
                    "NotEvaluated",
                    row.GetProperty("disposition").GetString());
                Assert.Equal(
                    "ApplicationAuthoredExemption",
                    row.GetProperty("applicability").GetString());
            });
    }

    [Fact]
    public async Task Pruning_UsesTheSelectedAspNetCoreInventory()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.AspNetCore.Pruning",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="Microsoft.AspNetCore.Authentication" version="[10.0.0]" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.AspNetCore.Pruning@1.0.0"),
            ],
            Tfm = "net11.0",
            PruningPlatformFamily = "ASPNETCORE",
            Select = [DependsAssetSections.Pruning],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    frameworkSpec =>
                    {
                        Assert.Equal("aspnetcore@11.0", frameworkSpec);
                        return PruneInventoryForFamily(
                            "Microsoft.AspNetCore.App",
                            "net11.0",
                            "Microsoft.AspNetCore.Authentication|11.0.0");
                    },
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row = Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        Assert.Equal(
            "AspNetCore",
            row.GetProperty("platform_family").GetString());
        Assert.Equal(
            "PlatformDelegation",
            row.GetProperty("disposition").GetString());
    }

    [Fact]
    public async Task Pruning_HumanFormatsPreservePolicyRows()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Pruning.Render",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="System.Text.Json" version="[9.0.0]" />
              <dependency id="System.Runtime" version="[4.3.2]" />
            </group>
            """);

        foreach (OutputFormat format in new[]
                 {
                     OutputFormat.Markdown,
                     OutputFormat.Table,
                 })
        {
            var options = new DependsOptions
            {
                AssetRoots =
                [
                    new DependsAssetRoot(
                        1,
                        DependencyInspectionRootKind.Package,
                        "Contoso.Pruning.Render@1.0.0"),
                ],
                Tfm = "net11.0",
                Select = [DependsAssetSections.Pruning],
                Format = format,
                SourceOptions = new NuGetSourceOptions
                {
                    Sources = [source],
                },
            };

            (int exitCode, string output, string error) =
                await ConsoleCapture.RunAsync(
                    () => DependsCommand.ExecuteAssetDependsAsync(
                        options,
                        _ => PruneInventory(
                            "System.Text.Json|11.0.0",
                            "System.Runtime|4.3.1"),
                        TestContext.Current.CancellationToken));

            Assert.Equal(0, exitCode);
            Assert.Empty(error);
            Assert.Contains(
                "System.Text.Json",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "System.Runtime",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "PlatformDelegation",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "PackageRetained",
                output,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Pruning_RowWindowDoesNotChangeSummaryOrExitStatus()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Pruning.Window",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="System.Text.Json" version="[9.0.0]" />
              <dependency id="System.Runtime" version="[4.3.2]" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Pruning.Window@1.0.0"),
            ],
            Tfm = "net11.0",
            Select = [DependsAssetSections.Pruning],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            Rows = RowWindow.Head(1),
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
                    _ => PruneInventory(
                        "System.Text.Json|11.0.0",
                        "System.Runtime|4.3.1"),
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            2,
            document.RootElement.GetProperty("summary")
                .GetProperty("pruning")
                .GetProperty("evaluated")
                .GetInt32());
        Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
    }

    [Fact]
    public async Task Pruning_RowWindowRetainsFailures()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Pruning.WindowFailure",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="Contoso.Missing" version="[1.0.0,2.0.0)" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Pruning.WindowFailure@1.0.0"),
            ],
            Tfm = "net11.0",
            Select =
            [
                DependsAssetSections.Pruning,
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
                    _ => PruneInventory(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Package pruning evidence completed as Failed",
            error,
            StringComparison.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            Assert.Empty(
                document.RootElement.GetProperty("pruning")
                    .EnumerateArray());
            JsonElement failure = Assert.Single(
                document.RootElement.GetProperty("failures")
                    .EnumerateArray());
            Assert.Equal(
                "Pruning",
                failure.GetProperty("phase").GetString());
        }
    }

    [Fact]
    public async Task Pruning_IncompleteDeclarationsCannotProduceAnExactCount()
    {
        string path = WriteTemporaryFile(
            "conflicting-pruning.nuspec",
            Manifest(
                "Contoso.Conflicting",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="System.Runtime" version="[4.3.1]" />
                  <dependency id="System.Runtime" version="[4.3.2]" />
                </group>
                """));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            path,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            JsonElement summary = document.RootElement
                .GetProperty("summary");
            Assert.Equal(
                "Partial",
                summary.GetProperty("declaration_completion").GetString());
            JsonElement pruning = summary.GetProperty("pruning");
            Assert.Equal(
                "Failed",
                pruning.GetProperty("completion").GetString());
            Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
        }

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                path,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_FailedRootCannotProduceAnExactCount()
    {
        string missing = Path.Combine(
            CreateTemporaryDirectory(),
            "missing.nuspec");

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            missing,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            JsonElement pruning = document.RootElement
                .GetProperty("summary")
                .GetProperty("pruning");
            Assert.Equal(
                "Failed",
                pruning.GetProperty("completion").GetString());
            Assert.Equal(1, pruning.GetProperty("roots").GetInt32());
            Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
            Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
        }

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                missing,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_FailedRootKeepsSuccessfulSiblingPartial()
    {
        string directory = CreateTemporaryDirectory();
        string valid = WriteTemporaryFile(
            "valid-pruning.nuspec",
            Manifest(
                "Contoso.Valid",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="System.Runtime" version="[4.3.2]" />
                </group>
                """));
        string missing = Path.Combine(directory, "missing.nuspec");

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            valid,
            "--nuspec",
            missing,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement pruning = document.RootElement
            .GetProperty("summary")
            .GetProperty("pruning");
        Assert.Equal(
            "Partial",
            pruning.GetProperty("completion").GetString());
        Assert.Equal(2, pruning.GetProperty("roots").GetInt32());
        Assert.Equal(1, pruning.GetProperty("declarations").GetInt32());
        Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
        Assert.Equal(1, pruning.GetProperty("source_bounded").GetInt32());
        Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
        Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
    }

    [Fact]
    public async Task Pruning_FailedLibraryCannotProduceAnExactCount()
    {
        string missing = Path.Combine(
            CreateTemporaryDirectory(),
            "missing.dll");

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            missing,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            JsonElement pruning = document.RootElement
                .GetProperty("summary")
                .GetProperty("pruning");
            Assert.Equal(
                "Failed",
                pruning.GetProperty("completion").GetString());
            Assert.Equal(1, pruning.GetProperty("roots").GetInt32());
            Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
            Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
            JsonElement failure = Assert.Single(
                document.RootElement.GetProperty("failures")
                    .EnumerateArray());
            Assert.Equal("Root", failure.GetProperty("phase").GetString());
        }

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--library",
                missing,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_AdmittedLibraryIsSuccessfulAndNotApplicable()
    {
        string library = typeof(DependsAssetCommandTests).Assembly.Location;

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            library,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            JsonElement pruning = document.RootElement
                .GetProperty("summary")
                .GetProperty("pruning");
            Assert.Equal(
                "Complete",
                pruning.GetProperty("completion").GetString());
            Assert.Equal(1, pruning.GetProperty("roots").GetInt32());
            Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
            Assert.Equal(0, pruning.GetProperty("failed").GetInt32());
            Assert.Empty(
                document.RootElement.GetProperty("pruning")
                    .EnumerateArray());
        }

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--library",
                library,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(0, countExitCode);
        Assert.Equal("0\n", countOutput);
        Assert.Empty(countError);
    }

    [Fact]
    public async Task Pruning_FailedLibraryKeepsSuccessfulSiblingPartial()
    {
        string missing = Path.Combine(
            CreateTemporaryDirectory(),
            "missing.dll");
        string valid = WriteTemporaryFile(
            "valid-pruning.nuspec",
            Manifest(
                "Contoso.Valid",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="System.Runtime" version="[4.3.2]" />
                </group>
                """));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            missing,
            "--nuspec",
            valid,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement pruning = document.RootElement
            .GetProperty("summary")
            .GetProperty("pruning");
        Assert.Equal(
            "Partial",
            pruning.GetProperty("completion").GetString());
        Assert.Equal(2, pruning.GetProperty("roots").GetInt32());
        Assert.Equal(1, pruning.GetProperty("declarations").GetInt32());
        Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
        Assert.Equal(1, pruning.GetProperty("source_bounded").GetInt32());
        Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
        Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures")
                .EnumerateArray());
        Assert.Equal("Root", failure.GetProperty("phase").GetString());

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--library",
                missing,
                "--nuspec",
                valid,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_UnavailableDeclarationsCannotProduceAnExactCount()
    {
        string path = WriteTemporaryFile(
            "project.assets.json",
            Encoding.UTF8.GetBytes("""{"version":3}"""));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            path,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Dependency declaration evidence completed",
            error,
            StringComparison.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(output))
        {
            JsonElement pruning = document.RootElement
                .GetProperty("summary")
                .GetProperty("pruning");
            Assert.Equal(
                "Failed",
                pruning.GetProperty("completion").GetString());
            Assert.Equal(1, pruning.GetProperty("roots").GetInt32());
            Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
            Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
            JsonElement failure = Assert.Single(
                document.RootElement.GetProperty("failures")
                    .EnumerateArray());
            Assert.Equal("Pruning", failure.GetProperty("phase").GetString());
            JsonElement prerequisite = failure.GetProperty("pruning");
            Assert.Equal(
                "Prerequisite",
                prerequisite.GetProperty("kind").GetString());
            Assert.Equal(1, prerequisite.GetProperty("root").GetInt32());
            Assert.Equal(
                "Unavailable",
                prerequisite.GetProperty("declaration_state").GetString());
            Assert.True(prerequisite.TryGetProperty("root_identity", out _));
        }

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                path,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_UnavailableDeclarationsKeepSuccessfulSiblingPartial()
    {
        string assets = WriteTemporaryFile(
            "project.assets.json",
            Encoding.UTF8.GetBytes("""{"version":3}"""));
        string valid = WriteTemporaryFile(
            "valid-pruning.nuspec",
            Manifest(
                "Contoso.Valid",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="System.Runtime" version="[4.3.2]" />
                </group>
                """));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            assets,
            "--nuspec",
            valid,
            "--tfm",
            "net11.0",
            "-S",
            "Pruning,Failures",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Dependency declaration evidence completed",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement pruning = document.RootElement
            .GetProperty("summary")
            .GetProperty("pruning");
        Assert.Equal(
            "Partial",
            pruning.GetProperty("completion").GetString());
        Assert.Equal(2, pruning.GetProperty("roots").GetInt32());
        Assert.Equal(1, pruning.GetProperty("declarations").GetInt32());
        Assert.Equal(1, pruning.GetProperty("not_evaluated").GetInt32());
        Assert.Equal(1, pruning.GetProperty("source_bounded").GetInt32());
        Assert.Equal(1, pruning.GetProperty("failed").GetInt32());
        Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures")
                .EnumerateArray());
        Assert.Equal("Pruning", failure.GetProperty("phase").GetString());
        JsonElement prerequisite = failure.GetProperty("pruning");
        Assert.Equal(
            "Prerequisite",
            prerequisite.GetProperty("kind").GetString());
        Assert.Equal(1, prerequisite.GetProperty("root").GetInt32());
        Assert.Equal(
            "Unavailable",
            prerequisite.GetProperty("declaration_state").GetString());

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                assets,
                "--nuspec",
                valid,
                "--tfm",
                "net11.0",
                "-S",
                "Pruning",
                "--count",
            ]);

        Assert.Equal(1, countExitCode);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Pruning' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DependenciesSelectionDoesNotReadPruningInventory()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Dependencies.Only",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="System.Text.Json" version="[9.0.0]" />
            </group>
            """);
        int inventoryReads = 0;
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Dependencies.Only@1.0.0"),
            ],
            Tfm = "net11.0",
            Select = [DependsAssetSections.Dependencies],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ =>
                    {
                        inventoryReads++;
                        throw new InvalidOperationException(
                            "Inventory must not be read.");
                    },
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(0, inventoryReads);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "NotRequested",
            document.RootElement.GetProperty("summary")
                .GetProperty("pruning")
                .GetProperty("completion")
                .GetString());
        Assert.False(
            document.RootElement.TryGetProperty("pruning", out _));
    }

    [Fact]
    public async Task Pruning_DirectNuspecRemainsSourceBounded()
    {
        int inventoryReads = 0;
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Nuspec,
                    NuspecFixture),
            ],
            Tfm = "net11.0",
            Select = [DependsAssetSections.Pruning],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ =>
                    {
                        inventoryReads++;
                        throw new InvalidOperationException(
                            "Inventory must not be read.");
                    },
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(0, inventoryReads);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "SourceBounded",
            document.RootElement.GetProperty("summary")
                .GetProperty("pruning")
                .GetProperty("completion")
                .GetString());
        Assert.All(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray(),
            row => Assert.Equal(
                "SourceBounded",
                row.GetProperty("disposition").GetString()));
    }

    [Fact]
    public async Task Pruning_InventoryFailurePreventsCandidateResolution()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Inventory.Root",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="Contoso.Missing.Child" version="[1.0.0]" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Inventory.Root@1.0.0"),
            ],
            Tfm = "net11.0",
            Select =
            [
                DependsAssetSections.Pruning,
                DependsAssetSections.Failures,
            ],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ => new InstalledPlatformPruneSource.Result(
                        null,
                        "The requested installed inventory is unavailable."),
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Package pruning evidence completed as Failed",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row = Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        Assert.Equal(
            "InventoryUnavailable",
            row.GetProperty("disposition").GetString());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures")
                .EnumerateArray());
        Assert.Equal(
            "Pruning",
            failure.GetProperty("phase").GetString());
        Assert.Equal(
            "Inventory",
            failure.GetProperty("pruning")
                .GetProperty("kind")
                .GetString());
    }

    [Fact]
    public async Task Pruning_InventoryTargetMismatchIsAVisibleFailure()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Inventory.Root",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="Contoso.Inventory.Child" version="[1.0.0]" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Inventory.Root@1.0.0"),
            ],
            Tfm = "net11.0",
            Select =
            [
                DependsAssetSections.Pruning,
                DependsAssetSections.Failures,
            ],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ => PruneInventoryFor(
                        "net10.0",
                        "Contoso.Inventory.Child|1.0.0"),
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Package pruning evidence completed as Failed",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row = Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        Assert.Equal(
            "InventoryUnavailable",
            row.GetProperty("disposition").GetString());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures")
                .EnumerateArray());
        Assert.Contains(
            "describes 'net10.0', not requested target 'net11.0'",
            failure.GetProperty("pruning")
                .GetProperty("message")
                .GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pruning_CandidateFailureRemainsTypedAndNonzero()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Candidate.Root",
            "1.0.0",
            """
            <group targetFramework="net11.0">
              <dependency id="Contoso.Missing.Candidate" version="[1.0.0,2.0.0)" />
            </group>
            """);
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    "Contoso.Candidate.Root@1.0.0"),
            ],
            Tfm = "net11.0",
            Select =
            [
                DependsAssetSections.Pruning,
                DependsAssetSections.Failures,
            ],
            Format = OutputFormat.Json,
            JsonOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [source],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => DependsCommand.ExecuteAssetDependsAsync(
                    options,
                    _ => PruneInventory(),
                    TestContext.Current.CancellationToken));

        Assert.True(
            exitCode == 1,
            $"Expected failure.{Environment.NewLine}{error}{Environment.NewLine}{output}");
        Assert.Contains(
            "Package pruning evidence completed as Failed",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row = Assert.Single(
            document.RootElement.GetProperty("pruning")
                .EnumerateArray());
        Assert.Equal(
            "CandidateUnavailable",
            row.GetProperty("disposition").GetString());
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures")
                .EnumerateArray());
        JsonElement pruning = failure.GetProperty("pruning");
        Assert.Equal("Candidate", pruning.GetProperty("kind").GetString());
        Assert.Equal(
            "Failed",
            pruning.GetProperty("candidate_state").GetString());
        Assert.Equal(
            "NoMatchingVersion",
            row.GetProperty("candidate")
                .GetProperty("kind")
                .GetString());
        Assert.Equal(
            "NoMatchingVersion",
            pruning.GetProperty("candidate")
                .GetProperty("kind")
                .GetString());
    }

    [Fact]
    public async Task Pruning_RequiresTargetAndOwnsPlatformFamilyGesture()
    {
        (int missingTfmExit, _, string missingTfmError) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                NuspecFixture,
                "-S",
                DependsAssetSections.Pruning,
            ]);
        (int unusedFamilyExit, _, string unusedFamilyError) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                NuspecFixture,
                "--platform-family",
                "aspnetcore",
                "-S",
                DependsAssetSections.Dependencies,
            ]);
        (int typeExit, _, string typeError) = await RunCapturedAsync(
        [
            "depends",
            "System.Int128",
            "--platform-family",
            "runtime",
        ]);

        Assert.Equal(1, missingTfmExit);
        Assert.Contains("requires --tfm", missingTfmError);
        Assert.Equal(1, unusedFamilyExit);
        Assert.Contains("requires the Pruning section", unusedFamilyError);
        Assert.Equal(1, typeExit);
        Assert.Contains("only by asset-mode depends", typeError);
    }

}
