using System.Collections.Concurrent;

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
        Assert.Equal("", result.Output.Trim());
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
    public async Task Find_PackageLocatorUsesAssemblyIdentityFromImplementationUniverse()
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
            "\"library\": \"DotnetInspector.Services\"",
            referenceResult.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"library\": \"Workspace.Search\"",
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
        CoreHttpClientFactory.SetAuthenticationDecorator(
            _ => new PayloadFeedHandler(
                FirstFeed,
                packageId,
                () => new ByteArrayContent(package),
                requests ?? new()));
        CoreHttpClientFactory.ResetSharedForTesting();
    }
}

public sealed class LocatorDiscoveryVisible;

[System.ComponentModel.EditorBrowsable(
    System.ComponentModel.EditorBrowsableState.Never)]
public sealed class LocatorDiscoveryHidden;

[Obsolete]
public sealed class LocatorDiscoveryObsolete;
