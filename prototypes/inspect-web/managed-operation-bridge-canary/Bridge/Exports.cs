using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using InspectWeb.Engine;
using BodyResult =
    InspectWeb.Engine.BrowserManagedOperationBodyResult<string, string, string>;
using BridgeResult =
    InspectWeb.Engine.BrowserManagedOperationResult<string, string, string>;

namespace InspectWeb.ManagedOperationBridge.BrowserCanary;

[JsonConverter(typeof(JsonStringEnumConverter<OperationResultKind>))]
public enum OperationResultKind
{
    Succeeded,
    Failed,
    Canceled,
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationFailureKind>))]
public enum OperationFailureKind
{
    Expected,
    Unexpected,
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationCancelReason>))]
public enum OperationCancelReason
{
    User,
    Superseded,
    Disposed,
    FeatureObserverFailed,
    Timeout,
    WorkerRestarted,
}

[JsonConverter(typeof(JsonStringEnumConverter<CancellationRequestKind>))]
public enum CancellationRequestKind
{
    Requested,
    AlreadyRequested,
    NotActive,
}

public sealed record OperationResultEnvelope(
    OperationResultKind Kind,
    string? Value,
    OperationFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    OperationCancelReason? CancelReason);

public sealed record CancellationRequestReceipt(
    CancellationRequestKind Kind,
    OperationCancelReason? Reason);

public sealed record VerificationReceipt(
    string Status,
    int BodyStarts,
    int WithoutProgressStarts,
    int CancellationRequests,
    int Completions,
    int RetainedReports,
    int DuplicateBoundaryFailures,
    int ProgressBoundaryFailures,
    int MalformedInputFailures,
    int OtherBoundaryFailures);

[JsonSerializable(typeof(OperationResultEnvelope))]
[JsonSerializable(typeof(CancellationRequestReceipt))]
[JsonSerializable(typeof(VerificationReceipt))]
[JsonSerializable(typeof(SharedProducerSnapshot))]
[JsonSerializable(typeof(SharedVerificationReceipt))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class CanaryJsonContext : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class Exports
{
    private static readonly object s_sync = new();
    private static readonly BrowserManagedOperationBridge s_bridge = new();
    private static readonly Dictionary<
        BrowserManagedOperationId,
        ControlledOperation> s_controllers = [];
    private static readonly Dictionary<
        BrowserManagedOperationId,
        IBrowserManagedOperationEvents<CanaryProgress>> s_retainedProgress = [];
    private static readonly Dictionary<OperationMode, int> s_modeStarts = [];
    private static int s_bodyStarts;
    private static int s_withoutProgressStarts;
    private static int s_cancellationRequests;
    private static int s_completions;
    private static int s_retainedReports;
    private static int s_duplicateBoundaryFailures;
    private static int s_progressBoundaryFailures;
    private static int s_malformedInputFailures;
    private static int s_otherBoundaryFailures;

    [JSExport]
    public static async Task<string> RunOperation(
        string operationId,
        string mode,
        [JSMarshalAs<JSType.Function<
            JSType.Number,
            JSType.String,
            JSType.Boolean>>]
        Action<int, string, bool> progressCallback,
        bool retainProgress)
    {
        ArgumentNullException.ThrowIfNull(progressCallback);
        BrowserManagedOperationId id;
        try
        {
            id = BrowserManagedOperationId.From(operationId);
        }
        catch (ArgumentException)
        {
            Interlocked.Increment(ref s_malformedInputFailures);
            throw;
        }
        OperationMode parsedMode = ParseMode(mode);
        var controller = new ControlledOperation(parsedMode);
        IBrowserManagedOperationEvents<CanaryProgress>? retainedProgress = null;

        try
        {
            BridgeResult result = await s_bridge.RunAsync<
                string,
                string,
                string,
                CanaryProgress>(
                    id,
                    progress => progressCallback(
                        progress.Sequence,
                        progress.Phase,
                        progress.IsFinal),
                    (token, progress) =>
                    {
                        InstallController(id, controller);
                        retainedProgress = progress;
                        Interlocked.Increment(ref s_bodyStarts);
                        IncrementModeStart(parsedMode);
                        return controller.RunAsync(token, progress);
                    },
                    static exception =>
                        new BrowserManagedOperationFailure<string, string>(
                            exception.GetType().Name,
                            exception.Message));
            OperationResultEnvelope envelope = ToEnvelope(result);
            return JsonSerializer.Serialize(
                envelope,
                CanaryJsonContext.Default.OperationResultEnvelope);
        }
        catch (BrowserManagedOperationBoundaryException exception)
        {
            RecordBoundaryFailure(exception.FailureKind);
            throw;
        }
        finally
        {
            RemoveController(id, controller);
            if (retainProgress && retainedProgress is not null)
                RetainProgress(id, retainedProgress);
        }
    }

    [JSExport]
    public static async Task<string> RunWithoutProgress(string operationId)
    {
        BridgeResult result = await s_bridge.RunAsync<
            string,
            string,
            string,
            CanaryProgress>(
                BrowserManagedOperationId.From(operationId),
                eventCallback: null,
                async (_, progress) =>
                {
                    Interlocked.Increment(ref s_withoutProgressStarts);
                    progress.Report(new CanaryProgress(1, "ignored", true));
                    await Task.Yield();
                    return new BodyResult.Succeeded("without-progress");
                },
                static exception =>
                    new BrowserManagedOperationFailure<string, string>(
                        exception.GetType().Name,
                        exception.Message));
        OperationResultEnvelope envelope = ToEnvelope(result);
        return JsonSerializer.Serialize(
            envelope,
            CanaryJsonContext.Default.OperationResultEnvelope);
    }

    [JSExport]
    public static string RequestCancellation(
        string operationId,
        string reason)
    {
        BrowserManagedCancellationRequestResult result =
            s_bridge.RequestCancellation(
                BrowserManagedOperationId.From(operationId),
                ParseCancelReason(reason));
        Interlocked.Increment(ref s_cancellationRequests);
        CancellationRequestReceipt receipt = result switch
        {
            BrowserManagedCancellationRequestResult.Requested requested =>
                new CancellationRequestReceipt(
                    CancellationRequestKind.Requested,
                    MapReason(requested.Reason)),
            BrowserManagedCancellationRequestResult.AlreadyRequested already =>
                new CancellationRequestReceipt(
                    CancellationRequestKind.AlreadyRequested,
                    MapReason(already.Reason)),
            BrowserManagedCancellationRequestResult.NotActive =>
                new CancellationRequestReceipt(
                    CancellationRequestKind.NotActive,
                    null),
            _ => throw new InvalidOperationException(
                "The bridge returned an unknown cancellation response."),
        };
        return JsonSerializer.Serialize(
            receipt,
            CanaryJsonContext.Default.CancellationRequestReceipt);
    }

    [JSExport]
    public static bool CompleteOperation(string operationId)
    {
        BrowserManagedOperationId id =
            BrowserManagedOperationId.From(operationId);
        ControlledOperation? controller;
        lock (s_sync)
            s_controllers.TryGetValue(id, out controller);
        if (controller is null || !controller.Complete())
            return false;

        Interlocked.Increment(ref s_completions);
        return true;
    }

    [JSExport]
    public static bool ReportRetainedProgress(string operationId)
    {
        BrowserManagedOperationId id =
            BrowserManagedOperationId.From(operationId);
        IBrowserManagedOperationEvents<CanaryProgress> progress;
        lock (s_sync)
        {
            if (!s_retainedProgress.Remove(id, out progress!))
            {
                throw new InvalidOperationException(
                    $"No managed progress reporter is retained for '{id}'.");
            }
        }

        Interlocked.Increment(ref s_retainedReports);
        progress.Report(new CanaryProgress(99, "after-settlement", true));
        return progress.IsClosed;
    }

    [JSExport]
    public static string VerifyBaseline()
    {
        VerificationReceipt receipt;
        lock (s_sync)
        {
            bool modesMatch =
                GetModeStarts(OperationMode.Cancel) == 7
                && GetModeStarts(OperationMode.Success) == 3
                && GetModeStarts(OperationMode.LateSuccess) == 2
                && GetModeStarts(OperationMode.ExpectedFailure) == 1
                && GetModeStarts(OperationMode.UnexpectedFailure) == 1
                && GetModeStarts(OperationMode.ForeignCancellation) == 1;
            if (!modesMatch
                || s_bodyStarts != 15
                || s_withoutProgressStarts != 1
                || s_cancellationRequests != 10
                || s_completions != 8
                || s_retainedReports != 4
                || s_duplicateBoundaryFailures != 1
                || s_progressBoundaryFailures != 1
                || s_malformedInputFailures != 3
                || s_otherBoundaryFailures != 0
                || s_controllers.Count != 0
                || s_retainedProgress.Count != 0
                || s_bridge.ActiveCount != 0)
            {
                throw new InvalidOperationException(
                    "The managed-operation bridge canary did not execute "
                    + "every baseline scenario exactly once.");
            }

            receipt = new VerificationReceipt(
                "managed-operation-bridge:baseline-ok",
                s_bodyStarts,
                s_withoutProgressStarts,
                s_cancellationRequests,
                s_completions,
                s_retainedReports,
                s_duplicateBoundaryFailures,
                s_progressBoundaryFailures,
                s_malformedInputFailures,
                s_otherBoundaryFailures);
        }

        return JsonSerializer.Serialize(
            receipt,
            CanaryJsonContext.Default.VerificationReceipt);
    }

    private static OperationResultEnvelope ToEnvelope(BridgeResult result) =>
        result switch
        {
            BridgeResult.Succeeded succeeded => new OperationResultEnvelope(
                OperationResultKind.Succeeded,
                succeeded.Value,
                null,
                null,
                null,
                null),
            BridgeResult.Failed failed => new OperationResultEnvelope(
                OperationResultKind.Failed,
                null,
                failed.FailureKind is BrowserManagedOperationFailureKind.Expected
                    ? OperationFailureKind.Expected
                    : OperationFailureKind.Unexpected,
                failed.Error,
                failed.Diagnostic,
                null),
            BridgeResult.Canceled canceled => new OperationResultEnvelope(
                OperationResultKind.Canceled,
                null,
                null,
                null,
                null,
                MapReason(canceled.Reason)),
            _ => throw new InvalidOperationException(
                "The bridge returned an unknown operation result."),
        };

    private static OperationMode ParseMode(string mode) =>
        mode switch
        {
            "cancel" => OperationMode.Cancel,
            "success" => OperationMode.Success,
            "late-success" => OperationMode.LateSuccess,
            "expected-failure" => OperationMode.ExpectedFailure,
            "unexpected-failure" => OperationMode.UnexpectedFailure,
            "foreign-cancellation" => OperationMode.ForeignCancellation,
            _ => ThrowUnknownMode(mode),
        };

    private static BrowserManagedOperationCancelReason ParseCancelReason(
        string reason) =>
        reason switch
        {
            "user" => BrowserManagedOperationCancelReason.User,
            "superseded" => BrowserManagedOperationCancelReason.Superseded,
            "disposed" => BrowserManagedOperationCancelReason.Disposed,
            "feature-observer-failed" =>
                BrowserManagedOperationCancelReason.FeatureObserverFailed,
            "timeout" => BrowserManagedOperationCancelReason.Timeout,
            "worker-restarted" =>
                BrowserManagedOperationCancelReason.WorkerRestarted,
            _ => ThrowUnknownReason(reason),
        };

    private static OperationMode ThrowUnknownMode(string mode)
    {
        Interlocked.Increment(ref s_malformedInputFailures);
        throw new ArgumentOutOfRangeException(
            nameof(mode),
            mode,
            "Unknown managed-operation canary mode.");
    }

    private static BrowserManagedOperationCancelReason ThrowUnknownReason(
        string reason)
    {
        Interlocked.Increment(ref s_malformedInputFailures);
        throw new ArgumentOutOfRangeException(
            nameof(reason),
            reason,
            "Unknown managed-operation cancellation reason.");
    }

    private static OperationCancelReason MapReason(
        BrowserManagedOperationCancelReason reason) =>
        reason switch
        {
            BrowserManagedOperationCancelReason.User =>
                OperationCancelReason.User,
            BrowserManagedOperationCancelReason.Superseded =>
                OperationCancelReason.Superseded,
            BrowserManagedOperationCancelReason.Disposed =>
                OperationCancelReason.Disposed,
            BrowserManagedOperationCancelReason.FeatureObserverFailed =>
                OperationCancelReason.FeatureObserverFailed,
            BrowserManagedOperationCancelReason.Timeout =>
                OperationCancelReason.Timeout,
            BrowserManagedOperationCancelReason.WorkerRestarted =>
                OperationCancelReason.WorkerRestarted,
            _ => throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "Unknown bridge cancellation reason."),
        };

    private static void InstallController(
        BrowserManagedOperationId id,
        ControlledOperation controller)
    {
        lock (s_sync)
        {
            if (!s_controllers.TryAdd(id, controller))
            {
                throw new InvalidOperationException(
                    $"Canary controller '{id}' is already active.");
            }

            controller.MarkInstalled();
        }
    }

    private static void RemoveController(
        BrowserManagedOperationId id,
        ControlledOperation controller)
    {
        if (!controller.IsInstalled)
            return;

        lock (s_sync)
        {
            if (!s_controllers.TryGetValue(id, out ControlledOperation? active)
                || !ReferenceEquals(active, controller)
                || !s_controllers.Remove(id))
            {
                throw new InvalidOperationException(
                    $"Canary controller '{id}' no longer owns its entry.");
            }
        }
    }

    private static void IncrementModeStart(OperationMode mode)
    {
        lock (s_sync)
            s_modeStarts[mode] = GetModeStarts(mode) + 1;
    }

    private static int GetModeStarts(OperationMode mode) =>
        s_modeStarts.GetValueOrDefault(mode);

    private static void RetainProgress(
        BrowserManagedOperationId id,
        IBrowserManagedOperationEvents<CanaryProgress> progress)
    {
        lock (s_sync)
        {
            if (!s_retainedProgress.TryAdd(id, progress))
            {
                throw new InvalidOperationException(
                    $"Managed progress reporter '{id}' is already retained.");
            }
        }
    }

    private static void RecordBoundaryFailure(string failureKind)
    {
        if (failureKind == "duplicate-active-operation")
            Interlocked.Increment(ref s_duplicateBoundaryFailures);
        else if (failureKind == "event-callback")
            Interlocked.Increment(ref s_progressBoundaryFailures);
        else
            Interlocked.Increment(ref s_otherBoundaryFailures);
    }

    private sealed class ControlledOperation(OperationMode mode)
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _completed;
        private int _installed;

        internal bool IsInstalled => Volatile.Read(ref _installed) != 0;

        internal void MarkInstalled() =>
            Volatile.Write(ref _installed, 1);

        internal bool Complete() =>
            Interlocked.Exchange(ref _completed, 1) == 0
            && _release.TrySetResult();

        internal async Task<BodyResult> RunAsync(
            CancellationToken token,
            IBrowserManagedOperationEvents<CanaryProgress> progress)
        {
            progress.Report(new CanaryProgress(1, "started", false));

            if (mode is OperationMode.Cancel)
            {
                await _release.Task.WaitAsync(token);
            }
            else
            {
                await _release.Task;
            }

            return mode switch
            {
                OperationMode.Success or OperationMode.LateSuccess =>
                    CompleteSuccessfully(progress),
                OperationMode.ExpectedFailure =>
                    new BodyResult.Failed(
                        "expected-canary-failure",
                        "The controlled feature reported an expected failure."),
                OperationMode.UnexpectedFailure =>
                    throw new InvalidOperationException(
                        "The controlled feature failed unexpectedly."),
                OperationMode.ForeignCancellation =>
                    throw new OperationCanceledException(
                        "The controlled feature supplied no accepted reason."),
                _ => throw new InvalidOperationException(
                    "The controlled feature reached an unsupported mode."),
            };
        }

        private static BodyResult CompleteSuccessfully(
            IBrowserManagedOperationEvents<CanaryProgress> progress)
        {
            progress.Report(new CanaryProgress(2, "completed", true));
            return new BodyResult.Succeeded("controlled-success");
        }
    }

    private readonly record struct CanaryProgress(
        int Sequence,
        string Phase,
        bool IsFinal);

    private enum OperationMode
    {
        Cancel,
        Success,
        LateSuccess,
        ExpectedFailure,
        UnexpectedFailure,
        ForeignCancellation,
    }
}
