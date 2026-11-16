using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Presentation;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public sealed partial class WorkspacePacketActivationTests
{
    const string NativeTypeJson =
        """{"f":6,"t":[[":Platform","11.0.0-rc.1.26425.128","net11.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"type","l":["System.Text.Json","11.0.0.0",null,"cc7b13ffcd2ddd51"],"y":"System.Text.Json.JsonElement"},"u":{"k":"type"},"f":"type.overview"}]}""";
    const string NativeLibraryJson =
        """{"f":6,"t":[[":Platform","11.0.0-rc.1.26425.128","net11.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"library","l":["System.Text.Json","11.0.0.0",null,"cc7b13ffcd2ddd51"]},"u":{"k":"library"},"f":"library.types"}]}""";

    [Theory]
    [InlineData(NativeTypeJson)]
    [InlineData(NativeLibraryJson)]
    public void NativeBindingsMatchExactPacketState(string json)
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            Encode(json), TestContext.Current.CancellationToken);
        WorkspaceShareViewState selected = packet.ViewStates[1];
        Assert.True(WorkspacePacketActivation.IsNativeBinding(packet, selected),
            $"format {packet.FormatVersion}; subject {selected.Subject?.GetType().Name}; "
            + $"context {selected.Context?.GetType().Name}; facet {selected.Facet}");
        Assert.Null(WorkspacePacketActivation.BindingError(packet, selected));
    }

    [Fact]
    public async Task PinnedRuntimeReplaysExactNativeNameViewsWithoutPackageMembership()
    {
        string archive = Path.Combine(AppContext.BaseDirectory, "RealAssets",
            "SourceNative",
            "microsoft.netcore.app.runtime.linux-x64.11.0.0-rc.1.26425.128.nupkg");
        using (ZipArchive package = ZipFile.OpenRead(archive))
        using (Stream image = package.GetEntry(
            "runtimes/linux-x64/lib/net11.0/System.Text.Json.dll")!.Open())
            Assert.Equal(
                "61bfbbb5104e2531474d5bc3e6cf5b596608174ee374b98e62957620dea7f870",
                Convert.ToHexStringLower(
                    await SHA256.HashDataAsync(
                        image, TestContext.Current.CancellationToken)));

        var store = new InMemoryPackageStore();
        await using (Stream contents = File.OpenRead(archive))
            await store.CommitAsync(
                "Microsoft.NETCore.App.Runtime.linux-x64",
                "11.0.0-rc.1.26425.128",
                PackageSourceClientFactory.GetProducerIdentity(
                    new PackageSource("test", SourceUrl)).Key,
                contents,
                TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        var diagnostics = new List<string>();
        WorkspaceContextLoadOptions load =
            LoadOptions(client, store) with { Log = diagnostics.Add };
        string typePacket = Encode(NativeTypeJson);
        string libraryPacket = Encode(NativeLibraryJson);

        WorkspacePacketRestorationResult result =
            await WorkspacePacketRestoration.RestoreAsync(
                typePacket, load, TestContext.Current.CancellationToken,
                captureSelectedSubject: true);
        Assert.True(result is WorkspacePacketRestorationResult.Restored,
            result is WorkspacePacketRestorationResult.Failed failure
                ? $"{failure.Summary} {string.Join("; ", failure.Details)} "
                    + string.Join("; ", diagnostics)
                : result.ToString());
        var restored = (WorkspacePacketRestorationResult.Restored)result;
        string sharedTree = await restored.Value.ExecuteAsync(async active =>
        {
            Assert.Empty(active.Activation.Snapshot.Scope.Packages);
            Assert.Single(active.Activation.Contexts);
            Assert.Single(active.Activation.SelectedContext!.LibraryOccurrences);
            Assert.IsType<WorkspaceDeclarationRequest.PlatformPopulation>(
                active.Activation.SelectedContext.Receipt.Request);
            CompleteRestorationSubjectResult<string> borrowed =
                await CompleteRestorationSelectedSubjectOperation
                    .ExecuteAdmittedAsync(
                        active.Workspace,
                        active.Activation,
                        (subject, lease, token) =>
                        {
                            var type = Assert.IsType<
                                CompleteRestorationSelectedSubject.AdmittedType>(
                                subject);
                            TypeOverviewHierarchyPresentationPlan presentation =
                                TypeOverviewHierarchyPresentation
                                    .CreateCompactPlan(
                                        TypeOverviewHierarchyPresentationFormat.Tree,
                                        includeNonPublic: false);
                            var envelope =
                                TypeOverviewDocumentInspectionOperation.Execute(
                                    new(
                                        subject.Occurrence.Library,
                                        TypeOverviewHierarchyPresentation
                                            .CreateInspectionPlan(
                                                type.Subject.Name,
                                                presentation,
                                                TypeInspectionPolicies
                                                    .CompactOverview.ExtractionBounds)),
                                    lease, token);
                            var available = Assert.IsType<
                                TypeOverviewDocumentInspectionOutcome.Available>(
                                envelope.Content);
                            using var output = new StringWriter();
                            TypeOverviewHierarchyPresentation.Write(
                                available.Document, presentation, output);
                            return ValueTask.FromResult(output.ToString());
                        },
                        TestContext.Current.CancellationToken);
            return Assert.IsType<
                CompleteRestorationSubjectResult<string>.Completed>(
                borrowed).Content;
        });

        var library = await ConsoleCapture.RunAsync(() =>
            WorkspacePacketActivation.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = libraryPacket,
                    Format = OutputFormat.Json,
                }, TestContext.Current.CancellationToken, load));
        Assert.True(library.ExitCode == 0, library.Error);
        Assert.Empty(library.Error);
        Assert.Contains("JsonElement", library.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"package\"", library.Output, StringComparison.Ordinal);
        using JsonDocument libraryJson = JsonDocument.Parse(library.Output);
        int expectedTypeCount = libraryJson.RootElement.GetProperty("document")
            .GetProperty("types").GetProperty("rows").GetProperty("items")
            .GetArrayLength();
        var libraryTree = await ConsoleCapture.RunAsync(() =>
            WorkspacePacketActivation.ExecuteAsync(
                new WorkspaceOptions { Packet = libraryPacket },
                TestContext.Current.CancellationToken, load));
        Assert.True(libraryTree.ExitCode == 0, libraryTree.Error);
        Assert.StartsWith("System.Text.Json (Platform)", libraryTree.Output,
            StringComparison.Ordinal);
        Assert.Contains("struct JsonElement", libraryTree.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", libraryTree.Output,
            StringComparison.Ordinal);
        var type = await ConsoleCapture.RunAsync(() =>
            WorkspacePacketActivation.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = typePacket,
                    Format = OutputFormat.Json,
                }, TestContext.Current.CancellationToken, load));
        Assert.True(type.ExitCode == 0, type.Error);
        Assert.Empty(type.Error);
        using JsonDocument json = JsonDocument.Parse(type.Output);
        JsonElement rows = json.RootElement.GetProperty("document")
            .GetProperty("members").GetProperty("rows")
            .GetProperty("items");
        Assert.NotEmpty(rows.EnumerateArray());
        Assert.All(rows.EnumerateArray(),
            row => Assert.True(
                row.GetProperty("exactMemberCount").GetInt32() >= 0));
        Assert.DoesNotContain("\"package\"", type.Output, StringComparison.Ordinal);
        var tree = await ConsoleCapture.RunAsync(() =>
            WorkspacePacketActivation.ExecuteAsync(
                new WorkspaceOptions { Packet = typePacket },
                TestContext.Current.CancellationToken, load));
        Assert.True(tree.ExitCode == 0, tree.Error);
        Assert.Contains("System.Text.Json.JsonElement", tree.Output,
            StringComparison.Ordinal);
        Assert.Contains("GetProperty", tree.Output, StringComparison.Ordinal);
        Assert.Equal(sharedTree, tree.Output);
        var plain = await ConsoleCapture.RunAsync(() =>
            WorkspacePacketActivation.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = typePacket,
                    Format = OutputFormat.PlainText,
                },
                TestContext.Current.CancellationToken, load));
        Assert.True(plain.ExitCode == 0, plain.Error);
        Assert.Equal(tree.Output, plain.Output);

        var libraryCount = await ConsoleCapture.RunAsync(() =>
            WorkspacePacketActivation.ExecuteAsync(
                new WorkspaceOptions { Packet = libraryPacket, Count = true },
                TestContext.Current.CancellationToken, load));
        Assert.True(libraryCount.ExitCode == 0, libraryCount.Error);
        Assert.Equal(expectedTypeCount,
            int.Parse(libraryCount.Output.Trim(),
                System.Globalization.CultureInfo.InvariantCulture));

        foreach (string missingPacket in new[]
        {
            Encode(NativeLibraryJson.Replace("System.Text.Json\",\"11.0.0.0",
                "Unavailable.Assembly\",\"11.0.0.0", StringComparison.Ordinal)),
            Encode(NativeTypeJson.Replace("System.Text.Json.JsonElement",
                "System.Text.Json.DoesNotExist", StringComparison.Ordinal)),
        })
        {
            var unavailable = await ConsoleCapture.RunAsync(() =>
                WorkspacePacketActivation.ExecuteAsync(
                    new WorkspaceOptions { Packet = missingPacket },
                    TestContext.Current.CancellationToken, load));
            Assert.Equal(1, unavailable.ExitCode);
            Assert.Empty(unavailable.Output);
            Assert.Contains("could not be restored", unavailable.Error,
                StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("-w")]
    [InlineData("raw")]
    public async Task NativeRoutingRefusesUnknownFacetBeforeAcquisitionAndUsesRenderedLines(
        string entrance)
    {
        string packet = Encode(NativeTypeJson);
        ArgumentPreprocessor.Reset();
        var root = CommandLineBuilder.CreateRootCommand();
        string[] args = [.. entrance == "-w"
            ? new[] { "-w", packet } : [packet], "-n", "1"];
        string[] processed = entrance == "raw"
            ? [WorkspaceActivationCommandDefinitions.CommandName, .. args]
            : CommandLineBuilder.PreprocessArgs(args, root);
        var selected = CliRowSelectionCommandRegistry.Prepare(
            root.Parse(processed), processed);
        Assert.Null(selected.Error);
        Assert.Equal(1, selected.Lowering!.LineIntent!.Count);
        Assert.Empty(selected.Lowering.SemanticIntent.Operations);

        string unsupported = Encode(NativeTypeJson.Replace(
            "type.overview", "type.metadata", StringComparison.Ordinal));
        var refusal = await RunAsync([.. entrance == "-w"
            ? new[] { "-w", unsupported } : [unsupported]]);
        Assert.Equal(1, refusal.ExitCode);
        Assert.Empty(refusal.Output);
        Assert.Contains("Format 6", refusal.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeSourceDenialFailsWithoutOutput()
    {
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextLoadOptions denied = new()
        {
            HttpClient = client,
            PackageStore = new InMemoryPackageStore(),
            SourceAuthorization = new UniformPackageSourceAuthorization([]),
        };
        var deniedResult = await ConsoleCapture.RunAsync(() =>
            WorkspacePacketActivation.ExecuteAsync(
                new WorkspaceOptions { Packet = Encode(NativeTypeJson) },
                TestContext.Current.CancellationToken, denied));
        Assert.Equal(1, deniedResult.ExitCode);
        Assert.Empty(deniedResult.Output);
        Assert.Contains("could not be restored", deniedResult.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeAcquisitionUsesTheCallerTransportForAnUncachedArchive()
    {
        var handler = new NotFoundSourceHandler();
        using var client = new HttpClient(handler);
        WorkspaceContextLoadOptions load =
            LoadOptions(client, new InMemoryPackageStore());
        var result = await ConsoleCapture.RunAsync(() =>
            WorkspacePacketActivation.ExecuteAsync(
                new WorkspaceOptions { Packet = Encode(NativeTypeJson) },
                TestContext.Current.CancellationToken, load));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("could not be restored", result.Error,
            StringComparison.Ordinal);
        Assert.True(handler.RequestCount > 0);
    }

    [Theory]
    [InlineData(NativeTypeJson, true, OutputFormat.Json)]
    [InlineData(NativeLibraryJson, false, OutputFormat.Tsv)]
    public async Task UnsupportedNativeTerminalRefusesBeforeAcquisition(
        string json, bool count, OutputFormat format)
    {
        var handler = new NotFoundSourceHandler();
        using var client = new HttpClient(handler);
        var result = await ConsoleCapture.RunAsync(() =>
            WorkspacePacketActivation.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = Encode(json),
                    Count = count,
                    Format = format,
                },
                TestContext.Current.CancellationToken,
                LoadOptions(client, new InMemoryPackageStore())));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(0, handler.RequestCount);
        Assert.Contains(count ? "Count terminal" : "tree and JSON",
            result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeContextQueryRefusesBeforeAcquisition()
    {
        var handler = new NotFoundSourceHandler();
        using var client = new HttpClient(handler);
        var result = await WorkspacePacketRestoration.RestoreAsync(
            Encode(NativeTypeJson),
            LoadOptions(client, new InMemoryPackageStore()),
            TestContext.Current.CancellationToken);
        var failed = Assert.IsType<
            WorkspacePacketRestorationResult.Failed>(result);
        Assert.Contains(failed.Details,
            static detail => detail.Contains("no CLI query binding",
                StringComparison.Ordinal));
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class NotFoundSourceHandler : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
