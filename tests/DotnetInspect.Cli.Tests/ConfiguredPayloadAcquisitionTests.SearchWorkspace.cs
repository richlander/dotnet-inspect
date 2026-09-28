using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    [InlineData("implements")]
    [InlineData("extensions")]
    public async Task SearchCommands_QueryCommittedPackageRoot(
        string operation)
    {
        string id = $"Workspace.Search.{operation}.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = CreatePackage(
            id,
            "selected search package",
            library: assembly,
            libraryName: "Workspace.Search.dll");
        var requests = new ConcurrentQueue<string>();
        ConfigureCommandFeed(id, package, requests);

        string[] arguments = operation switch
        {
            "type" =>
            [
                "find",
                typeof(WorkspaceImplementation).FullName!,
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
                "--tips", "q",
            ],
            "member" =>
            [
                "find",
                $".{MemberSearchServiceTests.SearchTargetMemberName}",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
                "--tips", "q",
            ],
            "implements" =>
            [
                "implements",
                typeof(IWorkspaceImplementationMarker).FullName!,
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
                "--tips", "q",
            ],
            "extensions" =>
            [
                "extensions",
                typeof(ExtensionWorkspaceRoot).FullName!,
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--all",
                "--reachable",
                "--depth", "1",
                "--json",
                "--verbose",
                "--tips", "q",
            ],
            _ => throw new InvalidOperationException(
                $"Unknown search operation '{operation}'."),
        };

        var result = await RunCommandAsync(arguments);

        Assert.True(result.Exit == 0, result.Error);
        if (operation != "type")
        {
            Assert.Contains(
                "Using committed package Root for search:",
                result.Error,
                StringComparison.Ordinal);
        }
        Assert.Contains(
            $"\"source\": \"{id}\"",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            $"\"source_version\": \"{Version}\"",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            requests,
            request => request.EndsWith(
                ".nupkg",
                StringComparison.Ordinal));
        Assert.Contains(
            operation switch
            {
                "type" => nameof(WorkspaceImplementation),
                "member" =>
                    MemberSearchServiceTests.SearchTargetMemberName,
                "implements" => nameof(WorkspaceImplementation),
                "extensions" =>
                    nameof(
                        ExtensionWorkspaceMethods.WorkspaceExtension),
                _ => throw new InvalidOperationException(),
            },
            result.Output,
            StringComparison.Ordinal);
        if (operation == "extensions")
        {
            Assert.Contains(
                "\"reachable_path\": \".Reachable\"",
                result.Output,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Depends_QueryCommittedPackageRoot()
    {
        string id = $"Workspace.Depends.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = CreatePackage(
            id,
            "selected dependency package",
            library: assembly,
            libraryName: "Workspace.Depends.dll");
        var requests = new ConcurrentQueue<string>();
        ConfigureCommandFeed(id, package, requests);

        var result = await RunCommandAsync(
            [
                "depends",
                typeof(WorkspaceImplementation).FullName!,
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--rows", "1",
                "--verbose",
                "--tips", "q",
            ]);

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "Using committed package Root for search:",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            typeof(IWorkspaceImplementationMarker).FullName!,
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            requests,
            request => request.EndsWith(
                ".nupkg",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("README.md", "NoCompileAssets")]
    [InlineData("ref/net11.0/_._", "EmptyCompileGroup")]
    public async Task Depends_PackageWithoutSurfaceIsCertifiedMiss(
        string packageEntry,
        string expectedStatus)
    {
        string id =
            $"Workspace.Depends.Empty.{Guid.NewGuid():N}";
        byte[] package;
        if (expectedStatus == "EmptyCompileGroup")
        {
            byte[] fallbackAssembly = await File.ReadAllBytesAsync(
                typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
                TestContext.Current.CancellationToken);
            package = SnupkgPdbReaderTests.MakeSnupkg(
                ($"{id}.nuspec", "<package />"u8.ToArray()),
                (packageEntry, []),
                ("lib/net8.0/Fallback.dll", fallbackAssembly));
        }
        else
        {
            package = SnupkgPdbReaderTests.MakeSnupkg(
                ($"{id}.nuspec", "<package />"u8.ToArray()),
                (packageEntry, []));
        }
        ConfigureCommandFeed(id, package);

        var result = await RunCommandAsync(
            [
                "depends", "No.Such.Type",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--verbose",
                "--tips", "q",
            ]);

        Assert.Equal(1, result.Exit);
        Assert.Contains(
            "Using committed package Root for search:",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            expectedStatus,
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            "Type 'No.Such.Type' not found in the specified scope.",
            result.Error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("queryResult").GetProperty("dependency")
            .GetProperty("found").GetBoolean());
        Assert.Empty(document.RootElement.GetProperty("rowSelection").GetProperty("relationships")
            .EnumerateArray());
    }

    [Fact]
    public async Task Depends_PackageRootPreparationFailureIsVisible()
    {
        string id =
            $"Workspace.Depends.Invalid.{Guid.NewGuid():N}";
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{id}.nuspec", "<package />"u8.ToArray()),
            ("lib/net11.0/Invalid.dll", [1, 2, 3]));
        ConfigureCommandFeed(id, package);

        var result = await RunCommandAsync(
            [
                "depends", "No.Such.Type",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--verbose",
                "--tips", "q",
            ]);

        Assert.Equal(1, result.Exit);
        Assert.Contains(
            $"Could not commit package Root '{id}@{Version}'",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Type 'No.Such.Type' not found",
            result.Error,
            StringComparison.Ordinal);
        Assert.Equal("", result.Output.Trim());
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("ref/net11.0/_._")]
    public async Task Find_PackageWithoutSurfaceReturnsStableEmptyResultArray(
        string packageEntry)
    {
        string id =
            $"Workspace.Search.Empty.{Guid.NewGuid():N}";
        byte[] package;
        if (packageEntry.EndsWith("_._", StringComparison.Ordinal))
        {
            byte[] fallbackAssembly = await File.ReadAllBytesAsync(
                typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
                TestContext.Current.CancellationToken);
            package = SnupkgPdbReaderTests.MakeSnupkg(
                ($"{id}.nuspec", "<package />"u8.ToArray()),
                (packageEntry, []),
                ("lib/net8.0/Fallback.dll", fallbackAssembly));
        }
        else
        {
            package = SnupkgPdbReaderTests.MakeSnupkg(
                ($"{id}.nuspec", "<package />"u8.ToArray()),
                (packageEntry, []));
        }
        ConfigureCommandFeed(id, package);

        var result = await RunCommandAsync(
            [
                "find", "No.Such.Type",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--verbose",
                "--tips", "q",
            ]);

        Assert.Equal(0, result.Exit);
        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(result.Output);
        Assert.Equal(
            System.Text.Json.JsonValueKind.Array,
            document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateArray());
        Assert.DoesNotContain(
            "PackageAssetUnavailable",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_PackageRootPreparationFailureReturnsStableEmptyResultArray()
    {
        string id =
            $"Workspace.Search.Invalid.{Guid.NewGuid():N}";
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{id}.nuspec", "<package />"u8.ToArray()),
            ("lib/net11.0/Invalid.dll", [1, 2, 3]));
        ConfigureCommandFeed(id, package);

        var result = await RunCommandAsync(
            [
                "find", "No.Such.Type",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--verbose",
                "--tips", "q",
            ]);

        Assert.Equal(0, result.Exit);
        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(result.Output);
        Assert.Equal(
            System.Text.Json.JsonValueKind.Array,
            document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateArray());
        Assert.DoesNotContain(
            "PackageAssetUnavailable",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            id.ToLowerInvariant(),
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_PackageLocatorAcquisitionFailureIsVisible()
    {
        string id =
            $"Workspace.Search.NotFound.{Guid.NewGuid():N}";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetAuthenticationDecorator(
            _ => new NotFoundPayloadFeedHandler(
                FirstFeed,
                id,
                requests));
        CoreHttpClientFactory.ResetSharedForTesting();

        var result = await RunCommandAsync(
            [
                "find", "No.Such.Type",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Equal("[]", result.Output.Trim());
        Assert.Contains(
            id,
            result.Error,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Could not load package Root",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            requests,
            request => request.EndsWith(
                ".nupkg",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Find_ReferenceOnlyPackageUsesCompatibility()
    {
        string id =
            $"Workspace.Search.ReferenceOnly.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{id}.nuspec", "<package />"u8.ToArray()),
            ("ref/net11.0/ReferenceOnly.dll", assembly));
        ConfigureCommandFeed(id, package);

        var result = await RunCommandAsync(
            [
                "find",
                typeof(ConfiguredPayloadAcquisitionTests).FullName!,
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--tips", "q",
            ]);

        Assert.Equal(0, result.Exit);
        Assert.Equal("", result.Error);
        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(result.Output);
        System.Text.Json.JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            typeof(ConfiguredPayloadAcquisitionTests).FullName,
            row.GetProperty("full_name").GetString());
        Assert.Equal(
            Version,
            row.GetProperty("source_version").GetString());
        Assert.Equal(
            "ReferenceOnly",
            row.GetProperty("library").GetString());
    }

    [Theory]
    [InlineData("ref/net11.0/Mixed.dll")]
    [InlineData("runtimes/any/lib/net11.0/Mixed.dll")]
    public async Task Find_PackageWithAdditionalFrameworkAssemblyUsesCompatibility(
        string additionalPath)
    {
        string id =
            $"Workspace.Search.MixedLayout.{Guid.NewGuid():N}";
        byte[] libraryAssembly = await File.ReadAllBytesAsync(
            FixtureCatalog.DiffV1.AssemblyPath(),
            TestContext.Current.CancellationToken);
        byte[] additionalAssembly = await File.ReadAllBytesAsync(
            FixtureCatalog.DiffV2.AssemblyPath(),
            TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{id}.nuspec", "<package />"u8.ToArray()),
            ("lib/net11.0/Mixed.dll", libraryAssembly),
            (additionalPath, additionalAssembly));
        ConfigureCommandFeed(id, package);

        var result = await RunCommandAsync(
            [
                "find",
                "DiffFixtureSample.GenericOverloadSample",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--tips", "q",
            ]);

        Assert.Equal(0, result.Exit);
        Assert.Equal("", result.Error);
        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(result.Output);
        Assert.Equal(2, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Find_LocatorUsesCallerPackageOrder()
    {
        const string FirstVersion = "1.0.1";
        const string SecondVersion = "1.0.0";
        string id =
            $"Workspace.Search.Order.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        IReadOnlyDictionary<string, byte[]> packages =
            new Dictionary<string, byte[]>
            {
                [FirstVersion] = CreatePackage(
                    id,
                    "caller-first package",
                    version: FirstVersion,
                    library: assembly,
                    libraryName: "CallerFirst.dll"),
                [SecondVersion] = CreatePackage(
                    id,
                    "caller-second package",
                    version: SecondVersion,
                    library: assembly,
                    libraryName: "CallerSecond.dll"),
            };
        CoreHttpClientFactory.SetAuthenticationDecorator(
            _ => new MultiVersionPayloadFeedHandler(
                FirstFeed,
                id,
                packages));
        CoreHttpClientFactory.ResetSharedForTesting();

        await AssertUsesFirstVersion(
            typeof(ConfiguredPayloadAcquisitionTests).FullName!,
            limit: 1);
        await AssertUsesFirstVersion("DotnetInspect.Cli.Tests");
        await AssertUsesFirstVersion(
            "DotnetInspect.Cli.Tests."
            + "ConfiguredPayloadAcquisitionTestz");

        async Task AssertUsesFirstVersion(
            string pattern,
            int? limit = null)
        {
            var arguments = new List<string>
            {
                "find", pattern,
                "--package", $"{id}@{FirstVersion}",
                "--package", $"{id}@{SecondVersion}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--tips", "q",
            };
            if (limit is not null)
            {
                arguments.Add("-n");
                arguments.Add(limit.Value.ToString());
            }

            var result = await RunCommandAsync([.. arguments]);

            Assert.Equal(0, result.Exit);
            using System.Text.Json.JsonDocument document =
                System.Text.Json.JsonDocument.Parse(result.Output);
            System.Text.Json.JsonElement[] rows =
                [.. document.RootElement.EnumerateArray()];
            Assert.NotEmpty(rows);
            Assert.All(
                rows,
                row => Assert.Equal(
                    FirstVersion,
                    row.GetProperty("source_version").GetString()));
        }
    }

    [Fact]
    public async Task Find_LocatorUsesSelectedAssetFileNameForLibrary()
    {
        string id =
            $"Workspace.Search.AssetName.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = CreatePackage(
            id,
            "renamed locator assembly",
            library: assembly,
            libraryName: "Renamed.dll");
        ConfigureCommandFeed(id, package);

        var result = await RunCommandAsync(
            [
                "find",
                typeof(ConfiguredPayloadAcquisitionTests).FullName!,
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--tips", "q",
            ]);

        Assert.Equal(0, result.Exit);
        Assert.Equal("", result.Error);
        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(result.Output);
        System.Text.Json.JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            "Renamed",
            row.GetProperty("library").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Find_LocatorPublishesDefinitionsInsteadOfForwarders(
        bool limit)
    {
        string id =
            $"Workspace.Search.Forwarder.{Guid.NewGuid():N}";
        byte[] facade = await File.ReadAllBytesAsync(
            FixtureCatalog.MatchBindingFacade.AssemblyPath(),
            TestContext.Current.CancellationToken);
        byte[] implementation = await File.ReadAllBytesAsync(
            FixtureCatalog.MatchBindingImplementation.AssemblyPath(),
            TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{id}.nuspec", "<package />"u8.ToArray()),
            (
                "lib/net11.0/"
                    + FixtureCatalog.MatchBindingFacade.AssemblyFileName,
                facade),
            (
                "lib/net11.0/"
                    + FixtureCatalog.MatchBindingImplementation
                        .AssemblyFileName,
                implementation));
        ConfigureCommandFeed(id, package);

        var arguments = new List<string>
        {
            "find",
            "DotnetInspector.MatchBinding.ComparisonApi",
            "--package", $"{id}@{Version}",
            "--tfm", "net11.0",
            "--source", FirstFeed,
            "--json",
            "--tips", "q",
        };
        if (limit)
        {
            arguments.Add("-n");
            arguments.Add("1");
        }

        var result = await RunCommandAsync([.. arguments]);

        Assert.Equal(0, result.Exit);
        Assert.Equal("", result.Error);
        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(result.Output);
        System.Text.Json.JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            "DotnetInspector.MatchBinding.Implementation",
            row.GetProperty("library").GetString());
    }

    [Theory]
    [InlineData(
        "DotnetInspect.Cli.Tests.NullablePatternTarget<string?>",
        "Direct")]
    [InlineData(
        "DotnetInspect.Cli.Tests.NullablePatternTargat<string?>",
        null)]
    public async Task Find_NullableGenericPatternPreservesCompatibilityAcrossLocatorRoutes(
        string pattern,
        string? expectedMatch)
    {
        string id =
            $"Workspace.Search.Nullable.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = CreatePackage(
            id,
            "nullable generic pattern package",
            library: assembly,
            libraryName: "NullablePattern.dll");
        ConfigureCommandFeed(id, package);

        foreach (bool forceCompatibility in new[] { false, true })
        {
            List<string> arguments =
            [
                "find", pattern,
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--tips", "q",
            ];
            if (forceCompatibility)
            {
                arguments.Add("--library");
                arguments.Add(
                    typeof(System.Text.Json.JsonSerializer).Assembly.Location);
            }

            var result = await RunCommandAsync([.. arguments]);

            Assert.Equal(0, result.Exit);
            using System.Text.Json.JsonDocument document =
                System.Text.Json.JsonDocument.Parse(result.Output);
            if (expectedMatch is null)
            {
                Assert.Empty(document.RootElement.EnumerateArray());
                continue;
            }

            System.Text.Json.JsonElement row =
                Assert.Single(
                    document.RootElement.EnumerateArray(),
                    row =>
                        row.GetProperty("full_name").GetString()
                        == typeof(NullablePatternTarget<>).FullName);
            Assert.Equal(
                pattern,
                row.GetProperty("pattern").GetString());
            Assert.Equal(
                expectedMatch,
                row.GetProperty("match").GetString());
            Assert.Equal(
                typeof(NullablePatternTarget<>).FullName,
                row.GetProperty("full_name").GetString());
        }
    }

    [Fact]
    public async Task Find_PackageLocatorInventoryRejectionIsVisible()
    {
        string id =
            $"Workspace.Search.Rejected.{Guid.NewGuid():N}";
        byte[] package = CreatePackage(
            id,
            "rejected declaration inventory",
            library: BuildDuplicateTypeDefinitionAssembly(),
            libraryName: "DuplicateTypes.dll");
        ConfigureCommandFeed(id, package);

        var result = await RunCommandAsync(
            [
                "find", "*",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
                "--tips", "q",
            ]);

        Assert.Equal(0, result.Exit);
        Assert.Equal("[]", result.Output.Trim());
        Assert.Contains(
            "Type declaration search in 'DuplicateTypes' was incomplete:",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_MixedPackageLayoutUsesCompatibilityAssemblyIdentity()
    {
        string id =
            $"Workspace.Search.Reference.{Guid.NewGuid():N}";
        byte[] referenceAssembly = await File.ReadAllBytesAsync(
            typeof(AssemblySetResolver).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{id}.nuspec", "<package />"u8.ToArray()),
            ("ref/net11.0/Workspace.Search.dll", referenceAssembly),
            ("lib/net11.0/Implementation.dll", referenceAssembly));
        ConfigureCommandFeed(id, package);

        var referenceResult = await RunCommandAsync(
            [
                "find",
                typeof(AssemblySetResolver).FullName!,
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
                "--tips", "q",
            ]);
        Assert.Equal(0, referenceResult.Exit);
        Assert.Contains(
            nameof(AssemblySetResolver),
            referenceResult.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"library\": \"Workspace.Search\"",
            referenceResult.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"library\": \"DotnetInspector.Services\"",
            referenceResult.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_LocatorPreservesDefaultDiscoveryVisibility()
    {
        string id =
            $"Workspace.Search.Visibility.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = CreatePackage(
            id,
            "locator visibility package",
            library: assembly,
            libraryName: "DotnetInspect.Cli.Tests.dll");
        ConfigureCommandFeed(id, package);

        async Task<string[]> SearchAsync(bool includeAll)
        {
            var arguments = new List<string>
            {
                "find",
                "DotnetInspect.Cli.Tests.LocatorDiscovery*",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--json",
            };
            if (includeAll)
                arguments.Add("--all");

            var result = await RunCommandAsync([.. arguments]);
            Assert.Equal(0, result.Exit);
            using System.Text.Json.JsonDocument document =
                System.Text.Json.JsonDocument.Parse(result.Output);
            return
            [
                .. document.RootElement.EnumerateArray().Select(
                    static row =>
                        row.GetProperty("full_name").GetString()!),
            ];
        }

        Assert.Equal(
            ["DotnetInspect.Cli.Tests.LocatorDiscoveryVisible"],
            await SearchAsync(includeAll: false));
        Assert.Equal(
            [
                "DotnetInspect.Cli.Tests.LocatorDiscoveryHidden",
                "DotnetInspect.Cli.Tests.LocatorDiscoveryObsolete",
                "DotnetInspect.Cli.Tests.LocatorDiscoveryVisible",
            ],
            (await SearchAsync(includeAll: true))
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private static void ConfigureCommandFeed(
        string packageId,
        byte[] package,
        ConcurrentQueue<string>? requests = null)
    {
        requests ??= new();
        CoreHttpClientFactory.SetAuthenticationDecorator(
            _ => new PayloadFeedHandler(
                FirstFeed,
                packageId,
                () => new ByteArrayContent(package),
                requests));
        CoreHttpClientFactory.ResetSharedForTesting();
        // The House path reaches a configured HTTP source through its
        // credential-free package-source transport, not the shared client.
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => new PayloadFeedHandler(
                FirstFeed,
                packageId,
                () => new ByteArrayContent(package),
                requests));
    }

    private static byte[] BuildDuplicateTypeDefinitionAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("DuplicateTypes.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("DuplicateTypes"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int i = 0; i < 2; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Duplicate"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    private sealed class MultiVersionPayloadFeedHandler(
        string source,
        string id,
        IReadOnlyDictionary<string, byte[]> packages)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            string flat =
                new Uri(new Uri(source), "flat2/").AbsoluteUri;
            HttpContent? content = null;
            if (url == source)
            {
                content = new StringContent($$"""
                    {"version":"3.0.0","resources":[
                      {"@id":"{{flat}}","@type":"PackageBaseAddress/3.0.0"}
                    ]}
                    """);
            }
            else
            {
                foreach ((string version, byte[] package) in packages)
                {
                    string packageUrl =
                        $"{flat}{id.ToLowerInvariant()}/{version}/"
                        + $"{id.ToLowerInvariant()}.{version}.nupkg";
                    if (url == packageUrl)
                    {
                        content = new ByteArrayContent(package);
                        break;
                    }
                }
            }

            if (content is null)
            {
                throw new InvalidOperationException(
                    $"Unexpected exact-pin request: {url}");
            }

            return Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = content,
                    RequestMessage = request,
                });
        }
    }
}

public sealed class LocatorDiscoveryVisible;

[System.ComponentModel.EditorBrowsable(
    System.ComponentModel.EditorBrowsableState.Never)]
public sealed class LocatorDiscoveryHidden;

[Obsolete]
public sealed class LocatorDiscoveryObsolete;
