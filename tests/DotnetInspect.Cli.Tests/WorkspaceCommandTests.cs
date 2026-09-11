using System.Net;
using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class WorkspaceCommandTests
{
    const string PackageId = "Workspace.Command.Fixture";
    const string Version = "1.0.0";
    const string Framework = "net10.0";

    static readonly PackageSource Source =
        new("fixture", "https://fixture.invalid/v3/index.json");

    [Theory]
    [InlineData("Ordinary")]
    [InlineData("Namespace.Type\\+Nested")]
    [InlineData("A\nB")]
    [InlineData("A\r\nB")]
    [InlineData("A\u202EB")]
    [InlineData("A\\u000AB")]
    public void PortableSelectors_RoundTripThroughDisplayContainment(
        string selector)
    {
        string encoded =
            WorkspaceNavigationPortableSelector.Encode(selector);

        Assert.Equal(
            encoded,
            CSharpText.CSharpIdentifier.ContainRenderedText(encoded));
        Assert.Equal(
            selector,
            WorkspaceNavigationPortableSelector.Decode(encoded));
    }

    [Fact]
    public void WorkspaceCommand_IsReservedFromImplicitPackageRouting()
    {
        string[] arguments =
            CommandLineBuilder.PreprocessArgs(["workspace", "--help"]);

        Assert.Equal("workspace", arguments[0]);
        Assert.Contains("workspace", CommandLineBuilder.KnownCommands);
    }

    [Fact]
    public void WorkspaceCommand_RegistersSharedOutputOptions()
    {
        string[] arguments =
            ["workspace", "--json", "--rows", "1", "--verbose"];

        var result = CommandLineBuilder.CreateRootCommand().Parse(arguments);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void WorkspaceCommand_RegistersStatelessNavigationOptions()
    {
        string[] arguments =
        [
            "workspace",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "net10.0",
            "--active-package",
            "1",
            "--library",
            "compile:lib/net10.0/System.Text.Json.dll",
            "--type",
            "System.Text.Json.JsonSerializer",
            "--lens",
            "type.compare",
        ];

        var result = CommandLineBuilder.CreateRootCommand().Parse(arguments);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task EmptyWorkspace_RendersTheTypedEmptyInventory()
    {
        using var client = new HttpClient(new FailingHandler());
        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions(),
                new WorkspaceContextLoadOptions
                {
                    HttpClient = client,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization([Source]),
                    PackageStore = new InMemoryPackageStore(),
                }));

        Assert.Equal(0, captured.ExitCode);
        Assert.Equal(
            """
            # Workspace

            No package occurrences.

            """.ReplaceLineEndings(),
            captured.Output);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task PopulatedWorkspace_CoalescesExactRoots()
    {
        var store = new InMemoryPackageStore();
        string sourceKey = NuGetCache.GetSourceKey(Source.Url);
        byte[] assembly =
            await File.ReadAllBytesAsync(
                typeof(WorkspaceCommandTests).Assembly.Location,
                TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly));
        using (var stream = new MemoryStream(package))
        {
            await store.CommitAsync(
                PackageId,
                Version,
                sourceKey,
                stream,
                TestContext.Current.CancellationToken);
        }

        using var client = new HttpClient(new FailingHandler());
        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages =
                    [
                        $"{PackageId}@{Version}",
                        $"{PackageId}@{Version}",
                    ],
                    Tfm = Framework,
                },
                new WorkspaceContextLoadOptions
                {
                    HttpClient = client,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization([Source]),
                    PackageStore = store,
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains("# Workspace", captured.Output);
        Assert.Contains(PackageId, captured.Output);
        Assert.Contains(Version, captured.Output);
        Assert.Contains(Framework, captured.Output);
        Assert.Equal(
            1,
            captured.Output.Split(
                PackageId,
                StringSplitOptions.None).Length - 1);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task EmptyCompileGroup_WithCompatibleLibrary_RemainsAWorkspaceRoot()
    {
        const string assetFramework = "net8.0";
        var store = new InMemoryPackageStore();
        string sourceKey = NuGetCache.GetSourceKey(Source.Url);
        byte[] assembly =
            await File.ReadAllBytesAsync(
                typeof(WorkspaceCommandTests).Assembly.Location,
                TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
            ($"ref/{Framework}/_._", []),
            ($"lib/{assetFramework}/DotnetInspect.Cli.Tests.dll", assembly));
        using (var stream = new MemoryStream(package))
        {
            await store.CommitAsync(
                PackageId,
                Version,
                sourceKey,
                stream,
                TestContext.Current.CancellationToken);
        }

        using var client = new HttpClient(new FailingHandler());
        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                },
                new WorkspaceContextLoadOptions
                {
                    HttpClient = client,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization([Source]),
                    PackageStore = store,
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains(PackageId, captured.Output);
        Assert.Contains(Framework, captured.Output);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task CompatibleCompileGroup_RendersTheRequestedFramework()
    {
        const string assetFramework = "net8.0";
        var store = new InMemoryPackageStore();
        string sourceKey = NuGetCache.GetSourceKey(Source.Url);
        byte[] assembly =
            await File.ReadAllBytesAsync(
                typeof(WorkspaceCommandTests).Assembly.Location,
                TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
            ($"lib/{assetFramework}/DotnetInspect.Cli.Tests.dll", assembly));
        using (var stream = new MemoryStream(package))
        {
            await store.CommitAsync(
                PackageId,
                Version,
                sourceKey,
                stream,
                TestContext.Current.CancellationToken);
        }

        using var client = new HttpClient(new FailingHandler());
        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                },
                new WorkspaceContextLoadOptions
                {
                    HttpClient = client,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization([Source]),
                    PackageStore = store,
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains(PackageId, captured.Output);
        Assert.Contains(Framework, captured.Output);
        Assert.Empty(captured.Error);
    }

    [Theory]
    [InlineData(OutputFormat.Json)]
    [InlineData(OutputFormat.Jsonl)]
    public async Task StructuredScope_PreservesOrderAndOnlyLowersPortableFacts(
        OutputFormat format)
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, "Workspace.Z", ("readme.txt", []));
        await AddPackageAsync(store, "Workspace.A", ("readme.txt", []));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = ["Workspace.Z@1.0.0", "Workspace.A@1.0.0", "Workspace.Z@1.0.0"],
                    Tfm = Framework,
                    Format = format,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        string json = format == OutputFormat.Json
            ? captured.Output
            : $"[{string.Join(",", captured.Output.Split(
                '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))}]";
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement[] rows = [.. document.RootElement.EnumerateArray()];
        Assert.Equal(["Workspace.Z", "Workspace.A"],
            rows.Select(row => row.GetProperty("package").GetString()));
        Assert.All(rows, row =>
        {
            Assert.Equal(["package", "version", "framework"],
                row.EnumerateObject().Select(property => property.Name));
            Assert.Equal(Version, row.GetProperty("version").GetString());
            Assert.Equal(Framework, row.GetProperty("framework").GetString());
        });
    }

    [Theory]
    [InlineData(OutputFormat.Markdown)]
    [InlineData(OutputFormat.Table)]
    [InlineData(OutputFormat.Tsv)]
    [InlineData(OutputFormat.PlainText)]
    public async Task RootOnlyPackage_RendersAcrossHumanFormats(OutputFormat format)
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, PackageId, ("readme.txt", []));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    Format = format,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains(PackageId, captured.Output);
        Assert.Contains(Version, captured.Output);
        Assert.Contains(Framework, captured.Output);
        Assert.Empty(captured.Error);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net11.0")]
    public async Task FailedAddBatch_DoesNotRenderASuccessfulPrefix(string assetFramework)
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, "Workspace.Good", ("readme.txt", []));
        await AddPackageAsync(store, "Workspace.Bad",
            ($"lib/{assetFramework}/Bad.dll", [1, 2, 3]));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = ["Workspace.Good@1.0.0", "Workspace.Bad@1.0.0"],
                    Tfm = Framework,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.NotEmpty(captured.Error);
    }

    [Fact]
    public async Task Count_UsesTheCoalescedCommittedRoots()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, PackageId, ("readme.txt", []));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}", $"{PackageId}@{Version}"],
                    Tfm = Framework,
                    Count = true,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Equal("1", captured.Output.Trim());
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task ActivePackage_CoalescedDuplicateRetainsExactBinding()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, PackageId, ("readme.txt", []));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages =
                    [
                        $"{PackageId}@{Version}",
                        $"{PackageId}@{Version}",
                    ],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        Assert.Equal(
            1,
            document.RootElement.GetProperty("packages").GetArrayLength());
        Assert.Equal(
            "Package",
            document.RootElement.GetProperty("navigation")[0]
                .GetProperty("active_kind").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RowWindow_AppliesToCommittedRootsBeforeRenderingOrCounting(bool count)
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, "Workspace.A", ("readme.txt", []));
        await AddPackageAsync(store, "Workspace.B", ("readme.txt", []));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = ["Workspace.A@1.0.0", "Workspace.A@1.0.0", "Workspace.B@1.0.0"],
                    Tfm = Framework,
                    Format = OutputFormat.Json,
                    Rows = RowWindow.Range(2, null),
                    Count = count,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        if (count)
        {
            Assert.Equal("1", captured.Output.Trim());
        }
        else
        {
            using JsonDocument document = JsonDocument.Parse(captured.Output);
            JsonElement row = Assert.Single(document.RootElement.EnumerateArray());
            Assert.Equal("Workspace.B", row.GetProperty("package").GetString());
        }
    }

    [Fact]
    public async Task ActivePackage_ProjectsPortableNavigationThroughJson()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll",
                await File.ReadAllBytesAsync(
                    typeof(WorkspaceCommandTests).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        JsonElement root = document.RootElement;
        Assert.Equal(
            "Library",
            root.GetProperty("navigation")[0]
                .GetProperty("active_kind").GetString());
        Assert.Equal(
            PackageId,
            root.GetProperty("packages")[0]
                .GetProperty("package").GetString());
        Assert.Equal(5, root.GetProperty("hierarchy").GetArrayLength());
        Assert.NotEmpty(root.GetProperty("libraries").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("types").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("members").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("lenses").EnumerateArray());
        Assert.Contains(
            root.GetProperty("libraries").EnumerateArray(),
            library => !string.IsNullOrEmpty(
                library.GetProperty("asset_id").GetString()));
        Assert.All(
            root.GetProperty("types").EnumerateArray(),
            type => Assert.False(string.IsNullOrEmpty(
                type.GetProperty("library_asset_id").GetString())));
        Assert.All(
            root.GetProperty("members").EnumerateArray(),
            member =>
            {
                Assert.False(string.IsNullOrEmpty(
                    member.GetProperty("library_asset_id").GetString()));
                Assert.False(string.IsNullOrEmpty(
                    member.GetProperty("containing_type").GetString()));
                Assert.False(string.IsNullOrEmpty(
                    member.GetProperty("declaring_type").GetString()));
            });
        Assert.DoesNotContain("workspace_identity", captured.Output);
        Assert.DoesNotContain("occurrence_identity", captured.Output);
        Assert.DoesNotContain("action_id", captured.Output);
        Assert.DoesNotContain("authority", captured.Output);
    }

    [Fact]
    public async Task ExactTypeAndMemberLens_UseAtomicStatelessNavigation()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll",
                await File.ReadAllBytesAsync(
                    typeof(WorkspaceCommandTests).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextLoadOptions load = LoadOptions(client, store);
        var inventory = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Format = OutputFormat.Json,
                },
                load,
                TestContext.Current.CancellationToken));
        using JsonDocument inventoryDocument =
            JsonDocument.Parse(inventory.Output);
        JsonElement inventoryRoot = inventoryDocument.RootElement;
        string assetId = inventoryRoot.GetProperty("libraries")
            .EnumerateArray()
            .Single(row => !string.IsNullOrEmpty(
                row.GetProperty("asset_id").GetString()))
            .GetProperty("asset_id").GetString()!;
        string typeName = typeof(WorkspaceCommandTests).FullName!;
        JsonElement typeRow = inventoryRoot.GetProperty("types")
            .EnumerateArray()
            .Single(row => row.GetProperty("type").GetString()
                == typeName);
        string memberSelector = inventoryRoot.GetProperty("members")
            .EnumerateArray()
            .First(row => row.GetProperty("declaring_type").GetString()
                == typeName)
            .GetProperty("selector").GetString()!;

        var typeResult = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Library = assetId,
                    Type = typeRow.GetProperty("type").GetString(),
                    Lens = "type.compare",
                    Format = OutputFormat.Json,
                },
                load,
                TestContext.Current.CancellationToken));
        Assert.Equal(0, typeResult.ExitCode);
        using JsonDocument typeDocument =
            JsonDocument.Parse(typeResult.Output);
        Assert.Equal(
            "Type",
            typeDocument.RootElement.GetProperty("navigation")[0]
                .GetProperty("active_kind").GetString());
        Assert.Equal(
            "type.compare",
            typeDocument.RootElement.GetProperty("navigation")[0]
                .GetProperty("lens").GetString());

        var memberResult = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Library = assetId,
                    Type = typeName,
                    Member = memberSelector,
                    Lens = "member.compare",
                    Format = OutputFormat.Json,
                },
                load,
                TestContext.Current.CancellationToken));
        Assert.Equal(0, memberResult.ExitCode);
        using JsonDocument memberDocument =
            JsonDocument.Parse(memberResult.Output);
        Assert.Equal(
            "Member",
            memberDocument.RootElement.GetProperty("navigation")[0]
                .GetProperty("active_kind").GetString());
        Assert.Equal(
            "member.compare",
            memberDocument.RootElement.GetProperty("navigation")[0]
                .GetProperty("lens").GetString());
    }

    [Fact]
    public async Task UnknownDestinationLens_RetainsSourceAndDiagnostic()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll",
                await File.ReadAllBytesAsync(
                    typeof(WorkspaceCommandTests).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        using var client = new HttpClient(new FailingHandler());
        string assetId =
            $"compile:lib/{Framework}/DotnetInspect.Cli.Tests.dll";

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Library = assetId,
                    Type = typeof(WorkspaceCommandTests).FullName,
                    Lens = "type.unknown",
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        Assert.Equal(
            "Library",
            document.RootElement.GetProperty("navigation")[0]
                .GetProperty("active_kind").GetString());
        JsonElement diagnostic = Assert.Single(
            document.RootElement.GetProperty("diagnostics")
                .EnumerateArray());
        Assert.Equal(
            "lens-unknown",
            diagnostic.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ActivePackage_MarkdownLowersNavigationThroughMarkout()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll",
                await File.ReadAllBytesAsync(
                    typeof(WorkspaceCommandTests).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains("# Workspace", captured.Output);
        Assert.Contains("## Navigation", captured.Output);
        Assert.Contains("## Packages", captured.Output);
        Assert.Contains("## Hierarchy", captured.Output);
        Assert.Contains("## Libraries", captured.Output);
        Assert.Contains("## Types", captured.Output);
        Assert.Contains("## Members", captured.Output);
        Assert.Contains("## Lenses", captured.Output);
    }

    [Fact]
    public async Task ActivePackage_JsonlCarriesPortableDescriptorRecords()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll",
                await File.ReadAllBytesAsync(
                    typeof(WorkspaceCommandTests).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Format = OutputFormat.Jsonl,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        JsonElement[] rows =
        [
            .. captured.Output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                    JsonDocument.Parse(line).RootElement.Clone()),
        ];
        Assert.Contains(
            rows,
            row => row.GetProperty("record").GetString()
                == "hierarchy");
        Assert.Contains(
            rows,
            row => row.GetProperty("record").GetString()
                == "library");
        Assert.Contains(
            rows,
            row => row.GetProperty("record").GetString()
                == "type");
        Assert.Contains(
            rows,
            row => row.GetProperty("record").GetString()
                == "member");
        Assert.Contains(
            rows,
            row => row.GetProperty("record").GetString()
                == "lens");
        Assert.All(
            rows.Where(row => row.GetProperty("record").GetString()
                is "type" or "member"),
            row => Assert.False(string.IsNullOrEmpty(
                row.GetProperty("library_asset_id").GetString())));
        Assert.All(
            rows.Where(row => row.GetProperty("record").GetString()
                == "member"),
            row =>
            {
                Assert.False(string.IsNullOrEmpty(
                    row.GetProperty("containing_type").GetString()));
                Assert.False(string.IsNullOrEmpty(
                    row.GetProperty("declaring_type").GetString()));
            });
        Assert.DoesNotContain("authority", captured.Output);
        Assert.DoesNotContain("action_id", captured.Output);
    }

    [Fact]
    public async Task ActivePackage_ActiveEntriesExecuteAndTombstoneRemainsRetired()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll",
                await File.ReadAllBytesAsync(
                    typeof(WorkspaceCommandTests).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        JsonElement[] lenses =
        [
            .. document.RootElement.GetProperty("lenses")
                .EnumerateArray(),
        ];
        Assert.Contains(
            lenses,
            lens => lens.GetProperty("facet").GetString()
                == "library.integrations");
        Assert.Contains(
            lenses,
            lens => lens.GetProperty("facet").GetString()
                == "library.analysis");
        Assert.Contains(
            lenses,
            lens => lens.GetProperty("facet").GetString()
                == "library.metadata");
        Assert.Contains(
            lenses,
            lens => lens.GetProperty("facet").GetString()
                == "library.compare");
        Assert.All(
            lenses.Where(lens =>
                lens.GetProperty("facet").GetString()
                    != "library.opportunities"),
            lens => Assert.Equal(
                "Available",
                lens.GetProperty("availability").GetString()));
        JsonElement retired = Assert.Single(
            lenses,
            lens => lens.GetProperty("facet").GetString()
                == "library.opportunities");
        Assert.Equal(
            "Unavailable",
            retired.GetProperty("availability").GetString());
        Assert.Equal(
            "This view is retired.",
            retired.GetProperty("diagnostic").GetString());
    }

    [Fact]
    public async Task MissingType_RendersNonSuccessSnapshotAndDiagnostic()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll",
                await File.ReadAllBytesAsync(
                    typeof(WorkspaceCommandTests).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Library =
                        $"compile:lib/{Framework}/DotnetInspect.Cli.Tests.dll",
                    Type = "Missing.Type",
                    Lens = "type.compare",
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Error);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        Assert.Equal(
            "Library",
            document.RootElement.GetProperty("navigation")[0]
                .GetProperty("active_kind").GetString());
        Assert.Equal(
            "selector-not-present",
            Assert.Single(
                document.RootElement.GetProperty("diagnostics")
                    .EnumerateArray())
                .GetProperty("code").GetString());
    }

    [Fact]
    public async Task RootOnlyAllLibraries_RendersTypedUnavailableSnapshot()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, PackageId, ("readme.txt", []));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    AllLibraries = true,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Error);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        Assert.Equal(
            "Package",
            document.RootElement.GetProperty("navigation")[0]
                .GetProperty("active_kind").GetString());
        Assert.Equal(
            "selector-unavailable",
            Assert.Single(
                document.RootElement.GetProperty("diagnostics")
                    .EnumerateArray())
                .GetProperty("code").GetString());
    }

    [Fact]
    public async Task AllLibrariesRejectsIgnoredLibraryWithoutTypeDestination()
    {
        string[] arguments =
        [
            "workspace",
            "--active-package",
            "1",
            "--all-libraries",
            "--library",
            "compile:lib/net10.0/Example.dll",
            "--json",
        ];

        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(arguments)
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "--library names the exact defining Library",
            captured.Error);
    }

    static WorkspaceContextLoadOptions LoadOptions(HttpClient client, IPackageStore store) => new()
    {
        HttpClient = client,
        SourceAuthorization = new UniformPackageSourceAuthorization([Source]),
        PackageStore = store,
    };

    static async Task AddPackageAsync(
        InMemoryPackageStore store, string packageId,
        params (string Path, byte[] Content)[] entries)
    {
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            [(packageId + ".nuspec", "<package />"u8.ToArray()), .. entries]);
        using var stream = new MemoryStream(package);
        await store.CommitAsync(
            packageId, Version, NuGetCache.GetSourceKey(Source.Url),
            stream, TestContext.Current.CancellationToken);
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                });
    }
}
