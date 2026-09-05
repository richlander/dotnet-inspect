using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json;
using InspectWeb.Engine.SourceFacade;
using TsJsExport;

namespace InspectWeb.Engine.Tests;

[CollectionDefinition("Type source operations", DisableParallelization = true)]
public sealed class TypeSourceOperationCollection;

[Collection("Type source operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserTypeSourceOperationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RunningManagedSource_RetainsBudgetUntilReleaseAndKeepsUserReasonAcrossLegacySupersession()
    {
        var bridge = new BrowserManagedOperationBridge();
        BrowserManagedOperationId id = BrowserManagedOperationId.From(Guid.NewGuid().ToString());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<BrowserManagedOperationResult<int, string, string>> running =
            bridge.RunAsync<int, string, string, object>(
                id, null,
                async (token, _) =>
                {
                    using BrowserSourceOperationLease lease =
                        await BrowserSourceOperationCoordinator.BeginAsync(
                            token, reason => bridge.RequestCancellation(id, reason));
                    await release.Task;
                    token.ThrowIfCancellationRequested();
                    return new BrowserManagedOperationBodyResult<int, string, string>.Succeeded(1);
                },
                error => new(error.Message, error.ToString()));

        Assert.IsType<BrowserManagedCancellationRequestResult.Requested>(
            bridge.RequestCancellation(id, BrowserManagedOperationCancelReason.User));
        Task<BrowserSourceOperationLease> legacy =
            BrowserSourceOperationCoordinator.BeginAsync().AsTask();
        var repeated = Assert.IsType<BrowserManagedCancellationRequestResult.AlreadyRequested>(
            bridge.RequestCancellation(id, BrowserManagedOperationCancelReason.Timeout));
        Assert.Equal(BrowserManagedOperationCancelReason.User, repeated.Reason);
        Assert.False(running.IsCompleted);
        Assert.False(legacy.IsCompleted);
        Assert.Equal(1, bridge.ActiveCount);
        release.SetResult();

        var canceled = Assert.IsType<BrowserManagedOperationResult<int, string, string>.Canceled>(
            await running);
        Assert.Equal(BrowserManagedOperationCancelReason.User, canceled.Reason);
        Assert.Equal(0, bridge.ActiveCount);
        using BrowserSourceOperationLease successor = await legacy;
        Assert.False(successor.CancellationToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("superseded")]
    [InlineData("disposed")]
    [InlineData("feature-observer-failed")]
    [InlineData("timeout")]
    [InlineData("worker-restarted")]
    public async Task CanceledGateWaiter_PreservesFirstReasonAndReleasesOnlyItsOwnResources(
        string reason)
    {
        using BrowserSourceOperationLease holder =
            await BrowserSourceOperationCoordinator.BeginAsync();
        string id = Guid.NewGuid().ToString();
        Task<string> waiting = Query(id);
        Assert.False(waiting.IsCompleted);

        BrowserTypeSourceCancellation first = Cancel(id, reason);
        Assert.Equal(BrowserTypeSourceCancellationKind.Requested, first.Kind);
        Assert.Equal(reason, first.Reason);
        BrowserTypeSourceCancellation repeated = Cancel(id, "user");
        if (repeated.Kind == BrowserTypeSourceCancellationKind.AlreadyRequested)
            Assert.Equal(reason, repeated.Reason);
        else
            Assert.Equal(BrowserTypeSourceCancellationKind.NotActive, repeated.Kind);

        BrowserTypeSourceResult result = Read(await waiting);
        Assert.Equal(BrowserTypeSourceResultKind.Canceled, result.Kind);
        Assert.Equal(reason, result.Reason);
        Assert.Null(result.Value);
        Assert.Null(result.Error);
        Assert.Equal(BrowserTypeSourceCancellationKind.NotActive, Cancel(id).Kind);

        Task<BrowserSourceOperationLease> next =
            BrowserSourceOperationCoordinator.BeginAsync().AsTask();
        Assert.False(next.IsCompleted);
        holder.Dispose();
        using BrowserSourceOperationLease successor = await next;
        Assert.False(successor.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task LegacySupersession_RecordsReasonBeforeCancelingManagedWaiter()
    {
        using BrowserSourceOperationLease holder =
            await BrowserSourceOperationCoordinator.BeginAsync();
        string id = Guid.NewGuid().ToString();
        Task<string> typeSource = Query(id);
        Task<BrowserSourceOperationLease> memberSource =
            BrowserSourceOperationCoordinator.BeginAsync().AsTask();

        Assert.Equal("superseded", Read(await typeSource).Reason);
        Assert.False(memberSource.IsCompleted);
        Assert.Equal(BrowserTypeSourceCancellationKind.NotActive, Cancel(id).Kind);
        holder.Dispose();
        using BrowserSourceOperationLease member = await memberSource;
        Assert.False(member.CancellationToken.IsCancellationRequested);
        SourceExports.CancelSourceQuery();
        Assert.True(member.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task KeyedCancellationOfOldOperation_DoesNotCancelReplacement()
    {
        using BrowserSourceOperationLease holder =
            await BrowserSourceOperationCoordinator.BeginAsync();
        string firstId = Guid.NewGuid().ToString();
        string secondId = Guid.NewGuid().ToString();
        Task<string> first = Query(firstId);
        Task<string> second = Query(secondId);
        string firstJson = await first;
        output.WriteLine($"A terminal: {firstJson}");
        Assert.Equal("superseded", Read(firstJson).Reason);
        string oldCancellation = SourceExports.CancelTypeSourceQuery(firstId, "user");
        output.WriteLine($"Cancel A after B starts: {oldCancellation}");
        Assert.Equal(BrowserTypeSourceCancellationKind.NotActive,
            JsonSerializer.Deserialize(oldCancellation,
                BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation)!.Kind);
        Assert.False(second.IsCompleted);
        output.WriteLine($"B remains pending: {!second.IsCompleted}");
        Assert.Equal(BrowserTypeSourceCancellationKind.Requested, Cancel(secondId).Kind);
        string secondJson = await second;
        output.WriteLine($"B terminal: {secondJson}");
        Assert.Equal("user", Read(secondJson).Reason);
        string settledCancellation = SourceExports.CancelTypeSourceQuery(secondId, "user");
        output.WriteLine($"Cancel B after settlement: {settledCancellation}");
        Assert.Equal(BrowserTypeSourceCancellationKind.NotActive,
            JsonSerializer.Deserialize(settledCancellation,
                BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation)!.Kind);
    }

    [Fact]
    public async Task LegacyCancellation_UsesUserReasonForManagedWaiter()
    {
        using BrowserSourceOperationLease holder =
            await BrowserSourceOperationCoordinator.BeginAsync();
        string id = Guid.NewGuid().ToString();
        Task<string> waiting = Query(id);
        SourceExports.CancelSourceQuery();
        Assert.Equal("user", Read(await waiting).Reason);
        Assert.Equal(BrowserTypeSourceCancellationKind.NotActive, Cancel(id).Kind);
    }

    [Fact]
    public async Task DuplicateActiveId_RejectsWithoutReplacingOriginal()
    {
        using BrowserSourceOperationLease holder =
            await BrowserSourceOperationCoordinator.BeginAsync();
        string id = Guid.NewGuid().ToString();
        Task<string> original = Query(id);
        var error = await Assert.ThrowsAsync<BrowserManagedOperationBoundaryException>(
            () => Query(id));
        Assert.Equal("duplicate-active-operation", error.FailureKind);
        Assert.False(original.IsCompleted);
        Assert.Equal(BrowserTypeSourceCancellationKind.Requested, Cancel(id).Kind);
        Assert.Equal("user", Read(await original).Reason);
    }

    [Fact]
    public async Task InvalidReason_RejectsWithoutCancelingActiveOperation()
    {
        using BrowserSourceOperationLease holder =
            await BrowserSourceOperationCoordinator.BeginAsync();
        string id = Guid.NewGuid().ToString();
        Task<string> waiting = Query(id);
        Assert.Throws<ArgumentException>(() => SourceExports.CancelTypeSourceQuery(id, "other"));
        Assert.False(waiting.IsCompleted);
        Cancel(id, "disposed");
        Assert.Equal("disposed", Read(await waiting).Reason);
        await Assert.ThrowsAsync<ArgumentException>(() => Query(""));
    }

    [Fact]
    public async Task ExportSuccess_PreservesSourceAndProvenanceAndReleasesScope()
    {
        const string packageId = "Type.Source.Bridge.Success";
        RegisterSourcePackage(packageId);
        string id = Guid.NewGuid().ToString();
        BrowserTypeSourceResult result = Read(await SourceExports.QueryTypeSource(
            id, packageId, "1.0.0", "net11.0", "TsJsExport.Contracts.dll",
            "TsJsExport.JsExportRootAttribute", "[]"));

        Assert.Equal(1, result.Version);
        Assert.Equal(BrowserTypeSourceResultKind.Succeeded, result.Kind);
        Assert.NotNull(result.Value);
        Assert.Equal("decompiled", result.Value.Provider);
        Assert.Contains("JsExportRootAttribute", result.Value.Text);
        Assert.Contains(packageId, result.Value.Provenance);
        Assert.Null(result.Value.Url);
        Assert.NotNull(result.Value.PdbSourceLimitation);
        await AssertReleased(id, packageId);
    }

    [Fact]
    public async Task MissingType_IsExpectedFailureAndReleasesScope()
    {
        const string packageId = "Type.Source.Bridge.Missing";
        RegisterSourcePackage(packageId);
        string id = Guid.NewGuid().ToString();
        BrowserTypeSourceResult result = Read(await SourceExports.QueryTypeSource(
            id, packageId, "1.0.0", "net11.0", "TsJsExport.Contracts.dll",
            "Missing.Type", "[]"));

        Assert.Equal(BrowserTypeSourceResultKind.Failed, result.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Expected, result.FailureKind);
        Assert.Contains("does not contain one exact type", result.Error);
        Assert.Contains("Missing.Type", result.Diagnostic);
        Assert.Null(result.Value);
        await AssertReleased(id, packageId);
    }

    [Fact]
    public async Task ProducerException_IsUnexpectedFailureAndReleasesScope()
    {
        const string packageId = "Type.Source.Bridge.Unexpected";
        RegisterSourcePackage(packageId);
        string id = Guid.NewGuid().ToString();
        BrowserTypeSourceResult result = Read(await SourceExports.QueryTypeSource(
            id, packageId, "1.0.0", "net11.0", "TsJsExport.Contracts.dll",
            "TsJsExport.JsExportRootAttribute", "{"));

        Assert.Equal(BrowserTypeSourceResultKind.Failed, result.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Unexpected, result.FailureKind);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Contains("JsonException", result.Diagnostic);
        await AssertReleased(id, packageId);
    }

    static async Task AssertReleased(string id, string packageId)
    {
        Assert.Equal(BrowserTypeSourceCancellationKind.NotActive, Cancel(id).Kind);
        using BrowserSourceOperationLease next =
            await BrowserSourceOperationCoordinator.BeginAsync();
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId, "1.0.0", "net11.0");
        BrowserPackageWorkspace.RemoveScope(scope);
        Assert.False(BrowserPackageWorkspace.IsScopeRetained(scope));
    }

    static Task<string> Query(string id) => SourceExports.QueryTypeSource(
        id, "", "1.0.0", "net11.0", "Example.dll", "Example.Type", "[]");

    static BrowserTypeSourceCancellation Cancel(string id, string reason = "user") =>
        JsonSerializer.Deserialize(
            SourceExports.CancelTypeSourceQuery(id, reason),
            BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation)!;

    static BrowserTypeSourceResult Read(string json) =>
        JsonSerializer.Deserialize(
            json, BrowserSourceJsonContext.Default.BrowserTypeSourceResult)!;

    static void RegisterSourcePackage(string packageId)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry("lib/net11.0/TsJsExport.Contracts.dll").Open();
            entry.Write(File.ReadAllBytes(typeof(JsExportRootAttribute).Assembly.Location));
        }
        BrowserPackageWorkspace.RegisterAcquiredPackage(
            new BrowserPackage(packageId, "1.0.0", bytes.ToArray(), fromCache: false));
    }
}
