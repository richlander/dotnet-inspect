using System.Net;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using ILInspector.Metadata;
using Markout;
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
    [InlineData("A\tB")]
    [InlineData("A\u0085B")]
    [InlineData("A\u2028B")]
    [InlineData("A\u2029B")]
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

    [Theory]
    [InlineData("A\tB")]
    [InlineData("A\u0085B")]
    [InlineData("A\u2028B")]
    [InlineData("A\u2029B")]
    [InlineData("A`B")]
    [InlineData("A|B")]
    [InlineData("A<B")]
    [InlineData("A>B")]
    [InlineData("A&B")]
    public void PortableSelectors_RoundTripThroughSupportedOutputFormats(
        string selector)
    {
        string encoded =
            WorkspaceNavigationPortableSelector.Encode(selector);
        var view = new WorkspaceNavigationView
        {
            Types =
            [
                new WorkspaceNavigationTypeRow(
                    encoded,
                    "Library",
                    "compile:lib/Fixture.dll",
                    "public",
                    "Available",
                    active: false,
                    retained: false),
            ],
        };

        Assert.Equal(encoded, Assert.Single(view.Types).Type);
        Assert.Contains(
            encoded,
            MarkoutSerializer.Serialize(
                view,
                WorkspaceNavigationViewContext.Default),
            StringComparison.Ordinal);
        Assert.Contains(
            encoded,
            RenderNavigation(view, new PlainTextFormatter()),
            StringComparison.Ordinal);
        Assert.Contains(
            encoded,
            RenderNavigationTable(view, tsv: false, jsonl: false),
            StringComparison.Ordinal);

        string tsv = RenderNavigationTable(
            view,
            tsv: true,
            jsonl: false,
            showHeader: false);
        string[] tsvFields = Assert.Single(
            tsv.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)).Split('\t');
        Assert.Equal(10, tsvFields.Length);
        Assert.Equal(encoded, tsvFields[2]);

        string jsonl = RenderNavigationTable(
            view,
            tsv: false,
            jsonl: true);
        using (JsonDocument document = JsonDocument.Parse(
            Assert.Single(
                jsonl.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries))))
        {
            Assert.Equal(
                encoded,
                document.RootElement.GetProperty("subject").GetString());
        }

        string json = JsonSerializer.Serialize(
            view,
            WorkspaceCommandJsonContext.Default.WorkspaceNavigationView);
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            Assert.Equal(
                encoded,
                document.RootElement
                    .GetProperty("types")[0]
                    .GetProperty("type")
                    .GetString());
        }

        Assert.Equal(
            selector,
            WorkspaceNavigationPortableSelector.Decode(encoded));
    }

    [Fact]
    public void PortableSelectors_PreservePrintableAsciiThroughMarkdown()
    {
        var rewritten = new List<char>();
        for (char character = '\u0020'; character <= '\u007E'; character++)
        {
            string encoded =
                WorkspaceNavigationPortableSelector.Encode(
                    $"A{character}B");
            var view = new WorkspaceNavigationView
            {
                Types =
                [
                    new WorkspaceNavigationTypeRow(
                        encoded,
                        "Library",
                        "compile:lib/Fixture.dll",
                        "public",
                        "Available",
                        active: false,
                        retained: false),
                ],
            };
            string markdown = MarkoutSerializer.Serialize(
                view,
                WorkspaceNavigationViewContext.Default);
            if (!markdown.Contains(encoded, StringComparison.Ordinal))
                rewritten.Add(character);
        }

        Assert.True(
            rewritten.Count == 0,
            string.Join(
                ", ",
                rewritten.Select(character =>
                    $"U+{(int)character:X4} '{character}'")));
    }

    [Fact]
    public async Task GenericTypeSelector_CopiesFromMarkdownAndRebinds()
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
        string typeName =
            typeof(WorkspaceNavigationGenericFixture<>).FullName!;
        string portable =
            WorkspaceNavigationPortableSelector.Encode(typeName);

        var inventory = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                },
                load,
                TestContext.Current.CancellationToken));
        Assert.Equal(0, inventory.ExitCode);
        Assert.Contains(portable, inventory.Output, StringComparison.Ordinal);

        var selected = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Library =
                        $"compile:lib/{Framework}/"
                            + "DotnetInspect.Cli.Tests.dll",
                    Type = portable,
                    Lens = "type.compare",
                    Format = OutputFormat.Json,
                },
                load,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, selected.ExitCode);
        using JsonDocument document = JsonDocument.Parse(selected.Output);
        Assert.True(
            document.RootElement.GetProperty("types")
                .EnumerateArray()
                .Single(row =>
                    row.GetProperty("type").GetString() == portable)
                .GetProperty("active").GetBoolean());
        Assert.Equal(
            "type.compare",
            document.RootElement.GetProperty("navigation")[0]
                .GetProperty("lens").GetString());
    }

    [Fact]
    public async Task TypeWithoutLibraryResolvesAgainstAggregateSubject()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll",
                await File.ReadAllBytesAsync(
                    typeof(WorkspaceCommandTests).Assembly.Location,
                    TestContext.Current.CancellationToken)),
            ($"lib/{Framework}/ILInspector.Metadata.dll",
                await File.ReadAllBytesAsync(
                    typeof(PdbContext).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        using var client = new HttpClient(new FailingHandler());
        string portable =
            WorkspaceNavigationPortableSelector.Encode(
                typeof(PdbContext).FullName!);

        var selected = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Type = portable,
                    Lens = "type.compare",
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, selected.ExitCode);
        Assert.Empty(selected.Error);
        using JsonDocument document =
            JsonDocument.Parse(selected.Output);
        Assert.True(
            document.RootElement.GetProperty("types")
                .EnumerateArray()
                .Single(row =>
                    row.GetProperty("type").GetString()
                        == portable)
                .GetProperty("active").GetBoolean());
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\u2028")]
    public async Task WhitespaceOnlyTypeSelector_CopiesAndRebinds(
        string typeName)
    {
        const string assemblyName = "WhitespaceType";
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/{assemblyName}.dll",
                BuildTypeAssembly(assemblyName, typeName)));
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
        Assert.Equal(0, inventory.ExitCode);
        using JsonDocument inventoryDocument =
            JsonDocument.Parse(inventory.Output);
        string portable = Assert.Single(
            inventoryDocument.RootElement.GetProperty("types")
                .EnumerateArray()).GetProperty("type").GetString()!;
        Assert.Equal(
            WorkspaceNavigationPortableSelector.Encode(typeName),
            portable);

        var selected = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    Library =
                        $"compile:lib/{Framework}/{assemblyName}.dll",
                    Type = portable,
                    Lens = "type.compare",
                    Format = OutputFormat.Json,
                },
                load,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, selected.ExitCode);
        using JsonDocument selectedDocument =
            JsonDocument.Parse(selected.Output);
        Assert.Equal(
            portable,
            Assert.Single(
                selectedDocument.RootElement.GetProperty("types")
                    .EnumerateArray()).GetProperty("type").GetString());
        Assert.True(
            Assert.Single(
                selectedDocument.RootElement.GetProperty("types")
                    .EnumerateArray()).GetProperty("active").GetBoolean());
        Assert.Equal(
            "type.compare",
            selectedDocument.RootElement.GetProperty("navigation")[0]
                .GetProperty("lens").GetString());
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
    public void WorkspaceCommand_RegistersInventoryConstructionOptions()
    {
        string[] arguments =
        [
            "workspace",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "net10.0",
            "--register-library",
            "System.Text.Json@10.0.0/System.Text.Json@10.0.0.0",
            "--register-package-prefix",
            "Microsoft.Extensions.",
            "--register-ecosystem",
            "aspire",
            "--kind",
            "exact-library",
            "--share",
            "packet",
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

            No top-level entries.

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
        Assert.Contains("Ready", captured.Output);
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
        Assert.Contains("Ready", captured.Output);
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
        Assert.Contains("Ready", captured.Output);
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
            ? JsonDocument.Parse(captured.Output)
                .RootElement.GetProperty("entries").GetRawText()
            : $"[{string.Join(",", captured.Output.Split(
                '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))}]";
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement[] rows = [.. document.RootElement.EnumerateArray()];
        Assert.Equal(["Workspace.Z", "Workspace.A"],
            rows.Select(row => row.GetProperty("package_id").GetString()));
        Assert.All(rows, row =>
        {
            Assert.Equal("package", row.GetProperty("kind").GetString());
            Assert.Equal(
                Version,
                row.GetProperty("package_version").GetString());
            Assert.Equal(
                Framework,
                row.GetProperty("requested_target_framework").GetString());
            Assert.Equal(
                "ready",
                row.GetProperty("state").GetProperty("kind").GetString());
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
        Assert.Contains("Ready", captured.Output);
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
            JsonElement row = Assert.Single(
                document.RootElement.GetProperty("entries").EnumerateArray());
            Assert.Equal(
                "Workspace.B",
                row.GetProperty("package_id").GetString());
        }
    }

    [Fact]
    public async Task MixedWorkspace_RendersPackagesThenRegistrations()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            "System.Text.Json",
            ("lib/net10.0/System.Text.Json.dll",
                BuildTypeAssembly("System.Text.Json", "System.Text.Json.JsonSerializer")));
        await AddPackageAsync(
            store,
            "Markout",
            ("lib/net10.0/Markout.dll",
                BuildTypeAssembly("Markout", "Markout.Document")));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages =
                    [
                        "System.Text.Json@1.0.0",
                        "Markout@1.0.0",
                    ],
                    Tfm = Framework,
                    RegisteredLibraries =
                    [
                        "System.Text.Json@1.0.0/System.Text.Json@1.0.0.0",
                    ],
                    RegisteredPackagePrefixes = ["Microsoft.Extensions."],
                    RegisteredEcosystems = ["aspire"],
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        JsonElement[] entries =
        [
            .. document.RootElement.GetProperty("entries").EnumerateArray(),
        ];
        Assert.Equal(
            ["package", "package", "exactLibrary", "packagePrefix", "ecosystem"],
            entries.Select(entry => entry.GetProperty("kind").GetString()));
        Assert.Equal(
            "System.Text.Json",
            entries[0].GetProperty("package_id").GetString());
        Assert.Equal(
            "System.Text.Json",
            entries[2].GetProperty("coordinate")
                .GetProperty("library_identity")
                .GetProperty("name").GetString());
        Assert.Equal(
            "Microsoft.Extensions.",
            entries[3].GetProperty("prefix")
                .GetProperty("prefix").GetString());
        Assert.Equal(
            "ecosystem.aspire",
            entries[4].GetProperty("id").GetString());
    }

    [Fact]
    public async Task KindFilter_PreservesTypedRegistrationVector()
    {
        using var client = new HttpClient(new FailingHandler());
        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    RegisteredLibraries =
                    [
                        "System.Text.Json@10.0.0/System.Text.Json@10.0.0.0",
                    ],
                    RegisteredPackagePrefixes = ["Microsoft.Extensions."],
                    RegisteredEcosystems = ["aspire"],
                    InventoryKinds =
                    [
                        WorkspaceTopLevelInventoryEntryKind.ExactLibrary,
                        WorkspaceTopLevelInventoryEntryKind.PackagePrefix,
                    ],
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        Assert.Equal(
            3,
            document.RootElement.GetProperty("total_entry_count").GetInt32());
        Assert.Equal(
            2,
            document.RootElement.GetProperty("selected_entry_count").GetInt32());
        Assert.Equal(
            ["exactLibrary", "packagePrefix"],
            document.RootElement.GetProperty("entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task PacketRoute_UsesExactPacketShareBasis()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll",
                await File.ReadAllBytesAsync(
                    typeof(WorkspaceCommandTests).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        WorkspaceSharePacket packet = CreatePacket(PackageId);
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = encoded,
                    ShareFormat = WorkspaceShareFormat.Packet,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        Assert.Equal(
            PackageId,
            Assert.Single(
                document.RootElement.GetProperty("entries")
                    .EnumerateArray())
                .GetProperty("package_id").GetString());
        Assert.Equal(encoded, captured.Error.Trim());

        var direct = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));
        Assert.Equal(0, direct.ExitCode);
        using JsonDocument directDocument = JsonDocument.Parse(direct.Output);
        Assert.Equal(
            directDocument.RootElement.GetProperty("entries").GetRawText(),
            document.RootElement.GetProperty("entries").GetRawText());
    }

    [Fact]
    public async Task PacketRoute_InventoriesPackageMembershipFromEveryContext()
    {
        const string secondPackageId = "Markout";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(WorkspaceCommandTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly));
        await AddPackageAsync(
            store,
            secondPackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly));
        WorkspaceSharePacket packet = CreatePacket(
            (PackageId, Version, Framework),
            (secondPackageId, Version, Framework));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = WorkspaceSharePacketCodec.Encode(packet),
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        Assert.Equal(
            [PackageId, secondPackageId],
            document.RootElement.GetProperty("entries")
                .EnumerateArray()
                .Select(entry =>
                    entry.GetProperty("package_id").GetString()));
    }

    [Fact]
    public async Task PacketRoute_CoalescesFloatingAndExactMembersResolvingToOneRoot()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(WorkspaceCommandTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly));
        WorkspaceSharePacket packet = CreatePacket(
            (PackageId, null, Framework),
            (PackageId, Version, Framework));
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        using var client = new HttpClient(new VersionListingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = encoded,
                    ShareFormat = WorkspaceShareFormat.Packet,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.True(
            captured.ExitCode == 0,
            $"Output: {captured.Output}{Environment.NewLine}Error: {captured.Error}");
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        Assert.Single(
            document.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal(encoded, captured.Error.Trim());
    }

    [Fact]
    public async Task PacketRoute_PreservesRequestedFrameworkWithCompatibleAssets()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(WorkspaceCommandTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ("lib/net8.0/DotnetInspect.Cli.Tests.dll", assembly));
        WorkspaceSharePacket packet = CreatePacket(PackageId);
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = encoded,
                    ShareFormat = WorkspaceShareFormat.Packet,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.True(
            captured.ExitCode == 0,
            $"Output: {captured.Output}{Environment.NewLine}Error: {captured.Error}");
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        JsonElement entry = Assert.Single(
            document.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal(
            Framework,
            entry.GetProperty("requested_target_framework").GetString());
        Assert.Equal(
            "net8.0",
            entry.GetProperty("selected_target_framework").GetString());
        Assert.Equal(
            "net8.0",
            entry.GetProperty("effective_target_framework").GetString());
        Assert.Equal(encoded, captured.Error.Trim());
    }

    [Fact]
    public async Task PacketRoute_RejectsUnsupportedLegacyFormat()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            """
            {
              "f": 1,
              "t": [[":Platform", "10.0.10", "net10.0", null]],
              "g": [[0]],
              "a": 0,
              "x": 0,
              "v": "api"
            }
            """,
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = WorkspaceSharePacketCodec.Encode(packet),
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains("requires packet format 2", captured.Error);
    }

    [Fact]
    public async Task VerbosePacketRows_DistinguishPackageContextTargets()
    {
        const string olderFramework = "net8.0";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(WorkspaceCommandTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{olderFramework}/DotnetInspect.Cli.Tests.dll", assembly));
        WorkspaceSharePacket packet = CreatePacket(
            (PackageId, Version, olderFramework),
            (PackageId, Version, Framework));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = WorkspaceSharePacketCodec.Encode(packet),
                    Verbose = true,
                    Format = OutputFormat.Table,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains($"requested {olderFramework}", captured.Output);
        Assert.Contains($"requested {Framework}", captured.Output);
        Assert.Equal(
            2,
            captured.Output.Split(
                $"selected {olderFramework}",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task ShareCannotBeCombinedWithPackageNavigation()
    {
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    ActivePackage = 1,
                    ShareFormat = WorkspaceShareFormat.Packet,
                },
                LoadOptions(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "--share reports top-level Workspace inventory",
            captured.Error);
    }

    [Fact]
    public async Task FilteredPacket_ContentRemainsAvailableButShareDoesNot()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll",
                await File.ReadAllBytesAsync(
                    typeof(WorkspaceCommandTests).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        string encoded = WorkspaceSharePacketCodec.Encode(
            CreatePacket(PackageId));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = encoded,
                    InventoryKinds =
                    [
                        WorkspaceTopLevelInventoryEntryKind.PackagePrefix,
                    ],
                    ShareFormat = WorkspaceShareFormat.Packet,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        Assert.Equal(
            0,
            document.RootElement.GetProperty("selected_entry_count")
                .GetInt32());
        Assert.Empty(
            document.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Contains(
            "do not represent inventory kind filters",
            captured.Error);
    }

    [Fact]
    public async Task DirectRouteShare_RemainsNonProjectable()
    {
        using var client = new HttpClient(new FailingHandler());
        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    RegisteredPackagePrefixes = ["Microsoft.Extensions."],
                    ShareFormat = WorkspaceShareFormat.Packet,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains("packagePrefix", captured.Output);
        Assert.Contains(
            "no retained Definitions-owned projection",
            captured.Error);
    }

    [Theory]
    [InlineData("--packet", "--package")]
    [InlineData("--packet", "--register-library")]
    [InlineData("--packet", "--active-package")]
    [InlineData("--kind", "--active-package")]
    public async Task IncompatibleConstructionAndInventoryOptionsFail(
        string first,
        string second)
    {
        var options = new WorkspaceOptions
        {
            Packet = first == "--packet" ? "invalid" : null,
            Packages = second == "--package" ? ["P@1.0.0"] : [],
            RegisteredLibraries =
                second == "--register-library"
                    ? ["P@1.0.0/P@1.0.0.0"]
                    : [],
            ActivePackage = second == "--active-package" ? 1 : null,
            InventoryKinds =
                first == "--kind"
                    ? [WorkspaceTopLevelInventoryEntryKind.Package]
                    : [],
        };
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                options,
                LoadOptions(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.NotEmpty(captured.Error);
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

    static string RenderNavigation(
        WorkspaceNavigationView view,
        Markout.Formatting.IMarkoutFormatter formatter)
    {
        var writer = new StringWriter { NewLine = "\n" };
        MarkoutSerializer.Serialize(
            view,
            writer,
            formatter,
            WorkspaceNavigationViewContext.Default);
        return writer.ToString();
    }

    static string RenderNavigationTable(
        WorkspaceNavigationView view,
        bool tsv,
        bool jsonl,
        bool showHeader = true) =>
        OutputFormatter.RenderTable(
            showHeader,
            (writer, formatter) => MarkoutSerializer.Serialize(
                WorkspaceNavigationProjection.CreateStream(view),
                writer,
                formatter,
                WorkspaceNavigationViewContext.Default,
                OutputFormatter.CreateTableWriterOptions(tsv, jsonl)));

    static WorkspaceContextLoadOptions LoadOptions(HttpClient client, IPackageStore store) => new()
    {
        HttpClient = client,
        SourceAuthorization = new UniformPackageSourceAuthorization([Source]),
        PackageStore = store,
    };

    static WorkspaceSharePacket CreatePacket(string packageId) =>
        CreatePacket((packageId, Version, Framework));

    static WorkspaceSharePacket CreatePacket(
        params (string Id, string? Version, string Framework)[] packages)
    {
        var registry = new InspectionDefinitionRegistry();
        WorkspaceContextDefinition[] contexts =
        [
            .. packages.Select((package, index) =>
                new WorkspaceContextDefinition(
                    $"context-{index}",
                    framework: package.Framework,
                    members:
                    [
                        new DefinitionMemberCoordinate.PackageCoordinate(
                            package.Id,
                            package.Version,
                            package.Framework),
                    ])),
        ];
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version2,
            "workspace",
            contexts));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [
                .. packages.Select((package, index) =>
                    new NavigationTabDefinition(
                        $"package-{index}",
                        coordinate:
                            new DefinitionMemberCoordinate.PackageCoordinate(
                                package.Id,
                                package.Version,
                                package.Framework))),
            ],
            "package-0"));
        registry.Add(new CommittedViewDefinition(
            InspectionDefinitionSchema.Version2,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                .. packages.Select((_, index) =>
                    new CommittedViewStateDefinition(
                        $"package-{index}",
                        new PortableSubjectRequest.Package(),
                        new PortableRetainedSubjectContext.Package(),
                        facet: "package.overview")),
            ]));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version2,
            "scenario",
            workspace: "workspace",
            context: "context-0",
            view: "view",
            navigation: "navigation"));
        var prepared = Assert.IsType<
            InspectionDefinitionScenarioPreparationResult.Version2>(
                registry.PrepareScenario("scenario"));
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                prepared.Definitions,
                TestContext.Current.CancellationToken);
        Assert.True(
            projection.Succeeded,
            projection.Failure?.Message);
        return Assert.IsType<WorkspaceSharePacket>(projection.Packet);
    }

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

    static byte[] BuildTypeAssembly(
        string assemblyName,
        string typeName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(assemblyName + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
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
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString(typeName),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
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

    sealed class VersionListingHandler : HttpMessageHandler
    {
        const string FlatContainer = "https://fixture.invalid/flat/";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            string? body = url switch
            {
                "https://fixture.invalid/v3/index.json" =>
                    $$"""
                    {"resources":[{"@id":"{{FlatContainer}}","@type":"PackageBaseAddress/3.0.0"}]}
                    """,
                $"{FlatContainer}workspace.command.fixture/index.json" =>
                    $$"""{"versions":["{{Version}}"]}""",
                _ => null,
            };
            return Task.FromResult(
                new HttpResponseMessage(
                    body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                {
                    Content = new StringContent(body ?? ""),
                    RequestMessage = request,
                });
        }
    }
}

public sealed class WorkspaceNavigationGenericFixture<T>;
